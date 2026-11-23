using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PSS790Player.Core
{
    public class FavoriteStore
    {
        private static readonly Lazy<FavoriteStore> _lazyInstance = new(() => new FavoriteStore());
        public static FavoriteStore Instance => _lazyInstance.Value;

        private readonly string _storagePath;
        private readonly object _lock = new();
        private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
        private volatile bool _isLoaded;

        public FavoriteStore(string? customPath = null)
        {
            _storagePath = customPath ?? AppPaths.FavoritesFilePath;
        }

        private void EnsureLoaded()
        {
            if (_isLoaded) return;
            lock (_lock)
            {
                if (_isLoaded) return;

                try
                {
                    if (File.Exists(_storagePath))
                    {
                        var json = File.ReadAllText(_storagePath);
                        var data = JsonSerializer.Deserialize<List<string>>(json);
                        if (data != null)
                        {
                            _favorites = new HashSet<string>(
                                data.Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Select(NormalizeKey),
                                StringComparer.OrdinalIgnoreCase
                            );
                        }
                    }
                }
                catch
                {
                    _favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                finally
                {
                    _isLoaded = true;
                }
            }
        }

        private void SaveInternal()
        {
            try
            {
                var dir = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sorted = _favorites.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                var json = JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true });
                var tempPath = $"{_storagePath}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _storagePath, overwrite: true);
            }
            catch
            {
                // Non-fatal
            }
        }

        private static string NormalizeKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return AppPaths.GetLibrarySubPath(path);
        }

        public bool IsFavorite(string path)
        {
            EnsureLoaded();
            var key = NormalizeKey(path);
            if (string.IsNullOrEmpty(key)) return false;

            lock (_lock)
            {
                return _favorites.Contains(key);
            }
        }

        public bool ToggleFavorite(string path)
        {
            EnsureLoaded();
            var key = NormalizeKey(path);
            if (string.IsNullOrEmpty(key)) return false;

            bool isNowFavorite;
            lock (_lock)
            {
                if (_favorites.Contains(key))
                {
                    _favorites.Remove(key);
                    isNowFavorite = false;
                }
                else
                {
                    _favorites.Add(key);
                    isNowFavorite = true;
                }

                SaveInternal();
            }

            return isNowFavorite;
        }

        public bool AddFavorite(string path)
        {
            EnsureLoaded();
            var key = NormalizeKey(path);
            if (string.IsNullOrEmpty(key)) return false;

            lock (_lock)
            {
                if (_favorites.Add(key))
                {
                    SaveInternal();
                    return true;
                }
                return false;
            }
        }

        public bool RemoveFavorite(string path)
        {
            EnsureLoaded();
            var key = NormalizeKey(path);
            if (string.IsNullOrEmpty(key)) return false;

            lock (_lock)
            {
                if (_favorites.Remove(key))
                {
                    SaveInternal();
                    return true;
                }
                return false;
            }
        }

        public List<string> GetAllFavorites()
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _favorites.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public int PruneMissing(IEnumerable<string> existingPaths)
        {
            EnsureLoaded();
            var existingKeys = new HashSet<string>(
                existingPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizeKey),
                StringComparer.OrdinalIgnoreCase
            );

            int pruned = 0;
            lock (_lock)
            {
                var stale = _favorites.Where(f => !existingKeys.Contains(f)).ToList();
                foreach (var s in stale)
                {
                    _favorites.Remove(s);
                    pruned++;
                }

                if (pruned > 0)
                {
                    SaveInternal();
                }
            }

            return pruned;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _isLoaded = true;
                _favorites.Clear();
                SaveInternal();
            }
        }
    }
}
