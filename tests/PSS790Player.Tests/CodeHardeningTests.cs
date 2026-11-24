using System;
using System.IO;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Core;
using PSS790Player.Bridge;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class CodeHardeningTests : IDisposable
    {
        private readonly string _testBaseDir;
        private readonly string _sysExDir;
        private readonly string _recordingsDir;
        private readonly string _outsideDir;

        public CodeHardeningTests()
        {
            _testBaseDir = Path.Combine(Path.GetTempPath(), "PSS790_HardeningTests_" + Guid.NewGuid().ToString("N"));
            _sysExDir = Path.Combine(_testBaseDir, "SysEx");
            _recordingsDir = Path.Combine(_testBaseDir, "Recordings");
            _outsideDir = Path.Combine(Path.GetTempPath(), "PSS790_Outside_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_sysExDir);
            Directory.CreateDirectory(_recordingsDir);
            Directory.CreateDirectory(_outsideDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testBaseDir))
                    Directory.Delete(_testBaseDir, recursive: true);
                if (Directory.Exists(_outsideDir))
                    Directory.Delete(_outsideDir, recursive: true);
            }
            catch { }
        }

        #region AppPaths Boundary Checks

        [Fact]
        public void AppPaths_IsPathInsideDirectory_ValidatesContainmentAccurately()
        {
            var parent = Path.Combine(_testBaseDir, "SafeFolder");
            Directory.CreateDirectory(parent);

            var validChild = Path.Combine(parent, "Sub", "song.mid");
            var exactMatch = parent;
            var siblingPrefix = Path.Combine(_testBaseDir, "SafeFolder_Evil", "hacked.mid");
            var traversalAttempt = Path.Combine(parent, "..", "escape.mid");
            var outsideFile = Path.Combine(_outsideDir, "target.txt");

            Assert.True(AppPaths.IsPathInsideDirectory(validChild, parent));
            Assert.True(AppPaths.IsPathInsideDirectory(exactMatch, parent));
            Assert.False(AppPaths.IsPathInsideDirectory(siblingPrefix, parent));
            Assert.False(AppPaths.IsPathInsideDirectory(traversalAttempt, parent));
            Assert.False(AppPaths.IsPathInsideDirectory(outsideFile, parent));
            Assert.False(AppPaths.IsPathInsideDirectory("", parent));
            Assert.False(AppPaths.IsPathInsideDirectory(validChild, ""));
            Assert.False(AppPaths.IsPathInsideDirectory(null!, parent));
        }

        #endregion

        #region SysExManager Security Bounds

        [Fact]
        public void SysExManager_DeleteDump_RejectsFilesOutsideSysExDir()
        {
            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            var manager = new SysExManager(player, recorder);

            // Create an innocent file outside the SysEx folder
            var sensitiveFile = Path.Combine(_outsideDir, "important_system_config.txt");
            File.WriteAllText(sensitiveFile, "DO NOT DELETE");

            bool deleted = manager.DeleteDump(sensitiveFile, _sysExDir);

            Assert.False(deleted);
            Assert.True(File.Exists(sensitiveFile), "File outside SysEx directory should NOT be deleted!");
        }

        [Fact]
        public void SysExManager_RenameDump_RejectsFilesOutsideSysExDir()
        {
            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            var manager = new SysExManager(player, recorder);

            var externalFile = Path.Combine(_outsideDir, "external.syx");
            File.WriteAllText(externalFile, "test data");

            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                manager.RenameDump(externalFile, "renamed.syx", _sysExDir);
            });
        }

        #endregion

        #region MidiRecorder Security Bounds

        [Fact]
        public void MidiRecorder_DeleteRecording_RejectsFilesOutsideRecordingsDir()
        {
            using var recorder = new MidiRecorder();

            var foreignFile = Path.Combine(_outsideDir, "foreign_take.mid");
            File.WriteAllText(foreignFile, "midi bytes");

            bool deleted = recorder.DeleteRecording(foreignFile, _recordingsDir);

            Assert.False(deleted);
            Assert.True(File.Exists(foreignFile), "File outside Recordings directory should NOT be deleted!");
        }

        [Fact]
        public void MidiRecorder_RenameRecording_RejectsFilesOutsideRecordingsDir()
        {
            using var recorder = new MidiRecorder();

            var foreignFile = Path.Combine(_outsideDir, "foreign_take.mid");
            File.WriteAllText(foreignFile, "midi bytes");

            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                recorder.RenameRecording(foreignFile, "hacked_take.mid", _recordingsDir);
            });
        }

        #endregion

        #region Crash Logging Diagnostic Hardening

        [Fact]
        public void App_WriteCrashLog_GeneratesFormattedDiagnosticLog()
        {
            var testEx = new InvalidOperationException("Simulated test crash for hardening verification", new ArgumentException("Inner test fault"));
            App.WriteCrashLog("UnitTest.CodeHardening", testEx, isFatal: false);

            var logPath = App.GetCrashLogPath();
            Assert.True(File.Exists(logPath), "Crash log should exist on disk");

            var logContent = File.ReadAllText(logPath);
            Assert.Contains("UnitTest.CodeHardening", logContent);
            Assert.Contains("Simulated test crash for hardening verification", logContent);
            Assert.Contains("Inner test fault", logContent);
            Assert.Contains("Runtime   : .NET", logContent);
        }

        #endregion

        #region WebUI Channel 16 Layout & Scrubber Token Verification

        [Fact]
        public void WebUI_Assets_ContainHardenedChannel16AndScrubberSignatures()
        {
            var appJsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "PSS790Player", "WebUI", "app.js");
            if (!File.Exists(appJsPath))
            {
                appJsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebUI", "app.js");
            }

            if (File.Exists(appJsPath))
            {
                var content = File.ReadAllText(appJsPath);
                Assert.Contains("🥁 DRUMS", content);
                Assert.Contains("Fixed Map", content);
                Assert.Contains("window.addEventListener(\"error\"", content);
                Assert.Contains("window.addEventListener(\"unhandledrejection\"", content);
                Assert.DoesNotContain("scrubberEl.addEventListener(\"change\", finishScrubbing)", content);
            }

            var cssPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "PSS790Player", "WebUI", "styles.css");
            if (!File.Exists(cssPath))
            {
                cssPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebUI", "styles.css");
            }

            if (File.Exists(cssPath))
            {
                var cssContent = File.ReadAllText(cssPath);
                Assert.Contains(".channel-strip-card", cssContent);
                Assert.Contains("min-width: 0;", cssContent);
                Assert.Contains("overflow: hidden;", cssContent);
            }
        }

        #endregion
    }
}
