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
///     every active manager's +0x10 again. Measured 2026-08-30: with only the start hook in place
///     particles still ran at double speed, so the broadcast does run on PC and it undoes the start
///     scaling on every manager that lives longer than a frame.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const int ParticleManagerStride = 0x80;
    private const int ParticleStepOffset    = 0x10;

    private long _part_starts;
    private long _part_rescaled;
    private int  _part_step_observed;

    private bool init_particle_hooks()
    {
        if (!_config.Particles) return true;

        bool ok = hook_or_log("_pppStartPart", EngineAddresses.PppStartPart,
            () => new FhMethodHandle<d_ppp_start_part>(new FhMethodLocation(EngineAddresses.PppStartPart, 0)).hook(this, h_ppp_start_part));

        ok &= hook_or_log("pppPartLoop", EngineAddresses.PppPartLoop,
            () => new FhMethodHandle<d_ppp_part_loop>(new FhMethodLocation(EngineAddresses.PppPartLoop, 0)).hook(this, h_ppp_part_loop));

        return ok;
    }

    private string particle_counts()
        => $"part_starts={_part_starts} part_rescaled={_part_rescaled} part_step=0x{_part_step_observed:X}";

    private static int scale_particle_step(int step)
        // A step of zero is a manager that is not meant to advance; scaling it is meaningless, and
        // rounding a small step to zero would freeze the effect outright.
        => step > 0 ? Math.Max(1, (int)Math.Round(step / Scale)) : step;

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
