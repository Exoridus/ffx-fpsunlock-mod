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

    /// <summary>The engine's KEEP_FPS flag, read where it lives.</summary>
    private static sbyte KeepFps => FhUtil.get_at<sbyte>(EngineAddresses.SgKeepFps);

    /// <summary>Target framerate divisor: the engine runs at 60 / this. 1 is 60 FPS, 2 is 30.</summary>
    private static uint VSyncInterval
    {
        get => FhUtil.get_at<uint>(EngineAddresses.SFlipVSyncInterval);
        set => FhUtil.set_at(EngineAddresses.SFlipVSyncInterval, value);
    }

    /// <summary>
    ///     What the module writes into the engine's limiter. 1 is 59.94 Hz, 2 is half that, and 0
    ///     removes the limit entirely - the WinMain pump multiplies this by one frame at 59.94 to
    ///     get the time it must sleep, so zero means it never sleeps.
    /// </summary>
    private static uint _vsync_interval_target = 1;
    private static uint VSyncIntervalTarget => _vsync_interval_target;

    /// <summary>
    ///     The rate every correction in this module is derived from, and nothing here assumes it is
    ///     60. It is the configured override, else the measured present rate snapped to a nominal
    ///     refresh rate, else what the limiter implies before the first measurement exists.
    ///
    ///     Measuring rather than assuming is what makes an unlocked framerate work: at interval 0
    ///     the engine presents as fast as it can, no global states the rate, and a scale derived
    ///     from a wrong number is worse than no scale at all.
    /// </summary>
    private static float TargetFramerate => _measured_framerate > 0
        ? _measured_framerate
        : 60f / Math.Max(1u, VSyncIntervalTarget);

    private static float _measured_framerate;
    private double _previous_sample_fps;

    /// <summary>
    ///     Nominal rates a display actually runs at. The measured value is snapped onto one of these
    ///     when it is close enough, because a scale that drifts with a frame time spike would make
    ///     every held sequence stutter; a rate far from all of them is used as measured.
    /// </summary>
    private static readonly float[] NominalRates = [30f, 50f, 60f, 72f, 75f, 90f, 100f, 120f, 144f, 165f, 240f];

    public override bool init(FhModContext mod_context, FileStream global_state_file)
    {
        string config_path = Fps60Config.ResolvePath();
        bool   config_found = File.Exists(config_path);

        _config = Fps60Config.Load(config_path);

        // The engine has no shutdown callback, so the restore is bound to process exit. The parked
        // texture animation step bytes go back first: they are engine work-buffer state rather than
        // image bytes, so the journal knows nothing about them.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            restore_parked_texture_animation();
            _patches.RestoreAll();
        };

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

        if (_config.VideoTargetFramerate is { } fps)
        {
            // .rdata, so PatchJournal's VirtualProtect window is what makes it writable; the
            // journal also puts the original 29.97 back on process exit.
            _patches.Write(EngineAddresses.TextureVideoTargetFramerate, BitConverter.GetBytes(fps));
            _logger.Info($"[Fps60] Video target framerate patched to {fps} at 0x{EngineAddresses.TextureVideoTargetFramerate:X}.");
        }

        ok &= init_timing_hooks();
        ok &= init_battle_hooks();
        ok &= init_frame_sequence_hooks();
        ok &= init_particle_hooks();
        ok &= init_effect_hooks();
        ok &= init_particle_kernel_hooks();
        ok &= init_motion_survey();
        ok &= init_survey_hooks();
        ok &= init_motion_sequence_hooks();
        ok &= init_lens_sprite_hook();
        ok &= init_cross_fade_hook();
        ok &= init_eternal_calm_hook();
        ok &= init_idle_sway_hook();
        ok &= init_neck_tracking_hook();
        

        _sync_aware = _config.SyncDataAware;
        _vsync_interval_target = _config.PresentInterval;
        if (_config.TargetFramerateOverride is { } forced) _measured_framerate = (float)forced;

        // Said first and unconditionally: a config that was looked for in the wrong directory
        // reads as a run with every default, and nothing else in this log distinguishes the two.
        _logger.Info(config_found
            ? $"[Fps60] Config read from {config_path}."
            : $"[Fps60] No config at {config_path} - every setting is at its default.");
        _logger.Info($"[Fps60] Initialized. present={_config.Present} atel={_config.AtelWaits} camera={_config.Camera} " +
                     $"fades={_config.Fades} motion={_config.Motion} battle={_config.BattleTimers}");
        _logger.Info($"[Fps60] Frame-sequence holds: menu_water={_config.MenuWater} fmv={_config.Fmv} " +
                     $"texture_animation={_config.TextureAnimation}. These are 30 Hz inside a 60 Hz game by " +
                     "design; the real fix is content at the target rate.");
        _logger.Info(_config.ParticleHold
            ? $"[Fps60] Particles held on skipped frames: particle_hold={_config.ParticleHold}, step scaling off."
            : $"[Fps60] Particles retimed by scaling the manager time step: particles={_config.Particles}.");

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

    /* The engine retimes its own display loop off the flip vsync interval. Setting it to 1 rather
     * than 2 is what actually produces 60 FPS; everything else in this module compensates for it. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvThiscall)])]
    private uint h_frame(nint ptr_this)
    {
        VSyncInterval = VSyncIntervalTarget;
        _frames++;

        // Both before the engine's update for this frame: the first decides whether this frame
        // advances a held sequence, the second acts on that decision.
        decide_frame_advance();
        retime_texture_animation();
        sample_particle_population();

        // Not behind the telemetry flag. This is where the rate every correction derives from is
        // measured, and where the image patches are kept in step with it; only the log line it
        // emits is telemetry.
        sample_present_rate();
        apply_rate_patches();

        return new FhMethodHandle<d_frame>(new FhMethodLocation(EngineAddresses.FFXApplicationAnimate, 0))
            .chain_from(h_frame).fnptr!(ptr_this);
    }

    /* The engine sets the interval to 1 in menus and 2 everywhere else. Since this module owns it,
     * every request to change it is dropped rather than chained. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_set_vsync(uint interval) { }

    // --- Telemetry ---

    private long _frames;
    private long _frames_at_last_sample;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _last_sample;

    /// <summary>
    ///     Measures the present rate over a five second window and hands it to the rate adoption.
    ///     Only the log line at the end is telemetry: the measurement itself is what
    ///     <see cref="TargetFramerate"/> is, so gating the whole method on the telemetry flag left
    ///     every correction running on the rate the limiter implied rather than the one the display
    ///     actually has.
    /// </summary>
    private void sample_present_rate()
    {
        TimeSpan now = _clock.Elapsed;
        if ((now - _last_sample).TotalSeconds < 5) return;

        double fps = (_frames - _frames_at_last_sample) / (now - _last_sample).TotalSeconds;

        adopt_measured_rate(fps);

        if (_config.Telemetry)
        {
            _logger.Info($"[Fps60] present {fps:F1} fps over the last {(now - _last_sample).TotalSeconds:F1}s, " +
                         $"vsync_interval={VSyncInterval}, keep_fps={KeepFps}, " +
                         $"sg_ratef={FhUtil.get_at<float>(EngineAddresses.SgRateF):F3}, " +
                         $"{particle_counts()}, {effect_counts()}, {kernel_counts()}, {motion_counts()}, {survey_counts()}, " +
                         $"{engine_state_counts()}, " +
                         $"{motion_sequence_counts()}, " +
                         $"{frame_sequence_counts()}, {lens_sprite_counts()}, " +
                         $"{texture_animation_counts()} cam_acc={_camera_acc_calls} {motion_speed_counts()}, " +
                         $"{cross_fade_counts()}, {atel_wait_counts()}, " +
                         $"{particle_timeline_counts()}, {frame_skip_counts()}, {eternal_calm_counts()}, " +
                         $"{idle_sway_counts()}, {neck_counts()}");
        }

        _frames_at_last_sample = _frames;
        _last_sample = now;
    }

    /// <summary>
    ///     Takes the measured present rate as the rate to correct for, once it is stable enough to
    ///     trust. A loading screen or a stall would otherwise move the scale, and a scale that moves
    ///     retimes a sequence that is already running.
    /// </summary>
    private void adopt_measured_rate(double fps)
    {
        if (_config.TargetFramerateOverride is { } forced)
        {
            if (Math.Abs(_measured_framerate - forced) > 0.01f)
            {
                _measured_framerate = (float)forced;
                _logger.Info($"[Fps60] Target framerate forced to {forced:F2} by config.");
            }

            return;
        }

        if (fps < 20 || fps > 400) return;

        // A rate is adopted only once two consecutive samples agree. The first sample of a run falls
        // inside the boot load and measured 46.7 fps once, which snapped to 50 and set a scale of
        // 1.67 for a game that was about to run at 60.
        double previous = _previous_sample_fps;
        _previous_sample_fps = fps;

        if (previous <= 0 || Math.Abs(fps - previous) / fps > 0.05) return;

        float nominal = NominalRates.MinBy(r => Math.Abs(r - fps));
        float adopted = Math.Abs(nominal - fps) / nominal < 0.08f ? nominal : (float)fps;

        // Only a change worth reacting to moves the scale, so an ordinary sample does nothing.
        if (Math.Abs(adopted - _measured_framerate) / Math.Max(1f, adopted) < 0.05f) return;

        _logger.Info($"[Fps60] Target framerate {(_measured_framerate == 0 ? "set" : "changed")} to " +
                     $"{adopted:F2} from a measured {fps:F1} fps. Scale is now {adopted / 30f:F2}.");

        _measured_framerate = adopted;
    }

    /// <summary>
    ///     Re-derives the three image patches, once per presented frame.
    ///
    ///     Every one of them takes its value from <see cref="Scale"/>, and Scale carries the syncdata
    ///     term as well as the measured rate. That term flips whenever a scene with recorded PS2
    ///     frame times starts or ends, which is far more often than the measured rate moves - so
    ///     re-deriving only on a rate change leaves a byte in .text that disagrees with the value the
    ///     rest of the module is using, with no second trigger to correct it. A byte cannot follow a
    ///     per-scene state unless something rewrites it.
    ///
    ///     Each of the three is a compare and a return while its value is unchanged, so the cost of
    ///     asking every frame is three float divisions. The rate is not asked for before one has been
    ///     adopted: at init it is only implied by the limiter, and with the limiter removed that
    ///     implication is wrong.
    /// </summary>
    private void apply_rate_patches()
    {
        if (_measured_framerate <= 0) return;

        patch_particle_timeline();
        patch_battle_cursor_blink();
        patch_dream_overlay_stars();
    }
}
