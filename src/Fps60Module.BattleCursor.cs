namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     The multi-target cursor blink in the battle command window.
///
///     __TODrawWaitBtlWinPrepare draws one cursor per selected target and gates that loop on
///     <c>test byte ptr [sg_count], 1</c>, so the cursors are visible on odd frames and hidden on
///     even ones. sg_count is the horizontal blank counter and it runs at the presented rate; it must
///     not be rescaled, because six of its readers select a GS double buffer off its parity and need
///     exactly the behaviour they have. So the blink is corrected where it is read rather than at the
///     counter, by moving the test to a higher bit.
///
///     A higher bit of a counter is still a fifty percent square wave, which is what makes this the
///     one correction here that loses nothing: bit 0 gives a period of two counts, bit 1 four, bit 2
///     eight, and the on-time stays half the period in every case. Bit 1 at 60 Hz is the authored
///     15 Hz with the authored duty cycle.
///
///     What the mask has to be scaled by is decided by what sg_count counts, and it does not count
///     presented frames. It is incremented once at the tail of every Sg_MainLoop pass, immediately
///     before the GS buffer swap that reads its parity, and nothing gates that increment - so a
///     frame the engine runs several simulation passes for advances it several times, and a scene
///     paced from the syncdata table advances it once per recorded PS2 frame. Passes per authored
///     30 Hz step is exactly what Scale is, which is why the mask comes from Scale and not from the
///     presented rate. Reading the presented rate instead would double the blink period through the
///     three syncdata scenes, where the engine holds itself to the recording's own cadence.
///
///     Not a detour. The function also writes the window's own elapsed time to +0xE0 and calls
///     TOBtlDrawCommandWindow, so holding the call would hold the command window itself. The single
///     cursor drawn at the end of the function is not gated at all and is left alone.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    /// <summary>
    ///     <c>F6 05 &lt;sg_count&gt; imm8</c>. The immediate is the seventh byte, and the six that
    ///     precede it are checked before anything is written: a wrong address here rewrites an
    ///     instruction rather than failing.
    /// </summary>
    private static ReadOnlySpan<byte> BlinkGatePrefix => [0xF6, 0x05, 0xF0, 0xBB, 0x3C, 0x02];
    private const int BlinkMaskOffset = 6;
    private const byte VanillaBlinkMask = 0x01;

    private int _blink_mask;

    /// <summary>
    ///     Called once per presented frame from <c>apply_rate_patches</c>, and a compare and a return
    ///     unless the mask it derives has changed. It has to be asked that often rather than only on
    ///     a rate change, because Scale also moves when a syncdata scene begins or ends.
    /// </summary>
    private void patch_battle_cursor_blink()
    {
        if (!_config.BattleCursorBlink) return;

        // The mask has to be a single bit, so the scale is rounded in log space rather than
        // arithmetically: a 90 Hz scale of 3 asks for a period of six frames that no single bit can
        // express, and bit 2 (eight frames) is closer to it than bit 1 (four).
        int exponent = (int)Math.Round(Math.Log2(Math.Max(1f, Scale)));
        int mask = 1 << Math.Clamp(exponent, 0, 7);

        if (mask == _blink_mask) return;

        byte* site = FhUtil.ptr_at<byte>(EngineAddresses.BattleCursorBlinkGate);
        byte current = site[BlinkMaskOffset];

        // Either untouched or carrying a mask this module wrote earlier; a mask with more than one
        // bit set is not the instruction we think it is.
        bool matches = new ReadOnlySpan<byte>(site, BlinkGatePrefix.Length).SequenceEqual(BlinkGatePrefix)
                    && current != 0 && (current & (current - 1)) == 0;

        if (!matches)
        {
            _logger.Error($"[Fps60] Battle cursor blink gate at RVA 0x{EngineAddresses.BattleCursorBlinkGate:X} " +
                          $"does not carry the expected instruction; nothing written. Found " +
                          $"{site[0]:X2} {site[1]:X2} {site[2]:X2} {site[3]:X2} {site[4]:X2} {site[5]:X2} " +
                          $"mask=0x{current:X2}.");
            return;
        }

        _blink_mask = mask;

        if (mask == VanillaBlinkMask)
        {
            _logger.Info("[Fps60] Battle cursor blink left on bit 0; the target framerate needs no change.");
            return;
        }

        _patches.Write(EngineAddresses.BattleCursorBlinkGate + BlinkMaskOffset, [(byte)mask]);
        _logger.Info($"[Fps60] Battle cursor blink mask set to 0x{mask:X2}, a period of {mask * 2} " +
                     "presented frames.");
    }
}
