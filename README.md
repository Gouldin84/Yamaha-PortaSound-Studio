# Yamaha PortaSound Studio

A standalone MIDI workstation, real-time voice remapper, and multi-tab sound design studio for classic **Yamaha PortaSound** keyboard synthesizers.

[![Release](https://img.shields.io/badge/release-v2.6.8-blue.svg)](https://github.com/Gouldin84/Yamaha-PortaSound-Studio/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-green.svg)]()
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)]()
[![Tests](https://img.shields.io/badge/Tests-200%20Passing-brightgreen.svg)]()

---

## Supported Synthesizers

| Model Era | Tone Generator | Voices & Polyphony | Key Hardware Capabilities |
| :--- | :--- | :--- | :--- |
| **Yamaha PSS-790** (1990) | Advanced Wave Memory (AWM) | 100 Voices • 28 Polyphony | Vector synth joystick, 50 styles, 16-channel multi-timbral MIDI |
| **Yamaha PSS-680 / 480 / 780** (1988–89) | 2-Operator FM (YM3420) | 100 Voices + 5 User Banks • 12 Polyphony | 32 Hand Percussion sounds, 5 FM user banks, Sound Source Mode 99 |

Because vintage Yamaha synthesizers predate General MIDI (GM), standard MIDI files sound distorted or unaligned on original hardware. **Yamaha PortaSound Studio** provides real-time translation, hardware playback, direct recording, deep sound design, and bidirectional SysEx preset management.

---

## Studio Workstations (3 Dedicated Tabs)

### 📻 Tab 1: Player & Voice Remapper
- **Real-Time Translation**: Translates General MIDI files on the fly into native PSS-790 (Chart 2 AWM) or PSS-680 (2-Op FM) instrument tables and percussion notes.
- **Smart Polyphony Guard**: Prevents hardware voice-stealing bugs by dynamically respecting model limits (28 voices on PSS-790, 12 voices on PSS-680 Mode 99), protecting lead melodies and kick/snare while culling background clutter.
- **FM User Banks & Library Presets (PSS-680)**: Program Changes 100–107 select the 5 Digital Synthesizer User Banks. Select any of the 23 curated FM presets in the mixer for instant 1-click SysEx upload and playback.
- **Vector Quad-Link Mode (PSS-790)**: Clones Channel 1 lead melodies across Channels 1–4 with dynamic CC 7 volume balancing and synchronized LED activity for true 4-voice vector playback during songs.
- **16-Channel Mixer**: Responsive activity LEDs, volume faders, mute/solo buttons, and per-song voice memory (`overrides.json`).
- **3-Way Mirrored Library & Sync**: Jukebox tree browser with drag-and-drop reorganization and 1-click symmetrical sync across `GM Midi`, `PSS-790 Midi`, and `PSS-680 Midi`.
- **Side-by-Side Batch Exporter**: Convert entire song folders for either keyboard individually or both simultaneously.

### 🎙️ Tab 2: Recording Studio & Take Vault
- **Direct Hardware Recording**: Capture live keyboard playing, auto-accompaniment styles, Custom Drummer beats, and vector joystick movements directly from MIDI IN.
- **Standalone Take Vault**: Work-in-progress takes are stored cleanly in `Music/Midi/Recordings/` with instant auditioning (`▶ Preview`).
- **Live Pitch & Chord Analyzer**: Real-time LCD display identifying notes, intervals, and chords (Major, Minor, 7ths, Dim, Aug, 9ths, and slash chords like `C Maj / E`).
- **Stereo Master VU Meters**: Dual 12-segment ballistic peak meters with dB calibration (-24dB to +3dB).
- **Reverse Export**: 1-click conversion of recorded takes back to standard General MIDI, or direct export to native `PSS-790 Midi/` and `PSS-680 Midi/` libraries.
- **DAW Transport Tools**: Configurable BPM (40–240), 1-bar count-in, and Web Audio metronome click track.

### 🎛️ Tab 3: Sound Designer & SysEx Vault
- **PSS-680 FM Sound Designer**:
  - Full 4-strip modular rack layout: Modulator, Carrier, Modulation & LFO, and Bank / Preset Vault.
  - Controls all 38 voice parameters, including **18 hidden synthesis parameters** not accessible from keyboard buttons (fine/coarse detune, decay rates, key scaling, independent carrier waveforms, and AMS tremolo depth).
  - 23 iconic factory presets (*Take On Me Lead, LatelyBass, DX7 Ballad EP, Axel F Brass, Stranger Things Arp Bass, Sonic 2 Chemical Plant*).
  - Musically constrained intelligent randomizer for instant patch creation.
  - Real-time Format 00H SysEx transmission and hardware audition button.
- **PSS-790 Vector Synthesizer**:
  - 2D interactive glass touchpad with circular radial grids and draggable puck $(X, Y) \in [-1.0, 1.0]$.
  - 4 cardinal voice poles (Voice A North • Voice B East • Voice C South • Voice D West) mapped across Channels 1–4.
  - Continuous radial bilinear CC 7 volume balancing ($W_A + W_B + W_C + W_D = 100\%$).
  - Automated LFO Motion Engine with 5 trajectories: Circular Orbit, Lissajous (Figure-8), Expanding Spiral, Diagonal Ping-Pong, and Random Drift.
  - 30 pre-seeded vector presets (*Blade Runner CS-80, Axel F Marimba-Brass, Thunder Force IV Shred, Castlevania Harpsichord*).
- **Bulk Memory Dump Explorer**:
  - Send and receive bulk RAM memory dumps, melody sequences, chord memories, and rhythm patterns.
  - Built-in hex viewer with byte offsets, ASCII view, and verified 7-bit two's complement checksum validation.
  - 1-click utility commands: GM On, Yamaha XG On, Mode 99 Init, Master Volume 100%, and Panic Reset.

---

## User Library Directory Structure

All files reside under your standard user directory (`C:\Users\%USERNAME%\Music\Midi\`):

```
Music\Midi\
├── GM Midi\              # Raw General MIDI files (auto-remapped on the fly)
├── PSS-790 Midi\         # Converted & native files for Yamaha PSS-790 (AWM)
├── PSS-680 Midi\         # Converted & native files for Yamaha PSS-680 (FM)
├── Recordings\           # Captured takes from hardware MIDI IN
└── SysEx Library\
    ├── PSS-680 Voices\   # 23 Factory & custom .syx FM voices (Format 00H)
    ├── PSS-790 Vectors\  # 30 Factory & custom .json vector synthesis presets
    └── Backups\          # Bulk RAM memory and sequencer dumps
```

---

## Hardware Setup Guide

### 1. Cabling
- **Playback**: PC USB-MIDI Interface `OUT` &rarr; Yamaha PSS `MIDI IN`.
- **Recording & SysEx**: Yamaha PSS `MIDI OUT` &rarr; PC USB-MIDI Interface `IN`.
> [!TIP]
> Keep the application's **MIDI Thru Forward** switch turned off unless using an external sound module; vintage Yamaha keyboards echo incoming MIDI data to their output jack, which can create a feedback loop if echoed back by the PC.

### 2. Configure Multi-Timbral Mode
- **PSS-790**: Press `RECEIVE CH/CLOCK` on the keyboard and verify channels 1–16 are enabled (`<E 1>` to `<E 16>`).
- **PSS-680 / 480 / 780**: Press `MIDI MODE [c]` and select `c.99` (Sound Source Mode 99).

### 3. Launch & Connect
1. Select your USB-MIDI interface in the **MIDI Port** (Output) and **MIDI In** (Input) dropdowns.
2. Choose your active synthesizer model (**PSS-790** or **PSS-680**) from the top header switcher.
3. Drop `.mid` files into the library tree or click **Load File** to start playing.

---

## Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| `Space` | Play / Pause toggle |
| `Esc` | Stop & Panic Reset (All-Notes-Off) |
| `Enter` | Play selected song immediately |
| `Arrow Up` / `Down` | Browse songs in library tree |
| `Arrow Left` / `Right` | Seek backward / forward 5 seconds |
| `Ctrl + B` | Toggle library sidebar visibility |

---

## Handheld & Touchscreen Ready

Optimized for the **Asus ROG Ally Z1 Extreme** ($1280 \times 720$ at 150% scaling) and Windows touch devices:
- Fixed-viewport DAW layout (`h-screen overflow-hidden`) with zero unwanted page bouncing.
- Independent two-pane kinetic touch scrolling with 120Hz smooth scrolling.
- Generous touch targets ($\ge 36\text{px}$–$44\text{px}$) and enlarged $22\text{px}$ slider thumbs.

---

## Installation & Download

Pre-built standalone single-file executables are available on the **[GitHub Releases Page](https://github.com/Gouldin84/Yamaha-PortaSound-Studio/releases)**:

| Asset | Type | Size | Description |
| :--- | :--- | :--- | :--- |
| **`PSS-Studio.exe`** | Windows Executable | ~73 MB | **Recommended**: Standalone executable with zero external runtime dependencies. |
| **`PSS-Studio-win-x64.zip`** | ZIP Archive | ~68 MB | Complete distribution package with `PSS-Studio.exe` and `Launch-Studio.bat`. |

---

## Building from Source

**Prerequisites**: Windows 10/11 x64 and the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Clone the repository
git clone https://github.com/Gouldin84/Yamaha-PortaSound-Studio.git
cd Yamaha-PortaSound-Studio

# Run automated test suite (196 tests)
dotnet test

# Build single-file release executable
dotnet publish src/PSS790Player/PSS790Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin/Publish/
```

---

## Acknowledgements & Trademarks

- **Melanchall.DryWetMidi**: High-performance .NET MIDI playback, parsing, and device I/O.
- **Microsoft.Web.WebView2**: Embedded Chromium desktop UI runtime.
- **Tailwind CSS**: Modern utility-first styling.

*Yamaha, PortaSound, PSS-790, PSS-680, and AWM are registered trademarks of Yamaha Corporation. This software is an independent open-source preservation project and is not affiliated with or endorsed by Yamaha Corporation.*

---

## License

This project is licensed under the [MIT License](LICENSE).
