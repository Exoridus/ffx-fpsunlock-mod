using Fahrenheit.Mods.Vrr;
using Xunit;

public sealed class NativeFieldTraceWindowTests
{
    [Fact]
    public void Native_owned_images_do_not_consume_the_later_field_capture()
    {
        var window = new NativeFieldTraceWindow();
        for (long image = 1; image <= 10000; image++)
            Assert.False(window.ShouldRecord(image, admitted: false));
        Assert.True(window.ShouldRecord(10001, admitted: true));
    }

    [Fact]
    public void Every_actor_in_the_thirty_second_field_window_retains_correlated_samples()
    {
        var window = new NativeFieldTraceWindow();
        for (long image = 100; image < 1900; image++)
            for (int actor = 0; actor < 16; actor++)
                Assert.True(window.ShouldRecord(image, admitted: true));
        Assert.False(window.ShouldRecord(1900, admitted: true));
        Assert.False(window.ShouldRecord(3000, admitted: true));
    }

    [Fact]
    public void Old_or_invalid_images_cannot_start_or_reenter_capture()
    {
        var window = new NativeFieldTraceWindow();
        Assert.False(window.ShouldRecord(0, admitted: true));
        Assert.False(window.ShouldRecord(-1, admitted: true));
        Assert.True(window.ShouldRecord(100, admitted: true));
        Assert.False(window.ShouldRecord(99, admitted: true));
    }
}

public sealed class NativeFieldTimingTests
{
    [Fact]
    public void Native_reference_pairs_nested_returns_without_advancing_the_outer_model()
    {
        using var passes = new NativeFieldReferencePasses(0.03336666666666667);
        var outer = passes.Enter(0);
        var inner = passes.Enter(0.1);
        Assert.NotEqual(outer.Serial, inner.Serial);
        Assert.Null(inner.Model);
        Assert.Equal(inner.Serial, passes.Exit());
        Assert.Equal(outer.Serial, passes.Exit());
        Assert.Null(passes.Exit());
        Assert.Equal(1, passes.Enter(0.03336666666666667).Model!.Value.Steps);
    }

    [Fact]
    public async Task Native_reference_uses_separate_thread_scopes()
    {
        using var passes = new NativeFieldReferencePasses(0.03336666666666667);
        var outer = passes.Enter(0);
        await Task.Factory.StartNew(() =>
        {
            Assert.Null(passes.Exit());
            var other = passes.Enter(1);
            Assert.Equal(1, other.Depth);
            Assert.NotEqual(outer.Serial, other.Serial);
            Assert.Equal(other.Serial, passes.Exit());
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    [Fact]
    public void Finds_one_relocated_limiter_operand_and_rejects_ambiguous_or_changed_code()
    {
        byte[] body = Convert.FromHexString("DD9D00000000DD8500000000DC0D78563412D9C1DC2500000000D8E1D8D1DFE0F6C441");
        Assert.True(NativeFieldTiming.TryFindLimiterOperand(body, out uint address));
        Assert.Equal(0x12345678u, address);
        Assert.False(NativeFieldTiming.TryFindLimiterOperand(body.Concat(body).ToArray(), out _));
        Assert.False(NativeFieldTiming.TryFindLimiterOperand(body.AsSpan(1), out _));
        body[18] = 0x90;
        Assert.False(NativeFieldTiming.TryFindLimiterOperand(body, out _));
    }

    [Fact]
    public void Native_limiter_duration_is_kept_in_seconds_without_rounding_to_thirty_hertz()
    {
        const double vblank = 0.016683333333333333;
        Assert.True(NativeFieldTiming.TryGetStepSeconds(vblank, out double step));
        Assert.Equal(vblank * 2, step);
        var clock = new FixedSimulationClock(step);
        clock.Plan(0);
        Assert.Equal(0, clock.Plan(1d / 30).Steps);
        Assert.Equal(1, clock.Plan(step).Steps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1d / 60)]
    [InlineData(1d / 30)]
    public void Refuses_a_changed_or_invalid_native_limiter(double period)
        => Assert.False(NativeFieldTiming.TryGetStepSeconds(period, out _));
}
