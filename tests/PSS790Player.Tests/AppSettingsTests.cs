using System;
using System.IO;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class AppSettingsTests : IDisposable
    {
        private readonly string? _originalCustomRoot;

        public AppSettingsTests()
        {
            _originalCustomRoot = AppSettings.CustomLibraryRoot;
        }

        public void Dispose()
        {
            // Restore original state
            AppSettings.CustomLibraryRoot = _originalCustomRoot;
        }

        [Fact]
        public void DefaultLibraryRoot_PointsToUserMusicMidi()
        {
            var defaultPath = AppPaths.DefaultBaseMidiDir;
            Assert.False(string.IsNullOrWhiteSpace(defaultPath));
            Assert.EndsWith("Midi", defaultPath);
        }

        [Fact]
        public void CustomLibraryRoot_CanBeSetAndReset()
        {
            var tempCustomPath = Path.Combine(Path.GetTempPath(), "TestYamahaStudioMidi_" + Guid.NewGuid());
            try
            {
                AppPaths.SetCustomLibraryRoot(tempCustomPath);
                Assert.Equal(tempCustomPath, AppSettings.CustomLibraryRoot);
                Assert.Equal(tempCustomPath, AppPaths.BaseMidiDir);

                // Subdirectories should now branch off the custom root
                Assert.Equal(Path.Combine(tempCustomPath, "GM Midi"), AppPaths.GmMidiDir);
                Assert.Equal(Path.Combine(tempCustomPath, "PSS-790 Midi"), AppPaths.Pss790MidiDir);
                Assert.Equal(Path.Combine(tempCustomPath, "PSS-680 Midi"), AppPaths.Pss680MidiDir);
                Assert.Equal(Path.Combine(tempCustomPath, "SysEx Library"), AppPaths.SysExDir);

                // Reset to default
                AppPaths.SetCustomLibraryRoot(null);
                Assert.Null(AppSettings.CustomLibraryRoot);
                Assert.Equal(AppPaths.DefaultBaseMidiDir, AppPaths.BaseMidiDir);
            }
            finally
            {
                if (Directory.Exists(tempCustomPath))
                {
                    try { Directory.Delete(tempCustomPath, true); } catch { }
                }
            }
        }
    }
}
