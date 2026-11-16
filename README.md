# Yamaha PSS-790 MIDI Player, Remapper & Recording Studio

A high-performance standalone MIDI workstation, real-time voice remapper, and two-way recording studio designed specifically for the **Yamaha PortaSound PSS-790** keyboard synthesizer.

[![Release](https://img.shields.io/github/v/release/Gouldin84/Yamaha-PSS-790-Midi-Player-Converter)](https://github.com/Gouldin84/Yamaha-PSS-790-Midi-Player-Converter/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-green.svg)]()
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)]()

---

## Overview

The **Yamaha PortaSound PSS-790** (released in 1990) features a 28-voice polyphonic Advanced Wave Memory (AWM) tone generator with 100 preset voices, 50 built-in rhythm styles, a vector synth joystick pad, and 16-channel multi-timbral MIDI reception. However, because it predates the General MIDI (GM) standard, standard MIDI files sound distorted, transposed, or unaligned without translation.

This application provides a complete bidirectional workstation:
1. **Player & Voice Remapper**: Real-time translation, hardware playback, and batch file conversion for General MIDI files.
2. **MIDI Recording Studio**: Direct capture from the PSS-790 keyboard, styles, and vector pad, with native 1:1 playback preservation and reverse export to standard General MIDI for modern DAWs.

---

## Key Features

### 📻 Tab 1: Player & Voice Remapper
- **Quick-Pick Folder Tree Sidebar (Jukebox Mode)**:
  - Docked collapsible 290px library browser with real-time search, recursive nested subfolder navigation, folder expand/collapse toggles, and item badges.
  - Symmetrical two-way directory mirroring between `Music\Midi\GM Midi` and `Music\Midi\PSS-790 Midi`, with automatic folder parity on creation and rename.
  - Recursive nested tree rendering (e.g. `Video Games\Amiga`, `Video Games\Arcade`), collapsible root library song grouping, and 1-click Collapse All / Expand All toggle.
  - **Drag & Drop Reorganization**: Drag single or multiple songs, or entire folder trees, directly into destination folders or library roots with live drop target highlighting.
  - **Multi-Select & Batch Actions**: Select multiple songs via row checkboxes, `Shift + Click` ranges, or the `☑` select mode toggle. Batch move or batch delete multiple songs in one click with mirrored counterpart synchronization.
  - **Right-Click File Management**: Add new folders, rename files/folders, move songs to destination folders, reveal in Explorer, or delete items directly from the tree context menu.
  - **Linked PSS Deletion & Move**: Symmetrically moves or deletes matching PSS-790 optimized versions in the mirrored counterpart library with a single confirmation.
  - **Split-Click Interaction**: Single-click loads song metadata and channel strips in idle mode for inspection and tweaking; double-click or `Enter` starts playback immediately.
  - **Portable Per-Song Voice Memory**: Custom voice selections persist in `Music\Midi\.pss790\overrides.json` and automatically recall when reloading songs. Moving or renaming files/folders dynamically migrates voice assignments without data loss. Dedicated "Reset to GM Voices" button resets overrides back to defaults.
- **Library Alignment & Sync System (Sync Folders)**:
  - Dedicated **Sync** button in the sidebar footer right beside **Refresh**.
  - Interactive diff analysis modal scanning both directories to detect:
    - 📥 **Missing PSS Files**: Newly added GM songs awaiting conversion.
    - 🔄 **Outdated PSS Files**: GM source files modified after their PSS conversion.
    - 🗑️ **Orphan PSS Files**: Ghost files in `PSS-790 Midi` whose GM original was renamed or deleted.
  - Incremental, non-destructive execution with live progress reporting, automatic pruning of orphaned keys in `VoiceOverrideStore`, and cleanup of empty orphan directories.
  - Optional checkbox to force re-convert the entire library with the latest hardware rules.
- **Format-Aware 16-Channel Mixer & Voice Inspector**:
  - Live channel strips with volume sliders, mute/solo buttons, and voice override dropdowns.
  - Inspecting native PSS-790 files correctly displays true Chart 2 hardware voices (e.g., `PIANO`, `VIOLIN 1`, `ELEC BASS 1`) and identifies Channel 16 as fixed rhythm, while GM files display General MIDI instrument labels.
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
  - Converts standard GM drum notes (35–81) to PSS-790 percussion notes (36–85, Chart 1).
  - Musical fallbacks for extended Roland GS / GM drum notes outside the standard range (slaps &rarr; rimshots, sticks &rarr; woodblocks, shakers &rarr; maracas, bell trees &rarr; triangles, surdos &rarr; bass drums).
- **Non-Linear Voice Translation**: Converts GM Program Change events to the specific non-linear MIDI program numbers required by the PSS-790 (Chart 2) to trigger appropriate voices (00–99).
- **Dynamic RPN 0,0 Pitch Bend Sensitivity & Scaling**:
  - Pre-scans controller streams for Registered Parameter Number (RPN 0,0) pitch bend range sensitivity declarations.
  - Retains full 1:1 scale factor for $\pm 12$-semitone octave slides (e.g., *The Moon.mid* intro), while standard $\pm 2$-semitone GM bends are scaled by 1/6 for the PSS-790's fixed 12-semitone hardware pitch wheel.
- **Real-Hardware Serial DIN Buffer & Voice Protections**:
  - **Foreign SysEx Stripping**: Filters Roland GS and Yamaha XG System Exclusive reset dumps (which can burst 250+ messages at once), preventing receive FIFO buffer overruns (`bu full` lockups) across 31,250 baud serial cables.
  - **Active Channel Missing Program Change Auto-Initialization**: Injects `ProgramChangeEvent(3)` (Voice 00: `PIANO`) at Tick 0 on active channels lacking an initial voice assignment, preventing voice bleeding from prior songs.
  - **Overlapping Same-Pitch Note Clamping**: Automatically truncates earlier same-pitch notes on identical channels if their duration overlaps a subsequent Note-On, preventing vintage voice-stealing circuit desynchronization.
  - **Zero-Duration Clamping**: Enforces a minimum note length of 1 tick to prevent hardware hanging notes.
- **Smart Polyphony Optimizer**:
  - Clamps lingering percussion note durations to free up AWM sound generator elements.
  - Implements hi-hat choke grouping (closed hi-hat truncates open hi-hat).
  - During peak polyphony congestion ($\ge 22$ notes), automatically sheds incidental background percussion and high-density subdivisions while strictly protecting melody, vocal, bass, and kick/snare parts.
- **Buffer Overrun Protection**: Filters unhandled Control Changes (keeping CC 1, 7, and 91) and strips Aftertouch to protect the synthesizer's internal microcomputer buffer (`bu full`).
- **Individual Note-Off Panic**: Sends explicit Note-Off commands across all active notes upon stop or seek, eliminating hanging notes.
- **Recursive Batch Converter**: 1-click batch converter for folders and nested subdirectories of MIDI files with relaxed vintage MIDI chunk parsing and real-time progress tracking.

### 🎙️ Tab 2: MIDI Recording Studio
- **Direct PSS-790 Recording**: Captures live performances, vector synthesis sweeps, and built-in auto-accompaniment styles directly from the keyboard's MIDI OUT.
- **Anti-Feedback Loop Guard**: Software MIDI Thru is disabled by default with an explicit opt-in toggle to safeguard against destructive feedback loops caused by the PSS-790's internal soft-thru merger.
- **DAW Transport & Timing Assist**:
  - Large digital timecode display (`00:00:00.0`) and musical bar/beat counter (`BAR 01 : 1`).
  - Configurable tempo (40 to 240 BPM) with synchronized slider and number inputs.
  - Visual 1-bar (4 beat) count-in countdown banner before capture begins.
  - Built-in Web Audio metronome click track (accented high click on beat 1).
- **16-Channel Inbound Signal Monitor**: Live DAW-style channel cards across all 16 MIDI channels illuminating glowing rose LEDs and displaying note names/velocities on incoming `NoteOnEvent`s.
- **3 Post-Recording Take Actions**:
  - **`⚡ Save Native (PSS-790 Ready)`**: Direct 1:1 capture without remapping. Embeds the `PSS790_REMAPPED:v1` marker and saves to `Music\Midi\PSS-790 Midi\`.
  - **`🎵 Export to General MIDI (DAW/PC)`**: Reverse conversion translating Channel 16 &rarr; Channel 10 drums, Chart 1 notes &rarr; GM drum notes, and Chart 2 voices &rarr; GM instruments for use in Ableton, FL Studio, Logic, Reaper, or Windows media players.
  - **`🎧 Send to Player (Audition)`**: Transfers the native take directly into Tab 1 in Passthrough Mode to play back on the keyboard immediately.

### 🎮 Asus ROG Ally & Handheld Touch Optimization
- **Fixed-Viewport Workstation Layout**: Full viewport locking (`h-screen overflow-hidden`) eliminates awkward page scrolling, matching professional DAWs like Ableton Live and FL Studio.
- **Independent Two-Pane Scrolling**:
  - **Left Sidebar**: 100% height pane where only the file tree scrolls internally. The search bar, selection buttons, docked batch action bar (`📦 Move` & `🗑️ Delete`), and refresh footer remain **100% visible on screen at all times**.
  - **Right Workspace**: Independent scrollable pane for the transport deck, 16-channel mixer, and inbound monitor. Scrolling the mixer never shifts or hides the library sidebar.
- **Touch-Friendly Hit Targets**:
  - Enlarged tap targets ($\ge 36\text{px}$–$44\text{px}$) across buttons and controls.
  - Checkboxes automatically visible on touchscreens with generous hit zones.
  - Enlarged $22\text{px}$ slider thumbs for effortless finger volume and tempo tweaking.
  - 120Hz fluid kinetic touch scrolling via `touch-action: pan-y`.
- **DPI Scaling & Handheld Dimensions**: Tailored for the Asus ROG Ally Z1 Extreme ($1280 \times 720$ effective resolution at 150% Windows scaling) and compact laptop displays with auto-scaling modals and compact single-line navigation.

### 📁 Standardized User Library (`C:\Users\%USERNAME%\Music\Midi\`)
- **`GM Midi\` Subfolder**: Drop zone for raw General MIDI files (automatically remapped during playback and conversion).
- **`PSS-790 Midi\` Subfolder**: Output destination for converted files and native PSS-790 takes (played in direct Passthrough Mode).
- **1-Click "Open Folder"**: Top navigation button to instantly reveal the library directory in Windows Explorer.
- **Visual Format Badging**:
  - `🎵 GM`: General MIDI file requiring auto-remapping.
  - `⚡ PSS-790`: Native PSS-790 file with direct passthrough engaged.

---

## Installation & Download

### Running Pre-Built Binaries
Download the latest standalone executable from the **[Releases](https://github.com/Gouldin84/Yamaha-PSS-790-Midi-Player-Converter/releases)** page:
1. Download `PSS790Player.exe` (or `PSS790Player-win-x64.zip`).
2. Run `PSS790Player.exe` directly (no installation or .NET runtime installation required).

### Running from Project Clone
If you have cloned the repository, a pre-compiled standalone executable is ready in the root `dist` folder:
```powershell
.\dist\PSS790Player.exe
```

---

## Hardware Setup Guide

### 1. Cabling
- **For Playback**: Connect your PC's USB-MIDI Interface `OUT` &rarr; Yamaha PSS-790 `MIDI IN`.
- **For Recording**: Connect Yamaha PSS-790 `MIDI OUT` &rarr; PC's USB-MIDI Interface `IN`.
- *Caution*: Keep the application's **MIDI Thru Forward** switch disabled unless intentionally routing to a separate sound module; the PSS-790 automatically merges MIDI IN data into its MIDI OUT jack, which can create a loopback feedback storm if echoed by the PC.

### 2. Configure PSS-790 Multi-Timbre Mode
1. Press the `RECEIVE CH/CLOCK` button on the PSS-790.
2. Verify that channels 1 through 16 are enabled (`<E 1>` through `<E 16>`).
3. Set MIDI Clock to disabled (`<d c>`) for playback, or enabled (`<E c>`) if synchronizing keyboard rhythm styles with external software.

### 3. Launch & Connect
1. Select your USB-MIDI interface in the application's **MIDI Port** (Output) and **MIDI In** (Input) dropdowns.
2. Drag and drop `.mid` files into `C:\Users\%USERNAME%\Music\Midi\GM Midi\` or click **Load File**.
3. Customize voice assignments if desired, and click **Play**.

---

## Technical Documentation

Detailed MIDI implementation tables, non-linear voice matrices, percussion mappings, and hardware quirks are documented in:
- [docs/PSS-790_MIDI_Reference.md](docs/PSS-790_MIDI_Reference.md)

---

## Building from Source

Requirements:
- Windows 10/11 x64
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# Clone the repository
git clone https://github.com/Gouldin84/Yamaha-PSS-790-Midi-Player-Converter.git
cd Yamaha-PSS-790-Midi-Player-Converter

# Build solution
dotnet build

# Run automated unit & integration tests
dotnet test

# Publish standalone single-file executable
dotnet publish src/PSS790Player/PSS790Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin/Publish
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
