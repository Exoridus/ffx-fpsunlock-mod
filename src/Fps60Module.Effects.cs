namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Battle and event effects - the magic, the weapon elements, the cutscene set pieces.
///
///     They do not live in FFX.exe. MsEffectProcess dispatches through the magic overlay's own
///     table, +0xc to advance and +0x10 to draw, so the effect's timeline is inside the DLL and no
///     amount of retiming in the executable reaches it.
///
///     What the executable does control is how often the advance is called, and there the engine is
///     unconditional: the simulation loop runs MsEffectProcess(0) on every iteration, and the
///     branch below it runs MsEffectProcess(0) once more when the frame ran no simulation step at
///     all. Either way the advance happens once per presented frame, which at 60 Hz is twice the
///     authored rate.
///
///     Holding the advance is therefore the whole fix, and unlike the particle experiment it cannot
///     flicker: mode 1 is never touched, so every frame still draws.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const int EffectAdvanceMode = 0;

    private long _effect_advances;
    private long _effect_draws;
    private long _effect_held;

    private string effect_counts()
        => $"eff_adv={_effect_advances} eff_draw={_effect_draws} eff_held={_effect_held}";

    private bool init_effect_hooks()
    {
        if (!_config.EffectHold && !_config.EffectProbe) return true;

        return hook_or_log("MsEffectProcess", EngineAddresses.MsEffectProcess,
            () => new FhMethodHandle<d_effect_process>(new FhMethodLocation(EngineAddresses.MsEffectProcess, 0))
                .hook(this, h_effect_process));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void d_effect_process(int mode);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private void h_effect_process(int mode)
    {
        if (mode == EffectAdvanceMode) _effect_advances++;
        else                           _effect_draws++;

        if (_config.EffectHold && mode == EffectAdvanceMode && !advance_this_frame())
        {
            _effect_held++;
            return;
        }

        new FhMethodHandle<d_effect_process>(new FhMethodLocation(EngineAddresses.MsEffectProcess, 0))
            .chain_from(h_effect_process).fnptr?.Invoke(mode);
    }
}
