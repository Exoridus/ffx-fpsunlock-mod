namespace Fahrenheit.Mods.FpsUnlock;

/// <summary>
///     Replacing an FMV without touching the 20.7 GB archive, and without a detour on the file
///     layer.
///
///     <para>The path is data, not a literal.</para> PhyFMVPlayerManager keeps the full path of the
///     clip it is about to play in its own buffer at instance+0x4F0 - prefix and authored case
///     intact, <c>../../../FFX_Data/GameData/PS3Data/Video/OPL_us.webm</c> - and the playback entry
///     reads it from there. So one hook on that entry, which runs once per video, does what two
///     hooks on <c>BigFileStream::checkExists</c> and <c>openFile</c> did while answering for every
///     file the engine opens. Those two are what <see cref="FpsUnlockConfig.AssetOverride"/> still
///     installs and what crashed the process twice.
///
///     <para>The replacement is written as an absolute path, and that is the whole trick.</para>
///     It settles both unknowns at once. The archive hashes the path from
///     <c>stream_prefix_strlen</c> bytes in, so an absolute path is hashed from its ninth character
///     and cannot match any digest in the index - the archive misses by construction, with nothing
///     patched and nothing hooked. The engine's fallback then calls <c>CreateFileW</c> on that same
///     string, and an absolute path resolves the same whatever the working directory happens to be
///     at that moment, which a relative one does not: the launcher leaves the process in
///     <c>fahrenheit\bin</c> and nothing in the engine sets it back.
/// </summary>
public unsafe sealed partial class FpsUnlockModule
{
    private long _fmv_starts;
    private long _fmv_rewrites;

    private string video_override_counts()
        => $"fmvpath={_fmv_starts}/{_fmv_rewrites}";

    /// <summary>
    ///     The engine reads this buffer through fiosUnifyFilename with a 256 byte bound, so that is
    ///     the limit a replacement has to respect; the fields around it start 0x1E0 bytes further on.
    /// </summary>
    private const int FmvPathCapacity = 256;

    private bool init_video_override()
    {
        if (!_config.VideoOverride || _overrides.Count == 0) return true;

        return hook_or_log("PhyFMVPlayerManager play start", EngineAddresses.FmvPlayStart,
            () => new FhMethodHandle<d_fmv_play_start>(new FhMethodLocation(EngineAddresses.FmvPlayStart, 0))
                .hook(this, h_fmv_play_start));
    }

    /* Measured thiscall: the manager arrives in ecx and the one argument on the stack. */
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int d_fmv_play_start(nint manager, int arg);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvThiscall)])]
    private int h_fmv_play_start(nint manager, int arg)
    {
        try
        {
            rewrite_fmv_path(manager);
        }
        catch (Exception e)
        {
            // A failed rewrite must never cost the video. The original path is still in the buffer.
            _logger.Info($"[FpsUnlock] FMV path rewrite skipped: {e.Message}");
        }

        return new FhMethodHandle<d_fmv_play_start>(new FhMethodLocation(EngineAddresses.FmvPlayStart, 0))
            .chain_from(h_fmv_play_start).fnptr!(manager, arg);
    }

    private void rewrite_fmv_path(nint manager)
    {
        if (manager <= 0x10000 || manager >= 0x7FFF0000) return;

        nint buffer = manager + EngineAddresses.FmvPathOffset;

        string? raw = Marshal.PtrToStringAnsi(buffer);
        if (string.IsNullOrEmpty(raw)) return;

        _fmv_starts++;

        if (!_overrides.TryGetValue(normalise(raw), out string? replacement))
        {
            _logger.Info($"[FpsUnlock] FMV path: {raw} (no override)");
            return;
        }

        /* Forward slashes throughout: CreateFileW takes them, and it keeps the string in the shape
         * the rest of the path handling expects to see. */
        string absolute = replacement.Replace('\\', '/');

        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(absolute);

        if (bytes.Length + 1 > FmvPathCapacity)
        {
            _logger.Info($"[FpsUnlock] FMV path: {absolute} is {bytes.Length} bytes and does not fit, left alone.");
            return;
        }

        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        *(byte*)(buffer + bytes.Length) = 0;

        _fmv_rewrites++;
        _logger.Info($"[FpsUnlock] FMV path: {raw} -> {absolute}");
    }
}
