namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Runs the engine at the display refresh rate and retimes the frame-bound systems that would
///     otherwise run at double speed.
///
///     Hooks are addressed by RVA (see <see cref="EngineAddresses"/>) because the pinned Fahrenheit's
///     generated call table exposes most engine functions only as FUN_&lt;address&gt; entries.
/// </summary>
[FhLoad(FhGameId.FFX)]
public unsafe sealed partial class Fps60Module : FhModule
{
    private readonly PatchJournal _patches = new();
    private Fps60Config _config = new();

    /// <summary>
    ///     Mirror of the engine's global keep-FPS flag. The engine has no getter for it, so the only
    ///     way to know its state is to watch every write.
    /// </summary>
    private sbyte _sg_keep_fps;

    /// <summary>Target framerate divisor: the engine runs at 60 / this. 1 is 60 FPS, 2 is 30.</summary>
    private static uint VSyncInterval
    {
        get => FhUtil.get_at<uint>(EngineAddresses.SFlipVSyncInterval);
        set => FhUtil.set_at(EngineAddresses.SFlipVSyncInterval, value);
    }

    private static float TargetFramerate => 60f / Math.Max(1u, VSyncInterval);

    public override bool init(FhModContext mod_context, FileStream global_state_file)
    {
        _config = Fps60Config.Load(Path.Combine(AppContext.BaseDirectory, "fhfps60.config.json"));

        // The engine has no shutdown callback, so the restore is bound to process exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _patches.RestoreAll();

        bool ok = true;

        if (_config.Present)
        {
            ok &= hook_or_log("FFXApplication::animate", EngineAddresses.FFXApplicationAnimate,
                () => new FhMethodHandle<d_frame>(new FhMethodLocation(EngineAddresses.FFXApplicationAnimate, 0)).hook(this, h_frame));
        }

        if (_config.KeepVsyncInterval)
        {
            ok &= hook_or_log("SetFlipVSyncInterval", EngineAddresses.SetFlipVSyncInterval,
                () => new FhMethodHandle<d_set_vsync>(new FhMethodLocation(EngineAddresses.SetFlipVSyncInterval, 0)).hook(this, h_set_vsync));
        }

        // Always tracked: several corrections below are conditional on it.
        ok &= hook_or_log("Sg_SetKeepFps", EngineAddresses.SgSetKeepFps,
            () => new FhMethodHandle<d_sg_set_keep_fps>(new FhMethodLocation(EngineAddresses.SgSetKeepFps, 0)).hook(this, h_sg_set_keep_fps));

        ok &= init_timing_hooks();
        ok &= init_battle_hooks();
        ok &= init_frame_sequence_hooks();
        ok &= init_survey_hooks();

        _logger.Info($"[Fps60] Initialized. present={_config.Present} atel={_config.AtelWaits} camera={_config.Camera} " +
                     $"fades={_config.Fades} motion={_config.Motion} battle={_config.BattleTimers}");
        _logger.Info($"[Fps60] Frame-sequence holds: menu_water={_config.MenuWater} fmv={_config.Fmv}. " +
                     "These are 30 Hz inside a 60 Hz game by design; the real fix is content at the target rate.");
        _logger.Info("[Fps60] Particles and texture animation are still unhandled and run at double speed.");

        return ok;
    }

    /// <summary>
    ///     Installs one hook and reports the failure with its address rather than only a false return,
    ///     so a bad RVA is distinguishable from a refused hook.
    /// </summary>
    private bool hook_or_log(string name, nint rva, Func<bool> install)
    {
        bool ok = install();
        if (!ok) _logger.Error($"[Fps60] Could not hook {name} at RVA 0x{rva:X}.");
        return ok;
    }

    // --- Present path ---

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate uint d_frame(nint ptr_this);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_set_vsync(uint interval);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate sbyte d_sg_set_keep_fps(sbyte keep);

    /* The engine retimes its own display loop off the flip vsync interval. Setting it to 1 rather
     * than 2 is what actually produces 60 FPS; everything else in this module compensates for it. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvThiscall)])]
    private uint h_frame(nint ptr_this)
    {
        VSyncInterval = 1;
        _frames++;
        if (_config.Telemetry) sample_present_rate();

        return new FhMethodHandle<d_frame>(new FhMethodLocation(EngineAddresses.FFXApplicationAnimate, 0))
            .chain_from(h_frame).fnptr!(ptr_this);
    }

    /* The engine sets the interval to 1 in menus and 2 everywhere else. Since this module owns it,
     * every request to change it is dropped rather than chained. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_set_vsync(uint interval) { }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private sbyte h_sg_set_keep_fps(sbyte keep)
    {
        _sg_keep_fps = keep;

        return new FhMethodHandle<d_sg_set_keep_fps>(new FhMethodLocation(EngineAddresses.SgSetKeepFps, 0))
            .chain_from(h_sg_set_keep_fps).fnptr!(keep);
    }

    // --- Telemetry ---

    private long _frames;
    private long _frames_at_last_sample;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _last_sample;

    private void sample_present_rate()
    {
        TimeSpan now = _clock.Elapsed;
        if ((now - _last_sample).TotalSeconds < 5) return;

        double fps = (_frames - _frames_at_last_sample) / (now - _last_sample).TotalSeconds;
        _logger.Info($"[Fps60] present {fps:F1} fps over the last {(now - _last_sample).TotalSeconds:F1}s, " +
                     $"vsync_interval={VSyncInterval}, keep_fps={_sg_keep_fps}, " +
                     $"sg_ratef={FhUtil.get_at<float>(EngineAddresses.SgRateF):F3}, {survey_counts()}");

        _frames_at_last_sample = _frames;
        _last_sample = now;
    }
}
