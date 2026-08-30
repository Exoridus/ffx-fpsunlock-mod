namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Call counters on the candidates for the two systems that still run at double speed. They
///     change no behaviour; they answer which function is actually on the live path, which name
///     matching alone has already got wrong twice in this module's history.
///
///     Read the counts from the telemetry line while the effect in question is on screen: an
///     animated weapon texture, or a campfire.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private long _texanim_draw;
    private long _screen_texanim_draw;
    private long _uv_scroll;
    private long _texanim_set_enable;
    private long _texanim_advance;

    /// <summary>
    ///     Calls that reached the old format's advance. Counted in the hold that hooks it rather than
    ///     by reading the format byte, which is not a readable global of this image.
    /// </summary>
    private long _texanim_advance_old_path;

    private bool init_survey_hooks()
    {
        if (!_config.SurveyTextureAnimation) return true;

        // Failures are not fatal here: a probe that will not attach is itself a result. But that
        // result has to be visible - a count of zero used to be indistinguishable from a probe that
        // never installed, and one of them was exactly that. The install outcome is logged per
        // probe, and a chain that does not resolve leaves fnptr null, so a counting probe can never
        // be the thing that ends the process.
        probe("chr_texanim_draw", EngineAddresses.ChrTexAnimDraw,
            () => new FhMethodHandle<d_texanim_draw>(new FhMethodLocation(EngineAddresses.ChrTexAnimDraw, 0)).hook(this, h_texanim_draw));
        probe("screenTextureAnimationDraw", EngineAddresses.ScreenTextureAnimationDraw,
            () => new FhMethodHandle<d_screen_texanim_draw>(new FhMethodLocation(EngineAddresses.ScreenTextureAnimationDraw, 0)).hook(this, h_screen_texanim_draw));
        probe("setMaterialUVScroll", EngineAddresses.SetMaterialUVScroll,
            () => new FhMethodHandle<d_uv_scroll>(new FhMethodLocation(EngineAddresses.SetMaterialUVScroll, 0)).hook(this, h_uv_scroll));
        probe("Ch_TextureAnimSetEnable", EngineAddresses.ChTextureAnimSetEnable,
            () => new FhMethodHandle<d_texanim_set_enable>(new FhMethodLocation(EngineAddresses.ChTextureAnimSetEnable, 0)).hook(this, h_texanim_set_enable));
        probe("texture animation advance dispatcher", EngineAddresses.ChrTexAnimAdvance,
            () => new FhMethodHandle<d_texanim_advance>(new FhMethodLocation(EngineAddresses.ChrTexAnimAdvance, 0)).hook(this, h_texanim_advance));

        _logger.Info("[Fps60] Texture animation survey armed; counts appear in the telemetry line.");
        return true;
    }

    private void probe(string name, nint rva, Func<bool> install)
        => _logger.Info(install()
            ? $"[Fps60] Probe attached: {name} at RVA 0x{rva:X}."
            : $"[Fps60] Probe did NOT attach: {name} at RVA 0x{rva:X}; its count stays 0 for that reason.");

    private string survey_counts()
        => $"texanim_draw={_texanim_draw} screen_texanim={_screen_texanim_draw} " +
           $"uv_scroll={_uv_scroll} texanim_enable={_texanim_set_enable} " +
           $"advance={_texanim_advance} advance_old={_texanim_advance_old_path} " +
           $"advance_new={_texanim_advance - _texanim_advance_old_path}";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int d_texanim_draw(int index, int arg2);

    // Three int parameters, read off the decompiled body at 0x0090bdb0: it indexes a table with
    // param_2 * 0x720 + param_1 + param_3 * 0x1c and formats a texture id from what it finds. The
    // probe used to declare it with none, so chaining called the original with three garbage stack
    // values - which is where the boot-video crashes came from.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_screen_texanim_draw(int table, int bank, int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_uv_scroll(nint arg1, float arg2, float arg3);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_texanim_set_enable(uint arg1, uint arg2);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private int h_texanim_draw(int index, int arg2)
    {
        _texanim_draw++;
        var orig = new FhMethodHandle<d_texanim_draw>(new FhMethodLocation(EngineAddresses.ChrTexAnimDraw, 0))
            .chain_from(h_texanim_draw).fnptr;
        return orig is null ? 0 : orig(index, arg2);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_screen_texanim_draw(int table, int bank, int index)
    {
        _screen_texanim_draw++;
        new FhMethodHandle<d_screen_texanim_draw>(new FhMethodLocation(EngineAddresses.ScreenTextureAnimationDraw, 0))
            .chain_from(h_screen_texanim_draw).fnptr?.Invoke(table, bank, index);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_uv_scroll(nint arg1, float arg2, float arg3)
    {
        _uv_scroll++;
        new FhMethodHandle<d_uv_scroll>(new FhMethodLocation(EngineAddresses.SetMaterialUVScroll, 0))
            .chain_from(h_uv_scroll).fnptr?.Invoke(arg1, arg2, arg3);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_texanim_set_enable(uint arg1, uint arg2)
    {
        _texanim_set_enable++;
        new FhMethodHandle<d_texanim_set_enable>(new FhMethodLocation(EngineAddresses.ChTextureAnimSetEnable, 0))
            .chain_from(h_texanim_set_enable).fnptr?.Invoke(arg1, arg2);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_texanim_advance(int slot);

    /* Total dispatcher calls. The split by format is not read out of tex_anim_wk: the addresses the
     * decompilation shows for it (0x0231xxxx) are past the end of .data and are not a global of this
     * image at all, and a probe that read there faulted on its first call. The old path is counted
     * where it is already hooked, in the hold, so the new path is the difference. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_texanim_advance(int slot)
    {
        _texanim_advance++;

        new FhMethodHandle<d_texanim_advance>(new FhMethodLocation(EngineAddresses.ChrTexAnimAdvance, 0))
            .chain_from(h_texanim_advance).fnptr?.Invoke(slot);
    }
}
