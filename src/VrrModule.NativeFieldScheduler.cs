namespace Fahrenheit.Mods.Vrr;

public unsafe sealed partial class VrrModule
{
    private bool _nativeFieldProbeEnabled => _config.NativeFieldProbe;
    private double _nativeFieldStepSeconds;
    private uint _nativeFieldNativeDivider = 2;
    private bool _nativeFieldHostBound, _nativeFieldHostOrderVerified, _nativeFieldPassFrozen;
    private long _nativeFieldPassSerial, _nativeFieldHostSeenSerial;

    private bool native_field_additional_pass()
        => _nativeFieldProbeEnabled && _nativeFieldPassFrozen && IsOwnedRenderLoop && !_render_simulation_pass;

    private float native_field_loop_delta(float delta)
    {
        // Gameplay events and the native clock share simulation seconds, independent of render intervals.
        if (_nativeFieldProbeEnabled && _nativeFieldPassFrozen && OwnsRenderPass && _render_simulation_pass
            && float.IsFinite(delta) && delta > 0)
        {
            delta = (float)_nativeFieldStepSeconds;
            _render_input_delta = delta;
        }
        return delta;
    }

    private bool native_field_host_classify(float delta)
    {
        if (!RuntimeEffectsArmed || !_nativeFieldProbeEnabled) return false;
        if (!IsOwnedRenderLoop || !_nativeFieldPassFrozen)
        {
            _nativeFieldHostOrderVerified = false;
            return false;
        }
        _nativeFieldHostSeenSerial = _nativeFieldPassSerial;
        _nativeFieldHostOrderVerified = true;
        diagnostic_event("native_field_host_phase", new { pass = _nativeFieldPassSerial, additional = !_render_simulation_pass, delta });
        return !_render_simulation_pass;
    }

    private void native_field_prepare_pass()
    {
        if (_render_frame_enabled && (!_nativeFieldHostOrderVerified || !native_field_environment_ready()))
            _render_frame_enabled = false;
        _render_clock_speed = 1;
        if (_render_plan_pending)
        {
            _render_plan = _render_frame_enabled ? _simulationClock.Plan(_render_now) : new(1, 1, false, 0);
            _render_plan_pending = false;
            _render_steps_remaining = _render_plan.Steps;
            _simulation_debt_dropped += _render_plan.DroppedSteps;
            if (_render_plan.Reset)
            {
                RenderPassContext.ResetHistory();
                reset_render_camera();
                reset_render_actors();
            }
        }
        _render_simulation_pass = !_render_frame_enabled || _render_steps_remaining > 0;
        _nativeFieldPassSerial++;
        _nativeFieldPassFrozen = true;
        RenderPassContext.IsActive = _render_frame_enabled;
        if (!_render_frame_enabled) VSyncInterval = _nativeFieldNativeDivider;
        RenderPassContext.IsPresentation = !_render_simulation_pass;
        pad_latch_pass_decided(RenderPassContext.IsPresentation);
        battle_effects_begin_pass(RenderPassContext.IsPresentation);
        RenderPassContext.SetPass(_render_frame_enabled ? _render_plan.Alpha : 1,
            _render_frame_enabled && (!_render_simulation_pass || _render_steps_remaining <= 1));
        render_camera_set_presentation_alpha(_render_frame_enabled ? _render_plan.Alpha : 1);
        if (_render_simulation_pass)
        {
            RenderPassContext.BeginSimulation();
            render_camera_begin_simulation();
            render_actors_begin_simulation();
        }
        else
        {
            viewport_presentation_begin();
            _advance_frame = false;
            _render_particle_pause_before = render_pause_particles();
            render_camera_begin_presentation();
            render_actors_begin_presentation(_render_plan.Alpha);
        }
    }

    private RenderClockAction native_field_clock(float nativeDelta)
    {
        _render_clock_seen = true;
        // Native-owned startup can pass zero delta without requesting independent admission.
        if (_render_simulation_pass && !_render_frame_enabled)
        {
            if (_render_clock_bootstrap) _simulationClock.Plan(_render_now);
            return RenderClockAction.Native;
        }
        bool hostMatched = _nativeFieldPassFrozen && _nativeFieldHostSeenSerial == _nativeFieldPassSerial;
        bool ordinarySpeed = float.IsFinite(nativeDelta) && nativeDelta > 0 && _render_input_delta > 0
            && Math.Abs(nativeDelta / _render_input_delta - 1) < 0.0001;
        if (!_render_simulation_pass)
        {
            if (!hostMatched || !ordinarySpeed || !native_field_environment_ready() || !pad_snapshot_presentation_ready())
                native_field_abort("native_field_additional_phase_lost");
            FhUtil.set_at(EngineAddresses.ElapsedFrameCountFrameSkip, 1);
            return RenderClockAction.Presentation;
        }
        if (!hostMatched || !ordinarySpeed)
        {
            _presentationAdmission.Refuse(hostMatched ? "native_field_speed_unsupported" : "native_field_host_order");
            _render_frame_enabled = false;
            RenderPassContext.IsActive = false;
            return RenderClockAction.Native;
        }
        decide_frame_advance();
        advance_overlay_spawn_clock();
        // The owned loop supplied the native simulation period before raising gameplay events.
        pad_native_clock_invoke(nativeDelta);
        if (!native_field_environment_ready() || pad_native_input_owner())
        {
            render_scheduler_native_input_handover();
            return RenderClockAction.Advanced;
        }
        FhUtil.set_at(EngineAddresses.ElapsedFrameCountFrameSkip, Math.Max(1, _render_steps_remaining));
        return RenderClockAction.Advanced;
    }
}
