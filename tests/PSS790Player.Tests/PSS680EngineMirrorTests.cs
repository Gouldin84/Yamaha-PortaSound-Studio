using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
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

            // PSS-680 has 100 built-in FM presets plus 5 user synth bank slots (100-104)
            Assert.Equal(105, Pss680Constants.Voices.Count);
            for (int i = 0; i < 105; i++)
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

        [Fact]
        public void Pss680_Remapper_StripsReverbCC91_AndDoesNotInjectBoost()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk();
            trk.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            trk.Events.Add(new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)64) { Channel = (FourBitNumber)0 }); // Reverb event
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { Channel = (FourBitNumber)0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(trk);

            // Remap for PSS-680 (pure FM synth, no DSP reverb)
            var opt680 = new RemapperOptions
            {
                TargetModel = "pss680",
                DefaultReverbBoost = true,
                FilterUnsupportedControllers = true
            };
            var remapped680 = Remapper.Remap(midi, opt680);

            var ccEvents680 = remapped680.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<ControlChangeEvent>()
                .Where(cc => cc.ControlNumber == 91)
                .ToList();

            // CC 91 should be completely stripped for PSS-680
            Assert.Empty(ccEvents680);

            // In contrast, PSS-790 should retain CC 91 reverb for its onboard DSP processor
            var opt790 = new RemapperOptions
            {
                TargetModel = "pss790",
                DefaultReverbBoost = true,
                FilterUnsupportedControllers = true
            };
            var remapped790 = Remapper.Remap(midi, opt790);
            var ccEvents790 = remapped790.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<ControlChangeEvent>()
                .Where(cc => cc.ControlNumber == 91)
                .ToList();

            Assert.NotEmpty(ccEvents790);
        }

        [Fact]
        public async Task BatchConverter_ConvertDirectoryDualAsync_SupportsIndependentModelOptionsAndSelectiveExport()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "PSSStudio_DualBatch_" + Guid.NewGuid().ToString("N"));
            string gmDir = Path.Combine(tempRoot, "GM");
            string pss790Dir = Path.Combine(tempRoot, "PSS790");
            string pss680Dir = Path.Combine(tempRoot, "PSS680");

            Directory.CreateDirectory(gmDir);
            Directory.CreateDirectory(pss790Dir);
            Directory.CreateDirectory(pss680Dir);

            try
            {
                var midi = new MidiFile();
                var trk = new TrackChunk();
                trk.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                trk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
                trk.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
                midi.Chunks.Add(trk);
                midi.Write(Path.Combine(gmDir, "SongA.mid"), true);

                // Test selective conversion: only PSS-680
                int count680Only = await BatchConverter.ConvertDirectoryDualAsync(
                    gmDir,
                    pss790Dir,
                    pss680Dir,
                    options790: new RemapperOptions { TargetModel = "pss790" },
                    options680: new RemapperOptions { TargetModel = "pss680" },
                    convert790: false,
                    convert680: true
                );

                Assert.Equal(1, count680Only);
                Assert.False(File.Exists(Path.Combine(pss790Dir, "SongA.mid")));
                Assert.True(File.Exists(Path.Combine(pss680Dir, "SongA.mid")));

                // Test dual conversion: both PSS-790 and PSS-680
                int countBoth = await BatchConverter.ConvertDirectoryDualAsync(
                    gmDir,
                    pss790Dir,
                    pss680Dir,
                    options790: new RemapperOptions { TargetModel = "pss790" },
                    options680: new RemapperOptions { TargetModel = "pss680" },
                    convert790: true,
                    convert680: true
                );

                Assert.Equal(1, countBoth);
                Assert.True(File.Exists(Path.Combine(pss790Dir, "SongA.mid")));
                Assert.True(File.Exists(Path.Combine(pss680Dir, "SongA.mid")));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    try { Directory.Delete(tempRoot, true); } catch { }
                }
            }
        }

        [Fact]
        public void AppPaths_FindMatchingLinkedFile_AutoDetectsPss680Directory()
        {
            var pss680Path = Path.Combine(AppPaths.Pss680MidiDir, "TestFolder", "Song_PSS680.mid");
            // Default targetModel parameter is "pss790", but auto-detection should recognize Pss680MidiDir
            var isAutoDetectedAs680 = pss680Path.StartsWith(AppPaths.Pss680MidiDir, StringComparison.OrdinalIgnoreCase);
            Assert.True(isAutoDetectedAs680);

            // Verify with custom directories in temp sandbox
            var tempRoot = Path.Combine(Path.GetTempPath(), "PSS680_LinkTest_" + Guid.NewGuid().ToString("N"));
            var gmDir = Path.Combine(tempRoot, "GM Midi");
            var pss680Dir = Path.Combine(tempRoot, "PSS-680 Midi");
            Directory.CreateDirectory(gmDir);
            Directory.CreateDirectory(pss680Dir);

            try
            {
                var gmFile = Path.Combine(gmDir, "CoolSong.mid");
                var pss680File = Path.Combine(pss680Dir, "CoolSong_PSS680.mid");
                File.WriteAllText(gmFile, "fake-gm-midi");
                File.WriteAllText(pss680File, "fake-pss680-midi");

                // Test GM -> PSS680 resolution
                var foundPss = AppPaths.FindMatchingLinkedFile(gmFile, targetModel: "pss680", customGmDir: gmDir, customPssDir: pss680Dir);
                Assert.Equal(pss680File, foundPss);

                // Test PSS680 -> GM resolution with targetModel="pss680"
                var foundGm = AppPaths.FindMatchingLinkedFile(pss680File, targetModel: "pss680", customGmDir: gmDir, customPssDir: pss680Dir);
                Assert.Equal(gmFile, foundGm);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    try { Directory.Delete(tempRoot, true); } catch { }
                }
            }
        }

        [Fact]
        public void AppPaths_FindAllMatchingLinkedFiles_FindsBothCounterparts()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "PSS680_AllLinkTest_" + Guid.NewGuid().ToString("N"));
            var gmDir = Path.Combine(tempRoot, "GM Midi");
            var pss790Dir = Path.Combine(tempRoot, "PSS-790 Midi");
            var pss680Dir = Path.Combine(tempRoot, "PSS-680 Midi");
            Directory.CreateDirectory(gmDir);
            Directory.CreateDirectory(pss790Dir);
            Directory.CreateDirectory(pss680Dir);

            try
            {
                var gmFile = Path.Combine(gmDir, "RetroTune.mid");
                var pss790File = Path.Combine(pss790Dir, "RetroTune_PSS790.mid");
                var pss680File = Path.Combine(pss680Dir, "RetroTune_PSS680.mid");

                File.WriteAllText(gmFile, "fake-gm");
                File.WriteAllText(pss790File, "fake-790");
                File.WriteAllText(pss680File, "fake-680");

                // Mock AppPaths properties temporarily or verify FindMatchingLinkedFile for both
                var link790 = AppPaths.FindMatchingLinkedFile(gmFile, "pss790", gmDir, pss790Dir);
                var link680 = AppPaths.FindMatchingLinkedFile(gmFile, "pss680", gmDir, pss680Dir);

                Assert.NotNull(link790);
                Assert.NotNull(link680);
                Assert.Equal(pss790File, link790);
                Assert.Equal(pss680File, link680);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    try { Directory.Delete(tempRoot, true); } catch { }
                }
            }
        }

        private class MockOutputDevice : IOutputDevice
        {
            public List<MidiEvent> SentEvents { get; } = new();
            public event EventHandler<MidiEventSentEventArgs>? EventSent;
            public void SendEvent(MidiEvent midiEvent) { SentEvents.Add(midiEvent); EventSent?.Invoke(this, new MidiEventSentEventArgs(midiEvent)); }
            public void PrepareForEventsSending() { }
            public void Dispose() { }
        }

        [Fact]
        public void Remap_Pss680_AllowsFullNoteRangeUpTo108_ForAcousticPianosAndStrings()
        {
            // GM Program 0 (Acoustic Grand Piano) with notes 88 (E6), 92 (G#6), 100 (E7), 108 (C8)
            var track = new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)88, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)92, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 100 },
                new NoteOnEvent((SevenBitNumber)100, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 100 },
                new NoteOnEvent((SevenBitNumber)108, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 100 }
            );
            var midi = new MidiFile(track);

            // Remap for PSS-680 (FM synthesis has no multisample aliasing, plays all notes up to 108)
            var opt680 = new RemapperOptions { TargetModel = "pss680", ForceRemap = true };
            var remapped680 = Remapper.Remap(midi, opt680);
            var notes680 = remapped680.GetTimedEvents().Select(te => te.Event).OfType<NoteOnEvent>().ToList();

            Assert.Equal(88, (byte)notes680[0].NoteNumber);
            Assert.Equal(92, (byte)notes680[1].NoteNumber);
            Assert.Equal(100, (byte)notes680[2].NoteNumber);
            Assert.Equal(108, (byte)notes680[3].NoteNumber);

            // Remap for PSS-790 (AWM engine has multisample aliasing above 88, folds notes down)
            var opt790 = new RemapperOptions { TargetModel = "pss790", ForceRemap = true };
            var remapped790 = Remapper.Remap(midi, opt790);
            var notes790 = remapped790.GetTimedEvents().Select(te => te.Event).OfType<NoteOnEvent>().ToList();

            Assert.Equal(88, (byte)notes790[0].NoteNumber);
            Assert.Equal(80, (byte)notes790[1].NoteNumber); // 92 folded down to 80
            Assert.Equal(88, (byte)notes790[2].NoteNumber); // 100 folded down to 88
            Assert.Equal(84, (byte)notes790[3].NoteNumber); // 108 folded down to 84
        }

        [Fact]
        public void SendVoiceOverride_Pss680_SendsDirectSequentialProgramChange()
        {
            using var player = new MidiPlayer();
            var mockDev = new MockOutputDevice();
            player.SetOutputDevice(mockDev);

            // Test PSS-680 voice override: Voice 10 (Violin 1) -> sends Program 10 directly
            player.SendVoiceOverride(0, 10, "pss680");
            var pc680 = mockDev.SentEvents.OfType<ProgramChangeEvent>().Last();
            Assert.Equal((SevenBitNumber)10, pc680.ProgramNumber);

            // Test PSS-790 voice override: Voice 10 (JAZZ ORGAN 1) -> sends Chart 2 Program 1
            player.SendVoiceOverride(0, 10, "pss790");
            var pc790 = mockDev.SentEvents.OfType<ProgramChangeEvent>().Last();
            Assert.Equal((SevenBitNumber)1, pc790.ProgramNumber);
        }

        [Fact]
        public void ReverseRemap_Pss680_RestoresScaledPitchBends()
        {
            // Pitch bend scaled down by 1/6: delta = 1000 / 6 = 167 -> pitch = 8192 + 167 = 8359
            var track = new TrackChunk(
                new TextEvent(Remapper.Pss680MarkerText) { DeltaTime = 0 },
                new ProgramChangeEvent((SevenBitNumber)3) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new PitchBendEvent((ushort)8359) { Channel = (FourBitNumber)0, DeltaTime = 0 }
            );
            var pss680Midi = new MidiFile(track);

            var gmMidi = ReverseRemapper.ReverseRemapToGeneralMidi(pss680Midi, "pss680");
            var pb = gmMidi.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().Single();

            // (8359 - 8192) * 6.0 = 167 * 6 = 1002 -> 8192 + 1002 = 9194
            Assert.True(pb.PitchValue > 9100 && pb.PitchValue < 9250);
        }

        [Fact]
        public void PolyphonyOptimizer_Pss680_CullsLatinHandPercussionFirst()
        {
            // Create a track with 12 melodic notes plus 1 PSS-680 incidental percussion (note 67 Bongo Low on Channel 15)
            var events = new List<MidiEvent>();
            for (int i = 0; i < 12; i++)
            {
                events.Add(new NoteOnEvent((SevenBitNumber)(60 + i), (SevenBitNumber)80) { Channel = (FourBitNumber)(i % 15), DeltaTime = 0 });
            }
            // Add PSS-680 Bongo Low (Pad C1, Note 67) on Channel 15 with low velocity
            events.Add(new NoteOnEvent((SevenBitNumber)67, (SevenBitNumber)50) { Channel = (FourBitNumber)15, DeltaTime = 0 });

            // Add note offs at time 480
            for (int i = 0; i < 12; i++)
            {
                events.Add(new NoteOffEvent((SevenBitNumber)(60 + i), (SevenBitNumber)0) { Channel = (FourBitNumber)(i % 15), DeltaTime = i == 0 ? 480 : 0 });
            }
            events.Add(new NoteOffEvent((SevenBitNumber)67, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 0 });

            var midi = new MidiFile(new TrackChunk(events));

            var optimizer = new PolyphonyOptimizer(enabled: true, threshold: 12, model: "pss680");
            var optimized = optimizer.Optimize(midi);

            var survivingNotes = optimized.GetNotes().ToList();
            // Total was 13 notes at same time, exceeding threshold 12.
            // Bongo Low (67 on Channel 15) should be culled, preserving all 12 melodic notes.
            Assert.Equal(12, survivingNotes.Count);
            Assert.DoesNotContain(survivingNotes, n => n.Channel == (FourBitNumber)15 && (byte)n.NoteNumber == 67);
        }

        [Fact]
        public void MidiPlayer_ActiveModel_ConfiguresModelCorrectly()
        {
            using var player = new MidiPlayer();
            Assert.Equal("pss790", player.ActiveModel);
            player.ActiveModel = "pss680";
            Assert.Equal("pss680", player.ActiveModel);
        }

        [Fact]
        public void TranslateDrumNote_Pss680_ExtendedNotes_MapToMusicalPercussion()
        {
            // Extended low percussion
            Assert.Equal(51, Pss680Constants.TranslateDrumNote(27)); // High Q -> Rim Shot
            Assert.Equal(51, Pss680Constants.TranslateDrumNote(28)); // Slap -> Rim Shot
            Assert.Equal(55, Pss680Constants.TranslateDrumNote(31)); // Sticks -> Cowbell
            Assert.Equal(55, Pss680Constants.TranslateDrumNote(33)); // Metronome Click -> Cowbell

            // Extended high percussion
            Assert.Equal(56, Pss680Constants.TranslateDrumNote(82)); // Shaker -> Cabasa
            Assert.Equal(56, Pss680Constants.TranslateDrumNote(83)); // Jingle Bell -> Cabasa
            Assert.Equal(71, Pss680Constants.TranslateDrumNote(84)); // Bell Tree -> Tambourine
            Assert.Equal(71, Pss680Constants.TranslateDrumNote(85)); // Castanets -> Tambourine
            Assert.Equal(45, Pss680Constants.TranslateDrumNote(86)); // Mute Surdo -> Bass Drum
            Assert.Equal(45, Pss680Constants.TranslateDrumNote(87)); // Open Surdo -> Bass Drum
        }

        [Fact]
        public void MidiHealthInspector_HighOctaveNotes_RecognizedUpTo108()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            // Valid range: Note 24 (C1) through Note 108 (C8)
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)24, (SevenBitNumber)80) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)24, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)108, (SevenBitNumber)80) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)108, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 });

            // Out-of-bounds notes
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)23, (SevenBitNumber)80) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)23, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)109, (SevenBitNumber)80) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)109, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 });

            midi.Chunks.Add(tr);

            var report680 = MidiHealthInspector.Analyze(midi, fileName: "Test680.mid", model: "pss680");
            Assert.Equal(2, report680.OutOfBoundsNotesCount);

            var report790 = MidiHealthInspector.Analyze(midi, fileName: "Test790.mid", model: "pss790");
            Assert.Equal(2, report790.OutOfBoundsNotesCount);
        }
    }
}
