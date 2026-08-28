namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Scaffold for the global 60 FPS mod. Owns configuration, the patch journal and
///     shutdown restore. Hooks are added per subsystem in dedicated partials.
/// </summary>
[FhLoad(FhGameId.FFX)]
public unsafe sealed partial class Fps60Module : FhModule
{
    private readonly PatchJournal _patches = new();
    private Fps60Config _config = new();

    public override bool init(FhModContext mod_context, FileStream global_state_file)
    {
        _config = Fps60Config.Load(Path.Combine(AppContext.BaseDirectory, "fhfps60.config.json"));
        _logger.Info("[Fps60] Scaffold loaded. No retiming hooks are installed yet.");
        return true;
    }

    public override bool shutdown()
    {
        _patches.RestoreAll();
        return true;
    }
}
