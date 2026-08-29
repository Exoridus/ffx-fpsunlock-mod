namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Hook targets and globals, as RVAs relative to the FFX.exe image base.
///
///     The pinned Fahrenheit's STEP generation exposes most engine functions only as
///     <c>FUN_&lt;address&gt;</c> entries, so hooks are addressed numerically rather than by
///     the friendly FhCall names. Values are derived from the knowledge base function
///     catalog, whose addresses are Ghidra VAs: RVA = VA - 0x400000.
/// </summary>
public static class EngineAddresses
{
    // --- Present and frame loop ---

    /// <summary>
    ///     FFXApplication::animate. Thiscall, returns uint. The game's per-frame entry point: it runs
    ///     the Steam callbacks, calls updateFFX (which drives the main loop and Sg_MainLoop) and then
    ///     chains into Phyre::PFramework::PApplication::animate.
    /// </summary>
    public const nint FFXApplicationAnimate = 0x2F600;

    /// <summary>
    ///     updateFFX. Cdecl, takes the frame delta. The main loop body itself; it repeats while
    ///     gElapsedFrameCount_frameSkip is non-zero without recomputing the delta.
    /// </summary>
    public const nint UpdateFFX = 0x4228D0;

    /// <summary>Phyre::PFramework::PWindowWin32Base::SetFlipVSyncInterval. Cdecl.</summary>
    public const nint SetFlipVSyncInterval = 0x225250;

    /// <summary>Sg_MainCalcRate. Computes sg_rate from the vertical/horizontal blank ratio each frame.</summary>
    public const nint SgMainCalcRate = 0x4207D0;

    /// <summary>
    ///     The frame-pacing routine that advances the vertical blank counters. It adds a hardcoded 2
    ///     to both, which is the engine's assumption of two vertical blanks per 30 Hz frame.
    /// </summary>
    public const nint AdvanceVBlankCounters = 0x421F90;

    /// <summary>Sg_SetKeepFps. Cdecl, sbyte in/out. There is no matching getter in the engine.</summary>
    public const nint SgSetKeepFps = 0x421C00;

    /// <summary>Sg_GetKeepFps.</summary>
    public const nint SgGetKeepFps = 0x4206B0;

    // --- Character and motion ---

    /// <summary>Ch_CalcMain(float delta). The engine passes a fixed delta of 0.033373334.</summary>
    public const nint ChCalcMain = 0x432E90;

    /// <summary>Ch_SetMotionSpeed(Actor*, ushort). Animation speed follows motion speed unless KEEP_FPS is set.</summary>
    public const nint ChSetMotionSpeed = 0x42B400;

    /// <summary>
    ///     Unnamed motion parameter setter: (chr_id, stat_id, float value). stat ids 0x03..0x06 are
    ///     MOTION_RUN_SPEED, _RETURN, _V0 and _ACC. Reached from ATEL call targets 70A8 and 70B2.
    /// </summary>
    public const nint SetMotionParamFloat = 0x3B4AA0;

    /// <summary>MsSetChrStatInfo(chr_id, stat_id, target_id, value). Reached from ATEL call targets 70AB and 7018.</summary>
    public const nint MsSetChrStatInfo = 0x3B4B80;

    /// <summary>MsEffectSetSpeed(byte chr_id, ushort speed).</summary>
    public const nint MsEffectSetSpeed = 0x3884F0;

    // --- Camera ---

    /// <summary>MsCameraMoveFrame(camera_id, _, _, frame_count, _). Cutscenes synchronise on camWait.</summary>
    public const nint MsCameraMoveFrame = 0x3BDDD0;

    /// <summary>MsCameraMoveAcc(camera_id, mode_non_ref, mode_polar, + four timing arguments).</summary>
    public const nint MsCameraMoveAcc = 0x3BD7E0;

    // --- Fades, flash, alpha ---

    /// <summary>
    ///     The common fade routine behind Sg_FadeIn/InW/Out/OutW: (frame_count, mode_in, mode_w).
    ///     Reached from ATEL call targets 4004 through 4007.
    /// </summary>
    public const nint SgFadeCommon = 0x42CE40;

    /// <summary>Sg_Flash(frame_count, ...). ATEL call target 4003.</summary>
    public const nint SgFlash = 0x42CD20;

    /// <summary>Sg_AccSetAlpha(alpha, frame_count). ATEL call target 401A.</summary>
    public const nint SgAccSetAlpha = 0x42BD90;

    /// <summary>TkSetFadeOut(frame_count). ATEL call target 00BB; also reachable outside it.</summary>
    public const nint TkSetFadeOut = 0x48EAC0;

    // --- ATEL ---

    /// <summary>
    ///     Init handler of ATEL call target 0000 ("wait", 53,676 call sites), the frame-based wait.
    ///     One-frame waits are idle loops and must not be scaled.
    /// </summary>
    public const nint AtelWaitInit = 0x45C3E0;

    /// <summary>Exec handler of ATEL call target 0000.</summary>
    public const nint AtelWaitExec = 0x45C570;

    /// <summary>AtelPopStackInteger(worker, stack).</summary>
    public const nint AtelPopStackInteger = 0x46DE90;

    // --- Battle, menu, particles, FMV ---

    /// <summary>TOBtlCtrlLimitTimer. Divides by a fixed 30 or 25 depending on the mode.</summary>
    public const nint TOBtlCtrlLimitTimer = 0x491A30;

    /// <summary>rnd. Used by the limit timer to jitter its last displayed digit.</summary>
    public const nint Rnd = 0x3989B0;

    /// <summary>pppFpStopStatus. Particle timing is self-driven, so particles are stopped rather than retimed.</summary>
    public const nint PppFpStopStatus = 0x32A840;

    /// <summary>graphicDrawMainMenuWaterEffect. One scrolling image per frame.</summary>
    public const nint GraphicDrawMainMenuWaterEffect = 0x23EAD0;

    /// <summary>PhyFMVPlayerManager::UpdateTexture. Thiscall.</summary>
    public const nint PhyFmvPlayerManagerUpdateTexture = 0x2D77B0;

    // --- Globals (RVA, same convention as the hook targets) ---

    /// <summary>uint. Target framerate is 60 / this. The engine uses 1 in menus and 2 elsewhere.</summary>
    public const nint SFlipVSyncInterval = 0x830E88;

    /// <summary>uint. Horizontal blank counter; the engine's own frame counter.</summary>
    public const nint SgCount = 0x1FCBBF0;

    /// <summary>
    ///     uint. Vertical blank counter. Sg_MainCalcRate derives the animation rate from its delta:
    ///     sg_rate = (sg_vcount - previous) * 0x80, and sg_ratef = sg_rate / 256, so a delta of 2 is
    ///     the 1.0 that means "one full 30 Hz step".
    /// </summary>
    public const nint SgVCount = 0xEFB7A8;

    /// <summary>uint. Second vertical blank counter, advanced in lockstep with the first.</summary>
    public const nint SgVCount2 = 0xEFB7AC;

    /// <summary>int. Animation rate in 1/256 units; 0x100 is full speed.</summary>
    public const nint SgRate = 0x1FCBBEC;

    /// <summary>float. sg_rate as a factor; 1.0 is full speed.</summary>
    public const nint SgRateF = 0x1FCBBE8;

    /// <summary>uint. Particle stop request.</summary>
    public const nint PpvUserStopPartF = 0x1F0FD34;

    /// <summary>byte, 0..69. Current frame of the main menu water animation.</summary>
    public const nint MenuWaterFrame = 0x8CBA09;

    /// <summary>double in .rdata. Hardcoded 29.97 target framerate of the video update loop's time step.</summary>
    public const nint TextureVideoTargetFramerate = 0x74A180;

    /// <summary>uint. Limit timer mode; the timer only runs when this equals 2.</summary>
    public const nint LimitTimerMode = 0xF3F73C;

    /// <summary>float. Accumulated limit timer frames.</summary>
    public const nint LimitTimerFrames = 0xF3F748;

    /// <summary>float. Limit timer base duration in seconds.</summary>
    public const nint LimitTimerBase = 0xF3F74C;

    /// <summary>float. Raw remaining limit time.</summary>
    public const nint LimitTimerRaw = 0xF3F750;

    /// <summary>float. Displayed limit time, deliberately jittered in its last digit.</summary>
    public const nint LimitTimerRounded = 0xF3F754;
}
