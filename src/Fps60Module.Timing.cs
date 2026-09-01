namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Retiming of the systems that express a duration in frames. Each of them takes a frame count or
///     a speed as an argument, so the correction is a scale applied on the way in.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    /// <summary>
    ///     How much longer a frame-expressed duration must be at the current framerate.
    ///
    ///     One is the answer while the engine is pacing itself from the syncdata table. Sg_MainCalcRate
    ///     takes sg_rate from g_sgSyncRate whenever g_isNeedSync is set, and the catch-up loop then
    ///     holds the simulation to the recorded PS2 frame times - the scene already runs at its
    ///     authored rate, and correcting it a second time is what halves it.
    /// </summary>
    private static float Scale => IsSyncPaced ? 1f : TargetFramerate / 30f;

    private static bool IsSyncPaced
        => _sync_aware && FhUtil.get_at<uint>(EngineAddresses.IsNeedSync) == 1;

    // Static because Scale is, and Scale is read from static helpers on hot paths.
    private static bool _sync_aware = true;

    /// <summary>Fractional part of the corrected vertical blank delta, carried across frames.</summary>
    private double _vblank_carry;

    /// <summary>
    ///     Frame counts are stored in 16 bits by every consumer this module scales for - the camera
    ///     track record, the fade, flash and alpha slots. Both overloads clamp, because an unclamped
    ///     scale that wrapped would make a long duration a short one rather than a wrong one.
    /// </summary>
    private static ushort scale_up(ushort frames) => (ushort)Math.Min(short.MaxValue, (int)(frames * Scale));
    private static uint scale_up(uint frames) => (uint)Math.Min(short.MaxValue, (long)(frames * Scale));
    /// <summary>
    ///     Halves a speed, never to zero. A motion speed of 0 does not advance at all, so rounding a
    ///     small non-zero speed away stalls whatever waits for that motion to finish - which is how
    ///     a cutscene ends up hanging rather than merely running at the wrong rate.
    /// </summary>
    private static ushort scale_down(ushort speed)
        => speed == 0 ? (ushort)0 : (ushort)Math.Max(1, (int)(speed / Scale));

    /// <summary>Actor flags. Bit 0x100000 marks an actor whose motion advance the engine scales itself.</summary>
    private const int ActorFlagsOffset = 0x194;
    private const uint ActorFlagEngineScaledMotion = 0x100000;

    /// <summary>
    ///     True when the engine applies sg_rate to this actor's motion advance on its own, in which
    ///     case scaling the speed on the way in would correct it twice.
    ///
    ///     The advance at 0x00838d10 guards that multiplication with both KEEP_FPS and the actor's
    ///     own flag 0x100000, which reads as "an actor without the flag is never corrected, so the
    ///     module must correct it". Measured 2026-08-30, acting on that made things worse: a cutscene
    ///     ran at half speed and then stalled outright. Something else corrects those actors too, and
    ///     until that is found the decision stays on KEEP_FPS alone. <see cref="Fps60Config.MotionPerActor"/>
    ///     turns the per-actor rule back on for the next attempt.
    /// </summary>
    private bool engine_corrects_motion(nint ptr_actor)
    {
        if (KeepFps == 0) return false;
        if (!_config.MotionPerActor) return true;

        return ptr_actor != 0
            && (*(uint*)(ptr_actor + ActorFlagsOffset) & ActorFlagEngineScaledMotion) != 0;
    }

    private bool init_timing_hooks()
    {
        bool ok = true;

        // Not optional: with the counters left alone every animation runs at double speed,
        // because the engine derives its animation rate from their delta.
        ok &= hook_or_log("vertical blank counters", EngineAddresses.AdvanceVBlankCounters,
            () => new FhMethodHandle<d_advance_vblank>(new FhMethodLocation(EngineAddresses.AdvanceVBlankCounters, 0)).hook(this, h_advance_vblank));

        if (_config.EventClockOnVblank)
        {
            ok &= hook_or_log("yiAnimInfo_init", EngineAddresses.YiAnimInfoInit,
                () => new FhMethodHandle<d_yi_anim_info_init>(new FhMethodLocation(EngineAddresses.YiAnimInfoInit, 0)).hook(this, h_yi_anim_info_init));
        }

        if (_config.CharacterDelta)
        {
            ok &= hook_or_log("Ch_CalcMain", EngineAddresses.ChCalcMain,
                () => new FhMethodHandle<d_ch_calc_main>(new FhMethodLocation(EngineAddresses.ChCalcMain, 0)).hook(this, h_ch_calc_main));
        }

        if (_config.AtelWaits)
        {
            ok &= hook_or_log("ATEL wait init", EngineAddresses.AtelWaitInit,
                () => new FhMethodHandle<d_atel_wait_init>(new FhMethodLocation(EngineAddresses.AtelWaitInit, 0)).hook(this, h_atel_wait_init));
        }

        if (_config.Camera)
        {
            ok &= hook_or_log("MsCameraMoveFrame", EngineAddresses.MsCameraMoveFrame,
                () => new FhMethodHandle<d_camera_move_frame>(new FhMethodLocation(EngineAddresses.MsCameraMoveFrame, 0)).hook(this, h_camera_move_frame));
            ok &= hook_or_log("MsCameraMoveAcc", EngineAddresses.MsCameraMoveAcc,
                () => new FhMethodHandle<d_camera_move_acc>(new FhMethodLocation(EngineAddresses.MsCameraMoveAcc, 0)).hook(this, h_camera_move_acc));
        }

        if (_config.Fades)
        {
            ok &= hook_or_log("Sg_Fade_Common", EngineAddresses.SgFadeCommon,
                () => new FhMethodHandle<d_fade_common>(new FhMethodLocation(EngineAddresses.SgFadeCommon, 0)).hook(this, h_fade_common));
            ok &= hook_or_log("Sg_Flash", EngineAddresses.SgFlash,
                () => new FhMethodHandle<d_sg_flash>(new FhMethodLocation(EngineAddresses.SgFlash, 0)).hook(this, h_sg_flash));
            ok &= hook_or_log("Sg_AccSetAlpha", EngineAddresses.SgAccSetAlpha,
                () => new FhMethodHandle<d_acc_set_alpha>(new FhMethodLocation(EngineAddresses.SgAccSetAlpha, 0)).hook(this, h_acc_set_alpha));
            ok &= hook_or_log("TkSetFadeOut", EngineAddresses.TkSetFadeOut,
                () => new FhMethodHandle<d_tk_set_fade_out>(new FhMethodLocation(EngineAddresses.TkSetFadeOut, 0)).hook(this, h_tk_set_fade_out));
        }

        if (_config.Motion)
        {
            ok &= hook_or_log("Ch_SetMotionSpeed", EngineAddresses.ChSetMotionSpeed,
                () => new FhMethodHandle<d_set_motion_speed>(new FhMethodLocation(EngineAddresses.ChSetMotionSpeed, 0)).hook(this, h_set_motion_speed));
            ok &= hook_or_log("MsEffectSetSpeed", EngineAddresses.MsEffectSetSpeed,
                () => new FhMethodHandle<d_effect_set_speed>(new FhMethodLocation(EngineAddresses.MsEffectSetSpeed, 0)).hook(this, h_effect_set_speed));
            ok &= hook_or_log("MsSetChrMotionParamF", EngineAddresses.SetMotionParamFloat,
                () => new FhMethodHandle<d_set_motion_param>(new FhMethodLocation(EngineAddresses.SetMotionParamFloat, 0)).hook(this, h_set_motion_param));
            ok &= hook_or_log("Ch_TextureSetAnimTimer", EngineAddresses.ChTextureSetAnimTimer,
                () => new FhMethodHandle<d_set_tex_anim_timer>(new FhMethodLocation(EngineAddresses.ChTextureSetAnimTimer, 0)).hook(this, h_set_tex_anim_timer));
            ok &= hook_or_log("MsSetChrStatInfo", EngineAddresses.MsSetChrStatInfo,
                () => new FhMethodHandle<d_set_chr_stat>(new FhMethodLocation(EngineAddresses.MsSetChrStatInfo, 0)).hook(this, h_set_chr_stat));
        }

        return ok;
    }

    // --- Delegates ---

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_advance_vblank(float arg1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_ch_calc_main(float delta);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_atel_wait_init(nint work, int* storage, nint stack);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int d_atel_pop_int(nint work, nint stack);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_camera_move_frame(uint camera_id, uint arg2, uint arg3, uint frames, uint arg5);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_camera_move_acc(uint camera_id, uint mode_non_ref, uint mode_polar, uint a4, uint a5, uint a6, uint a7);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_fade_common(ushort frames, uint mode_in, uint mode_w);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_sg_flash(ushort frames, byte arg2, byte arg3, byte arg4);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_acc_set_alpha(ushort alpha, ushort frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_tk_set_fade_out(uint frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_set_motion_speed(nint ptr_actor, ushort speed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_effect_set_speed(byte chr_id, int speed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_set_motion_param(uint chr_id, uint stat_id, float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_set_chr_stat(uint chr_id, uint stat_id, uint target_id, uint value);

    /* Cdecl, measured rather than taken from the catalog: Ch_TextureSetAnimTimer ends in a plain c3,
     * so the caller cleans. Declaring it StdCall made the detour pop eight bytes the four call sites
     * pop as well, which drifts the stack on every call and faults far from the cause. */
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_set_tex_anim_timer(nint ptr_chr, int timer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_yi_anim_info_init(nint info);

    // --- Handlers ---

    /* yiAnimInfo_init reads KEEP_FPS once and stores it as the clock the event worker's animation
     * curves run on: zero means yiGetFCount, which is sg_count * 2 and gains two units per presented
     * frame with nothing correcting it, non-zero means yiGetVCount, which h_advance_vblank rescales.
     *
     * KEEP_FPS is observed toggling during play, so which clock a scene gets is otherwise decided by
     * whatever it happened to be at init. Writing the mode after the original has run puts every
     * scene on the counter that is corrected. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_yi_anim_info_init(nint info)
    {
        new FhMethodHandle<d_yi_anim_info_init>(new FhMethodLocation(EngineAddresses.YiAnimInfoInit, 0))
            .chain_from(h_yi_anim_info_init).fnptr?.Invoke(info);

        FhUtil.set_at(EngineAddresses.YiAnimClockMode, 1u);
        _logger.Info("[Fps60] Event worker clock forced to the vertical blank counter.");
    }

    /* The engine advances both vertical blank counters by a hardcoded 2 per frame, its assumption of
     * two vertical blanks per 30 Hz frame. Sg_MainCalcRate turns their delta into the animation rate
     * (delta 2 -> sg_ratef 1.0), so at 60 Hz the rate stays at 1.0 and every animation advances a
     * full 30 Hz step twice as often.
     *
     * The increment sits behind menu-loop and screen-state conditions, so the delta is measured
     * rather than assumed: whatever the original added is rescaled to the actual framerate. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_advance_vblank(float arg1)
    {
        uint before  = FhUtil.get_at<uint>(EngineAddresses.SgVCount);
        uint before2 = FhUtil.get_at<uint>(EngineAddresses.SgVCount2);

        new FhMethodHandle<d_advance_vblank>(new FhMethodLocation(EngineAddresses.AdvanceVBlankCounters, 0))
            .chain_from(h_advance_vblank).fnptr!(arg1);

        uint delta = FhUtil.get_at<uint>(EngineAddresses.SgVCount) - before;
        if (delta == 0) return;

        // The counters are integers and the corrected delta is not: at 60 Hz it is 1, at 120 Hz it
        // is 0.5, and rounding 0.5 up to 1 would leave every animation running at double speed with
        // no way to tell. The remainder is carried instead, so the counter advances 1, 0, 1, 0 and
        // the rate averages out exactly. That is what makes this module's correction independent of
        // the target framerate rather than tied to twice 30.
        _vblank_carry += delta / Scale;

        uint scaled = (uint)_vblank_carry;
        _vblank_carry -= scaled;

        FhUtil.set_at(EngineAddresses.SgVCount,  before  + scaled);
        FhUtil.set_at(EngineAddresses.SgVCount2, before2 + scaled);
    }

    /* The engine always passes a fixed 0.033373334, one 30 Hz frame. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_ch_calc_main(float delta)
    {
        new FhMethodHandle<d_ch_calc_main>(new FhMethodLocation(EngineAddresses.ChCalcMain, 0))
            .chain_from(h_ch_calc_main).fnptr!(1f / TargetFramerate);
    }

    /* ATEL call target 0000, the frame-based wait. A wait of one frame is an idle loop rather than a
     * duration, so scaling it would change control flow instead of timing. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_atel_wait_init(nint work, int* storage, nint stack)
    {
        int frames = new FhMethodHandle<d_atel_pop_int>(new FhMethodLocation(EngineAddresses.AtelPopStackInteger, 0))
            .fnptr!(work, stack);

        *storage = frames != 1 ? (int)(frames * Scale) : frames;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_camera_move_frame(uint camera_id, uint arg2, uint arg3, uint frames, uint arg5)
    {
        new FhMethodHandle<d_camera_move_frame>(new FhMethodLocation(EngineAddresses.MsCameraMoveFrame, 0))
            .chain_from(h_camera_move_frame).fnptr!(camera_id, arg2, arg3, scale_up(frames), arg5);
    }

    /* Whether all four trailing arguments are durations is not established; PWarp scales all four and
     * the result is reported as correct, so this follows it until something disagrees. */
    private long _camera_acc_calls;

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_camera_move_acc(uint camera_id, uint mode_non_ref, uint mode_polar, uint a4, uint a5, uint a6, uint a7)
    {
        _camera_acc_calls++;

        // The encounter scripts call camMoveAcc(0, 0, 30, 15) in 540 of 641 CamEnter entry points,
        // so a logged call with those arguments unscaled says the hook fires and the scaling does
        // not, and no call at all says the hook is not on the path. Nothing else distinguishes the
        // two, and the battle entry camera is the one place a player notices.
        if (_config.SurveyCamera && _camera_acc_calls <= 8)
            _logger.Info($"[Fps60] MsCameraMoveAcc #{_camera_acc_calls}: cam={camera_id} p2={mode_non_ref} " +
                         $"p3={mode_polar} in={a4},{a5},{a6},{a7} -> out={scale_up(a4)},{scale_up(a5)}," +
                         $"{scale_up(a6)},{scale_up(a7)} (scale={Scale:F2})");

        new FhMethodHandle<d_camera_move_acc>(new FhMethodLocation(EngineAddresses.MsCameraMoveAcc, 0))
            .chain_from(h_camera_move_acc).fnptr!(camera_id, mode_non_ref, mode_polar,
                scale_up(a4), scale_up(a5), scale_up(a6), scale_up(a7));
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_fade_common(ushort frames, uint mode_in, uint mode_w)
    {
        new FhMethodHandle<d_fade_common>(new FhMethodLocation(EngineAddresses.SgFadeCommon, 0))
            .chain_from(h_fade_common).fnptr!(scale_up(frames), mode_in, mode_w);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_sg_flash(ushort frames, byte arg2, byte arg3, byte arg4)
    {
        new FhMethodHandle<d_sg_flash>(new FhMethodLocation(EngineAddresses.SgFlash, 0))
            .chain_from(h_sg_flash).fnptr!(scale_up(frames), arg2, arg3, arg4);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_acc_set_alpha(ushort alpha, ushort frames)
    {
        new FhMethodHandle<d_acc_set_alpha>(new FhMethodLocation(EngineAddresses.SgAccSetAlpha, 0))
            .chain_from(h_acc_set_alpha).fnptr!(alpha, scale_up(frames));
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_tk_set_fade_out(uint frames)
    {
        new FhMethodHandle<d_tk_set_fade_out>(new FhMethodLocation(EngineAddresses.TkSetFadeOut, 0))
            .chain_from(h_tk_set_fade_out).fnptr!(scale_up(frames));
    }

    /* Animation speed follows motion speed unless keep-FPS is asserted, in which case the engine
     * already retimes animations off sg_rate and scaling here would slow them twice.
     *
     * The engine can also assert keep-FPS per actor, and provides no getter for that state, so this
     * prototype only honours the global flag. Actors that carry the per-actor flag while the global
     * one is clear are still retimed and will run at half speed. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    /* KEEP_FPS alone does not mean the engine corrects the actor. The motion advance computes
     * (speed * factor >> 8) * scale / 0x1e00 and only then, guarded by BOTH Sg_GetKeepFps() and the
     * actor's own flag 0x100000 at +0x194, multiplies by sg_rate. An actor without that flag is
     * never corrected by the engine, so skipping the scale on the global flag alone left it running
     * at double speed - which is what put the field NPCs out of step while Tidus was right. */
    private void h_set_motion_speed(nint ptr_actor, ushort speed)
    {
        if (!engine_corrects_motion(ptr_actor)) speed = scale_down(speed);

        new FhMethodHandle<d_set_motion_speed>(new FhMethodLocation(EngineAddresses.ChSetMotionSpeed, 0))
            .chain_from(h_set_motion_speed).fnptr!(ptr_actor, speed);
    }

    /* A negative speed is not a speed. MsEffectSetSpeed treats it as "drop the override": it clears
     * the actor's effect speed field and hands the decision back to MsCalcMotionSpeed. Reading the
     * argument as unsigned turned that sentinel into 0x7FFF, so a reset became the fastest effect
     * speed the field can hold. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_effect_set_speed(byte chr_id, int speed)
    {
        if (speed >= 0) speed = scale_down((ushort)Math.Min(ushort.MaxValue, speed));

        new FhMethodHandle<d_effect_set_speed>(new FhMethodLocation(EngineAddresses.MsEffectSetSpeed, 0))
            .chain_from(h_effect_set_speed).fnptr!(chr_id, speed);
    }

    /* Only the four run-speed properties are frame-expressed. Ids 0, 1, 2, 7 and 8 are distances,
     * offsets and a weight; scaling those would move characters, not retime them.
     *
     * Id 6 is not a speed but an acceleration, and it therefore scales with the square. The consumer
     * integrates it once per frame - v += motion_run_speed_acc, then pos -= v, starting from
     * motion_run_speed_v0 (id 5). Under t -> 2t a per-frame velocity halves and a per-frame velocity
     * increment quarters; treating both alike left the acceleration twice as strong as the rest of
     * the motion, which reads as a character that starts correctly and then overshoots. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_set_motion_param(uint chr_id, uint stat_id, float value)
    {
        float scaled = stat_id switch
        {
            3 or 4 or 5 => value / Scale,
            6           => value / (Scale * Scale),
            _           => value
        };

        new FhMethodHandle<d_set_motion_param>(new FhMethodLocation(EngineAddresses.SetMotionParamFloat, 0))
            .chain_from(h_set_motion_param).fnptr!(chr_id, stat_id, scaled);
    }

    /* Passes every stat through unchanged. The hook stays because the address and the reasoning are
     * worth keeping in one place, and re-enabling a correction here is one line.
     *
     * Both corrections this used to make were wrong, and for opposite reasons.
     *
     * STAT_ATTACK_INC_SPEED and _DEC_SPEED are not rates. The engine's own debug dump names them
     * 攻撃：加速％ and 減速％ - percentages, stored as single bytes at Chr+0x4B9 and +0x4BA, and no
     * function in the image reads either one.
     *
     * The three frame-count stats are durations, but scaling them alone makes characters travel
     * further rather than slower. Chr+0x40C is the iteration count of a per-frame integration whose
     * result is a distance: the loop adds Chr+0x468 into a velocity and the velocity into a
     * position, and those three rates arrive as ATEL float arguments that nothing here scales.
     * Doubling only the loop count doubles the distance walked. They are also bytes, so a scaled
     * value above 127 wrapped. The correction belongs at the ATEL opcode that sets the rates, with
     * the frame counts, or nowhere. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_set_chr_stat(uint chr_id, uint stat_id, uint target_id, uint value)
    {
        new FhMethodHandle<d_set_chr_stat>(new FhMethodLocation(EngineAddresses.MsSetChrStatInfo, 0))
            .chain_from(h_set_chr_stat).fnptr!(chr_id, stat_id, target_id, value);
    }

    /* Passes the value through. It is not a period.
     *
     * The byte this writes is the step the new-format advance adds to its sequence accumulator each
     * call - the accumulator is then compared against the per-sprite duration, so the period lives
     * in the asset and the timer is the rate. Scaling it up therefore doubled a rate that was
     * already running twice too fast, which is 4x rather than a correction. Its only two engine
     * writers confirm the reading: MsCalcMotionSpeed writes the literals 1 and 0 on the zero
     * crossing of the motion speed, so it is pause and resume, not a duration.
     *
     * The correction the byte can express is a hold, not a factor - write 0 on a skipped frame and
     * restore the engine's own last value on an advancing one. That needs per-slot state this hook
     * does not have yet, and until it does, running vanilla is the correct behaviour rather than a
     * placeholder. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_set_tex_anim_timer(nint ptr_chr, int timer)
    {
        new FhMethodHandle<d_set_tex_anim_timer>(new FhMethodLocation(EngineAddresses.ChTextureSetAnimTimer, 0))
            .chain_from(h_set_tex_anim_timer).fnptr!(ptr_chr, timer);
    }
}
