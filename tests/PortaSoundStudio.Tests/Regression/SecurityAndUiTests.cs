using System;
using System.IO;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Core;
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

        #region Blazor Hybrid Layout & Hardware Guide Verification

        [Fact]
        public void BlazorHybrid_Assets_ContainHardenedChannel16AndOfflineCssSignatures()
        {
            var srcDir = FindRepoSrcDir();
            Assert.NotNull(srcDir);

            var indexHtml = File.ReadAllText(Path.Combine(srcDir!, "wwwroot", "index.html"));
            Assert.Contains("_framework/blazor.webview.js", indexHtml);
            Assert.Contains("css/tailwind.css", indexHtml);
            Assert.DoesNotContain("cdn.tailwindcss.com", indexHtml);

            var tailwindPath = Path.Combine(srcDir!, "wwwroot", "css", "tailwind.css");
            Assert.True(File.Exists(tailwindPath), "Offline compiled tailwind.css must exist.");
            Assert.True(new FileInfo(tailwindPath).Length > 10_000, "Compiled tailwind.css must be populated.");

            var cssContent = File.ReadAllText(Path.Combine(srcDir!, "wwwroot", "css", "styles.css"));
            Assert.Contains(".channel-strip-card", cssContent);
            Assert.Contains("min-width: 0;", cssContent);
            Assert.Contains("overflow: hidden;", cssContent);

            var mixerContent = File.ReadAllText(Path.Combine(srcDir!, "Components", "Player", "ChannelMixerRack.razor"));
            Assert.Contains("DRUM", mixerContent);
            Assert.Contains("Fixed Rhythm", mixerContent);
        }

        [Fact]
        public void BlazorHybrid_Assets_ContainHardwareCaptureGuideSignatures()
        {
            var srcDir = FindRepoSrcDir();
            Assert.NotNull(srcDir);

            var recContent = File.ReadAllText(Path.Combine(srcDir!, "Components", "Recorder", "RecorderWorkspace.razor"));
            Assert.Contains("studioInterop.saveGuidePreference", recContent);
            Assert.Contains("Loop Guard", recContent);

            var guideContent = File.ReadAllText(Path.Combine(srcDir!, "Components", "Modals", "HardwareGuideModal.razor"));
            Assert.Contains("MEMORY BULK DUMP", guideContent);
            Assert.Contains("Format 00H", guideContent);
            Assert.Contains("PSS-790", guideContent);
            Assert.Contains("PSS-680", guideContent);

            var sysexContent = File.ReadAllText(Path.Combine(srcDir!, "Components", "SoundDesign", "SysExVaultPanel.razor"));
            Assert.Contains("MEMORY BULK DUMP", sysexContent);
            Assert.Contains("SysExManager", sysexContent);
        }

        [Fact]
        public void BlazorHybrid_Assets_ReactiveScopesAndModelFilteringAreWired()
        {
            var srcDir = FindRepoSrcDir();
            Assert.NotNull(srcDir);

            var mainLayout = File.ReadAllText(Path.Combine(srcDir!, "Components", "Layout", "MainLayout.razor"));
            Assert.Contains("@inherits StudioComponentBase", mainLayout);
            Assert.Contains("StateScope.Shell", mainLayout);
            Assert.Contains("ActiveHeaderMenu", mainLayout);

            var headerBar = File.ReadAllText(Path.Combine(srcDir!, "Components", "Layout", "HeaderBar.razor"));
            Assert.Contains("@inherits StudioComponentBase", headerBar);
            Assert.Contains("StateScope.Shell", headerBar);
            Assert.Contains("ActiveHeaderMenu", headerBar);

            var librarySidebar = File.ReadAllText(Path.Combine(srcDir!, "Components", "Player", "LibrarySidebar.razor"));
            Assert.Contains("@inherits StudioComponentBase", librarySidebar);
            Assert.Contains("cat:pss790", librarySidebar);
            Assert.Contains("cat:pss680", librarySidebar);
            Assert.Contains("ActiveModel", librarySidebar);
            Assert.True(librarySidebar.IndexOf("cat:pss680", StringComparison.Ordinal) < librarySidebar.IndexOf("cat:gm", StringComparison.Ordinal),
                "General MIDI section should be rendered at the bottom after PSS-790 / PSS-680 sections.");

            var drumPads = File.ReadAllText(Path.Combine(srcDir!, "Components", "SoundDesign", "DrumPadsDeck.razor"));
            Assert.Contains("@if (is680)", drumPads);
        }

        [Fact]
        public void BlazorHybrid_Assets_IncludeNeoVintageDesignSystemAndVisualizer()
        {
            var srcDir = FindRepoSrcDir();
            Assert.NotNull(srcDir);

            var indexHtml = File.ReadAllText(Path.Combine(srcDir!, "wwwroot", "index.html"));
            Assert.Contains("css/design-system.css", indexHtml);

            var designSystem = File.ReadAllText(Path.Combine(srcDir!, "wwwroot", "css", "design-system.css"));
            Assert.Contains("--vfd-bg:", designSystem);
            Assert.Contains(".v-fader-stage", designSystem);
            Assert.DoesNotContain("slider-vertical", designSystem);
            Assert.Contains("max-height: 760px", designSystem);

            var mixerRack = File.ReadAllText(Path.Combine(srcDir!, "Components", "Player", "ChannelMixerRack.razor"));
            Assert.Contains("initMixerFaders", mixerRack);
            Assert.Contains("data-fader-channel", mixerRack);

            var vizDeck = File.ReadAllText(Path.Combine(srcDir!, "Components", "Player", "MidiVisualizerDeck.razor"));
            Assert.Contains("liveStageCanvas", vizDeck);
            Assert.Contains("16-Ch Spectrum", vizDeck);
            Assert.Contains("Note Matrix", vizDeck);
            Assert.Contains("VFD Scope", vizDeck);
        }

        private static string? FindRepoSrcDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PortaSoundStudio");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        #endregion
    }
}
