namespace Fahrenheit.Mods.Vrr;

public unsafe sealed partial class VrrModule
{
    private bool _nativeFieldDividerObserved;

    private bool prepare_native_field_probe()
    {
        if (Environment.GetEnvironmentVariable("FFX_VRR_NATIVE_FIELD_REFERENCE") == "1") return false;
        byte[] body = new byte[NativeFieldTiming.LimiterBodyLength];
        Span<byte> period = stackalloc byte[8];
        if (!MemoryProbe.TryCopy((nint)FhUtil.ptr_at<byte>(NativeFieldTiming.LimiterBody), body)
            || !NativeFieldTiming.TryFindLimiterOperand(body, out uint operand)
            || !MemoryProbe.TryCopy(unchecked((nint)operand), period)
            || !NativeFieldTiming.TryGetStepSeconds(BitConverter.ToDouble(period), out _nativeFieldStepSeconds))
        {
            _logger.Error("[Vrr] Native field probe refused: unsupported native limiter.");
            return false;
        }
        object events = FhApi.Events.Common.GameLoop;
        var bind = events.GetType().GetMethod("TryBindAdditionalImageClassifier", [typeof(object), typeof(Func<float, bool>)]);
        try
        {
            if (bind is null || bind.ReturnType != typeof(bool)
                || bind.Invoke(events, [this, (Func<float, bool>)native_field_host_classify]) is not true)
            {
                _logger.Error("[Vrr] Native field probe requires the reviewed Fahrenheit additional-image phase extension before any hooks are installed.");
                return false;
            }
        }
        catch (Exception error) { _logger.Error($"[Vrr] Native field host binding refused: {error.Message}"); return false; }
        _nativeFieldHostBound = true;
        _renderActorInterpolationDisabled = Environment.GetEnvironmentVariable("FFX_VRR_NATIVE_FIELD_ACTOR_INTERPOLATION") == "0";
        IndependentPresentationEnabled = true;
        NativeRenderValidationEnabled = false;
        _vsync_interval_target = 2;
        _logger.Warning($"[Vrr] Bounded native field probe: step={_nativeFieldStepSeconds:R}s, render cap=60, actor interpolation={!_renderActorInterpolationDisabled}. Native gameplay and geometry acceptance remain pending.");
        return true;
    }

    private bool native_field_environment_ready()
    {
        nint foreground = native_field_foreground_window();
        native_field_window_process(foreground, out uint process);
        Span<byte> room = stackalloc byte[2];
        Span<byte> scene = stackalloc byte[4];
        return foreground != 0 && process == Environment.ProcessId
            && _diagnostics?.IsCapturing == true
            && _nativeFieldNativeDivider == 2
            && MemoryProbe.TryCopy((nint)Fahrenheit.FFX.Globals.save_data, room)
            && MemoryProbe.TryCopy((nint)Fahrenheit.FFX.Globals.event_id, scene)
            && BitConverter.ToUInt16(room) == 60 && BitConverter.ToInt32(scene) == 60
            && !EngineSyncPaced && !fmv_playing() && !pad_native_input_owner()
            && FhUtil.get_at<byte>(EngineAddresses.BattleState) == 0;
    }

    private void native_field_abort(string reason)
    {
        refuse_independent_presentation(reason);
        Environment.FailFast($"Native field diagnostic stopped before unsafe native forwarding: {reason}");
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint native_field_foreground_window();
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint native_field_window_process(nint window, out uint process);
}
