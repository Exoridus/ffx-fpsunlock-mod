using Fahrenheit.Events;
using Xunit;

public sealed class AdditionalImagePhaseTests
{
    [Fact]
    public void Concurrent_registration_has_exactly_one_owner()
    {
        var phase = new AdditionalImagePhase();
        int owners = 0;
        Parallel.For(0, 32, _ =>
        {
            if (phase.TryBind(new object(), _ => true)) Interlocked.Increment(ref owners);
        });
        Assert.Equal(1, owners);
    }

    [Fact]
    public void Unbound_host_preserves_native_update_events()
        => Assert.False(new AdditionalImagePhase().IsAdditionalImage(0.03f));

    [Fact]
    public void One_owner_classifies_before_events_and_cannot_be_replaced()
    {
        var phase = new AdditionalImagePhase();
        bool additional = true;
        Assert.True(phase.TryBind(new object(), delta => additional && delta > 0));
        bool frozen = phase.IsAdditionalImage(0.03f);
        additional = false;
        Assert.True(frozen);
        Assert.False(phase.IsAdditionalImage(0.03f));
        Assert.False(phase.TryBind(new object(), _ => true));
    }
}
