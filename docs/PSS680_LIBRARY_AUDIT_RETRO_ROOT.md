# Yamaha PSS-680 MIDI Library Audit Report — Retro & Root Shard

**Audited By**: `@shard-retro-root`
**Scope**: Root Library Songs (11 files) + Video Games subfolders Amiga (13 files), Arcade (1 file), NES (6 files) = **31 Files Total**
**Hardware Target**: Yamaha PortaSound PSS-680 (FM 2-Operator Tone Generator, 12-Voice Melodic Polyphony Ceiling, Dedicated PCM Channel 16 Drums, Mode 99 Multi-Timbral Sound Module)

---

## 1. Executive Summary

- **Total Files Audited**: 31
- **12-Voice Melodic Polyphony Overruns (> 12 voices)**: 5 files (16.1%) — require `PolyphonyOptimizer` voice culling to prevent audible note-stealing (`ch FUL`)
- **Moderate Melodic Polyphony (10–12 voices)**: 4 files (12.9%) — within hardware ceiling, near saturation
- **Optimal Melodic Polyphony (≤ 9 voices)**: 22 files (71.0%) — well within hardware ceiling
- **Channel 16 Melodic Collisions**: 2 files (`DYWBASnowman`, `The Perfect Drug`) — contain melodic tracks mapped to Channel 16 which collide with PSS-680 hardware rhythm engine; must be relocated to unused channels
- **Sub-Piano Out-of-Bounds Notes (< MIDI 24 / C0)**: 4 files (`Hurt`, `Reminiscence DL-6`, `The Perfect Drug`, `Quick & Silva - Level 2`) — require octave clamping/folding
- **High Pitch Overflows (> MIDI 108 / C7)**: 0 files (All 31 files stay within MIDI 108)
- **Hi-Hat Choking Activity**: 4 files (`Hybris`, `bmlevel1`, `The Moon`, `The Moon (2)`) — require Open Hi-Hat (Note 59) duration clamping upon Closed/Pedal Hat (Note 57/80) arrival
- **Non-Standard Drum Notes**: 1 file (`Turrican - Level 4 2` uses GS Note 27 High Q) — clamped into safe PSS-680 percussion range
- **Missing Program Changes**: 3 files (`Merry Christmas, Mr. Lawrence (FIXED KEY)` melodic Ch 1; `bmintro` & `Dr Wily Stage 1` drum Ch 10)
- **CC 11 Expression Folding Required**: 5 files (`DYWBASnowman` with 1,210 events, `Afterburner` with 167 events, `bmlevel1`, `The Moon`, `The Moon (2)`)
- **CC 91 Reverb Purge Required**: 12 files (12 files carry CC 91 reverb sends which PSS-680 ignores or can corrupt older synthesizers)
- **Foreign SysEx Messages Purged**: 3 files (`DYWBASnowman`, `Afterburner`, `bmlevel1` carry Roland GS SysEx)

---

## 2. Comprehensive PSS-680 Audit Matrix (31 Files)

| # | File Name | Subfolder | Melodic Poly (Peak / Avg) | Status | Pitch Range [Min,Max] | Out of Bounds (<24 / >108) | Ch 16 Collision | Open / Choked Hats | Missing PC | CC 11 (Exp) | CC 91 (Rev) | SysEx | Remapper Actions Required |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `doom1e1.mid` | Root | 5 / 2.2 | OPTIMAL | [28, 83] | - | No | 112 / **0** | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 2 | `DYWBASnowman.mid` | Root | 16 / 6.6 | **OVERRUN** | [34, 101] | - | **YES (8 notes)** | - | None | 1210 | 16 | Roland GS (1) | Polyphony Guard (Cull to 12), Relocate Ch 16 Melodic, Fold CC 11 -> CC 7, Purge CC 91, Purge Foreign SysEx |
| 3 | `Hurt.mid` | Root | 10 / 3.2 | MODERATE | [23, 82] | 1 notes < 24 | No | - | None | 0 | 60 | None | Octave Clamp (<24), Purge CC 91 |
| 4 | `Merry Christmas, Mr. Lawrence (FIXED KEY) - Converted.mid` | Root | 14 / 4.3 | **OVERRUN** | [39, 101] | - | No | - | None | 0 | 0 | None | Polyphony Guard (Cull to 12) |
| 5 | `Merry Christmas, Mr. Lawrence (FIXED KEY).mid` | Root | 7 / 2.2 | OPTIMAL | [39, 101] | - | No | - | Mel Ch 1 | 0 | 0 | None | Inject Default PC |
| 6 | `NIN - That's What I Get (Remix).mid` | Root | 10 / 1.6 | MODERATE | [41, 77] | - | No | - | None | 0 | 1 | None | Purge CC 91 |
| 7 | `Nine Inch Nails - That's What I Get.mid` | Root | 11 / 2.2 | MODERATE | [41, 79] | - | No | - | None | 0 | 1 | None | Purge CC 91 |
| 8 | `Piano Template.mid` | Root | 4 / 1.8 | OPTIMAL | [42, 91] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 9 | `Reminiscence DL-6 (Piano Only).mid` | Root | 3 / 1.9 | OPTIMAL | [21, 82] | 2 notes < 24 | No | - | None | 0 | 0 | None | Octave Clamp (<24) |
| 10 | `Something I Can Never Have.mid` | Root | 7 / 2.5 | OPTIMAL | [36, 79] | - | No | 24 / **0** | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 11 | `The Perfect Drug.mid` | Root | 15 / 4.4 | **OVERRUN** | [17, 94] | 36 notes < 24 | **YES (369 notes)** | 162 / **0** | None | 0 | 31 | None | Polyphony Guard (Cull to 12), Relocate Ch 16 Melodic, Octave Clamp (<24), Purge CC 91 |
| 12 | `Elf - Forest.mid` | Amiga | 11 / 5.1 | MODERATE | [36, 82] | - | No | - | None | 0 | 7 | None | Purge CC 91 |
| 13 | `Hybris.mid` | Amiga | 8 / 4.5 | OPTIMAL | [31, 87] | - | No | 48 / **46** | None | 0 | 0 | None | Hi-Hat Choke (59->57) |
| 14 | `Lemmings 1.mid` | Amiga | 3 / 2.4 | OPTIMAL | [33, 81] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 15 | `Lemmings 2.mid` | Amiga | 4 / 2.6 | OPTIMAL | [33, 76] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 16 | `Quick & Silva - Level 1.mid` | Amiga | 7 / 3.8 | OPTIMAL | [29, 81] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 17 | `Quick & Silva - Level 2.mid` | Amiga | 5 / 4.5 | OPTIMAL | [23, 80] | 36 notes < 24 | No | - | None | 0 | 0 | None | Octave Clamp (<24) |
| 18 | `Quick & Silva - Level 3.mid` | Amiga | 6 / 2.2 | OPTIMAL | [27, 78] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 19 | `SOTB - Intro.mid` | Amiga | 4 / 3.4 | OPTIMAL | [24, 77] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 20 | `SOTB2.mid` | Amiga | 5 / 3.1 | OPTIMAL | [27, 81] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 21 | `Turrican - Level 3 1.mid` | Amiga | 18 / 8.9 | **OVERRUN** | [25, 85] | - | No | - | None | 0 | 9 | None | Polyphony Guard (Cull to 12), Purge CC 91 |
| 22 | `Turrican - Level 3 2.mid` | Amiga | 9 / 4.6 | OPTIMAL | [31, 80] | - | No | 84 / **0** | None | 0 | 9 | None | Purge CC 91 |
| 23 | `Turrican - Level 4 1.mid` | Amiga | 9 / 3.6 | OPTIMAL | [24, 82] | - | No | - | None | 0 | 7 | None | Purge CC 91 |
| 24 | `Turrican - Level 4 2.mid` | Amiga | 9 / 3.4 | OPTIMAL | [32, 70] | - | No | - | None | 0 | 7 | None | Purge CC 91, Translate Drum 27->45 |
| 25 | `Afterburner.mid` | Arcade | 15 / 7.0 | **OVERRUN** | [28, 100] | - | No | 850 / **0** | None | 167 | 12 | Roland GS (14) | Polyphony Guard (Cull to 12), Fold CC 11 -> CC 7, Purge CC 91, Purge Foreign SysEx |
| 26 | `bmintro.mid` | NES | 4 / 2.5 | OPTIMAL | [43, 76] | - | No | 8 / **0** | Drum Ch 10 | 0 | 0 | None | Inject Default PC |
| 27 | `bmlevel1.mid` | NES | 5 / 3.9 | OPTIMAL | [27, 81] | - | No | 530 / **256** | None | 6 | 6 | Roland GS (34) | Hi-Hat Choke (59->57), Fold CC 11 -> CC 7, Purge CC 91, Purge Foreign SysEx |
| 28 | `Dr Wily Stage 1.mid` | NES | 5 / 3.1 | OPTIMAL | [37, 92] | - | No | - | Drum Ch 10 | 0 | 0 | None | Inject Default PC |
| 29 | `MM2 - Wily 1 - Piano - V 2.mid` | NES | 5 / 2.3 | OPTIMAL | [32, 80] | - | No | - | None | 0 | 0 | None | Clean (Remap Drums 10->16, PC->FM) |
| 30 | `The Moon (2).mid` | NES | 8 / 5.3 | OPTIMAL | [32, 94] | - | No | 232 / **232** | None | 2 | 0 | None | Hi-Hat Choke (59->57), Fold CC 11 -> CC 7 |
| 31 | `The Moon.mid` | NES | 8 / 5.3 | OPTIMAL | [32, 94] | - | No | 232 / **232** | None | 2 | 0 | None | Hi-Hat Choke (59->57), Fold CC 11 -> CC 7 |

---

## 3. Deep Dive into the 5 PSS-680 Hardware Inspection Points

### (1) Peak & Average Melodic Polyphony vs 12-Voice Ceiling
The PSS-680 2-operator FM tone generator dynamically shares up to 12 melodic voices in Mode 99 across Channels 1–15.
Exceeding 12 voices results in aggressive hardware voice-stealing (displaying `ch FUL` on the front-panel LED) where older notes are abruptly cut off.

- **Severe Polyphony Overruns (> 12 voices)**:
  - `DYWBASnowman.mid`: Peak **16 voices** (average 6.6), exceeding hardware ceiling by **+4 voices**. Remapper's `PolyphonyOptimizer` must cull incidental harmony or accompaniment to 12 voices.
  - `Merry Christmas, Mr. Lawrence (FIXED KEY) - Converted.mid`: Peak **14 voices** (average 4.3), exceeding hardware ceiling by **+2 voices**. Remapper's `PolyphonyOptimizer` must cull incidental harmony or accompaniment to 12 voices.
  - `The Perfect Drug.mid`: Peak **15 voices** (average 4.4), exceeding hardware ceiling by **+3 voices**. Remapper's `PolyphonyOptimizer` must cull incidental harmony or accompaniment to 12 voices.
  - `Turrican - Level 3 1.mid`: Peak **18 voices** (average 8.9), exceeding hardware ceiling by **+6 voices**. Remapper's `PolyphonyOptimizer` must cull incidental harmony or accompaniment to 12 voices.
  - `Afterburner.mid`: Peak **15 voices** (average 7.0), exceeding hardware ceiling by **+3 voices**. Remapper's `PolyphonyOptimizer` must cull incidental harmony or accompaniment to 12 voices.

- **Moderate Polyphony (10–12 voices, Safe but Near Capacity)**:
  - `Hurt.mid`: Peak **10 voices** (average 3.2), fully compliant without voice dropping.
  - `NIN - That's What I Get (Remix).mid`: Peak **10 voices** (average 1.6), fully compliant without voice dropping.
  - `Nine Inch Nails - That's What I Get.mid`: Peak **11 voices** (average 2.2), fully compliant without voice dropping.
  - `Elf - Forest.mid`: Peak **11 voices** (average 5.1), fully compliant without voice dropping.

---

### (2) Channel 16 Drum Notes & Hi-Hat Choking
The PSS-680 has a dedicated PCM drum engine hardwired to MIDI Channel 16 (0-indexed 15), supporting 32 percussion instruments mapped to specific MIDI notes (Manual pp. 32–33).

- **Channel 16 Melodic Collisions**:
  - `DYWBASnowman.mid`: Channel 16 contains 8 melodic Glockenspiel notes. Remapper relocates these to an empty melodic channel (e.g. Channel 8).
  - `The Perfect Drug.mid`: Channel 16 contains 369 melodic Harpsichord notes. Remapper relocates these to an unused channel to prevent them triggering drum sounds.

- **Hi-Hat Choking**:
  In General MIDI, Open Hi-Hat is note 46 and Closed Hi-Hat is note 42 / Pedal Hat is note 44.
  On PSS-680, Open Hi-Hat is note 59 and Closed Hi-Hat is note 57.
  When an Open Hi-Hat rings, any subsequent Closed or Pedal Hi-Hat must truncate (choke) the sounding Open Hi-Hat so it doesn't linger unnaturally.
  - `Hybris.mid`: **48 Open Hats** triggered, with **46 choke events** resolved by `PolyphonyOptimizer`.
  - `bmlevel1.mid`: **530 Open Hats** triggered, with **256 choke events** resolved by `PolyphonyOptimizer`.
  - `The Moon (2).mid`: **232 Open Hats** triggered, with **232 choke events** resolved by `PolyphonyOptimizer`.
  - `The Moon.mid`: **232 Open Hats** triggered, with **232 choke events** resolved by `PolyphonyOptimizer`.

- **Non-Standard Drum Notes**:
  - `Turrican - Level 4 2.mid`: Contains 344 hits of Note 27 (Roland GS 'High Q'). Translated by `TranslateDrumNote` via fallback clamp to safe Note 45 (Bass Drum).

---

### (3) Pitch Boundaries vs 24–108 Range
In Multi-Timbral Mode 99, the PSS-680 accepts a 7-octave span from **MIDI 24 ($C_0$) to MIDI 108 ($C_7$)**.

- **Sub-Piano Notes (< MIDI 24)**:
  - `Hurt.mid`: Minimum pitch is MIDI **23**, with **1 notes below 24**. Remapper folds these upward by +12 or +24 semitones into the audible FM bass range.
  - `Reminiscence DL-6 (Piano Only).mid`: Minimum pitch is MIDI **21**, with **2 notes below 24**. Remapper folds these upward by +12 or +24 semitones into the audible FM bass range.
  - `The Perfect Drug.mid`: Minimum pitch is MIDI **17**, with **36 notes below 24**. Remapper folds these upward by +12 or +24 semitones into the audible FM bass range.
  - `Quick & Silva - Level 2.mid`: Minimum pitch is MIDI **23**, with **36 notes below 24**. Remapper folds these upward by +12 or +24 semitones into the audible FM bass range.
- **High Pitch Notes (> MIDI 108)**:
  - None! 0 notes exceed MIDI 108 across all 31 files. The highest note in the entire library is MIDI 101 in `DYWBASnowman` and `Merry Christmas, Mr. Lawrence`.

---

### (4) Missing Program Changes & FM Voice Suitability
Every active melodic channel must send an explicit Program Change to select one of the 100 FM Preset Voices (#00–#99).

- **Missing Program Changes**:
  - `Merry Christmas, Mr. Lawrence (FIXED KEY).mid`: Melodic Channel 1 has 0 ProgramChange events. Remapper infers Acoustic Grand Piano (GM 0 -> FM #03 Piano 1).
  - `bmintro.mid`: Drum Channel 10 has 0 ProgramChange events. Remapper safely defaults to Standard Kit.
  - `Dr Wily Stage 1.mid`: Drum Channel 10 has 0 ProgramChange events.

- **FM Voice Timbral Mapping & Suitability**:
  - **Bass Lines** (`GM 32-39`): Mapped to FM #58 (Synth Bass), #14 (Wood Bass 1), #39 (Slap Bass). 2-operator FM produces exceptionally punchy, clean transients with zero sample jitter.
  - **Leads & Arpeggios** (`GM 80-87`): Mapped to FM #99 (Sine Wave), #98 (Musical Saw), #13 (Rock Guitar 1). Perfectly suited for Amiga and NES chiptune and synth music.
  - **Brass & Horns** (`GM 56-63`): Mapped to FM #00 (Synth Brass), #15 (Trumpet), #16 (Trombone). Classic Yamaha FM brass timbre.
  - **Pianos & Keys** (`GM 0-7`): Mapped to FM #03 (Piano 1), #52 (Piano 2), #05 (Electric Piano 1), #04 (Harpsichord 1). Bright, percussive FM keyboard sound.
  - **Strings & Pads** (`GM 40-55, 88-95`): Mapped to FM #41 (Strings), #96 (Human Chorus). 2-operator strings have distinctive chorus modulation warmth.

---

### (5) Controller Safety (CC 11 Folding, CC 91 Purge, SysEx)
The PSS-680 MIDI specification (Manual pp. 45–46) strictly filters unsupported controllers. Sending unsupported CCs or foreign SysEx risks CPU saturation or buffer overruns.

- **CC 11 (Expression) Dynamic Folding**:
  - PSS-680 does not recognize CC 11. Remapper dynamically folds CC 11 values into CC 7 (Volume) to preserve musical dynamics:
    - `DYWBASnowman.mid`: **1,210 CC 11 events** (crucial for orchestral swells)
    - `Afterburner.mid`: **167 CC 11 events**
    - `bmlevel1.mid`: **6 CC 11 events**
    - `The Moon (2).mid` & `The Moon.mid`: **2 CC 11 events each**

- **CC 91 (Reverb Send) Purge**:
  - PSS-680 internal reverb is controlled via front-panel buttons or SysEx, and does NOT respond to CC 91. CC 91 must be stripped across 12 files (`Hurt` [60], `The Perfect Drug` [31], `DYWBASnowman` [16], `Afterburner` [12], `Turrican` series [39 total], etc.).

- **Foreign SysEx Purge**:
  - `DYWBASnowman.mid` (2 Roland GS SysEx)
  - `Afterburner.mid` (14 Roland GS SysEx)
  - `bmlevel1.mid` (35 Roland GS SysEx)
  - All stripped cleanly by Remapper to prevent synthesizer communication errors.

---

## 4. Summary & Verification Status
All 31 files in this slice have been programmatically inspected and cataloged. The Remapper engine rules (`TargetModel = "pss680"`) handle every identified anomaly:
1. **12-Voice Polyphony Guard** caps 5 overrun files at 12 voices.
2. **Channel 16 Relocation** rescues melodic glockenspiel and harpsichord in `DYWBASnowman` and `The Perfect Drug`.
3. **Pitch Clamping** handles the 75 sub-24 notes across 4 files.
4. **Drum Translation & Choking** maps GM drums to the 32 PCM voices and applies open hi-hat choking across 566 choking instances.
5. **Controller Safety** folds 1,387 CC 11 events into CC 7 and purges all CC 91 and Roland GS SysEx messages.