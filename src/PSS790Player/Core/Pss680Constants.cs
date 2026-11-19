using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PSS790Player.Core
{
    public record Pss680VoiceInfo(int Number, string Name, string Category)
    {
        public int VoiceNumber => Number;
    }
    public record Pss680StyleInfo(int Number, string Name, string Category);
    public record Pss680DrumInfo(byte NoteNumber, string Name, string PadSetAndNumber, string NoteName);

    public static class Pss680Constants
    {
        public const byte ManufacturerYamaha = 0x43;
        public const byte InstrumentClassPss = 0x76;

        public const byte FormatFmVoice = 0x00;
        public const byte FormatMelodyMemory = 0x01;
        public const byte FormatChordMemory = 0x02;
        public const byte FormatRhythmPattern = 0x03;

        public const int Format00DecodedByteCount = 33;
        public const int Format00NibbleCount = 66;
        public const int Format00TotalSysExLength = 72; // F0 43 76 00 [66 nibbles] [checksum] F7

        public const int Format01DecodedByteCount = 5026;
        public const int Format01NibbleCount = 10052;
        public const int Format01TotalSysExLength = 10058;

        public const int Format02DecodedByteCount = 202;
        public const int Format02NibbleCount = 404;
        public const int Format02TotalSysExLength = 410;

        public const int Format03DecodedByteCount = 358;
        public const int Format03NibbleCount = 716;
        public const int Format03TotalSysExLength = 722;

        public const int PolyphonyMode99 = 12;

        // PSS-680 Incidental Hand Percussion note set (eligible for culling during polyphony congestion)
        public static readonly HashSet<byte> IncidentalDrums = new()
        {
            54, // Hand Clap (Pad D7)
            55, // Cowbell (Pad D5)
            56, // Cabasa (Pad D6)
            58, // Brush Hit (Pad A7)
            61, // Splash Cymbal (Pad C8)
            66, // Conga Hi Alt
            67, // Bongo Low (Pad C1)
            68, // Bongo Hi (Pad C2)
            69, // Timbale Low (Pad D3)
            70, // Timbale Hi (Pad D4)
            71, // Tambourine (Pad C7)
            73, // Cowbell Alt
            74, // Agogo Low (Pad C3)
            75, // Agogo Hi (Pad C4)
            76, // Cuica Low (Pad C5)
            77, // Cuica Hi (Pad C6)
            78, // Whistle (Pad D8)
            79  // Brush (Pad A6)
        };

        // 100 Preset Voices (#00 - #99) directly from PSS-680 Owner's Manual p.40
        public static readonly ReadOnlyDictionary<int, Pss680VoiceInfo> Voices = new(new Dictionary<int, Pss680VoiceInfo>
        {
            { 0, new(0, "Synth Brass", "Brass") },
            { 1, new(1, "Jazz Organ", "Organ") },
            { 2, new(2, "Pipe Organ 1", "Organ") },
            { 3, new(3, "Piano 1", "Keyboard") },
            { 4, new(4, "Harpsichord 1", "Keyboard") },
            { 5, new(5, "Electric Piano 1", "Keyboard") },
            { 6, new(6, "Celesta", "Chromatic") },
            { 7, new(7, "Vibraphone", "Chromatic") },
            { 8, new(8, "Marimba 1", "Chromatic") },
            { 9, new(9, "Steel Drum 1", "Percussive") },
            { 10, new(10, "Violin 1", "Strings") },
            { 11, new(11, "Cello", "Strings") },
            { 12, new(12, "Jazz Guitar", "Guitar") },
            { 13, new(13, "Rock Guitar 1", "Guitar") },
            { 14, new(14, "Wood Bass 1", "Bass") },
            { 15, new(15, "Trumpet", "Brass") },
            { 16, new(16, "Trombone", "Brass") },
            { 17, new(17, "Horn", "Brass") },
            { 18, new(18, "Soprano Sax", "Reed") },
            { 19, new(19, "Clarinet", "Woodwind") },
            { 20, new(20, "Flute", "Woodwind") },
            { 21, new(21, "Oboe", "Woodwind") },
            { 22, new(22, "Harmonica", "Wind") },
            { 23, new(23, "Whistle", "Wind") },
            { 24, new(24, "Music Box", "Chromatic") },
            { 25, new(25, "Honky-Tonk Piano", "Keyboard") },
            { 26, new(26, "Toy Piano", "Keyboard") },
            { 27, new(27, "Transistor Organ", "Organ") },
            { 28, new(28, "Tremolo Organ", "Organ") },
            { 29, new(29, "Small Church Organ", "Organ") },
            { 30, new(30, "Funky Clavi", "Keyboard") },
            { 31, new(31, "Accordion", "Wind") },
            { 32, new(32, "Glockenspiel", "Chromatic") },
            { 33, new(33, "Hawaiian Guitar", "Guitar") },
            { 34, new(34, "Banjo", "Plucked") },
            { 35, new(35, "Bowed Bass", "Strings") },
            { 36, new(36, "Acoustic Guitar", "Guitar") },
            { 37, new(37, "Harp", "Strings") },
            { 38, new(38, "Picked Bass", "Bass") },
            { 39, new(39, "Slap Bass", "Bass") },
            { 40, new(40, "Ukulele", "Plucked") },
            { 41, new(41, "Strings", "Ensemble") },
            { 42, new(42, "Alpenhorn", "Brass") },
            { 43, new(43, "Bagpipe", "Wind") },
            { 44, new(44, "Mute Trumpet", "Brass") },
            { 45, new(45, "Tenor Sax", "Reed") },
            { 46, new(46, "Jug", "Wind") },
            { 47, new(47, "Panflute", "Woodwind") },
            { 48, new(48, "Ice Block", "Percussive") },
            { 49, new(49, "Reed Organ", "Organ") },
            { 50, new(50, "Electronic Organ", "Organ") },
            { 51, new(51, "Pipe Organ 2", "Organ") },
            { 52, new(52, "Piano 2", "Keyboard") },
            { 53, new(53, "Harpsichord 2", "Keyboard") },
            { 54, new(54, "Electric Piano 2", "Keyboard") },
            { 55, new(55, "Glass Celesta", "Chromatic") },
            { 56, new(56, "Bandoneon", "Wind") },
            { 57, new(57, "Street Organ", "Organ") },
            { 58, new(58, "Synth Bass", "Bass") },
            { 59, new(59, "Marimba 2", "Chromatic") },
            { 60, new(60, "Steel Drum 2", "Percussive") },
            { 61, new(61, "Chimes", "Chromatic") },
            { 62, new(62, "Tubular Bells", "Chromatic") },
            { 63, new(63, "Hand Bell", "Chromatic") },
            { 64, new(64, "Carillon", "Chromatic") },
            { 65, new(65, "Synth Tom", "Percussive") },
            { 66, new(66, "Timpani", "Percussive") },
            { 67, new(67, "Violin 2", "Strings") },
            { 68, new(68, "Rock Guitar 2", "Guitar") },
            { 69, new(69, "Tremolo Guitar", "Guitar") },
            { 70, new(70, "Rock Guitar 3", "Guitar") },
            { 71, new(71, "Pedal Steel Guitar", "Guitar") },
            { 72, new(72, "12String Guitar", "Guitar") },
            { 73, new(73, "Classic Guitar", "Guitar") },
            { 74, new(74, "Mandolin", "Plucked") },
            { 75, new(75, "Sitar", "Plucked") },
            { 76, new(76, "Koto", "Plucked") },
            { 77, new(77, "Shamisen", "Plucked") },
            { 78, new(78, "Jamisen", "Plucked") },
            { 79, new(79, "Mute Bass", "Bass") },
            { 80, new(80, "Electric Bass", "Bass") },
            { 81, new(81, "Wood Bass 2", "Bass") },
            { 82, new(82, "Electric Trumpet", "Brass") },
            { 83, new(83, "Wow Trumpet", "Brass") },
            { 84, new(84, "Tuba", "Brass") },
            { 85, new(85, "Alto Sax", "Reed") },
            { 86, new(86, "Bass Clarinet", "Woodwind") },
            { 87, new(87, "Bassoon", "Woodwind") },
            { 88, new(88, "Recorder", "Woodwind") },
            { 89, new(89, "Ocarina", "Woodwind") },
            { 90, new(90, "Piccolo", "Woodwind") },
            { 91, new(91, "Samba Whistle", "Wind") },
            { 92, new(92, "Brass Ensemble", "Brass") },
            { 93, new(93, "Woodwind Ensemble", "Woodwind") },
            { 94, new(94, "Human Voice 1", "Vocal") },
            { 95, new(95, "Human Voice 2", "Vocal") },
            { 96, new(96, "Human Chorus", "Vocal") },
            { 97, new(97, "Kazoo", "Wind") },
            { 98, new(98, "Musical Saw", "Synth") },
            { 99, new(99, "Sine Wave", "Synth") },
            // Digital Synthesizer User Banks (100–104) & Extended Slots (105–107)
            { 100, new(100, "User FM Synth Bank 1", "Digital Synthesizer") },
            { 101, new(101, "User FM Synth Bank 2", "Digital Synthesizer") },
            { 102, new(102, "User FM Synth Bank 3", "Digital Synthesizer") },
            { 103, new(103, "User FM Synth Bank 4", "Digital Synthesizer") },
            { 104, new(104, "User FM Synth Bank 5", "Digital Synthesizer") },
            { 105, new(105, "Extended FM Bank 1", "Digital Synthesizer") },
            { 106, new(106, "Extended FM Bank 2", "Digital Synthesizer") },
            { 107, new(107, "Extended FM Bank 3", "Digital Synthesizer") }
        });

        // 100 Rhythm Styles (#00 - #99) directly from PSS-680 Owner's Manual p.40
        public static readonly ReadOnlyDictionary<int, Pss680StyleInfo> Styles = new(new Dictionary<int, Pss680StyleInfo>
        {
            { 0, new(0, "Rhythm & Blues 1", "R&B / Blues") },
            { 1, new(1, "Rhythm & Blues 2", "R&B / Blues") },
            { 2, new(2, "Slow Blues", "R&B / Blues") },
            { 3, new(3, "Gospel", "R&B / Blues") },
            { 4, new(4, "Disco", "Dance") },
            { 5, new(5, "Rap", "Dance") },
            { 6, new(6, "Safari", "World") },
            { 7, new(7, "Fusion 1", "Fusion") },
            { 8, new(8, "Fusion 2", "Fusion") },
            { 9, new(9, "16Beat 1", "Pop / Rock") },
            { 10, new(10, "16Beat 2", "Pop / Rock") },
            { 11, new(11, "Techno Rock 1", "Rock") },
            { 12, new(12, "Techno Rock 2", "Rock") },
            { 13, new(13, "Funk 1", "Funk") },
            { 14, new(14, "Funk 2", "Funk") },
            { 15, new(15, "Funk 3", "Funk") },
            { 16, new(16, "Funk 4", "Funk") },
            { 17, new(17, "Rock'n' Roll 1", "Rock") },
            { 18, new(18, "Rock'n' Roll 2", "Rock") },
            { 19, new(19, "Rock'n' Roll 3", "Rock") },
            { 20, new(20, "Rock-a-Ballad", "Rock") },
            { 21, new(21, "Hard Rock", "Rock") },
            { 22, new(22, "Heavy Metal", "Rock") },
            { 23, new(23, "Speed Metal", "Rock") },
            { 24, new(24, "Pop Rock 1", "Pop / Rock") },
            { 25, new(25, "Pop Rock 2", "Pop / Rock") },
            { 26, new(26, "Rock Baroque", "Fusion") },
            { 27, new(27, "Christmas Rock", "Pop / Rock") },
            { 28, new(28, "Oriental Rock", "Rock") },
            { 29, new(29, "Brass Rock", "Rock") },
            { 30, new(30, "Swing Guitar", "Jazz") },
            { 31, new(31, "Swing Piano", "Jazz") },
            { 32, new(32, "Swing Organ", "Jazz") },
            { 33, new(33, "Swing Vibraphone", "Jazz") },
            { 34, new(34, "Big Band 1", "Jazz") },
            { 35, new(35, "Big Band 2", "Jazz") },
            { 36, new(36, "Big Band 3", "Jazz") },
            { 37, new(37, "Big Band (Waltz)", "Jazz") },
            { 38, new(38, "Modern Jazz", "Jazz") },
            { 39, new(39, "Jazz Waltz", "Jazz") },
            { 40, new(40, "5/4 Swing", "Jazz") },
            { 41, new(41, "Jazz Ballad", "Jazz") },
            { 42, new(42, "Shuffle", "Jazz") },
            { 43, new(43, "Boogie Woogie Piano 1", "Jazz") },
            { 44, new(44, "Boogie Woogie Piano 2", "Jazz") },
            { 45, new(45, "Boogie Woogie Piano 3", "Jazz") },
            { 46, new(46, "Boogie Big Band 1", "Jazz") },
            { 47, new(47, "Boogie Big Band 2", "Jazz") },
            { 48, new(48, "Dixieland Combo", "Jazz") },
            { 49, new(49, "Dixieland Banjo", "Jazz") },
            { 50, new(50, "Jazz Baroque", "Jazz") },
            { 51, new(51, "Bossa Nova 1", "Latin") },
            { 52, new(52, "Bossa Nova 2", "Latin") },
            { 53, new(53, "Samba 1", "Latin") },
            { 54, new(54, "Samba 2", "Latin") },
            { 55, new(55, "Samba 3", "Latin") },
            { 56, new(56, "Salsa 1", "Latin") },
            { 57, new(57, "Salsa 2", "Latin") },
            { 58, new(58, "Salsa 3", "Latin") },
            { 59, new(59, "Tango", "Latin") },
            { 60, new(60, "Habanera", "Latin") },
            { 61, new(61, "Mambo 1", "Latin") },
            { 62, new(62, "Mambo 2", "Latin") },
            { 63, new(63, "Mambo 3", "Latin") },
            { 64, new(64, "Rhumba", "Latin") },
            { 65, new(65, "Cha-Cha-Cha", "Latin") },
            { 66, new(66, "Conga", "Latin") },
            { 67, new(67, "Merengue", "Latin") },
            { 68, new(68, "Calypso 1", "Latin") },
            { 69, new(69, "Calypso 2", "Latin") },
            { 70, new(70, "Son Afro", "Latin") },
            { 71, new(71, "Reggae", "Latin") },
            { 72, new(72, "Cuban", "Latin") },
            { 73, new(73, "Beguine", "Latin") },
            { 74, new(74, "Island Folklore", "World") },
            { 75, new(75, "Mariachi", "World") },
            { 76, new(76, "Argentine Folklore", "World") },
            { 77, new(77, "March 1", "Traditional") },
            { 78, new(78, "March 2", "Traditional") },
            { 79, new(79, "Majestic March", "Traditional") },
            { 80, new(80, "Fanfare", "Traditional") },
            { 81, new(81, "Lullaby", "Traditional") },
            { 82, new(82, "Baroque", "Classical") },
            { 83, new(83, "Baroque (Waltz)", "Classical") },
            { 84, new(84, "String Quartet", "Classical") },
            { 85, new(85, "Viennese Waltz", "Classical") },
            { 86, new(86, "Polka", "Traditional") },
            { 87, new(87, "Bolero", "Traditional") },
            { 88, new(88, "Flamenco", "Traditional") },
            { 89, new(89, "Classical Guitar", "Classical") },
            { 90, new(90, "Ragtime Piano", "Traditional") },
            { 91, new(91, "Country", "Country") },
            { 92, new(92, "Country Piano", "Country") },
            { 93, new(93, "Country Steel Guitar", "Country") },
            { 94, new(94, "Bluegrass", "Country") },
            { 95, new(95, "Street Organ", "Traditional") },
            { 96, new(96, "Hawaiian", "World") },
            { 97, new(97, "Mandolin Band", "Traditional") },
            { 98, new(98, "Chanson", "Traditional") },
            { 99, new(99, "Barbershop Quartet", "Traditional") }
        });

        // 32 Percussion Instruments mapped to MIDI Notes on Channel 16 (Manual pp. 32-33)
        public static readonly ReadOnlyDictionary<byte, Pss680DrumInfo> DrumMap = new(new Dictionary<byte, Pss680DrumInfo>
        {
            { 38, new(38, "Snare Hi (Alt)", "-", "D1") },
            { 40, new(40, "Synth Tom Low (Alt)", "-", "E1") },
            { 41, new(41, "Synth Tom Low", "Pad B3", "F1") },
            { 42, new(42, "Synth Tom Mid", "Pad B4", "F#1") },
            { 43, new(43, "Synth Tom Hi", "Pad B5", "G1") },
            { 44, new(44, "Bass Drum (Alt)", "-", "G#1") },
            { 45, new(45, "Bass Drum", "Pad B1", "A1") },
            { 46, new(46, "Rim Shot (Alt)", "-", "A#1") },
            { 47, new(47, "Low Tom (Alt)", "-", "B1") },
            { 48, new(48, "Low Tom", "Pad A3", "C2") },
            { 49, new(49, "Snare Hi", "Pad B2", "C#2") },
            { 50, new(50, "Mid Tom", "Pad A4", "D2") },
            { 51, new(51, "Rim Shot", "Pad A1", "D#2") },
            { 52, new(52, "Snare Low", "Pad A2", "E2") },
            { 53, new(53, "Hi Tom", "Pad A5", "F2") },
            { 54, new(54, "Hand Clap", "Pad D7", "F#2") },
            { 55, new(55, "Cowbell", "Pad D5", "G2") },
            { 56, new(56, "Cabasa", "Pad D6", "G#2") },
            { 57, new(57, "Hi-Hat Closed", "Pad B6", "A2") },
            { 58, new(58, "Brush Hit", "Pad A7", "A#2") },
            { 59, new(59, "Hi-Hat Open", "Pad B7", "B2") },
            { 60, new(60, "Crash Cymbal", "Pad B8", "C3") },
            { 61, new(61, "Splash Cymbal", "Pad C8", "C#3") },
            { 62, new(62, "Ride Cymbal (Alt)", "-", "D3") },
            { 63, new(63, "Ride Cymbal", "Pad A8", "D#3") },
            { 64, new(64, "Conga Low", "Pad D1", "E3") },
            { 65, new(65, "Conga Hi", "Pad D2", "F3") },
            { 66, new(66, "Conga Hi (Alt)", "-", "F#3") },
            { 67, new(67, "Bongo Low", "Pad C1", "G3") },
            { 68, new(68, "Bongo Hi", "Pad C2", "G#3") },
            { 69, new(69, "Timbale Low", "Pad D3", "A3") },
            { 70, new(70, "Timbale Hi", "Pad D4", "A#3") },
            { 71, new(71, "Tambourine", "Pad C7", "B3") },
            { 73, new(73, "Cowbell (Alt)", "-", "C#4") },
            { 74, new(74, "Agogo Low", "Pad C3", "D4") },
            { 75, new(75, "Agogo Hi", "Pad C4", "D#4") },
            { 76, new(76, "Cuica Low", "Pad C5", "E4") },
            { 77, new(77, "Cuica Hi", "Pad C6", "F4") },
            { 78, new(78, "Whistle", "Pad D8", "F#4") },
            { 79, new(79, "Brush", "Pad A6", "G4") }
        });

        // Full 128-entry General MIDI to PSS-680 FM Voice Map
        public static readonly ReadOnlyDictionary<byte, int> GmToPss680VoiceMap = new(new Dictionary<byte, int>
        {
            // Pianos
            { 0, 3 }, { 1, 52 }, { 2, 5 }, { 3, 25 }, { 4, 5 }, { 5, 54 }, { 6, 4 }, { 7, 30 },
            // Chromatic
            { 8, 6 }, { 9, 32 }, { 10, 24 }, { 11, 7 }, { 12, 8 }, { 13, 59 }, { 14, 62 }, { 15, 76 },
            // Organs
            { 16, 1 }, { 17, 28 }, { 18, 27 }, { 19, 2 }, { 20, 49 }, { 21, 31 }, { 22, 22 }, { 23, 56 },
            // Guitars
            { 24, 73 }, { 25, 36 }, { 26, 12 }, { 27, 69 }, { 28, 13 }, { 29, 68 }, { 30, 70 }, { 31, 71 },
            // Basses
            { 32, 14 }, { 33, 80 }, { 34, 38 }, { 35, 81 }, { 36, 39 }, { 37, 39 }, { 38, 58 }, { 39, 58 },
            // Strings
            { 40, 10 }, { 41, 67 }, { 42, 11 }, { 43, 35 }, { 44, 41 }, { 45, 37 }, { 46, 37 }, { 47, 66 },
            // Ensemble
            { 48, 41 }, { 49, 41 }, { 50, 41 }, { 51, 41 }, { 52, 96 }, { 53, 94 }, { 54, 95 }, { 55, 0 },
            // Brass
            { 56, 15 }, { 57, 16 }, { 58, 84 }, { 59, 44 }, { 60, 17 }, { 61, 92 }, { 62, 0 }, { 63, 82 },
            // Reed
            { 64, 18 }, { 65, 85 }, { 66, 45 }, { 67, 86 }, { 68, 21 }, { 69, 21 }, { 70, 87 }, { 71, 19 },
            // Pipe
            { 72, 90 }, { 73, 20 }, { 74, 88 }, { 75, 47 }, { 76, 46 }, { 77, 89 }, { 78, 23 }, { 79, 89 },
            // Synth Lead
            { 80, 99 }, { 81, 98 }, { 82, 23 }, { 83, 47 }, { 84, 13 }, { 85, 94 }, { 86, 0 }, { 87, 58 },
            // Synth Pad
            { 88, 96 }, { 89, 41 }, { 90, 0 }, { 91, 96 }, { 92, 35 }, { 93, 55 }, { 94, 94 }, { 95, 83 },
            // Synth FX
            { 96, 48 }, { 97, 41 }, { 98, 55 }, { 99, 99 }, { 100, 61 }, { 101, 95 }, { 102, 24 }, { 103, 99 },
            // Ethnic
            { 104, 75 }, { 105, 34 }, { 106, 77 }, { 107, 76 }, { 108, 24 }, { 109, 43 }, { 110, 10 }, { 111, 21 },
            // Percussive
            { 112, 63 }, { 113, 48 }, { 114, 9 }, { 115, 48 }, { 116, 66 }, { 117, 65 }, { 118, 65 }, { 119, 99 },
            // Sound FX
            { 120, 13 }, { 121, 94 }, { 122, 99 }, { 123, 23 }, { 124, 64 }, { 125, 65 }, { 126, 97 }, { 127, 65 }
        });

        // Reverse PSS-680 FM Voice to General MIDI Program Map
        public static readonly ReadOnlyDictionary<int, byte> Pss680ToGmVoiceMap = new(new Dictionary<int, byte>
        {
            { 0, 62 }, { 1, 16 }, { 2, 19 }, { 3, 0 }, { 4, 6 }, { 5, 4 }, { 6, 8 }, { 7, 11 }, { 8, 12 }, { 9, 114 },
            { 10, 40 }, { 11, 42 }, { 12, 26 }, { 13, 28 }, { 14, 32 }, { 15, 56 }, { 16, 57 }, { 17, 60 }, { 18, 64 }, { 19, 71 },
            { 20, 73 }, { 21, 68 }, { 22, 22 }, { 23, 78 }, { 24, 10 }, { 25, 3 }, { 26, 0 }, { 27, 18 }, { 28, 17 }, { 29, 19 },
            { 30, 7 }, { 31, 21 }, { 32, 9 }, { 33, 26 }, { 34, 105 }, { 35, 43 }, { 36, 25 }, { 37, 46 }, { 38, 34 }, { 39, 36 },
            { 40, 24 }, { 41, 48 }, { 42, 60 }, { 43, 109 }, { 44, 59 }, { 45, 66 }, { 46, 76 }, { 47, 75 }, { 48, 115 }, { 49, 20 },
            { 50, 16 }, { 51, 19 }, { 52, 1 }, { 53, 6 }, { 54, 5 }, { 55, 93 }, { 56, 23 }, { 57, 16 }, { 58, 38 }, { 59, 13 },
            { 60, 114 }, { 61, 14 }, { 62, 14 }, { 63, 112 }, { 64, 124 }, { 65, 117 }, { 66, 47 }, { 67, 41 }, { 68, 29 }, { 69, 27 },
            { 70, 30 }, { 71, 31 }, { 72, 25 }, { 73, 24 }, { 74, 105 }, { 75, 104 }, { 76, 107 }, { 77, 106 }, { 78, 106 }, { 79, 34 },
            { 80, 33 }, { 81, 35 }, { 82, 63 }, { 83, 95 }, { 84, 58 }, { 85, 65 }, { 86, 67 }, { 87, 70 }, { 88, 74 }, { 89, 79 },
            { 90, 72 }, { 91, 78 }, { 92, 61 }, { 93, 71 }, { 94, 53 }, { 95, 54 }, { 96, 52 }, { 97, 126 }, { 98, 81 }, { 99, 80 }
        });

        // General MIDI Standard Drums (35-81) to PSS-680 Channel 16 Percussion Map (36-84)
        public static readonly ReadOnlyDictionary<byte, byte> GmToPss680DrumMap = new(new Dictionary<byte, byte>
        {
            { 35, 45 }, { 36, 45 }, { 37, 51 }, { 38, 52 }, { 39, 54 }, { 40, 49 },
            { 41, 48 }, { 42, 57 }, { 43, 48 }, { 44, 57 }, { 45, 48 }, { 46, 59 },
            { 47, 50 }, { 48, 50 }, { 49, 60 }, { 50, 53 }, { 51, 63 }, { 52, 60 },
            { 53, 63 }, { 54, 71 }, { 55, 61 }, { 56, 55 }, { 57, 60 }, { 58, 56 },
            { 59, 63 }, { 60, 68 }, { 61, 67 }, { 62, 65 }, { 63, 65 }, { 64, 64 },
            { 65, 70 }, { 66, 69 }, { 67, 75 }, { 68, 74 }, { 69, 56 }, { 70, 56 },
            { 71, 78 }, { 72, 78 }, { 73, 56 }, { 74, 56 }, { 75, 51 }, { 76, 51 },
            { 77, 51 }, { 78, 76 }, { 79, 77 }, { 80, 61 }, { 81, 61 }
        });

        // Reverse PSS-680 32-Drum Notes to General MIDI Drum Notes
        public static readonly ReadOnlyDictionary<byte, byte> Pss680ToGmDrumMap = new(new Dictionary<byte, byte>
        {
            { 38, 40 }, { 40, 41 }, { 41, 41 }, { 42, 47 }, { 43, 50 }, { 44, 35 },
            { 45, 36 }, { 46, 37 }, { 47, 45 }, { 48, 45 }, { 49, 40 }, { 50, 48 },
            { 51, 37 }, { 52, 38 }, { 53, 50 }, { 54, 39 }, { 55, 56 }, { 56, 69 },
            { 57, 42 }, { 58, 38 }, { 59, 46 }, { 60, 49 }, { 61, 55 }, { 62, 51 },
            { 63, 51 }, { 64, 64 }, { 65, 63 }, { 66, 63 }, { 67, 61 }, { 68, 60 },
            { 69, 66 }, { 70, 65 }, { 71, 54 }, { 73, 56 }, { 74, 68 }, { 75, 67 },
            { 76, 78 }, { 77, 79 }, { 78, 72 }, { 79, 38 }
        });

        public static byte TranslateDrumNote(byte gmNote)
        {
            if (GmToPss680DrumMap.TryGetValue(gmNote, out var pss680Note))
            {
                return pss680Note;
            }

            // Musical fallbacks for extended GM/GS/XG drum notes (13-34 and 82-91)
            if (gmNote is 13 or 14 or 86 or 87) return 45; // Surdo -> Bass Drum (Pad B1)
            if (gmNote is 15 or 16 or 27 or 28) return 51; // High Q / Slap / Whip -> Rim Shot (Pad A1)
            if (gmNote is 17 or 18 or 29 or 30) return 57; // Scratch Push/Pull -> Closed Hi-Hat (Pad D7)
            if (gmNote is 19 or 26) return 51;             // Finger Snap -> Rim Shot (Pad A1)
            if (gmNote is 20 or 21 or 23 or 31 or 32 or 33) return 55; // Clicks / Sticks -> Cowbell (Pad D5)
            if (gmNote is 22 or 34) return 61;             // Metronome Bell -> Agogo Hi (Pad B5)
            if (gmNote is 24 or 25) return 52;             // Snare Roll -> Snare Low (Pad A2)
            if (gmNote is 82 or 83) return 56;             // Shaker / Jingle Bell -> Cabasa (Pad D6)
            if (gmNote is 84 or 85) return 71;             // Bell Tree / Castanets -> Tambourine (Pad C7)
            if (gmNote is 88 or 89) return 61;             // Cuica -> Agogo Hi (Pad B5)
            if (gmNote is 90 or 91) return 61;             // Triangle -> Agogo Hi (Pad B5)

            if (DrumMap.ContainsKey(gmNote))
            {
                return gmNote;
            }
            if (gmNote < 35) return 45; // Bass drum
            if (gmNote > 81) return 56; // Default high percussion -> Cabasa
            return 52;                  // Snare Low
        }
    }
}
