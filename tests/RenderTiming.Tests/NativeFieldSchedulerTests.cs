using Xunit;

namespace Fahrenheit.Mods.Vrr;

[Collection("Render scheduler")]
public sealed class NativeFieldSchedulerTests
{
    [Theory]
    [InlineData(0.01, true, false)]
    [InlineData(0.01, true, true)]
    [InlineData(0.03336666666666667, false, false)]
    public void Additional_memory_samples_bracket_the_native_call_without_changing_its_result(double now, bool additional, bool throws)
    {
        var module = new VrrModule().EnableAdapter();
        module.PrepareNativeField(now);
        var failure = new InvalidOperationException("native cloth observation fixture");
        NativeClock.LoopAction = delta =>
        {
            Assert.Equal(additional, module.NativeFieldHostClassify());
            module.NativeFieldClock(delta);
            module.AdditionalMemorySamples.Add("native");
            if (throws) throw failure;
        };
        try
        {
            if (throws) Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => module.CallDiagnosticLoop(1f / 120)));
            else module.CallDiagnosticLoop(1f / 120);
        }
        finally { NativeClock.LoopAction = null; VrrModule.ClearDiagnosticLoop(); }
        Assert.Equal(additional ? new[] { "before", "native", "after" } : new[] { "native" }, module.AdditionalMemorySamples);
        Assert.Equal(0, module.CurrentDiagnosticLoop);
    }
    [Theory]
    [InlineData(0f)]
    [InlineData(.125f)]
    public void Native_field_unadmitted_loop_keeps_its_original_delta(float incoming)
    {
        var module = new VrrModule().EnableAdapter();
        module.PrepareNativeField(0.03336666666666667);
        module.SetHookScope(true, false, true, false);
        float? forwarded = null;
        NativeClock.LoopAction = delta =>
        {
            forwarded = delta;
            Assert.False(module.NativeFieldHostClassify());
            module.NativeFieldClock(delta);
        };
        try { module.CallDiagnosticLoop(incoming); }
        finally { NativeClock.LoopAction = null; VrrModule.ClearDiagnosticLoop(); }
        Assert.Equal(incoming, forwarded);
        Assert.Null(module.CoopFailure);
    }

    [Fact]
    public void Native_field_loop_does_not_hide_invalid_zero_delta_on_an_owned_real_pass()
    {
        var module = new VrrModule().EnableAdapter();
        module.PrepareNativeField(0.03336666666666667);
        NativeClock.LoopAction = delta =>
        {
            Assert.Equal(0, delta);
            Assert.False(module.NativeFieldHostClassify());
            module.NativeFieldClock(delta);
        };
        try { module.CallDiagnosticLoop(0); }
        finally { NativeClock.LoopAction = null; VrrModule.ClearDiagnosticLoop(); }
        Assert.Equal("native_field_speed_unsupported", module.CoopFailure);
    }

    [Theory]
    [InlineData(0.03336666666666667, false)]
    [InlineData(0.01, true)]
    public void Native_field_loop_gives_gameplay_events_the_native_period_and_preserves_extra_image_delta(double now, bool additional)
    {
        var module = new VrrModule().EnableAdapter();
        module.PrepareNativeField(now);
        float? forwarded = null;
        NativeClock.LoopAction = delta =>
        {
            forwarded = delta;
            Assert.Equal(additional, module.NativeFieldHostClassify());
            module.NativeFieldClock(delta);
        };
        try { module.CallDiagnosticLoop(1f / 120); }
        finally { NativeClock.LoopAction = null; VrrModule.ClearDiagnosticLoop(); }
        Assert.Equal(additional ? 1f / 120 : (float)0.03336666666666667, forwarded);
        Assert.Null(module.CoopFailure);
    }

    [Theory]
    [InlineData(false, true, true, "native_field_capture_inactive")]
    [InlineData(true, false, true, "native_field_host_unbound")]
    [InlineData(true, true, false, "native_field_environment_unready")]
    public void Native_field_status_names_the_boundary_that_blocks_admission(bool capture, bool host, bool environment, string reason)
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.03336666666666667);
        module.CaptureLoops(capture);
        module.NativeFieldHostBindingForTest(host);
        module.NativeFieldEnvironmentReady = environment;
        Assert.False(module.NativeValidationFrameEligibleForTest());
        Assert.Equal(reason, module.InputGateReason);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f / 15)]
    public void Native_owned_startup_or_speed_does_not_permanently_refuse_the_field_probe(float delta)
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.03336666666666667);
        module.SetHookScope(true, false, true, false);
        Assert.False(module.NativeFieldHostClassify());
        module.NativeFieldClock(delta);
        Assert.Null(module.CoopFailure);
    }

    [Fact]
    public void Invalid_speed_on_an_owned_simulation_step_still_refuses_admission()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.03336666666666667);
        Assert.False(module.NativeFieldHostClassify());
        module.NativeFieldClock(0);
        Assert.Equal("native_field_speed_unsupported", module.CoopFailure);
    }

    [Fact]
    public void Invalid_speed_after_an_additional_phase_never_forwards_native_clock()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.01);
        Assert.True(module.NativeFieldHostClassify());
        int calls = 0;
        NativeClock.Advance = () => calls++;
        try { Assert.Throws<InvalidOperationException>(() => module.NativeFieldClock(0)); }
        finally { NativeClock.Advance = null; }
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Late_menu_owner_cannot_publish_history_on_an_additional_pass()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.01);
        int before = NativeClock.PadCalls;
        FhUtil.MenuPause = 1;
        try { Assert.Throws<InvalidOperationException>(() => module.CallPad()); }
        finally { FhUtil.MenuPause = 0; }
        Assert.Equal(before, NativeClock.PadCalls);
    }

    [Fact]
    public void Deferred_latch_cannot_be_flushed_by_additional_pass_cleanup()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.01);
        module.NativeFieldPendingLatch();
        int before = NativeClock.PadCalls;
        Assert.Throws<InvalidOperationException>(() => module.EndPad());
        Assert.Equal(before, NativeClock.PadCalls);
    }

    [Fact]
    public void Additional_pass_does_not_poll_or_execute_a_disconnect_history_reset()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.01);
        int historyResets = 0;
        NativeClock.PollAction = () => historyResets++;
        try { module.CallPadPortPoll(0, 0, 0); }
        finally { NativeClock.PollAction = null; }
        Assert.Equal(0, historyResets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_or_duplicate_snapshot_never_forwards_on_a_frozen_additional_pass(bool duplicate)
    {
        NativeSnapshotFixture.Reset();
        var module = new VrrModule();
        module.PrepareNativeField(0.01);
        module.NativeFieldSnapshotProducerState(duplicate);
        int before = NativeClock.SnapshotCalls;
        Assert.Throws<InvalidOperationException>(() => module.CallSnapshotCore());
        Assert.Equal(before, NativeClock.SnapshotCalls);
    }

    [Fact]
    public void Additional_phase_is_selected_before_the_host_and_clock_does_not_publish_input()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.01);
        Assert.True(module.NativeFieldPresentation);
        Assert.True(module.NativeFieldHostClassify());
        int calls = 0;
        NativeClock.Advance = () => calls++;
        try { module.NativeFieldClock(1f / 30); }
        finally { NativeClock.Advance = null; }
        Assert.Equal(0, calls);
        Assert.True(module.NativeFieldPresentation);
    }

    [Fact]
    public void Unknown_host_order_uses_native_events_instead_of_guessing_an_additional_pass()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.01, hostVerified: false);
        Assert.False(module.NativeFieldPresentation);
        Assert.False(module.NativeFieldHostClassify());
    }

    [Fact]
    public void Owner_loss_after_suppressed_host_events_never_falls_through_to_native_clock()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.01);
        Assert.True(module.NativeFieldHostClassify());
        module.NativeFieldEnvironmentReady = false;
        int calls = 0;
        NativeClock.Advance = () => calls++;
        try { Assert.Throws<InvalidOperationException>(() => module.NativeFieldClock(1f / 30)); }
        finally { NativeClock.Advance = null; }
        Assert.Equal(0, calls);
        Assert.True(module.NativeFieldPresentation);
    }

    [Fact]
    public void Native_step_advances_once_without_historical_input_cooperation()
    {
        var module = new VrrModule();
        module.PrepareNativeField(0.03336666666666667);
        Assert.False(module.NativeFieldHostClassify());
        int calls = 0;
        NativeClock.Advance = () => calls++;
        try { module.NativeFieldClock(1f / 30); }
        finally { NativeClock.Advance = null; }
        Assert.Equal(1, calls);
        Assert.False(module.NativeFieldPresentation);
    }
}
