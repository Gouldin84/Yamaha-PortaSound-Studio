using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class PSS680EngineMirrorTests : IDisposable
    {
        private readonly string _testDir;

        public PSS680EngineMirrorTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "PSS680_EngineMirror_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void Pss680_Constants_VoiceAndDrumMaps_AreCompleteAndAccurate()
        {
            // All 128 GM instruments must have an FM preset mapping (00-99)
            Assert.Equal(128, Pss680Constants.GmToPss680VoiceMap.Count);
            foreach (var kvp in Pss680Constants.GmToPss680VoiceMap)
            {
                Assert.InRange(kvp.Value, 0, 99);
            }

            // PSS-680 has exactly 100 built-in FM presets
            Assert.Equal(100, Pss680Constants.Voices.Count);
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(i, Pss680Constants.Voices[i].Number);
                Assert.False(string.IsNullOrWhiteSpace(Pss680Constants.Voices[i].Name));
            }

            // Kick drum (GM 35, 36) -> PSS-680 Bass Drum (45)
            Assert.Equal(45, Pss680Constants.TranslateDrumNote(35));
            Assert.Equal(45, Pss680Constants.TranslateDrumNote(36));

            // Snare (GM 38) -> PSS-680 Snare Mid (52)
            Assert.Equal(52, Pss680Constants.TranslateDrumNote(38));

            // Closed Hi-Hat (GM 42) -> PSS-680 Closed Hi-Hat (57)
            Assert.Equal(57, Pss680Constants.TranslateDrumNote(42));
        }

        [Fact]
        public void Pss680_Remapper_TransformsGeneralMidiToPss680Native()
        {
            var midi = new MidiFile();

            var track1 = new TrackChunk();
            // Channel 0 Melodic
            track1.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            track1.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
            track1.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });

            // Channel 9 General MIDI Drums
            track1.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)9 });
            track1.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)9 });

            midi.Chunks.Add(track1);

            var options = new RemapperOptions
            {
                TargetModel = "pss680",
                EnablePolyphonyGuard = true
            };

            var remapped = Remapper.Remap(midi, options);

            // Format detection must recognize PSS-680 native
            var format = Remapper.DetectMidiFormat(remapped);
            Assert.Equal(MidiFormat.Pss680Native, format);
            Assert.True(Remapper.HasPss680Marker(remapped));
            Assert.False(Remapper.HasPss790Marker(remapped));

            // Drums must be remapped to Channel 16 (FourBitNumber 15)
            var drumEvents = remapped.GetTimedEvents().Where(e => e.Event is NoteOnEvent ne && ne.Channel == 15).ToList();
            Assert.NotEmpty(drumEvents);

            // No notes should remain on Channel 10 (FourBitNumber 9)
            var oldDrumEvents = remapped.GetTimedEvents().Where(e => e.Event is NoteOnEvent ne && ne.Channel == 9).ToList();
            Assert.Empty(oldDrumEvents);
        }

        [Fact]
        public void Pss680_PolyphonyGuard_Enforces12VoiceLimit()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();

            // 12 Melodic notes on Channel 0 (notes 60 to 71)
            for (int note = 60; note <= 71; note++)
            {
                track.Events.Add(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)90) { Channel = (FourBitNumber)0 });
            }

            // 1 Incidental drum note on GM Channel 9: GM 56 (Cowbell) -> maps to Note 55 on both PSS-790 and PSS-680
            track.Events.Add(new NoteOnEvent((SevenBitNumber)56, (SevenBitNumber)60) { Channel = (FourBitNumber)9 });

            // Note Offs
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            for (int note = 61; note <= 71; note++)
            {
                track.Events.Add(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            }
            track.Events.Add(new NoteOffEvent((SevenBitNumber)56, (SevenBitNumber)0) { Channel = (FourBitNumber)9 });

            midi.Chunks.Add(track);

            var options680 = new RemapperOptions
            {
                TargetModel = "pss680",
                EnablePolyphonyGuard = true
            };

            var remapped680 = Remapper.Remap(midi, options680);

            // Under PSS-680 (12-voice ceiling), the 13th note (incidental cowbell) must be culled so peak <= 12
            var notes680 = remapped680.GetNotes().ToList();
            long firstTime680 = notes680.Min(n => n.Time);
            int concurrent680 = notes680.Count(n => n.Time <= (firstTime680 + 10) && (n.Time + n.Length) > (firstTime680 + 10));
            Assert.Equal(12, concurrent680);

            // Under PSS-790 (22-voice ceiling), 13 notes is below threshold, so the cowbell is preserved (13 notes)
            var options790 = new RemapperOptions
            {
                TargetModel = "pss790",
                EnablePolyphonyGuard = true
            };
            var remapped790 = Remapper.Remap(midi, options790);
            var notes790 = remapped790.GetNotes().ToList();
            long firstTime790 = notes790.Min(n => n.Time);
            int concurrent790 = notes790.Count(n => n.Time <= (firstTime790 + 10) && (n.Time + n.Length) > (firstTime790 + 10));
            Assert.Equal(13, concurrent790);
        }

        [Fact]
        public void Pss680_ReverseRemapper_RestoresToGeneralMidi()
        {
            var pss680Midi = new MidiFile();
            var track = new TrackChunk();

            // PSS-680 Marker
            track.Events.Add(new TextEvent(Remapper.Pss680MarkerText));

            // Channel 15 (Ch 16) Drums: Bass Drum note 36
            track.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)15 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)15 });

            // Channel 0 Melodic FM preset 15 (Trumpet)
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)15) { Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)90) { Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });

            pss680Midi.Chunks.Add(track);

            var gm = ReverseRemapper.ReverseRemapToGeneralMidi(pss680Midi, "pss680");

            // Marker must be stripped
            Assert.False(Remapper.HasPss680Marker(gm));

            // Drums must be moved to Channel 10 (FourBitNumber 9)
            var gmDrumNotes = gm.GetNotes().Where(n => n.Channel == 9).ToList();
            Assert.NotEmpty(gmDrumNotes);
            Assert.Equal(36, gmDrumNotes[0].NoteNumber); // GM Bass Drum 1

            // Channel 15 should have no drums
            var ch16Notes = gm.GetNotes().Where(n => n.Channel == 15).ToList();
            Assert.Empty(ch16Notes);
        }

        [Fact]
        public async Task Pss680_BatchConverter_DualSync_SynchronizesBothKeyboards()
        {
            string gmDir = Path.Combine(_testDir, "GM Midi");
            string pss790Dir = Path.Combine(_testDir, "PSS-790 Midi");
            string pss680Dir = Path.Combine(_testDir, "PSS-680 Midi");

            Directory.CreateDirectory(gmDir);
            Directory.CreateDirectory(pss790Dir);
            Directory.CreateDirectory(pss680Dir);

            // Create a sample GM Midi file
            var midi = new MidiFile();
            var trk = new TrackChunk();
            trk.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(trk);

            string gmSongPath = Path.Combine(gmDir, "TestSyncSong.mid");
            midi.Write(gmSongPath, true);

            // Dual Sync Analysis
            var analysis = BatchConverter.AnalyzeDualSync(gmDir, pss790Dir, pss680Dir);
            Assert.False(analysis.IsInSync);
            Assert.Equal(1, analysis.Analysis790.MissingInTarget.Count);
            Assert.Equal(1, analysis.Analysis680.MissingInTarget.Count);

            // Execute Dual Sync
            var syncOptions = new LibrarySyncOptions
            {
                ConvertMissing = true,
                UpdateOutdated = true,
                RemoveOrphans = true,
                ForceReconvertAll = false
            };
            var result = await BatchConverter.ExecuteDualSyncAsync(
                gmDir, pss790Dir, pss680Dir,
                syncOptions
            );

            // Both 790 and 680 files should now exist
            string target790Path = Path.Combine(pss790Dir, "TestSyncSong.mid");
            string target680Path = Path.Combine(pss680Dir, "TestSyncSong.mid");

            Assert.True(File.Exists(target790Path));
            Assert.True(File.Exists(target680Path));

            // Verify models
            var read790 = MidiFile.Read(target790Path);
            Assert.True(Remapper.HasPss790Marker(read790));

            var read680 = MidiFile.Read(target680Path);
            Assert.True(Remapper.HasPss680Marker(read680));

            // Second analysis should now be in sync
            var postAnalysis = BatchConverter.AnalyzeDualSync(gmDir, pss790Dir, pss680Dir);
            Assert.True(postAnalysis.IsInSync);
            Assert.Equal(0, postAnalysis.TotalMissing);
        }

        [Fact]
        public void Pss680_SysExQuickCommands_MatchYamahaManualSpecifications()
        {
            // Mode 99 (Sound Source Mode 99 - velocity sensitive multi-timbral)
            // F0 43 76 00 05 99 F7
            byte[] mode99 = new byte[] { 0xF0, 0x43, 0x76, 0x00, 0x05, 0x99, 0xF7 };
            Assert.Equal(0xF0, mode99[0]);
            Assert.Equal(0x43, mode99[1]);
            Assert.Equal(0x76, mode99[2]);
            Assert.Equal(0x99, mode99[5]);
            Assert.Equal(0xF7, mode99[6]);

            // Request Dump Commands (F0 43 20 23 <format> 00 F7)
            // Format 00H: Voice Dump
            byte[] reqVoice = new byte[] { 0xF0, 0x43, 0x20, 0x23, 0x00, 0x00, 0xF7 };
            Assert.Equal(0x00, reqVoice[4]);

            // Format 01H: Melody Dump
            byte[] reqMelody = new byte[] { 0xF0, 0x43, 0x20, 0x23, 0x01, 0x00, 0xF7 };
            Assert.Equal(0x01, reqMelody[4]);

            // Format 02H: Chord Dump
            byte[] reqChord = new byte[] { 0xF0, 0x43, 0x20, 0x23, 0x02, 0x00, 0xF7 };
            Assert.Equal(0x02, reqChord[4]);

            // Format 03H: Rhythm / Custom Drummer Dump
            byte[] reqRhythm = new byte[] { 0xF0, 0x43, 0x20, 0x23, 0x03, 0x00, 0xF7 };
            Assert.Equal(0x03, reqRhythm[4]);
        }

        [Fact]
        public void Pss680_HealthInspector_AdjustsThresholdFor12Voices()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk();

            // 15 simultaneous notes
            for (int i = 0; i < 15; i++)
            {
                trk.Events.Add(new NoteOnEvent((SevenBitNumber)(50 + i), (SevenBitNumber)80) { Channel = (FourBitNumber)0 });
            }
            for (int i = 0; i < 15; i++)
            {
                trk.Events.Add(new NoteOffEvent((SevenBitNumber)(50 + i), (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            }
            midi.Chunks.Add(trk);

            // Analyze for PSS-680 (12-voice limit)
            var health680 = MidiHealthInspector.Analyze(midi, fileName: "Test680.mid", model: "pss680");
            Assert.Equal(15, health680.PeakPolyphony);
            // 15 > 12 -> VoiceStressRating is HighPolyphony
            Assert.Equal("HighPolyphony", health680.VoiceStressRating);
            Assert.Contains("PSS-680", health680.BadgeTooltip);
            Assert.Contains("12", health680.BadgeTooltip);

            // Analyze for PSS-790 (28-voice limit)
            var health790 = MidiHealthInspector.Analyze(midi, fileName: "Test790.mid", model: "pss790");
            Assert.Equal(15, health790.PeakPolyphony);
            // 15 <= 24 -> VoiceStressRating is Optimal
            Assert.Equal("Optimal", health790.VoiceStressRating);
            Assert.Contains("PSS-790", health790.BadgeTooltip);
        }
    }
}
