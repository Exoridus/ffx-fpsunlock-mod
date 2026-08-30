namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Particle retiming. Every particle manager keeps a fixed-point time accumulator and advances
///     it by a per-manager step each frame, so unlike the frame-sequence systems particles carry a
///     rate that can be scaled rather than only held.
///
///     Two writers set that step and both have to be caught.
///
///     <c>_pppStartPart</c> writes its second argument into the manager's +0x10 when the manager
///     starts. Scaling the argument there covers every source of an initial step, including the
///     literal 0x1000 the magic path passes, which a write to the global would never reach.
///
///     <c>pppDataRcv</c>, which <c>pppPartLoop</c> calls at its end, broadcasts the global step into
///     every active manager's +0x10 again. That is the PS2 path: measured 2026-08-30 with the loop
///     hook attached, part_rescaled stayed 0 and the global step read 0, so the broadcast does not
///     run on PC and the start scaling is never undone.
///
///     Which leaves the step scaling unable to fix the speed at all. <c>_pppRunPart</c> calls
///     <c>pppRunPartStd</c> before, and independently of, the accumulator update, so the
///     instruction pass runs once per call: particle motion follows call frequency, and the step
///     only stretches the lifetime timeline. <see cref="Fps60Config.ParticleHold"/> is the
///     experiment that addresses the frequency instead.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const int ParticleManagerStride = 0x80;
    private const int ParticleStepOffset    = 0x10;

    private long _part_starts;
    private long _part_held;
    private long _part_rescaled;
    private int  _part_step_observed;

    private bool init_particle_hooks()
    {
        if (!_config.Particles && !_config.ParticleHold) return true;

        bool ok = hook_or_log("_pppStartPart", EngineAddresses.PppStartPart,
            () => new FhMethodHandle<d_ppp_start_part>(new FhMethodLocation(EngineAddresses.PppStartPart, 0)).hook(this, h_ppp_start_part));

        ok &= hook_or_log("pppPartLoop", EngineAddresses.PppPartLoop,
            () => new FhMethodHandle<d_ppp_part_loop>(new FhMethodLocation(EngineAddresses.PppPartLoop, 0)).hook(this, h_ppp_part_loop));

        return ok;
    }

    private string particle_counts()
        => $"part_starts={_part_starts} part_rescaled={_part_rescaled} part_held={_part_held} " +
           $"part_step=0x{_part_step_observed:X}";

    /* The hold and the step scaling are alternatives, not layers. Holding halves how often the
     * pass runs; scaling the step on top of that would stretch every lifetime to twice its wall
     * clock length. */
    private int scale_particle_step(int step)
        // A step of zero is a manager that is not meant to advance; scaling it is meaningless, and
        // rounding a small step to zero would freeze the effect outright.
        => _config.ParticleHold || step <= 0 ? step : Math.Max(1, (int)Math.Round(step / Scale));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_ppp_start_part(nint manager, int time_step, nint data, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_ppp_part_loop();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_ppp_start_part(nint manager, int time_step, nint data, int flags)
    {
        _part_starts++;
        _part_step_observed = time_step;

        new FhMethodHandle<d_ppp_start_part>(new FhMethodLocation(EngineAddresses.PppStartPart, 0))
            .chain_from(h_ppp_start_part).fnptr?.Invoke(manager, scale_particle_step(time_step), data, flags);
    }

    /* The broadcast runs inside this call, so the correction has to come after it. Only a step that
     * still equals the global is rewritten: that makes the pass idempotent (a step this module
     * already scaled no longer matches) and leaves the magic path's own steps alone. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_ppp_part_loop()
    {
        /* The hold skips the pass outright rather than shortening it, because the pass advances
         * particles once per call whatever its accumulator says. Skipping the draw with it is the
         * known risk, and the counter is what tells the two apart afterwards. */
        if (_config.ParticleHold && !advance_this_frame())
        {
            _part_held++;
            return;
        }

        new FhMethodHandle<d_ppp_part_loop>(new FhMethodLocation(EngineAddresses.PppPartLoop, 0))
            .chain_from(h_ppp_part_loop).fnptr?.Invoke();

        int global_step = FhUtil.get_at<int>(EngineAddresses.PpvPartTimeStep);
        if (global_step <= 0) return;

        int scaled = scale_particle_step(global_step);
        if (scaled == global_step) return;

        int count = FhUtil.get_at<int>(EngineAddresses.PpvPartManagerCount);
        byte* managers = FhUtil.ptr_at<byte>(EngineAddresses.PpvPartManagers);

        for (int i = 0; i < count; i++)
        {
            int* step = (int*)(managers + i * ParticleManagerStride + ParticleStepOffset);
            if (*step != global_step) continue;

            *step = scaled;
            _part_rescaled++;
        }
    }
}
