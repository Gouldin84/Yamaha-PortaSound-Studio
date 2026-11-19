using System;
using System.IO;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class LibrarySyncTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _gmDir;
        private readonly string _pssDir;
        private readonly string _overridesPath;
        private readonly VoiceOverrideStore _store;

        public LibrarySyncTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "PSS790_SyncTests_" + Guid.NewGuid().ToString("N"));
            _gmDir = Path.Combine(_tempRoot, "GM Midi");
            _pssDir = Path.Combine(_tempRoot, "PSS-790 Midi");
            _overridesPath = Path.Combine(_tempRoot, "overrides.json");

            Directory.CreateDirectory(_gmDir);
            Directory.CreateDirectory(_pssDir);
            _store = new VoiceOverrideStore(_overridesPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, true);
                }
            }
            catch { }
        }

        private void CreateDummyMidi(string filePath, byte note = 60)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);
            midi.Write(filePath, true);
        }

        [Fact]
        public void AnalyzeSync_DetectsMissingOutdatedAndOrphans()
        {
            // GM has Song1 and Song2 (in subfolder)
            var gmSong1 = Path.Combine(_gmDir, "Song1.mid");
            var gmSong2 = Path.Combine(_gmDir, "Sub", "Song2.mid");
            CreateDummyMidi(gmSong1);
            CreateDummyMidi(gmSong2);

            // PSS has Song1 (older) and an Orphan (in another subfolder)
            var pssSong1 = Path.Combine(_pssDir, "Song1.mid");
            var pssOrphan = Path.Combine(_pssDir, "Legacy", "Orphan.mid");
            CreateDummyMidi(pssSong1);
            CreateDummyMidi(pssOrphan);

            // Set GM Song1 to be newer than PSS Song1
            File.SetLastWriteTimeUtc(pssSong1, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(gmSong1, DateTime.UtcNow);

            var analysis = BatchConverter.AnalyzeSync(_gmDir, _pssDir);

            Assert.False(analysis.IsInSync);
            Assert.Equal(2, analysis.TotalGmFiles);
            Assert.Equal(2, analysis.TotalPssFiles);
            Assert.Contains(Path.Combine("Sub", "Song2.mid"), analysis.MissingInTarget);
            Assert.Contains("Song1.mid", analysis.OutdatedInTarget);
            Assert.Contains(Path.Combine("Legacy", "Orphan.mid"), analysis.OrphanInTarget);
        }

        [Fact]
        public async Task ExecuteSync_ConvertsMissingUpdatesOutdatedAndRemovesOrphans()
        {
            var gmSong1 = Path.Combine(_gmDir, "Pop", "Song1.mid");
            var gmSong2 = Path.Combine(_gmDir, "Rock", "Song2.mid");
            CreateDummyMidi(gmSong1);
            CreateDummyMidi(gmSong2);

            var pssSong1 = Path.Combine(_pssDir, "Pop", "Song1.mid");
            var pssOrphan = Path.Combine(_pssDir, "Old", "Orphan.mid");
            CreateDummyMidi(pssSong1);
            CreateDummyMidi(pssOrphan);

            // Add override for orphan to test cleanup
            _store.SetOverride(pssOrphan, 0, 15);
            Assert.True(_store.HasOverrides(pssOrphan));

            // Set GM Song1 newer
            File.SetLastWriteTimeUtc(pssSong1, DateTime.UtcNow.AddHours(-1));
            File.SetLastWriteTimeUtc(gmSong1, DateTime.UtcNow);

            var options = new LibrarySyncOptions
            {
                ConvertMissing = true,
                UpdateOutdated = true,
                RemoveOrphans = true
            };

            var result = await BatchConverter.ExecuteSyncAsync(_gmDir, _pssDir, options, _store);

            Assert.True(result.Success);
            Assert.Equal(1, result.ConvertedCount); // Song2 converted
            Assert.Equal(1, result.UpdatedCount);   // Song1 updated
            Assert.Equal(1, result.RemovedOrphansCount); // Orphan deleted

            // Verify files on disk
            Assert.True(File.Exists(Path.Combine(_pssDir, "Rock", "Song2.mid")));
            Assert.False(File.Exists(pssOrphan));
            Assert.False(_store.HasOverrides(pssOrphan)); // Orphan voice override cleaned up

            // Orphan directory should be pruned
            Assert.False(Directory.Exists(Path.Combine(_pssDir, "Old")));

            // After sync, both libraries must be 100% in sync
            var postAnalysis = BatchConverter.AnalyzeSync(_gmDir, _pssDir);
            Assert.True(postAnalysis.IsInSync);
            Assert.Equal(2, postAnalysis.TotalGmFiles);
            Assert.Equal(2, postAnalysis.TotalPssFiles);
            Assert.Empty(postAnalysis.MissingInTarget);
            Assert.Empty(postAnalysis.OutdatedInTarget);
            Assert.Empty(postAnalysis.OrphanInTarget);
        }

        [Fact]
        public async Task ExecuteSync_ForceReconvertAll_ReconvertsEveryFile()
        {
            var gmSong1 = Path.Combine(_gmDir, "Song1.mid");
            var gmSong2 = Path.Combine(_gmDir, "Song2.mid");
            CreateDummyMidi(gmSong1);
            CreateDummyMidi(gmSong2);

            var options = new LibrarySyncOptions
            {
                ForceReconvertAll = true
            };

            var result = await BatchConverter.ExecuteSyncAsync(_gmDir, _pssDir, options, _store);

            Assert.True(result.Success);
            Assert.Equal(2, result.UpdatedCount);
            Assert.True(File.Exists(Path.Combine(_pssDir, "Song1.mid")));
            Assert.True(File.Exists(Path.Combine(_pssDir, "Song2.mid")));
        }

        [Fact]
        public void PruneEmptyDirectories_RemovesNestedEmptyDirectoriesBottomUp()
        {
            var nestedEmpty1 = Path.Combine(_gmDir, "EmptyParent", "EmptyChild", "EmptyGrandchild");
            var nestedEmpty2 = Path.Combine(_gmDir, "AnotherEmptyFolder");
            var populatedFolder = Path.Combine(_gmDir, "PopulatedFolder");
            Directory.CreateDirectory(nestedEmpty1);
            Directory.CreateDirectory(nestedEmpty2);
            Directory.CreateDirectory(populatedFolder);
            CreateDummyMidi(Path.Combine(populatedFolder, "Test.mid"));

            var count = BatchConverter.PruneEmptyDirectories(_gmDir);

            Assert.True(count >= 4); // EmptyGrandchild, EmptyChild, EmptyParent, AnotherEmptyFolder
            Assert.False(Directory.Exists(Path.Combine(_gmDir, "EmptyParent")));
            Assert.False(Directory.Exists(nestedEmpty2));
            Assert.True(Directory.Exists(populatedFolder));
            Assert.True(File.Exists(Path.Combine(populatedFolder, "Test.mid")));
        }

        [Fact]
        public async Task ExecuteDualSync_WithPruneEmpty_CleansAcrossAllThreeRoots()
        {
            var pss680Dir = Path.Combine(_tempRoot, "PSS-680 Midi");
            Directory.CreateDirectory(pss680Dir);

            // Create populated song
            CreateDummyMidi(Path.Combine(_gmDir, "Synthwave", "Song1.mid"));

            // Create empty ghost directories in all 3 roots
            var emptyGm = Path.Combine(_gmDir, "Ghost1");
            var empty790 = Path.Combine(_pssDir, "Ghost2", "SubGhost");
            var empty680 = Path.Combine(pss680Dir, "Ghost3");
            Directory.CreateDirectory(emptyGm);
            Directory.CreateDirectory(empty790);
            Directory.CreateDirectory(empty680);

            var options = new LibrarySyncOptions
            {
                ConvertMissing = true,
                UpdateOutdated = true,
                RemoveOrphans = true,
                PruneEmptyDirectories = true
            };

            var result = await BatchConverter.ExecuteDualSyncAsync(_gmDir, _pssDir, pss680Dir, options, _store);

            Assert.True(result.Success);
            Assert.True(result.PrunedDirectoriesCount >= 4); // Ghost1, SubGhost, Ghost2, Ghost3
            Assert.False(Directory.Exists(emptyGm));
            Assert.False(Directory.Exists(Path.Combine(_pssDir, "Ghost2")));
            Assert.False(Directory.Exists(empty680));
            Assert.True(File.Exists(Path.Combine(_pssDir, "Synthwave", "Song1.mid")));
            Assert.True(File.Exists(Path.Combine(pss680Dir, "Synthwave", "Song1.mid")));
        }

        private void CreateDummyXgMidi(string filePath, byte note = 60)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var midi = new MidiFile();
            var track = new TrackChunk();
            // Yamaha XG System On SysEx: F0 43 10 4C 00 00 7E 00 F7
            track.Events.Add(new NormalSysExEvent(new byte[] { 0x43, 0x10, 0x4C, 0x00, 0x00, 0x7E, 0x00 }));
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);
            midi.Write(filePath, true);
        }

        private void CreateDummyV1RemappedMidi(string filePath, bool is680 = false)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var midi = new MidiFile();
            var track = new TrackChunk();
            string legacyMarker = is680 ? "PSS680_REMAPPED" : "PSS790_REMAPPED";
            track.Events.Add(new TextEvent(legacyMarker));
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);
            midi.Write(filePath, true);
        }

        [Fact]
        public async Task AnalyzeSync_DetectsLegacyXgConversions_FlagsAsOutdatedForUpgrade()
        {
            var gmXgSong = Path.Combine(_gmDir, "XG_Hits", "CoolSong.mid");
            var pssXgSong = Path.Combine(_pssDir, "XG_Hits", "CoolSong.mid");

            CreateDummyXgMidi(gmXgSong);
            CreateDummyV1RemappedMidi(pssXgSong, is680: false);

            // Crucial: Set PSS target write time NEWER than source GM write time
            // Under legacy timestamp-only comparison, this would NOT be detected as outdated!
            File.SetLastWriteTimeUtc(gmXgSong, DateTime.UtcNow.AddHours(-5));
            File.SetLastWriteTimeUtc(pssXgSong, DateTime.UtcNow);

            var analysis = BatchConverter.AnalyzeSync(_gmDir, _pssDir, "pss790");

            Assert.False(analysis.IsInSync);
            Assert.Equal(1, analysis.TotalXgFiles);
            Assert.Single(analysis.XgUpgradesInTarget);
            Assert.Contains(Path.Combine("XG_Hits", "CoolSong.mid"), analysis.XgUpgradesInTarget);
            Assert.Contains(Path.Combine("XG_Hits", "CoolSong.mid"), analysis.OutdatedInTarget);

            // Execute sync to upgrade outdated XG conversions
            var options = new LibrarySyncOptions
            {
                UpdateOutdated = true
            };
            var result = await BatchConverter.ExecuteSyncAsync(_gmDir, _pssDir, options, _store);

            Assert.True(result.Success);
            Assert.Equal(1, result.UpdatedCount);

            // Target file must now have the :v2 algorithm marker embedded
            Assert.True(Remapper.HasAlgorithmV2MarkerFile(pssXgSong, "pss790"));

            // Re-analyzing must show 100% in sync with 0 upgrades remaining
            var postAnalysis = BatchConverter.AnalyzeSync(_gmDir, _pssDir, "pss790");
            Assert.True(postAnalysis.IsInSync);
            Assert.Empty(postAnalysis.OutdatedInTarget);
            Assert.Empty(postAnalysis.XgUpgradesInTarget);
        }

        [Fact]
        public async Task AnalyzeDualSync_DetectsXgUpgradesAcrossBothModels()
        {
            var pss680Dir = Path.Combine(_tempRoot, "PSS-680 Midi");
            Directory.CreateDirectory(pss680Dir);

            var gmSong = Path.Combine(_gmDir, "SonicXG.mid");
            var pss790Song = Path.Combine(_pssDir, "SonicXG.mid");
            var pss680Song = Path.Combine(pss680Dir, "SonicXG.mid");

            CreateDummyXgMidi(gmSong);
            CreateDummyV1RemappedMidi(pss790Song, is680: false);
            CreateDummyV1RemappedMidi(pss680Song, is680: true);

            // Ensure targets have newer timestamps
            File.SetLastWriteTimeUtc(gmSong, DateTime.UtcNow.AddDays(-1));
            File.SetLastWriteTimeUtc(pss790Song, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(pss680Song, DateTime.UtcNow);

            var dual = BatchConverter.AnalyzeDualSync(_gmDir, _pssDir, pss680Dir);

            Assert.False(dual.IsInSync);
            Assert.Equal(1, dual.TotalXgFiles);
            Assert.Equal(2, dual.TotalXgUpgrades);
            Assert.Equal(2, dual.TotalOutdated);

            var options = new LibrarySyncOptions
            {
                UpdateOutdated = true
            };
            var result = await BatchConverter.ExecuteDualSyncAsync(_gmDir, _pssDir, pss680Dir, options, _store);

            Assert.True(result.Success);
            Assert.Equal(2, result.UpdatedCount);

            Assert.True(Remapper.HasAlgorithmV2MarkerFile(pss790Song, "pss790"));
            Assert.True(Remapper.HasAlgorithmV2MarkerFile(pss680Song, "pss680"));

            var postDual = BatchConverter.AnalyzeDualSync(_gmDir, _pssDir, pss680Dir);
            Assert.True(postDual.IsInSync);
            Assert.Equal(0, postDual.TotalXgUpgrades);
            Assert.Equal(0, postDual.TotalOutdated);
        }
    }
}
