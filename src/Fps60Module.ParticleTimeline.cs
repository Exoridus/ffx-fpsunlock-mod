namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     The particle system's own clock, and the only correction here that is a rate change in the
///     image rather than a hook.
///
///     Every particle object carries its age in fixed point at obj+0xc, and the dispatcher advances
///     it by a constant per pass. That age is the timeline: a step's keyframe fires when the age
///     equals the cursor's tag, the loop point and the death threshold are compared against it, and
///     the trail kernels index their history buffer with it. At 60 Hz it advances twice as fast as
///     it was authored for, which is why a campfire burns through its program in half the time.
///
///     Halving the constant is safe in a way a hold never is, and the reason is arithmetic rather
///     than luck. A keyframe fires on exact equality, so every tag that works today must lie in
///     {base + k * 0x1000}; halving makes the reachable set {base + k * 0x800}, a strict superset.
///     No keyframe can be missed. Everything else expressed in age units - the loop point, the death
///     threshold, the trail's "am I in the last pass" test, the negative age nudge that keeps an
///     object alive one more pass - is authored data in the same unit and stays correct in wall
///     clock time without being touched. Patching those "for consistency" would break them.
///
///     The manager's emission clock has to move with it. It advances by its own step per pass and
///     decides when the next object is spawned, so halving only the age would double the standing
///     population: the same spawn rate per frame against twice as many frames of life.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    /// <summary>
    ///     The two instructions that convert passes into age. Found by reading the dispatcher and
    ///     confirmed by scanning the whole image, every shipped DLL and every executable in the game
    ///     directory: there are exactly two, both in FFX.exe, and no overlay carries a third.
    /// </summary>
    private static readonly (string Name, nint Rva)[] AgeStepSites =
    [
        ("pppRunPartStd", 0x316E22),
        ("FUN_007170f0",  0x317282),
    ];

    /// <summary>`add dword ptr [eax+0Ch], imm32` - the immediate starts at the fourth byte.</summary>
    private static ReadOnlySpan<byte> AgeStepPrefix => [0x81, 0x40, 0x0C];
    private const int AgeStepImmediateOffset = 3;
    private const int VanillaAgeStep = 0x1000;

    private long _offgrid_keyframes;

    private string particle_timeline_counts() => $"kf_offgrid={_offgrid_keyframes}";

    private bool _timeline_patched;

    /// <summary>
    ///     Applied once the target framerate is known rather than at init, because at init it is
    ///     only implied by the limiter - and with the limiter removed that implication is wrong.
    /// </summary>
    private void patch_particle_timeline()
    {
        if (!_config.ParticleTimeline || _timeline_patched) return;
        _timeline_patched = true;

        int step = Math.Max(1, (int)Math.Round(VanillaAgeStep / Scale));

        if (step == VanillaAgeStep)
        {
            _logger.Info("[Fps60] Particle timeline left at its authored rate; the target framerate needs no change.");
            return;
        }

        foreach ((string name, nint rva) in AgeStepSites)
        {
            byte* site = FhUtil.ptr_at<byte>(rva);

            // Verified before it is written, because a wrong address here rewrites an instruction
            // rather than failing: the three opcode bytes and the vanilla immediate must both match.
            bool matches = new ReadOnlySpan<byte>(site, 3).SequenceEqual(AgeStepPrefix)
                        && *(int*)(site + AgeStepImmediateOffset) == VanillaAgeStep;

            if (!matches)
            {
                _logger.Error($"[Fps60] Particle timeline site {name} at RVA 0x{rva:X} does not carry " +
                              $"the expected instruction; nothing written. Found " +
                              $"{site[0]:X2} {site[1]:X2} {site[2]:X2} imm=0x{*(int*)(site + AgeStepImmediateOffset):X}.");
                return;
            }
        }

        foreach ((string name, nint rva) in AgeStepSites)
        {
            _patches.Write(rva + AgeStepImmediateOffset, BitConverter.GetBytes(step));
            _logger.Info($"[Fps60] Particle age step at {name} (RVA 0x{rva:X}) set to 0x{step:X}.");
        }
    }

    /// <summary>
    ///     Counts keyframe tags that are not multiples of the vanilla step.
    ///
    ///     Such a tag is unreachable today, because the age only ever lands on multiples of 0x1000
    ///     and the comparison is equality - so it is dead authoring data. After the step is halved it
    ///     becomes reachable and fires for the first time. The expectation is zero; a non-zero count
    ///     is not a fault but something to look at before trusting the change.
    /// </summary>
    private void note_keyframe_grid(int data)
    {
        if (data == 0) return;
        if ((*(int*)data & (VanillaAgeStep - 1)) != 0) _offgrid_keyframes++;
    }
}
