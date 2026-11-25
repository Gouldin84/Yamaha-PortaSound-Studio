using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PSS790Player.Core
{
    public class VoiceOverrideStore
    {
        private static readonly Lazy<VoiceOverrideStore> _lazyInstance = new(() => new VoiceOverrideStore());
        public static VoiceOverrideStore Instance => _lazyInstance.Value;

        private string _storagePath;
        private readonly object _lock = new();
        private Dictionary<string, Dictionary<int, int>> _overrides = new(StringComparer.OrdinalIgnoreCase);
        private volatile bool _isLoaded;

        public VoiceOverrideStore(string? customPath = null)
        {
            _storagePath = customPath ?? AppPaths.OverridesFilePath;
        }

        public void Reload(string? customPath = null)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(customPath))
                {
                    _storagePath = customPath;
                }
                _overrides.Clear();
                _isLoaded = false;
            }
            EnsureLoaded();
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

                var json = JsonSerializer.Serialize(_overrides, new JsonSerializerOptions { WriteIndented = true });
                var tempPath = $"{_storagePath}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _storagePath, overwrite: true);
            }
            catch { }
        }

        private static string GetRelativeKeyPart(string filePath) =>
            AppPaths.GetRelativeLibraryPath(filePath).Replace('/', '\\');

        private static string NormalizeKey(string filePath, string model = "pss790")
        {
            var rel = GetRelativeKeyPart(filePath);
            return string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase) ? $"pss680:{rel}" : $"pss790:{rel}";
        }

        public Dictionary<int, int?> GetOverrides(string filePath, string model = "pss790")
        {
            EnsureLoaded();
            var result = new Dictionary<int, int?>();
            var key = NormalizeKey(filePath, model);
            var legacyKey = GetRelativeKeyPart(filePath);

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
            var legacyKey = GetRelativeKeyPart(filePath);

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

        public void RemoveOverrides(string filePath)
        {
            EnsureLoaded();
            var key790 = NormalizeKey(filePath, "pss790");
            var key680 = NormalizeKey(filePath, "pss680");
            var legacyKey = GetRelativeKeyPart(filePath);

            lock (_lock)
            {
                bool removed = _overrides.Remove(key790) |
                               _overrides.Remove(key680) |
                               _overrides.Remove(legacyKey);

                if (removed)
                {
                    SaveInternal();
                }
            }
        }

        public void RemoveFolderOverrides(string folderPath)
        {
            EnsureLoaded();
            var prefix790 = NormalizeKey(folderPath, "pss790").TrimEnd('\\') + "\\";
            var prefix680 = NormalizeKey(folderPath, "pss680").TrimEnd('\\') + "\\";
            var legacyPrefix = GetRelativeKeyPart(folderPath).TrimEnd('\\') + "\\";

            lock (_lock)
            {
                var keysToRemove = new List<string>();
                foreach (var k in _overrides.Keys)
                {
                    if (k.StartsWith(prefix790, StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith(prefix680, StringComparison.OrdinalIgnoreCase) ||
                        (!k.StartsWith("pss790:", StringComparison.OrdinalIgnoreCase) &&
                         !k.StartsWith("pss680:", StringComparison.OrdinalIgnoreCase) &&
                         k.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)))
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
            var old790 = NormalizeKey(oldFilePath, "pss790");
            var new790 = NormalizeKey(newFilePath, "pss790");
            var old680 = NormalizeKey(oldFilePath, "pss680");
            var new680 = NormalizeKey(newFilePath, "pss680");
            var oldLegacy = GetRelativeKeyPart(oldFilePath);
            var newLegacy = GetRelativeKeyPart(newFilePath);

            lock (_lock)
            {
                bool changed = false;

                if (!string.Equals(old790, new790, StringComparison.OrdinalIgnoreCase) &&
                    _overrides.TryGetValue(old790, out var map790))
                {
                    _overrides[new790] = map790;
                    _overrides.Remove(old790);
                    changed = true;
                }

                if (!string.Equals(old680, new680, StringComparison.OrdinalIgnoreCase) &&
                    _overrides.TryGetValue(old680, out var map680))
                {
                    _overrides[new680] = map680;
                    _overrides.Remove(old680);
                    changed = true;
                }

                if (!string.Equals(oldLegacy, newLegacy, StringComparison.OrdinalIgnoreCase) &&
                    _overrides.TryGetValue(oldLegacy, out var mapLegacy))
                {
                    _overrides[newLegacy] = mapLegacy;
                    _overrides.Remove(oldLegacy);
                    changed = true;
                }

                if (changed)
                {
                    SaveInternal();
                }
            }
        }

        public void RenameFolder(string oldFolderFullPath, string newFolderFullPath)
        {
            EnsureLoaded();
            var old790Prefix = NormalizeKey(oldFolderFullPath, "pss790").TrimEnd('\\') + "\\";
            var new790Prefix = NormalizeKey(newFolderFullPath, "pss790").TrimEnd('\\') + "\\";
            var old680Prefix = NormalizeKey(oldFolderFullPath, "pss680").TrimEnd('\\') + "\\";
            var new680Prefix = NormalizeKey(newFolderFullPath, "pss680").TrimEnd('\\') + "\\";
            var oldLegacyPrefix = GetRelativeKeyPart(oldFolderFullPath).TrimEnd('\\') + "\\";
            var newLegacyPrefix = GetRelativeKeyPart(newFolderFullPath).TrimEnd('\\') + "\\";

            lock (_lock)
            {
                var migrations = new List<(string OldKey, string NewKey)>();
                foreach (var k in _overrides.Keys)
                {
                    if (!string.Equals(old790Prefix, new790Prefix, StringComparison.OrdinalIgnoreCase) &&
                        k.StartsWith(old790Prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        migrations.Add((k, new790Prefix + k.Substring(old790Prefix.Length)));
                    }
                    else if (!string.Equals(old680Prefix, new680Prefix, StringComparison.OrdinalIgnoreCase) &&
                             k.StartsWith(old680Prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        migrations.Add((k, new680Prefix + k.Substring(old680Prefix.Length)));
                    }
                    else if (!string.Equals(oldLegacyPrefix, newLegacyPrefix, StringComparison.OrdinalIgnoreCase) &&
                             !k.StartsWith("pss790:", StringComparison.OrdinalIgnoreCase) &&
                             !k.StartsWith("pss680:", StringComparison.OrdinalIgnoreCase) &&
                             k.StartsWith(oldLegacyPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        migrations.Add((k, newLegacyPrefix + k.Substring(oldLegacyPrefix.Length)));
                    }
                }

                if (migrations.Count > 0)
                {
                    foreach (var (oldKey, newKey) in migrations)
                    {
                        _overrides[newKey] = _overrides[oldKey];
                        _overrides.Remove(oldKey);
                    }
                    SaveInternal();
                }
            }
        }

        public bool HasOverrides(string filePath, string? model = null)
        {
            EnsureLoaded();
            var key790 = NormalizeKey(filePath, "pss790");
            var key680 = NormalizeKey(filePath, "pss680");
            var legacyKey = GetRelativeKeyPart(filePath);

            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(model))
                {
                    return (_overrides.TryGetValue(key790, out var m790) && m790.Count > 0) ||
                           (_overrides.TryGetValue(key680, out var m680) && m680.Count > 0) ||
                           (_overrides.TryGetValue(legacyKey, out var mLeg) && mLeg.Count > 0);
                }

                if (string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase))
                {
                    return _overrides.TryGetValue(key680, out var m680) && m680.Count > 0;
                }

                return (_overrides.TryGetValue(key790, out var m790Only) && m790Only.Count > 0) ||
                       (_overrides.TryGetValue(legacyKey, out var mLegOnly) && mLegOnly.Count > 0);
            }
        }
    }
}
