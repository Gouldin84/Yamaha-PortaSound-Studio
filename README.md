# Yamaha PSS-790 MIDI Player & Remapper

A high-performance standalone MIDI player, voice remapper, and converter designed specifically for the **Yamaha PortaSound PSS-790** keyboard synthesizer.

[![Release](https://img.shields.io/github/v/release/Gouldin84/Yamaha-PSS-790-Midi-Player-Converter)](https://github.com/Gouldin84/Yamaha-PSS-790-Midi-Player-Converter/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-green.svg)]()
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)]()

---

## Overview

The Yamaha PSS-790 (released in 1990) features a 28-voice polyphonic AWM tone generator with 100 preset voices, 50 rhythm styles, and 16-channel multi-timbral reception. However, because it predates the General MIDI (GM) standard, standard MIDI files sound unaligned or distorted without translation.

This application provides real-time translation, hardware playback, and offline file conversion to output cleanly onto the PSS-790.

### Key Capabilities

- **Automatic Channel Routing**: Shifts GM percussion from Channel 10 to Channel 16 (PSS-790's fixed rhythm channel) and safely moves any melodic data on Channel 16 to free melodic channels.
- **Percussion Note Translation**: Converts GM drum notes (35–81) to PSS-790 percussion notes (36–85, Chart 1) so kick, snare, toms, hi-hats, and cymbals trigger the correct instruments.
- **Non-Linear Voice Translation**: Converts GM Program Change events to the specific non-linear MIDI program numbers required by the PSS-790 (Chart 2) to trigger appropriate voices (00–99).
- **16-Channel Voice Selector**: Real-time combobox selector on every channel to manually override voice assignments on the fly during playback or before saving.
- **Pitch Bend Scaling**: Scales GM pitch bend values by 1/6 (~0.1667) to match the PSS-790's fixed +/- 1 octave (12 semitones) hardware pitch wheel range.
- **Smart Polyphony Optimizer**:
  - Clamps lingering percussion note durations to free up AWM sound generator elements.
  - Implements hi-hat choke grouping (closed hi-hat truncates open hi-hat).
  - During peak polyphony congestion (>= 22 notes), automatically sheds incidental background percussion and high-density subdivisions while strictly protecting melody, vocal, bass, and kick/snare parts.
- **Buffer Overrun Protection**: Filters unhandled Control Changes (keeping CC 1, 7, and 91) and strips Aftertouch to protect the synthesizer's internal microcomputer buffer (`bu full`).
- **Individual Note-Off Panic**: Sends explicit Note-Off commands across all active notes upon stop or seek, eliminating hanging notes.
- **Standalone `.exe`**: Self-contained Windows x64 binary with zero external runtime dependencies.
- **Modern DAW Interface**: Sleek dark aesthetic styled with Tailwind CSS, glowing real-time note meters, and custom volume faders.
- **File Export & Batch Conversion**: Saves corrected `.mid` files with voice overrides preserved, plus a 1-click batch converter for directories of MIDI files.

---

## Installation & Download

Download the latest standalone executable from the **[Releases](https://github.com/Gouldin84/Yamaha-PSS-790-Midi-Player-Converter/releases)** page:
1. Download `PSS790Player.exe` (or `PSS790Player-win-x64.zip`).
2. Run `PSS790Player.exe` (no installation or setup required).

---

## Hardware Setup Guide

1. **Connect MIDI**:
   - Connect your PC's USB-MIDI Interface `OUT` to the Yamaha PSS-790 `MIDI IN`.
   - *Note*: Leave the PSS-790 `MIDI OUT` disconnected during computer playback to avoid feedback loops caused by the keyboard's internal soft-thru merger.
2. **Configure PSS-790 Multi-Timbre Mode**:
   - Press the `RECEIVE CH/CLOCK` button on the PSS-790.
   - Verify that channels 1 through 16 are enabled (`<E 1>` through `<E 16>`).
   - Set MIDI Clock to disabled (`<d c>`).
3. **Launch Player**:
   - Select your USB-MIDI interface in the application's MIDI Port dropdown.
   - Load any standard MIDI file, customize voice assignments if desired, and click Play.

---

## Technical Documentation

Detailed MIDI specification, voice matrices, and percussion tables are documented in:
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

# Run unit tests
dotnet test

# Publish standalone single-file executable
dotnet publish src/PSS790Player/PSS790Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin/Publish
```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
