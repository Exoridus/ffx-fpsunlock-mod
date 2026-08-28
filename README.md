# fahrenheit-fps60-mod

A Fahrenheit module that runs Final Fantasy X HD Remaster at the display refresh
rate, with every frame-bound engine system retimed, and with true 60 FPS FMVs
delivered through Fahrenheit's integrated External File Loader.

There is no 30 FPS fallback. A subsystem that is not yet correct at 60 Hz is
logged or fails visibly; it is never masked by halving an update rate.

## Status

Scaffold. Module lifecycle, per-correction configuration and the patch/restore
journal exist. No retiming hooks are installed yet.

## Layout

```
Fahrenheit.Mods.Fps60.csproj   project; references Fahrenheit core from .workspace/fahrenheit
fhfps60.manifest.json          Fahrenheit mod manifest
fahrenheit.release.ref         pinned Fahrenheit commit
build.ps1                      build, optional -Deploy into the game's mods folder
tools/bootstrap.ps1            clone and pin Fahrenheit into .workspace/fahrenheit
src/                           module sources
docs/                          origin brief and findings
```

## Build

```powershell
pwsh tools/bootstrap.ps1
pwsh build.ps1 -Deploy
```

## Constraints

- `FFX.exe` and `FFX_Data.vbf` are never modified. All code patches happen in
  process memory and are reverted on shutdown by `PatchJournal`.
- Replacement videos are never committed. They live on the NAS and are staged
  into `efl/x/FFX_Data/GameData/PS3Data/Video/` at packaging time.
- Fahrenheit is the sole hook and file-loader owner. ffgriever's External File
  Loader is not used alongside it.
- FFX only. FFX-2 is out of scope.

## Related

- `ffx-knowledge-base` owns the reverse-engineering evidence, addresses and
  catalogs this module consumes. New engine findings belong there, not here.
- Fahrenheit's own `PWarpModule` is an upstream work in progress covering the
  same ground. If it ships, this module should defer to it rather than double
  hook.
