using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class FileManagementTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _gmDir;
        private readonly string _pss790Dir;
        private readonly string _pss680Dir;
        private readonly string _overridesFile;
        private readonly VoiceOverrideStore _store;

        public FileManagementTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PSS790_FileMgmt_Tests_" + Guid.NewGuid().ToString("N"));
            _gmDir = Path.Combine(_tempDir, "GM Midi");
            _pss790Dir = Path.Combine(_tempDir, "PSS-790 Midi");
            _pss680Dir = Path.Combine(_tempDir, "PSS-680 Midi");

            Directory.CreateDirectory(_tempDir);
            Directory.CreateDirectory(_gmDir);
            Directory.CreateDirectory(_pss790Dir);
            Directory.CreateDirectory(_pss680Dir);

            _overridesFile = Path.Combine(_tempDir, "overrides.json");
            _store = new VoiceOverrideStore(_overridesFile);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void IsPathInsideLibrary_ValidatesLibraryBoundaries()
        {
            var gmSub = Path.Combine(_gmDir, "Doom", "E1M1.mid");
            var pssSub = Path.Combine(_pss790Dir, "Monkey Island", "Theme.mid");
            var rootGm = _gmDir;
            var outside = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var traversal = Path.Combine(_gmDir, "..", "..", "Windows", "System32");

            Assert.True(AppPaths.IsPathInsideLibrary(gmSub, _gmDir, _pss790Dir, _pss680Dir));
            Assert.True(AppPaths.IsPathInsideLibrary(pssSub, _gmDir, _pss790Dir, _pss680Dir));
            Assert.False(AppPaths.IsPathInsideLibrary(rootGm, _gmDir, _pss790Dir, _pss680Dir));
            Assert.False(AppPaths.IsPathInsideLibrary(outside, _gmDir, _pss790Dir, _pss680Dir));
            Assert.False(AppPaths.IsPathInsideLibrary(traversal, _gmDir, _pss790Dir, _pss680Dir));
            Assert.False(AppPaths.IsPathInsideLibrary(null!, _gmDir, _pss790Dir, _pss680Dir));
            Assert.False(AppPaths.IsPathInsideLibrary("", _gmDir, _pss790Dir, _pss680Dir));
        }

        [Fact]
        public void VoiceOverrideStore_RenameFile_MigratesOverrideKey()
        {
            var oldPath = Path.Combine(AppPaths.GmMidiDir, "Games", "Track1.mid");
            var newPath = Path.Combine(AppPaths.GmMidiDir, "Games", "Track1_Renamed.mid");

            _store.SetOverride(oldPath, 0, 15);
            Assert.True(_store.HasOverrides(oldPath));
            Assert.Equal(15, _store.GetOverrides(oldPath)[0]);

            _store.RenameFile(oldPath, newPath);

            Assert.False(_store.HasOverrides(oldPath));
            Assert.True(_store.HasOverrides(newPath));
            Assert.Equal(15, _store.GetOverrides(newPath)[0]);
        }

        [Fact]
        public void VoiceOverrideStore_RenameFolder_MigratesAllNestedKeys()
        {
            var oldFolder = Path.Combine(AppPaths.GmMidiDir, "Doom");
            var newFolder = Path.Combine(AppPaths.GmMidiDir, "UltimateDoom");

            var song1 = Path.Combine(oldFolder, "E1M1.mid");
            var song2 = Path.Combine(oldFolder, "Sub", "E1M2.mid");

            _store.SetOverride(song1, 0, 42);
            _store.SetOverride(song2, 1, 99);

            _store.RenameFolder(oldFolder, newFolder);

            var newSong1 = Path.Combine(newFolder, "E1M1.mid");
            var newSong2 = Path.Combine(newFolder, "Sub", "E1M2.mid");

            Assert.False(_store.HasOverrides(song1));
            Assert.False(_store.HasOverrides(song2));
            Assert.True(_store.HasOverrides(newSong1));
            Assert.True(_store.HasOverrides(newSong2));
            Assert.Equal(42, _store.GetOverrides(newSong1)[0]);
            Assert.Equal(99, _store.GetOverrides(newSong2)[1]);
        }

        [Fact]
        public void VoiceOverrideStore_RemoveFolderOverrides_CleansUpOrphans()
        {
            var folder = Path.Combine(AppPaths.GmMidiDir, "TestFolder");
            var song = Path.Combine(folder, "Song.mid");

            _store.SetOverride(song, 2, 77);
            Assert.True(_store.HasOverrides(song));

            _store.RemoveFolderOverrides(folder);

            Assert.False(_store.HasOverrides(song));
        }

        [Fact]
        public void FindMatchingOptimizedFile_DetectsExactAndSuffixMatches()
        {
            var testSub = "UnitTesting_" + Guid.NewGuid().ToString("N");
            var gmSubDir = Path.Combine(_gmDir, testSub);
            var pssSubDir = Path.Combine(_pss790Dir, testSub);

            Directory.CreateDirectory(gmSubDir);
            Directory.CreateDirectory(pssSubDir);

            var gmSong1 = Path.Combine(gmSubDir, "Song1.mid");
            var pssSong1 = Path.Combine(pssSubDir, "Song1.mid"); // Exact match
            File.WriteAllText(gmSong1, "dummy");
            File.WriteAllText(pssSong1, "dummy");

            var match1 = AppPaths.FindMatchingOptimizedFile(gmSong1, "pss790", _gmDir, _pss790Dir);
            Assert.NotNull(match1);
            Assert.Equal(pssSong1, match1);

            var gmSong2 = Path.Combine(gmSubDir, "Song2.mid");
            var pssSong2 = Path.Combine(pssSubDir, "Song2_PSS790.mid"); // Suffix match
            File.WriteAllText(gmSong2, "dummy");
            File.WriteAllText(pssSong2, "dummy");

            var match2 = AppPaths.FindMatchingOptimizedFile(gmSong2, "pss790", _gmDir, _pss790Dir);
            Assert.NotNull(match2);
            Assert.Equal(pssSong2, match2);

            var gmSong3 = Path.Combine(gmSubDir, "Song3.mid");
            File.WriteAllText(gmSong3, "dummy");
            var match3 = AppPaths.FindMatchingOptimizedFile(gmSong3, "pss790", _gmDir, _pss790Dir);
            Assert.Null(match3);

            // Reverse check: From PSS-790 to GM
            var revMatch1 = AppPaths.FindMatchingLinkedFile(pssSong1, "pss790", _gmDir, _pss790Dir);
            Assert.NotNull(revMatch1);
            Assert.Equal(gmSong1, revMatch1);

            var revMatch2 = AppPaths.FindMatchingLinkedFile(pssSong2, "pss790", _gmDir, _pss790Dir);
            Assert.NotNull(revMatch2);
            Assert.Equal(gmSong2, revMatch2);
        }

        [Fact]
        public void LibraryService_BuildSnapshotAndCategoryTree_ProducesValidSerialization()
        {
            var state = new PSS790Player.Services.AppStateStore();
            using var library = new PSS790Player.Services.LibraryService(state);
            var snapshot = library.BuildSnapshot();
            var snapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot);

            Assert.Contains("\"SubFolders\":", snapshotJson);
            Assert.Contains("\"AllFiles\":", snapshotJson);

            var allFiles = new List<PSS790Player.Models.MidiFileItem>();
            var tempDir = Path.Combine(Path.GetTempPath(), "PSS790_DtoTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var midFile = Path.Combine(tempDir, "song1.mid");
                File.WriteAllBytes(midFile, new byte[] { 0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0 });

                var tree = PSS790Player.Services.LibraryService.BuildCategoryTree(tempDir, "TestCat", "gm", allFiles);
                Assert.NotNull(tree);
                Assert.Single(allFiles);
                Assert.Equal("song1.mid", allFiles[0].FileName);
                Assert.Equal("TestCat", allFiles[0].Category);
                Assert.Equal("gm", allFiles[0].Format);

                var treeJson = System.Text.Json.JsonSerializer.Serialize(tree);
                Assert.Contains("\"FileName\":\"song1.mid\"", treeJson);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void SyncMirroredDirectories_EnsuresFolderParityBothWays()
        {
            var uniqueId = Guid.NewGuid().ToString("N");
            var gmUniqueDir = Path.Combine(_gmDir, "TestMirrorGM_" + uniqueId, "SubFolder");
            var pssUniqueDir = Path.Combine(_pss790Dir, "TestMirrorPSS_" + uniqueId, "ChildFolder");

            Directory.CreateDirectory(gmUniqueDir);
            Directory.CreateDirectory(pssUniqueDir);

            // Run sync on sandboxed test directories
            AppPaths.SyncMirroredDirectories(_gmDir, _pss790Dir, _pss680Dir);

            // Target mirrored directories should now exist across all 3 libraries
            var expectedPssCounterpart = Path.Combine(_pss790Dir, "TestMirrorGM_" + uniqueId, "SubFolder");
            var expectedGmCounterpart = Path.Combine(_gmDir, "TestMirrorPSS_" + uniqueId, "ChildFolder");
            var expected680Counterpart = Path.Combine(_pss680Dir, "TestMirrorGM_" + uniqueId, "SubFolder");

            Assert.True(Directory.Exists(expectedPssCounterpart));
            Assert.True(Directory.Exists(expectedGmCounterpart));
            Assert.True(Directory.Exists(expected680Counterpart));
        }

        [Fact]
        public void VoiceOverrideStore_BatchRename_MigratesMultipleSongOverrides()
        {
            var oldFolder = Path.Combine(AppPaths.GmMidiDir, "BatchSource");
            var newFolder = Path.Combine(AppPaths.GmMidiDir, "BatchTarget");

            var songA = Path.Combine(oldFolder, "SongA.mid");
            var songB = Path.Combine(oldFolder, "SongB.mid");
            var songC = Path.Combine(oldFolder, "SongC.mid");

            var newSongA = Path.Combine(newFolder, "SongA.mid");
            var newSongB = Path.Combine(newFolder, "SongB.mid");
            var newSongC = Path.Combine(newFolder, "SongC.mid");

            _store.SetOverride(songA, 0, 10);
            _store.SetOverride(songB, 1, 20);
            _store.SetOverride(songC, 2, 30);

            Assert.True(_store.HasOverrides(songA));
            Assert.True(_store.HasOverrides(songB));
            Assert.True(_store.HasOverrides(songC));

            // Perform batch rename
            var batchMoves = new[]
            {
                (oldPath: songA, newPath: newSongA),
                (oldPath: songB, newPath: newSongB),
                (oldPath: songC, newPath: newSongC)
            };

            foreach (var (oldPath, newPath) in batchMoves)
            {
                _store.RenameFile(oldPath, newPath);
            }

            Assert.False(_store.HasOverrides(songA));
            Assert.False(_store.HasOverrides(songB));
            Assert.False(_store.HasOverrides(songC));

            Assert.True(_store.HasOverrides(newSongA));
            Assert.True(_store.HasOverrides(newSongB));
            Assert.True(_store.HasOverrides(newSongC));

            Assert.Equal(10, _store.GetOverrides(newSongA)[0]);
            Assert.Equal(20, _store.GetOverrides(newSongB)[1]);
            Assert.Equal(30, _store.GetOverrides(newSongC)[2]);
        }

        [Fact]
        public void MoveFolder_PreservesNestedSongOverridesAcrossHierarchies()
        {
            var rootFolder = Path.Combine(AppPaths.GmMidiDir, "Level1", "Level2");
            var movedFolder = Path.Combine(AppPaths.GmMidiDir, "Archived", "Level2");

            var nestedSong = Path.Combine(rootFolder, "DeepTrack.mid");
            var expectedMovedSong = Path.Combine(movedFolder, "DeepTrack.mid");

            _store.SetOverride(nestedSong, 5, 88);
            Assert.True(_store.HasOverrides(nestedSong));

            _store.RenameFolder(rootFolder, movedFolder);

            Assert.False(_store.HasOverrides(nestedSong));
            Assert.True(_store.HasOverrides(expectedMovedSong));
            Assert.Equal(88, _store.GetOverrides(expectedMovedSong)[5]);
        }

        [Fact]
        public void BatchDelete_RemovesOverridesAndLinkedFiles()
        {
            var testSub = "BatchDeleteTest_" + Guid.NewGuid().ToString("N");
            var gmSubDir = Path.Combine(_gmDir, testSub);
            var pssSubDir = Path.Combine(_pss790Dir, testSub);

            Directory.CreateDirectory(gmSubDir);
            Directory.CreateDirectory(pssSubDir);

            var gm1 = Path.Combine(gmSubDir, "Track1.mid");
            var pss1 = Path.Combine(pssSubDir, "Track1.mid");
            var gm2 = Path.Combine(gmSubDir, "Track2.mid");
            var pss2 = Path.Combine(pssSubDir, "Track2.mid");

            File.WriteAllText(gm1, "dummy");
            File.WriteAllText(pss1, "dummy");
            File.WriteAllText(gm2, "dummy");
            File.WriteAllText(pss2, "dummy");

            _store.SetOverride(gm1, 0, 12);
            _store.SetOverride(pss1, 0, 15);

            var filesToDelete = new[] { gm1, gm2 };
            foreach (var file in filesToDelete)
            {
                _store.RemoveOverrides(file);
                File.Delete(file);

                var linked = AppPaths.FindMatchingLinkedFile(file, "pss790", _gmDir, _pss790Dir);
                if (!string.IsNullOrEmpty(linked) && File.Exists(linked))
                {
                    _store.RemoveOverrides(linked);
                    File.Delete(linked);
                }
            }

            Assert.False(File.Exists(gm1));
            Assert.False(File.Exists(pss1));
            Assert.False(File.Exists(gm2));
            Assert.False(File.Exists(pss2));

            Assert.False(_store.HasOverrides(gm1));
            Assert.False(_store.HasOverrides(pss1));
        }
    }
}
