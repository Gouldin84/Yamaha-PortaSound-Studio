using System;
using System.IO;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class FileManagementTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _overridesFile;
        private readonly VoiceOverrideStore _store;

        public FileManagementTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PSS790_FileMgmt_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
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
            var gmSub = Path.Combine(AppPaths.GmMidiDir, "Doom", "E1M1.mid");
            var pssSub = Path.Combine(AppPaths.Pss790MidiDir, "Monkey Island", "Theme.mid");
            var rootGm = AppPaths.GmMidiDir;
            var outside = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var traversal = Path.Combine(AppPaths.GmMidiDir, "..", "..", "Windows", "System32");

            Assert.True(AppPaths.IsPathInsideLibrary(gmSub));
            Assert.True(AppPaths.IsPathInsideLibrary(pssSub));
            Assert.False(AppPaths.IsPathInsideLibrary(rootGm));
            Assert.False(AppPaths.IsPathInsideLibrary(outside));
            Assert.False(AppPaths.IsPathInsideLibrary(traversal));
            Assert.False(AppPaths.IsPathInsideLibrary(null!));
            Assert.False(AppPaths.IsPathInsideLibrary(""));
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
            // Create test hierarchy in temp location
            AppPaths.EnsureDirectoriesExist();

            var testSub = "UnitTesting_" + Guid.NewGuid().ToString("N");
            var gmDir = Path.Combine(AppPaths.GmMidiDir, testSub);
            var pssDir = Path.Combine(AppPaths.Pss790MidiDir, testSub);

            Directory.CreateDirectory(gmDir);
            Directory.CreateDirectory(pssDir);

            try
            {
                var gmSong1 = Path.Combine(gmDir, "Song1.mid");
                var pssSong1 = Path.Combine(pssDir, "Song1.mid"); // Exact match
                File.WriteAllText(gmSong1, "dummy");
                File.WriteAllText(pssSong1, "dummy");

                var match1 = AppPaths.FindMatchingOptimizedFile(gmSong1);
                Assert.NotNull(match1);
                Assert.Equal(pssSong1, match1);

                var gmSong2 = Path.Combine(gmDir, "Song2.mid");
                var pssSong2 = Path.Combine(pssDir, "Song2_PSS790.mid"); // Suffix match
                File.WriteAllText(gmSong2, "dummy");
                File.WriteAllText(pssSong2, "dummy");

                var match2 = AppPaths.FindMatchingOptimizedFile(gmSong2);
                Assert.NotNull(match2);
                Assert.Equal(pssSong2, match2);

                var gmSong3 = Path.Combine(gmDir, "Song3.mid");
                File.WriteAllText(gmSong3, "dummy");
                var match3 = AppPaths.FindMatchingOptimizedFile(gmSong3);
                Assert.Null(match3);

                // Reverse check: From PSS-790 to GM
                var revMatch1 = AppPaths.FindMatchingLinkedFile(pssSong1);
                Assert.NotNull(revMatch1);
                Assert.Equal(gmSong1, revMatch1);

                var revMatch2 = AppPaths.FindMatchingLinkedFile(pssSong2);
                Assert.NotNull(revMatch2);
                Assert.Equal(gmSong2, revMatch2);
            }
            finally
            {
                try { if (Directory.Exists(gmDir)) Directory.Delete(gmDir, true); } catch { }
                try { if (Directory.Exists(pssDir)) Directory.Delete(pssDir, true); } catch { }
            }
        }

        [Fact]
        public void ScanLibraryFiles_ProducesValidSerialization()
        {
            var bridge = (PSS790Player.Bridge.UiBridge)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PSS790Player.Bridge.UiBridge));
            var method = typeof(PSS790Player.Bridge.UiBridge).GetMethod("ScanLibraryFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = method!.Invoke(bridge, null);
            var json = System.Text.Json.JsonSerializer.Serialize(result);

            Assert.Contains("\"subFolders\":", json);
            Assert.Contains("\"folders\":", json);
            Assert.Contains("\"files\":", json);
        }

        [Fact]
        public void SyncMirroredDirectories_EnsuresFolderParityBothWays()
        {
            AppPaths.EnsureDirectoriesExist();

            var uniqueId = Guid.NewGuid().ToString("N");
            var gmUniqueDir = Path.Combine(AppPaths.GmMidiDir, "TestMirrorGM_" + uniqueId, "SubFolder");
            var pssUniqueDir = Path.Combine(AppPaths.Pss790MidiDir, "TestMirrorPSS_" + uniqueId, "ChildFolder");

            Directory.CreateDirectory(gmUniqueDir);
            Directory.CreateDirectory(pssUniqueDir);

            try
            {
                // Run sync
                AppPaths.SyncMirroredDirectories();

                // Target mirrored directories should now exist
                var expectedPssCounterpart = Path.Combine(AppPaths.Pss790MidiDir, "TestMirrorGM_" + uniqueId, "SubFolder");
                var expectedGmCounterpart = Path.Combine(AppPaths.GmMidiDir, "TestMirrorPSS_" + uniqueId, "ChildFolder");

                Assert.True(Directory.Exists(expectedPssCounterpart));
                Assert.True(Directory.Exists(expectedGmCounterpart));
            }
            finally
            {
                try { var d = Path.Combine(AppPaths.GmMidiDir, "TestMirrorGM_" + uniqueId); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
                try { var d = Path.Combine(AppPaths.Pss790MidiDir, "TestMirrorGM_" + uniqueId); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
                try { var d = Path.Combine(AppPaths.GmMidiDir, "TestMirrorPSS_" + uniqueId); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
                try { var d = Path.Combine(AppPaths.Pss790MidiDir, "TestMirrorPSS_" + uniqueId); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            }
        }
    }
}
