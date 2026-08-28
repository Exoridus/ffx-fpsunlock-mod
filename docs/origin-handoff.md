# FFX 60 FPS mod handoff

Stand: 2026-08-28

## Vorhaben

Das Ziel ist ein echter, globaler 60-FPS-Mod für Final Fantasy X HD Remaster
auf PC. Das Spiel soll während Gameplay, Kämpfen, Menüs, Zwischensequenzen
und FMVs durchgehend mit 60 FPS laufen. Ein automatischer oder versteckter
Rückfall auf 30 FPS ist ausdrücklich nicht Teil des Ziels.

Der Mod soll:

- die Engine bei 60 FPS mit korrekter Spielgeschwindigkeit betreiben;
- alle framegebundenen Wartezeiten, Bewegungen, Animationen, Effekte, Fades,
  Kameras und Timer retimen;
- echte 59,94/60-FPS-FMVs abspielen, nicht 30-FPS-Videos durch Frameskipping
  optisch auf 60 FPS strecken;
- optional hochskalierte FMVs bis 4K verwenden;
- ohne dauerhaftes Verändern von `FFX.exe` oder `FFX_Data.vbf` installierbar
  und vollständig entfernbar sein;
- über Fahrenheit beim Prozessstart injiziert werden;
- Fehler sichtbar protokollieren und einzelne Korrekturen für Tests
  konfigurierbar machen, ohne im fertigen Mod einen 30-FPS-Fallback
  anzubieten.

Die 60-FPS-Funktion gehört in ein Fahrenheit-Modul im Mod-Runtime-Repository,
nicht in `ffx-knowledge-base`. Dieses Repository liefert die Reverse-Engineering-
Erkenntnisse, Adressen, Kataloge und Evidenz. Der konkrete Runtime-Mod sollte als
neuer Mod oder im passenden Fahrenheit-Mod-Repository entstehen.

## Gewünschte Architektur

Der Mod besteht aus zwei Teilen, bleibt für den Benutzer aber ein Paket:

1. Ein Fahrenheit-DLL-Modul injiziert die Engine-Hooks und Runtime-Patches.
2. Ein Fahrenheit-EFL-Overlay liefert neu kodierte WebM-Dateien anstelle der
   Videos im VBF-Archiv.

Fahrenheit soll der einzige Hook- und File-Loader-Besitzer sein. Ein paralleler
Einsatz von ffgrievers External File Loader ist laut Fahrenheit nicht definiert
und soll vermieden werden.

Die aktuelle Fahrenheit-Generation hat EFL in `fhruntime` integriert. Der
aktuelle Asset-Root ist `efl/x`; `data/x` gehört zu älteren Layouts. Ein
Video-Override sieht konzeptionell so aus:

```text
<mod>/
|-- <60-fps-runtime-module>
`-- efl/
    `-- x/
        `-- FFX_Data/
            `-- GameData/
                `-- PS3Data/
                    `-- Video/
                        `-- opn_us.webm
```

Die Runtime-Patches sollen nur den Prozessspeicher verändern. Auch Werte in
schreibgeschützten EXE-Sektionen können zur Laufzeit mit einem eng begrenzten
`VirtualProtect`-Fenster gepatcht und beim Entladen wiederhergestellt werden.
Ein Patch der EXE auf der Festplatte ist dafür nicht erforderlich.

## Ausgangsbasen

### PWarpModule

`PWarpModule` ist die technisch wertvolle Ausgangsbasis. Der im Discord-Export
gesicherte C#-Quelltext enthält echte Fahrenheit-Hooks und konkrete
FFX-spezifische Korrekturen. Er hookt unter anderem:

- `PApplication::frame` und `SetFlipVSyncInterval`;
- `MsCameraMoveFrame` und `MsCameraMoveAcc`;
- `Sg_SetKeepFps` und `Ch_CalcMain`;
- `Ch_SetMotionSpeed`;
- `CT_0000_Init` für ATEL-Wartezeiten;
- `Sg_Flash`, `Sg_Fade_Common`, `TkSetFadeOut` und `Sg_AccSetAlpha`;
- `MsEffectSetSpeed` und weitere Charakter-/Effektpfade;
- `TOBtlCtrlLimitTimer`;
- Menü-Wasser und Game-Control-Texturanimation;
- `PhyFMVPlayerManager::UpdateTexture`.

Besonders wichtig: Die vorhandene FMV-Lösung lässt das Update nur jeden
zweiten Frame laufen. Damit bleibt ein 29,97-FPS-Video optisch korrekt, aber das
ist genau der vom Ziel ausgeschlossene 30-FPS-Sondermodus. Dieser Hook ist nur
Evidenz für den betroffenen Wiedergabepfad und muss durch eine echte
60-FPS-Lösung ersetzt werden.

Auch die Unterdrückung und Reaktivierung von Texturanimationen an jedem zweiten
Frame ist ein Workaround, kein akzeptabler Endzustand für einen globalen
60-FPS-Mod.

Lokale Evidenzkopie:
`.workspace/exported-media/modding-fahrenheit-dev_1340460420477222923.json_Files/refs/69c313ff2bdc8464.src.txt`

### Crystal Echoes FrameRateUnlocker

Crystal Echoes ist nur als Gerüst und Ideensammlung brauchbar. Der Code hat
Konfiguration, QPC-Messung, einen einfachen Sleep-basierten Limiter und
öffentliche Funktionen. Die entscheidenden Stellen sind jedoch nicht
implementiert:

- Direct3D-Hooks werden nur beschrieben;
- Engine-Offets müssen laut Quelltext erst durch Reverse Engineering bestimmt
  werden;
- die Stellen zum Schreiben der Patch-Bytes sind Kommentare;
- Delta Time wird berechnet, aber nicht in die Engine geschrieben;
- Restore/Detour-Code ist auskommentiert;
- Aussagen zu 60-120+ FPS, 4K/8K, DX12/Vulkan, DLSS, FSR und XeSS sind keine
  nachgewiesene FFX-Implementierung.

Crystal Echoes darf daher nicht als funktionierender Unlocker bewertet werden.
Wir können seine Konfigurations- und Lifecycle-Ideen übernehmen, aber die
eigentliche technische Basis ist `PWarpModule` plus die Erkenntnisse aus dieser
Knowledge Base.

Lokale Evidenzkopie:
`.workspace/exported-media/modding-github_1328431585183400058.json_Files/refs/cc854cf3a131fe5d.src.txt`

## Was bereits über die Engine bekannt ist

Die Engine ist umfangreich katalogisiert, aber nicht vollständig verstanden:

- 56.309 Funktionen sind im Engine-Katalog erfasst.
- 7.216 davon tragen einen belastbaren Namen, etwa 12,8 Prozent.
- Der ATEL-Korpus enthält 1.591 Skript-Dumps.
- Der aktuelle Dispatch-Katalog hat 1.511 Slots, davon 882 benannt und 629
  unbenannt.
- In einer anderen, deduplizierten Sicht sind 990 tatsächlich verwendete
  ATEL-Funktionen erfasst, davon 699 benannt und 291 noch opak. Diese Zahlen
  messen nicht dasselbe wie die 1.511 Dispatch-Slots und widersprechen ihnen
  daher nicht.
- Für jede lokale `FUN_*`-Funktion liegt ein dekompilierter Body vor.
- Die Ghidra-Exporte enthalten Funktionen, Referenzen, Signaturen, Symbole,
  Globals, Datentypen, Variablen und Xrefs.

Damit kennen wir genug für einen ernsthaften Prototyp und viele gezielte
Korrekturen. Wir haben aber weder jede Funktion reverse engineered noch einen
vollständigen Beweis, dass alle framegebundenen Engine-Pfade bekannt sind.
Eine Aussage wie "die gesamte Engine ist gemappt" wäre falsch.

Bereits durch Code oder Evidenz identifizierte 60-FPS-relevante Bereiche:

- Present/VSync und Hauptframe;
- globale Charakterberechnung und Motion Speed;
- ATEL-Frame-Waits;
- Kamera-Bewegungsdauern und Beschleunigungen;
- Fades, Flash und Alpha-Verläufe;
- ausgewählte Effektgeschwindigkeiten;
- Battle-Limit-Timer;
- Menü-Wassereffekt;
- Texturanimation;
- FMV-Texture-Update;
- ATEL-Motion-Aufrufe `5018`, `5021` und `504C`, für die Halbierung als
  Korrektur der doppelten Bewegungsgeschwindigkeit dokumentiert ist.

## Einschätzung nach Stabilität

### Direkt als erste lauffähige Basis umsetzbar

Diese Punkte haben konkrete Hook-Ziele und können ohne Änderung der Gamefiles
in einen ersten Build übernommen beziehungsweise sauber neu implementiert
werden:

- Fahrenheit-Mod-Lifecycle, Konfiguration und Hook-Installation;
- Erzwingen des 60-FPS-Present-Pfads;
- Unterdrücken der Rücksetzung des VSync-Intervalls auf 30 FPS;
- 60-Hz-Delta für `Ch_CalcMain`;
- Verdoppeln nicht-trivialer ATEL-Frame-Waits;
- Retiming der bekannten Kamera-, Fade-, Flash-, Alpha- und Motion-Speed-Pfade;
- Korrektur des Battle-Limit-Timers;
- Prozessspeicher-Patches mit Restore beim Entladen;
- EFL-Auslieferung gleichnamiger Ersatz-WebMs.

"Direkt umsetzbar" bedeutet, dass der Implementierungspfad bekannt ist. Es ist
noch keine Zusage, dass die erste Kombination davon das gesamte Spiel stabil
durchspielt. Global stabil darf der Mod erst nach einer breiten Testmatrix
genannt werden.

### Bekannterweise instabil oder als Endlösung abgelehnt

- FMV-Update nur an jedem zweiten Frame: optisch 30 FPS, daher ausgeschlossen.
- Texturanimation nur jeden zweiten Frame aktivieren: Workaround mit sichtbarem
  beziehungsweise latentem Flimmer- und Zustandsrisiko.
- Aktuelle Particle/VFX-Experimente: Alternate-Frame-/Pause-Hacks können
  flimmern; es existiert zudem Evidenz für einen Access Violation durch
  Shader-Nutzung vor Initialisierung.
- Einfaches `Sleep` als alleiniger Frame-Pacer, wie im Crystal-Echoes-Gerüst:
  nicht genau genug für sauberes Frame-Pacing.
- DLL- oder Asset-Hot-Reload während ein betroffener Pfad aktiv ist. Geladene
  oder gemappte Dateien können alte Handles behalten oder den Reload unsicher
  machen.

### Noch nicht ausreichend unterstützt oder bewiesen

- Vollständige Particle-/VFX-Simulation bei 60 Updates pro Sekunde;
- alle Texturvideo- und Hintergrund-FMV-Sonderfälle;
- lückenlose Abdeckung aller ATEL-Call-Targets und aller unbekannten
  framegebundenen Funktionen;
- alle Minispiele, insbesondere ihre eigenen Timer und Simulationsschritte;
- jede Kampf-, Aeon-, Overdrive-, Status-, UI- und Pause-Sonderlogik;
- Verhalten bei niedriger Renderleistung, Alt-Tab, Ladehitches und variabler
  Refresh Rate;
- 120 FPS oder beliebige variable FPS. Das erste Ziel ist fest 60/59,94;
- native 4K60-WebM-Dekodierung als garantiert stabile Konfiguration;
- exakte Kompatibilität jeder regionalen FMV-/Untertitelvariante.

Diese Liste bedeutet nicht, dass die Bereiche sicher kaputt sind. Sie bedeutet,
dass die vorhandene Knowledge Base noch keinen ausreichenden Stabilitätsbeweis
liefert.

## FMVs

### Originale und Format

Die extrahierten Originalvideos liegen gemäß lokaler Konfiguration auf dem NAS
unter:

```text
\\10.0.10.50\data\archive\final-fantasy-assets\FFX_OriginalAssets\
ffx_data\gamedata\ps3data\video\
```

Bekannter Bestand:

- 95 WebM-Dateien;
- zusammen ungefähr 4,98 GiB;
- Beispiel `opn_us.webm`: 1280x720, 30000/1001 FPS, VP8-Video und
  Vorbis-Audio.

In der Installation liegen sie nicht als frei zu überschreibender Videoordner,
sondern in `FFX_Data.vbf`. Regionale Zuordnungen liegen unter den Video-
Unterordnern `us`, `jp`, `asia` und `cn` in `ffx_videolist.txt`.
`texturevideo` ist ein eigener Steuerdatenbereich und nicht mit den normalen
WebM-FMVs gleichzusetzen.

### Zielkonvertierung

Die neuen Videos sollen dieselben logischen Pfade, Dateinamen, Laufzeiten und
Audiospuren behalten. Dann sind normalerweise keine Änderungen an
`ffx_videolist.txt` erforderlich.

Empfohlene Offline-Pipeline:

1. Original verlustarm dekodieren.
2. Kompressionsartefakte vorsichtig bereinigen, ohne Filmkorn und feine
   Animationen zu zerstören.
3. Ein hochwertiges 4K-Upscale-Master erzeugen.
4. Frames auf exakt 60000/1001 interpolieren, sofern das Spiel mit 59,94 läuft;
   alternativ muss die gesamte Runtime bewusst auf exakt 60,000 ausgelegt
   werden. 59,94 und 60,000 dürfen nicht still vermischt werden.
5. Auf identische Laufzeit und Audio-Synchronität prüfen.
6. Zunächst kompatibel als WebM mit VP8-Video, Vorbis-Audio und `yuv420p`
   kodieren.
7. Über EFL unter demselben VBF-internen Pfad ausliefern.

1080p ist keine Zielgrenze. Es war nur die konservative erste
Stabilitätsstufe. Der bevorzugte Test erzeugt aus derselben kurzen FMV drei
Varianten:

- 1920x1080 bei 59,94 FPS;
- 2560x1440 bei 59,94 FPS;
- 3840x2160 bei 59,94 FPS.

Wenn 4K60 sauber dekodiert, synchron bleibt und keine Speicher- oder
Frame-Pacing-Probleme erzeugt, soll 4K angeboten werden. Gründe für den Test
statt einer Vorabgarantie sind die 32-Bit-Adressraumgrenze des Spiels, deutlich
größere Decode-Surfaces, die CPU-Last von VP8 bei 4K60 und noch ungeklärte
interne Texturgrenzen. Aus 720p entstehen durch Upscaling außerdem keine echten
4K-Quelldetails; der sichtbare Gewinn hängt stark vom Material ab.

Neue Codecs wie VP9 oder AV1 sind erst eine spätere Untersuchung. VP8/Vorbis
minimiert beim ersten Test die Zahl der gleichzeitig veränderten Variablen.

### EFL, VBF und der FMV-Pfad

FMV und EFL sind keine konkurrierenden Konzepte:

- FMV ist der Inhalt und der Wiedergabepfad.
- EFL ist der Mechanismus, der der Engine unsere Ersatzdatei liefert.

Direktes Patchen oder Neupacken des VBF wäre möglich, aber unnötig fragil.
Geänderte Dateigrößen können zusätzliche Size-Table-Anpassungen erfordern.
EFL lässt das Archiv und die EXE unverändert.

Community-Evidenz sagt, dass andere FMVs durch den Fileloader laufen. Der
beobachtete Sonderfall `FAL_jp.webm` passierte den Loader in einer untersuchten
Farplane-Szene nicht; in derselben Diskussion wurde daraus geschlossen, dass
dieses konkrete Video dort wahrscheinlich überhaupt nicht geladen wurde. Das
ist kein Gegenbeweis für normale FMV-Overrides, muss aber in der Testmatrix
berücksichtigt werden.

EFL-Pfade können zur Laufzeit neu indiziert werden. Eine bereits geöffnete
oder geladene FMV sollte trotzdem nicht während der Wiedergabe ersetzt werden.
Die stabile Benutzerkonfiguration legt alle Ersatzvideos vor Spielstart ab.

## Keine 30-FPS-Sonderfälle

Die Anforderung ist nicht "meistens 60 FPS". Folgende Lösungen sind deshalb
ausdrücklich ausgeschlossen:

- Umschalten auf 30 FPS vor oder während einer FMV;
- jedes zweite FMV-Update auslassen;
- jedes zweite Texturanimations-Update auslassen, wenn dadurch effektiv wieder
  30-Hz-Verhalten entsteht;
- 30-Hz-Simulation mit bloß verdoppelter Darstellung als fertige Lösung;
- stilles Zurückfallen auf 30 FPS bei unbekannten Szenen.

Wenn ein Subsystem noch nicht bei 60 Hz korrekt läuft, soll der Test-Build das
protokollieren oder sichtbar fehlschlagen. Er soll den Mangel nicht durch einen
unsichtbaren 30-FPS-Modus kaschieren.

## Live-Injektion und Gamefiles

Für die geplante Lösung müssen keine originalen Gamefiles überschrieben
werden:

- Hooks, Konstanten und Code-Patches werden beim Start in den Prozess injiziert.
- Die originalen Bytes werden für ein sauberes Restore gespeichert.
- Ersatzvideos liegen im Modordner und werden durch EFL aufgelöst.
- `FFX.exe` und `FFX_Data.vbf` bleiben unverändert.
- Entfernen des Modordners stellt den Vanilla-Zustand wieder her.

Live ein- und ausschaltbare Einzelkorrekturen sind für Entwicklung sinnvoll.
Ein kompletter Hot-Unload des globalen Timing-Mods mitten in einer Szene ist
jedoch kein Stabilitätsziel. Der Produktionsmodus installiert die Hooks vor dem
ersten relevanten Engine-Update und entfernt sie erst beim kontrollierten
Shutdown.

## Empfohlene Umsetzungsreihenfolge

### Phase 1: reproduzierbare 60-Hz-Basis

- Neues Fahrenheit-Modul mit sauberem Init/Shutdown und Patch-Restore.
- `PWarpModule`-Hooks portieren, aber jeden Hook einzeln konfigurierbar und
  protokolliert halten.
- Den 30-FPS-FMV-Frameskip und Alternate-Frame-Texturworkaround nicht als
  Standard übernehmen.
- Frame-Pacing und tatsächliche Present-Rate messen.
- Ein kurzer reproduzierbarer Spielstand pro Hauptkontext: Feld, Kampf, Menü,
  Skriptsequenz und FMV.

### Phase 2: echte 60-FPS-FMV-Probe

- Eine kurze, unkritische FMV in 1080p59,94, 1440p59,94 und 4K59,94 erzeugen.
- Gleiche Dateinamen, Dauer und Audiospur beibehalten.
- EFL-Override vor Spielstart laden.
- Decodierung, Audio-Sync, ATEL-Frameabfragen, Untertitel, Übergang in und aus
  der FMV sowie Speicherverbrauch messen.
- Danach den echten 60-Hz-FMV-Updatepfad implementieren; kein Frameskipping.

### Phase 3: systematische Timing-Abdeckung

- Alle `PWarpModule`-Korrekturen isoliert testen.
- Bekannte ATEL-Motion-Targets `5018`, `5021`, `504C` abdecken.
- Unbenannte Call-Targets anhand tatsächlicher 60-FPS-Fehler priorisieren,
  nicht blind alle 629 Slots reverse engineeren.
- VFX/Particle-Pipeline mit Zustands- und Call-Order-Tracing untersuchen.
- Menüs, Kämpfe, Aeons, Overdrives, Blitzball, Tempel, Fahrzeuge,
  Hintergrundvideos und große Story-Szenen in eine Testmatrix aufnehmen.

### Phase 4: Gesamtspiel und Distribution

- Alle 95 FMVs erst nach bestandenem Einzelvideo-Test konvertieren.
- Regionale Varianten und Untertitelpfade prüfen.
- Lange Spieltests, Saves an kritischen Storypunkten und reproduzierbare
  Fehlerszenen verwenden.
- Erst danach eine Konfiguration als "stabil" benennen.
- Mod als ein Fahrenheit-Paket mit DLL, Manifest, Konfiguration und
  `efl/x`-Assetbaum ausliefern.

## Akzeptanzkriterien für "stabil"

Der Mod darf erst als stabil bezeichnet werden, wenn mindestens Folgendes
belegt ist:

- Present und Engine-Update bleiben außerhalb von Ladebildschirmen bei 60 Hz;
- keine Szene schaltet absichtlich auf 30 FPS;
- Spiel-, Kampf- und Animationsgeschwindigkeit entsprechen Vanilla-Zeit;
- ATEL-Waits, Kamera, Fades, Timer und Eingaben laufen zeitlich korrekt;
- FMVs werden als echte 59,94/60-FPS-Dateien mit korrekter Dauer und Audio-Sync
  abgespielt;
- Particle/VFX zeigen kein Alternate-Frame-Flimmern und keinen Shader-
  Initialisierungscrash;
- Menüs und Minispiele bleiben bedienbar und zeitlich korrekt;
- Laden, Speichern, Pause, Alt-Tab und Szenenwechsel hinterlassen keine
  verlorenen Hooks oder inkonsistenten Timing-Zustände;
- die originalen Gamefiles bleiben byte-identisch;
- Deinstallation durch Entfernen des Modpakets stellt Vanilla wieder her.

## Wichtige lokale Quellen für die nächste Session

- `AGENTS.md`, besonders Rule 0 und die Trennung zwischen Knowledge Base und
  Mod-Runtime.
- `packs/ffx/` für den schnellen Überblick.
- `canonical/ffx/engine/` für Funktionen, Strukturen und Globals.
- `canonical/ffx/scripts/` und `target/text/` für ATEL.
- `docs/forensics/` und `docs/roadmap.md` für Evidenz und offenen Stand.
- `.workspace/intermediate/ghidra-server/` und
  `inputs/decompilation/FFX.exe.c` für Callgraph und Bodies.
- `.workspace/external/fahrenheit/README.md` für aktuelle Fahrenheit-
  Installation und EFL-Kompatibilität.
- `canonical/ffx/community/discord_llm_findings.json` für die zitierten
  Community-Beobachtungen.
- die beiden unter "Ausgangsbasen" genannten Source-Exports.
- der konfigurierte NAS-Root für die 95 Original-WebMs.

Vor neuer Reverse-Engineering-Arbeit immer zuerst `kb-serve` und die
Knowledge-Base-Werkzeuge `search`, `lookup`, `coverage`, `symbol`, `related` und
`path` verwenden. Bei Aussagen zum aktuellen Abdeckungsstand die Index-
Generation und ihre Aktualität prüfen.

## Offene Entscheidungen vor Implementierungsbeginn

- Soll die feste Zielrate 60000/1001 oder exakt 60,000 Hz sein? FMV-Encoding,
  Timer und Tests müssen dieselbe Entscheidung verwenden.
- Welche konkrete Fahrenheit-Version ist das Runtime-Ziel?
- Entsteht ein eigener `FFX 60 FPS`-Mod oder wird ein bestehender Mod erweitert?
- Welche kurze FMV und welche Saves bilden die erste reproduzierbare Testbasis?
- Ist 4K60 die Standardauslieferung oder eine optionale Asset-Stufe neben
  1080p/1440p?

Die technische Empfehlung ist: festes 60000/1001-Ziel, eigener Fahrenheit-Mod,
`PWarpModule` als Hook-Grundlage, Crystal Echoes nur als Lifecycle-/Konfigurations-
Inspiration, echte interpolierte FMVs über das integrierte EFL und zuerst ein
kleiner beweisbarer Vertikalschnitt statt einer sofortigen Konvertierung aller
95 Videos.
