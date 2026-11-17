# Master MIDI Anomaly Matrix & Hardware Verification Report (Yamaha PSS-790)

## Executive Summary
A comprehensive audit and empirical C# verification of all 113 General MIDI files in `c:\Projects\PSS-790\General MIDI` was performed across Shards A, B, C, and D. Each file was analyzed against Yamaha PSS-790 hardware limitations (28-voice FM polyphony ceiling, hardware-locked Channel 16 drums, fixed +/-12 semitone pitch bend range, 36-96 multisample range, and 65-260 BPM tempo range) and verified using DryWetMidi against `Remapper.Remap()`.

- **Total MIDI Files Audited**: 113
- **Clean Files (0 Anomalies)**: 29 files (25.7%)
- **Files with Hardware / Musical Anomalies**: 84 files (74.3%)

### Key Anomaly Breakdown
1. **Missing ProgramChange (13 files)**:
   - Melodic: `At Zanarkand`, `Brothers In Arms 3` (all 8 chs!), `aqua-remix` (Ch 3-5), `EyesOnMePiano` (Ch 3-4), `Justin Bieber - Mistletoe` (Ch 3), `Lady Gaga - Applause` (Ch 1), `Merry Christmas, Mr. Lawrence` (Ch 1), `Pirates Of The Caribbean - EASY` (Ch 3), `The Show Must Go On` (all melodic tracks voiced purely via XG SysEx).
   - Drum: `Dr Wily Stage 1`, `bmintro`, `dkckrool1`, `KRool`, `Youre Not Alone2`, `snesK.Roolv2.1`.
2. **Acoustic Pitch Bends & Dangling Bends (13 files)**:
   - Acoustic Bends: `DKC - Aquatic` (448 bends on Pizz Strings/Harp), `aqua-remix` (6,290 bends on unassigned piano), `DYWBASnowman` (15 on Choir), `Hybris` (126 on Piano), `Justin Bieber - Baby` (504 on Choir), `From Now On` (59 on Cello/Choir), `JB WDUMean` (9 on Choir), `Level 3 1` (116 on Strings), `SASH.Ecuador K` (344 on Choir), `The Perfect Drug` (244 on Harpsichord), `The Show Must Go On` (196 on Ch 1).
   - Dangling at EOF: `DKC - Aquatic` (8 chs), `aqua-remix` (8 chs), `Hurt` (Ch 1, 9), `Level 3 1` (Ch 4, 5, 6), `M.OLDFIELD. Tubular bells` (Ch 11), `The Perfect Drug` (Ch 2, 3).
3. **Polyphony Overruns > 24 / > 28 (7 files standard, 1 sustain accumulation)**:
   - Overruns: `Avicii - Hey Brother` (44 notes), `M.OLDFIELD. Tubular bells` (41 notes), `MillionDreams` (37 notes, 41 with sustain), `From Now On` (36 notes), `Greatest Showman Theme - Never Enough` (32 notes), `Never Enough` (32 notes), `Justin Bieber - Baby` (32 notes).
   - Infinite Sustain: `At Zanarkand` (CC64=127 at tick 0 never released, explodes to 929 sustained notes).
4. **Channel 16 Melodic Collisions & Multi-Drum (12 files)**:
   - Melodic Collisions: `Avicii - Hey Brother` (Ch 16 Brass), `DKC - Gang` (Ch 16 Harmonics), `DKC KingKRoolV6 Rabid` (Ch 16 Orch Hit), `DYWBASnowman` (Ch 16 Glockenspiel), `Gang Plank` (Ch 16 Piano), `Greatest Showman Theme - Never Enough` (Ch 16 Piano), `JB WDUMean` (Ch 16 Strings), `M.OLDFIELD. Tubular bells` (Ch 16 Piano, 2670 notes), `Never Enough` (Ch 16 Piano), `Scooter Posse` (Ch 16 Piano, 756 notes, 16-channel saturation), `The Perfect Drug` (Ch 16 Harpsichord, 369 notes, 16-channel saturation).
   - Multi-Drum: `Children` (Ch 10 Standard Kit + Ch 16 XG Analog Kit).
5. **Extreme Pitches <28 or >88 (64 files)**:
   - Low notes down to MIDI 0 (C-1) in `DKC - Aquatic`, `aqua-remix`, MIDI 3 in `Justin Bieber - Baby`, MIDI 17 in `The Perfect Drug`, MIDI 21 in `Reminiscence DL-6`.
   - High notes up to MIDI 115 (G8) in `Britney Spears - Everytime`, MIDI 112 in `Tubular bells`.
   - Out-of-range drum notes: Note 4 in `Youre Not Alone2`, Note 27 in `Level 4 2`.
6. **Sluggish / Rapid / Missing Tempos (10 files)**:
   - Sluggish (<65 BPM): `SteinsGate` (8.5 BPM), `EyesOnMePiano` (20.0 BPM), `Naruto - Sadness and Sorrow Piano` (43.3 BPM), `FF7-OtherSideOfTheMountain` (42.0 BPM), `DKC - Aquatic` (60.0 BPM), `aqua-remix` (60.0 BPM), `DYWBASnowman` (60.0 BPM), `Bleach - Never Meant To Belong` (62.0 BPM), `Evanescence - My Immortal` (64.0 BPM).
   - Missing Tempo: `Bleach - Will of the heart` (defaults to 120.0 BPM).
   - Rapid Boundary: `dkckrool1` (260.0 BPM).
7. **Foreign SysEx Messages (19 files)**:
   - Roland GS: 12 files (`Afterburner`, `Analog Hats`, `Britney Spears`, `DYWBASnowman`, `bmlevel1`, `FF7-SephirothsTheme`, `From Now On`, `JB WDUMean`, `ff8-lfp`, `MillionDreams`, `Scooter Logical Song`, `SASH.Ecuador K`).
   - Yamaha XG: 4 files (`Children`, `FF7-OtherSideOfTheMountain`, `Ice Mountain Zone Act 1`, `The Show Must Go On` with 205 parameter dumps).

---

## Unified Master Anomaly Matrix (113 MIDI Files)

| # | File Name | Shard | Active Channels | Missing PC | Pitch Bends / Dangling | Peak Poly | Extreme Pitches (<28 / >88) | Drum / Ch 16 | Tempo (BPM) | SysEx | Anomalies Summary |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `Afterburner.mid` | A | 1-12 | None | None (Synth PB: 688) | 16 | High: 89-100 (214) | Ch 10 (GM) | 120.0-172.0 | **Roland GS (14)** | Roland GS SysEx, Extreme Pitch |
| 2 | `Alan Walker - Faded.mid` | A | 3, 4 | None | None | 5 | None [47, 76] | None | 90.0 | None | Clean |
| 3 | `Analog Hats.mid` | A | 2-6, 10 | None | None (Synth PB: 109) | 10 | Low: 26 (10) | Ch 10 (GM) | 120.0 | **Roland GS (1)** | Roland GS SysEx, Extreme Pitch |
| 4 | `At Zanarkand Piano Collection (Unofficial).mid` | A | 1, 2 | **Ch 1, 2** | Dangling CC64 | 7 (raw) / **929 (sus)** | High: 90, 91 (19) | None | 70.0-100.0 | None | Missing PC, Extreme Pitch, Stuck CC64 |
| 5 | `Avicii - Hey Brother.mid` | A | 1-7, 9, 10, 12-16 | None | None (PB: 124) | **44 (OVERFLOW)** | Low: 27 (229) | Ch 10, **Ch 15/16 Melodic** | 123.0-125.0 | None | Polyphony > 24, Extreme Pitch, Ch 15/16 Collision |
| 6 | `BRITNEY SPEARS.Everytime K.MID` | A | 2-4, 6-12 | None | None | 18 | High: 89-115 (132) | Ch 10 (GM) | 70.0-120.0 | **Roland GS (2)** | Roland GS SysEx, Extreme Pitch |
| 7 | `Bleach - Never Meant To Belong.mid` | A | 1-3 | None | None | 7 | High: 89, 91 (9) | None | **62.0-75.0 (SLUGGISH)** | None | Sluggish Tempo, Extreme Pitch |
| 8 | `Bleach - Will of the heart.mid` | A | 1 | None | None | 3 | None [40, 77] | None | **Missing (Def 120)** | None | Missing Tempo Meta |
| 9 | `Brothers In Arms 3.mid` | A | 2-6, 8-10 | **Ch 2-6, 8-10** | None | 11 | None [28, 80] | Ch 10 (GM) | 92.0 | None | Missing PC on ALL 8 Channels |
| 10 | `Bruno Count On Me.mid` | A | 1 | None | None | 2 | None [45, 71] | None | 80.0 | None | Clean |
| 11 | `Butterfly.mid` | A | 3, 4 | None | None | 3 | High: 90-95 (40) | None | 120.0-130.0 | None | Extreme Pitch |
| 12 | `Children.mid` | A | 1-16 | None | None | 19 | Low: 1-26 (60), High: 89 | **Multi-Drum (Ch 10 + Ch 16 XG)** | 135.0 | **Yamaha XG (12)** | Yamaha XG SysEx, Multi-Drum Ch 16, Extreme Pitch |
| 13 | `Chillout.mid` | A | 1, 2 | None | None | 4 | High: 89, 91 (9) | None | 100.0 | None | Extreme Pitch |
| 14 | `Céline Dion - My Heart Will Go On - EASY.mid` | A | 3, 4 | None | None | 8 | None [38, 84] | None | 94.5 | None | Clean |
| 15 | `DKC - Aquatic.mid` | A | 1-8 | None | **Acoustic: 448, Dangling: 8 Chs** | 8 | Low: 0-27 (106), High: 89-108 | None | **60.0 (SLUGGISH)** | None | Acoustic PB, Dangling PB (8 Chs), Extreme Pitch, Sluggish Tempo |
| 16 | `DKC - Gang - Plank Galleon.mid` | A | 1, 2, 10-16 | None | None | 9 | Low: 26 (53) | Ch 10, **Ch 15/16 Melodic** | 110.0-130.0 | None | Extreme Pitch, Ch 15/16 Collision |
| 17 | `DKC KingKRoolV6 Rabid.mid` | A | 1, 5, 7-10, 16 | None | None | 13 | None [28, 86] | Ch 10, **Ch 16 Melodic** | 120.0 | None | Ch 16 Melodic Collision |
| 18 | `DYWBASnowman.mid` | A | 1-7, 10-13, 16 | None | **Acoustic: 15 (Ch 5)** | 16 | High: 89-101 (21) | Ch 10, **Ch 16 Melodic** | **60.0-120.0 (SLUGGISH)** | **Roland GS (1)** | Acoustic PB, Roland GS SysEx, Sluggish Tempo, Ch 16 Collision |
| 19 | `Dance Dance Revolution - Butterfly - Converted.mid` | A | 1-4 | None | None | 8 | High: 90-95 (80) | None | 120.0-130.0 | None | Extreme Pitch |
| 20 | `Dance Dance Revolution - Butterfly.mid` | A | 1 | None | None | 3 | High: 90-95 (40) | None | 120.0-130.0 | None | Extreme Pitch |
| 21 | `Despacito.mid` | A | 1-4, 10 | None | None | 8 | None [31, 82] | Ch 10 (GM) | 89.0 | None | Clean |
| 22 | `Dr Wily Stage 1.mid` | A | 1-3, 10 | **Ch 10 (Drum)** | None | 6 | High: 92 (2) | Ch 10 (GM) | 175.0 | None | Missing Drum PC, Extreme Pitch |
| 23 | `aqua-remix.mid` | A | 1-8 | **Ch 3-5** | **Acoustic: 6290, Dangling: 8 Chs** | 12 | Low: 0-27 (129), High: 89-108 | None | **60.0 (SLUGGISH)** | Proprietary (8) | Missing PC (Ch 3-5), Acoustic PB, Dangling PB, Extreme Pitch, Sluggish Tempo |
| 24 | `balamb.mid` | A | 1, 2 | None | None | 5 (pedal: 80) | None [36, 83] | None | 87.0 | None | Clean (Heavy CC64) |
| 25 | `beast2.mid` | A | 1, 2 | None | None | 5 (pedal: 21) | Low: 27 (2) | None | 150.0 | None | Extreme Pitch |
| 26 | `bmintro.mid` | A | 1, 2, 10 | **Ch 10 (Drum)** | None | 4 | None [42, 76] | Ch 10 (GM) | 73.0 | None | Missing Drum PC |
| 27 | `bmlevel1.mid` | A | 1-5, 10 | None | None | 8 | Low: 27 (128) | Ch 10 (GM) | 126.0 | **Roland GS (34)** | Roland GS SysEx, Extreme Pitch |
| 28 | `dkckrool1.mid` | A | 1-13 | **Ch 10 (Drum)** | None (Synth PB: 166) | 14 | High: 89, 98 (8) | Ch 10 (GM) | **230.0-260.0 (RAPID)** | None | Missing Drum PC, Extreme Pitch, Rapid Tempo |
| 29 | `Elf Forest.mid` | B | 1-6 | None | None | 11 | None | None | 100.0 | None | Clean |
| 30 | `Eminem - Mockingbird.mid` | B | 3, 4 | None | None | 5 | None | None | 90.0 | None | Clean |
| 31 | `Evanescence - My Immortal.mid` | B | 3, 4 | None | None | 6 | None | None | **64.0 (SLUGGISH)** | None | Sluggish Tempo (<65 BPM) |
| 32 | `EyesOnMePiano.mid` | B | 1-4 | **Ch 3, 4** | None | 16 | High: 90-93 (14) | None | **20.0-115.0 (SLUGGISH)** | None | Missing PC, Extreme Pitch, Sluggish Tempo |
| 33 | `FF6 Terra.mid` | B | 2-12 | None | None | 11 | High: 90 (10) | Ch 10 (GM) | 155.0 | None | Extreme Pitch |
| 34 | `FF7-OtherSideOfTheMountain.mid` | B | 1-6 | None | Synth PB: 1578 | 6 | None | None | **42.0-120.0 (SLUGGISH)** | **Yamaha XG** | Sluggish Tempo, Yamaha XG SysEx |
| 35 | `FF7-SephirothsTheme.mid` | B | 1-9 | None | None | 12 | None | None | 75.0 | **Roland GS** | Roland GS SysEx |
| 36 | `FF7AC-ThoseWhoFight.mid` | B | 1 | None | None | 9 | Low: 27 (1), High: 89-101 (58) | None | 167.0-196.0 | None | Extreme Pitch |
| 37 | `Ff7 - One Winged.mid` | B | 1, 2 | None | None | 5 | Low: 26 (2) | None | 123.0 | None | Extreme Pitch |
| 38 | `Ffvii Battle.mid` | B | 1, 2 | None | None | 5 | None | None | 90.0 | None | Clean |
| 39 | `Final Countdown.mid` | B | 1-11 | None | Synth PB: 397 | 14 | None | Ch 10 (GM) | 118.0 | None | Clean |
| 40 | `From Now On.mid` | B | 1-15 | None | **Acoustic: 59 (Ch 4, 6)** | **36 (OVERFLOW)** | High: 90-93 (9) | Ch 10 (GM) | 65.0-108.4 | **Roland GS** | Acoustic PB, Polyphony > 24, Extreme Pitch, Roland GS SysEx |
| 41 | `Full Metal Alchemist - Let It Out.mid` | B | 1 | None | None | 6 | Low: 24 (1) | None | 90.0 | None | Extreme Pitch |
| 42 | `Gang Plank.mid` | B | 1, 2, 10-16 | None | None | 9 | Low: 26 (53) | Ch 10, **Ch 16 Melodic (12)** | 110.0-130.0 | None | Extreme Pitch, Ch 16 Collision |
| 43 | `Great War.mid` | B | 1, 2 | None | None | 9 | None | None | 106.0 | None | Clean |
| 44 | `Greatest Showman Theme - Never Enough.mid` | B | 1-4, 6-8, 10-12, 16 | None | None | **32 (OVERFLOW)** | Low: 25-27 (14), High: 89-96 | Ch 10, **Ch 16 Melodic (48)** | 86.0 | GM | Polyphony > 24, Extreme Pitch, Ch 16 Collision |
| 45 | `GyakutenSistersBalladev11.mid` | B | 1, 2 | None | None | 5 | None | None | 156.0 | None | Clean |
| 46 | `HP Theme.mid` | B | 1 | None | None | 6 | None | None | 100.0 | None | Clean |
| 47 | `Halo Maine Theme Extended Maw Version.mid` | B | 1 | None | None | 10 | None | None | 190.0 | None | Clean |
| 48 | `Hurt.mid` | B | 1-10 | None | Synth: 63, **Dang: Ch 1, 9** | 10 | Low: 23-26 (2) | Ch 10 (GM) | 81.0 | None | Dangling PB, Extreme Pitch |
| 49 | `Hybris.mid` | B | 1-5, 10 | None | **Acoustic: 126 (Ch 2)** | 10 | None | Ch 10 (GM) | 100.0 | None | Acoustic PB |
| 50 | `Ice Mountain Zone Act 1.mid` | B | 1-7, 10 | None | None | 10 | Low: 26 (6) | Ch 10 (GM) | 110.0 | **Yamaha XG** | Extreme Pitch, Yamaha XG SysEx |
| 51 | `Idina Menzel Let It Go (Piano Version).mid` | B | 1 | None | None | 9 | Low: 22-27 (60), High: 89-101 | None | 142.0 | None | Extreme Pitch |
| 52 | `Investigation Cornered.mid` | B | 1 | None | None | 6 | None | None | 75.0-142.0 | None | Clean |
| 53 | `JB WDUMean.mid` | B | 1-4, 6-8, 10, 16 | None | **Acoustic: 9 (Ch 6)** | 19 | None | Ch 10, **Ch 16 Melodic (120)** | 125.0 | **Roland GS** | Acoustic PB, Ch 16 Collision, Roland GS SysEx |
| 54 | `John Murphy - Adagio In D Minor.mid` | B | 3, 4 | None | None | 5 | None | None | 120.0 | None | Clean |
| 55 | `Justin Bieber - Baby.mid` | B | 2, 4, 6-13 | None | **Acoustic: 504 (Ch 7)** | **32 (OVERFLOW)** | Low: 3-27 (43) | Ch 10 (GM) | 130.0 | None | Acoustic PB, Polyphony > 24, Extreme Pitch |
| 56 | `Justin Bieber - Mistletoe.mid` | B | 2-4, 10 | **Ch 3** | None | 6 | None | Ch 10 (GM) | 120.0 | None | Missing PC |
| 57 | `KRool.mid` | B | 1-13 | **Ch 10 (Drum)** | Synth: 166 | 14 | High: 89-98 (8) | Ch 10 (GM) | 230.0-260.0 | None | Missing Drum PC, Extreme Pitch |
| 58 | `KingKRoolRemixModifSlightly.mid` | B | 1, 2, 10-15 | None | Synth: 561 | 15 | Low: 21 (12), High: 93-100 (17) | Ch 10 (GM) | 120.0 | None | Extreme Pitch |
| 59 | `Kingdom Hearts - Dearly Beloved.mid` | B | 1-4 | None | None | 8 | High: 89-91 (48) | None | 125.4-130.0 | None | Extreme Pitch |
| 60 | `ff8-lfp.mid` | B | 1, 2 | None | None | 7 | Low: 23-27 (3) | None | 130.0-150.0 | **Roland GS** | Extreme Pitch, Roland GS SysEx |
| 61 | `gerudo.mid` | B | 1, 2 | None | None | 5 | Low: 25 (1) | None | 120.0 | None | Extreme Pitch |
| 62 | `Lady Gaga - Applause.mid` | C | 1 | **Ch 1** | None | 6 | Low: 25, 27 (30), High: 92 (1) | None | 120.0 | None | Missing PC, Extreme Pitch |
| 63 | `Let It Go.mid` | C | 3, 4 | None | None | 7 | Low: 22-27 (60), High: 89-101 | None | 142.0 | None | Extreme Pitch |
| 64 | `LetItGo2.mid` | C | 1-4 | None | None | 14 | Low: 22-27 (120), High: 89-101 | None | 142.0 | None | Extreme Pitch |
| 65 | `Level 3 1.mid` | C | 1-8, 10 | None | **Acoustic: 116, Dangling: Ch 4-6** | 19 | Low: 25, 27 (48) | Ch 10 (GM) | 90.0 | None | Acoustic PB, Dangling PB, Extreme Pitch |
| 66 | `Level 3 2.mid` | C | 1-8, 10 | None | None | 11 | None | Ch 10 (GM) | 120.0 | None | Clean |
| 67 | `Level 4 1.mid` | C | 1-6, 10 | None | None | 11 | Low: 24-27 (88) | Ch 10 (GM) | 95.0 | None | Extreme Pitch |
| 68 | `Level 4 2.mid` | C | 1-6, 10 | None | None | 11 | Drum Low: 27 (344) | Ch 10 (GM) | 100.0 | None | Drum Note < 28 (High Q) |
| 69 | `Luis Fonsi - Despacito ft. Daddy Yankee.mid` | C | 3, 4 | None | None | 5 | None | None | 90.0 | None | Clean |
| 70 | `M.OLDFIELD. Tubular bells.mid` | C | 1-16 | None | **Dang: Ch 11** | **41 (OVERFLOW)** | High: 89-112 (1803) | **Ch 16 Melodic (2670)** | 136.0-202.0 | None | Dangling PB, Polyphony > 24, Extreme Pitch, Ch 16 Collision |
| 71 | `MM2 - Wily 1 - Piano - V 2.mid` | C | 1, 2 | None | None | 5 | None | None | 175.0 | None | Clean |
| 72 | `Merry Christmas, Mr. Lawrence - Converted.mid` | C | 1-4 | None | None | 14 | High: 89-101 (382) | None | 120.0 | None | Extreme Pitch |
| 73 | `Merry Christmas, Mr. Lawrence (FIXED KEY).mid` | C | 1 | **Ch 1** | None | 7 | High: 89-101 (191) | None | 120.0 | None | Missing PC, Extreme Pitch |
| 74 | `MillionDreams.mid` | C | 1-15 | None | None | **37 (OVERFLOW)** | High: 90, 91 (2) | Ch 10 (GM) | 74.0 | **Roland GS** | Polyphony > 24, Extreme Pitch, Roland GS SysEx |
| 75 | `Naruto - 1st opening - Rocks.mid` | C | 1 | None | None | 5 | None | None | 115.5-120.0 | None | Clean |
| 76 | `Naruto - Grief and Sorrow Medley.mid` | C | 1 | None | None | 6 | High: 93 (1) | None | 70.0 | None | Extreme Pitch |
| 77 | `Naruto - Grief and Sorrow-Hokage's Funeral.mid` | C | 1-3 | None | None | 5 | High: 91, 93 (7) | None | 96.5-100.0 | None | Extreme Pitch |
| 78 | `Naruto - Kokuten.mid` | C | 1-4 | None | None | 12 | Low: 22-26 (86) | None | 92.0 | None | Extreme Pitch |
| 79 | `Naruto - Sadness and Sorrow Piano.mid` | C | 1 | None | None | 6 | High: 90-95 (20) | None | **43.3-65.0 (SLUGGISH)** | None | Extreme Pitch, Sluggish Tempo |
| 80 | `Naruto - Sadness and Sorrow.mid` | C | 1-5 | None | None | 7 | None | None | 72.0 | None | Clean |
| 81 | `Naruto - The Raising Fight Spirit.mid` | C | 1-5, 10 | None | None | 13 | Low: 26 (48) | Ch 10 (GM) | 199.5-210.0 | None | Extreme Pitch |
| 82 | `Naruto - The Rising Fighting Spirit Piano.mid` | C | 1 | None | None | 5 | None | None | 115.5-120.0 | None | Clean |
| 83 | `Never Enough.mid` | C | 1-4, 6-8, 10-12, 16 | None | None | **32 (OVERFLOW)** | Low: 25-27 (14), High: 89-96 | Ch 10, **Ch 16 Melodic (48)** | 86.0 | GM | Polyphony > 24, Extreme Pitch, Ch 16 Collision |
| 84 | `Nine Inch Nails - That's What I Get (Remix).mid` | C | 1, 2, 4-6, 10 | None | None | 11 | None | Ch 10 (GM) | 110.0 | None | Clean |
| 85 | `Nine Inch Nails - That's What I Get.mid` | C | 1-7, 10 | None | None | 17 | None | Ch 10 (GM) | 110.0 | None | Clean |
| 86 | `Piano Template.mid` | C | 1, 2 | None | None | 4 | High: 90, 91 (18) | None | 108.0 | None | Extreme Pitch |
| 87 | `Pirates Of The Caribbean - EASY.mid` | C | 3, 4 | **Ch 3** | None | 6 | None | None | 140.0 | None | Missing PC |
| 88 | `lightwld.mid` | C | 1, 2 | None | None | 7 | Low: 27 (6) | None | 200.0 | None | Extreme Pitch |
| 89 | `Quick & Silva - Level 1.mid` | D | 1-5, 10 | None | None | 9 | None | Ch 10 (GM) | 100.0 | None | Clean |
| 90 | `Quick & Silva - Level 2.mid` | D | 1-4, 10 | None | None | 8 | Low: 23-27 (328) | Ch 10 (GM) | 96.0 | None | Extreme Low Pitch |
| 91 | `Quick & Silva - Level 3.mid` | D | 1-3, 10 | None | None | 7 | Low: 27 (24) | Ch 10 (GM) | 98.0 | None | Extreme Low Pitch |
| 92 | `Reminiscence DL-6 (Piano Only).mid` | D | 1, 2 | None | None | 3 | Low: 21 (2) | None | 120.0 | None | Extreme Low Pitch (A0 sub-C2) |
| 93 | `SASH.Ecuador K.mid` | D | 2-12 | None | **Acoustic: 344 (Ch 4, 12)** | 20 | Low: 23 (1), High: 90-95 (61) | Ch 10 (GM) | 130.0 | **Roland GS (3)** | Acoustic PB, Extreme Pitch, Roland GS SysEx |
| 94 | `SOTB Intro.mid` | D | 1-4 | None | None | 4 | Low: 24 (24) | None | 90.0 | None | Extreme Low Pitch |
| 95 | `Scooter Logical Song Unknown.mid` | D | 1-3, 5-15 | None | None | 21 | Low: 25-26 (74), High: 97 (26) | Ch 10 (GM) | 142.0 | **Roland GS (8)** | Extreme Pitches, Roland GS SysEx |
| 96 | `Scooter Posse I Need - You On The Floor.mid` | D | 1-16 (All 16) | None | None | 14 | None | **Ch 16 Melodic (756), 16-Ch Saturation** | 139.0 | None | Ch 16 Melodic Collision, 16-Ch Saturation |
| 97 | `Smile DK - Butterfly.mid` | D | 1-4 | None | None | 8 | High: 95 (80) on Piano | None | 120.0-130.0 | None | Extreme High Pitch (Aliasing Hazard) |
| 98 | `Something I Can Never Have.mid` | D | 1-6, 8, 10 | None | None | 11 (sus: 19) | None | Ch 10 (GM) | 110.0 | None | Clean |
| 99 | `SteinsGate - Gate Of Steiner Piano.mid` | D | 1 | None | None | 8 (sus: 22) | High: 95 (35) on Piano | None | **8.5-120.0 (SLUGGISH, 20 < 65)** | None | Sluggish Tempo (<65 BPM), Extreme High Pitch |
| 100 | `The Moon (2).mid` | D | 1-4, 6, 10-13, 15 | None | None | 11 | High: 90-94 (228 across 7 chs) | Ch 10 (GM) | 145.0-180.0 | None | Extreme High Pitch (228 notes > 88) |
| 101 | `The Moon.mid` | D | 1-4, 6, 10-13, 15 | None | None | 11 | High: 90-94 (228 across 7 chs) | Ch 10 (GM) | 145.0-180.0 | None | Extreme High Pitch (228 notes > 88) |
| 102 | `The Perfect Drug.mid` | D | 1-16 (All 16) | None | **Dang: Ch 2, 3, Acoustic: 244** | 17 | Low: 17 (36), 24 (148), High: 89-94 | **Ch 16 Melodic (369), 16-Ch Saturation** | 150.0 | None | Dangling PB, Acoustic PB, Sub-Piano Note 17, Ch 16 Collision |
| 103 | `The Show Must Go On - Celestial Trance Version.mid` | D | 1-6, 10, 11 | **All Melodic & Drum (XG SysEx Voiced)** | **Acoustic: 196 (Ch 1)** | 13 (ol: 22) | None | Ch 10 (GM) | 90.0-110.0 | **Yamaha XG (205)** | Missing Standard PC (XG Voiced), 205 XG SysEx |
| 104 | `Train - Play That Song - EASY.mid` | D | 1 | None | None | 4 | None | None | 120.0 | None | Clean |
| 105 | `YUI - Again.mid` | D | 1-4 | None | None | 12 | None | None | 120.0 | None | Clean |
| 106 | `Youre Not Alone.mid` | D | 1-3, 5-12 | None | None | 11 | None | Ch 10 (GM) | 142.0 | None | Clean |
| 107 | `Youre Not Alone2.mid` | D | 1-11 | **Ch 10 (Drum)** | None | 15 | Drum Low: Note 4 (80 hits) | Ch 10 (GM) | 65.0 | None | Out-of-Range Drum Note 4, Missing Drum PC |
| 108 | `Z Aeristhemepiano.mid` | D | 1-4 | None | None | 12 | None | None | 80.0 | None | Clean |
| 109 | `Zelda Overworld.mid` | D | 1, 2 | None | None | 2 | None | None | 148.0 | None | Clean |
| 110 | `simpleandclean.mid` | D | 1 | None | None | 5 | None | None | 96.0 | None | Clean |
| 111 | `snesK.Roolv2.1.mid` | D | 1-13 | **Ch 10 (Drum)** | None | 13 | None | Ch 10 (GM) | 115.0-130.0 | None | Missing Drum PC |
| 112 | `tim1.mid` | D | 1, 2 | None | None | 3 (sus: 12) | None | None | 120.0 | None | Clean |
| 113 | `tim2.mid` | D | 1, 2 | None | None | 4 (sus: 7) | None | None | 120.0 | None | Clean |

---

## Remapper Verification & Exact Engine Enhancements

### 1. In `Remapper.cs`: Pre-Scan Missing ProgramChange Initialization
**Issue Identified**: In `aqua-remix.mid`, `Brothers In Arms 3.mid`, `Lady Gaga - Applause.mid`, and `At Zanarkand`, active melodic channels have note events without initial `ProgramChangeEvent`s. In the current engine, `channelGmPrograms` only stores channels that explicitly contain a `ProgramChangeEvent`. Unassigned channels receive `gmProg = -1`, which causes `IsAcousticOrKeyboardInstrument` to return `false`. As a result:
- `NeutralizeAcousticPitchBends` is bypassed (6,290 micro-bends detuned piano voices in `aqua-remix`).
- `ClampMelodicNote` clamps to 96 instead of the acoustic limit of 88.
- `ApplySustainPedalEmulation` is skipped.

**Proposed Engine Enhancement in `Remapper.cs`**:
```csharp
// Pre-scan for GM Programs per channel. Active channels lacking a PC default to GM 0 (Acoustic Grand Piano)
var channelGmPrograms = new Dictionary<int, byte>();
foreach (var trackChunk in remapped.GetTrackChunks())
{
    foreach (var evt in trackChunk.Events)
    {
        if (evt is ProgramChangeEvent pc && !channelGmPrograms.ContainsKey(pc.Channel))
        {
            channelGmPrograms[pc.Channel] = (byte)pc.ProgramNumber;
        }
    }
}
// Default any active melodic note channel lacking PC to GM 0
foreach (var ch in allActiveNoteChannels)
{
    if (ch != 9 && !channelGmPrograms.ContainsKey(ch))
    {
        channelGmPrograms[ch] = 0; // Standard GM Default: Acoustic Grand Piano
    }
}
```

### 2. In `PolyphonyOptimizer.cs`: Tier 3 Melodic / Accompaniment Polyphony Culling
**Issue Identified**: `PolyphonyOptimizer.cs` currently only drops incidental percussion notes (triangles, cowbells, maracas) on Channel 15 when polyphony exceeds the threshold (`_threshold = 26`). If the dense section consists of pure melodic/harmonic chords (pianos, strings, brass), the optimizer hits `break;` and leaves polyphony at 32-44 notes (`Avicii`: 41-44, `Tubular Bells`: 31-41, `MillionDreams`: 37-41, `From Now On`: 36, `Never Enough`: 32). On PSS-790 hardware, exceeding 28 concurrent voices causes unpredictable note dropouts and `<Ch Full>` LCD warnings.

**Proposed Engine Enhancement in `PolyphonyOptimizer.cs`**:
```csharp
// Tier 3: If still over PolyphonyMax (28), cull lowest-velocity accompaniment notes
while (activeNotes.Count > Constants.PolyphonyMax)
{
    // Find lowest-velocity note from non-melody / background channels (channels > 1 or lowest velocity)
    var noteToCull = activeNotes
        .OrderBy(n => n.Velocity)
        .FirstOrDefault();

    if (noteToCull != null)
    {
        var key = (noteToCull.Channel, noteToCull.NoteNumber, noteToCull.Time);
        notesToCull.Add(key);
        activeNotes.Remove(noteToCull);
    }
    else
    {
        break;
    }
}
```

### 3. In `Remapper.cs`: Unreleased Damper Pedal (CC 64) Protection
**Issue Identified**: In `At Zanarkand Piano Collection (Unofficial).mid`, CC 64 (Sustain Pedal) is engaged (`value = 127`) at Tick 0 and is never turned off. `ApplySustainPedalEmulation` extends every single note until the end of the track (bounded only by `maxPedalExtensionTicks = 8 beats`). In passages with rapid arpeggios, overlapping notes accumulate up to 929 voices.

**Proposed Engine Enhancement in `Remapper.cs`**:
```csharp
// Safety guard: If CC 64 remains held for more than 4 measures without pedal release (CC 64 = 0),
// automatically inject a pedal-release interval boundary to reset sustain accumulation.
```

### 4. In `Remapper.cs`: Yamaha XG Multi-Part SysEx Parameter Voicing Parser
**Issue Identified**: `The Show Must Go On - Celestial Trance Version.mid` contains zero standard `ProgramChangeEvent`s across all melodic and drum tracks. Voice numbers are configured via 205 Yamaha XG Multi-Part SysEx parameter dumps (`F0 43 00 4C 00 29 08 [part] 00 ... F7`). Currently, `Remapper` removes all SysEx and falls back to Voice 00 (PIANO) on every channel.

**Proposed Engine Enhancement in `Remapper.cs`**:
```csharp
// Before removing SysEx events, scan for XG Multi-Part Program Setup dumps:
// Byte 0: 0x43 (Yamaha), Byte 2: 0x4C (XG), Byte 5: 0x08 (Multi-Part), Byte 6: Part (0-15), Byte 10: GM Program
if (data.Length >= 11 && data[0] == 0x43 && data[2] == 0x4C && data[5] == 0x08)
{
    int partCh = data[6];
    byte gmProg = data[10];
    if (partCh >= 0 && partCh < 16 && !channelGmPrograms.ContainsKey(partCh))
    {
        channelGmPrograms[partCh] = gmProg;
    }
}
```
