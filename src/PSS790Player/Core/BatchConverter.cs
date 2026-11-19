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
        public List<string> XgUpgradesInTarget { get; set; } = new();
        public int TotalGmFiles { get; set; }
        public int TotalPssFiles { get; set; }
        public int TotalXgFiles { get; set; }
        public bool IsInSync => MissingInTarget.Count == 0 && OutdatedInTarget.Count == 0 && OrphanInTarget.Count == 0;
    }

    public class LibrarySyncOptions
    {
        public bool ConvertMissing { get; set; } = true;
        public bool UpdateOutdated { get; set; } = true;
        public bool RemoveOrphans { get; set; } = true;
        public bool PruneEmptyDirectories { get; set; } = true;
        public bool ForceReconvertAll { get; set; } = false;
        public RemapperOptions? RemapperOptions { get; set; }
    }

    public class LibrarySyncResult
    {
        public int ConvertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int RemovedOrphansCount { get; set; }
        public int PrunedDirectoriesCount { get; set; }
        public int TotalOperations => ConvertedCount + UpdatedCount + RemovedOrphansCount + PrunedDirectoriesCount;
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
        public int TotalXgFiles => Math.Max(Analysis790.TotalXgFiles, Analysis680.TotalXgFiles);
        public int TotalXgUpgrades => Analysis790.XgUpgradesInTarget.Count + Analysis680.XgUpgradesInTarget.Count;
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
            RemapperOptions? options790 = null,
            RemapperOptions? options680 = null,
            bool convert790 = true,
            bool convert680 = true,
            IProgress<(int Current, int Total, string FileName)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' does not exist.");

            if (!convert790 && !convert680)
                return 0;

            if (convert790 && !Directory.Exists(target790Dir))
                Directory.CreateDirectory(target790Dir);

            if (convert680 && !Directory.Exists(target680Dir))
                Directory.CreateDirectory(target680Dir);

            var files = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int targetMultiplier = (convert790 ? 1 : 0) + (convert680 ? 1 : 0);
            int total = files.Count * targetMultiplier;
            int completed = 0;

            var opt790 = options790 != null ? options790 : new RemapperOptions();
            opt790.TargetModel = "pss790";

            var opt680 = options680 != null ? options680 : new RemapperOptions();
            opt680.TargetModel = "pss680";
            opt680.DefaultReverbBoost = false; // PSS-680 has no DSP reverb

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var relPath = Path.GetRelativePath(sourceDirectory, file);

                    MidiFile? sourceMidi = null;
                    try
                    {
                        sourceMidi = MidiFile.Read(file, Constants.DefaultReadingSettings);
                    }
                    catch (Exception)
                    {
                        completed += targetMultiplier;
                        progress?.Report((completed, total, relPath));
                        continue;
                    }

                    // 1. Convert for PSS-790
                    if (convert790)
                    {
                        var dest790 = Path.Combine(target790Dir, relPath);
                        var dir790 = Path.GetDirectoryName(dest790);
                        if (!string.IsNullOrEmpty(dir790) && !Directory.Exists(dir790)) Directory.CreateDirectory(dir790);

                        try
                        {
                            var remap790 = Remapper.Remap(sourceMidi.Clone(), opt790);
                            Remapper.SaveRemapped(remap790, dest790, "pss790");
                            completed++;
                            progress?.Report((completed, total, $"{relPath} (PSS-790)"));
                        }
                        catch (Exception)
                        {
                            completed++;
                            progress?.Report((completed, total, $"{relPath} (PSS-790 failed)"));
                        }
                    }

                    // 2. Convert for PSS-680
                    if (convert680)
                    {
                        var dest680 = Path.Combine(target680Dir, relPath);
                        var dir680 = Path.GetDirectoryName(dest680);
                        if (!string.IsNullOrEmpty(dir680) && !Directory.Exists(dir680)) Directory.CreateDirectory(dir680);

                        try
                        {
                            var remap680 = Remapper.Remap(sourceMidi.Clone(), opt680);
                            Remapper.SaveRemapped(remap680, dest680, "pss680");
                            completed++;
                            progress?.Report((completed, total, $"{relPath} (PSS-680)"));
                        }
                        catch (Exception)
                        {
                            completed++;
                            progress?.Report((completed, total, $"{relPath} (PSS-680 failed)"));
                        }
                    }
                }
            }, cancellationToken);

            return targetMultiplier > 0 ? completed / targetMultiplier : 0;
        }

        public static Task<int> ConvertDirectoryDualAsync(
            string sourceDirectory,
            string target790Dir,
            string target680Dir,
            RemapperOptions? baseOptions,
            IProgress<(int Current, int Total, string FileName)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return ConvertDirectoryDualAsync(
                sourceDirectory,
                target790Dir,
                target680Dir,
                baseOptions,
                baseOptions,
                true,
                true,
                progress,
                cancellationToken);
        }

        public static LibrarySyncAnalysis AnalyzeSync(string sourceDir, string targetDir, string? targetModel = null)
        {
            var analysis = new LibrarySyncAnalysis();
            if (!Directory.Exists(sourceDir))
                return analysis;

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string resolvedModel = targetModel ?? (targetDir.Contains("680", StringComparison.OrdinalIgnoreCase) ? "pss680" : "pss790");

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

                    bool isOutdated = srcTime > tgtTime;

                    // Intelligent Algorithm Version Check:
                    // If source is a Yamaha XG file and target was converted with legacy algorithm (lacking v2 marker),
                    // flag as outdated for the new XG conversion engine!
                    if (!isOutdated)
                    {
                        if (Remapper.IsXgMidiFile(srcFullPath))
                        {
                            analysis.TotalXgFiles++;
                            if (!Remapper.HasAlgorithmV2MarkerFile(tgtFullPath, resolvedModel))
                            {
                                isOutdated = true;
                                analysis.XgUpgradesInTarget.Add(relPath);
                            }
                        }
                    }

                    if (isOutdated)
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

            var analysis = AnalyzeSync(sourceDir, targetDir, options.RemapperOptions?.TargetModel);

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
                        var targetModel = options.RemapperOptions?.TargetModel ?? (targetDir.Contains("680", StringComparison.OrdinalIgnoreCase) ? "pss680" : "pss790");
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

                // 3. Clean up empty directories across target and source
                if (options?.PruneEmptyDirectories ?? true)
                {
                    result.PrunedDirectoriesCount += PruneEmptyDirectories(targetDir);
                    result.PrunedDirectoriesCount += PruneEmptyDirectories(sourceDir);
                }
            }, cancellationToken);

            return result;
        }

        /// <summary>
        /// Recursively removes all empty subdirectories in bottom-up order across a library folder.
        /// A directory is pruned if it contains zero files and zero remaining subdirectories.
        /// </summary>
        public static int PruneEmptyDirectories(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return 0;

            int pruned = 0;
            try
            {
                var dirs = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length) // Deepest first
                    .ToList();

                foreach (var dir in dirs)
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                            if (Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                            {
                                Directory.Delete(dir, recursive: true);
                                pruned++;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore permission or file-lock issues on individual directory
                    }
                }
            }
            catch
            {
                // Non-critical traversal error
            }

            return pruned;
        }

        public static DualLibrarySyncAnalysis AnalyzeDualSync(string sourceDir, string target790Dir, string target680Dir)
        {
            return new DualLibrarySyncAnalysis
            {
                Analysis790 = AnalyzeSync(sourceDir, target790Dir, "pss790"),
                Analysis680 = AnalyzeSync(sourceDir, target680Dir, "pss680")
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
                PruneEmptyDirectories = options?.PruneEmptyDirectories ?? true,
                ForceReconvertAll = options?.ForceReconvertAll ?? false,
                RemapperOptions = new RemapperOptions { TargetModel = "pss790" }
            };

            var res790 = await ExecuteSyncAsync(sourceDir, target790Dir, opt790, overrideStore, progress, cancellationToken);
            combinedResult.ConvertedCount += res790.ConvertedCount;
            combinedResult.UpdatedCount += res790.UpdatedCount;
            combinedResult.RemovedOrphansCount += res790.RemovedOrphansCount;
            combinedResult.PrunedDirectoriesCount += res790.PrunedDirectoriesCount;

            if (cancellationToken.IsCancellationRequested) return combinedResult;

            // 2. Sync PSS-680 library
            var opt680 = new LibrarySyncOptions
            {
                ConvertMissing = options?.ConvertMissing ?? true,
                UpdateOutdated = options?.UpdateOutdated ?? true,
                RemoveOrphans = options?.RemoveOrphans ?? true,
                PruneEmptyDirectories = options?.PruneEmptyDirectories ?? true,
                ForceReconvertAll = options?.ForceReconvertAll ?? false,
                RemapperOptions = new RemapperOptions { TargetModel = "pss680" }
            };

            var res680 = await ExecuteSyncAsync(sourceDir, target680Dir, opt680, overrideStore, progress, cancellationToken);
            combinedResult.ConvertedCount += res680.ConvertedCount;
            combinedResult.UpdatedCount += res680.UpdatedCount;
            combinedResult.RemovedOrphansCount += res680.RemovedOrphansCount;
            combinedResult.PrunedDirectoriesCount += res680.PrunedDirectoriesCount;

            // 3. Final bottom-up pruning sweep across all 3 library directories
            if (options?.PruneEmptyDirectories ?? true)
            {
                combinedResult.PrunedDirectoriesCount += PruneEmptyDirectories(sourceDir);
                combinedResult.PrunedDirectoriesCount += PruneEmptyDirectories(target790Dir);
                combinedResult.PrunedDirectoriesCount += PruneEmptyDirectories(target680Dir);
            }

            return combinedResult;
        }
    }
}
