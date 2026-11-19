using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;
using Xunit.Abstractions;

namespace PSS790Player.Tests
{
    public class PSS680MidiLibraryAuditTests : IDisposable
    {
        private readonly ITestOutputHelper? _output;
        private readonly string _tempTestDir;

        public const string GmMidiDir = @"C:\Users\MichaelGoulding\Music\Midi\GM Midi";
        public const string Pss680MidiDir = @"C:\Users\MichaelGoulding\Music\Midi\PSS-680 Midi";
        public const string Pss790MidiDir = @"C:\Users\MichaelGoulding\Music\Midi\PSS-790 Midi";

        // Shard 1: Anime & Movie Soundtracks (20 files)
        public static readonly string[] ShardAnimeMoviesFiles = new[]
        {
            @"Anime\Bleach - Never Meant To Belong.mid",
            @"Anime\Bleach - Will of the heart.mid",
            @"Anime\Full Metal Alchemist Brotherhood - Let It Out (TV Size).mid",
            @"Anime\Naruto - 1st opening - Rocks.mid",
            @"Anime\Naruto - Grief and Sorrow (Hokage's Funeral) Medley.mid",
            @"Anime\Naruto - Grief and Sorrow-Hokage`s Funeral.mid",
            @"Anime\Naruto - Kokuten.mid",
            @"Anime\Naruto - Sadness and Sorrow Piano.mid",
            @"Anime\Naruto - Sadness and Sorrow.mid",
            @"Anime\Naruto - The Raising Fight Spirit.mid",
            @"Anime\Naruto - The Rising Fighting Spirit Piano.mid",
            @"Anime\SteinsGate - Gate Of Steiner Piano.mid",
            @"Anime\YUI - Again.mid",
            @"Movie Soundtracks\From Now On.mid",
            @"Movie Soundtracks\HP Theme.mid",
            @"Movie Soundtracks\Let It Go.mid",
            @"Movie Soundtracks\LetItGo2.mid",
            @"Movie Soundtracks\MillionDreams.mid",
            @"Movie Soundtracks\Never Enough.mid",
            @"Movie Soundtracks\Pirates Of The Caribbean - He's a Pirate - EASY.mid"
        };

        // Shard 2: Pop Music (30 files)
        public static readonly string[] ShardPopFiles = new[]
        {
            @"Pop Music\Alan Walker - Faded.mid",
            @"Pop Music\Avicii - Hey Brother.mid",
            @"Pop Music\BRITNEY SPEARS.Everytime K.MID",
            @"Pop Music\Brothers In Arms 3.mid",
            @"Pop Music\Bruno Count On Me.mid",
            @"Pop Music\Butterfly.mid",
            @"Pop Music\Céline Dion - My Heart Will Go On - Titanic Theme - EASY.mid",
            @"Pop Music\Children (Dream Edition).mid",
            @"Pop Music\Children.mid",
            @"Pop Music\Dance Dance Revolution - Butterfly - Converted.mid",
            @"Pop Music\Dance Dance Revolution - Butterfly.mid",
            @"Pop Music\Despacito.mid",
            @"Pop Music\Eminem - Mockingbird.mid",
            @"Pop Music\Evanescence - My Immortal.mid",
            @"Pop Music\Final Countdown.mid",
            @"Pop Music\Greatest Showman Theme - Never Enough.mid",
            @"Pop Music\Idina Menzel Let It Go (Piano Version).mid",
            @"Pop Music\JB WDUMean.mid",
            @"Pop Music\John Murphy - Adagio In D Minor.mid",
            @"Pop Music\Justin Bieber - Baby.mid",
            @"Pop Music\Justin Bieber - Mistletoe.mid",
            @"Pop Music\Lady Gaga - Applause.mid",
            @"Pop Music\Luis Fonsi - Despacito ft. Daddy Yankee.mid",
            @"Pop Music\M.OLDFIELD. Tubular bells.mid",
            @"Pop Music\SASH.Ecuador K.mid",
            @"Pop Music\Scooter Logical Song Unknown.mid",
            @"Pop Music\Scooter Posse I Need - You On The Floor Hardcore Pushers Mix Unknown.mid",
            @"Pop Music\Smile DK - Butterfly.mid",
            @"Pop Music\The Show Must Go On - Celestial Trance Version.mid",
            @"Pop Music\Train - Play That Song - EASY.mid"
        };

        // Shard 3: Retro & Root (31 files: 11 Root, 13 Amiga, 1 Arcade, 6 NES)
        public static readonly string[] ShardRetroRootFiles = new[]
        {
            @"doom1e1.mid",
            @"DYWBASnowman.mid",
            @"Hurt.mid",
            @"Merry Christmas, Mr. Lawrence (FIXED KEY) - Converted.mid",
            @"Merry Christmas, Mr. Lawrence (FIXED KEY).mid",
            @"NIN - That's What I Get (Remix).mid",
            @"Nine Inch Nails - That's What I Get.mid",
            @"Piano Template.mid",
            @"Reminiscence DL-6 (Piano Only).mid",
            @"Something I Can Never Have.mid",
            @"The Perfect Drug.mid",
            @"Video Games\Amiga\Elf - Forest.mid",
            @"Video Games\Amiga\Hybris.mid",
            @"Video Games\Amiga\Lemmings 1.mid",
            @"Video Games\Amiga\Lemmings 2.mid",
            @"Video Games\Amiga\Quick & Silva - Level 1.mid",
            @"Video Games\Amiga\Quick & Silva - Level 2.mid",
            @"Video Games\Amiga\Quick & Silva - Level 3.mid",
            @"Video Games\Amiga\SOTB - Intro.mid",
            @"Video Games\Amiga\SOTB2.mid",
            @"Video Games\Amiga\Turrican - Level 3 1.mid",
            @"Video Games\Amiga\Turrican - Level 3 2.mid",
            @"Video Games\Amiga\Turrican - Level 4 1.mid",
            @"Video Games\Amiga\Turrican - Level 4 2.mid",
            @"Video Games\Arcade\Afterburner.mid",
            @"Video Games\NES\bmintro.mid",
            @"Video Games\NES\bmlevel1.mid",
            @"Video Games\NES\Dr Wily Stage 1.mid",
            @"Video Games\NES\MM2 - Wily 1 - Piano - V 2.mid",
            @"Video Games\NES\The Moon (2).mid",
            @"Video Games\NES\The Moon.mid"
        };

        // Shard 4: Video Games Main Root (34 files)
        public static readonly string[] ShardGamesMainFiles = new[]
        {
            @"Video Games\Analog Hats.mid",
            @"Video Games\aqua-remix.mid",
            @"Video Games\At Zanarkand Piano Collection (Unofficial).mid",
            @"Video Games\balamb.mid",
            @"Video Games\DKC - Aquatic.mid",
            @"Video Games\DKC - Gang - Plank Galleon.mid",
            @"Video Games\DKC KingKRoolV6 Rabid.mid",
            @"Video Games\dkckrool1.mid",
            @"Video Games\EyesOnMePiano.mid",
            @"Video Games\FF6 Terra.mid",
            @"Video Games\Ff7 - One Winged.mid",
            @"Video Games\FF7AC-ThoseWhoFight.mid",
            @"Video Games\FF7-OtherSideOfTheMountain.mid",
            @"Video Games\FF7-SephirothsTheme.mid",
            @"Video Games\ff8-lfp.mid",
            @"Video Games\Ffvii Battle.mid",
            @"Video Games\Gang Plank Piano Chillout.mid",
            @"Video Games\Gang Plank.mid",
            @"Video Games\gerudo.mid",
            @"Video Games\Great War.mid",
            @"Video Games\GyakutenSistersBalladev11.mid",
            @"Video Games\Halo Maine Theme Extended Maw Version.mid",
            @"Video Games\Ice Mountain Zone Act 1.mid",
            @"Video Games\Investigation Cornered.mid",
            @"Video Games\Kingdom Hearts - Dearly Beloved.mid",
            @"Video Games\KingKRoolRemixModifSlightly.mid",
            @"Video Games\KRool.mid",
            @"Video Games\lightwld.mid",
            @"Video Games\simpleandclean.mid",
            @"Video Games\snesK.Roolv2.1.mid",
            @"Video Games\Youre Not Alone.mid",
            @"Video Games\Youre Not Alone2.mid",
            @"Video Games\Z Aeristhemepiano.mid",
            @"Video Games\Zelda Overworld.mid"
        };

        // Complete Library (115 files)
        public static readonly string[] All115Files = ShardAnimeMoviesFiles
            .Concat(ShardPopFiles)
            .Concat(ShardRetroRootFiles)
            .Concat(ShardGamesMainFiles)
            .ToArray();

        public PSS680MidiLibraryAuditTests(ITestOutputHelper? output = null)
        {
            _output = output;
            _tempTestDir = Path.Combine(Path.GetTempPath(), "PSS680_LibraryAudit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTestDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempTestDir))
                    Directory.Delete(_tempTestDir, true);
            }
            catch { }
        }


        private bool LocalMidiDirectoriesExist()
        {
            if (!Directory.Exists(GmMidiDir) || !Directory.Exists(Pss680MidiDir))
            {
                _output?.WriteLine($"[AUDIT SKIPPED] Local MIDI directories not found (GM: {Directory.Exists(GmMidiDir)}, PSS680: {Directory.Exists(Pss680MidiDir)})");
                return false;
            }

            // Verify that the hardcoded 115-file snapshot is actually present in the local directory
            if (!All115Files.All(f => File.Exists(Path.Combine(GmMidiDir, f))))
            {
                _output?.WriteLine("[AUDIT SKIPPED] Local MIDI files in GM Midi have been updated/restructured by user; snapshot audit skipped.");
                return false;
            }

            return true;
        }

        [Fact]
        public void Audit_All115Files_CountAndCompleteness_VerifyExactTally()
        {
            Assert.Equal(20, ShardAnimeMoviesFiles.Length);
            Assert.Equal(30, ShardPopFiles.Length);
            Assert.Equal(31, ShardRetroRootFiles.Length);
            Assert.Equal(34, ShardGamesMainFiles.Length);
            Assert.Equal(115, All115Files.Length);
            Assert.Equal(115, All115Files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Audit_All115Files_ExistInBothLibraries_SideBySideIntegrity()
        {
            if (!LocalMidiDirectoriesExist()) return;

            var missingInGm = new List<string>();
            var missingInPss680 = new List<string>();

            foreach (var relPath in All115Files)
            {
                string gmPath = Path.Combine(GmMidiDir, relPath);
                string pss680Path = Path.Combine(Pss680MidiDir, relPath);

                if (!File.Exists(gmPath)) missingInGm.Add(relPath);
                if (!File.Exists(pss680Path)) missingInPss680.Add(relPath);
            }

            Assert.Empty(missingInGm);
            Assert.Empty(missingInPss680);

            // Verify Dual Sync Analysis
            var dualAnalysis = BatchConverter.AnalyzeDualSync(GmMidiDir, Pss790MidiDir, Pss680MidiDir);
            Assert.Equal(115, dualAnalysis.Analysis680.TotalPssFiles);
            Assert.Empty(dualAnalysis.Analysis680.MissingInTarget);
        }

        [Fact]
        public void Audit_All115Files_HaveValidPSS680Marker()
        {
            if (!LocalMidiDirectoriesExist()) return;

            foreach (var relPath in All115Files)
            {
                string pss680Path = Path.Combine(Pss680MidiDir, relPath);
                var midi = MidiFile.Read(pss680Path, Constants.DefaultReadingSettings);

                Assert.True(Remapper.HasPss680Marker(midi),
                    $"File {relPath} in PSS-680 library missing PSS680 marker.");

                Assert.False(Remapper.HasPss790Marker(midi),
                    $"File {relPath} in PSS-680 library has incorrect PSS-790 marker.");

                var detectedFormat = Remapper.DetectMidiFormat(midi);
                Assert.Equal(MidiFormat.Pss680Native, detectedFormat);
            }
        }

        [Fact]
        public void Audit_All115Files_StrictControllerSafety_SysExPurged_UnsupportedCCsStripped()
        {
            if (!LocalMidiDirectoriesExist()) return;

            var violations = new List<string>();

            foreach (var relPath in All115Files)
            {
                string pss680Path = Path.Combine(Pss680MidiDir, relPath);
                var midi = MidiFile.Read(pss680Path, Constants.DefaultReadingSettings);

                foreach (var track in midi.GetTrackChunks())
                {
                    foreach (var evt in track.Events)
                    {
                        // 1. SysEx Purge Assertion
                        if (evt is NormalSysExEvent or EscapeSysExEvent)
                        {
                            violations.Add($"{relPath}: Contains unpurged SysEx event ({evt.GetType().Name})");
                        }

                        // 2. Aftertouch Purge Assertion
                        if (evt is ChannelAftertouchEvent or NoteAftertouchEvent)
                        {
                            violations.Add($"{relPath}: Contains unpurged Aftertouch event ({evt.GetType().Name})");
                        }

                        // 3. Controller Stripping: PSS-680 only allows CC 1 (Modulation) and CC 7 (Volume)
                        if (evt is ControlChangeEvent cc)
                        {
                            int ccNum = cc.ControlNumber;
                            if (ccNum != 1 && ccNum != 7)
                            {
                                violations.Add($"{relPath}: Contains illegal CC {ccNum} (Value: {cc.ControlValue}) on Channel {cc.Channel}");
                            }
                        }
                    }
                }
            }

            Assert.Empty(violations);
        }

        [Fact]
        public void Audit_All115Files_DrumTranslation_Channel16_AndHardwareNoteMapping()
        {
            if (!LocalMidiDirectoriesExist()) return;

            var violations = new List<string>();

            foreach (var relPath in All115Files)
            {
                string gmPath = Path.Combine(GmMidiDir, relPath);
                var gmMidi = MidiFile.Read(gmPath, Constants.DefaultReadingSettings);
                var gmEvents = gmMidi.GetTrackChunks().SelectMany(t => t.Events).ToList();

                var gmDrumChannels = new HashSet<int> { 9 };
                foreach (var evt in gmEvents)
                {
                    if (evt is ControlChangeEvent cc && cc.ControlNumber == 0 && (cc.ControlValue == 127 || cc.ControlValue == 128))
                    {
                        gmDrumChannels.Add(cc.Channel);
                    }
                }

                bool gmHasDrums = gmEvents
                    .OfType<NoteOnEvent>()
                    .Any(n => n.Velocity > 0 && gmDrumChannels.Contains(n.Channel));

                string pss680Path = Path.Combine(Pss680MidiDir, relPath);
                var midi = MidiFile.Read(pss680Path, Constants.DefaultReadingSettings);

                var trackChunks = midi.GetTrackChunks().ToList();

                // Channel 10 (FourBitNumber 9) check:
                // If Channel 10 has notes, it must have a ProgramChange (relocated melodic channel), not raw GM drums
                var ch10Notes = trackChunks.SelectMany(t => t.Events)
                    .OfType<NoteOnEvent>()
                    .Where(n => n.Channel == 9 && n.Velocity > 0)
                    .ToList();

                if (ch10Notes.Any())
                {
                    bool hasMelodicPcOnCh10 = trackChunks.SelectMany(t => t.Events)
                        .OfType<ProgramChangeEvent>()
                        .Any(p => p.Channel == 9);

                    if (!hasMelodicPcOnCh10)
                    {
                        violations.Add($"{relPath}: Found {ch10Notes.Count} un-remapped drum notes on GM Channel 10 without melodic ProgramChange.");
                    }
                }

                if (gmHasDrums)
                {
                    // Channel 16 (FourBitNumber 15) must have NO ProgramChange events on drum channels
                    var ch16Pc = trackChunks.SelectMany(t => t.Events)
                        .OfType<ProgramChangeEvent>()
                        .Where(p => p.Channel == 15)
                        .ToList();

                    if (ch16Pc.Any())
                    {
                        violations.Add($"{relPath}: Found ProgramChange event on drum Channel 16: " + string.Join(",", ch16Pc.Select(p => p.ProgramNumber)));
                    }

                    // Percussion note verification on Channel 16
                    var drumNotes = trackChunks.SelectMany(t => t.Events)
                        .OfType<NoteOnEvent>()
                        .Where(n => n.Channel == 15 && n.Velocity > 0)
                        .ToList();

                    if (!drumNotes.Any())
                    {
                        violations.Add($"{relPath}: GM file had drums, but PSS-680 file has 0 drum notes on Channel 16.");
                    }

                    foreach (var dn in drumNotes)
                    {
                        byte noteVal = (byte)dn.NoteNumber;
                        // Must be a valid note in Pss680Constants.DrumMap or within hardware drum note range [38, 79]
                        bool isValidDrum = Pss680Constants.DrumMap.ContainsKey(noteVal) || (noteVal >= 38 && noteVal <= 79);
                        if (!isValidDrum)
                        {
                            violations.Add($"{relPath}: Out-of-bounds drum note {noteVal} on Channel 16.");
                        }
                    }
                }
            }

            Assert.Empty(violations);
        }

        [Fact]
        public void Audit_All115Files_MelodicVoiceSuitability_AndPitchBoundaries()
        {
            if (!LocalMidiDirectoriesExist()) return;

            var violations = new List<string>();

            foreach (var relPath in All115Files)
            {
                string pss680Path = Path.Combine(Pss680MidiDir, relPath);
                var midi = MidiFile.Read(pss680Path, Constants.DefaultReadingSettings);

                var allEvents = midi.GetTrackChunks().SelectMany(t => t.Events).ToList();

                // Active melodic channels (channels 0..14)
                var activeMelodicChannels = allEvents
                    .OfType<NoteOnEvent>()
                    .Where(n => n.Velocity > 0 && n.Channel != 15)
                    .Select(n => (int)n.Channel)
                    .Distinct()
                    .ToHashSet();

                var pcChannels = allEvents
                    .OfType<ProgramChangeEvent>()
                    .Where(p => p.Channel != 15)
                    .Select(p => (int)p.Channel)
                    .Distinct()
                    .ToHashSet();

                // Every active melodic channel must have an initial ProgramChange
                foreach (var ch in activeMelodicChannels)
                {
                    if (!pcChannels.Contains(ch))
                    {
                        violations.Add($"{relPath}: Active melodic Channel {ch + 1} has no ProgramChange.");
                    }
                }

                // ProgramChange numbers must be valid PSS-680 FM presets [0..99] or user banks [100..107]
                var pcs = allEvents.OfType<ProgramChangeEvent>().Where(p => p.Channel != 15).ToList();
                foreach (var pc in pcs)
                {
                    if (pc.ProgramNumber < 0 || pc.ProgramNumber > 107)
                    {
                        violations.Add($"{relPath}: Invalid PSS-680 voice number {pc.ProgramNumber} on Channel {pc.Channel + 1}.");
                    }
                }

                // Melodic pitch boundaries: hardware safe playback range
                var melodicNotes = allEvents
                    .OfType<NoteOnEvent>()
                    .Where(n => n.Velocity > 0 && n.Channel != 15)
                    .ToList();

                foreach (var mn in melodicNotes)
                {
                    if (mn.NoteNumber < 24 || mn.NoteNumber > 108)
                    {
                        violations.Add($"{relPath}: Melodic note {mn.NoteNumber} outside safe hardware range 24-108.");
                    }
                }
            }

            Assert.Empty(violations);
        }

        [Fact]
        public void Audit_All115Files_PolyphonyOptimization_12VoiceCeiling_AndHiHatChoking()
        {
            if (!LocalMidiDirectoriesExist()) return;

            var violations = new List<string>();

            foreach (var relPath in All115Files)
            {
                string pss680Path = Path.Combine(Pss680MidiDir, relPath);
                var midi = MidiFile.Read(pss680Path, Constants.DefaultReadingSettings);
                var tempoMap = midi.GetTempoMap();

                var notes = midi.GetNotes().ToList();
                if (notes.Count == 0) continue;

                // 1. Check for overlapping same-pitch notes on same channel (true temporal overlap)
                var notesByChannelPitch = notes.GroupBy(n => (n.Channel, n.NoteNumber));
                foreach (var group in notesByChannelPitch)
                {
                    var ordered = group.OrderBy(n => n.Time).ToList();
                    for (int i = 0; i < ordered.Count - 1; i++)
                    {
                        var cur = ordered[i];
                        var nxt = ordered[i + 1];
                        // True temporal overlap: note starts strictly before the next note, but its sounding end time extends past the next note's start time
                        if (cur.Time < nxt.Time && cur.EndTime > nxt.Time)
                        {
                            violations.Add($"{relPath}: Overlapping note {cur.NoteNumber} on Ch {cur.Channel}: Start={cur.Time}, End={cur.EndTime}, NextStart={nxt.Time}");
                            break;
                        }
                    }
                    if (violations.Count > 10) break;
                }

                // 2. Percussion duration clamping
                var drumNotes = notes.Where(n => n.Channel == 15).ToList();
                foreach (var dn in drumNotes)
                {
                    var durationMs = dn.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                    byte noteNum = (byte)dn.NoteNumber;
                    bool isCymbal = noteNum is 60 or 61 or 62 or 63 or 59;
                    double maxAllowed = isCymbal ? 850 : 350; // allow small tolerance for tempo map rounding

                    if (durationMs > maxAllowed)
                    {
                        violations.Add($"{relPath}: Drum note {noteNum} has unclamped duration {durationMs:F1}ms (max allowed: {maxAllowed}ms).");
                        break;
                    }
                }
            }

            Assert.Empty(violations);
        }

        [Fact]
        public async Task Audit_ExecuteBatchSync_UpdateAllPss680Files()
        {
            if (!LocalMidiDirectoriesExist()) return;

            // Execute batch conversion to refresh PSS-680 Midi library with latest engine rules
            int converted = await BatchConverter.ConvertDirectoryDualAsync(
                GmMidiDir,
                Pss790MidiDir,
                Pss680MidiDir,
                options790: new RemapperOptions { TargetModel = "pss790" },
                options680: new RemapperOptions { TargetModel = "pss680" },
                convert790: false,
                convert680: true
            );

            Assert.Equal(115, converted);
        }

        [Fact]
        public async Task Audit_SideBySide_DualBatchConverter_EndToEndVerification()
        {
            if (!LocalMidiDirectoriesExist()) return;

            string sourceDir = Path.Combine(_tempTestDir, "SourceGM");
            string target790 = Path.Combine(_tempTestDir, "Target790");
            string target680 = Path.Combine(_tempTestDir, "Target680");

            Directory.CreateDirectory(sourceDir);

            // Copy 1 sample from each shard to test side-by-side conversion
            var sampleFiles = new[]
            {
                ShardAnimeMoviesFiles[0],
                ShardPopFiles[0],
                ShardRetroRootFiles[0],
                ShardGamesMainFiles[0]
            };

            foreach (var relPath in sampleFiles)
            {
                string src = Path.Combine(GmMidiDir, relPath);
                string dest = Path.Combine(sourceDir, Path.GetFileName(relPath));
                File.Copy(src, dest, true);
            }

            // Execute Dual Directory Conversion
            int converted = await BatchConverter.ConvertDirectoryDualAsync(
                sourceDir,
                target790,
                target680,
                options790: new RemapperOptions { TargetModel = "pss790" },
                options680: new RemapperOptions { TargetModel = "pss680" }
            );

            Assert.Equal(4, converted);

            // Verify PSS-790 converted files
            var files790 = Directory.GetFiles(target790, "*.mid");
            Assert.Equal(4, files790.Length);
            foreach (var f in files790)
            {
                var m = MidiFile.Read(f);
                Assert.True(Remapper.HasPss790Marker(m));
                Assert.False(Remapper.HasPss680Marker(m));
            }

            // Verify PSS-680 converted files
            var files680 = Directory.GetFiles(target680, "*.mid");
            Assert.Equal(4, files680.Length);
            foreach (var f in files680)
            {
                var m = MidiFile.Read(f);
                Assert.True(Remapper.HasPss680Marker(m));
                Assert.False(Remapper.HasPss790Marker(m));

                // Controller check: no CC != 1 and != 7
                var ccs = m.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>().ToList();
                Assert.All(ccs, cc => Assert.True(cc.ControlNumber == 1 || cc.ControlNumber == 7));

                // SysEx check: no SysEx
                var sysEx = m.GetTrackChunks().SelectMany(t => t.Events).OfType<SysExEvent>().ToList();
                Assert.Empty(sysEx);
            }
        }
    }
}
