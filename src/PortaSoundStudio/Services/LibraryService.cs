using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Melanchall.DryWetMidi.Core;
using Microsoft.Win32;
using PSS790Player.Core;
using PSS790Player.Models;

namespace PSS790Player.Services
{
    /// <summary>
    /// Handles library tree scanning, cross-library file/folder management (mirroring GM / PSS-790 / PSS-680),
    /// favorites, batch conversion, library sync, health audits, and storage path settings.
    /// </summary>
    public sealed class LibraryService : IDisposable
    {
        private readonly AppStateStore _state;
        private readonly object _ctsLock = new();
        private CancellationTokenSource? _batchCts;
        private CancellationTokenSource? _syncCts;
        private CancellationTokenSource? _auditCts;
        private volatile bool _isDisposed;

        public event Action<string, string?>? SongPathRelocated;
        public event Action<string, bool>? SongPathDeleted;
        public event Action? LibraryRootChanged;

        public LibraryService(AppStateStore state)
        {
            _state = state;
        }

        public LibrarySnapshot BuildSnapshot(string? gmDir = null, string? pss790Dir = null, string? pss680Dir = null)
        {
            var resolvedGm = gmDir ?? AppPaths.GmMidiDir;
            var resolved790 = pss790Dir ?? AppPaths.Pss790MidiDir;
            var resolved680 = pss680Dir ?? AppPaths.Pss680MidiDir;

            if (gmDir == null && pss790Dir == null && pss680Dir == null)
            {
                AppPaths.EnsureDirectoriesExist();
                AppPaths.SyncMirroredDirectories();
            }

            var flatCollector = new List<MidiFileItem>();
            var gmCat = BuildCategoryTree(resolvedGm, "General MIDI", "gm", flatCollector);
            var pss790Cat = BuildCategoryTree(resolved790, "Yamaha PSS-790", "pss790", flatCollector);
            var pss680Cat = BuildCategoryTree(resolved680, "Yamaha PSS-680", "pss680", flatCollector);

            return new LibrarySnapshot
            {
                Gm = gmCat,
                Pss790 = pss790Cat,
                Pss680 = pss680Cat,
                AllFiles = flatCollector
            };
        }

        public void RefreshLibrary()
        {
            if (_isDisposed) return;
            var snapshot = BuildSnapshot();
            var favs = new HashSet<string>(FavoriteStore.Instance.GetAllFavorites(), StringComparer.OrdinalIgnoreCase);

            _state.Mutate(s =>
            {
                s.Library = snapshot;
                s.Favorites = favs;
                s.LibraryRoot = AppPaths.BaseMidiDir;
                s.DefaultLibraryRoot = AppPaths.DefaultBaseMidiDir;
            });
        }

        public static CategoryTreeData BuildCategoryTree(
            string rootDir,
            string categoryName,
            string format,
            List<MidiFileItem> flatCollector)
        {
            var allFolders = new List<FolderOption> { new() { SubFolder = "", Display = "(Root)" } };
            if (Directory.Exists(rootDir))
            {
                foreach (var dir in Directory.GetDirectories(rootDir, "*", SearchOption.AllDirectories)
                    .OrderBy(d => Path.GetRelativePath(rootDir, d), StringComparer.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(rootDir, dir);
                    allFolders.Add(new FolderOption { SubFolder = rel, Display = rel.Replace('\\', '/') });
                }
            }

            var rootNode = BuildDirectoryNode(rootDir, rootDir, categoryName, format, flatCollector);
            return new CategoryTreeData
            {
                Name = categoryName,
                FullPath = rootDir,
                Format = format,
                SubFolders = rootNode.SubFolders,
                AllFolders = allFolders,
                Files = rootNode.Files,
                DirectCount = rootNode.DirectCount,
                Count = rootNode.TotalCount
            };
        }

        public static FolderNode BuildDirectoryNode(
            string currentDir,
            string rootDir,
            string categoryName,
            string format,
            List<MidiFileItem> flatCollector)
        {
            var dirName = Path.GetFileName(currentDir);
            var relPath = string.Equals(currentDir, rootDir, StringComparison.OrdinalIgnoreCase)
                ? ""
                : Path.GetRelativePath(rootDir, currentDir);

            var directFiles = new List<MidiFileItem>();
            if (Directory.Exists(currentDir))
            {
                var filePaths = Directory.GetFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                foreach (var file in filePaths)
                {
                    var fileRel = Path.GetRelativePath(rootDir, file);
                    var item = new MidiFileItem
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        RelativePath = fileRel,
                        SubFolder = Path.GetDirectoryName(fileRel) ?? "",
                        Category = categoryName,
                        Format = format,
                        IsFavorite = FavoriteStore.Instance.IsFavorite(file)
                    };
                    flatCollector.Add(item);
                    directFiles.Add(item);
                }
            }

            var subFolders = new List<FolderNode>();
            if (Directory.Exists(currentDir))
            {
                foreach (var subDir in Directory.GetDirectories(currentDir, "*", SearchOption.TopDirectoryOnly)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                {
                    var child = BuildDirectoryNode(subDir, rootDir, categoryName, format, flatCollector);
                    if (child.TotalCount > 0)
                    {
                        subFolders.Add(child);
                    }
                }
            }

            int directCount = directFiles.Count;
            int totalCount = directCount + subFolders.Sum(s => s.TotalCount);
            return new FolderNode
            {
                Name = string.IsNullOrEmpty(dirName) ? categoryName : dirName,
                FullPath = currentDir,
                RelativePath = relPath,
                Category = categoryName,
                Format = format,
                Files = directFiles,
                SubFolders = subFolders,
                DirectCount = directCount,
                TotalCount = totalCount
            };
        }

        public bool CreateFolder(string parentSubFolder, string folderName)
        {
            var trimmed = folderName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _state.AddToast("Folder name cannot be empty.", ToastSeverity.Error);
                return false;
            }

            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _state.AddToast("Folder name contains invalid characters.", ToastSeverity.Error);
                return false;
            }

            var gmTarget = Path.Combine(AppPaths.GmMidiDir, parentSubFolder ?? "", trimmed);
            var pss790Target = Path.Combine(AppPaths.Pss790MidiDir, parentSubFolder ?? "", trimmed);
            var pss680Target = Path.Combine(AppPaths.Pss680MidiDir, parentSubFolder ?? "", trimmed);

            if (!AppPaths.IsPathInsideLibrary(gmTarget) ||
                !AppPaths.IsPathInsideLibrary(pss790Target) ||
                !AppPaths.IsPathInsideLibrary(pss680Target))
            {
                _state.AddToast("Invalid directory path.", ToastSeverity.Error);
                return false;
            }

            if (Directory.Exists(gmTarget) || Directory.Exists(pss790Target) || Directory.Exists(pss680Target))
            {
                _state.AddToast($"Folder '{trimmed}' already exists.", ToastSeverity.Warning);
                return false;
            }

            Directory.CreateDirectory(gmTarget);
            Directory.CreateDirectory(pss790Target);
            Directory.CreateDirectory(pss680Target);
            RefreshLibrary();
            _state.AddToast($"Created folder across libraries: {trimmed}", ToastSeverity.Success);
            return true;
        }

        public bool RenameItem(string fullPath, string newName, bool isFolder)
        {
            var trimmed = newName?.Trim();
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(trimmed))
            {
                _state.AddToast("Invalid path or name.", ToastSeverity.Error);
                return false;
            }
            if (!AppPaths.IsPathInsideLibrary(fullPath))
            {
                _state.AddToast("Cannot rename items outside library.", ToastSeverity.Error);
                return false;
            }
            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _state.AddToast("Name contains invalid characters.", ToastSeverity.Error);
                return false;
            }

            var parentDir = Path.GetDirectoryName(fullPath) ?? "";
            if (isFolder)
            {
                if (!Directory.Exists(fullPath))
                {
                    _state.AddToast("Folder not found.", ToastSeverity.Error);
                    return false;
                }

                var newPath = Path.Combine(parentDir, trimmed);
                if (!AppPaths.IsPathInsideLibrary(newPath) || Directory.Exists(newPath))
                {
                    _state.AddToast(Directory.Exists(newPath) ? $"Folder '{trimmed}' already exists." : "Invalid target folder path.", ToastSeverity.Error);
                    return false;
                }

                Directory.Move(fullPath, newPath);
                MigrateMetadataForFolderMove(fullPath, newPath);
                MirrorFolderRenameAcrossLibraries(fullPath, trimmed);

                RefreshLibrary();
                _state.AddToast($"Renamed folder to: {trimmed}", ToastSeverity.Success);
                return true;
            }

            if (!File.Exists(fullPath))
            {
                _state.AddToast("File not found.", ToastSeverity.Error);
                return false;
            }

            if (!trimmed.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            {
                trimmed += ".mid";
            }

            var newFilePath = Path.Combine(parentDir, trimmed);
            if (!AppPaths.IsPathInsideLibrary(newFilePath) || File.Exists(newFilePath))
            {
                _state.AddToast(File.Exists(newFilePath) ? $"File '{trimmed}' already exists." : "Invalid target file path.", ToastSeverity.Error);
                return false;
            }

            File.Move(fullPath, newFilePath);
            VoiceOverrideStore.Instance.RenameFile(fullPath, newFilePath);
            FavoriteStore.Instance.RenameFile(fullPath, newFilePath);
            SongPathRelocated?.Invoke(fullPath, newFilePath);
            RefreshLibrary();
            _state.AddToast($"Renamed song to: {trimmed}", ToastSeverity.Success);
            return true;
        }

        public bool MoveFile(string sourcePath, string targetCategory, string targetSubFolder, bool alsoMoveLinked, string? linkedPath = null)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath) || !AppPaths.IsPathInsideLibrary(sourcePath))
            {
                _state.AddToast("Source file not found inside library.", ToastSeverity.Error);
                return false;
            }

            var destDir = ResolveDestinationDirectory(targetCategory, targetSubFolder);
            if (destDir == null)
            {
                _state.AddToast("Invalid destination folder.", ToastSeverity.Error);
                return false;
            }
            Directory.CreateDirectory(destDir);

            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(destDir, fileName);
            if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(destPath))
            {
                _state.AddToast($"A file named '{fileName}' already exists in destination.", ToastSeverity.Warning);
                return false;
            }

            File.Move(sourcePath, destPath);
            VoiceOverrideStore.Instance.RenameFile(sourcePath, destPath);
            FavoriteStore.Instance.RenameFile(sourcePath, destPath);
            SongPathRelocated?.Invoke(sourcePath, destPath);

            int movedLinkedCount = alsoMoveLinked ? MoveLinkedCounterparts(sourcePath, targetSubFolder, linkedPath) : 0;
            RefreshLibrary();

            var folderLabel = string.IsNullOrEmpty(targetSubFolder) ? "(Root)" : targetSubFolder;
            _state.AddToast(movedLinkedCount > 0
                ? $"Moved song (+ {movedLinkedCount} linked across libraries) to {folderLabel}"
                : $"Moved '{fileName}' to {folderLabel}", ToastSeverity.Success);
            return true;
        }

        public int BatchMoveFiles(IEnumerable<string> sourcePaths, string targetCategory, string targetSubFolder, bool alsoMoveLinked)
        {
            var destDir = ResolveDestinationDirectory(targetCategory, targetSubFolder);
            if (destDir == null)
            {
                _state.AddToast("Invalid destination folder.", ToastSeverity.Error);
                return 0;
            }
            Directory.CreateDirectory(destDir);

            int movedCount = 0;
            int movedLinkedCount = 0;

            foreach (var sourcePath in sourcePaths)
            {
                if (!File.Exists(sourcePath) || !AppPaths.IsPathInsideLibrary(sourcePath)) continue;
                var destPath = Path.Combine(destDir, Path.GetFileName(sourcePath));
                if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(destPath))
                {
                    File.Move(sourcePath, destPath);
                    VoiceOverrideStore.Instance.RenameFile(sourcePath, destPath);
                    FavoriteStore.Instance.RenameFile(sourcePath, destPath);
                    SongPathRelocated?.Invoke(sourcePath, destPath);
                    movedCount++;
                }

                if (alsoMoveLinked)
                {
                    movedLinkedCount += MoveLinkedCounterparts(sourcePath, targetSubFolder, null);
                }
            }

            RefreshLibrary();
            var label = string.IsNullOrEmpty(targetSubFolder) ? "(Root)" : targetSubFolder;
            _state.AddToast(movedLinkedCount > 0
                ? $"Moved {movedCount} songs (+ {movedLinkedCount} linked) to {label}"
                : $"Moved {movedCount} song{(movedCount == 1 ? "" : "s")} to {label}", ToastSeverity.Success);
            return movedCount;
        }

        public bool MoveFolder(string sourceFolderPath, string targetCategory, string targetParentSubFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath) || !AppPaths.IsPathInsideLibrary(sourceFolderPath))
            {
                _state.AddToast("Source folder not found inside library.", ToastSeverity.Error);
                return false;
            }

            var baseDir = GetCategoryBaseDir(targetCategory);
            var folderName = Path.GetFileName(sourceFolderPath);
            var destDir = Path.Combine(baseDir, targetParentSubFolder ?? "", folderName);

            if (!AppPaths.IsPathInsideLibrary(destDir) ||
                AppPaths.IsPathInsideDirectory(destDir, sourceFolderPath) ||
                Directory.Exists(destDir))
            {
                _state.AddToast("Cannot move folder into that target.", ToastSeverity.Error);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destDir) ?? baseDir);
            Directory.Move(sourceFolderPath, destDir);
            MigrateMetadataForFolderMove(sourceFolderPath, destDir);
            MirrorFolderMoveAcrossLibraries(sourceFolderPath, targetParentSubFolder ?? "", folderName);

            RefreshLibrary();
            _state.AddToast($"Moved folder '{folderName}' across libraries.", ToastSeverity.Success);
            return true;
        }

        public LinkedCheckResult CheckLinkedItem(string filePath, bool isFolder)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new LinkedCheckResult { FilePath = "", IsFolder = isFolder };
            }

            if (isFolder)
            {
                var srcRoot = FindContainingRoot(filePath);
                string? firstMirror = null;
                if (srcRoot != null)
                {
                    var rel = Path.GetRelativePath(srcRoot, filePath);
                    foreach (var r in new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir })
                    {
                        if (string.Equals(r, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;
                        var cand = Path.Combine(r, rel);
                        if (Directory.Exists(cand))
                        {
                            firstMirror = cand;
                            break;
                        }
                    }
                }
                return new LinkedCheckResult
                {
                    FilePath = filePath,
                    HasLinked = firstMirror != null,
                    LinkedPath = firstMirror,
                    LinkedName = firstMirror != null ? AppPaths.GetRelativeLibraryPath(firstMirror) : null,
                    IsFolder = true
                };
            }

            var allLinked = AppPaths.FindAllMatchingLinkedFiles(filePath);
            var primary = allLinked.FirstOrDefault(f => f.Contains(_state.ActiveModel == "pss680" ? "PSS-680" : "PSS-790")) ?? allLinked.FirstOrDefault();
            return new LinkedCheckResult
            {
                FilePath = filePath,
                HasLinked = primary != null,
                LinkedPath = primary,
                LinkedName = primary != null ? AppPaths.GetRelativeLibraryPath(primary) : null,
                IsFolder = false
            };
        }

        public BatchLinkedCheckResult CheckBatchLinkedFiles(IReadOnlyCollection<string> filePaths)
        {
            int linked = filePaths.Count(p => !string.IsNullOrEmpty(p) && AppPaths.FindAllMatchingLinkedFiles(p).Count > 0);
            return new BatchLinkedCheckResult
            {
                TotalCount = filePaths.Count,
                HasLinked = linked > 0,
                LinkedCount = linked
            };
        }

        public void DeleteItem(string fullPath, bool isFolder, bool alsoDeleteLinked, string? linkedPath = null)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !AppPaths.IsPathInsideLibrary(fullPath))
            {
                _state.AddToast("Cannot delete items outside library.", ToastSeverity.Error);
                return;
            }

            if (isFolder)
            {
                if (!Directory.Exists(fullPath)) return;
                SongPathDeleted?.Invoke(fullPath, true);
                VoiceOverrideStore.Instance.RemoveFolderOverrides(fullPath);
                Directory.Delete(fullPath, recursive: true);

                bool deletedMirror = false;
                if (alsoDeleteLinked)
                {
                    var srcRoot = FindContainingRoot(fullPath);
                    if (srcRoot != null)
                    {
                        var rel = Path.GetRelativePath(srcRoot, fullPath);
                        foreach (var r in new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir })
                        {
                            if (string.Equals(r, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;
                            var cand = Path.Combine(r, rel);
                            if (Directory.Exists(cand) && AppPaths.IsPathInsideLibrary(cand))
                            {
                                SongPathDeleted?.Invoke(cand, true);
                                VoiceOverrideStore.Instance.RemoveFolderOverrides(cand);
                                Directory.Delete(cand, recursive: true);
                                deletedMirror = true;
                            }
                        }
                    }
                    FavoriteStore.Instance.RemoveFolder(fullPath);
                }

                RefreshLibrary();
                _state.AddToast(deletedMirror ? $"Deleted folder across libraries: {Path.GetFileName(fullPath)}" : $"Deleted folder: {Path.GetFileName(fullPath)}", ToastSeverity.Info);
                return;
            }

            if (!File.Exists(fullPath)) return;
            SongPathDeleted?.Invoke(fullPath, false);
            VoiceOverrideStore.Instance.RemoveOverrides(fullPath);
            File.Delete(fullPath);

            int linkedDeleted = 0;
            if (alsoDeleteLinked)
            {
                var allLinked = AppPaths.FindAllMatchingLinkedFiles(fullPath);
                if (!string.IsNullOrWhiteSpace(linkedPath) && File.Exists(linkedPath) &&
                    !allLinked.Contains(linkedPath, StringComparer.OrdinalIgnoreCase))
                {
                    allLinked.Add(linkedPath);
                }

                foreach (var lPath in allLinked)
                {
                    if (File.Exists(lPath) && AppPaths.IsPathInsideLibrary(lPath))
                    {
                        SongPathDeleted?.Invoke(lPath, false);
                        VoiceOverrideStore.Instance.RemoveOverrides(lPath);
                        File.Delete(lPath);
                        linkedDeleted++;
                    }
                }
            }

            if (alsoDeleteLinked || AppPaths.FindAllMatchingLinkedFiles(fullPath).Count == 0)
            {
                FavoriteStore.Instance.RemoveFavorite(fullPath);
            }

            RefreshLibrary();
            _state.AddToast(linkedDeleted > 0
                ? $"Deleted song (+ {linkedDeleted} linked across libraries): {Path.GetFileName(fullPath)}"
                : $"Deleted: {Path.GetFileName(fullPath)}", ToastSeverity.Info);
        }

        public void BatchDeleteFiles(IEnumerable<string> filePaths, bool alsoDeleteLinked)
        {
            int deletedCount = 0;
            int deletedLinkedCount = 0;

            foreach (var fullPath in filePaths)
            {
                if (!File.Exists(fullPath) || !AppPaths.IsPathInsideLibrary(fullPath)) continue;
                SongPathDeleted?.Invoke(fullPath, false);
                VoiceOverrideStore.Instance.RemoveOverrides(fullPath);
                File.Delete(fullPath);
                deletedCount++;

                if (alsoDeleteLinked)
                {
                    foreach (var lPath in AppPaths.FindAllMatchingLinkedFiles(fullPath))
                    {
                        if (File.Exists(lPath) && AppPaths.IsPathInsideLibrary(lPath))
                        {
                            SongPathDeleted?.Invoke(lPath, false);
                            VoiceOverrideStore.Instance.RemoveOverrides(lPath);
                            File.Delete(lPath);
                            deletedLinkedCount++;
                        }
                    }
                }

                if (alsoDeleteLinked || AppPaths.FindAllMatchingLinkedFiles(fullPath).Count == 0)
                {
                    FavoriteStore.Instance.RemoveFavorite(fullPath);
                }
            }

            RefreshLibrary();
            _state.AddToast(deletedLinkedCount > 0
                ? $"Deleted {deletedCount} songs (+ {deletedLinkedCount} linked)"
                : $"Deleted {deletedCount} song{(deletedCount == 1 ? "" : "s")}", ToastSeverity.Info);
        }

        public void ToggleFavorite(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            FavoriteStore.Instance.ToggleFavorite(path);
            RefreshLibrary();
        }

        public void PruneEmptyFolders()
        {
            _ = Task.Run(() =>
            {
                int pruned = 0;
                pruned += BatchConverter.PruneEmptyDirectories(AppPaths.GmMidiDir);
                pruned += BatchConverter.PruneEmptyDirectories(AppPaths.Pss790MidiDir);
                pruned += BatchConverter.PruneEmptyDirectories(AppPaths.Pss680MidiDir);
                RefreshLibrary();
                _state.AddToast($"Pruned {pruned} empty folders across all 3 libraries.", ToastSeverity.Success);
            });
        }

        public void StartBatchConversion(BatchConversionRequest req)
        {
            CancellationTokenSource localCts;
            lock (_ctsLock)
            {
                try { _batchCts?.Cancel(); } catch { }
                try { _batchCts?.Dispose(); } catch { }
                localCts = new CancellationTokenSource();
                _batchCts = localCts;
            }

            var sourceDir = string.IsNullOrWhiteSpace(req.SourceDir) ? AppPaths.GmMidiDir : req.SourceDir;
            var target790 = string.IsNullOrWhiteSpace(req.Target790Dir) ? AppPaths.Pss790MidiDir : req.Target790Dir;
            var target680 = string.IsNullOrWhiteSpace(req.Target680Dir) ? AppPaths.Pss680MidiDir : req.Target680Dir;

            var opt790 = new RemapperOptions
            {
                TargetModel = "pss790",
                EnablePolyphonyGuard = req.PolyphonyGuard790,
                DefaultReverbBoost = req.ReverbBoost790,
                NormalizeAwmVelocities = req.VelocityNorm790,
                EmulateSustainPedal = req.Sustain790,
                FoldExpressionPedal = req.ExpressionFold790,
                NeutralizeAcousticPitchBends = req.PitchTune790,
                EnsureSetupPreRoll = req.PreRoll790,
                AutoCenterDanglingPitchBends = true
            };

            var opt680 = new RemapperOptions
            {
                TargetModel = "pss680",
                EnablePolyphonyGuard = req.PolyphonyGuard680,
                DefaultReverbBoost = false,
                FilterUnsupportedControllers = req.CleanControllers680,
                NormalizeAwmVelocities = false,
                BakeFmVelocity = req.FmVelocity680,
                TranslateDrums = req.Drums680,
                EmulateSustainPedal = req.Sustain680,
                FoldExpressionPedal = req.ExpressionFold680,
                NeutralizeAcousticPitchBends = req.PitchTune680,
                EnsureSetupPreRoll = req.PreRoll680,
                AutoCenterDanglingPitchBends = true
            };

            _state.Mutate(s => s.BatchProgress = new BatchProgressInfo
            {
                IsRunning = true,
                Status = "Scanning GM MIDI library..."
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    var progress = new Progress<(int Current, int Total, string FileName)>(p =>
                    {
                        _state.Mutate(s =>
                        {
                            s.BatchProgress.IsRunning = true;
                            s.BatchProgress.Processed = p.Current;
                            s.BatchProgress.Total = p.Total;
                            s.BatchProgress.CurrentFile = p.FileName;
                            s.BatchProgress.Status = $"Converting {p.Current}/{p.Total}: {p.FileName}";
                        });
                    });

                    int count = await BatchConverter.ConvertDirectoryDualAsync(
                        sourceDir, target790, target680, opt790, opt680,
                        req.Convert790, req.Convert680, progress, localCts.Token);

                    RefreshLibrary();
                    _state.Mutate(s =>
                    {
                        s.BatchProgress.IsRunning = false;
                        s.BatchProgress.ConvertedCount = count;
                        s.BatchProgress.Status = $"Completed ({count} file outputs written).";
                    });
                    _state.AddToast($"Batch conversion finished ({count} converted).", ToastSeverity.Success);
                }
                catch (OperationCanceledException)
                {
                    _state.Mutate(s =>
                    {
                        s.BatchProgress.IsRunning = false;
                        s.BatchProgress.WasCancelled = true;
                        s.BatchProgress.Status = "Batch conversion cancelled.";
                    });
                }
                catch (Exception ex)
                {
                    _state.Mutate(s =>
                    {
                        s.BatchProgress.IsRunning = false;
                        s.BatchProgress.Status = $"Batch failed: {ex.Message}";
                    });
                    _state.AddToast($"Batch error: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void CancelBatchConversion()
        {
            lock (_ctsLock)
            {
                try { _batchCts?.Cancel(); } catch { }
            }
        }

        public void AnalyzeLibrarySync()
        {
            _state.Mutate(s =>
            {
                s.SyncState = new LibrarySyncState { IsAnalyzing = true, Status = "Comparing GM, PSS-790, and PSS-680 libraries..." };
            });

            _ = Task.Run(() =>
            {
                try
                {
                    var dual = BatchConverter.AnalyzeDualSync(AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir);
                    int out790 = dual.Analysis790.MissingInTarget.Count + dual.Analysis790.OutdatedInTarget.Count + dual.Analysis790.XgUpgradesInTarget.Count;
                    int out680 = dual.Analysis680.MissingInTarget.Count + dual.Analysis680.OutdatedInTarget.Count + dual.Analysis680.XgUpgradesInTarget.Count;

                    _state.Mutate(s =>
                    {
                        s.SyncState = new LibrarySyncState
                        {
                            IsAnalyzing = false,
                            TotalGm = dual.Analysis790.TotalGmFiles,
                            UpToDate = dual.Analysis790.TotalGmFiles - Math.Max(out790, out680),
                            NewOrOutdated790 = out790,
                            NewOrOutdated680 = out680,
                            Orphan790 = dual.Analysis790.OrphanInTarget.Count,
                            Orphan680 = dual.Analysis680.OrphanInTarget.Count,
                            Status = (out790 == 0 && out680 == 0 && dual.Analysis790.OrphanInTarget.Count == 0 && dual.Analysis680.OrphanInTarget.Count == 0)
                                ? "All 3 libraries are cleanly synchronized."
                                : "Updates available for synchronization."
                        };
                    });
                }
                catch (Exception ex)
                {
                    _state.Mutate(s => s.SyncState = new LibrarySyncState { Status = $"Sync analysis failed: {ex.Message}" });
                }
            });
        }

        public void ExecuteLibrarySync(bool removeOrphans = true, bool pruneEmpty = true, bool forceReconvertAll = false)
        {
            CancellationTokenSource localCts;
            lock (_ctsLock)
            {
                try { _syncCts?.Cancel(); } catch { }
                try { _syncCts?.Dispose(); } catch { }
                localCts = new CancellationTokenSource();
                _syncCts = localCts;
            }

            _state.Mutate(s =>
            {
                s.SyncState.IsRunning = true;
                s.SyncState.Status = "Synchronizing libraries...";
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    var opts = new LibrarySyncOptions
                    {
                        ConvertMissing = true,
                        UpdateOutdated = true,
                        RemoveOrphans = removeOrphans,
                        PruneEmptyDirectories = pruneEmpty,
                        ForceReconvertAll = forceReconvertAll
                    };

                    var progress = new Progress<(int Current, int Total, string FileName, string Action)>(p =>
                    {
                        _state.Mutate(s =>
                        {
                            s.SyncState.IsRunning = true;
                            s.SyncState.Processed = p.Current;
                            s.SyncState.Total = p.Total;
                            s.SyncState.CurrentFile = p.FileName;
                            s.SyncState.Status = $"{p.Action}: {p.FileName}";
                        });
                    });

                    var res = await BatchConverter.ExecuteDualSyncAsync(
                        AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir,
                        opts, VoiceOverrideStore.Instance, progress, localCts.Token);

                    RefreshLibrary();
                    _state.Mutate(s =>
                    {
                        s.SyncState.IsRunning = false;
                        s.SyncState.Status = $"Sync finished: {res.ConvertedCount} converted, {res.UpdatedCount} updated, {res.RemovedOrphansCount} orphans removed.";
                    });
                    _state.AddToast($"Library sync complete ({res.TotalOperations} operations).", ToastSeverity.Success);
                }
                catch (OperationCanceledException)
                {
                    _state.Mutate(s => { s.SyncState.IsRunning = false; s.SyncState.Status = "Sync cancelled."; });
                }
                catch (Exception ex)
                {
                    _state.Mutate(s => { s.SyncState.IsRunning = false; s.SyncState.Status = $"Sync error: {ex.Message}"; });
                    _state.AddToast($"Sync failed: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void CancelLibrarySync()
        {
            lock (_ctsLock)
            {
                try { _syncCts?.Cancel(); } catch { }
            }
        }

        public void RunHealthAudit(string? directory = null)
        {
            CancellationTokenSource localCts;
            lock (_ctsLock)
            {
                try { _auditCts?.Cancel(); } catch { }
                try { _auditCts?.Dispose(); } catch { }
                localCts = new CancellationTokenSource();
                _auditCts = localCts;
            }

            string targetDir = string.IsNullOrWhiteSpace(directory) ? AppPaths.GmMidiDir : directory;
            string model = _state.ActiveModel;

            _state.Mutate(s => s.HealthAudit = new HealthAuditState { IsRunning = true });

            _ = Task.Run(() =>
            {
                if (!Directory.Exists(targetDir))
                {
                    _state.Mutate(s => s.HealthAudit = new HealthAuditState { IsRunning = false });
                    return;
                }

                var files = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int optimal = 0, autofixed = 0, highPoly = 0, corrupt = 0;
                var items = new List<HealthAuditItemDto>(files.Count);

                for (int i = 0; i < files.Count; i++)
                {
                    if (localCts.IsCancellationRequested) break;
                    var path = files[i];
                    var rel = Path.GetRelativePath(targetDir, path);
                    try
                    {
                        var mf = MidiFile.Read(path, Constants.DefaultReadingSettings);
                        var report = MidiHealthInspector.Analyze(mf, null, Path.GetFileName(path), model);
                        if (report.BadgeType == "warning") highPoly++;
                        else if (report.BadgeType == "autofixed") autofixed++;
                        else optimal++;

                        items.Add(new HealthAuditItemDto
                        {
                            FileName = Path.GetFileName(path),
                            RelativePath = rel,
                            Category = model == "pss680" ? "PSS-680" : "PSS-790",
                            PeakPolyphony = report.PeakPolyphony,
                            VoiceStressRating = report.VoiceStressRating,
                            BadgeType = report.BadgeType,
                            BadgeLabel = report.BadgeLabel,
                            Summary = report.BadgeTooltip
                        });
                    }
                    catch
                    {
                        corrupt++;
                        items.Add(new HealthAuditItemDto
                        {
                            FileName = Path.GetFileName(path),
                            RelativePath = rel,
                            Category = "Corrupt / Unreadable",
                            BadgeType = "warning",
                            BadgeLabel = "Read Error",
                            Summary = "File failed strict MIDI parsing."
                        });
                    }
                }

                _state.Mutate(s => s.HealthAudit = new HealthAuditState
                {
                    IsRunning = false,
                    ScannedCount = items.Count,
                    TotalCount = files.Count,
                    OptimalCount = optimal,
                    AutoFixedCount = autofixed,
                    HighPolyCount = highPoly,
                    CorruptCount = corrupt,
                    Items = items
                });
            });
        }

        public string? BrowseLibraryFolder()
        {
            string? selected = null;
            var app = Application.Current;
            void ShowDialog()
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Select Yamaha PortaSound Studio Library Root Folder",
                    InitialDirectory = Directory.Exists(AppPaths.BaseMidiDir) ? AppPaths.BaseMidiDir : AppPaths.DefaultBaseMidiDir
                };
                if (dlg.ShowDialog() == true)
                {
                    selected = dlg.FolderName;
                }
            }

            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(ShowDialog);
            else
                ShowDialog();

            return selected;
        }

        public void SaveLibraryRoot(string newRoot)
        {
            if (string.IsNullOrWhiteSpace(newRoot)) return;
            AppPaths.SetCustomLibraryRoot(newRoot.Trim());
            _ = Task.Run(() =>
            {
                RefreshLibrary();
                LibraryRootChanged?.Invoke();
                _state.AddToast($"Library storage location updated to: {AppPaths.BaseMidiDir}", ToastSeverity.Success);
            });
        }

        public void ResetLibraryRoot()
        {
            AppPaths.SetCustomLibraryRoot(null);
            _ = Task.Run(() =>
            {
                RefreshLibrary();
                LibraryRootChanged?.Invoke();
                _state.AddToast($"Library storage location reset to default: {AppPaths.BaseMidiDir}", ToastSeverity.Info);
            });
        }

        public void OpenExplorer() => AppPaths.OpenFolderInExplorer();
        public void RevealInExplorer(string fullPath) => AppPaths.RevealInExplorer(fullPath);

        private int MoveLinkedCounterparts(string sourcePath, string targetSubFolder, string? linkedPathHint)
        {
            int moved = 0;
            var targets = new List<(string filePath, string destDir)>();
            if (AppPaths.IsPathInsideDirectory(sourcePath, AppPaths.GmMidiDir))
            {
                var l790 = AppPaths.FindMatchingLinkedFile(sourcePath, "pss790");
                if (!string.IsNullOrEmpty(l790) && File.Exists(l790))
                    targets.Add((l790, Path.Combine(AppPaths.Pss790MidiDir, targetSubFolder)));
                var l680 = AppPaths.FindMatchingLinkedFile(sourcePath, "pss680");
                if (!string.IsNullOrEmpty(l680) && File.Exists(l680))
                    targets.Add((l680, Path.Combine(AppPaths.Pss680MidiDir, targetSubFolder)));
            }
            else
            {
                var lGm = AppPaths.FindMatchingLinkedFile(sourcePath);
                if (!string.IsNullOrEmpty(lGm) && File.Exists(lGm))
                {
                    targets.Add((lGm, Path.Combine(AppPaths.GmMidiDir, targetSubFolder)));
                    var is680 = AppPaths.IsPathInsideDirectory(sourcePath, AppPaths.Pss680MidiDir);
                    var otherModel = is680 ? "pss790" : "pss680";
                    var otherRoot = is680 ? AppPaths.Pss790MidiDir : AppPaths.Pss680MidiDir;
                    var otherLink = AppPaths.FindMatchingLinkedFile(lGm, otherModel);
                    if (!string.IsNullOrEmpty(otherLink) && File.Exists(otherLink))
                        targets.Add((otherLink, Path.Combine(otherRoot, targetSubFolder)));
                }
            }

            if (!string.IsNullOrWhiteSpace(linkedPathHint) && File.Exists(linkedPathHint) &&
                !targets.Any(x => string.Equals(x.filePath, linkedPathHint, StringComparison.OrdinalIgnoreCase)))
            {
                var mirrorBase = AppPaths.IsPathInsideDirectory(linkedPathHint, AppPaths.GmMidiDir)
                    ? AppPaths.GmMidiDir
                    : (AppPaths.IsPathInsideDirectory(linkedPathHint, AppPaths.Pss680MidiDir) ? AppPaths.Pss680MidiDir : AppPaths.Pss790MidiDir);
                targets.Add((linkedPathHint, Path.Combine(mirrorBase, targetSubFolder)));
            }

            foreach (var (lFile, destDir) in targets)
            {
                Directory.CreateDirectory(destDir);
                var dest = Path.Combine(destDir, Path.GetFileName(lFile));
                if (!string.Equals(lFile, dest, StringComparison.OrdinalIgnoreCase) && !File.Exists(dest))
                {
                    File.Move(lFile, dest);
                    VoiceOverrideStore.Instance.RenameFile(lFile, dest);
                    FavoriteStore.Instance.RenameFile(lFile, dest);
                    SongPathRelocated?.Invoke(lFile, dest);
                    moved++;
                }
            }

            return moved;
        }

        private void MigrateMetadataForFolderMove(string oldFolder, string newFolder)
        {
            VoiceOverrideStore.Instance.RenameFolder(oldFolder, newFolder);
            FavoriteStore.Instance.RenameFolder(oldFolder, newFolder);
            SongPathRelocated?.Invoke(oldFolder, newFolder);
        }

        private void MirrorFolderRenameAcrossLibraries(string oldFolder, string newName)
        {
            var srcRoot = FindContainingRoot(oldFolder);
            if (srcRoot == null) return;
            var rel = Path.GetRelativePath(srcRoot, oldFolder);
            foreach (var mirrorRoot in new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir })
            {
                if (string.Equals(mirrorRoot, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;
                var mirrorFolder = Path.Combine(mirrorRoot, rel);
                if (!Directory.Exists(mirrorFolder)) continue;
                var newMirror = Path.Combine(Path.GetDirectoryName(mirrorFolder) ?? mirrorRoot, newName);
                if (!Directory.Exists(newMirror))
                {
                    Directory.Move(mirrorFolder, newMirror);
                    MigrateMetadataForFolderMove(mirrorFolder, newMirror);
                }
            }
        }

        private void MirrorFolderMoveAcrossLibraries(string sourceFolderPath, string targetParentSubFolder, string folderName)
        {
            var srcRoot = FindContainingRoot(sourceFolderPath);
            if (srcRoot == null) return;
            var rel = Path.GetRelativePath(srcRoot, sourceFolderPath);
            foreach (var mirrorRoot in new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir })
            {
                if (string.Equals(mirrorRoot, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;
                var mirrorSrc = Path.Combine(mirrorRoot, rel);
                if (!Directory.Exists(mirrorSrc)) continue;
                var mirrorDest = Path.Combine(mirrorRoot, targetParentSubFolder, folderName);
                if (!AppPaths.IsPathInsideLibrary(mirrorDest) || Directory.Exists(mirrorDest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(mirrorDest) ?? mirrorRoot);
                Directory.Move(mirrorSrc, mirrorDest);
                MigrateMetadataForFolderMove(mirrorSrc, mirrorDest);
            }
        }

        private static string? FindContainingRoot(string path)
        {
            if (AppPaths.IsPathInsideDirectory(path, AppPaths.GmMidiDir)) return AppPaths.GmMidiDir;
            if (AppPaths.IsPathInsideDirectory(path, AppPaths.Pss790MidiDir)) return AppPaths.Pss790MidiDir;
            if (AppPaths.IsPathInsideDirectory(path, AppPaths.Pss680MidiDir)) return AppPaths.Pss680MidiDir;
            return null;
        }

        private static string GetCategoryBaseDir(string targetCategory) => targetCategory.ToLowerInvariant() switch
        {
            "pss680" => AppPaths.Pss680MidiDir,
            "pss790" => AppPaths.Pss790MidiDir,
            _ => AppPaths.GmMidiDir
        };

        private static string? ResolveDestinationDirectory(string targetCategory, string? targetSubFolder)
        {
            var baseDir = GetCategoryBaseDir(targetCategory);
            var destDir = Path.Combine(baseDir, targetSubFolder ?? "");
            bool valid = AppPaths.IsPathInsideLibrary(destDir) ||
                         string.Equals(Path.GetFullPath(destDir), Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase);
            return valid ? destDir : null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            lock (_ctsLock)
            {
                try { _batchCts?.Cancel(); _batchCts?.Dispose(); } catch { }
                try { _syncCts?.Cancel(); _syncCts?.Dispose(); } catch { }
                try { _auditCts?.Cancel(); _auditCts?.Dispose(); } catch { }
            }
        }
    }
}
