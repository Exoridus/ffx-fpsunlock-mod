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

    private bool init_survey_hooks()
    {
        if (!_config.SurveyTextureAnimation) return true;

        // Failures are not fatal here: a probe that will not attach is itself a result.
        new FhMethodHandle<d_texanim_draw>(new FhMethodLocation(EngineAddresses.ChrTexAnimDraw, 0)).hook(this, h_texanim_draw);
        new FhMethodHandle<d_screen_texanim_draw>(new FhMethodLocation(EngineAddresses.ScreenTextureAnimationDraw, 0)).hook(this, h_screen_texanim_draw);
        new FhMethodHandle<d_uv_scroll>(new FhMethodLocation(EngineAddresses.SetMaterialUVScroll, 0)).hook(this, h_uv_scroll);
        new FhMethodHandle<d_texanim_set_enable>(new FhMethodLocation(EngineAddresses.ChTextureAnimSetEnable, 0)).hook(this, h_texanim_set_enable);

        _logger.Info("[Fps60] Texture animation survey armed; counts appear in the telemetry line.");
        return true;
    }

    private string survey_counts()
        => $"texanim_draw={_texanim_draw} screen_texanim={_screen_texanim_draw} " +
           $"uv_scroll={_uv_scroll} texanim_enable={_texanim_set_enable}";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int d_texanim_draw(int index, int arg2);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_screen_texanim_draw();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_uv_scroll(nint arg1, float arg2, float arg3);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_texanim_set_enable(uint arg1, uint arg2);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private int h_texanim_draw(int index, int arg2)
    {
        _texanim_draw++;
        return new FhMethodHandle<d_texanim_draw>(new FhMethodLocation(EngineAddresses.ChrTexAnimDraw, 0))
            .chain_from(h_texanim_draw).fnptr!(index, arg2);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_screen_texanim_draw()
    {
        _screen_texanim_draw++;
        new FhMethodHandle<d_screen_texanim_draw>(new FhMethodLocation(EngineAddresses.ScreenTextureAnimationDraw, 0))
            .chain_from(h_screen_texanim_draw).fnptr!();
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_uv_scroll(nint arg1, float arg2, float arg3)
    {
        _uv_scroll++;
        new FhMethodHandle<d_uv_scroll>(new FhMethodLocation(EngineAddresses.SetMaterialUVScroll, 0))
            .chain_from(h_uv_scroll).fnptr!(arg1, arg2, arg3);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_texanim_set_enable(uint arg1, uint arg2)
    {
        _texanim_set_enable++;
        new FhMethodHandle<d_texanim_set_enable>(new FhMethodLocation(EngineAddresses.ChTextureAnimSetEnable, 0))
            .chain_from(h_texanim_set_enable).fnptr!(arg1, arg2);
    }
}
