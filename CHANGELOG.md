# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.1] - 2026-11-16

### Changed
- Streamlined GitHub release notes generation: Releases now display an isolated, focused summary of app changes for that specific version with zero links to the main changelog file or commit diffs.

## [1.3.0] - 2026-11-16

### Added
- **Native 1:1 PSS-790 Capture (`SaveNativeTake`)**:
  - Direct 1:1 preservation of takes recorded from the Yamaha PSS-790.
  - Automatically injects the non-destructive `PSS790_REMAPPED:v1` marker into Track 0.
  - Saves directly into `Music\Midi\PSS-790 Midi\`.
  - Bypasses destructive GM remapping (preserves native Channel 16 drums, Chart 1 percussion notes, Chart 2 programs, and native $\pm 1$ octave pitch bends).
- **Reverse Remapper (`ReverseRemapper.cs`)**:
  - Translates PSS-790 native takes to standard General MIDI for export to modern DAWs (Ableton, FL Studio, Logic, Reaper) and PC media players.
  - Relocates Channel 16 drums &rarr; Channel 10.
  - Translates Chart 1 percussion note numbers (36–85) &rarr; Standard GM drum notes (35–81) via `Pss790ToGmDrumMap`.
  - Translates Chart 2 Program Change bytes &rarr; Standard GM Instrument Numbers (0–127) via `Pss790MidiProgramToGmMap`.
  - Strips PSS-790 metadata markers and exports directly to `Music\Midi\GM Midi\`.
- **Refined Studio Take Actions UI**:
  - `⚡ Save Native (PSS-790 Ready)`: Direct 1:1 save without remapping.
  - `🎵 Export to General MIDI (DAW/PC)`: Reverse conversion for PC/DAW compatibility.
  - `🎧 Send to Player (Audition)`: Immediate handoff to Tab 1 in Passthrough Mode.

## [1.2.0] - 2026-11-16

### Added
- **MIDI Recording Studio (2nd Tab)**: Dedicated recording workstation transforming the player into a bidirectional DAW for the Yamaha PSS-790.
- **Top-Level Tab Switcher**: Seamless switching between `Player & Remapper` and `MIDI Studio` with live recording status badges.
- **Live MIDI Input Discovery**: Real-time port discovery and selection for external USB-MIDI keyboards, controllers, or the Yamaha PSS-790's MIDI OUT.
- **Recording Transport**:
  - Sample-accurate recording engine powered by `Melanchall.DryWetMidi.Multimedia.Recording`.
  - Prominent glowing record button with pulsing animation during live capture.
  - Digital timer displaying elapsed time (`00:00:00.0`) and musical bar/beat counters (`BAR 01 : 1`).
  - Configurable tempo (40 to 240 BPM) with synchronized slider and number inputs.
  - 1-Bar (4 beat) visual count-in countdown banner before recording starts.
  - Integrated Web Audio metronome click track (accented high click on beat 1).
- **Anti-Feedback Loop Guard**: Software MIDI Thru is disabled by default with an explicit opt-in toggle to safeguard against destructive feedback loops when connected to the PSS-790's soft-thru merger.
- **16-Channel Inbound Signal Monitor**:
  - Live DAW-style channel meter cards across all 16 MIDI channels.
  - Real-time LED activity meters illuminating on inbound `NoteOnEvent` with velocity scaling.
  - Inbound live ticker showing active channel and note data.
- **Take Management & 1-Click Post-Recording Actions**:
  - Quick audition preview player to listen to the captured take before saving.
  - **Save to PSS-790 Midi**: Automatically remaps drum notes (Chart 1), translates voices (Chart 2), scales pitch bends (1/6), clamps percussion decays, embeds `PSS790_REMAPPED:v1`, and saves to `Music\Midi\PSS-790 Midi\`.
  - **Save to GM Midi**: Saves raw uncompressed take directly into `Music\Midi\GM Midi\`.
  - **Send to Player**: 1-click handoff transferring the recorded take into Tab 1 for immediate playback on the PSS-790 and live 16-channel voice overriding.

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
