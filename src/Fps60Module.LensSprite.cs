namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     The sprite frame clock of the lens and flare family, which is the one place in the particle
///     system where a rate can be halved exactly instead of a call being skipped.
///
///     KeLnsShp_Update advances an accumulator by a fixed 0x200 per call and compares it against the
///     current sprite frame's own duration, carrying the remainder across frames. Its four callers
///     are the four Lns draw kernels, so it runs once per drawn frame and doubles at 60 Hz - this is
///     the rhythm of a flare or a background flash, and no hold anywhere else reaches it.
///
///     The four Lns*Update kernels do not: their entire body is the keyframe one-shot that resolves
///     the shape pointer and seeds this accumulator. Holding them retimes nothing and costs the
///     keyframe, which is why they stay excluded.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    /// <summary>What the engine adds per call, and therefore what has to be given back.</summary>
    private const short LensSpriteStep = 0x200;

    private long _lens_sprite_calls;

    private string lens_sprite_counts() => $"lens_clock={_lens_sprite_calls}";

    private bool init_lens_sprite_hook()
    {
        if (!_config.LensSpriteClock) return true;

        return hook_or_log("KeLnsShp_Update", EngineAddresses.KeLnsShpUpdate,
            () => new FhMethodHandle<d_lens_shape_update>(new FhMethodLocation(EngineAddresses.KeLnsShpUpdate, 0))
                .hook(this, h_lens_shape_update));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_lens_shape_update(nint work);

    /* Gives back part of the step before the original adds it, which turns +0x200 per call into
     * +0x200/Scale and leaves everything else in the function untouched: the frame index, the
     * remainder carry, the loop and hold-last handling on the last frame.
     *
     * This is a scale rather than a skip, so no sprite frame is dropped however short its duration.
     * The accumulator is the first field of the block param_1[1] points at, and the pointer
     * indirection is the engine's own - get_ptr32_func is the identity on PC. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_lens_shape_update(nint work)
    {
        _lens_sprite_calls++;

        if (work != 0 && Scale > 1f)
        {
            short* accumulator = *(short**)(work + 4);

            // The block is engine-allocated and the pointer is written by the Lns update kernels on
            // the keyframe tick; before that it is null and the draw skips the whole sprite.
            if (accumulator != null)
                *accumulator -= (short)(LensSpriteStep - LensSpriteStep / Scale);
        }

        new FhMethodHandle<d_lens_shape_update>(new FhMethodLocation(EngineAddresses.KeLnsShpUpdate, 0))
            .chain_from(h_lens_shape_update).fnptr?.Invoke(work);
    }
}
