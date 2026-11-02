# Native field playtest

This private diagnostic tests native gameplay time with additional interpolated images.
It is not a release. The first native probe produced zero additional images because a
zero-delta native startup pass permanently refused field admission. The corrected build
passes the native startup check without that refusal and passes 2478 source/build tests.
That check did not load the field save or establish additional images. The previous
private package is superseded; use the corrected package. Fixed60 remains on main.

## Package and setup

Run `build.cmd mod-field-pack --sdk official --configuration Release --out-dir artifacts/native-field-playtest-corrected`. Output is
`artifacts/native-field-playtest-corrected/`: the mod ZIP, a separate Fahrenheit alpha12 phase
overlay ZIP, hashes and provenance. The build does not deploy or launch anything.
The overlay is built from official commit 7e85839b2efb67124d1760598c9f220cb559a41a
with only the included additional-image event extension. The mod uses the published
alpha12 SDK and discovers that extension at startup.

For local testing, the existing loader also supports a separate runtime: stage0
locates stage1 from its working directory, stage1 sets APP_CONTEXT_BASE_DIRECTORY
from its own directory, and the managed Finder roots config/mods/state/saves/logs
there. Keep the original game executable as the stage0 target; it retains the game
directory as its working directory. The validated local startup used that arrangement
with only fhr/fhvrr, without replacing the normal installation or changing graphics
settings. Its private test set `native-field-proof` contains a byte-identical copy of
slot 167 (02:06:34, 510 gil). Normal deployment remains a separate manual option below.

With the game closed, preserve the current Fahrenheit installation and saves before
manually staging this private test. Start with official alpha12 and its x86 prerequisites.
Extract the host overlay into `fahrenheit/` (replacing `bin/fh.dll` and `bin/fhr.dll`),
and the mod ZIP into `fahrenheit/mods/`. Keep only `fhvrr` in the test loadorder;
Fahrenheit automatically loads `fhr` from `bin/`. Use the same mod composition for both runs. Do not add Parry,
QoL, other FPS mods or runtime probes for the first comparison.

Use Steam build 25501027, windowed/borderless, MSAA off, normal speed, no boosters.
The additional-image gate requires both room and loaded event ID 60, native divider 2,
foreground focus, no battle/movie/native input owner and an active diagnostic capture.
Use set `vrr-playtest-20261018`, slot `167`, playtime `02:06:34`, 510 gil.
The actual Fahrenheit save file is `ffx_167`, SHA-256
1396978730669639380a8376b4f08a89acb65d681007490d19d5632d925b7b61.
Its internal room/event is Besaid Crusaders Lodge (bsvr0200); that internal label
is not the save-point name displayed in the load menu. Slot 000 in the active
Fahrenheit save set differs from slot 000 in the original Documents save folder.
Do not overwrite another save. The gate, rather than the save filename, decides admission.

## Two separate process starts

Do not resume the visual comparison until the corrected build reports active field
admission and a nonzero additional-image count. Starting the profile is insufficient.

Set process environment in the shell that launches FFX, or its launcher. Clear both
mode variables first; inherited environment must reach FFX.exe.

```powershell
Remove-Item Env:FFX_VRR_NATIVE_FIELD_REFERENCE -ErrorAction SilentlyContinue
Remove-Item Env:FFX_VRR_NATIVE_FIELD_PROBE -ErrorAction SilentlyContinue
$env:FFX_VRR_NATIVE_FIELD_REFERENCE = '1'
```

For the first run, load the selected field save, then record a short idle and walking
segment using repeatable endpoints. Reference mode installs no VRR hooks or patches.
Its host-event clock model is observational; it does not measure gameplay steps.

Close FFX. For the second process:

```powershell
Remove-Item Env:FFX_VRR_NATIVE_FIELD_REFERENCE -ErrorAction SilentlyContinue
$env:FFX_VRR_NATIVE_FIELD_PROBE = '1'
```

Repeat the same movement for 5-10 seconds, including a camera change where possible.
The probe overrides VRR settings with its bounded native profile; changing F7 simulation
or maximum-FPS settings is not part of this test. Gameplay events and the native main loop receive the guarded native period on owned real steps, while extra/native-owned deltas are retained. The scheduler uses the guarded native
period (currently 0.03336666666666667 seconds), not alternating hardcoded passes.
Record the display externally if possible. Then test focus loss/restoration and leaving
the eligible scene as separate short runs, without saving. An ineligible real-tick
boundary returns to native timing; loss after declaring an extra image stops the process.

## Evidence required

Keep the Fahrenheit log and fresh `%LOCALAPPDATA%\Fahrenheit\VRR\diagnostics\` files
from each process, plus mod composition, hashes, movement duration and display settings.
`FFX_DIAGNOSTICS_DIRECTORY` can select a separate directory for each run. Captures are
bounded; start a fresh process for another sample. A saturated/failed capture revokes
admission and can trigger the diagnostic stop if it happens inside an additional pass.

Accept only if all of these hold:

- The walking/gameplay duration matches the native reference. Extra passes do not
  advance native clock, input snapshot/history, gameplay or host Pre/PostUpdate events.
- Camera, character root and bones visibly change on extra images. Correlate
  `native_field_host_phase`, `render_pass`, `native_field_camera`, `native_field_actor`,
  existing image/draw completion events and `native_field_present` by image/tick receipt.
  Camera/actor matrices share a window of 1800 image identities after the first
  admitted image (30 seconds at the initial 60 FPS cap). Native-owned images do
  not start or consume that window; multiple actors do not shorten it. Start
  walking promptly once admission is confirmed. A changed palette hash
  alone is insufficient to establish visible animation.
- Ordinary draw completion and successful Present belong to that same image/pose.
  Present success alone does not prove physical scanout or monitor VRR.
- Focus/scene transitions neither publish stale input nor perform gameplay on a pass
  whose host gameplay events were suppressed. Any diagnostic stop is a failed transition
  acceptance case to investigate, not a successful graceful recovery.

The paired comparison also records input-to-action delay; event timestamps do not remove
the native gameplay tick latency. Variable rates, 120 FPS, combat, cutscenes, Parry/Dodge,
E33 and broader compatibility remain deferred until this proof passes.

To undo the private test, close FFX, clear both mode variables and restore the preserved
Fahrenheit installation. No installed game files or saves were changed by packaging.

## Focused clothing diagnosis

This diagnostic revision adds passive before/after memory samples around frozen
additional loop calls. Set FFX_DIAGNOSTICS_BINARY=1, FFX_DIAGNOSTICS_ACTORS=8 and
FFX_DIAGNOSTICS_WATCHES to the executable-qualified cloth watch plan. These use
existing binary channels and the original ReadProcessMemory reader; no new hook,
cloth hold, render-state write or gameplay correction is installed.

The selected native snapshot identified seven active actor slots and eleven
dynamic entries. The bounded watch plan covers ten entries in slots 0..6, reading
each entry header, descriptor and at most the first 31 float4 positions. One entry
and all remaining vertices are outside coverage. Descriptor/pointer changes,
unreadable data, missing boundary samples or a truncated capture invalidate a
comparison; byte equality cannot prove the entire cloth state stayed unchanged.
Snapshots are non-atomic and cannot establish GPU resource ownership.

Load the same reviewed field save, stay focused, then capture about five seconds
idle, five seconds turning and five seconds walking. Close normally to flush both
recorders. Check binary_capture for 30 accepted watches and no watch_plan_rejected
or sampling failure. On additional loops, compare watches between loop_enter and
loop_exit; existing character_enter/exit brackets retain real-step samples. Join
with render_pass, image identities and actor/draw/Present receipts. A changed
sample on an extra localizes a mutation candidate, not the visible flicker's cause.
If no sampled mutation occurs, presentation-time mismatch and output/buffer use
remain open; no cloth fix is inferred.

The 512 MiB JSON limit and focus/scene gate remain active. Their loss returns a
real boundary to native pacing and can stop an already frozen extra. This is an
explicit bounded diagnostic, not a normal-play release. Do not interpret 30 FPS
after capture saturation as a cloth-test result or remove the safety condition.

Actor-only A/B diagnostic: set FFX_VRR_NATIVE_FIELD_ACTOR_INTERPOLATION=0 before starting the private native-field process to preserve native hierarchy output, including repeated draws. Unset or 1 retains interpolation. This flag has no effect on ordinary startup. Use the same package, save and profile for both variants, with separate diagnostic directories. Camera interpolation and additional images remain enabled. Session metadata records native_field_actor_interpolation. B intentionally has less smooth actor animation; judge clothing flicker separately. This does not change the product direction toward the preserved corrected60/high-FPS baseline.
