namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Systems that play a sequence of prepared images, one per frame, and carry no notion of a
///     duration to scale. They can only be held: the same image is presented for as many frames as
///     the framerate multiplier demands.
///
///     This is frame skipping, and it means these subsystems remain 30 Hz inside a 60 Hz game. It is
///     a deliberate, temporary exception, taken because the alternative is a visibly wrong playback
///     speed. The real fix is content that matches the target framerate.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    /// <summary>
    ///     Number of presented frames each held frame must survive. 2 at 60 Hz, 1 at 30 Hz, which
    ///     disables the hold entirely.
    /// </summary>
    private int HoldFrames => Math.Max(1, (int)Math.Round(Scale));

    /// <summary>True on the frames a held sequence is allowed to advance.</summary>
    private bool advance_this_frame() => _frames % HoldFrames == 0;

    private bool init_frame_sequence_hooks()
    {
        bool ok = true;

        if (_config.MenuWater)
        {
            ok &= hook_or_log("graphicDrawMainMenuWaterEffect", EngineAddresses.GraphicDrawMainMenuWaterEffect,
                () => new FhMethodHandle<d_menu_water>(new FhMethodLocation(EngineAddresses.GraphicDrawMainMenuWaterEffect, 0)).hook(this, h_menu_water));
        }

        if (_config.Fmv)
        {
            ok &= hook_or_log("graphicVideoUpdate", EngineAddresses.GraphicVideoUpdate,
                () => new FhMethodHandle<d_video_update>(new FhMethodLocation(EngineAddresses.GraphicVideoUpdate, 0)).hook(this, h_video_update));
        }

        if (_config.TextureAnimation)
        {
            ok &= hook_or_log("old-format texture animation advance", EngineAddresses.ChrTexAnimAdvanceOld,
                () => new FhMethodHandle<d_texanim_advance_old>(new FhMethodLocation(EngineAddresses.ChrTexAnimAdvanceOld, 0)).hook(this, h_texanim_advance_old));
        }

        return ok;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_menu_water();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void d_video_update();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_texanim_advance_old(int slot);

    /* The main menu water is 69 images composited one per frame, with the current index in a byte.
     * The draw itself advances the index, so the hold is applied afterwards by winding it back on
     * the frames that are not allowed to advance. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_menu_water()
    {
        byte before = FhUtil.get_at<byte>(EngineAddresses.MenuWaterFrame);

        new FhMethodHandle<d_menu_water>(new FhMethodLocation(EngineAddresses.GraphicDrawMainMenuWaterEffect, 0))
            .chain_from(h_menu_water).fnptr!();

        if (!advance_this_frame()) FhUtil.set_at(EngineAddresses.MenuWaterFrame, before);
    }

    /* The video update loop multiplies its time step by a hardcoded 29.97 and ignores the timer it
     * keeps, so the only lever on a 29.97 source is how often the update runs at all.
     *
     * The skip belongs here rather than inside the FMV manager. Holding the manager's own texture
     * update leaves it mid-state and faults during playback, which is what an earlier build did.
     *
     * This is the hook that has to go once real 60 FPS video ships: with a 59.94 asset the update
     * belongs on every frame, and holding it would halve the video's own framerate. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private void h_video_update()
    {
        if (!advance_this_frame()) return;

        new FhMethodHandle<d_video_update>(new FhMethodLocation(EngineAddresses.GraphicVideoUpdate, 0))
            .chain_from(h_video_update).fnptr!();
    }

    /* The engine carries two texture animation formats side by side and picks one per character on
     * byte 2 of the descriptor. The new format's advance takes its step from Ch_TextureSetAnimTimer,
     * which this module already retimes; this is the old one, and its counters are literal - the
     * sprite step is +1 per call and the blink countdown is rand() % 0x5a + 0x3c ticked down by one.
     * Nothing in it can be scaled, so the call itself is held instead.
     *
     * 445 of 872 character model directories ship without the new format's asset, so this is not a
     * rare path. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private void h_texanim_advance_old(int slot)
    {
        if (!advance_this_frame()) return;

        new FhMethodHandle<d_texanim_advance_old>(new FhMethodLocation(EngineAddresses.ChrTexAnimAdvanceOld, 0))
            .chain_from(h_texanim_advance_old).fnptr?.Invoke(slot);
    }
}
