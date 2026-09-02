namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Battle and event effects - the magic, the weapon elements, the cutscene set pieces.
///
///     They do not live in FFX.exe. MsEffectProcess dispatches through the magic overlay's own
///     table, +0xc to advance and +0x10 to draw, so the effect's timeline is inside the DLL and no
///     amount of retiming in the executable reaches it.
///
///     What the executable does control is how often the advance is called, and there the engine is
///     unconditional: the simulation loop runs MsEffectProcess(0) on every iteration, and the
///     branch below it runs MsEffectProcess(0) once more when the frame ran no simulation step at
///     all. Either way the advance happens once per presented frame, which at 60 Hz is twice the
///     authored rate.
///
///     Holding the advance is therefore the whole fix, and unlike the particle experiment it cannot
///     flicker: mode 1 is never touched, so every frame still draws.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const int EffectAdvanceMode = 0;

    private long _effect_advances;
    private long _effect_draws;
    private long _effect_held;

    private string effect_counts()
        => $"eff_adv={_effect_advances} eff_draw={_effect_draws} eff_held={_effect_held} " +
           $"eternal={_eternal_calls}/{_eternal_held}";

    private long _eternal_calls;
    private long _eternal_held;

    private bool init_effect_hooks()
    {
        if (!_config.EffectHold && !_config.EffectProbe) return true;

        bool ok = hook_or_log("MsEffectProcess", EngineAddresses.MsEffectProcess,
            () => new FhMethodHandle<d_effect_process>(new FhMethodLocation(EngineAddresses.MsEffectProcess, 0))
                .hook(this, h_effect_process));

        // The eternal set's second list has to be held with the first or not at all. Holding one of
        // the two retimes half its population, which is the defect this module has already shipped
        // three times.
        if (_config.EffectHold && _config.EternalEffectHold)
        {
            ok &= hook_or_log("eternal effect run_after", EngineAddresses.EternalEffectRunAfter,
                () => new FhMethodHandle<d_eternal_run>(new FhMethodLocation(EngineAddresses.EternalEffectRunAfter, 0))
                    .hook(this, h_eternal_run_after));
        }

        return ok;
    }

    /* Cdecl, not StdCall, and the binary is what says so: every ret in this function is a plain
     * c3, so the caller cleans the argument. The catalog calls it __stdcall, but that label is a
     * blanket default there, carried by 57,699 of 58,806 functions and predicting nothing - a scan
     * of every function's last instruction finds 16,025 that really do end in ret imm16 and 33,784
     * that do not. A StdCall delegate made the detour pop the argument the caller also pops, which
     * drifted the stack and killed the boot before the first splash. */
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_effect_process(int mode);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_effect_process(int mode)
    {
        if (mode == EffectAdvanceMode) _effect_advances++;
        else                           _effect_draws++;

        if (_config.EffectHold && mode == EffectAdvanceMode && !advance_this_frame()
            && (_config.EffectHoldInBattle || hold_is_inert_below()))
        {
            _effect_held++;
            return;
        }

        new FhMethodHandle<d_effect_process>(new FhMethodLocation(EngineAddresses.MsEffectProcess, 0))
            .chain_from(h_effect_process).fnptr?.Invoke(mode);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_eternal_run();

    /* The eternal set's second object list. Its two entry points are not an advance and a draw but
     * two lists, each run by the same worker and each decrementing its own per-channel wait bytes,
     * so the first is already held by the mode 0 hold above and this is the other half. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_eternal_run_after()
    {
        _eternal_calls++;

        if (_config.EffectHold && !advance_this_frame()) { _eternal_held++; return; }

        new FhMethodHandle<d_eternal_run>(new FhMethodLocation(EngineAddresses.EternalEffectRunAfter, 0))
            .chain_from(h_eternal_run_after).fnptr?.Invoke();
    }

    /// <summary>
    ///     True while there is no live effect overlay at all, in battle or out of it.
    ///
    ///     Skipping mode 0 never skips a draw, and that is a property of the function rather than of
    ///     where it is called from. MsEffectProcess discriminates on the mode at all three of its
    ///     dispatch sites, not only in its head: the eternal table's +0xC against its +0x10, then the
    ///     same pair per battle actor whose Chr+0xdfb is 4 or 5, then the same pair for the non-actor
    ///     overlay. Every path to an overlay's +0x10 is behind mode == 1. What runs on both modes is
    ///     MsGetChrTop, MsGetChr, FlushCache, MagicFile_GetEffectOverlayTable and the
    ///     ms_effect_from_magicfile flag, which is raised and cleared inside the same call - reads
    ///     and a flag with no life beyond the call, so a skipped call leaves nothing behind.
    ///
    ///     So this is not the safety condition it was written as. It is a narrower one kept as a
    ///     measurement lever: with an overlay live, the held advance is what makes battle effects run
    ///     at the authored rate, and it is also what leaves the roughly a third of overlays that read
    ///     Sg_GetCurExecFrames inside their draw internally inconsistent. Turning the hold off where
    ///     no overlay is live at all costs nothing either way, which is what makes it a usable
    ///     baseline. <see cref="Fps60Config.EffectHoldInBattle"/> is what decides whether it applies.
    /// </summary>
    private static bool hold_is_inert_below()
    {
        if (FhUtil.get_at<nint>(EngineAddresses.PlayerChrs) != 0) return false;

        byte overlay_state = FhUtil.get_at<byte>(EngineAddresses.GlobalEffectOverlayState);
        return overlay_state is not (4 or 5);
    }
}
