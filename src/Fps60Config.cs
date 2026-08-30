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
}
