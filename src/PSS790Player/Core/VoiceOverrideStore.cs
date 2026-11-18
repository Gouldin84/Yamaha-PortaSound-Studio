using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PSS790Player.Core
{
    public class VoiceOverrideStore
    {
        private static VoiceOverrideStore? _instance;
        public static VoiceOverrideStore Instance => _instance ??= new VoiceOverrideStore();

        private readonly string _storagePath;
        private readonly object _lock = new();
        private Dictionary<string, Dictionary<int, int>> _overrides = new(StringComparer.OrdinalIgnoreCase);
        private bool _isLoaded;

        public VoiceOverrideStore(string? customPath = null)
        {
            _storagePath = customPath ?? AppPaths.OverridesFilePath;
        }

        private void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_isLoaded) return;
                _isLoaded = true;

                try
                {
                    if (File.Exists(_storagePath))
                    {
                        var json = File.ReadAllText(_storagePath);
                        var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, int>>>(json);
                        if (data != null)
                        {
                            _overrides = new Dictionary<string, Dictionary<int, int>>(data, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch
                {
                    _overrides = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
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

                var json = JsonSerializer.Serialize(_overrides, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
            }
            catch { }
        }

        private static string NormalizeKey(string filePath, string model = "pss790")
        {
            var rel = AppPaths.GetRelativeLibraryPath(filePath).Replace('/', '\\');
            return string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase) ? $"pss680:{rel}" : $"pss790:{rel}";
        }

        public Dictionary<int, int?> GetOverrides(string filePath, string model = "pss790")
        {
            EnsureLoaded();
            var result = new Dictionary<int, int?>();
            var key = NormalizeKey(filePath, model);
            var legacyKey = AppPaths.GetRelativeLibraryPath(filePath).Replace('/', '\\');

            lock (_lock)
            {
                if (_overrides.TryGetValue(key, out var songOverrides) ||
                    (!string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase) && _overrides.TryGetValue(legacyKey, out songOverrides)))
                {
                    foreach (var (ch, voice) in songOverrides)
                    {
                        result[ch] = voice;
                    }
                }
            }

            return result;
        }

        public void SetOverride(string filePath, int channel, int? voiceNumber, string model = "pss790")
        {
            EnsureLoaded();
            var key = NormalizeKey(filePath, model);

            lock (_lock)
            {
                if (!_overrides.TryGetValue(key, out var songOverrides))
                {
                    songOverrides = new Dictionary<int, int>();
                    _overrides[key] = songOverrides;
                }

                if (voiceNumber.HasValue)
                {
                    songOverrides[channel] = voiceNumber.Value;
                }
                else
                {
                    songOverrides.Remove(channel);
                    if (songOverrides.Count == 0)
                    {
                        _overrides.Remove(key);
                    }
                }

                SaveInternal();
            }
        }

        public void ResetOverrides(string filePath, string model = "pss790")
        {
            EnsureLoaded();
            var key = NormalizeKey(filePath, model);
            var legacyKey = AppPaths.GetRelativeLibraryPath(filePath).Replace('/', '\\');

            lock (_lock)
            {
                bool removed = _overrides.Remove(key);
                if (!string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase))
                {
                    removed |= _overrides.Remove(legacyKey);
                }

                if (removed)
                {
                    SaveInternal();
                }
            }
        }

        public void RemoveOverrides(string filePath) => ResetOverrides(filePath, "pss790");

        public void RemoveFolderOverrides(string folderPath)
        {
            EnsureLoaded();
            var prefix = NormalizeKey(folderPath).TrimEnd('\\') + "\\";

            lock (_lock)
            {
                var keysToRemove = new List<string>();
                foreach (var k in _overrides.Keys)
                {
                    if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        keysToRemove.Add(k);
                    }
                }

                if (keysToRemove.Count > 0)
                {
                    foreach (var k in keysToRemove)
                    {
                        _overrides.Remove(k);
                    }
                    SaveInternal();
                }
            }
        }

        public void RenameFile(string oldFilePath, string newFilePath)
        {
            EnsureLoaded();
            var oldKey = NormalizeKey(oldFilePath);
            var newKey = NormalizeKey(newFilePath);

            if (string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)) return;

            lock (_lock)
            {
                if (_overrides.TryGetValue(oldKey, out var songOverrides))
                {
                    _overrides[newKey] = songOverrides;
                    _overrides.Remove(oldKey);
                    SaveInternal();
                }
            }
        }

        public void RenameFolder(string oldFolderFullPath, string newFolderFullPath)
        {
            EnsureLoaded();
            var oldPrefix = NormalizeKey(oldFolderFullPath).TrimEnd('\\') + "\\";
            var newPrefix = NormalizeKey(newFolderFullPath).TrimEnd('\\') + "\\";

            if (string.Equals(oldPrefix, newPrefix, StringComparison.OrdinalIgnoreCase)) return;

            lock (_lock)
            {
                var keysToMigrate = new List<string>();
                foreach (var k in _overrides.Keys)
                {
                    if (k.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        keysToMigrate.Add(k);
                    }
                }

                if (keysToMigrate.Count > 0)
                {
                    foreach (var oldKey in keysToMigrate)
                    {
                        var suffix = oldKey.Substring(oldPrefix.Length);
                        var newKey = newPrefix + suffix;
                        _overrides[newKey] = _overrides[oldKey];
                        _overrides.Remove(oldKey);
                    }
                    SaveInternal();
                }
            }
        }

        public bool HasOverrides(string filePath)
        {
            EnsureLoaded();
            var key = NormalizeKey(filePath);

            lock (_lock)
            {
                return _overrides.TryGetValue(key, out var map) && map.Count > 0;
            }
        }
    }
}
