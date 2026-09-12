FFX FPS Unlock (fhfpsunlock) with Fahrenheit
=============================================

Lifts Final Fantasy X HD Remaster (PC) from 30 Hz to 60 Hz and retimes the
game systems that assume 30 Hz. Experimental: back up your saves before you
play. Final Fantasy X only; FFX-2 is not supported.


Installation
------------

1. Copy the contents of this archive into the game directory, next to
   FFX.exe. On Steam that is typically:
   C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\

   Afterwards the layout must be:
     <game>\FFX.exe
     <game>\start-ffx-with-mods.cmd
     <game>\fahrenheit\bin\fhstage0.exe
     <game>\fahrenheit\mods\fhfpsunlock\fhfpsunlock.dll
     <game>\fahrenheit\mods\loadorder

2. Microsoft .NET Runtime 10 (x86) must be installed. The launcher checks
   for it and offers to install it through winget. Manual download:
   https://dotnet.microsoft.com/en-us/download/dotnet/10.0
   -> ".NET Runtime 10", Windows x86.

3. Always start the game through start-ffx-with-mods.cmd, not through Steam
   and not through FFX.exe directly. Steam has to be running.

On first start Fahrenheit creates the config, logs, saves and state folders
under fahrenheit\ on its own.


Settings
--------

Fahrenheit's settings window (ImGui) opens in game. Pick the target rate
under "FPS Unlock". The default is about 59.94 Hz; 30 leaves every duration
as the game authored it. Rates above 60 are untested.

There is deliberately no fahrenheit\mods\fhfpsunlock\fhfpsunlock.config.json:
without the file the shipped defaults apply.


Uninstall
---------

Close the game, then delete the fahrenheit\ folder and
start-ffx-with-mods.cmd. FFX.exe and the game data are never modified.


Known limits
------------

- Three cutscenes paced from recorded frame data keep their recorded tempo
  so the voice track stays in sync.
- Do not install together with other mods that patch the same timing
  functions.
- When reporting a problem, name the scene, the settings and attach the
  newest file under fahrenheit\logs\.


Sources
-------

fhfpsunlock  https://github.com/Exoridus/ffx-fpsunlock-mod
Fahrenheit   https://github.com/fahrenheit-crew/fahrenheit
