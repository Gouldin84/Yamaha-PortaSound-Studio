using System;
using System.IO;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class JukeboxTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _tempOverridesFile;

        public JukeboxTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"PSS790_JukeboxTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _tempOverridesFile = Path.Combine(_tempDir, "overrides.json");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
        }

        [Fact]
        public void VoiceOverrideStore_SavesAndRetrievesPerSongOverrides()
        {
            var store = new VoiceOverrideStore(_tempOverridesFile);

            var songPath = Path.Combine(AppPaths.GmMidiDir, "Doom", "E1M1.mid");
            Assert.False(store.HasOverrides(songPath));

            // Set custom overrides: Ch 0 -> Voice 15 (Distortion Guitar), Ch 1 -> Voice 84 (Slap Bass)
            store.SetOverride(songPath, 0, 15);
            store.SetOverride(songPath, 1, 84);

            Assert.True(store.HasOverrides(songPath));
            var overrides = store.GetOverrides(songPath);
            Assert.Equal(2, overrides.Count);
            Assert.Equal(15, overrides[0]);
            Assert.Equal(84, overrides[1]);

            // Create a second store instance pointing to the same file to verify disk persistence
            var reloadedStore = new VoiceOverrideStore(_tempOverridesFile);
            Assert.True(reloadedStore.HasOverrides(songPath));
            var reloadedOverrides = reloadedStore.GetOverrides(songPath);
            Assert.Equal(2, reloadedOverrides.Count);
            Assert.Equal(15, reloadedOverrides[0]);
            Assert.Equal(84, reloadedOverrides[1]);

            // Reset overrides
            reloadedStore.ResetOverrides(songPath);
            Assert.False(reloadedStore.HasOverrides(songPath));
            Assert.Empty(reloadedStore.GetOverrides(songPath));
        }

        [Fact]
        public void AppPaths_GetRelativeLibraryPath_FormatsCleanRelativeKeys()
        {
            var internalGm = Path.Combine(AppPaths.BaseMidiDir, "GM Midi", "Doom", "E1M1.mid");
            var rel = AppPaths.GetRelativeLibraryPath(internalGm);
            Assert.Contains("GM Midi", rel);
            Assert.Contains("Doom", rel);
            Assert.Contains("E1M1.mid", rel);

            var externalPath = @"C:\Games\Doom\D_E1M1.mid";
            var extRel = AppPaths.GetRelativeLibraryPath(externalPath);
            Assert.Equal("D_E1M1.mid", extRel);
        }
    }
}
