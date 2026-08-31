namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Observe-only survey of the per-actor motion advance.
///
///     FUN_00838d10 is where an actor's motion speed enters its frame accumulator, and it is the
///     only reader of sg_rate in the whole image. Three paths leave it:
///
///     <list type="bullet">
///       <item>flag 0x40 clear: the speed is scaled by sg_rate, but only when the actor also
///             carries 0x100000 and KEEP_FPS is set;</item>
///       <item>flag 0x40 set and the sign bit set: a separate accumulator at actor+0x740 that
///             wraps against a per-motion length, with no rate correction anywhere;</item>
///       <item>flag 0x40 set and the sign bit clear: the same speed computation as the first path,
///             again with no rate correction.</item>
///     </list>
///
///     So two of the three paths cannot be corrected by KEEP_FPS at all, and the first is gated on
///     a bit nothing in the decompilation ever sets. Forcing the correction for every actor was
///     tried and made a cutscene run at half speed and then stall, so the answer is not "set it
///     everywhere". This counts which path each actor actually takes, which is the measurement that
///     was missing.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const uint MotionFlagAltPath = 0x40;
    private const uint MotionFlagRated   = 0x100000;

    private long _motion_calls;
    private long _motion_rated;      // 0x40 clear and 0x100000 set: the corrected path
    private long _motion_plain;      // 0x40 clear, no 0x100000: uncorrected
    private long _motion_alt;        // 0x40 set: the other accumulator, never corrected
    private uint _motion_flags_seen; // every bit ever observed, to see what an actor carries

    private string motion_counts()
        => $"mot_calls={_motion_calls} mot_rated={_motion_rated} mot_plain={_motion_plain} " +
           $"mot_alt={_motion_alt} mot_bits=0x{_motion_flags_seen:X}";

    private bool init_motion_survey()
    {
        if (!_config.MotionSurvey) return true;

        return hook_or_log("motion advance", EngineAddresses.MotionAdvance,
            () => new FhMethodHandle<d_motion_advance>(new FhMethodLocation(EngineAddresses.MotionAdvance, 0))
                .hook(this, h_motion_advance));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_motion_advance(nint actor, int mode);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_motion_advance(nint actor, int mode)
    {
        if (actor != 0)
        {
            uint flags = *(uint*)(actor + EngineAddresses.ActorFlagsOffset);

            _motion_calls++;
            _motion_flags_seen |= flags;

            if ((flags & MotionFlagAltPath) != 0)      _motion_alt++;
            else if ((flags & MotionFlagRated) != 0)   _motion_rated++;
            else                                       _motion_plain++;
        }

        new FhMethodHandle<d_motion_advance>(new FhMethodLocation(EngineAddresses.MotionAdvance, 0))
            .chain_from(h_motion_advance).fnptr?.Invoke(actor, mode);
    }
}
