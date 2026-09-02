namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     A read-only probe over the field particle manager array, for the one diagnosis that cannot be
///     made from the counters the other subsystems keep.
///
///     Field particle groups vanish and come back, and two candidate causes are left. pppFpLoop
///     keeps a restart countdown at manager+0x00 which it decrements once per pass, with no time
///     term at all, and then tests the result: while the result is still zero or above the group
///     is skipped outright - not advanced, not drawn - and at -1 every object is freed and the
///     group is started again in the same pass as the decrement. That is a pass-counted duration,
///     so at twice the pass rate it elapses in half the wall clock time. The other candidate is
///     pppCheckViewCylinder, which is vanilla, camera driven and rate independent, and which
///     nothing in this module could have changed.
///
///     The countdown is what this reads, and the two end-condition flags beside it are what says
///     which kind of group is being looked at: _pppRunPartFp ends a group on its authored death
///     time when +0x1C is zero, and on an empty object list when +0x1C and +0x1D are both set. A
///     session in which every live manager reads -1 throughout eliminates the countdown and leaves
///     the view cylinder.
///
///     Draw-nothing, write-nothing. It reads engine memory and emits a log line; it holds no frame,
///     scales no step, and no correction reads any state it keeps.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const int FieldManagerStride = 0x80;

    /// <summary>
    ///     Entries in the manager array. The parallel per-group array pppFpLoop indexes alongside it
    ///     begins 0x4000 bytes further on, so the table cannot hold more than this however the count
    ///     word reads.
    /// </summary>
    private const int FieldManagerCapacity = 0x4000 / FieldManagerStride;

    /// <summary>pppFpLoop's own marker for a group that is not running.</summary>
    private const int FieldManagerDead = unchecked((int)0xFFFFF000);

    private TimeSpan _fp_probe_last;
    private long _fp_countdowns_seen;

    /// <summary>
    ///     Samples the array once a second from the frame hook, which is the game thread and is
    ///     between two pppFpLoop passes rather than inside one, so nothing is being written while it
    ///     reads. One line per sample rather than one per manager, so a play session stays readable.
    /// </summary>
    private void sample_field_managers()
    {
        if (!_config.FieldManagerProbe) return;

        TimeSpan now = _clock.Elapsed;
        if ((now - _fp_probe_last).TotalSeconds < 1) return;
        _fp_probe_last = now;

        nint table = FhUtil.get_at<nint>(EngineAddresses.PpvFpGroupTable);
        if (table == 0) return;

        int count = *(ushort*)(table + 4);
        if (count > FieldManagerCapacity) count = FieldManagerCapacity;

        byte* managers = FhUtil.ptr_at<byte>(EngineAddresses.PpvFpManagers);

        List<string> live = [];

        for (int i = 0; i < count; i++)
        {
            byte* mgr = managers + i * FieldManagerStride;
            int countdown = *(int*)mgr;

            if (countdown == FieldManagerDead) continue;

            if (countdown >= 0) _fp_countdowns_seen++;

            live.Add($"[{i} cd={countdown} f1c={mgr[0x1C]} f1d={mgr[0x1D]}]");
        }

        // Logged even when nothing is live: an empty sample is what says the field half was reached
        // at all, and distinguishes a scene with no groups from a probe that never ran.
        _logger.Info($"[Fps60] fp_mgr t={ElapsedSeconds:F1} groups={count} live={live.Count} " +
                     $"counting={_fp_countdowns_seen} {string.Join(' ', live)}");
    }
}
