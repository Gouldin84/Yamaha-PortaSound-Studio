using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Core;

namespace PSS790Player.Core
{
    public class LibrarySyncAnalysis
    {
        public List<string> MissingInTarget { get; set; } = new();
        public List<string> OutdatedInTarget { get; set; } = new();
        public List<string> OrphanInTarget { get; set; } = new();
        public int TotalGmFiles { get; set; }
        public int TotalPssFiles { get; set; }
        public bool IsInSync => MissingInTarget.Count == 0 && OutdatedInTarget.Count == 0 && OrphanInTarget.Count == 0;
    }

    public class LibrarySyncOptions
    {
        public bool ConvertMissing { get; set; } = true;
        public bool UpdateOutdated { get; set; } = true;
        public bool RemoveOrphans { get; set; } = true;
        public bool ForceReconvertAll { get; set; } = false;
        public RemapperOptions? RemapperOptions { get; set; }
    }

    public class LibrarySyncResult
    {
        public int ConvertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int RemovedOrphansCount { get; set; }
        public int TotalOperations => ConvertedCount + UpdatedCount + RemovedOrphansCount;
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

    public class DualLibrarySyncAnalysis
    {
        public LibrarySyncAnalysis Analysis790 { get; set; } = new();
        public LibrarySyncAnalysis Analysis680 { get; set; } = new();

        public int TotalGmFiles => Analysis790.TotalGmFiles;
        public int TotalPss790Files => Analysis790.TotalPssFiles;
        public int TotalPss680Files => Analysis680.TotalPssFiles;
        public int TotalMissing => Analysis790.MissingInTarget.Count + Analysis680.MissingInTarget.Count;
        public int TotalOutdated => Analysis790.OutdatedInTarget.Count + Analysis680.OutdatedInTarget.Count;
        public int TotalOrphans => Analysis790.OrphanInTarget.Count + Analysis680.OrphanInTarget.Count;
        public bool IsInSync => Analysis790.IsInSync && Analysis680.IsInSync;
    }

    public class BatchConverter
    {
        public static async Task<int> ConvertDirectoryAsync(
            string sourceDirectory,
            string targetDirectory,
            RemapperOptions? options = null,
            IProgress<(int Current, int Total, string FileName)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' does not exist.");

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            options ??= new RemapperOptions();
            if (string.IsNullOrEmpty(options.TargetModel))
            {
                options.TargetModel = targetDirectory.Contains("PSS-680", StringComparison.OrdinalIgnoreCase) || targetDirectory.Contains("PSS680", StringComparison.OrdinalIgnoreCase)
                    ? "pss680"
                    : "pss790";
            }

            var files = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int total = files.Count;
            int completed = 0;

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var relPath = Path.GetRelativePath(sourceDirectory, file);
                    var destFile = Path.Combine(targetDirectory, relPath);
                    var destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    try
                    {
                        var sourceMidi = MidiFile.Read(file, Constants.DefaultReadingSettings);
                        var remapped = Remapper.Remap(sourceMidi, options);
                        Remapper.SaveRemapped(remapped, destFile, options.TargetModel);
                    }
                    catch (Exception)
                    {
                        // Continue on corrupted files
                    }

                    completed++;
                    progress?.Report((completed, total, relPath));
                }
            }, cancellationToken);

            return completed;
        }

        public static async Task<int> ConvertDirectoryDualAsync(
            string sourceDirectory,
            string target790Dir,
            string target680Dir,
            RemapperOptions? baseOptions = null,
            IProgress<(int Current, int Total, string FileName)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' does not exist.");

            if (!Directory.Exists(target790Dir))
                Directory.CreateDirectory(target790Dir);

            if (!Directory.Exists(target680Dir))
                Directory.CreateDirectory(target680Dir);

            var files = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int total = files.Count * 2;
            int completed = 0;

            var opt790 = new RemapperOptions
            {
                TargetModel = "pss790",
                EnablePolyphonyGuard = baseOptions?.EnablePolyphonyGuard ?? true,
                DefaultReverbBoost = baseOptions?.DefaultReverbBoost ?? true,
                EmulateSustainPedal = baseOptions?.EmulateSustainPedal ?? true
            };

            var opt680 = new RemapperOptions
            {
                TargetModel = "pss680",
                EnablePolyphonyGuard = baseOptions?.EnablePolyphonyGuard ?? true,
                DefaultReverbBoost = baseOptions?.DefaultReverbBoost ?? true,
                EmulateSustainPedal = baseOptions?.EmulateSustainPedal ?? true
            };

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var relPath = Path.GetRelativePath(sourceDirectory, file);

                    // 1. Convert for PSS-790
                    var dest790 = Path.Combine(target790Dir, relPath);
                    var dir790 = Path.GetDirectoryName(dest790);
                    if (!string.IsNullOrEmpty(dir790) && !Directory.Exists(dir790)) Directory.CreateDirectory(dir790);

                    // 2. Convert for PSS-680
                    var dest680 = Path.Combine(target680Dir, relPath);
                    var dir680 = Path.GetDirectoryName(dest680);
                    if (!string.IsNullOrEmpty(dir680) && !Directory.Exists(dir680)) Directory.CreateDirectory(dir680);

                    try
                    {
                        var sourceMidi = MidiFile.Read(file, Constants.DefaultReadingSettings);

                        var remap790 = Remapper.Remap(sourceMidi, opt790);
                        Remapper.SaveRemapped(remap790, dest790, "pss790");
                        completed++;
                        progress?.Report((completed, total, $"{relPath} (PSS-790)"));

                        var remap680 = Remapper.Remap(sourceMidi, opt680);
                        Remapper.SaveRemapped(remap680, dest680, "pss680");
                        completed++;
                        progress?.Report((completed, total, $"{relPath} (PSS-680)"));
                    }
                    catch (Exception)
                    {
                        completed += 2;
                        progress?.Report((completed, total, relPath));
                    }
                }
            }, cancellationToken);

            return completed / 2;
        }

        public static LibrarySyncAnalysis AnalyzeSync(string sourceDir, string targetDir)
        {
            var analysis = new LibrarySyncAnalysis();
            if (!Directory.Exists(sourceDir))
                return analysis;

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var sourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => Path.GetRelativePath(sourceDir, f), f => f, StringComparer.OrdinalIgnoreCase);

            var targetFiles = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => Path.GetRelativePath(targetDir, f), f => f, StringComparer.OrdinalIgnoreCase);

            analysis.TotalGmFiles = sourceFiles.Count;
            analysis.TotalPssFiles = targetFiles.Count;

            foreach (var kvp in sourceFiles)
            {
                var relPath = kvp.Key;
                var srcFullPath = kvp.Value;

                if (!targetFiles.TryGetValue(relPath, out var tgtFullPath))
                {
                    analysis.MissingInTarget.Add(relPath);
                }
                else
                {
                    var srcTime = File.GetLastWriteTimeUtc(srcFullPath);
                    var tgtTime = File.GetLastWriteTimeUtc(tgtFullPath);
                    if (srcTime > tgtTime)
                    {
                        analysis.OutdatedInTarget.Add(relPath);
                    }
                }
            }

            foreach (var kvp in targetFiles)
            {
                var relPath = kvp.Key;
                if (!sourceFiles.ContainsKey(relPath))
                {
                    analysis.OrphanInTarget.Add(relPath);
                }
            }

            return analysis;
        }

        public static async Task<LibrarySyncResult> ExecuteSyncAsync(
            string sourceDir,
            string targetDir,
            LibrarySyncOptions? options = null,
            VoiceOverrideStore? overrideStore = null,
            IProgress<(int Current, int Total, string FileName, string Action)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new LibrarySyncOptions();
            overrideStore ??= VoiceOverrideStore.Instance;
            var result = new LibrarySyncResult();

            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException($"Source directory '{sourceDir}' does not exist.");

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            AppPaths.SyncMirroredDirectories();

            var analysis = AnalyzeSync(sourceDir, targetDir);

            var filesToConvert = new List<(string RelPath, string Action)>();

            if (options.ForceReconvertAll)
            {
                var allSource = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                    .Select(f => Path.GetRelativePath(sourceDir, f))
                    .ToList();

                foreach (var rel in allSource)
                {
                    filesToConvert.Add((rel, "reconvert"));
                }
            }
            else
            {
                if (options.ConvertMissing)
                {
                    foreach (var rel in analysis.MissingInTarget)
                    {
                        filesToConvert.Add((rel, "convert_missing"));
                    }
                }

                if (options.UpdateOutdated)
                {
                    foreach (var rel in analysis.OutdatedInTarget)
                    {
                        filesToConvert.Add((rel, "update_outdated"));
                    }
                }
            }

            var orphansToDelete = options.RemoveOrphans ? analysis.OrphanInTarget : new List<string>();
            int totalOps = filesToConvert.Count + orphansToDelete.Count;
            int completedOps = 0;

            await Task.Run(() =>
            {
                // 1. Process Conversions & Updates
                foreach (var item in filesToConvert)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var srcPath = Path.Combine(sourceDir, item.RelPath);
                    var destPath = Path.Combine(targetDir, item.RelPath);
                    var destFolder = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destFolder) && !Directory.Exists(destFolder))
                    {
                        Directory.CreateDirectory(destFolder);
                    }

                    try
                    {
                        var targetModel = options.RemapperOptions?.TargetModel ?? (targetDir.Contains("PSS-680", StringComparison.OrdinalIgnoreCase) ? "pss680" : "pss790");
                        var modelOpts = new RemapperOptions
                        {
                            TargetModel = targetModel,
                            EnablePolyphonyGuard = options.RemapperOptions?.EnablePolyphonyGuard ?? true,
                            DefaultReverbBoost = options.RemapperOptions?.DefaultReverbBoost ?? true,
                            EmulateSustainPedal = options.RemapperOptions?.EmulateSustainPedal ?? true
                        };

                        var sourceMidi = MidiFile.Read(srcPath, Constants.DefaultReadingSettings);
                        var remapped = Remapper.Remap(sourceMidi, modelOpts);
                        Remapper.SaveRemapped(remapped, destPath, targetModel);

                        if (item.Action == "convert_missing")
                            result.ConvertedCount++;
                        else
                            result.UpdatedCount++;
                    }
                    catch
                    {
                        // Continue on file error
                    }

                    completedOps++;
                    progress?.Report((completedOps, totalOps, item.RelPath, item.Action));
                }

                // 2. Process Orphan Deletions
                foreach (var orphanRel in orphansToDelete)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var orphanPath = Path.Combine(targetDir, orphanRel);
                    try
                    {
                        if (File.Exists(orphanPath))
                        {
                            File.Delete(orphanPath);
                            overrideStore.RemoveOverrides(orphanPath);
                            result.RemovedOrphansCount++;
                        }
                    }
                    catch
                    {
                        // Continue on file error
                    }

                    completedOps++;
                    progress?.Report((completedOps, totalOps, orphanRel, "delete_orphan"));
                }

                // 3. Clean up empty directories in targetDir that do not exist in sourceDir
                try
                {
                    var targetDirs = Directory.GetDirectories(targetDir, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length) // deepest first
                        .ToList();

                    foreach (var td in targetDirs)
                    {
                        var relDir = Path.GetRelativePath(targetDir, td);
                        var srcMatchingDir = Path.Combine(sourceDir, relDir);
                        if (!Directory.Exists(srcMatchingDir))
                        {
                            if (Directory.GetFiles(td, "*", SearchOption.AllDirectories).Length == 0)
                            {
                                Directory.Delete(td, true);
                            }
                        }
                    }
                }
                catch
                {
                    // Non-critical directory cleanup error
                }
            }, cancellationToken);

            return result;
        }

        public static DualLibrarySyncAnalysis AnalyzeDualSync(string sourceDir, string target790Dir, string target680Dir)
        {
            return new DualLibrarySyncAnalysis
            {
                Analysis790 = AnalyzeSync(sourceDir, target790Dir),
                Analysis680 = AnalyzeSync(sourceDir, target680Dir)
            };
        }

        public static async Task<LibrarySyncResult> ExecuteDualSyncAsync(
            string sourceDir,
            string target790Dir,
            string target680Dir,
            LibrarySyncOptions? options = null,
            VoiceOverrideStore? overrideStore = null,
            IProgress<(int Current, int Total, string FileName, string Action)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var combinedResult = new LibrarySyncResult();

            // 1. Sync PSS-790 library
            var opt790 = new LibrarySyncOptions
            {
                ConvertMissing = options?.ConvertMissing ?? true,
                UpdateOutdated = options?.UpdateOutdated ?? true,
                RemoveOrphans = options?.RemoveOrphans ?? true,
                ForceReconvertAll = options?.ForceReconvertAll ?? false,
                RemapperOptions = new RemapperOptions { TargetModel = "pss790" }
            };

            var res790 = await ExecuteSyncAsync(sourceDir, target790Dir, opt790, overrideStore, progress, cancellationToken);
            combinedResult.ConvertedCount += res790.ConvertedCount;
            combinedResult.UpdatedCount += res790.UpdatedCount;
            combinedResult.RemovedOrphansCount += res790.RemovedOrphansCount;

            if (cancellationToken.IsCancellationRequested) return combinedResult;

            // 2. Sync PSS-680 library
            var opt680 = new LibrarySyncOptions
            {
                ConvertMissing = options?.ConvertMissing ?? true,
                UpdateOutdated = options?.UpdateOutdated ?? true,
                RemoveOrphans = options?.RemoveOrphans ?? true,
                ForceReconvertAll = options?.ForceReconvertAll ?? false,
                RemapperOptions = new RemapperOptions { TargetModel = "pss680" }
            };

            var res680 = await ExecuteSyncAsync(sourceDir, target680Dir, opt680, overrideStore, progress, cancellationToken);
            combinedResult.ConvertedCount += res680.ConvertedCount;
            combinedResult.UpdatedCount += res680.UpdatedCount;
            combinedResult.RemovedOrphansCount += res680.RemovedOrphansCount;

            return combinedResult;
        }
    }
}
