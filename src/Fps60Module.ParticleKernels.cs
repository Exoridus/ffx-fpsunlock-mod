namespace Fahrenheit.Mods.Fps60;

/// <summary>
///     Particle retiming at the only place the engine separates advancing from drawing.
///
///     A particle program is a list of steps, and each step's descriptor carries up to two
///     per-object entry points: <c>+0x8</c> and <c>+0xc</c>. The dispatch in pppRunPartStd and
///     FUN_007170f0 calls whichever are present, once per object. The two are not variants of the
///     same thing - pppKeThSft adds constants into the object's position, angle and colour and
///     touches nothing else, while pppKeThDraw only reads the object and calls KeThCp_Draw. Update
///     and draw, in that order.
///
///     That is what makes this the one retiming that can work. Holding the pass, at any level above
///     this, takes the draw with it and the particles vanish on held frames - measured, twice.
///     Holding the update kernels alone leaves every draw in place: the objects simply do not move
///     on that frame, which is exactly what a 30 Hz animation looks like at 60 Hz.
///
///     The motion itself carries no time: the kernels add a constant per call. So there is nothing
///     to scale, and halving the manager's step reaches only the emission and lifetime timeline.
/// </summary>
public unsafe sealed partial class Fps60Module
{
    /// <summary>
    ///     The step kernels that mutate an object, by RVA, and whether holding one is safe.
    ///     Everything ending in Con, Con2, Des or Draw is absent: those construct a step or draw
    ///     one, and neither advances anything.
    ///
    ///     <c>Hold: false</c> also marks pppKeDrct and pppKeGrvTgt, whose entire body is the
    ///     keyframe one-shot. They do nothing on an ordinary tick, so holding them retimes nothing
    ///     and can only cost the keyframe. They stay hooked for their counts.
    ///
    ///     <c>Hold: false</c> marks the four the Lns draw kernels call themselves -
    ///     <c>pppKeLnsFlsDraw</c> opens with <c>pppKeLnsFlsUpdate(param_1, param_2, param_3)</c>,
    ///     and Arnd, Clm and Crn do the same. Holding them is not risky, it is pointless: their
    ///     entire body sits inside the keyframe test, so on an ordinary tick they do nothing at
    ///     all, and on a keyframe tick they resolve the shape pointer and seed the sprite
    ///     accumulator the draw reads back immediately. Skipping that leaves the draw with the
    ///     init stub's placeholder pointer, which its own guard then turns into an invisible or
    ///     wrongly bright sprite - which is what a running game showed.
    ///
    ///     No time passes in them. The clock of this family is <c>KeLnsShp_Update</c>, which the
    ///     draw calls and which advances a fixed step per drawn frame; that is scaled instead, in
    ///     <see cref="h_lens_shape_update"/>.
    /// </summary>
    private static readonly (string Name, nint Rva, bool Hold)[] KernelUpdates =
    [
        ("pppKeBornRnd2",      0x3588A0, true ), ("pppKeBornRnd3",      0x358A50, true ),
        ("pppKeBornRnd5",      0x359010, true ), ("pppKeBornRnd6",      0x3592F0, true ),
        ("pppKeDrct",          0x35E520, false), ("pppKeGrvEff",        0x359740, true ),
        ("pppKeGrvTgt",        0x3598A0, false), ("pppKeHmgEff",        0x359CE0, true ),
        ("pppKeLnsArndUpdate", 0x35A3C0, false), ("pppKeLnsClmUpdate",  0x35A790, false),
        ("pppKeLnsCrnUpdate",  0x35ABA0, false), ("pppKeLnsFlsUpdate",  0x35AF80, false),
        ("pppKeLnsLpSft",      0x35A020, true ), ("pppKeMatSN",         0x3340F0, true ),
        ("pppKeMvYpEff",       0x35E400, true ), ("pppKeShpTail",       0x34C570, true ),
        ("pppKeShpTail2",      0x34E570, true ), ("pppKeShpTail2X",     0x34F5B0, true ),
        ("pppKeShpTail3",      0x350390, true ), ("pppKeShpTail3X",     0x351D80, true ),
        ("pppKeShpTailX",      0x34D570, true ), ("pppKeTh",            0x336110, true ),
        ("pppKeThSft",         0x336F50, true ), ("pppKeThTp",          0x336E40, true ),
    ];

    /// <summary>
    ///     The object's current program step, at <c>+0xc</c>. Every kernel compares it against the
    ///     step index it was given: pppKeThSft runs its body only when they match, and
    ///     pppKeShpTail3 treats zero as "this object is new" and seeds all 28 entries of its trail
    ///     ring from the current position.
    ///
    ///     A held frame must never be an object's first frame. Skipping the first call leaves that
    ///     seeding undone, and the draw kernel then reads a ring that was never written - which is
    ///     what threw the pyreflies across the screen.
    /// </summary>
    private const int ObjectStepOffset = 0xc;

    /// <summary>
    ///     True on a call the object cannot afford to miss, whatever else the kernel does.
    ///
    ///     <c>obj+0xc</c> is not a step index but the object's age in fixed point, advanced by
    ///     0x1000 per pass at the tail of the dispatcher. A step's parameters arrive as a keyframe
    ///     timeline, and a kernel runs its one-shot arm exactly when the age matches the keyframe
    ///     the dispatcher's cursor is on - <c>*param_2</c>, already in the arguments. The cursor
    ///     moves on afterwards, so that tick never comes back.
    ///
    ///     Three kernels resolve a pointer to another emitter's object on that tick, cache it in
    ///     their working area and dereference it unconditionally below: pppKeGrvEff and pppKeHmgEff
    ///     write it to <c>work+0xa0</c> and <c>work+0xa4</c>, pppKeTh likewise. Their constructors
    ///     are empty stubs and pppCreatePObject does not clear the working area, so a lost keyframe
    ///     leaves them dereferencing recycled heap rather than merely animating wrong. Age zero is
    ///     covered by the same test because the first pass is a keyframe too.
    /// </summary>
    private static bool is_initialising_call(int obj, int data)
    {
        int age = *(int*)(obj + ObjectStepOffset);
        return age == 0 || age == *(int*)data;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void d_ke_update(int obj, int data, int prog);

    // The chain is keyed by delegate, so every target needs its own instance; the list is also what
    // keeps them alive against the collector.
    private readonly List<d_ke_update> _kernel_hooks = [];

    private long _ke_calls;
    private long _ke_held;

    // Per kernel, so a scene can be asked which steps it actually runs rather than guessed at.
    private readonly Dictionary<string, (long Calls, long Held)> _ke_by_name = [];

    private string kernel_counts()
    {
        string busiest = string.Join(' ', _ke_by_name
            .Where(p => p.Value.Calls > 0)
            .OrderByDescending(p => p.Value.Calls)
            .Take(6)
            .Select(p => $"{p.Key}={p.Value.Calls}/{p.Value.Held}"));

        return $"ke_calls={_ke_calls} ke_held={_ke_held}" + (busiest.Length > 0 ? $" [{busiest}]" : "");
    }

    private bool init_particle_kernel_hooks()
    {
        if (!_config.ParticleKernelHold) return true;

        bool ok = true;

        foreach ((string name, nint rva, bool safe) in KernelUpdates)
        {
            // Every kernel is hooked so its call count is known; only the selected ones are held.
            // A scene's own list of steps is the thing worth knowing, and it costs one counter.
            bool hold = safe
                        && (_config.ParticleKernelOnly.Length == 0
                            || _config.ParticleKernelOnly.Contains(name, StringComparer.OrdinalIgnoreCase))
                        && !_config.ParticleKernelExcept.Contains(name, StringComparer.OrdinalIgnoreCase);

            string key = name;
            _ke_by_name[key] = (0, 0);

            d_ke_update? self = null;

            self = (obj, data, prog) =>
            {
                _ke_calls++;
                note_keyframe_grid(data);
                var counts = _ke_by_name[key];

                if (hold && !advance_this_frame() && !is_initialising_call(obj, data))
                {
                    _ke_held++;
                    _ke_by_name[key] = (counts.Calls + 1, counts.Held + 1);
                    return;
                }

                _ke_by_name[key] = (counts.Calls + 1, counts.Held);

                new FhMethodHandle<d_ke_update>(new FhMethodLocation(rva, 0))
                    .chain_from(self!).fnptr?.Invoke(obj, data, prog);
            };

            _kernel_hooks.Add(self);

            d_ke_update hook = self;
            ok &= hook_or_log(name, rva,
                () => new FhMethodHandle<d_ke_update>(new FhMethodLocation(rva, 0)).hook(this, hook));

            _logger.Info(hold
                ? $"[Fps60] Holding particle step kernel {name} at RVA 0x{rva:X}."
                : $"[Fps60] Counting particle step kernel {name} at RVA 0x{rva:X}, not held.");
        }

        return ok;
    }
}
