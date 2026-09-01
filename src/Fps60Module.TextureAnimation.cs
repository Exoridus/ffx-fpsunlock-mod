namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     The new character texture animation format, which is the one an animated weapon texture uses.
///
///     Its advance adds a per-slot step byte to a sequence accumulator once per call and compares the
///     accumulator against the sprite's authored duration, so the step is a rate and the period lives
///     in the asset. At 60 Hz the call happens twice as often and the animation runs twice as fast.
///
///     The step cannot express one half: it is a signed byte and its engine values are 1 and 0. So
///     the correction is the one shape the byte does allow - write 0 on the frames a 30 Hz sequence
///     would not have advanced, and put the engine's own value back on the frames it would. That is a
///     hold, but a hold of the step rather than of the call, so the advance still runs, still draws,
///     and still carries its own state forward.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    private const nint TexAnimSlotBase = TexAnimWorkBase;

    /// <summary>What the engine last had in a slot's step byte before this module zeroed it.</summary>
    private readonly sbyte[] _texanim_parked = new sbyte[TexAnimSlotCount];

    private long _texanim_parked_frames;
    private long _texanim_restored;

    private string texture_animation_counts()
        => $"texanim_parked={_texanim_parked_frames}/{_texanim_restored}";

    /// <summary>
    ///     Runs once per presented frame, before the engine's update for that frame.
    ///
    ///     Only slots on the new format are touched. The old format ignores this byte entirely - its
    ///     counters are literal increments, which is why that path is held at the call instead.
    /// </summary>
    private void retime_texture_animation()
    {
        if (!_config.TextureAnimationStep) return;

        bool advancing = advance_this_frame();

        for (int slot = 0; slot < TexAnimSlotCount; slot++)
        {
            nint format = TexAnimSlotBase + slot * TexAnimSlotStride + TexAnimFormatOffset;
            if (FhUtil.get_at<byte>(format) != 1) continue;

            nint timer = TexAnimSlotBase + slot * TexAnimSlotStride + TexAnimTimerOffset;
            sbyte value = FhUtil.get_at<sbyte>(timer);

            if (advancing)
            {
                // Only a slot this module actually zeroed is restored, and only while it still reads
                // zero. The engine writes this byte itself - MsCalcMotionSpeed puts a 0 there to
                // pause the animation on a motion speed zero crossing - and overwriting that would
                // resume an animation the game deliberately stopped.
                if (_texanim_parked[slot] != 0 && value == 0)
                {
                    FhUtil.set_at(timer, _texanim_parked[slot]);
                    _texanim_restored++;
                }

                _texanim_parked[slot] = 0;
                continue;
            }

            if (value == 0) continue;

            _texanim_parked[slot] = value;
            FhUtil.set_at(timer, (sbyte)0);
            _texanim_parked_frames++;
        }
    }
}
