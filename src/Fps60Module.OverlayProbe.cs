namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Answers, without waiting for a crash, whether a particle object outlives the magic overlay
///     whose program descriptors it points at.
///
///     A magic overlay ships its own <c>_PPP_PROG</c> records in its .data, and the step entries of
///     any group it started point at them. MagicFile_Unload calls FreeLibrary in the tail of
///     Sg_MainLoop; pppDeletePObject then reads a step's descriptor and calls its destructor slot
///     unconditionally. If a live object still references a descriptor inside the unloaded image at
///     that moment, the teardown reads and calls unmapped memory - inside FFX.exe, reached from
///     Sg_MainLoop, with no detour of ours on the path.
///
///     So the probe runs at the one instant that decides it: immediately before the unload. Any
///     descriptor outside the FFX.exe image is the proof; a run of battles with none falsifies the
///     candidate and leaves the keyframe fix carrying the crash on its own.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    // The walk, all of it read out of pppRunPartStd (FFX.exe.c:791030-791048). get_ptr32_func is
    // the identity on PC (FFX.exe.c:1370824), so every stored dword is a plain pointer.
    private const int ManagerEmitterCountOffset = 0x18;
    private const int ManagerEmitterArrayOffset = 0x3c;
    private const int EmitterObjectCountOffset  = 0x0c;   // ushort
    private const int GroupStepCountOffset      = 0x26;   // short
    private const int GroupStepArrayOffset      = 0x28;
    private const int GroupStepStride           = 0x10;

    private nint _image_base;
    private nint _image_end;

    private long _unloads_probed;
    private long _unloads_with_foreign_descriptor;

    private bool init_overlay_probe()
    {
        if (!_config.OverlayUnloadProbe) return true;

        var main = Process.GetCurrentProcess().MainModule;
        if (main is null)
        {
            _logger.Error("[Fps60] Overlay probe needs the main module's range and could not read it.");
            return false;
        }

        _image_base = main.BaseAddress;
        _image_end  = main.BaseAddress + main.ModuleMemorySize;

        _logger.Info($"[Fps60] Overlay unload probe armed. FFX.exe image 0x{_image_base:X}..0x{_image_end:X}.");

        return hook_or_log("MagicFile_Unload", EngineAddresses.MagicFileUnload,
            () => new FhMethodHandle<d_magic_file_unload>(new FhMethodLocation(EngineAddresses.MagicFileUnload, 0))
                .hook(this, h_magic_file_unload));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint d_magic_file_unload();

    /* Takes no arguments, so cdecl and stdcall are the same call here and the convention carries no
     * risk. The probe runs before the original: after it the image is already gone. */
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private uint h_magic_file_unload()
    {
        probe_live_descriptors();

        var orig = new FhMethodHandle<d_magic_file_unload>(new FhMethodLocation(EngineAddresses.MagicFileUnload, 0))
            .chain_from(h_magic_file_unload).fnptr;

        return orig is null ? 0 : orig();
    }

    private void probe_live_descriptors()
    {
        _unloads_probed++;

        int magic_id = FhUtil.get_at<int>(EngineAddresses.ToBeDeleteMagicId);
        int managers = FhUtil.get_at<int>(EngineAddresses.PpvPartManagerCount);

        int live_objects = 0;
        int steps_seen = 0;
        int foreign = 0;
        string first = "";

        // Every read below is off engine data being torn down, so each level is bounds-checked
        // rather than trusted: a count that is not plausible ends that branch instead of the game.
        for (int i = 0; i < managers && i < 256; i++)
        {
            nint manager = EngineAddresses.PpvPartManagers + i * ParticleManagerStride;
            byte* m = FhUtil.ptr_at<byte>(manager);

            int emitters = *(int*)(m + ManagerEmitterCountOffset);
            nint emitter_array = *(nint*)(m + ManagerEmitterArrayOffset);

            if (emitters <= 0 || emitters > 4096 || !mapped(emitter_array)) continue;

            for (int e = 0; e < emitters; e++)
            {
                // The emitter record is 0x10 bytes: group pointer, parameter block, _, object count.
                nint emitter = emitter_array + e * 0x10;
                if (!mapped(emitter)) break;

                nint group = *(nint*)emitter;
                int objects = *(ushort*)(emitter + EmitterObjectCountOffset);

                if (objects == 0 || !mapped(group)) continue;
                live_objects += objects;

                int step_count = *(short*)(group + GroupStepCountOffset);
                if (step_count <= 0 || step_count > 256) continue;

                for (int s = 0; s < step_count; s++)
                {
                    nint step = group + GroupStepArrayOffset + s * GroupStepStride;
                    if (!mapped(step)) break;

                    nint descriptor = *(nint*)step;
                    if (descriptor == 0) continue;

                    steps_seen++;

                    if (descriptor >= _image_base && descriptor < _image_end) continue;

                    foreign++;
                    if (first.Length == 0)
                        first = $"manager={i} emitter={e} step={s} descriptor=0x{descriptor:X} objects={objects}";
                }
            }
        }

        if (foreign > 0) _unloads_with_foreign_descriptor++;

        _logger.Info($"[Fps60] MagicFile_Unload({magic_id}): managers={managers} live_objects={live_objects} " +
                     $"steps={steps_seen} foreign_descriptors={foreign}" +
                     (foreign > 0 ? $" FIRST[{first}]" : "") +
                     $" (probed={_unloads_probed}, with_foreign={_unloads_with_foreign_descriptor})");
    }

    /// <summary>
    ///     A cheap plausibility gate rather than a real query: the walk runs on data the engine is
    ///     tearing down, and a stale count would otherwise send it into an arbitrary address.
    /// </summary>
    private static bool mapped(nint address)
        => address > 0x10000 && address < 0x7FFF0000;

    private string overlay_probe_counts()
        => _config.OverlayUnloadProbe
            ? $"unloads={_unloads_probed} unloads_foreign={_unloads_with_foreign_descriptor}"
            : "unloads=off";
}
