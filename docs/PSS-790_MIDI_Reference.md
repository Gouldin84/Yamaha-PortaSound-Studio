# Yamaha PortaSound PSS-790 — MIDI Technical Reference & Device Limitations

This document provides a comprehensive technical reference for the **Yamaha PortaSound PSS-790** (released c. 1990) based on the official Owner's Manual and MIDI Implementation Chart. It documents the tone generator architecture, voice and percussion tables, General MIDI (GM) conversion matrices, and critical hardware limitations.

---

## 1. Device Architecture & Specifications

| Parameter | Specification | Notes |
| :--- | :--- | :--- |
| **Tone Generator** | Advanced Wave Memory (AWM) | Sample playback synthesis |
| **Polyphony** | 28 notes maximum (Dynamic Allocation) | Multi-element voices consume multiple voices per note |
| **Multi-Timbre** | 16 Channels simultaneously | Channels 1–15 Melodic, Channel 16 Percussion |
| **Physical Keyboard** | 61 half-sized keys (C1–C6) | Velocity-sensitive (Note-On velocity 1–127) |
| **Playable Note Range** | MIDI Notes 12–108 (C-1 to C7) | Notes outside range are clamped or muted |
| **Preset Voices** | 100 Voices (Numbered 00–99) | Selected via non-linear MIDI Program Change bytes |
| **Percussion Sounds** | 50 Percussion Instruments | Mapped to MIDI notes 36–85 on Channel 16 |
| **Supported Controllers** | CC 1 (Vibrato), CC 7 (Volume), CC 91 (Reverb) | CC 64 (Sustain), CC 10 (Pan), CC 11 (Expression) are NOT supported |
| **Pitch Bend** | +/- 1 Octave (+/- 12 semitones), 7-bit resolution | Non-adjustable range; GM requires 1/6 scaling |
| **MIDI Connectors** | MIDI IN, MIDI OUT, MIDI THRU (5-pin DIN) | MIDI OUT merges generated data with MIDI IN echo |

---

## 2. Critical Hardware Limitations & Mitigations

### Limitation 1: Buffer Overrun (`<bu full>`)
* **Symptom**: If the Master device sends high-density MIDI data, the PSS-790 display flashes `<bu full>` for one second, and **all sounding notes immediately cut off**.
* **Cause**: The 1990 internal 8-bit microcomputer has a very limited MIDI input buffer. It quickly overfills if flooded with dense MIDI message streams.
* **Problematic Data**:
  - Unsupported Control Changes (e.g., continuous Pan CC10, Expression CC11, Breath CC2).
  - Dense Pitch Bend sweeps (e.g., hundreds of pitch bend events per second).
  - Channel Aftertouch (`0xDn`) or Polyphonic Key Pressure (`0xAn`).
  - SysEx dumps or non-PSS-790 SysEx messages.
* **Mitigation in Remapper / Player**:
  - Filter out all unsupported CCs (strip CCs other than 1, 7, and 91).
  - Strip all Aftertouch and foreign SysEx messages.
  - Thin dense Pitch Bend events (decimate high-frequency streams).

### Limitation 2: Channel Full (`<Ch Full>`) & Multi-Element Polyphony Dropouts
* **Symptom**: Display flashes `<Ch Full>` and notes drop out abruptly.
* **Cause**: While the theoretical polyphony is 28 notes, **individual AWM voices consist of multiple elements (oscillators)**:
  - Standard voice: 1 element &rarr; 28 simultaneous notes.
  - Layered / Dual voice: 2 elements &rarr; 14 simultaneous notes.
  - 4-element voice (Vector Synth mix or complex presets): 4 elements &rarr; **only 7 simultaneous notes!**
* **Mitigation Strategy — Best-Endeavour Polyphony Optimization**:
  To protect melody, vocal lines, and core harmonic voice parts without noticeable degradation, polyphony overruns are handled by shedding low-impact incidental components first:
  1. **Percussion Decay & Voice Hoard Clamping**:
     - General MIDI files frequently leave Note-On events sounding for bars even though percussion sounds decay quickly. On AWM modules, open notes occupy generator elements until Note-Off is received.
     - Clamping percussion note durations (e.g., max 0.25s for hits/clicks, 0.8s for cymbals) and cutting open hi-hats when closed hi-hats fire (choke groups) recovers substantial polyphony with zero audible loss.
  2. **Tiered Culling Hierarchy (Active when approaching polyphony ceiling &ge; 22–24 notes)**:
     - **Tier 1 — Incidental Percussion (Dropped First)**: High-register, low-audibility percussions: Triangles, Guiro / brush squeeze, Cuica, Whistle, Cabasa / Maracas, Agogo, Claves, Tambourine, and secondary cymbal accents (Splash, Ride Cup).
     - **Tier 2 — High-Density Rhythm Subdivisions (Dropped Second)**: Thin rapid 16th-note or 32nd-note hi-hat / shaker rolls to 8th-notes during heavy chord peaks.
     - **Tier 3 — Background Sound FX & Low-Velocity Doublings (Dropped Third)**: Low-velocity incidental pads, sound effects (rain, breath, seashore), or unessential doubled octave notes in background tracks.
     - **PROTECTED (Never Dropped)**: Lead melody, vocal melody, bassline (Channel 15), core kick drum (Bass Drum Lo/Hi), and primary snare hits.

### Limitation 3: Channel 16 Percussion Lock & Program Change Rejection
* **Symptom**: Sending Program Change messages on Channel 16 has no effect; GM files playing melody on Channel 16 play drum noises.
* **Cause**: Note \*8 of the MIDI Implementation Chart explicitly states: *"Ignores the Message based on Ch. 16"*. Channel 16 is hardwired as the Rhythm / Percussion channel (Voice 96 PERCUSSIONS).
* **Mitigation**:
  - Standard GM drum tracks on Channel 10 (0-indexed 9) **must be relocated to Channel 16** (0-indexed 15).
  - If the original MIDI file contains melodic notes on Channel 16, they **must be moved to a free channel (1–15)**.

### Limitation 4: Fixed +/- 1 Octave Pitch Bend Range
* **Symptom**: Guitar bends, synth slides, and vibrato bends sound wildly out of tune (bending up to 6 times too far).
* **Cause**: General MIDI defines default Pitch Bend Range as **+/- 2 semitones**. The PSS-790 has a **fixed range of +/- 12 semitones (+/- 1 octave)** and does not recognize RPN 0 (Pitch Bend Sensitivity).
* **Mitigation**:
  - Scale all incoming MIDI Pitch Bend values by a factor of **1/6 (~0.1667)** when playing GM files:
    $$\text{PSS790\_Bend} = \text{round}\left(\frac{\text{GM\_Bend}}{6}\right)$$

### Limitation 5: Lack of All Notes Off / Reset Controllers Support
* **Symptom**: When pausing, stopping, or seeking in a MIDI player, notes hang indefinitely on the PSS-790.
* **Cause**: The PSS-790 does **NOT** recognize Channel Mode Messages:
  - CC 120 (All Sound Off): **Ignored**
  - CC 121 (Reset All Controllers): **Ignored**
  - CC 123 (All Notes Off): **Ignored**
* **Mitigation**:
  - The player cannot rely on CC 123 for "Panic" or Stop.
  - To stop playback cleanly, the engine must send explicit `Note Off` (`0x8n` or `0x9n v=0`) messages for all active notes on all 16 channels.

### Limitation 6: Unsupported Sustain Pedal (CC 64) & Stereo Pan (CC 10)
* **Symptom**: Piano pieces that rely on sustain pedal sound dry and staccato; stereo panning automation does not pan instruments.
* **Cause**: The PSS-790 hardware lacks sustain pedal circuitry and only implements CC 1, CC 7, and CC 91.
* **Mitigation**:
  - CC 64 events can either be filtered out to save buffer bandwidth, or the remapper can optionally extend note durations during legato passages when CC 64 is active.

### Limitation 7: MIDI OUT Soft-Thru Loopback Hazard
* **Symptom**: Instant lockup or runaway feedback when connecting both MIDI IN and MIDI OUT to a computer.
* **Cause**: As stated on manual page 32: *"This Terminal also Transmits the same Channel Messages as Received by the MIDI IN Terminal."* The PSS-790 merges received MIDI IN messages into MIDI OUT.
* **Mitigation**:
  - Computer software must disable MIDI Thru / Echo. The player should only open the MIDI OUT port to the PSS-790.

---

## 3. PSS-790 Voice Table & MIDI Program Change Numbers (Chart 2)

On the PSS-790, panel Voice numbers (00–99) are mapped to raw MIDI Program Change bytes (0–124). To select a specific Voice on Channels 1–15, the master device must transmit the corresponding **MIDI Program Number**.

| Voice # | Voice Name | Category | MIDI Program # |
| :---: | :--- | :--- | :---: |
| **00** | PIANO | Keyboard | 3 |
| **01** | ELECTRIC PIANO 1 | Keyboard | 5 |
| **02** | ELECTRIC PIANO 2 | Keyboard | 54 |
| **03** | HARPSICHORD 1 | Keyboard | 4 |
| **04** | HARPSICHORD 2 | Keyboard | 53 |
| **05** | HONKY-TONK PIANO | Keyboard | 25 |
| **06** | CRAVI (Clavinet) | Keyboard | 30 |
| **07** | CELESTA | Keyboard / Perc | 6 |
| **08** | PIPE ORGAN | Organ | 2 |
| **09** | REED ORGAN | Organ | 49 |
| **10** | JAZZ ORGAN 1 | Organ | 1 |
| **11** | JAZZ ORGAN 2 | Organ | 27 |
| **12** | TREMOLO ORGAN | Organ | 28 |
| **13** | FULL ORGAN | Organ | 29 |
| **14** | ACCORDION | Organ | 31 |
| **15** | DISTORTION GUITAR | Guitar | 13 |
| **16** | ELECTRIC GUITAR | Guitar | 68 |
| **17** | TREMOLO GUITAR | Guitar | 69 |
| **18** | MUTE GUITAR | Guitar | 70 |
| **19** | DISTORTION GUITAR FLANGE | Guitar | 108 |
| **20** | E. GUITAR FLANGE | Guitar | 121 |
| **21** | MUTE GUITAR ECHO | Guitar | 116 |
| **22** | E. GUITAR ECHO | Guitar | 123 |
| **23** | JAZZ GUITAR | Guitar | 12 |
| **24** | JAZZ GUITAR ECHO | Guitar | 122 |
| **25** | FOLK GUITAR | Guitar | 36 |
| **26** | 12 STRING GUITAR | Guitar | 72 |
| **27** | GUT GUITAR | Guitar | 73 |
| **28** | GUT GUITAR ECHO | Guitar | 124 |
| **29** | STEEL GUITAR | Guitar | 33 |
| **30** | VIOLIN 1 | Strings | 10 |
| **31** | VIOLIN 2 | Strings | 67 |
| **32** | PIZZICATO VIOLIN | Strings | 65 |
| **33** | CELLO | Strings | 11 |
| **34** | STRINGS 1 | Ensemble | 41 |
| **35** | STRINGS 2 | Ensemble | 50 |
| **36** | PIZZICATO STRINGS | Ensemble | 78 |
| **37** | HARP | Strings | 37 |
| **38** | UKULELE | Plucked | 40 |
| **39** | BANJO | Plucked | 34 |
| **40** | VIBRAPHONE | Chromatic | 7 |
| **41** | MARIMBA | Chromatic | 8 |
| **42** | XYLOPHONE | Chromatic | 59 |
| **43** | GLOCKENSPIEL | Chromatic | 32 |
| **44** | TRUMPET | Brass | 15 |
| **45** | MUTE TRUMPET | Brass | 44 |
| **46** | TRUMPET ECHO | Brass | 118 |
| **47** | MUTE TRUMPET ECHO | Brass | 120 |
| **48** | FLUGELHORN | Brass | 87 |
| **49** | TROMBONE | Brass | 16 |
| **50** | HORN | Brass | 17 |
| **51** | TUBA | Brass | 84 |
| **52** | BRASS ENSEMBLE | Brass | 92 |
| **53** | BRASS HIT | Brass / Accent | 104 |
| **54** | CLARINET | Woodwind | 19 |
| **55** | FLUTE | Woodwind | 20 |
| **56** | PICCOLO | Woodwind | 103 |
| **57** | SOPRANO SAX | Reed | 101 |
| **58** | ALTO SAX | Reed | 18 |
| **59** | TENOR SAX | Reed | 102 |
| **60** | BASSOON | Woodwind | 21 |
| **61** | RECORDER | Woodwind | 88 |
| **62** | SAMBA WHISTLE | Wind / FX | 91 |
| **63** | WOODWIND ENSEMBLE | Woodwind | 93 |
| **64** | CHORUS | Vocal | 96 |
| **65** | SYNTH BRASS 1 | Synth | 0 |
| **66** | SYNTH BRASS 2 | Synth | 82 |
| **67** | SYNTH BRASS 3 | Synth | 115 |
| **68** | SYNTH CHORUS | Synth Vocal | 94 |
| **69** | SYNTH FLUTE 1 | Synth Lead | 112 |
| **70** | SYNTH FLUTE 2 | Synth Lead | 47 |
| **71** | SYNTH REED 1 | Synth Lead | 45 |
| **72** | SYNTH REED 2 | Synth Lead | 85 |
| **73** | SYNTH REED 3 | Synth Lead | 113 |
| **74** | SYNTH STRINGS 1 | Synth Pad | 51 |
| **75** | SYNTH STRINGS 2 | Synth Pad | 109 |
| **76** | SYNTH STRINGS 3 | Synth Pad | 119 |
| **77** | SYNTH PIANO 1 | Synth Lead | 60 |
| **78** | SYNTH PIANO 2 | Synth Lead | 117 |
| **79** | SYNTH PIANO 3 | Synth Lead | 110 |
| **80** | SYNTH PIANO 4 | Synth Lead | 95 |
| **81** | SYNTH PAN VOICE | Synth Pad | 111 |
| **82** | FANTASY | Synth FX / Pad | 64 |
| **83** | ELECTRIC BASS | Bass | 38 |
| **84** | SLAP BASS | Bass | 39 |
| **85** | FRETLESS BASS | Bass | 80 |
| **86** | PICK BASS | Bass | 79 |
| **87** | MUTE BASS | Bass | 114 |
| **88** | MUTE BASS ECHO | Bass | 107 |
| **89** | WOOD BASS 1 | Bass | 14 |
| **90** | WOOD BASS 2 | Bass | 81 |
| **91** | SYNTH BASS 1 | Synth Bass | 58 |
| **92** | SYNTH BASS 2 | Synth Bass | 86 |
| **93** | BOWED BASS | Bass | 35 |
| **94** | STEEL DRUM | Percussive | 9 |
| **95** | TIMPANI | Percussive | 66 |
| **96** | PERCUSSIONS | Percussion Map | 100 |
| **97** | HARMONICA | Wind | 22 |
| **98** | MUSIC BOX | Chromatic | 24 |
| **99** | ORCHESTRA HIT | Accent | 90 |

---

## 4. Percussion Note Mapping (Chart 1)

Percussion sounds are played on **Channel 16** via Note On messages (`0x9F`):

| Note # | Note Name | Instrument Name | Notes / Best GM Origin |
| :---: | :---: | :--- | :--- |
| **36** | C1 | BASS DRUM REVERB | GM 35 (Acoustic Bass Drum) / GM 36 |
| **37** | C#1 | TRIANGLE MUTE | GM 80 (Mute Triangle) |
| **38** | D1 | SYNTH SNARE | GM 40 (Electric Snare) |
| **39** | D#1 | TRIANGLE OPEN | GM 81 (Open Triangle) |
| **40** | E1 | SYNTH TOM BASS | Synth Tom |
| **41** | F1 | SYNTH TOM LO | Synth Tom |
| **42** | F#1 | SYNTH TOM MID | Synth Tom |
| **43** | G1 | SYNTH TOM HI | Synth Tom |
| **44** | G#1 | BASS DRUM LO | GM 35 (Acoustic Bass Drum) |
| **45** | A1 | BASS DRUM HI | GM 36 (Bass Drum 1) |
| **46** | A#1 | RIM SHOT 1 | GM 37 (Side Stick) |
| **47** | B1 | TOM BASS | GM 41 (Low Floor Tom) |
| **48** | C2 | TOM LO | GM 43 (High Floor Tom) / GM 45 (Low Tom) |
| **49** | C#2 | SNARE HI | GM 38 (Acoustic Snare) |
| **50** | D2 | TOM MID | GM 47 / 48 (Mid Tom) |
| **51** | D#2 | RIM SHOT 2 | Rim Shot |
| **52** | E2 | SNARE LO | Secondary Snare |
| **53** | F2 | TOM HI | GM 50 (High Tom) |
| **54** | F#2 | HAND CLAPS | GM 39 (Hand Clap) |
| **55** | G2 | COWBELL | GM 56 (Cowbell) |
| **56** | G#2 | CABASA | GM 69 (Cabasa) / GM 70 (Maracas) |
| **57** | A2 | HI HAT CLOSED | GM 42 (Closed Hi-Hat) |
| **58** | A#2 | BRASH HIT | Brush Hit |
| **59** | B2 | HI HAT OPEN | GM 46 (Open Hi-Hat) |
| **60** | C3 | CRASH CYMBAL | GM 49 (Crash 1) / GM 57 (Crash 2) |
| **61** | C#3 | SPLASH CYMBAL | GM 55 (Splash Cymbal) |
| **62** | D3 | RIDE CYMBAL CUP | GM 53 (Ride Bell) / GM 51 (Ride 1) |
| **63** | D#3 | RIDE CYMBAL EDGE | GM 59 (Ride 2) |
| **64** | E3 | CONGA LO | GM 64 (Low Conga) |
| **65** | F3 | CONGA HI | GM 63 (Open High Conga) |
| **66** | F#3 | CONGA HI MUTE | GM 62 (Mute High Conga) |
| **67** | G3 | BONGO LO | GM 61 (Low Bongo) |
| **68** | G#3 | BONGO HI | GM 60 (High Bongo) |
| **69** | A3 | TIMBALE LO | GM 66 (Low Timbale) |
| **70** | A#3 | TIMBALE HI | GM 65 (High Timbale) |
| **71** | B3 | TAMBOURINE | GM 54 (Tambourine) |
| **72** | C4 | CLAVES LO | GM 75 (Claves) / GM 77 (Low Woodblock) |
| **73** | C#4 | CLAVES HI | GM 76 (High Woodblock) |
| **74** | D4 | AGOGO LO | GM 68 (Low Agogo) |
| **75** | D#4 | AGOGO HI | GM 67 (High Agogo) |
| **76** | E4 | CUICA LO | GM 78 (Mute Cuica) |
| **77** | F4 | CUICA HI | GM 79 (Open Cuica) |
| **78** | F#4 | WHISTLE | GM 71 / 72 (Whistle) |
| **79** | G4 | BRASH SQUEEZE | GM 73 / 74 (Guiro) |
| **80** | G#4 | HI HAT FOOT | GM 44 (Pedal Hi-Hat) |
| **81** | A4 | SNARE GATED REVERB | Gated Snare |
| **82** | A#4 | ONE | Voice Sample ("One") |
| **83** | B4 | TWO | Voice Sample ("Two") |
| **84** | C5 | THREE | Voice Sample ("Three") |
| **85** | C#5 | FOUR | Voice Sample ("Four") |

---

## 5. General MIDI to PSS-790 Percussion Translation Lookup

When translating GM Drum tracks (standard Channel 10), remap incoming note numbers according to this translation table:

| GM Note | GM Instrument Name | PSS-790 Note | PSS-790 Instrument Name |
| :---: | :--- | :---: | :--- |
| **35** | Acoustic Bass Drum | **44** | BASS DRUM LO |
| **36** | Bass Drum 1 | **45** | BASS DRUM HI |
| **37** | Side Stick | **46** | RIM SHOT 1 |
| **38** | Acoustic Snare | **49** | SNARE HI |
| **39** | Hand Clap | **54** | HAND CLAPS |
| **40** | Electric Snare | **38** | SYNTH SNARE |
| **41** | Low Floor Tom | **47** | TOM BASS |
| **42** | Closed Hi-Hat | **57** | HI HAT CLOSED |
| **43** | High Floor Tom | **48** | TOM LO |
| **44** | Pedal Hi-Hat | **80** | HI HAT FOOT |
| **45** | Low Tom | **48** | TOM LO |
| **46** | Open Hi-Hat | **59** | HI HAT OPEN |
| **47** | Low-Mid Tom | **50** | TOM MID |
| **48** | Hi-Mid Tom | **50** | TOM MID |
| **49** | Crash Cymbal 1 | **60** | CRASH CYMBAL |
| **50** | High Tom | **53** | TOM HI |
| **51** | Ride Cymbal 1 | **62** | RIDE CYMBAL CUP |
| **52** | Chinese Cymbal | **60** | CRASH CYMBAL |
| **53** | Ride Bell | **62** | RIDE CYMBAL CUP |
| **54** | Tambourine | **71** | TAMBOURINE |
| **55** | Splash Cymbal | **61** | SPLASH CYMBAL |
| **56** | Cowbell | **55** | COWBELL |
| **57** | Crash Cymbal 2 | **60** | CRASH CYMBAL |
| **58** | Vibraslap | **56** | CABASA |
| **59** | Ride Cymbal 2 | **63** | RIDE CYMBAL EDGE |
| **60** | Hi Bongo | **68** | BONGO HI |
| **61** | Low Bongo | **67** | BONGO LO |
| **62** | Mute Hi Conga | **66** | CONGA HI MUTE |
| **63** | Open Hi Conga | **65** | CONGA HI |
| **64** | Low Conga | **64** | CONGA LO |
| **65** | High Timbale | **70** | TIMBALE HI |
| **66** | Low Timbale | **69** | TIMBALE LO |
| **67** | High Agogo | **75** | AGOGO HI |
| **68** | Low Agogo | **74** | AGOGO LO |
| **69** | Cabasa | **56** | CABASA |
| **70** | Maracas | **56** | CABASA |
| **71** | Short Whistle | **78** | WHISTLE |
| **72** | Long Whistle | **78** | WHISTLE |
| **73** | Short Guiro | **79** | BRASH SQUEEZE |
| **74** | Long Guiro | **79** | BRASH SQUEEZE |
| **75** | Claves | **72** | CLAVES LO |
| **76** | Hi Wood Block | **73** | CLAVES HI |
| **77** | Low Wood Block | **72** | CLAVES LO |
| **78** | Mute Cuica | **76** | CUICA LO |
| **79** | Open Cuica | **77** | CUICA HI |
| **80** | Mute Triangle | **37** | TRIANGLE MUTE |
| **81** | Open Triangle | **39** | TRIANGLE OPEN |

---

## 6. General MIDI Instrument (0–127) to PSS-790 Translation Map

To play General MIDI files, map the GM Program Number (0–127) to the target PSS-790 Voice, and transmit the corresponding PSS-790 MIDI Program Number byte:

| GM # | GM Instrument Name | PSS-790 Voice # | PSS-790 Voice Name | Transmit MIDI Prog # |
| :---: | :--- | :---: | :--- | :---: |
| **0** | Acoustic Grand Piano | 00 | PIANO | 3 |
| **1** | Bright Acoustic Piano | 00 | PIANO | 3 |
| **2** | Electric Grand Piano | 01 | ELECTRIC PIANO 1 | 5 |
| **3** | Honky-tonk Piano | 05 | HONKY-TONK PIANO | 25 |
| **4** | Electric Piano 1 (Rhodes) | 01 | ELECTRIC PIANO 1 | 5 |
| **5** | Electric Piano 2 (Chorused) | 02 | ELECTRIC PIANO 2 | 54 |
| **6** | Harpsichord | 03 | HARPSICHORD 1 | 4 |
| **7** | Clavinet | 06 | CRAVI | 30 |
| **8** | Celesta | 07 | CELESTA | 6 |
| **9** | Glockenspiel | 43 | GLOCKENSPIEL | 32 |
| **10** | Music Box | 98 | MUSIC BOX | 24 |
| **11** | Vibraphone | 40 | VIBRAPHONE | 7 |
| **12** | Marimba | 41 | MARIMBA | 8 |
| **13** | Xylophone | 42 | XYLOPHONE | 59 |
| **14** | Tubular Bells | 43 | GLOCKENSPIEL | 32 |
| **15** | Dulcimer | 37 | HARP | 37 |
| **16** | Drawbar Organ | 10 | JAZZ ORGAN 1 | 1 |
| **17** | Percussive Organ | 11 | JAZZ ORGAN 2 | 27 |
| **18** | Rock Organ | 13 | FULL ORGAN | 29 |
| **19** | Church Organ | 08 | PIPE ORGAN | 2 |
| **20** | Reed Organ | 09 | REED ORGAN | 49 |
| **21** | Accordion | 14 | ACCORDION | 31 |
| **22** | Harmonica | 97 | HARMONICA | 22 |
| **23** | Tango Accordion | 14 | ACCORDION | 31 |
| **24** | Acoustic Guitar (nylon) | 27 | GUT GUITAR | 73 |
| **25** | Acoustic Guitar (steel) | 29 | STEEL GUITAR | 33 |
| **26** | Electric Guitar (jazz) | 23 | JAZZ GUITAR | 12 |
| **27** | Electric Guitar (clean) | 16 | ELECTRIC GUITAR | 68 |
| **28** | Electric Guitar (muted) | 18 | MUTE GUITAR | 70 |
| **29** | Overdriven Guitar | 15 | DISTORTION GUITAR | 13 |
| **30** | Distortion Guitar | 15 | DISTORTION GUITAR | 13 |
| **31** | Guitar Harmonics | 26 | 12 STRING GUITAR | 72 |
| **32** | Acoustic Bass | 89 | WOOD BASS 1 | 14 |
| **33** | Electric Bass (finger) | 83 | ELECTRIC BASS | 38 |
| **34** | Electric Bass (pick) | 86 | PICK BASS | 79 |
| **35** | Fretless Bass | 85 | FRETLESS BASS | 80 |
| **36** | Slap Bass 1 | 84 | SLAP BASS | 39 |
| **37** | Slap Bass 2 | 84 | SLAP BASS | 39 |
| **38** | Synth Bass 1 | 91 | SYNTH BASS 1 | 58 |
| **39** | Synth Bass 2 | 92 | SYNTH BASS 2 | 86 |
| **40** | Violin | 30 | VIOLIN 1 | 10 |
| **41** | Viola | 33 | CELLO | 11 |
| **42** | Cello | 33 | CELLO | 11 |
| **43** | Contrabass | 93 | BOWED BASS | 35 |
| **44** | Tremolo Strings | 35 | STRINGS 2 | 50 |
| **45** | Pizzicato Strings | 36 | PIZZICATO STRINGS | 78 |
| **46** | Orchestral Harp | 37 | HARP | 37 |
| **47** | Timpani | 95 | TIMPANI | 66 |
| **48** | String Ensemble 1 | 34 | STRINGS 1 | 41 |
| **49** | String Ensemble 2 | 35 | STRINGS 2 | 50 |
| **50** | Synth Strings 1 | 74 | SYNTH STRINGS 1 | 51 |
| **51** | Synth Strings 2 | 75 | SYNTH STRINGS 2 | 109 |
| **52** | Choir Aahs | 64 | CHORUS | 96 |
| **53** | Voice Oohs | 68 | SYNTH CHORUS | 94 |
| **54** | Synth Voice | 81 | SYNTH PAN VOICE | 111 |
| **55** | Orchestra Hit | 99 | ORCHESTRA HIT | 90 |
| **56** | Trumpet | 44 | TRUMPET | 15 |
| **57** | Trombone | 49 | TROMBONE | 16 |
| **58** | Tuba | 51 | TUBA | 84 |
| **59** | Muted Trumpet | 45 | MUTE TRUMPET | 44 |
| **60** | French Horn | 50 | HORN | 17 |
| **61** | Brass Section | 52 | BRASS ENSEMBLE | 92 |
| **62** | Synth Brass 1 | 65 | SYNTH BRASS 1 | 0 |
| **63** | Synth Brass 2 | 66 | SYNTH BRASS 2 | 82 |
| **64** | Soprano Sax | 57 | SOPRANO SAX | 101 |
| **65** | Alto Sax | 58 | ALTO SAX | 18 |
| **66** | Tenor Sax | 59 | TENOR SAX | 102 |
| **67** | Baritone Sax | 59 | TENOR SAX | 102 |
| **68** | Oboe | 63 | WOODWIND ENSEMBLE | 93 |
| **69** | English Horn | 50 | HORN | 17 |
| **70** | Bassoon | 60 | BASSOON | 21 |
| **71** | Clarinet | 54 | CLARINET | 19 |
| **72** | Piccolo | 56 | PICCOLO | 103 |
| **73** | Flute | 55 | FLUTE | 20 |
| **74** | Recorder | 61 | RECORDER | 88 |
| **75** | Pan Flute | 81 | SYNTH PAN VOICE | 111 |
| **76** | Blown Bottle | 69 | SYNTH FLUTE 1 | 112 |
| **77** | Shakuhachi | 81 | SYNTH PAN VOICE | 111 |
| **78** | Whistle | 62 | SAMBA WHISTLE | 91 |
| **79** | Ocarina | 55 | FLUTE | 20 |
| **80** | Lead 1 (square) | 71 | SYNTH REED 1 | 45 |
| **81** | Lead 2 (sawtooth) | 65 | SYNTH BRASS 1 | 0 |
| **82** | Lead 3 (calliope) | 69 | SYNTH FLUTE 1 | 112 |
| **83** | Lead 4 (chiff) | 70 | SYNTH FLUTE 2 | 47 |
| **84** | Lead 5 (charang) | 72 | SYNTH REED 2 | 85 |
| **85** | Lead 6 (voice) | 68 | SYNTH CHORUS | 94 |
| **86** | Lead 7 (fifths) | 73 | SYNTH REED 3 | 113 |
| **87** | Lead 8 (bass+lead) | 91 | SYNTH BASS 1 | 58 |
| **88** | Pad 1 (new age) | 82 | FANTASY | 64 |
| **89** | Pad 2 (warm) | 74 | SYNTH STRINGS 1 | 51 |
| **90** | Pad 3 (polysynth) | 75 | SYNTH STRINGS 2 | 109 |
| **91** | Pad 4 (choir) | 64 | CHORUS | 96 |
| **92** | Pad 5 (bowed) | 93 | BOWED BASS | 35 |
| **93** | Pad 6 (metallic) | 76 | SYNTH STRINGS 3 | 119 |
| **94** | Pad 7 (halo) | 82 | FANTASY | 64 |
| **95** | Pad 8 (sweep) | 76 | SYNTH STRINGS 3 | 119 |
| **96** | FX 1 (rain) | 98 | MUSIC BOX | 24 |
| **97** | FX 2 (soundtrack) | 82 | FANTASY | 64 |
| **98** | FX 3 (crystal) | 07 | CELESTA | 6 |
| **99** | FX 4 (atmosphere) | 82 | FANTASY | 64 |
| **100** | FX 5 (brightness) | 77 | SYNTH PIANO 1 | 60 |
| **101** | FX 6 (goblins) | 82 | FANTASY | 64 |
| **102** | FX 7 (echoes) | 22 | E. GUITAR ECHO | 123 |
| **103** | FX 8 (sci-fi) | 82 | FANTASY | 64 |
| **104** | Sitar | 39 | BANJO | 34 |
| **105** | Banjo | 39 | BANJO | 34 |
| **106** | Shamisen | 39 | BANJO | 34 |
| **107** | Koto | 37 | HARP | 37 |
| **108** | Kalimba | 98 | MUSIC BOX | 24 |
| **109** | Bag pipe | 09 | REED ORGAN | 49 |
| **110** | Fiddle | 30 | VIOLIN 1 | 10 |
| **111** | Shanai | 58 | ALTO SAX | 18 |
| **112** | Tinkle Bell | 07 | CELESTA | 6 |
| **113** | Agogo | 43 | GLOCKENSPIEL | 32 |
| **114** | Steel Drums | 94 | STEEL DRUM | 9 |
| **115** | Woodblock | 41 | MARIMBA | 8 |
| **116** | Taiko Drum | 95 | TIMPANI | 66 |
| **117** | Melodic Tom | 95 | TIMPANI | 66 |
| **118** | Synth Drum | 95 | TIMPANI | 66 |
| **119** | Reverse Cymbal | 96 | PERCUSSIONS | 100 |
| **120** | Guitar Fret Noise | 18 | MUTE GUITAR | 70 |
| **121** | Breath Noise | 61 | RECORDER | 88 |
| **122** | Seashore | 82 | FANTASY | 64 |
| **123** | Bird Tweet | 56 | PICCOLO | 103 |
| **124** | Telephone Ring | 43 | GLOCKENSPIEL | 32 |
| **125** | Helicopter | 15 | DISTORTION GUITAR | 13 |
| **126** | Applause | 96 | PERCUSSIONS | 100 |
| **127** | Gunshot | 99 | ORCHESTRA HIT | 90 |

---

## 7. Recommended Keyboard Settings for Computer Playback

When connecting the PSS-790 as a sound module for playback from a PC sequencer or our custom player:
1. **Receive Channels**: Press `RECEIVE CH/CLOCK` on the PSS-790. Ensure channels 1 through 16 show `<E 1>` through `<E 16>` (Enabled).
2. **MIDI Clock**: Set to `<d c>` (Disabled) unless explicitly synchronizing auto-accompaniment styles with the computer.
3. **MIDI Cabling**:
   - PC USB-MIDI Interface `OUT` &rarr; Yamaha PSS-790 `MIDI IN`.
   - Leave Yamaha PSS-790 `MIDI OUT` disconnected during playback to prevent MIDI feedback loopback.
