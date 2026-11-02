namespace Fahrenheit.Mods.Vrr;

internal sealed class NativeFieldTraceWindow
{
    private long _firstImage;

    internal bool ShouldRecord(long image, bool admitted)
    {
        if (!admitted || image <= 0) return false;
        long first = System.Threading.Interlocked.CompareExchange(ref _firstImage, image, 0);
        if (first == 0) first = image;
        return image >= first && image - first < 1800;
    }
}

internal static class NativeFieldTiming
{
    internal const nint LimiterBody = 0x22ee50;
    internal const int LimiterBodyLength = 1606;

    internal static bool TryGetStepSeconds(double vblankSeconds, out double stepSeconds)
    {
        stepSeconds = 0;
        if (!double.IsFinite(vblankSeconds) || vblankSeconds <= 0
            || Math.Abs(1 / vblankSeconds - 59.94005994) > 0.01) return false;
        stepSeconds = 2 * vblankSeconds;
        return true;
    }

    internal static bool TryFindLimiterOperand(ReadOnlySpan<byte> body, out uint address)
    {
        ReadOnlySpan<byte> pattern = [0xdd,0x9d,0,0,0,0,0xdd,0x85,0,0,0,0,0xdc,0x0d,0,0,0,0,
            0xd9,0xc1,0xdc,0x25,0,0,0,0,0xd8,0xe1,0xd8,0xd1,0xdf,0xe0,0xf6,0xc4,0x41];
        address = 0;
        int matches = 0;
        for (int i = 0; i <= body.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (j is >= 2 and <= 5 or >= 8 and <= 11 or >= 14 and <= 17 or >= 22 and <= 25) continue;
                if (body[i + j] != pattern[j]) { match = false; break; }
            }
            if (!match) continue;
            matches++;
            address = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body[(i + 14)..]);
        }
        if (matches == 1 && address != 0) return true;
        address = 0;
        return false;
    }
}

internal sealed class NativeFieldReferencePasses(double stepSeconds) : IDisposable
{
    private sealed class State(double step)
    {
        internal readonly FixedSimulationClock Clock = new(step);
        internal readonly Stack<long> Invocations = new();
    }

    private readonly System.Threading.ThreadLocal<State> _states = new(() => new(stepSeconds));
    private long _serial;

    internal (long Serial, int Depth, SimulationFramePlan? Model) Enter(double now)
    {
        State state = _states.Value!;
        long serial = System.Threading.Interlocked.Increment(ref _serial);
        state.Invocations.Push(serial);
        return (serial, state.Invocations.Count, state.Invocations.Count == 1 ? state.Clock.Plan(now) : null);
    }

    internal long? Exit() => _states.Value!.Invocations.TryPop(out long serial) ? serial : null;
    public void Dispose() => _states.Dispose();
}
