using System;

namespace Fahrenheit.Events;

internal sealed class AdditionalImagePhase
{
    private object? _owner;
    private Func<float, bool>? _classify;

    internal bool TryBind(object owner, Func<float, bool> classify)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(classify);
        if (System.Threading.Interlocked.CompareExchange(ref _owner, owner, null) is not null) return false;
        _classify = classify;
        return true;
    }

    internal bool IsAdditionalImage(float delta) => _classify?.Invoke(delta) ?? false;
}
