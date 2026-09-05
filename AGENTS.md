# AI Repository Rules — fahrenheit-fpsunlock-mod

## Purpose

Runtime consumer, not a research repo. This is a Fahrenheit module that makes
FFX HD Remaster run at 60 FPS. Reverse-engineering evidence, engine addresses
and catalogs live in the sibling `ffx-knowledge-base`; findings discovered here
are promoted there, not documented only here.

## Hard rules

- FFX only (`FhGameId.FFX`). FFX-2 is out of scope.
- No 30 FPS fallback, ever. Alternate-frame updates and frameskipping are
  workarounds, not acceptable end states. A subsystem that is not correct at
  60 Hz is logged or disabled through config, never hidden.
- Original game files stay byte-identical. Code patches are in-process only and
  go through `PatchJournal` so shutdown restores the image.
- Fahrenheit is the only hook and file-loader owner.
- No video or game asset is ever committed.
- Every retiming correction is individually toggleable in `FpsUnlockConfig`.

## Conventions

- C# / .NET 10, `win-x86`, file-scoped namespaces, nullable enabled, `sealed`.
- Hook targets are addressed by RVA (`FhMethodLocation`), because the pinned
  Fahrenheit's STEP generation exposes most functions only as `FUN_<addr>`
  entries. RVA = Ghidra VA - 0x400000.
- `FhMethodLocation` is a ref struct; construct it at the use site.
- One partial per subsystem (`FpsUnlockModule.Present.cs`, `.Atel.cs`, ...).
