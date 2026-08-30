namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Particle retiming. Every particle manager keeps a fixed-point time accumulator and advances
///     it by a per-manager step each frame, so unlike the frame-sequence systems particles carry a
///     rate that can be scaled rather than only held.
///
///     The step is set once, when a manager starts: <c>_pppStartPart</c> writes its second argument
///     into the manager's +0x10 and <c>_pppRunPart</c> adds that to the accumulator. Scaling the
///     argument therefore halves particle speed at 60 Hz while leaving drawing and lifetime alone -
///     an effect simply takes twice as many frames to reach the same limit, which is the same wall
///     clock duration.
///
///     Scaling the argument rather than the global it usually comes from is deliberate: the global
///     is only one of the sources. The magic path starts its managers with a literal 0x1000, and a
///     write to the global would not reach it.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private long _part_starts;
    private int  _part_step_observed;

    private bool init_particle_hooks()
    {
        if (!_config.Particles) return true;

        return hook_or_log("_pppStartPart", EngineAddresses.PppStartPart,
            () => new FhMethodHandle<d_ppp_start_part>(new FhMethodLocation(EngineAddresses.PppStartPart, 0)).hook(this, h_ppp_start_part));
    }

    private string particle_counts()
        => $"part_starts={_part_starts} part_step=0x{_part_step_observed:X}";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_ppp_start_part(nint manager, int time_step, nint data, int flags);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_ppp_start_part(nint manager, int time_step, nint data, int flags)
    {
        _part_starts++;
        _part_step_observed = time_step;

        // A step of zero is a manager that is not meant to advance; scaling it is meaningless and
        // rounding a small step to zero would freeze the effect outright.
        int scaled = time_step > 0 ? Math.Max(1, (int)Math.Round(time_step / Scale)) : time_step;

        new FhMethodHandle<d_ppp_start_part>(new FhMethodLocation(EngineAddresses.PppStartPart, 0))
            .chain_from(h_ppp_start_part).fnptr?.Invoke(manager, scaled, data, flags);
    }
}
