namespace Fahrenheit.Mods.FpsUnlock;

/// <summary>
///     Loose files that win over the VBF, without touching the 20.7 GB archive.
///
///     The engine already loads loose files. Its fios open resolves a path through
///     fiosUnifyFilename and a plain tolower, asks <c>BigFileStream::openFile</c> first, and only
///     when that returns null does it call <c>CreateFileW</c> on the same lowercased relative path
///     - keeping the two results side by side as <c>handle_vbf</c> and <c>handle_os</c>. The
///     existence probe has the same shape around <c>checkExists</c>. So an override needs no
///     archive surgery and no patched fall-through: it needs the archive to say no.
///
///     That is all this does. Both hooks answer "not in the archive" for a path the override tree
///     carries, and the engine's own fallback opens the file. Nothing here fabricates a VFile,
///     which is why the override file has to sit at exactly the relative path the engine asks for
///     rather than in a folder of its own: <c>CreateFileW</c> receives that path unchanged and
///     resolves it against the process working directory, which is the game directory.
///
///     <para>The hooks are not installed when there is nothing to override.</para> They sit on the
///     path every single file open takes, so a run with an empty override tree pays nothing at all
///     rather than paying a detour per file. When there is something, the cost is a dictionary
///     lookup on an already-lowercased string.
///
///     <para>Both targets are measured thiscall.</para> 412 and 204 bytes, each ending in
///     <c>ret 4</c> - one stack argument, <c>this</c> in ecx - and each opening with the security
///     cookie prologue. The catalog calls checkExists <c>__stdcall</c> with one parameter, which is
///     its blanket default and wrong in the convention.
/// </summary>
public unsafe sealed partial class FpsUnlockModule
{
    /// <summary>
    ///     Relative path as the engine asks for it, lowercased, to the absolute file that answers
    ///     it. Built once at init and never mutated, so the hooks need no lock.
    /// </summary>
    private readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal);

    private long _override_probes;
    private long _override_hits;

    /// <summary>
    ///     Files in the override tree, then probes seen and probes answered. A file count above
    ///     zero with no probes means the engine never asked for that path - almost always a
    ///     misspelled directory rather than a failed hook.
    /// </summary>
    private string asset_override_counts()
        => $"override={_overrides.Count}/{_override_probes}/{_override_hits}";

    /// <summary>
    ///     Normalises a path the way the two hooks receive it. The engine has already lowercased it
    ///     and turned backslashes into forward slashes by this point; what it has not done is strip
    ///     the prefix its callers use, and the shapes are the ones the FFX HD file loader
    ///     established - a leading <c>../../../</c>, a leading <c>../../..</c>, or a bare slash.
    /// </summary>
    private static string normalise(string path)
    {
        if (path.StartsWith("../../../", StringComparison.Ordinal)) return path[9..];
        if (path.StartsWith("../../..",  StringComparison.Ordinal)) return path[8..];
        if (path.Length > 0 && path[0] == '/')                      return path[1..];
        return path;
    }

    private bool init_asset_override()
    {
        if (!_config.AssetOverride) return true;

        string root = Path.GetFullPath(Directory.GetCurrentDirectory());

        foreach (string relative in _config.AssetOverrideRoots)
        {
            string dir = Path.GetFullPath(Path.Combine(root, relative));
            if (!Directory.Exists(dir)) continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                // The key is what the engine will ask for: relative to the game directory, lower
                // case, forward slashes - so ffx_data/gamedata/... , which is the only shape the
                // engine's own CreateFileW fallback can find again. The roots are subtrees of that
                // directory rather than the directory itself, which is what keeps FFX.exe, the
                // fahrenheit tree and the archives out of the index.
                string key = Path.GetRelativePath(root, file).Replace('\\', '/').ToLowerInvariant();
                _overrides.TryAdd(key, file);
            }
        }

        if (_overrides.Count == 0)
        {
            _logger.Info("[FpsUnlock] Asset override: nothing found, hooks not installed.");
            return true;
        }

        foreach ((string key, string file) in _overrides.OrderBy(e => e.Key))
            _logger.Info($"[FpsUnlock] Asset override: {key} -> {file} ({new FileInfo(file).Length:N0} bytes)");

        bool ok = hook_or_log("BigFileStream::checkExists", EngineAddresses.BigFileCheckExists,
            () => new FhMethodHandle<d_big_file_check_exists>(new FhMethodLocation(EngineAddresses.BigFileCheckExists, 0))
                .hook(this, h_big_file_check_exists));

        ok &= hook_or_log("BigFileStream::openFile", EngineAddresses.BigFileOpenFile,
            () => new FhMethodHandle<d_big_file_open_file>(new FhMethodLocation(EngineAddresses.BigFileOpenFile, 0))
                .hook(this, h_big_file_open_file));

        return ok;
    }

    /* Measured thiscall on both: each ends in ret 4, so the callee pops the one stack argument and
     * this arrives in ecx. Declared with an explicit this parameter because that is how the
     * framework expresses a thiscall here. */
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate byte d_big_file_check_exists(nint ptr_this, nint path);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate nint d_big_file_open_file(nint ptr_this, nint path);

    /// <summary>
    ///     True when this path is one the override tree answers. Counted separately from the hit so
    ///     a log can tell "the hook never fired" from "the hook fired and matched nothing".
    /// </summary>
    private bool overridden(nint path)
    {
        _override_probes++;

        if (path == 0) return false;

        string? raw = Marshal.PtrToStringAnsi(path);
        if (string.IsNullOrEmpty(raw)) return false;

        if (!_overrides.ContainsKey(normalise(raw))) return false;

        _override_hits++;
        return true;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvThiscall)])]
    private byte h_big_file_check_exists(nint ptr_this, nint path)
    {
        // Zero means "the archive does not have it", which is what sends the caller to CreateFileW.
        if (overridden(path)) return 0;

        return new FhMethodHandle<d_big_file_check_exists>(new FhMethodLocation(EngineAddresses.BigFileCheckExists, 0))
            .chain_from(h_big_file_check_exists).fnptr!(ptr_this, path);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvThiscall)])]
    private nint h_big_file_open_file(nint ptr_this, nint path)
    {
        // Null is the miss the fios open tests for before it falls through to the filesystem. The
        // caller stores it as handle_vbf either way, so returning it costs nothing to unwind.
        if (overridden(path)) return 0;

        return new FhMethodHandle<d_big_file_open_file>(new FhMethodLocation(EngineAddresses.BigFileOpenFile, 0))
            .chain_from(h_big_file_open_file).fnptr!(ptr_this, path);
    }
}
