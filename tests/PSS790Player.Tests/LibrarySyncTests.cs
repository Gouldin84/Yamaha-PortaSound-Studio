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
    }
}
