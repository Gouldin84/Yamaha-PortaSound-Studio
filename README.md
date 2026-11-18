# Yamaha PortaSound Studio

A high-performance standalone MIDI workstation, real-time voice remapper, and multi-tab recording & SysEx studio designed for the **Yamaha PortaSound PSS-790** (AWM) and **PSS-680 / PSS-480 / PSS-780** (2-Operator FM) keyboard synthesizers.

[![Release](https://img.shields.io/github/v/release/Gouldin84/Yamaha-PortaSound-Studio)](https://github.com/Gouldin84/Yamaha-PortaSound-Studio/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-green.svg)]()
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)]()

---

## Overview

**Yamaha PortaSound Studio** is a unified, high-performance standalone MIDI workstation, real-time voice remapper, and multi-tab recording & SysEx studio supporting both major eras of classic Yamaha PortaSound synthesizer architecture:
1. **Yamaha PortaSound PSS-790** (released 1990): 28-voice polyphonic Advanced Wave Memory (AWM) sample-playback tone generator with 100 preset voices, 50 rhythm styles, vector synth joystick, and 16-channel multi-timbral MIDI.
2. **Yamaha PortaSound PSS-680 / PSS-480 / PSS-780** (released 1988–1989): 12-voice polyphonic 2-Operator FM (Frequency Modulation) sound engine (YM3420 / OPLL family) with 100 FM voices, 100 rhythm styles, 32-drum Hand Percussion & Custom Drummer, 5 digital synthesizer memory banks, and multi-timbral Sound Source Mode 99.

Because vintage Yamaha synthesizers predate General MIDI (GM), standard MIDI files sound distorted, transposed, or unaligned without translation. Yamaha PortaSound Studio provides real-time translation, hardware playback, direct recording, and bidirectional SysEx preset management across three dedicated studio tabs:
- **Player & Voice Remapper**: Real-time translation, hardware playback, and batch file conversion for General MIDI files.
- **MIDI Recording Studio & Vault**: Direct capture from keyboard performance, styles, and vector pad, with native take preservation (`PSS790_RECORDING:v1`), standalone take vault, stereo VU meters, live chord analyzer, and reverse export to standard General MIDI.
- **SysEx Vault & Preset Librarian**: Bidirectional System Exclusive memory dumps, voice parameter backup, hex payload inspection, and 1-click restore to keyboard memory.

---

## Key Features

### 🎛️ Dual-Engine Architecture & Model Dropdown Selector
- **Interactive Synthesizer Model Selector**:
  - Top navigation dropdown featuring custom photorealistic 3D isometric squircle icons matching the retro-futuristic dark neon aesthetic.
  - Switch on the fly between **Yamaha PSS-790 (AWM Engine)** and **Yamaha PSS-680 (2-Op FM Engine)**.
  - Active selection persists across sessions in `localStorage`.
- **Dynamic UI Reconfiguration**:
  - Switching models automatically updates the header branding, engine badge, voice override dropdowns, drum mappings, and SysEx tools.
  - Channel voice selectors dynamically populate with the active model's 100 preset voices (PSS-790 AWM Chart 2 voices vs PSS-680 FM voices #00–#99).

---

### 🎹 Yamaha PSS-680 / PSS-480 / PSS-780 2-Operator FM Synthesis
- **2-Operator FM Sound Engine (YM3420)**:
  - Full support for Yamaha's iconic 2-operator FM architecture (Modulator + Carrier) with 4 sine waveform tables: Standard Sine ($\sin \theta$), Squared Sine ($\sin^2 \theta$), Sine Half-Wave, and Squared Sine Half-Wave.
  - Fine detune ($\text{DT1} = -3\dots +3$), coarse detune ($\text{DT2} = +600\text{ cents}$), frequency multipliers ($\text{MUL} = 0\dots 15$), and modulator feedback ($\text{FB} = 0\dots 7$).
- **Sound Source Mode 99 (Multi-Timbral)**:
  - Dynamic 12-voice polyphony allocation shared across Channels 1–15.
  - Full dynamic Note-On velocity sensitivity ($9nH, v = 1\dots 127$) for expressive velocity-sensitive playback.
  - Extended 7-octave playable note range (MIDI Notes 24 to 108 / $C_0$ to $C_7$).
- **Complete Preset & Style Tables**:
  - All 100 PSS-680 Preset Voices (#00–#99) from `00: Synth Brass` to `99: Sine Wave`.
  - All 100 PSS-680 Rhythm Styles (#00–#99) from `00: Rhythm & Blues 1` to `99: Barbershop Quartet`.
- **32-Drum Hand Percussion & Custom Drummer Mapping**:
  - Translates and triggers all 32 PCM percussion sounds across Pad Sets A, B, C, D on MIDI Channel 16 (MIDI Notes 38 to 79).
  - Preserves velocity-sensitive drum dynamics and accent playback.
- **Deep System Exclusive (SysEx) Engine**:
  - **Format 00H (FM 2-Operator Voice Data)**: 33 decoded bytes / 66 nibbles exposing deep synthesis parameters hidden from the physical keyboard panel (envelope rates AR, D1R, D2R, RR, SRR, breakpoint D1L, level key scaling LKS HI/LO, rate scaling RKS, vibrato delay VDT, and waveform selection).
  - **Format 01H (Melody Memory)**: 5026-byte sequence dumps for all 5 melody memory banks with track linking and portamento speed.
  - **Format 02H (Chord Memory)**: 202-byte chord sequence dumps covering 13 chord types, measure/beat counters, and orchestration switches.
  - **Format 03H (Rhythm Pattern / Custom Drummer)**: 358-byte pattern dumps for 32 instrument tracks across 8 beats with quantize grids and accent flags.
  - **Verified Checksum & Nibble Protocol**: Strict implementation of high-nibble-first transmission and Yamaha 7-bit two's complement checksum formula:
    $$\text{Checksum} = (128 - (\sum \text{DecodedBytes} \pmod{128})) \pmod{128}$$

### 📻 Tab 1: Player & Voice Remapper
- **Quick-Pick Folder Tree Sidebar (Jukebox Mode)**:
  - Docked collapsible 290px library browser with real-time search, recursive nested subfolder navigation, folder expand/collapse toggles, and item badges.
  - Symmetrical multi-way directory mirroring across `Music\Midi\GM Midi`, `Music\Midi\PSS-790 Midi`, and `Music\Midi\PSS-680 Midi`, with automatic folder parity on creation and rename.
  - Recursive nested tree rendering (e.g. `Video Games\Amiga`, `Video Games\Arcade`), collapsible root library song grouping, and 1-click Collapse All / Expand All toggle.
  - **Drag & Drop Reorganization**: Drag single or multiple songs, or entire folder trees, directly into destination folders or library roots with live drop target highlighting.
  - **Multi-Select & Batch Actions**: Select multiple songs via row checkboxes, `Shift + Click` ranges, or the `☑` select mode toggle. Batch move or batch delete multiple songs in one click with mirrored counterpart synchronization.
  - **Right-Click File Management**: Add new folders, rename files/folders, move songs to destination folders, reveal in Explorer, or delete items directly from the tree context menu.
  - **Linked PSS Deletion & Move**: Symmetrically moves or deletes matching PSS-790 and PSS-680 optimized versions in the mirrored counterpart libraries with a single confirmation.
  - **Split-Click Interaction**: Single-click loads song metadata and channel strips in idle mode for inspection and tweaking; double-click or `Enter` starts playback immediately.
  - **Portable Per-Song Voice Memory**: Custom voice selections persist in `Music\Midi\.pss790\overrides.json` (namespaced per model: `pss790:relPath` vs `pss680:relPath`) and automatically recall when reloading songs. Moving or renaming files/folders dynamically migrates voice assignments without data loss. Dedicated "Reset to GM Voices" button resets overrides back to defaults.
- **Simultaneous Dual-Keyboard Library Alignment & Sync (Sync Folders)**:
  - Dedicated **Sync** button in the sidebar footer right beside **Refresh**.
  - Interactive diff analysis modal scanning `GM Midi`, `PSS-790 Midi`, and `PSS-680 Midi` simultaneously to detect:
    - 📥 **Missing PSS Files**: Newly added GM songs awaiting conversion for either keyboard.
    - 🔄 **Outdated PSS Files**: GM source files modified after their PSS conversion.
    - 🗑️ **Orphan PSS Files**: Ghost files in `PSS-790 Midi` or `PSS-680 Midi` whose GM original was renamed or deleted.
  - Incremental, non-destructive execution with live progress reporting, automatic pruning of orphaned keys in `VoiceOverrideStore`, and cleanup of empty orphan directories.
  - Optional checkbox to force re-convert the entire library with the latest hardware rules for both models.
- **Smart Polyphony Guard & Voice Protections**:
  - Dynamically enforces model-specific hardware limits: 28 voices for PSS-790, and 12 voices for PSS-680 (Sound Source Mode 99).
  - Automatically drops low-velocity background percussion and density subdivisions during heavy congestion while strictly shielding melody, vocal, bass, and kick/snare parts.
  - Hi-hat choke grouping (closed hi-hat truncates open hi-hat ring).
  - Truncates lingering percussion durations to free up AWM sound generator elements.
  - Overlapping same-pitch note clamping and zero-duration protection to prevent hardware voice-stealing bugs.
- **Format-Aware 16-Channel Mixer & Voice Inspector**:
  - **Real-Time 16-Channel Activity LEDs**: Live responsive glowing LED meters for every MIDI channel (green for melodic channels 1–15, amber for rhythm channel 16) with polyphonic chord hold and minimum pulse decay so rapid staccato hits and hi-hats flash brightly.
  - **Hardware Mute & Solo Channel Isolation**: Direct hardware note suppression for muted and un-soloed channels, with background parameter continuity (volume, pan, pitch bend, voice) for instant glitch-free unmuting.
  - **Real-Time Playback Progress Scrubber**: Smooth high-precision progress bar with live time displays and seek-on-release scrubbing.
  - Live channel strips with volume sliders, mute/solo buttons, and voice override dropdowns.
  - Inspecting native PSS-790 files correctly displays true Chart 2 hardware voices (e.g., `PIANO`, `VIOLIN 1`, `ELEC BASS 1`) and identifies Channel 16 as fixed rhythm, while GM files display General MIDI instrument labels.
- **Microsoft GS Wavetable Synth Unmodified GM Mode**:
  - When output is set to Microsoft GS Wavetable Synth, the remapping engine automatically disengages (Channel 10 drums and standard GM program numbers are preserved 1:1), enabling pure, unmodified General MIDI auditioning on PC speakers.
- **Live MIDI Hot-Swap**:
  - Dynamically detects USB-MIDI cable connections and disconnections in real time without restarting the application; active playback pauses gracefully if an interface is unplugged.
- **Media Player Keyboard Shortcuts**:
  | Key | Action |
  |---|---|
  | `Space` | Play / Pause toggle |
  | `Esc` | Stop & All-Notes-Off Panic |
  | `Arrow Up` / `Arrow Down` | Browse songs in visible library list |
  | `Enter` | Play selected song immediately |
  | `Arrow Left` / `Arrow Right` | Seek backward / forward 5 seconds |
  | `Ctrl + B` | Toggle quick-pick sidebar |
- **Automatic Channel Routing**: Shifts GM percussion from Channel 10 to Channel 16 (PSS-790's fixed rhythm channel) and safely moves any melodic data on Channel 16 to free melodic channels.
- **Percussion Note Translation & Extended Fallbacks**:
  - Converts standard GM drum notes (35–81) to PSS-790 percussion notes (36–85, Chart 1) with rock/metal snare punch (GM 40 Electric Snare &rarr; Note 49 `SNARE HI`) and dual-zone ride cymbal wash/bell separation (GM 51 &rarr; Note 63 `RIDE CYMBAL EDGE`, GM 53 &rarr; Note 62 `RIDE CYMBAL CUP`).
  - Musical fallbacks for extended Roland GS / GM drum notes outside the standard range (slaps &rarr; rimshots, sticks &rarr; woodblocks, shakers &rarr; maracas, bell trees &rarr; triangles, surdos &rarr; bass drums).
- **Full-Register Melodic Range & Bassline Preservation**:
  - Preserves full bass guitar ($E_1=28$ to $B_1=35$) and low keyboard registers down to Note 24 ($C_1$), eliminating artificial octave folding in basslines.
  - Expands the high register up to Note 108 ($C_8$) for flutes, piccolos, glockenspiels, and synth leads, with intelligent Note 88 ($E_6$) sweet-spot protection for acoustic piano/strings to prevent vintage 12-bit AWM multisample aliasing.
- **Non-Linear Voice Translation**: Converts GM Program Change events to the specific non-linear MIDI program numbers required by the PSS-790 (Chart 2) to trigger appropriate voices (00–99).
- **Dynamic RPN 0,0 Pitch Bend Sensitivity & Scaling**:
  - Pre-scans controller streams for Registered Parameter Number (RPN 0,0) pitch bend range sensitivity declarations.
  - Retains full 1:1 scale factor for $\pm 12$-semitone octave slides (e.g., *The Moon.mid* intro), while standard $\pm 2$-semitone GM bends are scaled by 1/6 for the PSS-790's fixed 12-semitone hardware pitch wheel.
- **Real-Hardware Serial DIN Buffer & Voice Protections**:
  - **Foreign SysEx Stripping**: Filters Roland GS and Yamaha XG System Exclusive reset dumps (which can burst 250+ messages at once), preventing receive FIFO buffer overruns (`bu full` lockups) across 31,250 baud serial cables.
  - **Active Channel Missing Program Change Auto-Initialization**: Injects `ProgramChangeEvent(3)` (Voice 00: `PIANO`) at Tick 0 on active channels lacking an initial voice assignment, preventing voice bleeding from prior songs.
- **Recursive Batch Converter**: 1-click batch converter for folders and nested subdirectories of MIDI files with relaxed vintage MIDI chunk parsing and real-time progress tracking.

---

### 🎙️ Tab 2: MIDI Recording Studio & Take Vault
- **Direct PSS-790 Recording**: Captures live performances, vector synthesis sweeps, and built-in auto-accompaniment styles directly from the keyboard's MIDI OUT.
- **Dedicated Standalone Recordings Library (Exclusive to Tab 2)**:
  - Separate project take vault stored in `Music\Midi\Recordings\` (`AppPaths.RecordingsDir`), keeping work-in-progress takes segregated from published songs.
  - Automatic background preservation of recorded takes upon recording stop with captured notes.
  - Browse all recorded takes with duration, note count, formatted timestamp, and file size in KB.
  - **Instant Take Auditioning**: Preview and stop recordings directly within Tab 2 (`▶ Preview` / `⏹ Stop`).
  - **Export to General MIDI**: Reverse-remaps Channel 16 drums to Channel 10 and PSS-790 voices back to standard GM programs, saving to `GM Midi/` with instant reflection in the Tab 1 jukebox.
  - **Export to PSS-790 Library**: Embeds native hardware marker and saves to `PSS-790 Midi/` for physical keyboard playback.
  - Inline rename, deletion, and real-time search filter for past takes.
  - "Open Folder" button revealing the recordings directory directly in Windows Explorer.
- **Stereo Master Peak Level VU Meters**:
  - Dual 12-segment graduated LED ladder meters (Left & Right) with calibrated dB scale (-24dB to +3dB clip).
  - 60 FPS ballistic physics with instantaneous velocity peak attack, smooth exponential decay, and peak-hold indicators.
  - Dynamic stereo pan spread responsive to inbound MIDI channels.
- **Real-Time Live Chord & Musical Pitch Analyzer**:
  - Phosphor green Studio LCD display detecting chord names, roots, qualities, and inversions (Major, Minor, 7ths, Maj7, Min7, Sus4, Sus2, Dim, Aug, 6th, Add9, 9th, compound chords, and slash inversions like `C Maj / E`).
  - Single-note pitch identification and 2-note musical intervals (e.g. `C4 - G4 (Perfect 5th)`).
- **Real Musical Pitch Displays Across all 16 Inbound Channel Cards**:
  - Displays actual live musical note names (`C4`, `G#3`, `Bb5`, etc.) and strike velocities on all 16 inbound channel cards.
- **Native Take Metadata Marker (`PSS790_RECORDING:v1`) & Passthrough Protection**:
  - Automatically embeds non-destructive meta text marker (`PSS790_RECORDING:v1`) on Track 0 of all takes.
  - `Remapper.DetectMidiFormat()` detects `PSS790_RECORDING` case-insensitively and activates **100% Passthrough Mode**, preventing accidental double-translation or note transposition when loaded into the player.
  - `ReverseRemapper.ReverseRemapToGeneralMidi()` cleanly strips both `PSS790_REMAPPED` and `PSS790_RECORDING` markers when exporting takes to GM.
- **Anti-Feedback Loop Guard**: Software MIDI Thru is disabled by default with an explicit opt-in toggle to safeguard against destructive feedback loops caused by the PSS-790's internal soft-thru merger.
- **DAW Transport & Timing Assist**:
  - Large digital timecode display (`00:00:00.0`) and musical bar/beat counter (`BAR 01 : 1`).
  - Configurable tempo (40 to 240 BPM) with synchronized slider and number inputs.
  - Visual 1-bar (4 beat) count-in countdown banner before capture begins.
  - Built-in Web Audio metronome click track (accented high click on beat 1).

---

### 💾 Tab 3: SysEx Vault & Preset Librarian
- **Bidirectional System Exclusive Support**:
  - Send and receive native Yamaha PSS-790 SysEx dumps (`F0 43 ... F7`).
  - Dedicated storage vault in `Music\Midi\SysEx\` (`AppPaths.SysExDir`).
- **Real-Time SysEx Capture**:
  - Listens on the selected MIDI In port for incoming bulk dumps, Vector Play recordings, and voice parameter updates.
  - Live hex view with automatic byte counting and manufacturer identification (Yamaha ID `43`).
  - Save received dumps as standard `.syx` files.
- **Keyboard Restore & Bulk Transmit**:
  - Transmit saved `.syx` files back to the PSS-790 to restore user voices, vector configurations, and style memories.
  - Paced hardware-safe transmission throttles SysEx packets to prevent synthesizer buffer overflows.
- **SysEx Hex Inspector**:
  - Built-in hex inspection modal with formatted byte offset viewing, ASCII representations, and payload validation.

---

### 🌓 Dark Studio & Light Studio Themes
- 1-click theme switcher toggle in the top-right toolbar.
- High-contrast, meticulously calibrated styling across both themes for mixing console cards, LCD displays, VU meters, modals, and status badges.

---

### 🎮 Asus ROG Ally & Handheld Touch Optimization
- **Fixed-Viewport Workstation Layout**: Full viewport locking (`h-screen overflow-hidden`) eliminates page scrolling, matching professional DAWs like Ableton Live and FL Studio.
- **Independent Two-Pane Scrolling**:
  - **Left Sidebar**: 100% height pane where only the file tree scrolls internally. The search bar, selection buttons, docked batch action bar, and refresh footer remain **100% visible on screen at all times**.
  - **Right Workspace**: Independent scrollable pane for the transport deck, 16-channel mixer, and studio monitors.
- **Touch-Friendly Hit Targets**:
  - Enlarged tap targets ($\ge 36\text{px}$–$44\text{px}$) across buttons and controls.
  - Checkboxes automatically visible on touchscreens with generous hit zones.
  - Enlarged $22\text{px}$ slider thumbs for effortless finger volume and tempo tweaking.
  - 120Hz fluid kinetic touch scrolling via `touch-action: pan-y`.
- **DPI Scaling & Handheld Dimensions**: Tailored for the Asus ROG Ally Z1 Extreme ($1280 \times 720$ effective resolution at 150% Windows scaling) and compact laptop displays with auto-scaling modals and compact single-line navigation.

---

### 📁 Standardized User Library (`C:\Users\%USERNAME%\Music\Midi\`)
- **`GM Midi\`**: Drop zone for raw General MIDI files (automatically remapped during playback and conversion).
- **`PSS-790 Midi\`**: Output destination for converted files and native PSS-790 takes (played in direct Passthrough Mode).
- **`Recordings\`**: Standalone vault for raw studio takes, auditioning, and GM/PSS export.
- **`SysEx\`**: Repository for System Exclusive dumps, voice presets, and vector memories.
- **1-Click "Folder" Button**: Top navigation button to instantly reveal `Music\Midi\` in Windows Explorer.
- **Visual Format Badging**:
  - `🎵 GM`: General MIDI file requiring auto-remapping.
  - `⚡ PSS-790`: Native PSS-790 file with direct passthrough engaged.
  - `⏺ REC`: Native studio recording take with passthrough protection.

---

## Installation & Download

### Latest Release: v2.1.0 (Yamaha PortaSound Studio)

| Download Asset | File Type | Size | Description |
| :--- | :--- | :--- | :--- |
| **[`PSS-Studio.exe`](https://github.com/Gouldin84/Yamaha-PortaSound-Studio/releases/download/v2.1.0/PSS-Studio.exe)** | Windows Executable | ~73 MB | **Recommended**: Standalone single-file executable with zero runtime dependencies. |
| **[`PSS-Studio-win-x64.zip`](https://github.com/Gouldin84/Yamaha-PortaSound-Studio/releases/download/v2.1.0/PSS-Studio-win-x64.zip)** | ZIP Archive | ~68 MB | Complete distribution archive with `PSS-Studio.exe`, `Launch-Studio.bat`, and ROG Ally guide. |
| **[`Launch-Studio.bat`](https://github.com/Gouldin84/Yamaha-PortaSound-Studio/releases/download/v2.1.0/Launch-Studio.bat)** | Batch Script | <1 KB | Convenience launcher script. |

> All official standalone binaries are published on the **[GitHub Releases Page](https://github.com/Gouldin84/Yamaha-PortaSound-Studio/releases)**.

#### Summary of Functionality in v2.1.0
- **Multi-Model Yamaha FM & AWM Support**: Seamlessly switch between the **PSS-790** (Advanced Wave Memory) and the **PSS-680 / PSS-480 / PSS-780** (2-Operator FM Synthesizer) engines directly from the top navigation dropdown.
- **Full Engine Mirror Image & Complete Separation**:
  - **Dynamic Jukebox Library**: Switching models immediately re-renders the library sidebar to display `⚡ PSS-680 Ready` (reading from `Music\Midi\PSS-680 Midi`) or `⚡ PSS-790 Ready` (reading from `Music\Midi\PSS-790 Midi`), maintaining tailored conversions for each keyboard.
  - **16-Channel Mixer Tailoring**: Channels 1–15 support live voice overrides across all 100 FM presets (#00–#99) for PSS-680 or 100 AWM presets for PSS-790. Channel 16 is fixed to 32 Hand Percussion Instruments for PSS-680 or Chart 1 Rhythm for PSS-790.
  - **Smart Polyphony Guard Calibration**: Automatically enforces the 12-voice hardware polyphony limit for PSS-680 (Sound Source Mode 99) while maintaining the 28-voice limit for PSS-790, dynamically culling incidental percussion during note bursts while strictly shielding lead melody and basslines.
  - **Studio Recorder Native Separation**: Records live takes directly into model-specific native formats (`PSS680_RECORDING:v1` to `Music\Midi\PSS-680 Midi` or `PSS790_RECORDING:v1` to `Music\Midi\PSS-790 Midi`) with 100% passthrough playback.
  - **Dedicated SysEx Workstations**: Tailored hardware command bars for each model—PSS-680 features 1-click Mode 99 initiation (`F0 43 76 00 05 99 F7`), FM Voice Bank Dump (`00H`), Melody Memory Dump (`01H`), Chord Memory Dump (`02H`), and Custom Drummer Dump (`03H`), alongside master volume and panic resets.
- **Simultaneous Dual-Keyboard Library Alignment & Synchronization**:
  - The **Sync Folders** button and **Batch Remapper** now analyze, convert, and synchronize **both** `PSS-790 Midi` and `PSS-680 Midi` libraries simultaneously against `GM Midi`.
  - Adding or modifying General MIDI files keeps both hardware formats in 100% parity, ensuring immediate physical playback regardless of which keyboard synthesizer is currently connected.
- **Dedicated 3D Isometric Hardware Icons**: Hand-crafted 3D isometric squircle icons rendered to faithfully depict each hardware synthesizer's physical front panel, louvered/domed speaker styling, display, and pad layouts.
- **100 FM Voices & 100 Rhythm Styles**: Complete native preset tables for PSS-680 (#00–#99) integrated into the 16-channel mixer and auto-accompaniment monitors.
- **32-Drum Hand Percussion Translation**: Channel 16 percussion remapping supporting all 4 pad banks (Sets A, B, C, D) with full dynamic Note-On velocity sensitivity.
- **Standalone `dist\` & `bin\Publish\` Distributions**: Packaged as `PSS-Studio.exe` with 1-click `build.bat` and `Launch-Studio.bat` scripts.

### Running from Project Clone / Local Build
If you have cloned the repository, standalone executables are ready in either the `dist\` or `bin\Publish\` folders:
```powershell
# Run from dist folder
.\dist\PSS-Studio.exe

# Or use the quick launcher
.\Launch-Studio.bat
```

---

## Hardware Setup Guide

### 1. Cabling
- **For Playback**: Connect your PC's USB-MIDI Interface `OUT` &rarr; Yamaha PSS `MIDI IN`.
- **For Recording**: Connect Yamaha PSS `MIDI OUT` &rarr; PC's USB-MIDI Interface `IN`.
- *Caution*: Keep the application's **MIDI Thru Forward** switch disabled unless intentionally routing to a separate sound module; vintage Yamaha keyboards automatically merge MIDI IN data into their MIDI OUT jack, which can create a loopback feedback storm if echoed by the PC.

### 2. Configure Multi-Timbre Mode
- **PSS-790**: Press `RECEIVE CH/CLOCK` and verify channels 1–16 are enabled (`<E 1>` through `<E 16>`).
- **PSS-680 / 480 / 780**: Press `MIDI MODE [c]` and select `c.99` (Sound Source / Multi-timbral mode).

### 3. Launch & Connect
1. Select your USB-MIDI interface in the application's **MIDI Port** (Output) and **MIDI In** (Input) dropdowns.
2. Select your active synthesizer model (**PSS-790** or **PSS-680**) from the top header dropdown.
3. Drag and drop `.mid` files into `C:\Users\%USERNAME%\Music\Midi\GM Midi\` or click **Load File**.
4. Customize voice assignments if desired, and click **Play**.

---

## Technical Documentation

Detailed MIDI implementation tables, non-linear voice matrices, percussion mappings, and hardware quirks are documented in:
- [docs/PSS-790_MIDI_Reference.md](docs/PSS-790_MIDI_Reference.md): PSS-790 AWM specifications & remapping matrix
- [docs/PSS680_SYSEX_SPEC.md](docs/PSS680_SYSEX_SPEC.md): PSS-680 / 480 / 780 2-Operator FM synthesis & SysEx specifications

---

## Building from Source

Requirements:
- Windows 10/11 x64
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# Clone the repository
git clone https://github.com/Gouldin84/Yamaha-PortaSound-Studio.git
cd Yamaha-PortaSound-Studio

# Build solution and run automated unit & integration tests
.\build.bat
# Or manually:
dotnet test

# Publish standalone single-file executable
dotnet publish src/PSS790Player/PSS790Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin/Publish/
```

---

## Legal Disclaimer & Acknowledgements

**Yamaha Trademark Disclaimer**:
Yamaha, PortaSound, PSS-790, and AWM are registered trademarks of Yamaha Corporation. This software is an independent open-source enthusiast project developed by Gouldin84 for vintage hardware interoperability, preservation, and non-commercial educational study. It is not affiliated with, sponsored by, endorsed by, or certified by Yamaha Corporation.

**Third-Party Open-Source Credits**:
- [Melanchall.DryWetMidi](https://github.com/melanchall/drywetmidi) (MIT License) - High-performance .NET MIDI playback, parsing, and device I/O
- [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) (Microsoft Software License) - Embedded Chromium desktop UI runtime
- [Tailwind CSS](https://tailwindcss.com) (MIT License) - Utility-first modern CSS framework
- [Inter Font](https://rsms.me/inter/) (SIL Open Font License 1.1) - Interface typography

For complete license texts, copyright notices, and community credits, see [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md).

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
