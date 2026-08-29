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

    /// <summary>Log measured present rate and frame delta statistics.</summary>
    public bool Telemetry { get; init; } = true;

    public static Fps60Config Load(string path)
    {
        if (!File.Exists(path)) return new Fps60Config();
        return JsonSerializer.Deserialize<Fps60Config>(File.ReadAllText(path)) ?? new Fps60Config();
    }
}
