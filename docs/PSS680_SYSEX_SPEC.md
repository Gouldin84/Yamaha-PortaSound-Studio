# Yamaha PortaSound PSS-680 (PSS-480 / PSS-780) — Complete Technical & SysEx Specification

> **Source Verification**: Extracted and verified directly from the official Yamaha PSS-680 Owner's Manual (`Manuals/PSS680E.pdf`, pages 1–52), MIDI Implementation Charts (Manual pp. 45–46), and Exclusive Data Format specifications (Manual pp. 41–44).

---

## 1. System Architecture & Hardware Overview

The **Yamaha PortaSound PSS-680** (and its companion FM models **PSS-480** and **PSS-780**) is powered by a 2-operator FM (Frequency Modulation) tone generator (YM3420 / OPLL-derivative family) paired with a PCM rhythm engine.

### Hardware Specifications
| Parameter | Specification | Manual Reference |
| :--- | :--- | :--- |
| **Keyboard** | 61 mini keys ($C_1$–$C_6$) | Page 40 |
| **Tone Generation** | 2-Operator FM Synthesizer (Modulator + Carrier) | Page 26–29, 41 |
| **Polyphony** | Up to 12 voices dynamically allocated (Mode 99) | Page 35, 46 |
| **Preset Voices** | 100 voices (#00–#99) | Page 40 |
| **Programmable Synth Memory** | 5 Digital Synthesizer Banks (Bank 1–5) | Page 26–29, 41 |
| **Preset Rhythm Styles** | 100 styles (#00–#99) | Page 40 |
| **Custom Drummer** | 32 PCM percussion instruments, 16 recorded tracks | Page 16–17, 44 |
| **Hand Percussion** | 8 physical pads with 4 selectable banks (32 instruments) | Page 15, 32–33 |
| **Song Memory** | Melody Memory (5 banks) + Chord Memory (5 banks) | Page 22–25, 42–43 |
| **Built-in Effects** | Vibrato, Duet, Sustain, Reverb (1–3), Stereo Chorus | Page 12–13 |
| **Controllers** | Pitch Bend Wheel ($\pm 12$ semitones), Transpose ($\pm 12$ semitones) | Page 14 |
| **MIDI Terminals** | MIDI IN, MIDI OUT, MIDI THRU (5-pin standard DIN) | Page 30 |

---

## 2. MIDI Implementation & Operating Modes

The PSS-680 features two fundamentally distinct MIDI operational modes selected via the front-panel **MIDI MODE [c]** switch:

### Mode 00: Keyboard Assign Mode (Default)
In Mode 00, the PSS-680 acts as an interactive arranger keyboard.
* **Melody / Manual Performance**: Transmitted on the designated transmit channel (default Channel 1, assignable 1–16).
* **Auto-Accompaniment Parts**: Transmitted on fixed channels:
  - **Chord**: Channel 11
  - **Orchestra 1, 2, 3**: Channels 12, 13, 14
  - **Bass**: Channel 15
  - **Rhythm (Drums)**: Channel 16
* **Note Input / Reception**:
  - Responds only to notes within the physical 5-octave keyboard range ($C_1$–$C_6$, MIDI 36–96).
  - Velocity data is **ignored** on reception (fixed volume, scaled by MELODY VOLUME slider).
  - Channel 16 note data triggers drum instruments according to the Percussion Map.
* **Program Changes**:
  - Channels 1–15: Values 0–99 select Preset Voices #00–#99; Values 100–104 select Digital Synthesizer Banks 1–5; Values 105–127 wrap to Voices #00–#22.
  - Channel 16: Values 0–99 select Rhythm Styles #00–#99; Values 100–127 wrap to Styles #00–#27.
* **Supported Control Changes**:
  - `CC 1`: Vibrato (Modulation) ON/OFF
  - `CC 64`: Sustain ON/OFF
  - `CC 65`: Portamento ON/OFF
* **System Real-Time**: Accepts MIDI Clock, Start, and Stop (can synchronize auto-accompaniment with external sequencers).

### Mode 99: Sound Source / Multi-Timbral Mode
In Mode 99, the PSS-680 acts as a standalone 16-channel multi-timbral MIDI sound module.
* **Polyphony & Dynamic Voice Allocation**:
  - 12-voice polyphony dynamically shared across Channels 1–15.
  - If more than 12 notes are requested simultaneously, the oldest sounding voice is stolen, and `ch FUL` appears on the LED display.
* **Extended Playable Range**:
  - Accepts MIDI Note numbers **24 to 108** ($C_0$ to $C_7$, a full 7-octave span).
* **Velocity Sensitivity**:
  - Note-On velocity ($9nH, v = 1\dots 127$) is **fully recognized** on all channels, providing dynamic volume control.
* **Local Off**:
  - Playing the physical keyboard sends MIDI Note-On/Off out of MIDI OUT, but does not trigger the internal sound engine directly.
* **Channel 16 (Rhythm Engine)**:
  - Channel 16 is dedicated to PCM percussion.
  - Note-On velocity dynamically modulates drum hit volume.
  - Program change messages on Channel 16 are ignored.
* **Pitch Bend Range (`MIDI IN` vs. Physical Wheel)**:
  - While the physical Pitch Bend Wheel and Melody Memory support up to $\pm 12$ semitones (Manual p. 14, 41), external `MIDI IN` Pitch Bend messages in Mode 99 respond to fixed $\pm 2$ semitones (1:1 with General MIDI).
* **System Real-Time**: Clock, Start, and Stop messages are **ignored** in Mode 99.

---

## 3. Percussion Mapping & Custom Drummer

The PSS-680 contains 32 distinct PCM percussion instruments accessible via the 8 Hand Percussion pads across 4 banks (Sets A, B, C, D) and via MIDI Channel 16.

### Complete MIDI Note to Percussion Mapping (Channel 16)
Verified from Manual pages 32–33:

| MIDI Note # | Hex | Pad / Sound Name | Pad Bank | Transmitted As | Accepted As |
| :---: | :---: | :--- | :---: | :---: | :---: |
| **36** | `0x24` | *(Unassigned)* | — | — | — |
| **37** | `0x25` | *(Unassigned)* | — | — | — |
| **38** | `0x26` | Snare Hi (Alt) | — | — | Snare Hi |
| **39** | `0x27` | *(Unassigned)* | — | — | — |
| **40** | `0x28` | Synth Tom Low (Alt) | — | — | Synth Tom Low |
| **41** | `0x29` | **Synth Tom Low** | B3 | Synth Tom Low | Synth Tom Low |
| **42** | `0x2A` | **Synth Tom Mid** | B4 | Synth Tom Mid | Synth Tom Mid |
| **43** | `0x2B` | **Synth Tom Hi** | B5 | Synth Tom Hi | Synth Tom Hi |
| **44** | `0x2C` | Bass Drum (Alt) | — | — | Bass Drum |
| **45** | `0x2D` | **Bass Drum** | B1 | Bass Drum | Bass Drum |
| **46** | `0x2E` | Rim Shot (Alt) | — | — | Rim Shot |
| **47** | `0x2F` | Low Tom (Alt) | — | — | Low Tom |
| **48** | `0x30` | **Low Tom** | A3 | Low Tom | Low Tom |
| **49** | `0x31` | **Snare Hi** | B2 | Snare Hi | Snare Hi |
| **50** | `0x32` | **Mid Tom** | A4 | Mid Tom | Mid Tom |
| **51** | `0x33` | **Rim Shot** | A1 | Rim Shot | Rim Shot |
| **52** | `0x34` | **Snare Low** | A2 | Snare Low | Snare Low |
| **53** | `0x35` | **Hi Tom** | A5 | Hi Tom | Hi Tom |
| **54** | `0x36` | **Hand Clap** | D7 | Hand Clap | Hand Clap |
| **55** | `0x37` | **Cowbell** | D5 | Cowbell | Cowbell |
| **56** | `0x38` | **Cabasa** | D6 | Cabasa | Cabasa |
| **57** | `0x39` | **Hi-Hat Closed** | B6 | Hi-Hat Closed | Hi-Hat Closed |
| **58** | `0x3A` | **Brush Hit** | A7 | Brush Hit | Brush Hit |
| **59** | `0x3B` | **Hi-Hat Open** | B7 | Hi-Hat Open | Hi-Hat Open |
| **60** | `0x3C` | **Crash Cymbal** | B8 | Crash Cymbal | Crash Cymbal |
| **61** | `0x3D` | **Splash Cymbal** | C8 | Splash Cymbal | Splash Cymbal |
| **62** | `0x3E` | Ride Cymbal (Alt) | — | — | Ride Cymbal |
| **63** | `0x3F` | **Ride Cymbal** | A8 | Ride Cymbal | Ride Cymbal |
| **64** | `0x40` | **Conga Low** | D1 | Conga Low | Conga Low |
| **65** | `0x41` | **Conga Hi** | D2 | Conga Hi | Conga Hi |
| **66** | `0x42` | Conga Hi (Alt) | — | — | Conga Hi |
| **67** | `0x43` | **Bongo Low** | C1 | Bongo Low | Bongo Low |
| **68** | `0x44` | **Bongo Hi** | C2 | Bongo Hi | Bongo Hi |
| **69** | `0x45` | **Timbale Low** | D3 | Timbale Low | Timbale Low |
| **70** | `0x46` | **Timbale Hi** | D4 | Timbale Hi | Timbale Hi |
| **71** | `0x47` | **Tambourine** | C7 | Tambourine | Tambourine |
| **72** | `0x48` | *(Unassigned)* | — | — | — |
| **73** | `0x49` | Cowbell (Alt) | — | — | Cowbell |
| **74** | `0x4A` | **Agogo Low** | C3 | Agogo Low | Agogo Low |
| **75** | `0x4B` | **Agogo Hi** | C4 | Agogo Hi | Agogo Hi |
| **76** | `0x4C` | **Cuica Low** | C5 | Cuica Low | Cuica Low |
| **77** | `0x4D` | **Cuica Hi** | C6 | Cuica Hi | Cuica Hi |
| **78** | `0x4E` | **Whistle** | D8 | Whistle | Whistle |
| **79** | `0x4F` | **Brush** | A6 | Brush | Brush |
| **80–84** | `0x50`–`0x54` | *(Unassigned)* | — | — | — |

---

## 4. System Exclusive (SysEx) Framing Protocol

Yamaha PSS-680 System Exclusive messages use the standard Yamaha manufacturer ID (`0x43`), sub-status class code `0x76` (PortaSound series), and a 1-byte format identifier.

### Message Structure
```
F0  43  76  [Format]  [Data Nibbles...]  [Checksum]  F7
│   │   │      │              │              │       └─ End of Exclusive (EOX)
│   │   │      │              │              └───────── 7-bit Two's Complement Checksum
│   │   │      │              └──────────────────────── Nibblized payload (High nibble first)
│   │   │      └─────────────────────────────────────── Format ID: 00H, 01H, 02H, 03H
│   │   └────────────────────────────────────────────── Instrument Class ID (76H = PSS Series)
│   └────────────────────────────────────────────────── Manufacturer ID (43H = Yamaha)
└────────────────────────────────────────────────────── SysEx Status Byte
```

### Supported Format Identifiers
| Format ID | Name | Decoded Bytes | Transmitted Nibbles | Total SysEx Size | Description |
| :---: | :--- | :---: | :---: | :---: | :--- |
| **`00H`** | FM 2-Operator Voice Data | 33 bytes | 66 bytes | 72 bytes | Single Digital Synth Bank Voice Dump |
| **`01H`** | Melody Memory Data | 5026 bytes | 10,052 bytes | 10,058 bytes | All 5 Melody Memory Banks + Track Config |
| **`02H`** | Chord Memory Data | 202 bytes | 404 bytes | 410 bytes | Chord sequence data + Orchestration |
| **`03H`** | Rhythm Pattern Data | 358 bytes | 716 bytes | 722 bytes | Custom Drummer pattern, timing, accents |

### Nibble Encoding Convention
Data bytes are split into two 4-bit nibbles and transmitted in the lower 4 bits of successive MIDI bytes (`0000dddd`).
* **High Nibble (Most Significant 4 bits) is transmitted FIRST**:
  $$\text{Nibble}_1 = (\text{byte} \gg 4) \ \& \ \text{0x0F}$$
* **Low Nibble (Least Significant 4 bits) is transmitted SECOND**:
  $$\text{Nibble}_2 = \text{byte} \ \& \ \text{0x0F}$$
* **Reconstruction**:
  $$\text{DecodedByte} = (\text{Nibble}_1 \ll 4) \mid \text{Nibble}_2$$

### Checksum Formula
The checksum is calculated on the **decoded data bytes** (before nibblization) and sent as a single 7-bit byte (`0ccccccc`) immediately before `F7H`:
$$\text{Sum} = \left( \sum_{i=0}^{N-1} \text{DecodedByte}[i] \right) \ \& \ \text{0x7F}$$
$$\text{Checksum} = (128 - \text{Sum}) \ \& \ \text{0x7F}$$

### Error Handling & Memory Protection
* If a checksum error occurs or transmission is interrupted, the PSS-680 displays `Err`.
* The instrument utilizes **modular error rollback**: only the memory bank or format section where the error occurred is reset; all other valid received banks remain preserved in RAM.

---

## 5. Format 00H: FM 2-Operator Voice Data (Digital Synthesizer)

Format `00H` allows direct reading and writing of custom 2-operator FM patches into any of the 5 Digital Synthesizer banks. The patch exposes both front-panel parameters and deep internal synthesis parameters not reachable from the physical keyboard buttons.

* **Operator Architecture**: Modulator (M = Operator 1) and Carrier (C = Operator 2).
* **Decoded Size**: 33 bytes.
* **Transmitted Size**: 66 nibbles.

### Bit Allocation Table (Format 00H)
| Byte # | Operator | Bit 7 | Bit 6 | Bit 5 | Bit 4 | Bit 3 | Bit 2 | Bit 1 | Bit 0 | Parameter & Range |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **0** | — | \multicolumn{8}{c|}{`BANK NUMBER`} | Target Bank (`0`–`4` for Bank #1–#5; $>4 \to 0$) |
| **1** | **M** | 0 | \multicolumn{3}{c|}{`DT1`} | \multicolumn{4}{c|}{`MUL`} | Detune 1 (sign + 3-bit abs) & Multiplier (0–15) |
| **2** | **C** | 0 | \multicolumn{3}{c|}{`DT1`} | \multicolumn{4}{c|}{`MUL`} | Detune 1 (sign + 3-bit abs) & Multiplier (0–15) |
| **3** | **M** | *X* | \multicolumn{7}{c|}{`TL`} | Total Level (Modulation Level, 0–99 inverted) |
| **4** | **C** | *X* | \multicolumn{7}{c|}{`TL`} | Total Level (Carrier Volume, 0–99 inverted) |
| **5** | **M** | \multicolumn{4}{c|}{`LKS(HI)`} | \multicolumn{4}{c|}{`LKS(LO)`} | Level Key Scaling High (0–15) & Low (0–15) |
| **6** | **C** | \multicolumn{4}{c|}{`LKS(HI)`} | \multicolumn{4}{c|}{`LKS(LO)`} | Level Key Scaling High (0–15) & Low (0–15) |
| **7** | **M** | \multicolumn{2}{c|}{`RKS`} | \multicolumn{6}{c|}{`AR`} | Rate Key Scaling (0–3) & Attack Rate (0–63) |
| **8** | **C** | \multicolumn{2}{c|}{`RKS`} | \multicolumn{6}{c|}{`AR`} | Rate Key Scaling (0–3) & Attack Rate (0–63) |
| **9** | **M** | `AM EN` | `DT2` | \multicolumn{6}{c|}{`D1R`} | AM Enable (0/1), Detune 2 (+600c), Decay 1 Rate (0–63) |
| **10** | **C** | `AM EN` | `DT2` | \multicolumn{6}{c|}{`D1R`} | AM Enable (0/1), Detune 2 (+600c), Decay 1 Rate (0–63) |
| **11** | **M** | \multicolumn{3}{c|}{`SIN TBL`} | \multicolumn{5}{c|}{`D2R`} | Waveform (0–3) & Decay 2 Rate (0–31) |
| **12** | **C** | \multicolumn{3}{c|}{`SIN TBL`} | \multicolumn{5}{c|}{`D2R`} | Waveform (0–3) & Decay 2 Rate (0–31) |
| **13** | **M** | \multicolumn{4}{c|}{`D1L`} | \multicolumn{4}{c|}{`RR`} | Decay 1 Level (0–15) & Release Rate (0–15) |
| **14** | **C** | \multicolumn{4}{c|}{`D1L`} | \multicolumn{4}{c|}{`RR`} | Decay 1 Level (0–15) & Release Rate (0–15) |
| **15** | — | *X* | *X* | \multicolumn{3}{c|}{`FB`} | *X* | *X* | *X* | Operator 1 Feedback Level (0–7) |
| **16** | — | *X* | \multicolumn{3}{c|}{`PMS`} | *X* | *X* | \multicolumn{2}{c|}{`AMS`} | Pitch Mod Sens / Vibrato (0–7), AM Sens (0–3) |
| **17–19** | — | \multicolumn{8}{c|}{*Unused (X)*} | Reserved (Set to 0) |
| **20** | **M** | *X* | *X* | *X* | *X* | \multicolumn{4}{c|}{`SRR`} | Sustain Release Rate (0–15) |
| **21** | **C** | *X* | *X* | *X* | *X* | \multicolumn{4}{c|}{`SRR`} | Sustain Release Rate (0–15) |
| **22** | — | *X* | \multicolumn{7}{c|}{`VDT`} | Vibrato Delay Time (0–127) |
| **23** | — | \multicolumn{8}{c|}{*Unused (X)*} | Reserved (Set to 0) |
| **24** | — | `V` | `S` | *X* | *X* | *X* | *X* | *X* | *X* | Vibrato Enable (1/0), Sustain Enable (1/0) |
| **25–32** | — | \multicolumn{8}{c|}{*Unused (X)*} | Reserved (Set to 0) |

*(Note: In the table, M = Modulator [Operator 1], C = Carrier [Operator 2], X = Don't Care / Unused).*

### Detailed Parameter Definitions

#### Frequency & Detuning
* **`MUL` (Frequency Multiple, Bits 3..0, Range 0–15)**:
  - `0`: 0.5x (one octave below fundamental)
  - `1`: 1.0x (fundamental frequency)
  - `2`: 2.0x (1 octave above)
  - `3`: 3.0x (1 octave + fifth above)
  - `4`: 4.0x (2 octaves above)
  - `5`..`15`: Harmonic multiples up to 15x.
* **`DT1` (Fine Detune, Bits 6..4)**:
  - Bit 6: Sign bit ($0 = \text{positive/sharp}$, $1 = \text{negative/flat}$).
  - Bits 5..4: 2-bit fine detune magnitude ($0\dots 3$).
* **`DT2` (Coarse Detune, Bit 6 of Bytes 9/10)**:
  - `0`: Normal pitch.
  - `1`: Fixed $+600 \text{ cents}$ coarse detune (+half-octave / tritone).

#### Output Levels & Panel Inversion
* **`TL` (Total Level / Modulation Level, Bits 6..0, Range 0–99)**:
  - Hardware attenuator: Raw value `0` represents **maximum volume / maximum modulation** ($99$ on front panel); raw value `99` (`0x63`) represents **silence** ($00$ on front panel).
  - Mapping formula:
    $$\text{PanelLevel} = \max(0, 99 - \text{SysExTL})$$
    $$\text{SysExTL} = 99 - \text{PanelLevel}$$

#### Waveform Selection (`SIN TBL`)
* **Waveform Table (Bits 7..5 of Bytes 11 & 12)**:
  - `000` (`0`): **Standard Sine Wave** ($\sin \theta$)
  - `001` (`1`): **Squared Sine Wave** ($\sin^2 \theta$)
  - `010` (`2`): **Sine Half-Wave** (positive half-cycle only, rectified)
  - `011` (`3`): **Squared Sine Half-Wave** (rectified squared)

#### Envelope Generator
* **`AR` (Attack Rate, Bits 5..0 of Bytes 7/8, Range 0–63)**: Attack speed ($0 = \text{slowest/infinite}$, $63 = \text{instant}$).
* **`D1R` (Decay 1 Rate, Bits 5..0 of Bytes 9/10, Range 0–63)**: Initial decay speed from peak to Decay 1 Level.
* **`D1L` (Decay 1 Level, Bits 7..4 of Bytes 13/14, Range 0–15)**: Breakpoint level between Decay 1 and Decay 2.
* **`D2R` (Decay 2 Rate, Bits 4..0 of Bytes 11/12, Range 0–31)**: Secondary decay slope toward sustain.
* **`RR` (Release Rate, Bits 3..0 of Bytes 13/14, Range 0–15)**: Release speed after Note-Off when Sustain is OFF.
* **`SRR` (Sustain Release Rate, Bits 3..0 of Bytes 20/21, Range 0–15)**: Release speed after Note-Off when Sustain is ON.

#### Scaling Curves
* **`LKS(LO)` & `LKS(HI)` (Level Key Scaling, Range 0–15)**:
  - Scales operator amplitude across keyboard keys: `LKS(LO)` scales notes below $C_3$; `LKS(HI)` scales notes above $C_3$.
  - Curves $1\dots 15$ provide linear to exponential attenuation.
* **`RKS` (Rate Key Scaling, Bits 7..6 of Bytes 7/8, Range 0–3)**:
  - Shortens envelope attack and decay times for higher musical pitches (emulating acoustic instruments).

#### Feedback & Modulation Sensitivities
* **`FB` (Feedback Level, Bits 5..3 of Byte 15, Range 0–7)**:
  - Modulator self-feedback loop ($0 = \text{none}$, $7 = \text{heavy saw/noise}$).
* **`PMS` (Pitch Modulation Sensitivity, Bits 6..4 of Byte 16, Range 0–7)**: Vibrato LFO depth.
* **`AMS` (Amplitude Modulation Sensitivity, Bits 1..0 of Byte 16, Range 0–3)**: Tremolo LFO depth.
* **`VDT` (Vibrato Delay Time, Bits 6..0 of Byte 22, Range 0–127)**: Delay onset before vibrato starts.
* **`V` (Vibrato Switch, Bit 7 of Byte 24)**: `1` = Vibrato Enabled, `0` = Disabled.
* **`S` (Sustain Switch, Bit 6 of Byte 24)**: `1` = Sustain Enabled, `0` = Disabled.

---

## 6. Format 01H: Melody Memory Data (5 Banks)

Format `01H` dumps all 5 banks of the PSS-680 Melody Sequencer.
* **Decoded Size**: 5026 bytes.
* **Transmitted Size**: 10,052 nibbles.

### Memory Layout
* **Byte 0**: Don't care (`0x00`).
* **Bytes 1–1000**: Melody Bank 1 Event Sequence.
* **Bytes 1001–2000**: Melody Bank 2 Event Sequence.
* **Bytes 2001–3000**: Melody Bank 3 Event Sequence.
* **Bytes 3001–4000**: Melody Bank 4 Event Sequence.
* **Bytes 4001–5000**: Melody Bank 5 Event Sequence.
* **Bytes 5001–5005**: Grouping Data (`GD`, Banks 1–5):
  - Bit layout: `0 0 0 e d c b a`
  - Bits `a` through `e` specify bank concatenation ($1 = \text{chained playback}$, $0 = \text{separated}$).
* **Bytes 5006–5010**: Pitch Bend Range (`PBR`, Banks 1–5):
  - 2's complement value ($1\dots 12$ semitones).
* **Bytes 5011–5015**: Melody Volume (`MVOL`, Banks 1–5):
  - Range `1` to `9`.
* **Bytes 5016–5020**: MIDI Transmit Channel (`MTC`, Banks 1–5):
  - Range `0` to `15` (Channels 1–16).
* **Bytes 5021–5025**: Portamento Speed (`PS`, Banks 1–5):
  - Range `1` to `5`.

### Melody Event Command Set
| Event Type | Byte 1 | Byte 2 | Description |
| :--- | :---: | :---: | :--- |
| **Duration** | `10DDDDDD` | — | Step time: $\text{Decimal} = 24 \times \text{Beat} - 1$ ($0 = 1/24 \text{ beat}$, $63 = 8/3 \text{ beat}$) |
| **Key On** | `01NNNNNN` | — | Key Down: $N = 0\dots 60$ ($0 = C_1$, $60 = C_6$) |
| **Key Off** | `00NNNNNN` | — | Key Up: $N = 0\dots 60$ ($0 = C_1$, $60 = C_6$) |
| **Voice Change** | `11111110` | `0TTTTTTT` | Program Change: $T = 0\dots 99$ (Presets), $100\dots 104$ (Synth 1–5) |
| **Pitch Bend** | `110BBBBB` | — | 5-bit offset from `40H` center |
| **Effect Switch** | `1110PRSV` | — | P = Portamento, R = Reverb, S = Sustain, V = Vibrato ($1 = \text{ON}, 0 = \text{OFF}$) |
| **End of Song** | `11111111` | — | `0xFF` terminates the bank sequence |

---

## 7. Format 02H: Chord Memory Data

Format `02H` dumps chord accompaniment progressions and style arrangement automation.
* **Decoded Size**: 202 bytes.
* **Transmitted Size**: 404 nibbles.

### Memory Layout
* **Byte 0**: Bank Number (`0`–`4` for Bank 1–5).
* **Bytes 1–5 (Initial Configuration)**:
  - Byte 1: Initial Style Number (`0SSSSSSS`, 0–99).
  - Byte 2: Reserved.
  - Byte 3: Initial Orchestration (`000DHAOT`: D = Custom Drummer, H = Chord, A = Bass, O = Orchestra, T = Rhythm).
  - Bytes 4–5: Reserved (`0x00`, `0x0F`).
* **Bytes 6–200**: Chord Sequence Events (Timing Header `MMMMBBB` where M = Measure offset, B = Beat offset):
  - **Chord Name** (2 bytes): `0MMMMBBB` followed by `RRRRCCCC`
    - Root note `RRRR`: $0 = C, 1 = C^\sharp, 2 = D, \dots, 11 = B$.
    - Chord type `CCCC`:
      - `0000`: Major (M)
      - `0001`: Minor (m)
      - `0010`: 7th
      - `0011`: Minor 7th (m7)
      - `0100`: Major 7th (M7)
      - `0101`: Minor Major 7th (mM7)
      - `0110`: Minor 7th flat 5 ($m7^{\flat 5}$)
      - `0111`: 7th suspended 4 (7sus4)
      - `1000`: Augmented (aug)
      - `1001`: Diminished (dim)
      - `1010`: 6th
      - `1011`: Minor 6th (m6)
      - `1100`: 7th flat 5 ($7^{\flat 5}$)
  - **Rhythm Controls**: `1MMMMBBB` followed by `110ZYXWV` (V = Fill 1, W = Fill 2, X = Fill 3, Y = Ending, Z = Stop).
  - **Melody Memory Switches**: `1MMMMBBB` followed by `100Abbbb` (Link melody bank playback).
  - **Orchestration Switches**: `1MMMMBBB` followed by `111DHAOT`.
  - **Style Number Change**: `1MMMM000` followed by `0SSSSSSS`.
* **Byte 201**: Tempo (`TTTTTTTT`, range 10–220 BPM).

---

## 8. Format 03H: Rhythm Pattern Data (Custom Drummer)

Format `03H` dumps custom drum patterns created with the Custom Drummer feature.
* **Decoded Size**: 358 bytes.
* **Transmitted Size**: 716 nibbles.

### Memory Layout
* **Byte 0**: Bank # (Ignored).
* **Byte 1**: Pattern Config:
  - Bit 3 (`Q`): Quantize grid ($0 = 1/4 \text{ beat}, 1 = 1/3 \text{ beat}$).
  - Bits 2..0 (`BEAT`): Pattern length in beats ($000 = 1 \text{ beat} \dots 111 = 8 \text{ beats}$).
* **Bytes 2–33**: Instrument Selection for Tracks 1–32:
  - Bit 7 (`R`): Roll Flag ($1 = \text{Roll Enabled}, 0 = \text{Normal}$).
  - Bit 6 (`A`): Accent 1 Flag ($1 = \text{Accent 1}, 0 = \text{Normal}$).
  - Bits 5..0 (`IIIIII`): Instrument ID ($1\dots 32$, corresponding to the 32 percussion instruments).
* **Bytes 34–65**: Quantize Resolution per Beat for Tracks 1–32:
  - Bits 7..0 (`ABCDEFGH`): Quantize for beats 1 through 8 ($0 = 1/8, 1 = 1/16$).
* **Bytes 66–321**: On Timing Step Grid (32 tracks $\times$ 8 beats = 256 bytes):
  - Bits 7..0 (`abcdefgh`): Trigger active on subdivisions 1 to 8 of that beat ($1 = \text{Trigger}, 0 = \text{Rest}$).
* **Bytes 322–325**: Volume Levels (5 bits, $0 = \text{Max Volume}, 31 = \text{Min Volume}$):
  - Byte 322: Normal Volume
  - Byte 323: Accent 1 Volume
  - Byte 324: Normal Roll Volume
  - Byte 325: Accent 1 Roll Volume
* **Bytes 326–357**: Instant Accent 2 Flags for Tracks 1–32:
  - Bit 0 (`ACC2`): Instant Accent Flag (+3 points velocity prior to Accent 1).

---

## 9. Cross-Model Comparison: PSS-680 vs PSS-480 vs PSS-780 vs PSS-790

| Feature | PSS-480 | PSS-680 | PSS-780 | PSS-790 |
| :--- | :--- | :--- | :--- | :--- |
| **Synthesis Chip** | 2-Op FM (YM3420) | 2-Op FM (YM3420) | 2-Op FM (YM3420) | AWM (PCM Sample ROM) |
| **Keyboard** | 49 mini keys ($C_2$–$C_6$) | 61 mini keys ($C_1$–$C_6$) | 61 mini keys ($C_1$–$C_6$) | 61 mini keys ($C_1$–$C_6$) |
| **Pitch Bend Wheel** | No | Yes ($\pm 12$ semitones) | Yes ($\pm 12$ semitones) | Yes ($\pm 12$ semitones) |
| **Hand Drum Pads** | No | 8 pads (4 sets = 32 sounds) | 8 pads (4 sets = 32 sounds) | 8 pads (sample-based) |
| **Custom Drummer** | No | Yes (16 tracks) | Yes (16 tracks) | Style Variation |
| **Digital Synth Banks** | 5 Banks | 5 Banks | 5 Banks | None (Fixed ROM Presets) |
| **SysEx Format 00H** | **Identical** | **Identical** | **Identical** | Incompatible (AWM Vector) |
| **Multi-Timbral Mode** | Mode 99 (12-voice) | Mode 99 (12-voice) | Mode 99 (12-voice) | 16-channel (28-voice) |
| **Polyphony** | 12 voices | 12 voices | 12 voices | 28 voices |
| **Dynamic Velocity** | In Mode 99 (Rx only) | In Mode 99 (Rx only) | In Mode 99 (Rx only) | Supported (Rx + Tx) |
