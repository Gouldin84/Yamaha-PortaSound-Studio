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
                        Remapper.SaveRemapped(remapped, destFile);
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
                        var sourceMidi = MidiFile.Read(srcPath, Constants.DefaultReadingSettings);
                        var remapped = Remapper.Remap(sourceMidi, options.RemapperOptions);
                        Remapper.SaveRemapped(remapped, destPath);

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
    }
}
