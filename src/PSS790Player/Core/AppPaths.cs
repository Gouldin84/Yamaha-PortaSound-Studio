using System;
using System.Diagnostics;
using System.IO;

namespace PSS790Player.Core
{
    public static class AppPaths
    {
        public static string BaseMidiDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Midi"
        );

        public static string GmMidiDir => Path.Combine(BaseMidiDir, "GM Midi");
        public static string Pss790MidiDir => Path.Combine(BaseMidiDir, "PSS-790 Midi");
        public static string MetadataDir => Path.Combine(BaseMidiDir, ".pss790");
        public static string OverridesFilePath => Path.Combine(MetadataDir, "overrides.json");

        public static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(BaseMidiDir))
                Directory.CreateDirectory(BaseMidiDir);

            if (!Directory.Exists(GmMidiDir))
                Directory.CreateDirectory(GmMidiDir);

            if (!Directory.Exists(Pss790MidiDir))
                Directory.CreateDirectory(Pss790MidiDir);

            if (!Directory.Exists(MetadataDir))
            {
                var dirInfo = Directory.CreateDirectory(MetadataDir);
                // Optionally mark hidden on Windows
                try
                {
                    dirInfo.Attributes |= FileAttributes.Hidden;
                }
                catch { }
            }
        }

        public static string GetRelativeLibraryPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;

            try
            {
                var fullNorm = Path.GetFullPath(fullPath);
                var baseNorm = Path.GetFullPath(BaseMidiDir);
                if (fullNorm.StartsWith(baseNorm, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetRelativePath(baseNorm, fullNorm);
                }
            }
            catch { }

            return Path.GetFileName(fullPath);
        }

        public static bool IsPathInsideLibrary(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                var full = Path.GetFullPath(path);
                var gm = Path.GetFullPath(GmMidiDir);
                var pss = Path.GetFullPath(Pss790MidiDir);

                // Must be inside GM Midi or PSS-790 Midi, and not equal to the root folder itself
                var insideGm = full.StartsWith(gm, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, gm, StringComparison.OrdinalIgnoreCase);
                var insidePss = full.StartsWith(pss, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, pss, StringComparison.OrdinalIgnoreCase);

                return insideGm || insidePss;
            }
            catch
            {
                return false;
            }
        }

        public static string? FindMatchingOptimizedFile(string gmFilePath)
        {
            if (string.IsNullOrWhiteSpace(gmFilePath)) return null;

            try
            {
                var fullNorm = Path.GetFullPath(gmFilePath);
                var gmNorm = Path.GetFullPath(GmMidiDir);

                if (!fullNorm.StartsWith(gmNorm, StringComparison.OrdinalIgnoreCase))
                    return null;

                var rel = Path.GetRelativePath(gmNorm, fullNorm);
                var subDir = Path.GetDirectoryName(rel) ?? "";
                var fileNameNoExt = Path.GetFileNameWithoutExtension(rel);
                var ext = Path.GetExtension(rel);

                // Candidate 1: Exact relative path in PSS-790 Midi
                var cand1 = Path.Combine(Pss790MidiDir, rel);
                if (File.Exists(cand1)) return cand1;

                // Candidate 2: Appended _PSS790 in corresponding subfolder
                var cand2 = Path.Combine(Pss790MidiDir, subDir, $"{fileNameNoExt}_PSS790{ext}");
                if (File.Exists(cand2)) return cand2;
            }
            catch { }

            return null;
        }

        public static void OpenFolderInExplorer(string? path = null)
        {
            EnsureDirectoriesExist();
            var target = !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : BaseMidiDir;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{target}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public static void RevealInExplorer(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;

            try
            {
                if (File.Exists(fullPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{fullPath}\"",
                        UseShellExecute = true
                    });
                }
                else if (Directory.Exists(fullPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{fullPath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }
    }
}
