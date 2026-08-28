namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Records the original bytes of every in-process patch so shutdown can restore
///     the image exactly. Restoring in reverse order matters: two patches may overlap
///     when one widens an instruction the other rewrote.
/// </summary>
public sealed unsafe class PatchJournal
{
    private readonly List<(nint Address, byte[] Original)> _entries = new();

    public void Write(nint address, ReadOnlySpan<byte> replacement)
    {
        byte[] original = new byte[replacement.Length];
        new Span<byte>((void*)address, replacement.Length).CopyTo(original);

        // The engine's .text is read-only; the window is reopened for the copy only.
        if (!VirtualProtect(address, (nuint)replacement.Length, PAGE_EXECUTE_READWRITE, out uint prev))
            throw new InvalidOperationException($"VirtualProtect failed at 0x{address:X8}");

        replacement.CopyTo(new Span<byte>((void*)address, replacement.Length));
        VirtualProtect(address, (nuint)replacement.Length, prev, out _);

        _entries.Add((address, original));
    }

    public void RestoreAll()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            (nint address, byte[] original) = _entries[i];
            if (!VirtualProtect(address, (nuint)original.Length, PAGE_EXECUTE_READWRITE, out uint prev)) continue;
            original.CopyTo(new Span<byte>((void*)address, original.Length));
            VirtualProtect(address, (nuint)original.Length, prev, out _);
        }
        _entries.Clear();
    }

    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(nint lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);
}
