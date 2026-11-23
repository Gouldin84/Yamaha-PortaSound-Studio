using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PSS790Player.Core
{
    public static class AppPaths
    {
        public static string DefaultBaseMidiDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Midi"
        );

        public static string BaseMidiDir
        {
            get
            {
                var custom = AppSettings.CustomLibraryRoot;
                if (!string.IsNullOrWhiteSpace(custom))
                {
                    return custom;
                }
                return DefaultBaseMidiDir;
            }
        }

        public static void SetCustomLibraryRoot(string? path)
        {
            AppSettings.CustomLibraryRoot = path;
        }

        public static string GmMidiDir => Path.Combine(BaseMidiDir, "GM Midi");
        public static string Pss790MidiDir => Path.Combine(BaseMidiDir, "PSS-790 Midi");
        public static string Pss680MidiDir => Path.Combine(BaseMidiDir, "PSS-680 Midi");
        public static string SysExDir => Path.Combine(BaseMidiDir, "SysEx Library");
        public static string FmVoicesDir => Path.Combine(SysExDir, "PSS-680 Voices");
        public static string VectorPresetsDir => Path.Combine(SysExDir, "PSS-790 Vectors");
        public static string RecordingsDir => Path.Combine(BaseMidiDir, "Recordings");
        public static string MetadataDir => Path.Combine(BaseMidiDir, ".pss790");
        public static string OverridesFilePath => Path.Combine(MetadataDir, "overrides.json");
        public static string FavoritesFilePath => Path.Combine(MetadataDir, "favourites.json");

        public static string GetModelMidiDir(string model) =>
            string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase) ? Pss680MidiDir : Pss790MidiDir;

        private static string EnsureTrailingSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;

        private static bool IsSameOrSubPath(string candidateFullPath, string rootFullPath)
        {
            if (string.Equals(candidateFullPath, rootFullPath, StringComparison.OrdinalIgnoreCase))
                return true;

            return candidateFullPath.StartsWith(EnsureTrailingSeparator(rootFullPath), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStrictSubPath(string candidateFullPath, string rootFullPath)
        {
            return !string.Equals(candidateFullPath, rootFullPath, StringComparison.OrdinalIgnoreCase) &&
                   candidateFullPath.StartsWith(EnsureTrailingSeparator(rootFullPath), StringComparison.OrdinalIgnoreCase);
        }

        public static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(BaseMidiDir))
                Directory.CreateDirectory(BaseMidiDir);

            if (!Directory.Exists(GmMidiDir))
                Directory.CreateDirectory(GmMidiDir);

            if (!Directory.Exists(Pss790MidiDir))
                Directory.CreateDirectory(Pss790MidiDir);

            if (!Directory.Exists(Pss680MidiDir))
                Directory.CreateDirectory(Pss680MidiDir);

            if (!Directory.Exists(SysExDir))
                Directory.CreateDirectory(SysExDir);

            if (!Directory.Exists(FmVoicesDir))
                Directory.CreateDirectory(FmVoicesDir);

            if (!Directory.Exists(VectorPresetsDir))
                Directory.CreateDirectory(VectorPresetsDir);

            if (!Directory.Exists(RecordingsDir))
                Directory.CreateDirectory(RecordingsDir);

            if (!Directory.Exists(MetadataDir))
            {
                var dirInfo = Directory.CreateDirectory(MetadataDir);
                try
                {
                    dirInfo.Attributes |= FileAttributes.Hidden;
                }
                catch { }
            }

            // Seed factory FM voices and Vector presets (cached per active library root)
            SoundDesignSeeder.SeedAll();
        }

        public static void SyncMirroredDirectories(string? gmDir = null, string? pss790Dir = null, string? pss680Dir = null)
        {
            var gm = gmDir ?? GmMidiDir;
            var pss790 = pss790Dir ?? Pss790MidiDir;
            var pss680 = pss680Dir ?? Pss680MidiDir;

            if (gmDir == null && pss790Dir == null && pss680Dir == null)
            {
                EnsureDirectoriesExist();
            }

            try
            {
                var libraryDirs = new[] { gm, pss790, pss680 };

                foreach (var source in libraryDirs)
                {
                    if (!Directory.Exists(source)) continue;
                    foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(source, dir);
                        foreach (var targetBase in libraryDirs)
                        {
                            if (string.Equals(source, targetBase, StringComparison.OrdinalIgnoreCase)) continue;
                            var target = Path.Combine(targetBase, rel);
                            if (!Directory.Exists(target))
                            {
                                Directory.CreateDirectory(target);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static string GetRelativeLibraryPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;

            try
            {
                var fullNorm = Path.GetFullPath(fullPath);
                var baseNorm = Path.GetFullPath(BaseMidiDir);
                if (IsSameOrSubPath(fullNorm, baseNorm))
                {
                    return Path.GetRelativePath(baseNorm, fullNorm);
                }
            }
            catch { }

            return Path.GetFileName(fullPath);
        }

        public static string GetLibrarySubPath(string fullPath, string? gmDir = null, string? pss790Dir = null, string? pss680Dir = null)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;

            try
            {
                var clean = fullPath.Replace('\\', '/');
                var match = System.Text.RegularExpressions.Regex.Match(clean, @"^(?:.*?/)?(?:GM Midi|PSS-790 Midi|PSS-680 Midi)/(?<subpath>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups["subpath"].Value.Replace('\\', '/');
                }

                if (Path.IsPathRooted(fullPath))
                {
                    var full = Path.GetFullPath(fullPath);
                    var gm = Path.GetFullPath(gmDir ?? GmMidiDir);
                    var pss790 = Path.GetFullPath(pss790Dir ?? Pss790MidiDir);
                    var pss680 = Path.GetFullPath(pss680Dir ?? Pss680MidiDir);

                    if (IsSameOrSubPath(full, gm))
                    {
                        return Path.GetRelativePath(gm, full).Replace('\\', '/');
                    }
                    if (IsSameOrSubPath(full, pss790))
                    {
                        return Path.GetRelativePath(pss790, full).Replace('\\', '/');
                    }
                    if (IsSameOrSubPath(full, pss680))
                    {
                        return Path.GetRelativePath(pss680, full).Replace('\\', '/');
                    }
                }
                else
                {
                    return clean.TrimStart('/');
                }
            }
            catch { }

            return Path.GetFileName(fullPath).Replace('\\', '/');
        }

        public static string ResolveInLibrary(string librarySubPath, string libraryRoot)
        {
            if (string.IsNullOrWhiteSpace(librarySubPath)) return string.Empty;
            var normalized = librarySubPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var rootFull = Path.GetFullPath(libraryRoot);
            var combinedFull = Path.GetFullPath(Path.Combine(rootFull, normalized));

            if (!IsSameOrSubPath(combinedFull, rootFull))
            {
                throw new ArgumentException("Resolved library path escapes target library root.", nameof(librarySubPath));
            }

            return combinedFull;
        }

        public static bool IsPathInsideLibrary(string path, string? gmDir = null, string? pss790Dir = null, string? pss680Dir = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                var full = Path.GetFullPath(path);
                var gm = Path.GetFullPath(gmDir ?? GmMidiDir);
                var pss790 = Path.GetFullPath(pss790Dir ?? Pss790MidiDir);
                var pss680 = Path.GetFullPath(pss680Dir ?? Pss680MidiDir);

                return IsStrictSubPath(full, gm) ||
                       IsStrictSubPath(full, pss790) ||
                       IsStrictSubPath(full, pss680);
            }
            catch
            {
                return false;
            }
        }

        public static string? FindMatchingOptimizedFile(string gmFilePath, string targetModel = "pss790", string? gmDir = null, string? pssDir = null)
        {
            return FindMatchingLinkedFile(gmFilePath, targetModel, gmDir, pssDir);
        }

        public static string? FindMatchingLinkedFile(string filePath, string targetModel = "pss790", string? customGmDir = null, string? customPssDir = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            try
            {
                var fullNorm = Path.GetFullPath(filePath);
                var gmNorm = Path.GetFullPath(customGmDir ?? GmMidiDir);
                var pss680Norm = Path.GetFullPath(Pss680MidiDir);
                var pss790Norm = Path.GetFullPath(Pss790MidiDir);

                string effectiveModel = targetModel;
                if (customPssDir == null)
                {
                    if (IsSameOrSubPath(fullNorm, pss680Norm))
                    {
                        effectiveModel = "pss680";
                    }
                    else if (IsSameOrSubPath(fullNorm, pss790Norm))
                    {
                        effectiveModel = "pss790";
                    }
                }

                var targetPssNorm = Path.GetFullPath(customPssDir ?? GetModelMidiDir(effectiveModel));
                var is680 = string.Equals(effectiveModel, "pss680", StringComparison.OrdinalIgnoreCase);
                var suffix = is680 ? "_PSS680" : "_PSS790";

                if (IsSameOrSubPath(fullNorm, gmNorm))
                {
                    var rel = Path.GetRelativePath(gmNorm, fullNorm);
                    var subDir = Path.GetDirectoryName(rel) ?? "";
                    var fileNameNoExt = Path.GetFileNameWithoutExtension(rel);
                    var ext = Path.GetExtension(rel);

                    var cand1 = Path.Combine(targetPssNorm, rel);
                    if (File.Exists(cand1)) return cand1;

                    var cand2 = Path.Combine(targetPssNorm, subDir, $"{fileNameNoExt}{suffix}{ext}");
                    if (File.Exists(cand2)) return cand2;
                }
                else if (IsSameOrSubPath(fullNorm, targetPssNorm))
                {
                    var rel = Path.GetRelativePath(targetPssNorm, fullNorm);
                    var subDir = Path.GetDirectoryName(rel) ?? "";
                    var fileNameNoExt = Path.GetFileNameWithoutExtension(rel);
                    var ext = Path.GetExtension(rel);

                    var cand1 = Path.Combine(gmNorm, rel);
                    if (File.Exists(cand1)) return cand1;

                    if (fileNameNoExt.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var origName = fileNameNoExt.Substring(0, fileNameNoExt.Length - suffix.Length);
                        var cand2 = Path.Combine(gmNorm, subDir, $"{origName}{ext}");
                        if (File.Exists(cand2)) return cand2;
                    }
                }
            }
            catch { }

            return null;
        }

        public static List<string> FindAllMatchingLinkedFiles(string filePath, string? customGmDir = null)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(filePath)) return results;

            try
            {
                var fullNorm = Path.GetFullPath(filePath);
                var gmNorm = Path.GetFullPath(customGmDir ?? GmMidiDir);

                if (IsSameOrSubPath(fullNorm, gmNorm))
                {
                    var link790 = FindMatchingLinkedFile(fullNorm, "pss790", customGmDir);
                    if (!string.IsNullOrEmpty(link790) && File.Exists(link790))
                    {
                        results.Add(link790);
                    }

                    var link680 = FindMatchingLinkedFile(fullNorm, "pss680", customGmDir);
                    if (!string.IsNullOrEmpty(link680) && File.Exists(link680) && !results.Contains(link680, StringComparer.OrdinalIgnoreCase))
                    {
                        results.Add(link680);
                    }
                }
                else
                {
                    var linkGm = FindMatchingLinkedFile(fullNorm, customGmDir: customGmDir);
                    if (!string.IsNullOrEmpty(linkGm) && File.Exists(linkGm))
                    {
                        results.Add(linkGm);
                        var isSource680 = IsSameOrSubPath(fullNorm, Path.GetFullPath(Pss680MidiDir));
                        var otherModel = isSource680 ? "pss790" : "pss680";
                        var otherLink = FindMatchingLinkedFile(linkGm, otherModel, customGmDir);
                        if (!string.IsNullOrEmpty(otherLink) && File.Exists(otherLink) && !results.Contains(otherLink, StringComparer.OrdinalIgnoreCase))
                        {
                            results.Add(otherLink);
                        }
                    }
                }
            }
            catch { }

            return results;
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

        public static void OpenSysExFolderInExplorer()
        {
            EnsureDirectoriesExist();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{SysExDir}\"",
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
