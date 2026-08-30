namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Per-correction toggles. Every retiming hook is individually switchable so a
///     regression can be bisected without rebuilding, and so a subsystem that is not
///     yet correct at 60 Hz can be disabled and reported rather than silently
///     falling back to 30 Hz.
/// </summary>
public sealed record Fps60Config
{
    /// <summary>Force the present path off the engine's 30 Hz vsync interval.</summary>
    public bool Present { get; init; } = true;

    /// <summary>Suppress engine code that resets the flip vsync interval back to 30 Hz.</summary>
    public bool KeepVsyncInterval { get; init; } = true;

    /// <summary>Feed the character update a delta derived from the measured present interval.</summary>
    public bool CharacterDelta { get; init; } = true;

    /// <summary>Scale ATEL frame waits to the active refresh rate.</summary>
    public bool AtelWaits { get; init; } = true;

    /// <summary>Retime camera move durations and accelerations.</summary>
    public bool Camera { get; init; } = true;

    /// <summary>Retime fade, flash and alpha ramps.</summary>
    public bool Fades { get; init; } = true;

    /// <summary>Retime motion and effect speeds.</summary>
    public bool Motion { get; init; } = true;

    /// <summary>
    ///     Decide the motion scale per actor rather than on KEEP_FPS alone, using the actor flag
    ///     0x100000 that gates the engine's own sg_rate correction. Off: acting on that flag made a
    ///     cutscene run at half speed and then stall, so the reading is incomplete.
    /// </summary>
    public bool MotionPerActor { get; init; }

    /// <summary>Retime the battle limit timer.</summary>
    public bool BattleTimers { get; init; } = true;

    /// <summary>
    ///     Hold the main menu water animation so its 69 prepared images play at their authored rate.
    ///     Frame skipping, and therefore 30 Hz inside a 60 Hz game.
    /// </summary>
    public bool MenuWater { get; init; } = true;

    /// <summary>
    ///     Hold the FMV texture update so 29.97 video plays at its authored rate. Frame skipping.
    ///     Turn this off once the shipped videos are encoded at the target framerate.
    /// </summary>
    public bool Fmv { get; init; } = true;

    /// <summary>
    ///     Hold the old-format character texture animation advance. The two texture animation
    ///     formats are retimed differently: the new one follows Ch_TextureSetAnimTimer, which the
    ///     motion retiming already scales, while the old one counts in literal increments and can
    ///     only be held. 445 of 872 character model directories are on the old path.
    /// </summary>
    public bool TextureAnimation { get; init; } = true;

    /// <summary>
    ///     Scale the time step every particle manager is started with. Unlike the held systems this
    ///     is a real retiming: the effect advances half as far per frame and therefore takes the
    ///     same wall clock time as at 30 Hz, with drawing and lifetime untouched.
    /// </summary>
    public bool Particles { get; init; } = true;

    /// <summary>
    ///     Hold the particle manager pass instead of scaling its time step, skipping it on the
    ///     frames a 30 Hz sequence would not have advanced.
    ///
    ///     An experiment, off by default, and the only lever consistent with what the step
    ///     scaling measured: pppRunPartStd runs once per call to _pppRunPart, before and
    ///     independently of the accumulator update, so particle motion follows how often the pass
    ///     runs rather than how large its step is. At 60 Hz it simply runs twice as often.
    ///
    ///     The risk is that the same call builds the draw packet, in which case particles are not
    ///     drawn on the held frames and flicker. That is what this flag exists to find out.
    /// </summary>
    public bool ParticleHold { get; init; }

    /// <summary>
    ///     Hold the effect advance on skipped frames, leaving the effect draw on every frame.
    ///
    ///     MsEffectProcess(0) advances, MsEffectProcess(1) draws, and the engine calls the advance
    ///     once per presented frame whether or not the frame ran a simulation step. Holding only the
    ///     advance is the one lever here that cannot flicker, because nothing about drawing changes.
    /// </summary>
    public bool EffectHold { get; init; }

    /// <summary>
    ///     Attach the MsEffectProcess hook and count advance and draw calls without holding
    ///     anything. Separates the two failure modes: a hook that the game cannot survive at all,
    ///     and a hold the overlay's state machine cannot survive.
    /// </summary>
    public bool EffectProbe { get; init; }

    /// <summary>
    ///     Overwrite the hardcoded 29.97 the video update loop multiplies its time step by
    ///     (a double in .rdata at 0x74A180). Null leaves it alone.
    ///
    ///     This is an experiment before it is a feature. The open question is whether the player
    ///     reads the container's own timestamps at all or only ever this constant: if playback
    ///     speed follows the value, the constant is the clock and content at 59.94 becomes usable;
    ///     if nothing changes, the container drives it and the constant is a leftover. WebM stores
    ///     no framerate field, only per-frame timestamps, so the constant is an assumption about
    ///     the asset rather than something read from it.
    /// </summary>
    public double? VideoTargetFramerate { get; init; }

    /// <summary>
    ///     Count calls to the texture animation candidates and report them in the telemetry line.
    ///     Changes no behaviour; it identifies which function is on the live path.
    /// </summary>
    public bool SurveyTextureAnimation { get; init; } = true;

    /// <summary>Log measured present rate and frame delta statistics.</summary>
    public bool Telemetry { get; init; } = true;

    public static Fps60Config Load(string path)
    {
        if (!File.Exists(path)) return new Fps60Config();
        return JsonSerializer.Deserialize<Fps60Config>(File.ReadAllText(path)) ?? new Fps60Config();
    }

    /// <summary>
    ///     Where the config actually sits, which is beside this assembly in the mod's own
    ///     directory. AppContext.BaseDirectory is the host's bin directory, not the mod's, so a
    ///     config written next to the DLL was silently never read and every run took the defaults.
    /// </summary>
    public static string ResolvePath()
    {
        var assembly = typeof(Fps60Config).Assembly.Location;

        if (!string.IsNullOrEmpty(assembly) && Path.GetDirectoryName(assembly) is { Length: > 0 } dir)
            return Path.Combine(dir, FileName);

        return Path.Combine(AppContext.BaseDirectory, FileName);
    }

    public const string FileName = "fhfps60.config.json";
}
