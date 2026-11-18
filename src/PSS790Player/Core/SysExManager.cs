using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace PSS790Player.Core
{
    public class SysExDumpInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string CreatedAtFormatted { get; set; } = string.Empty;
        public int PacketCount { get; set; }
        public string Manufacturer { get; set; } = "Yamaha";
        public string DumpType { get; set; } = "PSS-790 Memory Bulk Dump";
        public string HexPreview { get; set; } = string.Empty;
    }

    public class SysExCaptureEventArgs : EventArgs
    {
        public int PacketCount { get; set; }
        public int ByteCount { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class SysExTransmitEventArgs : EventArgs
    {
        public int CurrentPacket { get; set; }
        public int TotalPackets { get; set; }
        public int CurrentBytes { get; set; }
        public int TotalBytes { get; set; }
        public int Percent { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
    }

    public class SysExManager : IDisposable
    {
        private readonly MidiPlayer _player;
        private readonly MidiRecorder _recorder;
        private readonly object _lock = new();

        private bool _isCapturing;
        private readonly List<byte[]> _capturedPackets = new();
        private System.Timers.Timer? _dumpIdleTimer;
        private CancellationTokenSource? _transmitCts;

        public bool IsCapturing => _isCapturing;
        public int CapturedPacketCount => _capturedPackets.Count;

        public event EventHandler<SysExCaptureEventArgs>? CaptureProgress;
        public event EventHandler<SysExDumpInfo>? DumpCaptured;
        public event EventHandler<SysExTransmitEventArgs>? TransmitProgress;

        public SysExManager(MidiPlayer player, MidiRecorder recorder)
        {
            _player = player;
            _recorder = recorder;
            _recorder.SysExReceived += OnSysExReceived;

            _dumpIdleTimer = new System.Timers.Timer(1500) // 1.5 seconds idle indicates dump stream completed
            {
                AutoReset = false
            };
            _dumpIdleTimer.Elapsed += (s, e) => OnDumpCompletedAutoSave();
        }

        public void StartCapture()
        {
            lock (_lock)
            {
                _isCapturing = true;
                _capturedPackets.Clear();
                _dumpIdleTimer?.Stop();
            }

            CaptureProgress?.Invoke(this, new SysExCaptureEventArgs
            {
                PacketCount = 0,
                ByteCount = 0,
                StatusMessage = "Listening for PSS-790 Memory Bulk Dump... Press MEMORY BULK DUMP button on keyboard twice."
            });
        }

        public void StopCapture(bool autoSave = true)
        {
            _dumpIdleTimer?.Stop();

            lock (_lock)
            {
                if (!_isCapturing) return;
                _isCapturing = false;

                if (autoSave && _capturedPackets.Count > 0)
                {
                    SaveCapturedDumpInternal();
                }
            }
        }

        private void OnSysExReceived(object? sender, NormalSysExEvent e)
        {
            if (!_isCapturing || e.Data == null || e.Data.Length == 0) return;

            // Reconstruct full raw SysEx message: 0xF0 + data + 0xF7
            byte[] fullMessage = new byte[e.Data.Length + 2];
            fullMessage[0] = 0xF0;
            Buffer.BlockCopy(e.Data, 0, fullMessage, 1, e.Data.Length);
            fullMessage[^1] = 0xF7;

            int totalBytes;
            int packetCount;

            lock (_lock)
            {
                _capturedPackets.Add(fullMessage);
                packetCount = _capturedPackets.Count;
                totalBytes = _capturedPackets.Sum(p => p.Length);

                // Reset stream completion idle timer
                _dumpIdleTimer?.Stop();
                _dumpIdleTimer?.Start();
            }

            CaptureProgress?.Invoke(this, new SysExCaptureEventArgs
            {
                PacketCount = packetCount,
                ByteCount = totalBytes,
                StatusMessage = $"Streaming packet #{packetCount} ({totalBytes:N0} bytes received)... PSS-790 display counting down."
            });
        }

        private void OnDumpCompletedAutoSave()
        {
            lock (_lock)
            {
                if (!_isCapturing || _capturedPackets.Count == 0) return;
                _isCapturing = false;
                SaveCapturedDumpInternal();
            }
        }

        private void SaveCapturedDumpInternal()
        {
            try
            {
                AppPaths.EnsureDirectoriesExist();
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"PSS790_Backup_{timestamp}.syx";
                var fullPath = Path.Combine(AppPaths.SysExDir, fileName);

                using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    foreach (var packet in _capturedPackets)
                    {
                        fs.Write(packet, 0, packet.Length);
                    }
                }

                var info = ParseDumpInfo(fullPath);
                DumpCaptured?.Invoke(this, info);
            }
            catch (Exception ex)
            {
                CaptureProgress?.Invoke(this, new SysExCaptureEventArgs
                {
                    PacketCount = _capturedPackets.Count,
                    ByteCount = _capturedPackets.Sum(p => p.Length),
                    StatusMessage = $"Error auto-saving SysEx dump: {ex.Message}"
                });
            }
        }

        public List<SysExDumpInfo> GetSysExLibrary()
        {
            AppPaths.EnsureDirectoriesExist();
            var list = new List<SysExDumpInfo>();

            if (!Directory.Exists(AppPaths.SysExDir))
                return list;

            var files = Directory.GetFiles(AppPaths.SysExDir, "*.syx", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var file in files)
            {
                try
                {
                    list.Add(ParseDumpInfo(file));
                }
                catch { }
            }

            return list;
        }

        public static SysExDumpInfo ParseDumpInfo(string fullPath)
        {
            var fi = new FileInfo(fullPath);
            var info = new SysExDumpInfo
            {
                FileName = fi.Name,
                FilePath = fi.FullName,
                FileSizeBytes = fi.Length,
                FileSizeFormatted = FormatBytes(fi.Length),
                CreatedAt = fi.LastWriteTime,
                CreatedAtFormatted = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
            };

            if (fi.Length > 0)
            {
                var bytes = File.ReadAllBytes(fullPath);
                var packets = ExtractPackets(bytes);
                info.PacketCount = packets.Count;

                if (bytes.Length > 1 && bytes[0] == 0xF0)
                {
                    byte mfg = bytes[1];
                    info.Manufacturer = mfg switch
                    {
                        0x43 => "Yamaha",
                        0x41 => "Roland",
                        0x42 => "Korg",
                        0x7E => "Universal Non-Realtime",
                        0x7F => "Universal Realtime",
                        _ => $"Mfg ID 0x{mfg:X2}"
                    };

                    if (mfg == 0x43)
                    {
                        if (bytes.Length > 2 && bytes[2] == Pss680Constants.InstrumentClassPss)
                        {
                            byte format = bytes.Length > 3 ? bytes[3] : (byte)0xFF;
                            info.DumpType = format switch
                            {
                                Pss680Constants.FormatFmVoice => "PSS-680/480/780 FM Voice Data (Format 00H)",
                                Pss680Constants.FormatMelodyMemory => "PSS-680/480/780 Melody Memory (Format 01H)",
                                Pss680Constants.FormatChordMemory => "PSS-680/480/780 Chord Memory (Format 02H)",
                                Pss680Constants.FormatRhythmPattern => "PSS-680/480/780 Custom Drummer (Format 03H)",
                                _ => "PSS-680/480/780 System Exclusive"
                            };
                        }
                        else
                        {
                            info.DumpType = "PSS-790 Song & Memory Bulk Dump";
                        }
                    }
                }

                info.HexPreview = GenerateHexPreview(bytes, 128);
            }

            return info;
        }

        public async Task<bool> TransmitDumpAsync(string filePath, int packetDelayMs = 30, IProgress<SysExTransmitEventArgs>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("SysEx dump file not found.", filePath);

            _transmitCts?.Cancel();
            _transmitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _transmitCts.Token;

            byte[] allBytes = await File.ReadAllBytesAsync(filePath, token);
            var packets = ExtractPackets(allBytes);

            if (packets.Count == 0)
                throw new InvalidOperationException("No valid SysEx messages found in file.");

            int totalPackets = packets.Count;
            int totalBytes = allBytes.Length;
            int bytesSent = 0;

            for (int i = 0; i < totalPackets; i++)
            {
                if (token.IsCancellationRequested)
                {
                    TransmitProgress?.Invoke(this, new SysExTransmitEventArgs
                    {
                        CurrentPacket = i,
                        TotalPackets = totalPackets,
                        Percent = totalPackets > 0 ? (int)((double)i / totalPackets * 100) : 0,
                        StatusMessage = "Transmission cancelled.",
                        IsCompleted = true
                    });
                    return false;
                }

                var packet = packets[i];
                bytesSent += packet.Length;

                // Strip leading 0xF0 and trailing 0xF7 for DryWetMIDI NormalSysExEvent data payload
                byte[] dataPayload = StripF0F7(packet);

                _player.SendEvent(new NormalSysExEvent(dataPayload));

                int pct = (int)((double)(i + 1) / totalPackets * 100);
                var args = new SysExTransmitEventArgs
                {
                    CurrentPacket = i + 1,
                    TotalPackets = totalPackets,
                    CurrentBytes = bytesSent,
                    TotalBytes = totalBytes,
                    Percent = pct,
                    StatusMessage = $"Sending packet {i + 1} of {totalPackets} ({pct}%)... PSS-790 display shows 'b-8' countdown."
                };

                progress?.Report(args);
                TransmitProgress?.Invoke(this, args);

                if (packetDelayMs > 0 && i < totalPackets - 1)
                {
                    await Task.Delay(packetDelayMs, token);
                }
            }

            var finalArgs = new SysExTransmitEventArgs
            {
                CurrentPacket = totalPackets,
                TotalPackets = totalPackets,
                CurrentBytes = totalBytes,
                TotalBytes = totalBytes,
                Percent = 100,
                StatusMessage = "Bulk dump restored successfully! Keyboard returned to standard tempo display.",
                IsCompleted = true
            };

            progress?.Report(finalArgs);
            TransmitProgress?.Invoke(this, finalArgs);
            return true;
        }

        public void CancelTransmission()
        {
            _transmitCts?.Cancel();
        }

        public void SendQuickCommand(string commandType)
        {
            byte[]? data = commandType.ToLowerInvariant() switch
            {
                "gm_on" => new byte[] { 0x7E, 0x7F, 0x09, 0x01 }, // GM System On
                "xg_on" => new byte[] { 0x43, 0x10, 0x4C, 0x00, 0x00, 0x7E, 0x00 }, // Yamaha XG System On
                "master_vol_100" or "master_volume_max" => new byte[] { 0x7F, 0x7F, 0x04, 0x01, 0x00, 0x7F }, // Master Volume Full
                "all_reset" => new byte[] { 0x7E, 0x7F, 0x09, 0x01 },
                _ => null
            };

            if (data != null)
            {
                _player.SendEvent(new NormalSysExEvent(data));
                if (commandType.ToLowerInvariant() == "all_reset")
                {
                    _player.SendPanicNoteOff();
                }
            }
        }

        public bool DeleteDump(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }

        public string RenameDump(string filePath, string newName)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("File not found.", filePath);

            if (!newName.EndsWith(".syx", StringComparison.OrdinalIgnoreCase))
            {
                newName += ".syx";
            }

            var dir = Path.GetDirectoryName(filePath) ?? AppPaths.SysExDir;
            var newPath = Path.Combine(dir, newName);

            if (File.Exists(newPath) && !string.Equals(filePath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"A SysEx file named '{newName}' already exists.");
            }

            File.Move(filePath, newPath, overwrite: true);
            return newPath;
        }

        public string ImportDump(string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("File not found.", sourceFilePath);

            AppPaths.EnsureDirectoriesExist();
            var destName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(AppPaths.SysExDir, destName);

            if (File.Exists(destPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(destName);
                destName = $"{nameWithoutExt}_{DateTime.Now:yyyyMMdd_HHmmss}.syx";
                destPath = Path.Combine(AppPaths.SysExDir, destName);
            }

            File.Copy(sourceFilePath, destPath);
            return destPath;
        }

        public static List<byte[]> ExtractPackets(byte[] stream)
        {
            var packets = new List<byte[]>();
            int start = -1;

            for (int i = 0; i < stream.Length; i++)
            {
                if (stream[i] == 0xF0)
                {
                    start = i;
                }
                else if (stream[i] == 0xF7 && start != -1)
                {
                    int len = (i - start) + 1;
                    byte[] packet = new byte[len];
                    Buffer.BlockCopy(stream, start, packet, 0, len);
                    packets.Add(packet);
                    start = -1;
                }
            }

            // Fallback if raw file lacks F0/F7 framing
            if (packets.Count == 0 && stream.Length > 0)
            {
                byte[] framed = new byte[stream.Length + 2];
                framed[0] = 0xF0;
                Buffer.BlockCopy(stream, 0, framed, 1, stream.Length);
                framed[^1] = 0xF7;
                packets.Add(framed);
            }

            return packets;
        }

        private static byte[] StripF0F7(byte[] packet)
        {
            if (packet.Length >= 2 && packet[0] == 0xF0 && packet[^1] == 0xF7)
            {
                byte[] data = new byte[packet.Length - 2];
                Buffer.BlockCopy(packet, 1, data, 0, data.Length);
                return data;
            }
            return packet;
        }

        public static string GenerateHexPreview(byte[] bytes, int maxBytes = 128)
        {
            int count = Math.Min(bytes.Length, maxBytes);
            var sb = new StringBuilder();

            for (int i = 0; i < count; i += 16)
            {
                sb.AppendFormat("{0:X4}: ", i);
                int lineLen = Math.Min(16, count - i);

                for (int j = 0; j < 16; j++)
                {
                    if (j < lineLen) sb.AppendFormat("{0:X2} ", bytes[i + j]);
                    else sb.Append("   ");
                    if (j == 7) sb.Append(" ");
                }

                sb.Append(" |");
                for (int j = 0; j < lineLen; j++)
                {
                    char c = (char)bytes[i + j];
                    sb.Append(c >= 32 && c <= 126 ? c : '.');
                }
                sb.Append("|\n");
            }

            if (bytes.Length > maxBytes)
            {
                sb.AppendFormat("... and {0:N0} more bytes ({1:N0} total bytes)\n", bytes.Length - maxBytes, bytes.Length);
            }

            return sb.ToString();
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
        }

        public void Dispose()
        {
            _dumpIdleTimer?.Dispose();
            _dumpIdleTimer = null;
            _transmitCts?.Cancel();
            _transmitCts?.Dispose();
            _transmitCts = null;
            _recorder.SysExReceived -= OnSysExReceived;
        }
    }
}
