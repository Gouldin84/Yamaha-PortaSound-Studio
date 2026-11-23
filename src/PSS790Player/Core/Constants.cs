using System.Collections.Generic;
using System.Collections.ObjectModel;
using Melanchall.DryWetMidi.Core;

namespace PSS790Player.Core
{
    public record VoiceInfo(int VoiceNumber, string Name, string Category, byte MidiProgram);
    public record DrumInfo(byte NoteNumber, string Name, string NoteName);

    public static class Constants
    {
        public const int PolyphonyMax = 28;
        public const int PolyphonyGuardThreshold = 26; // Raised from 22 to 26 to maximize utilization of PSS-790 28-voice hardware limit
        public const double PitchBendScaleFactor = 1.0 / 6.0; // PSS-790 is fixed +/- 12 semitones, GM is +/- 2
        public const byte AwmVelocityMin = 36;
        public const byte AwmVelocityMax = 118;

        public static readonly ReadingSettings DefaultReadingSettings = new()
        {
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
            InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.ReadValid,
            InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
            MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore
        };

        public static readonly HashSet<byte> AllowedControlChanges = new()
        {
            1,  // Modulation / Vibrato
            7,  // Volume
            10, // Pan
            91  // Reverb Level (0x5B)
        };

        // Complete 100 Voices from Manual Chart 2
        public static readonly ReadOnlyDictionary<int, VoiceInfo> Pss790Voices = new(new Dictionary<int, VoiceInfo>
        {
            { 0, new(0, "PIANO", "Keyboard", 3) },
            { 1, new(1, "ELECTRIC PIANO 1", "Keyboard", 5) },
            { 2, new(2, "ELECTRIC PIANO 2", "Keyboard", 54) },
            { 3, new(3, "HARPSICHORD 1", "Keyboard", 4) },
            { 4, new(4, "HARPSICHORD 2", "Keyboard", 53) },
            { 5, new(5, "HONKY-TONK PIANO", "Keyboard", 25) },
            { 6, new(6, "CRAVI", "Keyboard", 30) },
            { 7, new(7, "CELESTA", "Keyboard", 6) },
            { 8, new(8, "PIPE ORGAN", "Organ", 2) },
            { 9, new(9, "REED ORGAN", "Organ", 49) },
            { 10, new(10, "JAZZ ORGAN 1", "Organ", 1) },
            { 11, new(11, "JAZZ ORGAN 2", "Organ", 27) },
            { 12, new(12, "TREMOLO ORGAN", "Organ", 28) },
            { 13, new(13, "FULL ORGAN", "Organ", 29) },
            { 14, new(14, "ACCORDION", "Organ", 31) },
            { 15, new(15, "DISTORTION GUITAR", "Guitar", 13) },
            { 16, new(16, "ELECTRIC GUITAR", "Guitar", 68) },
            { 17, new(17, "TREMOLO GUITAR", "Guitar", 69) },
            { 18, new(18, "MUTE GUITAR", "Guitar", 70) },
            { 19, new(19, "DISTORTION GUITAR FLANGE", "Guitar", 108) },
            { 20, new(20, "E. GUITAR FLANGE", "Guitar", 121) },
            { 21, new(21, "MUTE GUITAR ECHO", "Guitar", 116) },
            { 22, new(22, "E. GUITAR ECHO", "Guitar", 123) },
            { 23, new(23, "JAZZ GUITAR", "Guitar", 12) },
            { 24, new(24, "JAZZ GUITAR ECHO", "Guitar", 122) },
            { 25, new(25, "FOLK GUITAR", "Guitar", 36) },
            { 26, new(26, "12 STRING GUITAR", "Guitar", 72) },
            { 27, new(27, "GUT GUITAR", "Guitar", 73) },
            { 28, new(28, "GUT GUITAR ECHO", "Guitar", 124) },
            { 29, new(29, "STEEL GUITAR", "Guitar", 33) },
            { 30, new(30, "VIOLIN 1", "Strings", 10) },
            { 31, new(31, "VIOLIN 2", "Strings", 67) },
            { 32, new(32, "PIZZICATO VIOLIN", "Strings", 65) },
            { 33, new(33, "CELLO", "Strings", 11) },
            { 34, new(34, "STRINGS 1", "Ensemble", 41) },
            { 35, new(35, "STRINGS 2", "Ensemble", 50) },
            { 36, new(36, "PIZZICATO STRINGS", "Ensemble", 78) },
            { 37, new(37, "HARP", "Strings", 37) },
            { 38, new(38, "UKULELE", "Plucked", 40) },
            { 39, new(39, "BANJO", "Plucked", 34) },
            { 40, new(40, "VIBRAPHONE", "Chromatic", 7) },
            { 41, new(41, "MARIMBA", "Chromatic", 8) },
            { 42, new(42, "XYLOPHONE", "Chromatic", 59) },
            { 43, new(43, "GLOCKENSPIEL", "Chromatic", 32) },
            { 44, new(44, "TRUMPET", "Brass", 15) },
            { 45, new(45, "MUTE TRUMPET", "Brass", 44) },
            { 46, new(46, "TRUMPET ECHO", "Brass", 118) },
            { 47, new(47, "MUTE TRUMPET ECHO", "Brass", 120) },
            { 48, new(48, "FLUGELHORN", "Brass", 87) },
            { 49, new(49, "TROMBONE", "Brass", 16) },
            { 50, new(50, "HORN", "Brass", 17) },
            { 51, new(51, "TUBA", "Brass", 84) },
            { 52, new(52, "BRASS ENSEMBLE", "Brass", 92) },
            { 53, new(53, "BRASS HIT", "Brass", 104) },
            { 54, new(54, "CLARINET", "Woodwind", 19) },
            { 55, new(55, "FLUTE", "Woodwind", 20) },
            { 56, new(56, "PICCOLO", "Woodwind", 103) },
            { 57, new(57, "SOPRANO SAX", "Reed", 101) },
            { 58, new(58, "ALTO SAX", "Reed", 18) },
            { 59, new(59, "TENOR SAX", "Reed", 102) },
            { 60, new(60, "BASSOON", "Woodwind", 21) },
            { 61, new(61, "RECORDER", "Woodwind", 88) },
            { 62, new(62, "SAMBA WHISTLE", "Wind", 91) },
            { 63, new(63, "WOODWIND ENSEMBLE", "Woodwind", 93) },
            { 64, new(64, "CHORUS", "Vocal", 96) },
            { 65, new(65, "SYNTH BRASS 1", "Synth", 0) },
            { 66, new(66, "SYNTH BRASS 2", "Synth", 82) },
            { 67, new(67, "SYNTH BRASS 3", "Synth", 115) },
            { 68, new(68, "SYNTH CHORUS", "Synth", 94) },
            { 69, new(69, "SYNTH FLUTE 1", "Synth", 112) },
            { 70, new(70, "SYNTH FLUTE 2", "Synth", 47) },
            { 71, new(71, "SYNTH REED 1", "Synth", 45) },
            { 72, new(72, "SYNTH REED 2", "Synth", 85) },
            { 73, new(73, "SYNTH REED 3", "Synth", 113) },
            { 74, new(74, "SYNTH STRINGS 1", "Synth", 51) },
            { 75, new(75, "SYNTH STRINGS 2", "Synth", 109) },
            { 76, new(76, "SYNTH STRINGS 3", "Synth", 119) },
            { 77, new(77, "SYNTH PIANO 1", "Synth", 60) },
            { 78, new(78, "SYNTH PIANO 2", "Synth", 117) },
            { 79, new(79, "SYNTH PIANO 3", "Synth", 110) },
            { 80, new(80, "SYNTH PIANO 4", "Synth", 95) },
            { 81, new(81, "SYNTH PAN VOICE", "Synth", 111) },
            { 82, new(82, "FANTASY", "Synth", 64) },
            { 83, new(83, "ELECTRIC BASS", "Bass", 38) },
            { 84, new(84, "SLAP BASS", "Bass", 39) },
            { 85, new(85, "FRETLESS BASS", "Bass", 80) },
            { 86, new(86, "PICK BASS", "Bass", 79) },
            { 87, new(87, "MUTE BASS", "Bass", 114) },
            { 88, new(88, "MUTE BASS ECHO", "Bass", 107) },
            { 89, new(89, "WOOD BASS 1", "Bass", 14) },
            { 90, new(90, "WOOD BASS 2", "Bass", 81) },
            { 91, new(91, "SYNTH BASS 1", "Synth Bass", 58) },
            { 92, new(92, "SYNTH BASS 2", "Synth Bass", 86) },
            { 93, new(93, "BOWED BASS", "Bass", 35) },
            { 94, new(94, "STEEL DRUM", "Percussive", 9) },
            { 95, new(95, "TIMPANI", "Percussive", 66) },
            { 96, new(96, "PERCUSSIONS", "Percussion", 100) },
            { 97, new(97, "HARMONICA", "Wind", 22) },
            { 98, new(98, "MUSIC BOX", "Chromatic", 24) },
            { 99, new(99, "ORCHESTRA HIT", "Accent", 90) }
        });

        // Complete Percussion Sounds from Manual Chart 1
        public static readonly ReadOnlyDictionary<byte, DrumInfo> Pss790Drums = new(new Dictionary<byte, DrumInfo>
        {
            { 36, new(36, "BASS DRUM REVERB", "C1") },
            { 37, new(37, "TRIANGLE MUTE", "C#1") },
            { 38, new(38, "SYNTH SNARE", "D1") },
            { 39, new(39, "TRIANGLE OPEN", "D#1") },
            { 40, new(40, "SYNTH TOM BASS", "E1") },
            { 41, new(41, "SYNTH TOM LO", "F1") },
            { 42, new(42, "SYNTH TOM MID", "F#1") },
            { 43, new(43, "SYNTH TOM HI", "G1") },
            { 44, new(44, "BASS DRUM LO", "G#1") },
            { 45, new(45, "BASS DRUM HI", "A1") },
            { 46, new(46, "RIM SHOT 1", "A#1") },
            { 47, new(47, "TOM BASS", "B1") },
            { 48, new(48, "TOM LO", "C2") },
            { 49, new(49, "SNARE HI", "C#2") },
            { 50, new(50, "TOM MID", "D2") },
            { 51, new(51, "RIM SHOT 2", "D#2") },
            { 52, new(52, "SNARE LO", "E2") },
            { 53, new(53, "TOM HI", "F2") },
            { 54, new(54, "HAND CLAPS", "F#2") },
            { 55, new(55, "COWBELL", "G2") },
            { 56, new(56, "CABASA", "G#2") },
            { 57, new(57, "HI HAT CLOSED", "A2") },
            { 58, new(58, "BRASH HIT", "A#2") },
            { 59, new(59, "HI HAT OPEN", "B2") },
            { 60, new(60, "CRASH CYMBAL", "C3") },
            { 61, new(61, "SPLASH CYMBAL", "C#3") },
            { 62, new(62, "RIDE CYMBAL CUP", "D3") },
            { 63, new(63, "RIDE CYMBAL EDGE", "D#3") },
            { 64, new(64, "CONGA LO", "E3") },
            { 65, new(65, "CONGA HI", "F3") },
            { 66, new(66, "CONGA HI MUTE", "F#3") },
            { 67, new(67, "BONGO LO", "G3") },
            { 68, new(68, "BONGO HI", "G#3") },
            { 69, new(69, "TIMBALE LO", "A3") },
            { 70, new(70, "TIMBALE HI", "A#3") },
            { 71, new(71, "TAMBOURINE", "B3") },
            { 72, new(72, "CLAVES LO", "C4") },
            { 73, new(73, "CLAVES HI", "C#4") },
            { 74, new(74, "AGOGO LO", "D4") },
            { 75, new(75, "AGOGO HI", "D#4") },
            { 76, new(76, "CUICA LO", "E4") },
            { 77, new(77, "CUICA HI", "F4") },
            { 78, new(78, "WHISTLE", "F#4") },
            { 79, new(79, "BRASH SQUEEZE", "G4") },
            { 80, new(80, "HI HAT FOOT", "G#4") },
            { 81, new(81, "SNARE GATED REVERB", "A4") },
            { 82, new(82, "ONE", "A#4") },
            { 83, new(83, "TWO", "B4") },
            { 84, new(84, "THREE", "C5") },
            { 85, new(85, "FOUR", "C#5") }
        });

        // General MIDI Drum (35-81) to PSS-790 Percussion Note Map
        public static readonly ReadOnlyDictionary<byte, byte> GmToPss790DrumMap = new(new Dictionary<byte, byte>
        {
            { 35, 44 }, // Acoustic Bass Drum -> BASS DRUM LO
            { 36, 45 }, // Bass Drum 1 -> BASS DRUM HI
            { 37, 46 }, // Side Stick -> RIM SHOT 1
            { 38, 49 }, // Acoustic Snare -> SNARE HI
            { 39, 54 }, // Hand Clap -> HAND CLAPS
            { 40, 49 }, // Electric Snare -> SNARE HI (rock backbeat punch)
            { 41, 47 }, // Low Floor Tom -> TOM BASS
            { 42, 57 }, // Closed Hi-Hat -> HI HAT CLOSED
            { 43, 48 }, // High Floor Tom -> TOM LO
            { 44, 80 }, // Pedal Hi-Hat -> HI HAT FOOT
            { 45, 48 }, // Low Tom -> TOM LO
            { 46, 59 }, // Open Hi-Hat -> HI HAT OPEN
            { 47, 50 }, // Low-Mid Tom -> TOM MID
            { 48, 50 }, // Hi-Mid Tom -> TOM MID
            { 49, 60 }, // Crash Cymbal 1 -> CRASH CYMBAL
            { 50, 53 }, // High Tom -> TOM HI
            { 51, 63 }, // Ride Cymbal 1 -> RIDE CYMBAL EDGE
            { 52, 60 }, // Chinese Cymbal -> CRASH CYMBAL
            { 53, 62 }, // Ride Bell -> RIDE CYMBAL CUP
            { 54, 71 }, // Tambourine -> TAMBOURINE
            { 55, 61 }, // Splash Cymbal -> SPLASH CYMBAL
            { 56, 55 }, // Cowbell -> COWBELL
            { 57, 60 }, // Crash Cymbal 2 -> CRASH CYMBAL
            { 58, 56 }, // Vibraslap -> CABASA
            { 59, 63 }, // Ride Cymbal 2 -> RIDE CYMBAL EDGE
            { 60, 68 }, // Hi Bongo -> BONGO HI
            { 61, 67 }, // Low Bongo -> BONGO LO
            { 62, 66 }, // Mute Hi Conga -> CONGA HI MUTE
            { 63, 65 }, // Open Hi Conga -> CONGA HI
            { 64, 64 }, // Low Conga -> CONGA LO
            { 65, 70 }, // High Timbale -> TIMBALE HI
            { 66, 69 }, // Low Timbale -> TIMBALE LO
            { 67, 75 }, // High Agogo -> AGOGO HI
            { 68, 74 }, // Low Agogo -> AGOGO LO
            { 69, 56 }, // Cabasa -> CABASA
            { 70, 56 }, // Maracas -> CABASA
            { 71, 78 }, // Short Whistle -> WHISTLE
            { 72, 78 }, // Long Whistle -> WHISTLE
            { 73, 79 }, // Short Guiro -> BRASH SQUEEZE
            { 74, 79 }, // Long Guiro -> BRASH SQUEEZE
            { 75, 72 }, // Claves -> CLAVES LO
            { 76, 73 }, // Hi Wood Block -> CLAVES HI
            { 77, 72 }, // Low Wood Block -> CLAVES LO
            { 78, 76 }, // Mute Cuica -> CUICA LO
            { 79, 77 }, // Open Cuica -> CUICA HI
            { 80, 37 }, // Mute Triangle -> TRIANGLE MUTE
            { 81, 39 }  // Open Triangle -> TRIANGLE OPEN
        });

        // General MIDI Instrument (0-127) to PSS-790 Voice Number Map
        public static readonly ReadOnlyDictionary<byte, int> GmToPss790VoiceMap = new(new Dictionary<byte, int>
        {
            // 0-7: Piano
            { 0, 0 }, { 1, 0 }, { 2, 0 }, { 3, 5 }, { 4, 1 }, { 5, 2 }, { 6, 3 }, { 7, 6 },
            // 8-15: Chromatic Percussion
            { 8, 7 }, { 9, 43 }, { 10, 98 }, { 11, 40 }, { 12, 41 }, { 13, 42 }, { 14, 43 }, { 15, 37 },
            // 16-23: Organ
            { 16, 10 }, { 17, 11 }, { 18, 13 }, { 19, 8 }, { 20, 9 }, { 21, 14 }, { 22, 97 }, { 23, 14 },
            // 24-31: Guitar
            { 24, 27 }, { 25, 29 }, { 26, 23 }, { 27, 16 }, { 28, 18 }, { 29, 15 }, { 30, 15 }, { 31, 26 },
            // 32-39: Bass
            { 32, 89 }, { 33, 83 }, { 34, 86 }, { 35, 85 }, { 36, 84 }, { 37, 84 }, { 38, 91 }, { 39, 92 },
            // 40-47: Solo Strings
            { 40, 30 }, { 41, 33 }, { 42, 33 }, { 43, 93 }, { 44, 35 }, { 45, 36 }, { 46, 37 }, { 47, 95 },
            // 48-55: Ensemble
            { 48, 34 }, { 49, 35 }, { 50, 74 }, { 51, 75 }, { 52, 64 }, { 53, 68 }, { 54, 81 }, { 55, 99 },
            // 56-63: Brass
            { 56, 44 }, { 57, 49 }, { 58, 51 }, { 59, 45 }, { 60, 50 }, { 61, 52 }, { 62, 65 }, { 63, 66 },
            // 64-71: Reed
            { 64, 57 }, { 65, 58 }, { 66, 59 }, { 67, 59 }, { 68, 63 }, { 69, 50 }, { 70, 60 }, { 71, 54 },
            // 72-79: Pipe
            { 72, 56 }, { 73, 55 }, { 74, 61 }, { 75, 81 }, { 76, 69 }, { 77, 81 }, { 78, 62 }, { 79, 55 },
            // 80-87: Synth Lead
            { 80, 71 }, { 81, 65 }, { 82, 69 }, { 83, 70 }, { 84, 72 }, { 85, 68 }, { 86, 73 }, { 87, 91 },
            // 88-95: Synth Pad
            { 88, 82 }, { 89, 74 }, { 90, 75 }, { 91, 64 }, { 92, 93 }, { 93, 76 }, { 94, 82 }, { 95, 76 },
            // 96-103: Synth Effects
            { 96, 98 }, { 97, 82 }, { 98, 7 }, { 99, 82 }, { 100, 77 }, { 101, 82 }, { 102, 22 }, { 103, 82 },
            // 104-111: Ethnic
            { 104, 39 }, { 105, 39 }, { 106, 39 }, { 107, 37 }, { 108, 98 }, { 109, 9 }, { 110, 30 }, { 111, 58 },
            // 112-119: Percussive
            { 112, 7 }, { 113, 43 }, { 114, 94 }, { 115, 41 }, { 116, 95 }, { 117, 95 }, { 118, 95 }, { 119, 96 },
            // 120-127: Sound FX
            { 120, 18 }, { 121, 61 }, { 122, 82 }, { 123, 56 }, { 124, 43 }, { 125, 15 }, { 126, 96 }, { 127, 99 }
        });

        // Incidental Percussion note set (eligible for culling during polyphony congestion)
        public static readonly HashSet<byte> IncidentalDrums = new()
        {
            37, // Triangle Mute
            39, // Triangle Open
            55, // Cowbell
            56, // Cabasa
            61, // Splash Cymbal
            62, // Ride Cymbal Cup
            66, // Conga Hi Mute
            71, // Tambourine
            72, // Claves Lo
            73, // Claves Hi
            74, // Agogo Lo
            75, // Agogo Hi
            76, // Cuica Lo
            77, // Cuica Hi
            78, // Whistle
            79  // Brash Squeeze
        };

        // Reverse Percussion Map: PSS-790 Chart 1 (36-85) to General MIDI Drum (35-81)
        public static readonly ReadOnlyDictionary<byte, byte> Pss790ToGmDrumMap = new(new Dictionary<byte, byte>
        {
            { 36, 35 }, // BASS DRUM REVERB -> Acoustic Bass Drum (35)
            { 37, 80 }, // TRIANGLE MUTE -> Mute Triangle (80)
            { 38, 40 }, // SYNTH SNARE -> Electric Snare (40)
            { 39, 81 }, // TRIANGLE OPEN -> Open Triangle (81)
            { 40, 41 }, // SYNTH TOM BASS -> Low Floor Tom (41)
            { 41, 45 }, // SYNTH TOM LO -> Low Tom (45)
            { 42, 47 }, // SYNTH TOM MID -> Low-Mid Tom (47)
            { 43, 50 }, // SYNTH TOM HI -> High Tom (50)
            { 44, 35 }, // BASS DRUM LO -> Acoustic Bass Drum (35)
            { 45, 36 }, // BASS DRUM HI -> Bass Drum 1 (36)
            { 46, 37 }, // RIM SHOT 1 -> Side Stick (37)
            { 47, 41 }, // TOM BASS -> Low Floor Tom (41)
            { 48, 45 }, // TOM LO -> Low Tom (45)
            { 49, 38 }, // SNARE HI -> Acoustic Snare (38)
            { 50, 47 }, // TOM MID -> Low-Mid Tom (47)
            { 51, 37 }, // RIM SHOT 2 -> Side Stick (37)
            { 52, 38 }, // SNARE LO -> Acoustic Snare (38)
            { 53, 50 }, // TOM HI -> High Tom (50)
            { 54, 39 }, // HAND CLAPS -> Hand Clap (39)
            { 55, 56 }, // COWBELL -> Cowbell (56)
            { 56, 69 }, // CABASA -> Cabasa (69)
            { 57, 42 }, // HI HAT CLOSED -> Closed Hi-Hat (42)
            { 58, 42 }, // BRASH HIT -> Closed Hi-Hat / Brush (42)
            { 59, 46 }, // HI HAT OPEN -> Open Hi-Hat (46)
            { 60, 49 }, // CRASH CYMBAL -> Crash Cymbal 1 (49)
            { 61, 55 }, // SPLASH CYMBAL -> Splash Cymbal (55)
            { 62, 53 }, // RIDE CYMBAL CUP -> Ride Bell (53)
            { 63, 59 }, // RIDE CYMBAL EDGE -> Ride Cymbal 2 (59)
            { 64, 64 }, // CONGA LO -> Low Conga (64)
            { 65, 63 }, // CONGA HI -> Open Hi Conga (63)
            { 66, 62 }, // CONGA HI MUTE -> Mute Hi Conga (62)
            { 67, 61 }, // BONGO LO -> Low Bongo (61)
            { 68, 60 }, // BONGO HI -> Hi Bongo (60)
            { 69, 66 }, // TIMBALE LO -> Low Timbale (66)
            { 70, 65 }, // TIMBALE HI -> High Timbale (65)
            { 71, 54 }, // TAMBOURINE -> Tambourine (54)
            { 72, 75 }, // CLAVES LO -> Claves (75)
            { 73, 76 }, // CLAVES HI -> Hi Wood Block (76)
            { 74, 68 }, // AGOGO LO -> Low Agogo (68)
            { 75, 67 }, // AGOGO HI -> High Agogo (67)
            { 76, 78 }, // CUICA LO -> Mute Cuica (78)
            { 77, 79 }, // CUICA HI -> Open Cuica (79)
            { 78, 71 }, // WHISTLE -> Short Whistle (71)
            { 79, 73 }, // BRASH SQUEEZE -> Short Guiro (73)
            { 80, 44 }, // HI HAT FOOT -> Pedal Hi-Hat (44)
            { 81, 38 }, // SNARE GATED REVERB -> Acoustic Snare (38)
            { 82, 76 }, // ONE -> Hi Wood Block / Click (76)
            { 83, 76 }, // TWO -> Hi Wood Block / Click (76)
            { 84, 76 }, // THREE -> Hi Wood Block / Click (76)
            { 85, 76 }  // FOUR -> Hi Wood Block / Click (76)
        });

        // Reverse Voice Map: PSS-790 Voice Number (0-99) to General MIDI Instrument (0-127)
        public static readonly ReadOnlyDictionary<int, byte> Pss790VoiceToGmMap = new(new Dictionary<int, byte>
        {
            { 0, 0 },   // PIANO -> Acoustic Grand Piano
            { 1, 4 },   // ELECTRIC PIANO 1 -> Electric Piano 1
            { 2, 5 },   // ELECTRIC PIANO 2 -> Electric Piano 2
            { 3, 6 },   // HARPSICHORD 1 -> Harpsichord
            { 4, 6 },   // HARPSICHORD 2 -> Harpsichord
            { 5, 3 },   // HONKY-TONK PIANO -> Honky-tonk Piano
            { 6, 7 },   // CRAVI -> Clavinet
            { 7, 8 },   // CELESTA -> Celesta
            { 8, 19 },  // PIPE ORGAN -> Church Organ
            { 9, 20 },  // REED ORGAN -> Reed Organ
            { 10, 16 }, // JAZZ ORGAN 1 -> Drawbar Organ
            { 11, 17 }, // JAZZ ORGAN 2 -> Percussive Organ
            { 12, 18 }, // TREMOLO ORGAN -> Rock Organ
            { 13, 18 }, // FULL ORGAN -> Rock Organ
            { 14, 21 }, // ACCORDION -> Accordion
            { 15, 30 }, // DISTORTION GUITAR -> Distortion Guitar
            { 16, 27 }, // ELECTRIC GUITAR -> Electric Guitar (clean)
            { 17, 27 }, // TREMOLO GUITAR -> Electric Guitar (clean)
            { 18, 28 }, // MUTE GUITAR -> Electric Guitar (muted)
            { 19, 30 }, // DISTORTION GUITAR FLANGE -> Distortion Guitar
            { 20, 27 }, // E. GUITAR FLANGE -> Electric Guitar (clean)
            { 21, 28 }, // MUTE GUITAR ECHO -> Electric Guitar (muted)
            { 22, 27 }, // E. GUITAR ECHO -> Electric Guitar (clean)
            { 23, 26 }, // JAZZ GUITAR -> Electric Guitar (jazz)
            { 24, 26 }, // JAZZ GUITAR ECHO -> Electric Guitar (jazz)
            { 25, 25 }, // FOLK GUITAR -> Acoustic Guitar (steel)
            { 26, 25 }, // 12 STRING GUITAR -> Acoustic Guitar (steel)
            { 27, 24 }, // GUT GUITAR -> Acoustic Guitar (nylon)
            { 28, 24 }, // GUT GUITAR ECHO -> Acoustic Guitar (nylon)
            { 29, 25 }, // STEEL GUITAR -> Acoustic Guitar (steel)
            { 30, 40 }, // VIOLIN 1 -> Violin
            { 31, 40 }, // VIOLIN 2 -> Violin
            { 32, 45 }, // PIZZICATO VIOLIN -> Pizzicato Strings
            { 33, 42 }, // CELLO -> Cello
            { 34, 48 }, // STRINGS 1 -> String Ensemble 1
            { 35, 49 }, // STRINGS 2 -> String Ensemble 2
            { 36, 45 }, // PIZZICATO STRINGS -> Pizzicato Strings
            { 37, 46 }, // HARP -> Orchestral Harp
            { 38, 24 }, // UKULELE -> Acoustic Guitar (nylon)
            { 39, 105 }, // BANJO -> Banjo
            { 40, 11 }, // VIBRAPHONE -> Vibraphone
            { 41, 12 }, // MARIMBA -> Marimba
            { 42, 13 }, // XYLOPHONE -> Xylophone
            { 43, 9 },  // GLOCKENSPIEL -> Glockenspiel
            { 44, 56 }, // TRUMPET -> Trumpet
            { 45, 59 }, // MUTE TRUMPET -> Muted Trumpet
            { 46, 56 }, // TRUMPET ECHO -> Trumpet
            { 47, 59 }, // MUTE TRUMPET ECHO -> Muted Trumpet
            { 48, 60 }, // FLUGELHORN -> French Horn
            { 49, 57 }, // TROMBONE -> Trombone
            { 50, 60 }, // HORN -> French Horn
            { 51, 58 }, // TUBA -> Tuba
            { 52, 61 }, // BRASS ENSEMBLE -> Brass Section
            { 53, 55 }, // BRASS HIT -> Orchestra Hit
            { 54, 71 }, // CLARINET -> Clarinet
            { 55, 73 }, // FLUTE -> Flute
            { 56, 72 }, // PICCOLO -> Piccolo
            { 57, 64 }, // SOPRANO SAX -> Soprano Sax
            { 58, 65 }, // ALTO SAX -> Alto Sax
            { 59, 66 }, // TENOR SAX -> Tenor Sax
            { 60, 70 }, // BASSOON -> Bassoon
            { 61, 74 }, // RECORDER -> Recorder
            { 62, 78 }, // SAMBA WHISTLE -> Whistle
            { 63, 68 }, // WOODWIND ENSEMBLE -> Oboe
            { 64, 52 }, // CHORUS -> Choir Aahs
            { 65, 62 }, // SYNTH BRASS 1 -> Synth Brass 1
            { 66, 63 }, // SYNTH BRASS 2 -> Synth Brass 2
            { 67, 62 }, // SYNTH BRASS 3 -> Synth Brass 1
            { 68, 53 }, // SYNTH CHORUS -> Voice Oohs
            { 69, 82 }, // SYNTH FLUTE 1 -> Lead 3 (calliope)
            { 70, 82 }, // SYNTH FLUTE 2 -> Lead 3 (calliope)
            { 71, 80 }, // SYNTH REED 1 -> Lead 1 (square)
            { 72, 84 }, // SYNTH REED 2 -> Lead 5 (charang)
            { 73, 86 }, // SYNTH REED 3 -> Lead 7 (fifths)
            { 74, 50 }, // SYNTH STRINGS 1 -> Synth Strings 1
            { 75, 51 }, // SYNTH STRINGS 2 -> Synth Strings 2
            { 76, 93 }, // SYNTH STRINGS 3 -> Pad 6 (metallic)
            { 77, 4 },  // SYNTH PIANO 1 -> Electric Piano 1
            { 78, 5 },  // SYNTH PIANO 2 -> Electric Piano 2
            { 79, 4 },  // SYNTH PIANO 3 -> Electric Piano 1
            { 80, 5 },  // SYNTH PIANO 4 -> Electric Piano 2
            { 81, 75 }, // SYNTH PAN VOICE -> Pan Flute
            { 82, 89 }, // FANTASY -> Pad 2 (warm)
            { 83, 33 }, // ELECTRIC BASS -> Electric Bass (finger)
            { 84, 36 }, // SLAP BASS -> Slap Bass 1
            { 85, 35 }, // FRETLESS BASS -> Fretless Bass
            { 86, 34 }, // PICK BASS -> Electric Bass (pick)
            { 87, 33 }, // MUTE BASS -> Electric Bass (finger)
            { 88, 33 }, // MUTE BASS ECHO -> Electric Bass (finger)
            { 89, 32 }, // WOOD BASS 1 -> Acoustic Bass
            { 90, 32 }, // WOOD BASS 2 -> Acoustic Bass
            { 91, 38 }, // SYNTH BASS 1 -> Synth Bass 1
            { 92, 39 }, // SYNTH BASS 2 -> Synth Bass 2
            { 93, 43 }, // BOWED BASS -> Contrabass
            { 94, 114 }, // STEEL DRUM -> Steel Drums
            { 95, 47 }, // TIMPANI -> Timpani
            { 96, 0 },  // PERCUSSIONS -> Standard Kit
            { 97, 22 }, // HARMONICA -> Harmonica
            { 98, 10 }, // MUSIC BOX -> Music Box
            { 99, 55 }  // ORCHESTRA HIT -> Orchestra Hit
        });

        // Reverse Chart 2 Program byte to VoiceInfo
        public static readonly ReadOnlyDictionary<byte, VoiceInfo> Pss790MidiProgramToVoice;

        // Reverse Chart 2 Program byte to GM Instrument Number (0-127)
        public static readonly ReadOnlyDictionary<byte, byte> Pss790MidiProgramToGmMap;

        static Constants()
        {
            var progToVoice = new Dictionary<byte, VoiceInfo>();
            foreach (var voice in Pss790Voices.Values)
            {
                progToVoice[voice.MidiProgram] = voice;
            }
            Pss790MidiProgramToVoice = new ReadOnlyDictionary<byte, VoiceInfo>(progToVoice);

            var map = new Dictionary<byte, byte>();
            foreach (var kvp in Pss790Voices)
            {
                if (Pss790VoiceToGmMap.TryGetValue(kvp.Key, out var gmProg))
                {
                    map[kvp.Value.MidiProgram] = gmProg;
                }
            }
            Pss790MidiProgramToGmMap = new ReadOnlyDictionary<byte, byte>(map);
        }

        public static bool IsMicrosoftGsWavetableSynth(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            return deviceName.IndexOf("Microsoft GS Wavetable", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   deviceName.IndexOf("GS Wavetable", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
