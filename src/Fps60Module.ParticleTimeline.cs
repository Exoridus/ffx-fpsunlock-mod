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

    private int _timeline_step;

    /// <summary>
    ///     Applied once the target framerate is known rather than at init, because at init it is
    ///     only implied by the limiter - and with the limiter removed that implication is wrong.
    ///
    ///     Re-applied whenever the rate changes. A one-shot patch looked right and was not: the
    ///     first measurement of a run happens while the game is still loading, and a boot sample of
    ///     46.7 fps froze the step at a correction for 50 Hz that the next sample already knew was
    ///     wrong. Writing again is safe because the journal restores in reverse order, so the
    ///     original still comes back last.
    /// </summary>
    private void patch_particle_timeline()
    {
        if (!_config.ParticleTimeline) return;

        // The step must divide the vanilla one exactly, and that is not a preference.
        //
        // The whole safety argument is that the halved age sequence contains every value the old one
        // reached, so no keyframe can be missed - and that only holds for 0x1000 / n. A step of
        // 0x99A, which is what a scale of 1.67 asks for, makes the ages multiples of 2458 and they
        // then never equal a keyframe at a multiple of 4096 again. The effect keeps its rate and
        // loses its program: pyreflies that stop at whatever size their last reached keyframe left
        // them at, and a fire that burns at the right speed and looks wrong.
        int divisor = Math.Max(1, (int)Math.Round(Scale));
        int step = VanillaAgeStep / divisor;

        if (step == _timeline_step) return;
        _timeline_step = step;

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
            int current = *(int*)(site + AgeStepImmediateOffset);

            // Either untouched or carrying a step this module wrote earlier; anything else is not
            // the instruction we think it is.
            bool matches = new ReadOnlySpan<byte>(site, 3).SequenceEqual(AgeStepPrefix)
                        && current > 0 && VanillaAgeStep % current == 0;

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
