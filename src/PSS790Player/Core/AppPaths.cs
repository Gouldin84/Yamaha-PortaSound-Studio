using System;
using System.Diagnostics;
using System.IO;

namespace PSS790Player.Core
{
    public static class AppPaths
    {
        public static string BaseMidiDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Midi"
        );

        public static string GmMidiDir => Path.Combine(BaseMidiDir, "GM Midi");
        public static string Pss790MidiDir => Path.Combine(BaseMidiDir, "PSS-790 Midi");

        public static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(BaseMidiDir))
                Directory.CreateDirectory(BaseMidiDir);

            if (!Directory.Exists(GmMidiDir))
                Directory.CreateDirectory(GmMidiDir);

            if (!Directory.Exists(Pss790MidiDir))
                Directory.CreateDirectory(Pss790MidiDir);
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
    }
}
