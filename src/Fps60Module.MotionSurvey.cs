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
///
///     The counts alone say how many actors run uncorrected but not which, so each path also names
///     the actors that reach it, once per distinct identity rather than once per frame.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const uint MotionFlagAltPath = 0x40;
    private const uint MotionFlagRated   = 0x100000;

    /// <summary>
    ///     Identity fields on the actor, which this reads as a <c>Chr</c>: model id at +0x4 and the
    ///     actor id at +0xc. The flag word the advance itself tests sits at +0x194, inside Chr's
    ///     layout, which is what ties the two together - but nothing in the advance proves the
    ///     parameter is a Chr, so the identity is logged rather than acted on. An actor that is not
    ///     one shows up as an implausible pair instead of silently mislabelling the finding.
    /// </summary>
    private const int ChrModelIdOffset = 0x4;
    private const int ChrIdOffset      = 0xC;

    /// <summary>
    ///     A scene can run more actors than are worth naming, and the log is not a per-frame trace.
    ///     Past this many distinct identities per path the survey keeps counting and stops naming.
    /// </summary>
    private const int MotionIdentityCap = 64;

    private long _motion_calls;
    private long _motion_rated;      // 0x40 clear and 0x100000 set: the corrected path
    private long _motion_plain;      // 0x40 clear, no 0x100000: uncorrected
    private long _motion_alt;        // 0x40 set: the other accumulator, never corrected
    private uint _motion_flags_seen; // every bit ever observed, to see what an actor carries

    private readonly HashSet<ulong> _motion_identities = [];
    private readonly Dictionary<string, int> _motion_named_per_path = [];

    private string motion_counts()
        => $"mot_calls={_motion_calls} mot_rated={_motion_rated} mot_plain={_motion_plain} " +
           $"mot_alt={_motion_alt} mot_bits=0x{_motion_flags_seen:X} " +
           $"mot_named={_motion_identities.Count}";

    /// <summary>
    ///     Logs an actor the first time it reaches a given path. The path is part of the key, so an
    ///     actor that changes classification mid-scene is reported again rather than swallowed -
    ///     which is itself the interesting case, since the gating bit is meant to arrive with data.
    /// </summary>
    private void note_motion_actor(string path, nint actor, uint flags)
    {
        uint model_id = *(uint*)(actor + ChrModelIdOffset);
        ushort id     = *(ushort*)(actor + ChrIdOffset);

        ulong key = ((ulong)path[0] << 56) | ((ulong)model_id << 16) | id;
        if (!_motion_identities.Add(key)) return;

        // Capped per path, not overall: the rated path carries nearly every actor and would
        // otherwise use up the budget before a single uncorrected one is named.
        _motion_named_per_path.TryGetValue(path, out int named);
        _motion_named_per_path[path] = named + 1;
        if (named >= MotionIdentityCap) return;

        _logger.Info($"[Fps60] Motion {path}: model_id=0x{model_id:X} id=0x{id:X} flags=0x{flags:X}.");
    }

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

            if ((flags & MotionFlagAltPath) != 0)    { _motion_alt++;   note_motion_actor("alt",   actor, flags); }
            else if ((flags & MotionFlagRated) != 0) { _motion_rated++; note_motion_actor("rated", actor, flags); }
            else                                     { _motion_plain++; note_motion_actor("plain", actor, flags); }
        }

        new FhMethodHandle<d_motion_advance>(new FhMethodLocation(EngineAddresses.MotionAdvance, 0))
            .chain_from(h_motion_advance).fnptr?.Invoke(actor, mode);
    }
}
