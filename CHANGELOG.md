# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.11.0] - 2026-11-17

### Added
- **Audio Fidelity & Playback Robustness Engine**:
  - Comprehensive hardware-tailored playback and conversion engine update maximizing sound fidelity and timing precision on the Yamaha PSS-790.
- **Dynamic Expression Swell Folding (CC 11 &rarr; CC 7)**:
  - Compensates for the PSS-790's lack of CC 11 hardware circuitry by mathematically folding CC 11 into CC 7: $V_{\text{eff}} = \text{round}\left(\frac{\text{CC } 7 \times \text{CC } 11}{127}\right)$.
  - Restores dynamic swells, crescendos, decrescendos, and track balance that were previously ignored by the hardware.
- **Proportional Live Mixer Volume Scaling (`MidiPlayer.cs`)**:
  - Embedded CC 7 volume events in MIDI files are now proportionally scaled against user mixer channel volume sliders: $\text{Sent CC } 7 = \text{round}\left(\frac{\text{File CC } 7 \times \text{Mixer Level}}{100}\right)$.
  - Custom channel volume balances stay locked in and are no longer overwritten mid-song by MIDI files.
- **Sub-Millisecond Windows Multimedia Timer Precision**:
  - Configured DryWetMIDI `PlaybackSettings` with `HighPrecisionTickGenerator` (`timeSetEvent` / 1ms tick resolution), eliminating micro-jitter and timing hesitation on busy 16-channel arrangements.
- **Opening Note Setup Pre-Roll Guard**:
  - Enforces a clean 48-tick ($\approx 25\text{--}50\text{ ms}$) setup pre-roll window if Note-On events occur at Tick 0 concurrently with Program Changes and controllers.
  - Gives the 8-bit Yamaha microcomputer time to configure tone generators before Note 1 strikes, permanently eliminating dropped opening notes and initial voice glitches.
- **AWM Dynamic Velocity Sweet-Spot Normalizer**:
  - Maps velocity extremes $[1, 127]$ into the PSS-790 AWM DAC sweet spot curve $[36, 118]$: $V_{\text{out}} = 36 + \text{round}\left((V_{\text{in}} - 1) \times \frac{82}{126}\right)$.
  - Lifts whispering inaudible notes into clear audibility while preventing harsh sample-split transients and polyphony competition.
- **Seamless Loop Wrap-Around Controller Re-Priming**:
  - Leverages DryWetMIDI `Playback.RepeatStarted` to perform note panic and re-transmit initial channel program changes, scaled volumes, and pitch center (8192) on loop wrap-around, ensuring clean, continuous playback.
- **Fidelity Test Suite (`AudioFidelityAndPlaybackTests.cs`)**:
  - Added 4 unit tests covering CC 11 expression folding, AWM velocity scaling, pre-roll separation, and high-precision tick generator creation (96/96 tests passing).

## [1.10.0] - 2026-11-17

### Added
- **SysEx Vault & Memory Bulk Dump Librarian (Tab 3)**:
  - Added a dedicated 3rd tab to the application: **`SysEx Vault`** for full bidirectional Yamaha PSS-790 memory bulk dumps, backup archiving, hardware restoration, and low-level system messaging.
- **Hardware Bulk Dump Capture (`SysExManager.cs`)**:
  - Live capture deck listens for PSS-790 `MEMORY BULK DUMP` panel transfers (triggered by pressing the button twice with countdown display `b-8`).
  - Seamless stream completion detection via 1.5s idle timer automatically writes binary captures directly to disk as `PSS790_Backup_YYYYMMDD_HHMMSS.syx`.
- **Dedicated SysEx Library Isolation**:
  - All `.syx` archives are isolated in `Music\Midi\SysEx Library\`, completely separate from song playlists and the Player/Studio tree.
  - SysEx Explorer provides search/filtering, card metadata (packet count, byte size, timestamp, manufacturer detection), renaming, and deletion.
- **Paced SysEx Transmission with Buffer Overflow Guard (`<bu full>`)**:
  - Safely restores bulk memory dumps back to the Yamaha PSS-790 with configurable inter-packet delay (15ms, 25ms, default 30ms, 50ms, 100ms) and live byte/packet countdown progress bar.
  - Protects the 8-bit Yamaha microcomputer from hardware buffer overflow error `<bu full>`.
- **Integrated Binary Hex Inspector**:
  - Visual modal inspects any `.syx` file with monospace address offsets, hex byte formatting, and ASCII character translation. Includes one-click copy to clipboard.
- **Quick SysEx Hardware Control Deck**:
  - One-click transmission of hardware commands: GM System On (`F0 7E 7F 09 01 F7`), Yamaha XG System On (`F0 43 10 4C 00 00 7E 00 F7`), Master Volume Full (`F0 7F 7F 04 01 7F 7F F7`), and All Reset.
- **SysEx Test Suite (`SysExManagerTests.cs`)**:
  - Added 7 unit tests covering packet boundary extraction, framing fallbacks, Yamaha manufacturer ID detection, hex preview column generation, file renaming, and deletion (92/92 tests passing).

## [1.9.0] - 2026-11-17

### Added
- **Autonomous MIDI Health Inspector (`MidiHealthInspector.cs`)**:
  - Pre-flight diagnostic analyzer that inspects any loaded or imported MIDI file before playback or remapping.
  - Automatically calculates exact peak concurrent polyphony via $O(N \log N)$ sweep-line note tracking, voice-stress ratings, active channel counts, explicit vs inferred instrument channels, runaway sustain pedals, dangling pitch bends, acoustic micro-bends, and note range bounds.
- **Always-Visible UI Health Badge & Interactive Diagnostics Popover**:
  - Added an always-visible status badge next to `#currentSongTitle` in the player panel displaying real-time health (`Optimal (16 Voices)`, `3 Auto-Fixes Applied`, `Polyphony Alert`).
  - Interactive Diagnostics Modal shows a visual Yamaha PSS-790 polyphony load gauge (0 to 28 voices), breakdown of applied auto-protections, and detailed song metrics with one-click inspection.
- **One-Click Library Health Audit Tool**:
  - Added a **"Run Health Audit"** button in the Batch Remapper modal.
  - Scans entire folders of General MIDI files in seconds and displays a comprehensive health summary: total files, files with auto-fixes, inferred/XG instruments, stuck pedals, and voice loads $> 28$.
- **Hardware Panic & Clean Song Transitions (`MidiPlayer.cs`)**:
  - Enhanced `SendPanicNoteOff()` on stop, pause, seek, and track-change to transmit `CC 123` (All Notes Off), `CC 64 = 0` (Damper/Sustain release), `CC 121 = 0` (Reset All Controllers), and `PitchBend = 8192` (Center Pitch) across all 16 channels, permanently preventing residual resonance, hung notes, or detuned oscillators between songs on physical Yamaha PSS-790 hardware.
- **Running-Status Zero-Velocity Note-On Normalization**:
  - Automatically normalizes `NoteOnEvent` with `Velocity == 0` into standard `NoteOffEvent` in `Remapper.cs`, ensuring 100% note pairing across all MIDI formats and fringe DOS/legacy game files.
- **Enhanced Instrument Inference Keywords & GS/XG Drum Support**:
  - Expanded track name heuristics to eliminate false substring matches (e.g., word-bounded EP/Rhodes vs "sweep") and prioritized solo instruments (pizzicato vs orchestral strings). Added native detection for Yamaha XG Part Mode SysEx drum routing.
- **Synthetic Edge-Case Stress Test Suite (`ProblematicMidiEdgeCaseTests.cs`)**:
  - Added 8 automated stress tests covering Black MIDI (128 simultaneous notes), Format 0 multi-channel, endless damper pedals (5,000 bars), dangling pitch bends, cryptic track names, zero-velocity notes, and non-standard GS/XG drum banks (84/84 tests passing).

## [1.8.9] - 2026-11-17

### Added
- **Multi-Agent Swarm Quality Audit & Engine Hardening**:
  - Executed a comprehensive parallel swarm audit across all 113 General MIDI files, establishing the [Master Anomaly Matrix](docs/MASTER_ANOMALY_MATRIX.md).
- **Intelligent Track-Name Instrument Inference**:
  - Implemented `InferGmProgramFromText` in `Remapper.cs`. When active tracks contain note data without standard `ProgramChangeEvent`s (e.g. *Brothers In Arms 3*), the engine scans track metadata and text events for instrument keywords (`bass`, `guitar`, `strings`, `organ`, `vocal`, `choir`, `brass`, `flute`, `pad`) to automatically assign true GM instruments instead of falling back to Acoustic Piano.
- **Yamaha XG Multi-Part SysEx Voicing Extractor**:
  - Added native Yamaha XG bulk dump (`08 [part] 00`) and parameter change (`08 [part] 03`) parsing in `Remapper.cs`. Automatically extracts intended GM voices from files voiced purely through XG SysEx dumps (e.g. *The Show Must Go On - Celestial Trance Version* with 205 SysEx messages) before SysEx is stripped.
- **Stuck Sustain Pedal (CC 64) Protection**:
  - Added unreleased damper pedal windowing in `ApplySustainPedalEmulation`. Binds dangling sustain pedal down intervals (where `CC 64 >= 64` was set once and never released, e.g. *At Zanarkand*) and intervals $> 4$ measures to a maximum duration of 2 measures (`tpqn * 8`), permanently eliminating 929-voice polyphony explosions.
- **Pre-Scan Active Channel Default Initialization**:
  - Pre-populates unassigned active melodic note channels with GM 0 (Acoustic Grand Piano) in `channelGmPrograms` prior to event processing. Ensures `NeutralizeAcousticPitchBends` neutralizes micro-bends (neutralizing 6,290 detuning pitch bends in *aqua-remix*) and injects explicit PSS-790 Program Changes at Tick 0.
- **Valid End-of-Track Pitch Bend Zeroing**:
  - Pitch bend auto-centering now inserts before `EndOfTrackEvent`, ensuring all hardware and MIDI parsers recognize the oscillator reset to 8192 at track end.

## [1.8.8] - 2026-11-17

### Fixed
- **Zero-Loss Timed Event Filtering (Fixed Track Desynchronization)**:
  - Replaced raw `trackChunk.Events.RemoveAll()` with DryWetMIDI's `using (var mgr = trackChunk.ManageTimedEvents()) { mgr.Objects.RemoveAll(...); }` across the core conversion engine (`Remapper.cs`).
  - Standard MIDI files represent event timing as relative delta-times. Removing unsupported CC events (CC 0, 32, 11, 100, 101) with raw list deletion previously dropped their delta-times, causing massive cumulative timing shifts (e.g. up to 32,626 ticks or 17 measures in *The Greatest Showman - From Now On*).
  - With timed event management, absolute timestamps (`.Time`) are strictly preserved, recalculating exact delta-times automatically. Timing drift across all tracks is now guaranteed at **0 ticks**.
- **Melodic, Vocal & Harmony Note Protection**:
  - Completely removed aggressive non-highest-pitch melodic note truncation from `PolyphonyOptimizer.cs`.
  - Vocal melodies (including tenor and baritone lines below high violins/synths), vocal harmonies, and lead motifs are 100% immune from premature voice culling or shortening.
  - Raised `PolyphonyGuardThreshold` in `Constants.cs` from 22 to **26** to take full advantage of the Yamaha PSS-790's true 28-voice hardware limit. Voice culling is strictly confined to low-priority incidental percussion on Channel 16 (cowbells, triangles, maracas, woodblocks).
- **Domain-Specific Sustain Pedal Emulation (GM 0–23 Only)**:
  - Restricted virtual CC 64 damper pedal duration extension in `Remapper.cs` strictly to acoustic pianos, electric pianos, chromatic percussion, and organs (`IsPianoOrKeyboardInstrument`).
  - Guitars, basses, brass, woodwinds, strings, and vocals no longer receive artificial sustain extensions, preventing polyphony bloat on fast guitar strums and arpeggios.
- **Universal Engine Integration**:
  - All 5 conversion pathways (Batch Remapper, Library Sync, Real-time Jukebox Player, Single File Export, and Take Recorder) inherit these timing and melodic protections automatically through the shared `Remapper.Remap()` pipeline.

## [1.8.7] - 2026-11-16

### Added
- **Automated Converter Intelligence & Multi-File Optimization Engine**:
  - Upgraded the core remapping engine (`Remapper.cs`, `RemapperOptions.cs`, and `PolyphonyOptimizer.cs`) to eliminate common MIDI translation flaws automatically across entire libraries:
    1. **Sustain Pedal Note Emulation (CC 64)**: PSS-790 hardware lacks sustain pedal circuitry, causing piano ballads (e.g. *Children*, *Everytime*) to sound staccato, empty, and clipped. The converter now automatically extends note durations while CC 64 is held, preventing abrupt voice cuts and delivering natural, lush sustain.
    2. **Acoustic Auto-Tune & Detune Protection**: Automatically neutralizes micro-pitch bends and negative pitch drift on acoustic instruments (Pianos, Organs, Strings, Bells, Choirs, Pads) to pure concert pitch (8192), while preserving scaled pitch bends on solo instruments (Guitars, Synth Leads, Bass).
    3. **Auto-Centering Dangling Pitch Bends**: Guarantees that any channel that experienced pitch bends receives an explicit center bend event (`PitchValue = 8192`) at track end, eliminating persistent out-of-tune hardware oscillator drift.
    4. **Dynamic 28-Voice Polyphony Guard with Melodic Preservation**: Enhanced polyphony optimization to track total simultaneous sounding notes. When active voices approach the hardware limit (24+), the engine culls incidental percussion on Ch 16 and gently trims older background accompaniment/pad notes while strictly protecting the lead melody note (highest pitch) and bass root note (lowest pitch).
    5. **Yamaha DSP Reverb Boost (CC 91)**: Injects default CC 91 reverb levels (value 54 for melodic voices, value 32 for drums) on channels lacking reverb events, ensuring tracks never play back bone-dry.
    6. **Acoustic Sweet-Spot Octave Clamping**: Folds harsh outlier notes above note 88 (E6) down by 1 octave for acoustic piano, organ, and string samples, avoiding shrill FM aliasing.
    7. **Sluggish Tempo Normalization**: Configurable `TempoMultiplier` and `AutoNormalizeSluggishTempo` to double half-time or dragging tempos (< 68 BPM) on dense rhythmic sequences.
- **Batch Converter Intelligence Controls**:
  - Added "Automated Remapping Engine" toggles directly in the Batch Remap modal (`index.html` & `app.js`) with all protections enabled by default.

### Fixed
- **ASUS ROG Ally Header Bar Clearance**:
  - Permanently removed the obsolete `Quick:` GM dropdown from the top navigation bar, guaranteeing that all buttons (`Library`, `Load`, `Folder`, `Export`, `Batch`, `About`) fit cleanly without clipping or wrapping on ASUS ROG Ally at 100%, 125%, and 150% scaling.

### Fixed
- **Top Header Bar Fit for ASUS ROG Ally & Handheld Displays**:
  - Restructured top navigation toolbar (`index.html`) to fit 720p / 1080p @ 150% Windows scaling (1280x720 effective resolution) on ASUS ROG Ally and similar handheld devices without clipping buttons or generating horizontal overflow.
  - Restricted the redundant `Quick:` GM dropdown to ultra-wide displays (`2xl:` >= 1536px), instantly reclaiming 180px of horizontal breathing room.
  - Refined button padding, compact icon-first labels with responsive text visibility, and constrained Port select width (`max-w-[110px]` to `[160px]`), ensuring `Library`, `Load`, `Folder`, `Export`, `Batch`, and `About` buttons remain 100% visible, accessible, and aligned.
- **Electric Grand Piano GM Remapping (Voice 00 Grand Piano)**:
  - Fixed GM Program 2 (Electric Grand Piano / Yamaha CP-80) remapping from PSS-790 Voice 01 (FM Electric Piano 1) to Voice 00 (AWM Acoustic Grand Piano).
  - Resolved harsh, metallic, tinny playback in acoustic ballads including *The Greatest Showman - Never Enough*, *Evanescence - My Immortal*, *Alan Walker - Faded*, and *Celine Dion - My Heart Will Go On*.
- **The Greatest Showman "Never Enough" PSS-790 Edition**:
  - Created full orchestral `PSS-790 Ready` multi-timbral arrangements in `PSS-790 Midi\Never Enough.mid` and `PSS-790 Midi\Greatest Showman Theme - Never Enough.mid` featuring warm Grand Piano, Bowed Contrabass, Strings, Flute, French Horn, Timpani, Harp, Tubular Bells, and Yamaha PSS-790 Drums.

## [1.8.5] - 2026-11-16

### Fixed
- **Clean Song Switching & Playback Lifecycle**:
  - Resolved track switching bug where selecting another song during playback would carry over the previous song's playback timestamp, causing the newly loaded song to play from the middle rather than starting at `00:00`.
  - Updated `UiBridge.cs` `ReapplyRemapping(bool preservePosition = false)` to restrict position preservation strictly to real-time mixer adjustments (voice overrides, polyphony guard, and voice resets). Loading a new file or take now always stops the previous song cleanly, issues hardware panic note-offs, and resets the playback head to `00:00`.
  - When a song is playing, clicking another song in the library or selecting one from the quick dropdown immediately silences the old song and starts playing the new song from the beginning (`00:00`).
  - Clicking the currently playing song in the library does not interrupt active audio, while double-clicking or pressing `Enter` restarts it from `00:00`.
  - Single-clicking songs while stopped or idle loads them in stopped mode at `00:00` with full metadata and channel strips ready.
  - Arrow Up/Down keyboard navigation in the library while playing scrolls the list and moves the selection highlight without disrupting active audio until `Enter` is pressed.

## [1.8.4] - 2026-11-16

### Fixed
- **16-Channel Real-Time Activity LEDs**:
  - Replaced uncalled DryWetMidi `Playback.EventCallback` with real-time `Playback.EventPlayed` event subscription in `MidiPlayer.cs`.
  - Mixer channel LEDs (CH 1–CH 16) now illuminate in real-time with vibrant green (melodic) and amber (drums) lights as notes are dispatched to MIDI hardware.
  - Implemented multi-note polyphonic tracking so chords keep channel LEDs illuminated until the final note in the chord is released.
  - Added 120ms visual peak-hold decay in `app.js` (`updateChannelLed`), guaranteeing that rapid staccato hits, fast hi-hat patterns, and 32nd-note rolls produce a crisp, punchy, clearly visible flash on high-refresh and 60Hz handheld screens.
  - Added `clearAllChannelLeds` on stop, pause, and library reload to immediately clear any active channel indicators.
- **Hardware Mute & Solo Channel Isolation**:
  - Introduced `FilteringOutputDevice` wrapping the MIDI output stream to transparently filter out `NoteOn` and `NoteOff` events on muted or un-soloed channels during playback.
  - Retains background control parameters (Volume, Pan, Program Change, Pitch Bend) so unmuting channels transitions smoothly without voice or pitch glitches.
  - Immediately silences currently sounding notes and dims channel LEDs when a channel is muted or excluded during solo.
- **Asynchronous UI Bridge Dispatching**:
  - Updated `UiBridge.SendToJs` to use asynchronous `Dispatcher.BeginInvoke` when called from background multimedia playback threads, preventing UI lockups and eliminating MIDI playback timer jitter under heavy note traffic.

## [1.8.3] - 2026-11-16

### Fixed
- **Real-Time Playback Progress Scrubber & Time Display**:
  - Implemented 100ms high-resolution timer (`_progressTimer`) in `MidiPlayer.cs` providing continuous, butter-smooth position updates during playback.
  - Resolved frozen progress slider and `00:00` timer display during MIDI output.
  - Enhanced scrubber interaction with live time previews while dragging and clean seek-on-release to prevent MIDI serial buffer congestion.
- **Asus ROG Ally & Handheld Viewport Fit (Complete 16-Channel Visibility)**:
  - Compact, height-optimized Transport Deck and 16-Channel Mixer cards saving over 220px of vertical screen space.
  - All 16 channels (CH 1 through CH 16) now fit gracefully on 720p, 800p (Steam Deck), and 1080p @ 150% Windows scaling without slicing off Channels 9–16.
  - High-visibility, touch-friendly scrollbar styling with cyan hover glow.
- **Roland GS & Yamaha XG Drum Bank Detection (Fixes *Children*)**:
  - Pre-scans for Bank Select `CC 0 == 127` or `128` (standard GS/XG drum bank declaration).
  - Prevents auxiliary drum channels (such as Channel 16 TR-909 dance kits in *Children.mid*) from being misclassified as melodic tracks and converted into twangy steel guitar notes.
  - Automatically merges auxiliary drum tracks onto Channel 16 with Chart 1 note translation (punchy kick & snare drums).
- **Physical Multisample Range Octave Folding (C2–C7 / Notes 36–96)**:
  - Folds melodic notes outside the PSS-790's physical 61-key multisample range by octaves (`note < 36` folds up, `note > 96` folds down).
  - Preserves harmonic pitch class while preventing sub-audible dropouts on low bass notes (e.g., note 29 in *Everytime* & *Children*) and high-frequency FM aliasing squeals (e.g., note 115 in *Everytime*).
- **Acoustic Orchestral Celesta Voicing (Fixes *Everytime*)**:
  - Remapped GM 8 (Celesta) to PSS-790 Voice 43 (`GLOCKENSPIEL`), eliminating the harsh 2-operator FM buzzer of Voice 7 and providing a warm, delicate acoustic chime for piano ballad intros.

## [1.8.2] - 2026-11-16

### Added
- **Authentic Yamaha PSS-790 Windows Application Icon & Favicon**:
  - High-resolution multi-frame `.ico` (`app.ico`) with 7 standard mipmapped resolutions (`256x256`, `128x128`, `64x64`, `48x48`, `32x32`, `24x24`, `16x16`).
  - Embedded into the native Windows Win32 PE header for Windows Explorer, Desktop shortcuts, Taskbar, and Alt-Tab switcher.
  - Safe in-memory manifest resource stream loading in WPF (`MainWindow.xaml.cs`) to prevent single-file XAML URI resolution crashes.
  - Dedicated browser favicon for WebView2.
- **Library Alignment & Sync System (Sync Folders)**:
  - Dedicated **Sync** button in the sidebar footer right beside **Refresh**.
  - Interactive diff analysis modal scanning both directories to detect:
    - 📥 **Missing PSS Files**: Newly added GM songs awaiting conversion.
    - 🔄 **Outdated PSS Files**: GM source files modified after their PSS conversion.
    - 🗑️ **Orphan PSS Files**: Ghost files in `PSS-790 Midi` whose GM original was renamed or deleted.
  - Incremental, non-destructive execution with live progress reporting, automatic pruning of orphaned keys in `VoiceOverrideStore`, and cleanup of empty orphan directories.
  - Optional checkbox to force re-convert the entire library with the latest hardware rules.
- **Real-Hardware Serial DIN Buffer & Voice Protections**:
  - **Foreign SysEx Stripping**: Filters Roland GS and Yamaha XG System Exclusive reset dumps (which can burst 250+ messages at once), preventing receive FIFO buffer overruns (`bu full` lockups) across 31,250 baud serial DIN cables.
  - **Dynamic RPN 0,0 Pitch Bend Sensitivity**: Pre-scans controller streams for Registered Parameter Number pitch bend sensitivity declarations. Retains full 1:1 scale factor for $\pm 12$-semitone octave slides (e.g., *The Moon.mid* intro), while scaling standard $\pm 2$-semitone GM bends by 1/6 for the PSS-790's fixed 12-semitone pitch wheel.
  - **Missing Program Change Auto-Initialization**: Injects `ProgramChangeEvent(3)` (Voice 00: `PIANO`) at Tick 0 on active melodic channels lacking an initial voice assignment, preventing voice bleeding from prior songs.
  - **Extended Percussion Fallbacks**: Musical translation for Roland GS / GM extended percussion notes outside the 35–81 range (slaps &rarr; rimshots, sticks &rarr; woodblocks, shakers &rarr; maracas, bell trees &rarr; triangles, surdos &rarr; bass drums).
  - **Overlapping Same-Pitch Note Clamping**: Automatically truncates earlier same-pitch notes on identical channels if their duration overlaps a subsequent Note-On, preventing vintage voice-stealing circuit desynchronization.
  - **Zero-Duration Clamping**: Enforces a minimum note length of 1 tick to prevent hardware hanging notes.
- **Format-Aware 16-Channel Mixer & Voice Inspector**:
  - Inspecting native PSS-790 files correctly displays true Chart 2 hardware voices (e.g., `PIANO`, `VIOLIN 1`, `ELEC BASS 1`) and identifies Channel 16 as fixed rhythm, while GM files display General MIDI instrument labels.
- **Single-File Self-Sufficiency & Asset Cache Clearing**:
  - Embedded all WebUI assets as embedded resources inside `PSS790Player.exe` with automatic fallback extraction, ensuring the `.exe` is 100% standalone and portable.
  - Added automatic WebView2 HTTP disk cache clearing on startup and asset cache-busting so updates load immediately without stale browser cache retention.

## [1.8.1] - 2026-11-16

### Added
- **1-Click Windows SmartScreen Unblocker & Setup Guide**:
  - Added `Launch-PSS790.bat` to eliminate Microsoft Defender SmartScreen prompts on Windows 11 / Asus ROG Ally by automatically clearing the browser "Mark of the Web" (`Zone.Identifier`) before launching.
  - Added `ASUS_ROG_ALLY_SETUP.txt` companion quick-start guide explaining 1-tap launching, manual unblock, and Micro-SD / USB transfer tips.
  - Updated GitHub Actions automated release pipeline to bundle both files inside `PSS790Player-win-x64.zip` and publish them as standalone release assets.

## [1.8.0] - 2026-11-16

### Added
- **Asus ROG Ally Z1 Extreme & Handheld Touch Optimizations**:
  - **Fixed Viewport Architecture (`h-screen overflow-hidden`)**: Eliminated outer page scrolling completely. The app operates as a locked, full-viewport audio workstation matching standard professional DAWs.
  - **Independent Two-Pane Scrolling**:
    - **Left Sidebar**: 100% height pane (`h-full flex flex-col`) where only the tree container scrolls internally. The sidebar header, search input, docked batch action bar (`📦 Move`, `🗑️ Delete`), and refresh footer remain **100% visible on screen at all times without window scrolling**.
    - **Right Workspace**: Dedicated scrollable deck (`flex-1 min-w-0 h-full overflow-y-auto`) for the transport deck, 16-channel mixer, and inbound signal monitor. Scrolling the mixer never shifts or conceals the library sidebar.
  - **Compact Single-Row Header**: Redesigned navigation header (~46px) with responsive icon badges that cleanly fit on $1097\text{px}$–$1280\text{px}$ screens without wrapping.
  - **Touch Hit Target Optimization**:
    - Checkbox hit zones enlarged to $\ge 36\text{px}$ and automatically displayed on touchscreens without requiring hover.
    - Mixer and tempo range slider thumbs enlarged to $22\text{px}$ for effortless finger dragging.
    - Kinetic 120Hz smooth scrolling enabled via `touch-action: pan-y`.
    - Modal dialogs fitted with `max-h-[90vh] overflow-y-auto` to prevent off-screen clipping when on-screen touch keyboards appear.
  - **WPF Handheld Display Scaling**: Updated `MainWindow.xaml` startup defaults to `Width="1280" Height="720" MinWidth="960" MinHeight="480" WindowState="Maximized"`, preventing titlebar/taskbar clipping on 7-inch 150% scaled displays.

## [1.7.1] - 2026-11-16

### Fixed
- **Persistent Viewport-Pinned Quick-Pick Library & Batch Bar**:
  - Pinned the quick-pick library file browser to viewport height (`h-[calc(100vh-6.5rem)] sticky top-[80px]`).
  - Added flexbox shrink-protection (`shrink-0`) to the sidebar header, search bar, batch operations bar (`#sidebarBatchBar`), and bottom refresh/status footer.
  - Set the tree container (`#treeContainer`) to `flex-1 min-h-0 overflow-y-auto`, ensuring the multi-select move and delete options and library footer are always immediately visible on screen regardless of monitor resolution, laptop screen height, or Windows display scaling, eliminating window scrolling.

## [1.7.0] - 2026-11-16

### Added
- **Interactive Drag & Drop File & Folder Management**:
  - Drag individual songs or multiple selected songs directly into any folder header, root library group, or category root.
  - Drag entire folders (and all nested subtrees) into other folders or root directories with instant reorganizational drops.
  - Active drop targets illuminate with cyan outlines (`ring-2 ring-cyan-400 bg-cyan-950/60`) for intuitive visual feedback.
  - Automatic circular drag prevention: moving a folder into itself or its own descendant subfolder is cleanly blocked.
- **Multi-Select Checkboxes & Batch Actions**:
  - Checkboxes added to song rows, supporting single-click toggles and `Shift + Click` range selections.
  - New multi-select mode toggle button (`☑`) in the sidebar header to reveal checkboxes across all files.
  - Docked batch action bar showing selected count, `All` (toggle select all visible), `Clear`, `📦 Move`, and `🗑️ Delete`.
- **Batch Move & Batch Delete with Mirrored Counterparts**:
  - **Batch Move**: Relocate multiple selected songs simultaneously to any folder, with optional mirrored relocation of linked counterpart files and automatic `VoiceOverrideStore` key migrations.
  - **Batch Delete**: Permanently delete multiple selected songs in one operation, with detection and optional removal of matching counterpart songs in the mirrored library.

## [1.6.1] - 2026-11-16

### Fixed
- **Quick-Pick Tree Population & Asset Synchronization**:
  - Restored full backward compatibility for `tree.folders` in library data serialization, populating `.files` and `.count` on flat folder entries to prevent undefined property errors.
  - Ensured WebUI distribution assets (`app.js`, `index.html`) synchronize cleanly into the release distribution folder.
  - By default on initial launch, root library songs are expanded and immediately playable while custom game subfolders remain neatly collapsed.

## [1.6.0] - 2026-11-16

### Added
- **Hierarchical Recursive Subfolder Tree**:
  - Replaced flat directory lists with true nested recursive folder trees.
  - Subfolders (e.g. `Video Games\Amiga` and `Video Games\Arcade`) now nest cleanly beneath parent folders (`Video Games`).
  - Collapsing a parent folder cleanly conceals its entire subtree, keeping large libraries tidy.
- **Collapsible Root Library Songs Group**:
  - Direct songs in the root of `GM Midi` or `PSS-790 Midi` are neatly grouped into a collapsible `📁 (Root Library Songs)` node, preventing dozens of root songs from cluttering the sidebar.
- **Collapse All / Expand All Quick Toggle**:
  - Added dedicated `⊟` / `⊞` button in the sidebar header to collapse all or expand all folders with one click.
- **Persistent Folder State**:
  - Folders start cleanly collapsed on initial launch, with open/closed states preserved in `localStorage` across sessions.
- **Symmetrical Folder Mirroring between GM and PSS-790**:
  - Symmetrical two-way synchronization ensures directory structures in `GM Midi` and `PSS-790 Midi` remain in parity.
  - Creating a new folder in either category automatically creates the matching folder in the other.
  - Renaming a folder updates the counterpart folder on both sides of the mirror.
- **Linked Song Relocation across Mirrored Folders**:
  - When moving a song in `GM Midi`, the system automatically detects the matching optimized PSS-790 version and offers: `[x] Also move matching linked version to mirrored folder`.
  - Moving relocates both files to their respective mirrored destinations and automatically migrates relative keys in `Music\Midi\.pss790\overrides.json` for both songs.

## [1.5.0] - 2026-11-16

### Added
- **Right-Click Library File Management & Context Menu**:
  - Floating dark-mode context menu accessible via right-click across all songs, folders, category headers, and empty space in the Quick-Pick tree.
  - **Add Folder**: Create new subdirectories directly within `GM Midi` or `PSS-790 Midi` (or nested inside existing folders). Empty folders are preserved and visible in the tree for easy organization.
  - **Move to Folder**: Reorganize songs into any existing subfolder or back to the category root via a dedicated destination picker modal.
  - **Rename (Songs & Folders)**: Fast in-place renaming dialog. For song files, the `.mid` extension is hidden during editing to prevent accidental loss, automatically re-attaching upon save.
  - **Delete with Linked PSS-790 Pair Detection**:
    - Delete songs and folders with clear confirmation dialogs.
    - When deleting a General MIDI song, the system automatically checks `PSS-790 Midi` for matching optimized counterparts (exact name or `_PSS790` suffix) and presents a checkbox: `[x] Also delete matching PSS-790 version`.
    - Deleting purges corresponding custom voice assignments from `overrides.json` to prevent orphaned metadata.
  - **Automatic Voice Override Migration**:
    - Moving or renaming songs or folders dynamically transposes relative library keys in `Music\Midi\.pss790\overrides.json`, ensuring custom instrument tweaks are never lost during reorganization.
  - **Reveal in File Explorer**:
    - Context menu option instantly opens Windows Explorer with the target file or folder selected.

## [1.4.0] - 2026-11-16

### Added
- **Quick-Pick Folder Tree Sidebar & Jukebox Mode**:
  - Docked collapsible folder tree sidebar (290px) docked to the left of the Player & Remapper tab, with smooth animated transitions.
  - Recursive library scanning of both `Music\Midi\GM Midi` and `Music\Midi\PSS-790 Midi`, supporting game OST folders and nested subdirectories (e.g., `Doom\`, `Monkey Island\`, `Duke Nukem\`).
  - Real-time search filter with instant matching across song titles and subfolder names, automatic expansion of folders containing matches, and a 1-click clear button.
  - Split-click interaction: Single-click loads song metadata, duration, and channel strips in idle state for inspection/tweaking; double-click or `Enter` plays immediately.
  - Visual status cues: Active song highlighted with playing indicator (`🔊`), selected song with inspection indicator (`▶`), and format tags (`[PSS]` emerald / `[GM]` indigo).
- **Portable Per-Song Voice Override Memory**:
  - Persistent storage in `Music\Midi\.pss790\overrides.json`, keeping custom instrument assignments portable alongside the user's MIDI library.
  - Saved voice overrides automatically restore to the mixer dropdowns whenever a song is loaded.
  - "Reset to GM Voices" action in the mixer header to instantly clear custom overrides on disk and return to automatic GM-to-PSS-790 voice remapping.
- **Media Player Keyboard Navigation**:
  - `Space`: Play / Pause toggle.
  - `Escape`: Stop & Panic (kills sounding voices and sends All Notes Off).
  - `Arrow Up` / `Arrow Down`: Browse previous/next song in the visible library tree.
  - `Enter`: Play currently selected song.
  - `Arrow Left` / `Arrow Right`: Seek backward/forward 5 seconds.
  - `Ctrl+B`: Toggle quick-pick sidebar open/closed from anywhere.
  - Smart input guard: Arrow keys, Space, and Enter remain functional inside text fields without triggering media playback.

## [1.3.2] - 2026-11-16

### Fixed
- **Fine-Tooth Comb MIDI Audit & Reverse Remapping Corrections**:
  - Corrected `Constants.Pss790VoiceToGmMap` for Voices 36–99 against Page 41 Chart 2: Fixed critical discrepancy where Voices 36–99 previously mapped to incorrect instruments (e.g., Orchestra Hit mapping to Gunshot, Harmonica mapping to Applause, Timpani mapping to Atmosphere, Electric Bass mapping to Koto).
  - Corrected `Constants.Pss790ToGmDrumMap` for all 50 percussion sounds (Chart 1 Notes 36–85): Fixed note offsets where Note 36 (Bass Drum Reverb) and Notes 40–43 (Synth Toms) mapped to acoustic snares instead of proper kicks and toms.
  - Symmetrical Pitch Bend Scaling in ReverseRemapper: Added reverse scaling factor (6.0x, clamped to 0–16383) so recorded pitch bends from the PSS-790's +/- 1 octave wheel reproduce with standard depth on General MIDI synths (+/- 2 semitones).
  - Dual-Action Panic & Silence: Augmented `SendPanicNoteOff` with broadcast of CC 123 (All Notes Off) across all 16 channels alongside explicit Note-Off commands for active sounding voices.

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
