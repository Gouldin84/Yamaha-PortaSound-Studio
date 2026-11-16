# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-11-16

### Added
- Dedicated User MIDI Library: Automatically creates and monitors `C:\Users\%USERNAME%\Music\Midi\` with subfolders:
  - `GM Midi`: Input folder for raw General MIDI files.
  - `PSS-790 Midi`: Output and storage folder for converted, PSS-790-ready files.
- One-Click "Open Library" button in the application header to instantly launch Windows Explorer to the library folder.
- Non-destructive MIDI marker (`PSS790_REMAPPED:v1`) embedded into converted files.
- Smart Format Detection and Idempotent Passthrough: Automatically detects if a file is already formatted for the PSS-790, bypassing remapping to prevent destructive double pitch-bend scaling and voice translation.
- UI Format Indicators & Badging:
  - Song header displays format badges: `🎵 GM (Auto-Remapping Active)` or `⚡ PSS-790 Native (Direct Passthrough)`.
  - Library dropdown categorizes songs into distinct optgroups with format icons.
- Automated library rescan on launch and immediately following file conversions.

## [1.0.0] - 2026-11-16

### Added
- Standalone Windows desktop player and remapper for the PSS-790 keyboard synthesizer.
- High-performance .NET 8 multimedia MIDI playback engine utilizing low-latency hardware USB-MIDI scheduling.
- Automatic channel routing: remaps General MIDI percussion from Channel 10 to Channel 16.
- Percussion note conversion from General MIDI drum map to PSS-790 percussion map (Chart 1).
- Melodic voice translation: maps General MIDI instruments (0-127) to PSS-790 preset voices (00-99) via non-linear Program Change mapping (Chart 2).
- Real-time 16-channel voice override selector to customize instrument assignments on the fly.
- Pitch bend 1/6 scaling to match the fixed +/- 1 octave pitch bend range of the PSS-790.
- Smart Polyphony Optimizer: protects melody and harmonic parts by clamping percussion decay and culling incidental percussion during dense passages.
- Unsupported controller filtering (CC whitelist: 1, 7, 91) and aftertouch stripping to prevent buffer overruns (`bu full`).
- Active note tracking and explicit individual Note-Off panic execution on stop/pause to prevent hanging notes.
- Export functionality to save remapped MIDI files for standalone storage or playback.
- Batch remapper utility for processing folders of MIDI files.
- Modern dark-mode DAW-inspired user interface styled with Tailwind CSS, glowing real-time note meters, and custom volume faders.
