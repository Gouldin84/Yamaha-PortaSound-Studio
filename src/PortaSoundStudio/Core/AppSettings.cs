using System;
using System.IO;
using System.Text.Json;

namespace PSS790Player.Core
{
    public class AppSettingsData
    {
        public string? CustomLibraryRoot { get; set; }
    }

    public static class AppSettings
    {
        private static readonly object _lock = new();
        private static AppSettingsData _current = new();
        private static volatile bool _loaded;

        public static string SettingsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YamahaPortaSoundStudio"
        );

        public static string SettingsFilePath => Path.Combine(SettingsDir, "settings.json");

        public static string? CustomLibraryRoot
        {
            get
            {
                EnsureLoaded();
                lock (_lock)
                {
                    return _current.CustomLibraryRoot;
                }
            }
            set
            {
                EnsureLoaded();
                lock (_lock)
                {
                    _current.CustomLibraryRoot = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    SaveInternal();
                }
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try
                {
                    if (File.Exists(SettingsFilePath))
                    {
                        var json = File.ReadAllText(SettingsFilePath);
                        _current = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
                    }
                    else
                    {
                        _current = new AppSettingsData();
                    }
                }
                catch
                {
                    _current = new AppSettingsData();
                }
                finally
                {
                    _loaded = true;
                }
            }
        }

        private static void SaveInternal()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }

                var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
                var tempPath = $"{SettingsFilePath}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, SettingsFilePath, overwrite: true);
            }
            catch { }
        }

        public static void ResetToDefault()
        {
            CustomLibraryRoot = null;
        }
    }
}
