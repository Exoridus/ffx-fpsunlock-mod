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
    private long _part_runs;
    private long _fp_runs;
    private long _fp_scaled;
    private readonly Dictionary<nint, (long Frame, long Ticks)> _fp_started = [];
    private readonly Queue<double> _fp_lifetimes = new();
    private readonly Queue<long> _fp_life_frames = new();
    private readonly Dictionary<nint, int> _fp_steps = [];
    private long _fp_held;
    private long _part_loops;
    private long _part_rescaled;
    private int  _part_step_observed;

    private bool init_particle_hooks()
    {
        // The manager step is one half of the timeline correction, so the start hook is needed
        // whenever that is on, not only when the old standalone step scaling is.
        if (!_config.Particles && !_config.ParticleHold && !_config.FieldParticleHold
            && !_config.ParticleTimeline) return true;

        bool ok = hook_or_log("_pppStartPart", EngineAddresses.PppStartPart,
            () => new FhMethodHandle<d_ppp_start_part>(new FhMethodLocation(EngineAddresses.PppStartPart, 0)).hook(this, h_ppp_start_part));

        ok &= hook_or_log("pppPartLoop", EngineAddresses.PppPartLoop,
            () => new FhMethodHandle<d_ppp_part_loop>(new FhMethodLocation(EngineAddresses.PppPartLoop, 0)).hook(this, h_ppp_part_loop));

        {
            ok &= hook_or_log("_pppRunPartFp", EngineAddresses.PppRunPartFp,
                () => new FhMethodHandle<d_ppp_run_part_fp>(new FhMethodLocation(EngineAddresses.PppRunPartFp, 0)).hook(this, h_ppp_run_part_fp));

            ok &= hook_or_log("_pppRunPart", EngineAddresses.PppRunPart,
                () => new FhMethodHandle<d_ppp_run_part>(new FhMethodLocation(EngineAddresses.PppRunPart, 0)).hook(this, h_ppp_run_part));
        }

        return ok;
    }

    private string particle_counts()
        => $"part_starts={_part_starts} part_runs={_part_runs} part_loops={_part_loops} " +
           $"part_rescaled={_part_rescaled} part_held={_part_held} fp_runs={_fp_runs} " +
           $"fp_held={_fp_held} fp_scaled={_fp_scaled} {field_lifetimes()} " +
           $"part_step=0x{_part_step_observed:X}";

    /* The hold and the step scaling are alternatives, not layers. Holding halves how often the
     * pass runs; scaling the step on top of that would stretch every lifetime to twice its wall
     * clock length. */
    private int scale_particle_step(int step)
    {
        // A step of zero is a manager that is not meant to advance; scaling it is meaningless, and
        // rounding a small step to zero would freeze the effect outright.
        if (_config.ParticleHold || step <= 0) return step;

        // The emission clock has to move by exactly the factor the object age moved by, and the age
        // is patched with an integer divisor rather than with Scale: the halved age sequence only
        // contains every value the old one reached when the step divides 0x1000 exactly. Deriving
        // the emission factor from Scale instead lets the two drift apart at any rate that is not a
        // whole multiple of 30 - at 50 Hz the age would step by 0x1000/2 while emission slowed by
        // 1.67 - which is the doubled standing population the timeline patch exists to avoid.
        if (_config.ParticleTimeline)
            return _timeline_step <= 0 ? step : Math.Max(1, step / (VanillaAgeStep / _timeline_step));

        return _config.Particles ? Math.Max(1, (int)Math.Round(step / Scale)) : step;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_ppp_start_part(nint manager, int time_step, nint data, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_ppp_part_loop();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint d_ppp_run_part(nint manager, byte mode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint d_ppp_run_part_fp(nint manager, uint mode);

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
    /* The hold sits here rather than on pppPartLoop. Measured 2026-08-30: holding the loop on every
     * second frame ran 30 skips a second, exactly half of 60, and changed nothing about how fast
     * particles moved - because the loop is one of eight callers of this function and the cutscene
     * path is not among them. This is the choke point they all pass through.
     *
     * A held frame returns 0. The callers read the result as "this manager is finished" and tear it
     * down when it is non-zero, so 0 is the answer that says nothing happened. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private uint h_ppp_run_part(nint manager, byte mode)
    {
        _part_runs++;

        if (_config.ParticleHold && !advance_this_frame())
        {
            _part_held++;
            return 0;
        }

        var orig = new FhMethodHandle<d_ppp_run_part>(new FhMethodLocation(EngineAddresses.PppRunPart, 0))
            .chain_from(h_ppp_run_part).fnptr;

        return orig is null ? 0 : orig(manager, mode);
    }

    /* The field half. A held frame returns 0 for the same reason the battle half does: pppFpLoop
     * frees the group and restarts it when the result is non-zero. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private uint h_ppp_run_part_fp(nint manager, uint mode)
    {
        _fp_runs++;

        if (_config.FieldParticleHold && !advance_this_frame())
        {
            _fp_held++;
            return 0;
        }

        if (_config.FieldParticleStepScale) scale_field_step(manager);

        if (!_fp_started.ContainsKey(manager)) note_field_start(manager);

        var orig = new FhMethodHandle<d_ppp_run_part_fp>(new FhMethodLocation(EngineAddresses.PppRunPartFp, 0))
            .chain_from(h_ppp_run_part_fp).fnptr;

        uint result = orig is null ? 0 : orig(manager, mode);

        // Non-zero is pppFpLoop's signal to free the group and start it again.
        if (result != 0) note_field_end(manager);

        return result;
    }

    /* How long a field particle group lives, in wall clock and in presented frames.
     *
     * This is the measurement the eye cannot make. A group's lifetime is authored in the manager's
     * own time units, so at the authored rate it should take the same number of seconds however
     * fast the game presents frames. Twice the advance rate halves the seconds and leaves the frame
     * count alone; a correct retiming restores the seconds and doubles the frames. */
    private string field_lifetimes()
    {
        if (_fp_lifetimes.Count == 0) return "fp_life=-";

        double ms = _fp_lifetimes.Average();
        double frames = _fp_life_frames.Average();

        return $"fp_life={ms:F0}ms/{frames:F0}f(n={_fp_lifetimes.Count})";
    }

    private void note_field_start(nint manager)
    {
        _fp_started[manager] = (_frames, Stopwatch.GetTimestamp());
    }

    private void note_field_end(nint manager)
    {
        if (!_fp_started.Remove(manager, out var start)) return;

        double ms = (Stopwatch.GetTimestamp() - start.Ticks) * 1000.0 / Stopwatch.Frequency;

        // A group that lived a single frame is a restart, not a lifetime.
        if (ms < 16) return;

        _fp_lifetimes.Enqueue(ms);
        _fp_life_frames.Enqueue(_frames - start.Frame);

        while (_fp_lifetimes.Count > 20) { _fp_lifetimes.Dequeue(); _fp_life_frames.Dequeue(); }
    }

    /* The manager's step lives at +0x10 and its accumulated time at +0x8, which _pppRunPartFp
     * advances by one step per call. Halving the step leaves the pass and its draw packet alone.
     *
     * The value we wrote is remembered per manager so a step is halved once rather than on every
     * frame: anything that does not match what we last wrote is a value the engine set, and only
     * that is scaled. */
    private void scale_field_step(nint manager)
    {
        // The two are alternatives, not layers. _pppStartPart is the entry point both particle
        // halves share - pppFpLoop restarts a field group through it with a literal 0x1000, the
        // battle path passes the global step - so h_ppp_start_part has already scaled the +0x10 of
        // every manager including this one, by the age step's integer divisor rather than by Scale.
        // Halving it again here advances field managers at a quarter of the authored step: emission
        // intervals at twice their wall clock length and half the standing population, against an
        // object age that is correct.
        if (_config.ParticleTimeline) return;

        int* step = (int*)(manager + ParticleStepOffset);

        if (*step <= 0) return;
        if (_fp_steps.TryGetValue(manager, out int written) && written == *step) return;

        *step = Math.Max(1, (int)Math.Round(*step / Scale));
        _fp_steps[manager] = *step;
        _fp_scaled++;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_ppp_part_loop()
    {
        _part_loops++;

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
