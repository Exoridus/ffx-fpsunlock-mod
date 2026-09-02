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

    /// <summary>
    ///     yiAnimInfo_init(info). Stdcall. Picks the clock the event worker's animation curves run
    ///     on and stores it in yi_anim_clock_mode, once, from whatever KEEP_FPS happens to be.
    /// </summary>
    public const nint YiAnimInfoInit = 0x514310;

    /// <summary>
    ///     The clock mode itself. Zero routes the worker through yiGetFCount, which is sg_count * 2
    ///     and advances two units per presented frame with nothing correcting it; non-zero routes it
    ///     through yiGetVCount, whose advance this module already rescales.
    /// </summary>
    public const nint YiAnimClockMode = 0x153BB08;

    /// <summary>
    ///     sbyte. The KEEP_FPS flag itself, which Sg_GetKeepFps returns. Read directly rather than
    ///     mirrored through the setter, so a write this module does not see cannot desynchronise it.
    /// </summary>
    public const nint SgKeepFps = 0xEFBBEC;

    // --- Character and motion ---

    /// <summary>
    ///     The per-actor motion advance, FUN_00838d10(actor, mode). Cdecl. It adds the actor's
    ///     motion speed into the frame accumulator at actor+0x740, and applies sg_rate only when
    ///     the actor's flag word at +0x194 carries 0x100000 and KEEP_FPS is set.
    /// </summary>
    public const nint MotionAdvance = 0x438D10;

    /// <summary>The actor's flag word, tested by the motion advance for 0x40 and 0x100000.</summary>
    public const int ActorFlagsOffset = 0x194;

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

    /// <summary>
    ///     Sg_AccSetAlpha(alpha, frame_count). Cdecl, measured: the body ends in a tail jmp to
    ///     graphicUpdateCrossFade after a plain pop ebp, and that target cleans through the caller.
    ///     ATEL call target 401A, where the first pushed argument is the alpha and the second the
    ///     frame count, the opposite of what the script dumps label them.
    ///
    ///     It seeds filter slot 0 rather than expressing a duration: the stored step is
    ///     max(1, |alpha - current| / frame_count), so a doubled frame count cannot buy a half step
    ///     once |delta| drops below it. See Fps60Module.CrossFade.
    /// </summary>
    public const nint SgAccSetAlpha = 0x42BD90;

    /// <summary>
    ///     Sg_DrawFilter(). Cdecl and void, measured: a plain c3 at 0x0082C277, closing the
    ///     8b e5 5d c3 epilogue at 0x0082C274, and its one call
    ///     site in Sg_MainLoop pushes nothing and cleans nothing. Runs once per main loop pass and
    ///     steps all five filter slots.
    /// </summary>
    public const nint SgDrawFilter = 0x42C140;

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

    /// <summary>
    ///     _pppStartPart(manager, time_step, data, flags). Cdecl. Starts one particle manager and
    ///     writes time_step into its +0x10, which _pppRunPart adds to the accumulator at +0x08 every
    ///     frame. It is the only place a manager's step is set, and it is where every source of a
    ///     step converges: the global ppv step, and the literal 0x1000 the magic path passes.
    /// </summary>
    public const nint PppStartPart = 0x3124A0;

    /// <summary>
    ///     pppPartLoop. Runs every active manager and calls pppDataRcv at its end, which is where the
    ///     broadcast lives that writes the global step into every manager's +0x10.
    /// </summary>
    public const nint PppPartLoop = 0x362330;

    /// <summary>
    ///     _pppRunPart(manager, mode). Cdecl, returns a status. The single choke point of the
    ///     particle advance: pppPartLoop is only one of its eight callers, and the cutscene and
    ///     battle paths reach it without going through the loop at all.
    /// </summary>
    public const nint PppRunPart = 0x312330;

    /// <summary>
    ///     _pppRunPartFp(manager, mode). Cdecl, returns a status. The field half of the particle
    ///     system, reached from pppFpLoop by way of yiCallFieldParticle. A cutscene's particles go
    ///     through this and never through _pppRunPart.
    /// </summary>
    public const nint PppRunPartFp = 0x3123D0;

    /// <summary>
    ///     MsEffectProcess(mode). Stdcall. Mode 0 advances every active effect, mode 1 draws them,
    ///     and both dispatch into the magic overlay DLL through the table at +0xc and +0x10 - which
    ///     is why no function in FFX.exe carries the effect's own timeline.
    ///
    ///     The advance is called once per presented frame regardless of how many simulation steps
    ///     the frame ran: inside the Sg_GetCurExecFrames loop when there is at least one, and again
    ///     in the iVar4 == 0 branch when there is none. At 60 Hz that is twice the rate the effects
    ///     were authored for.
    /// </summary>
    public const nint MsEffectProcess = 0x387EC0;

    /// <summary>
    ///     _player_chrs, the base of the battle Chr array, which MsGetChrTop returns unchanged. Zero
    ///     outside battle, which is what makes the per-actor half of MsEffectProcess inert there.
    /// </summary>
    public const nint PlayerChrs = 0xD334CC;

    /// <summary>
    ///     The non-actor effect overlay's state byte, the global counterpart of Chr+0xdfb. Values 4
    ///     and 5 mean an overlay is live and MsEffectProcess dispatches into it on both modes.
    /// </summary>
    public const nint GlobalEffectOverlayState = 0xD33364;

    /// <summary>
    ///     The old-format character texture animation advance. Dispatched per slot from
    ///     FUN_0077c9c0 on descriptor byte 2; the new format goes to FUN_0077f450 instead.
    ///     Its counters are literal increments - a sprite step of +1 per call and a blink
    ///     countdown of rand() % 0x5a + 0x3c - so it carries nothing to scale.
    /// </summary>
    public const nint ChrTexAnimAdvanceOld = 0x37FFD0;

    /// <summary>
    ///     MagicFile_Unload. No arguments. Calls graphicVFXDestroyAllExceptFieldAndEternal and then
    ///     PhyreFIOS::UnloadMagicfilePrx, which is the FreeLibrary. It runs from MagicFile_Update in
    ///     the tail of Sg_MainLoop, after the frame's present - so an overlay is stopped and unmapped
    ///     inside one main loop iteration, not across a frame boundary.
    /// </summary>
    public const nint MagicFileUnload = 0x5DA940;

    /// <summary>int. The magic id MagicFile_Cleanup queued for unloading; -1 when nothing is queued.</summary>
    public const nint ToBeDeleteMagicId = 0x864CA4;

    /// <summary>
    ///     The eternal effect set's second entry point, efftOverTbl+0x10. Ghidra names the pair
    ///     op_effect_run_before and op_effect_run_after: both run FUN_0080cd60 over a different
    ///     object list, so neither is a draw and both decrement the per-channel wait bytes.
    /// </summary>
    public const nint EternalEffectRunAfter = 0x400590;

    /// <summary>
    ///     KeLnsShp_Update(work). The sprite frame clock of the lens and flare family: adds a fixed
    ///     0x200 to an accumulator per call and compares it against the current frame's duration.
    ///     Its four callers are the four Lns draw kernels, so it advances once per drawn frame.
    /// </summary>
    public const nint KeLnsShpUpdate = 0x36ACF0;

    /// <summary>
    ///     Ch_SeqFrame(work). The motion sequence VM. Its wait opcode counts raw calls at
    ///     work+0x72a and nothing corrects it.
    /// </summary>
    public const nint ChSeqFrame = 0x437840;

    /// <summary>
    ///     The motion cross-fade, FUN_00839630(work, hokan). Blends each channel by
    ///     (target - current) / hokan; the advance counts hokan down immediately afterwards.
    /// </summary>
    public const nint ChMotionInterpolate = 0x439630;

    /// <summary>
    ///     graphicTextureVideoUpdate. The texture video path, which is not the FMV path and is not
    ///     reached by the FMV frameskip.
    /// </summary>
    public const nint GraphicTextureVideoUpdate = 0x244470;

    /// <summary>graphicDrawMainMenuWaterEffect. One scrolling image per frame.</summary>
    public const nint GraphicDrawMainMenuWaterEffect = 0x23EAD0;

    /// <summary>
    ///     graphicVideoUpdate. Stdcall, no arguments. The video update the FMV path runs per frame,
    ///     and the one the engine's own developers frameskip to get correct playback speed out of a
    ///     29.97 source. Preferred over PhyFMVPlayerManager::UpdateTexture (0x2D77B0), which sits
    ///     deeper in the manager and cannot be skipped without leaving it mid-state.
    /// </summary>
    public const nint GraphicVideoUpdate = 0x245FA0;

    /// <summary>
    ///     sFMVPlayerManager, the PhyFMVPlayerManager singleton pointer. Byte 0x6D0 of the instance
    ///     is the playback flag graphicVideoUpdate tests before it does anything: while it is clear
    ///     the function returns immediately, so a call is not a video frame.
    /// </summary>
    public const nint FmvPlayerManager = 0x8DED2C;

    // --- Globals (RVA, same convention as the hook targets) ---

    /// <summary>
    ///     short. Filter slot 0's remaining step count, and the only field of the slot whose reaching
    ///     zero ends the cross-fade. Sg_AccSetAlpha hands its address to PostProcessManager, which
    ///     keeps the pointer and dereferences it on every rendered frame, so writing it directly is
    ///     seen without going back through the setter.
    /// </summary>
    public const nint CrossFadeSlot0Counter = 0xF006C4;

    /// <summary>
    ///     short. Filter slot 0's current alpha, 0 to 0x80. On the HD build this is the motion blur
    ///     weight: the render pass divides it by 128 and hands it to MotionBlur::setMotionBlurWeight.
    ///     Restoring it after the engine has cleared it is what would leave a permanent trail.
    /// </summary>
    public const nint CrossFadeSlot0Alpha = 0xF006C6;

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

    /// <summary>
    ///     int. The engine's own count of live particle objects, accumulated during the dispatcher
    ///     pass and reset each frame - so it has to be read from inside a frame to mean anything.
    /// </summary>
    public const nint PobjCounter = 0x1F0FD24;

    /// <summary>uint. Particle stop request.</summary>
    public const nint PpvUserStopPartF = 0x1F0FD34;

    /// <summary>
    ///     int. The global particle time step, in the same fixed-point units as each manager's
    ///     accumulator. Zero in the image and written at runtime; observed as 0x1000.
    /// </summary>
    public const nint PpvPartTimeStep = 0x94B7DC;

    /// <summary>int. Number of active particle managers.</summary>
    public const nint PpvPartManagerCount = 0x94B4B8;

    /// <summary>Particle manager array. Stride 0x80; the time step is at +0x10 of each entry.</summary>
    public const nint PpvPartManagers = 0x94E380;

    /// <summary>uint. Non-zero while an FMV is playing.</summary>
    public const nint GMoviePlay = 0xD2A008;

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

    /// <summary>
    ///     uint. EnableGameTextureAnimation. The image ships it as 1; FUN_00836790 is the only
    ///     writer and FUN_006bb810 calls it with 0 on one branch. Zero disables the whole character
    ///     texture animation advance.
    /// </summary>
    public const nint EnableGameTextureAnimation = 0x849720;

    /// <summary>
    ///     uint. g_isNeedSync. While it is 1, Sg_MainCalcRate takes sg_rate from the syncdata table
    ///     instead of from the vertical blank delta, which discards this module's correction.
    /// </summary>
    public const nint IsNeedSync = 0xEFB858;

    /// <summary>
    ///     uint. force_wait_blur_frame_count. Set to 0x5a by the eternal effect VM's opcode 0x0c
    ///     handler at 0x007f91a0 and counted down once per MsBtlReadManage call, which is once per
    ///     presented frame. Battle state 0x16 waits for it to reach zero before the encounter starts.
    /// </summary>
    public const nint ForceWaitBlurFrameCount = 0xD2CA64;

    /// <summary>
    ///     The eternal effect VM's transition blur, opcode 0x0c of the object opcode table. It is
    ///     what writes the 90-frame battle entry hold.
    /// </summary>
    public const nint EternalBlurTransition = 0x3F91A0;

    // --- Texture animation candidates, survey only ---

    /// <summary>chr_texanim_draw. Dispatches to the old or new per-character texture animation draw.</summary>
    public const nint ChrTexAnimDraw = 0x37C920;

    /// <summary>screenTextureAnimationDraw.</summary>
    public const nint ScreenTextureAnimationDraw = 0x50BDB0;

    /// <summary>setMaterialUVScroll. Scrolling UVs rather than a frame sequence.</summary>
    public const nint SetMaterialUVScroll = 0x2AC9C0;

    /// <summary>Ch_TextureAnimSetEnable.</summary>
    public const nint ChTextureAnimSetEnable = 0x430D90;

    /// <summary>
    ///     Ch_TextureSetAnimTimer(chr, timer). Stdcall. Forwards to tex_anim_timer, which stores a
    ///     byte per texture animation slot at tex_anim_wk+0xE. It is a period rather than a frame
    ///     index, so it scales like every other duration in this module.
    ///
    ///     Its callers are what identify it: MsCalcMotionSpeed derives it from battle motion speed,
    ///     which is why animated weapon textures run at double speed for the same reason animations
    ///     did. The other two are MsBtlBridgeBtl2Event and one magic-side function.
    /// </summary>
    public const nint ChTextureSetAnimTimer = 0x43D070;
}
