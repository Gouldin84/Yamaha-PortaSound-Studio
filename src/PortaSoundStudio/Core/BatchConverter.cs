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
        public bool PruneSourceDirectory { get; set; } = true;
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
        private static RemapperOptions CloneOptionsForModel(RemapperOptions? source, string targetModel)
        {
            bool is680 = string.Equals(targetModel, "pss680", StringComparison.OrdinalIgnoreCase);
            return new RemapperOptions
            {
                TargetModel = is680 ? "pss680" : "pss790",
                EnablePolyphonyGuard = source?.EnablePolyphonyGuard ?? true,
                ScalePitchBend = source?.ScalePitchBend ?? true,
                PitchBendMode = source?.PitchBendMode ?? PitchBendScaleMode.Auto,
                NeutralizeAcousticPitchBends = source?.NeutralizeAcousticPitchBends ?? true,
                AutoCenterDanglingPitchBends = source?.AutoCenterDanglingPitchBends ?? true,
                EmulateSustainPedal = source?.EmulateSustainPedal ?? true,
                FoldExpressionPedal = source?.FoldExpressionPedal ?? true,
                NormalizeAwmVelocities = is680 ? false : (source?.NormalizeAwmVelocities ?? true),
                BakeFmVelocity = source?.BakeFmVelocity ?? true,
                TranslateDrums = source?.TranslateDrums ?? true,
                VectorQuadLayering = source?.VectorQuadLayering ?? false,
                EnsureSetupPreRoll = source?.EnsureSetupPreRoll ?? true,
                DefaultReverbBoost = is680 ? false : (source?.DefaultReverbBoost ?? true),
                FilterUnsupportedControllers = source?.FilterUnsupportedControllers ?? true,
                ForceRemap = source?.ForceRemap ?? false,
                TempoMultiplier = source?.TempoMultiplier ?? 1.0,
                AutoNormalizeSluggishTempo = source?.AutoNormalizeSluggishTempo ?? false,
                MasterTranspose = source?.MasterTranspose ?? 0,
                ChannelOctaveShifts = source?.ChannelOctaveShifts != null
                    ? new Dictionary<int, int>(source.ChannelOctaveShifts)
                    : new Dictionary<int, int>(),
                ChannelVoiceOverrides = source?.ChannelVoiceOverrides != null
                    ? new Dictionary<int, int?>(source.ChannelVoiceOverrides)
                    : new Dictionary<int, int?>()
            };
        }

        private static string ResolveTargetModel(string? explicitModel, string targetDirectory)
        {
            if (!string.IsNullOrWhiteSpace(explicitModel))
            {
                return string.Equals(explicitModel, "pss680", StringComparison.OrdinalIgnoreCase) ? "pss680" : "pss790";
            }

            return targetDirectory.Contains("PSS-680", StringComparison.OrdinalIgnoreCase) ||
                   targetDirectory.Contains("PSS680", StringComparison.OrdinalIgnoreCase)
                ? "pss680"
                : "pss790";
        }

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

            string resolvedModel = ResolveTargetModel(options?.TargetModel, targetDirectory);
            var effectiveOptions = CloneOptionsForModel(options, resolvedModel);

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
                        var remapped = Remapper.Remap(sourceMidi, effectiveOptions);
                        Remapper.SaveRemapped(remapped, destFile, effectiveOptions.TargetModel);
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
            RemapperOptions? options790,
            RemapperOptions? options680,
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

            // Always clone options independently per model so shared instances are never mutated in place
            var opt790 = CloneOptionsForModel(options790, "pss790");
            var opt680 = CloneOptionsForModel(options680, "pss680");

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

            string resolvedModel = ResolveTargetModel(targetModel, targetDir);

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

                bool isXg = Remapper.IsXgMidiFile(srcFullPath);
                if (isXg)
                {
                    analysis.TotalXgFiles++;
                }

                if (!targetFiles.TryGetValue(relPath, out var tgtFullPath))
                {
                    analysis.MissingInTarget.Add(relPath);
                }
                else
                {
                    var srcTime = File.GetLastWriteTimeUtc(srcFullPath);
                    var tgtTime = File.GetLastWriteTimeUtc(tgtFullPath);

                    bool isOutdated = srcTime > tgtTime;

                    if (!isOutdated && !Remapper.HasAlgorithmV2MarkerFile(tgtFullPath, resolvedModel))
                    {
                        isOutdated = true;
                        if (isXg)
                        {
                            analysis.XgUpgradesInTarget.Add(relPath);
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

            // Mirror subdirectories strictly from sourceDir -> targetDir without touching unrelated global folders
            try
            {
                foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(sourceDir, dir);
                    var mirroredTarget = Path.Combine(targetDir, rel);
                    if (!Directory.Exists(mirroredTarget))
                    {
                        Directory.CreateDirectory(mirroredTarget);
                    }
                }
            }
            catch { }

            string targetModel = ResolveTargetModel(options.RemapperOptions?.TargetModel, targetDir);
            var analysis = AnalyzeSync(sourceDir, targetDir, targetModel);

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

            var modelOpts = CloneOptionsForModel(options.RemapperOptions, targetModel);

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
                            overrideStore.ResetOverrides(orphanPath, targetModel);
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

                // 3. Clean up empty directories across target (and source unless deferred by dual sync)
                if (options.PruneEmptyDirectories)
                {
                    result.PrunedDirectoriesCount += PruneEmptyDirectories(targetDir);
                    if (options.PruneSourceDirectory)
                    {
                        result.PrunedDirectoriesCount += PruneEmptyDirectories(sourceDir);
                    }
                }
            }, cancellationToken);

            return result;
        }

        /// <summary>
        /// Recursively removes all empty subdirectories in bottom-up order across a library folder.
        /// </summary>
        public static int PruneEmptyDirectories(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return 0;

            int pruned = 0;
            try
            {
                var dirs = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)
                    .ToList();

                foreach (var dir in dirs)
                {
                    try
                    {
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir, recursive: false);
                            pruned++;
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

        private sealed class SynchronousProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public SynchronousProgress(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler(value);
        }

        private static int CountPlannedOps(LibrarySyncAnalysis analysis, string sourceDir, LibrarySyncOptions? options)
        {
            int ops = 0;
            if (options?.ForceReconvertAll == true)
            {
                ops += Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                    .Count(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                if (options?.ConvertMissing ?? true) ops += analysis.MissingInTarget.Count;
                if (options?.UpdateOutdated ?? true) ops += analysis.OutdatedInTarget.Count;
            }

            if (options?.RemoveOrphans ?? true) ops += analysis.OrphanInTarget.Count;
            return ops;
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

            int ops790 = 0;
            int ops680 = 0;
            int grandTotal = 0;
            if (progress != null && Directory.Exists(sourceDir))
            {
                var pre = AnalyzeDualSync(sourceDir, target790Dir, target680Dir);
                ops790 = CountPlannedOps(pre.Analysis790, sourceDir, options);
                ops680 = CountPlannedOps(pre.Analysis680, sourceDir, options);
                grandTotal = Math.Max(1, ops790 + ops680);
            }

            var progress790 = progress == null
                ? null
                : new SynchronousProgress<(int Current, int Total, string FileName, string Action)>(p =>
                {
                    int capped = ops790 > 0 ? Math.Min(p.Current, ops790) : p.Current;
                    progress.Report((Math.Min(capped, grandTotal), grandTotal, p.FileName, p.Action));
                });

            // 1. Sync PSS-790 library (defer source pruning until both targets finish)
            var opt790 = new LibrarySyncOptions
            {
                ConvertMissing = options?.ConvertMissing ?? true,
                UpdateOutdated = options?.UpdateOutdated ?? true,
                RemoveOrphans = options?.RemoveOrphans ?? true,
                PruneEmptyDirectories = options?.PruneEmptyDirectories ?? true,
                PruneSourceDirectory = false,
                ForceReconvertAll = options?.ForceReconvertAll ?? false,
                RemapperOptions = CloneOptionsForModel(options?.RemapperOptions, "pss790")
            };

            var res790 = await ExecuteSyncAsync(sourceDir, target790Dir, opt790, overrideStore, progress790, cancellationToken);
            combinedResult.ConvertedCount += res790.ConvertedCount;
            combinedResult.UpdatedCount += res790.UpdatedCount;
            combinedResult.RemovedOrphansCount += res790.RemovedOrphansCount;
            combinedResult.PrunedDirectoriesCount += res790.PrunedDirectoriesCount;

            if (cancellationToken.IsCancellationRequested) return combinedResult;

            var progress680 = progress == null
                ? null
                : new SynchronousProgress<(int Current, int Total, string FileName, string Action)>(p =>
                {
                    int capped = ops680 > 0 ? Math.Min(p.Current, ops680) : p.Current;
                    progress.Report((Math.Min(ops790 + capped, grandTotal), grandTotal, p.FileName, p.Action));
                });

            // 2. Sync PSS-680 library (preserving caller RemapperOptions with model-specific overrides)
            var opt680 = new LibrarySyncOptions
            {
                ConvertMissing = options?.ConvertMissing ?? true,
                UpdateOutdated = options?.UpdateOutdated ?? true,
                RemoveOrphans = options?.RemoveOrphans ?? true,
                PruneEmptyDirectories = options?.PruneEmptyDirectories ?? true,
                PruneSourceDirectory = false,
                ForceReconvertAll = options?.ForceReconvertAll ?? false,
                RemapperOptions = CloneOptionsForModel(options?.RemapperOptions, "pss680")
            };

            var res680 = await ExecuteSyncAsync(sourceDir, target680Dir, opt680, overrideStore, progress680, cancellationToken);
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
