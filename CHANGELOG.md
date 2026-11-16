# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
