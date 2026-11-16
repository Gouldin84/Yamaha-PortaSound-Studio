using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Core;

namespace PSS790Player.Core
{
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
    }
}
