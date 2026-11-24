using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private static readonly HashSet<string> WindowsReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private readonly MidiPlayer _player;
        private readonly MidiRecorder _recorder;
        private readonly object _captureLock = new();
        private readonly object _transmitStateLock = new();
        private readonly SemaphoreSlim _transmitSemaphore = new(1, 1);

        private volatile bool _isDisposed;
        private bool _isCapturing;
        private readonly List<byte[]> _capturedPackets = new();
        private int _capturedByteCount;
        private System.Timers.Timer? _dumpIdleTimer;
        private CancellationTokenSource? _transmitCts;

        public bool IsCapturing
        {
            get
            {
                lock (_captureLock)
                {
                    return _isCapturing;
                }
            }
        }

        public int CapturedPacketCount
        {
            get
            {
                lock (_captureLock)
                {
                    return _capturedPackets.Count;
                }
            }
        }

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
            _dumpIdleTimer.Elapsed += (s, e) =>
            {
                try { OnDumpCompletedAutoSave(); } catch { }
            };
        }

        public void StartCapture()
        {
            lock (_captureLock)
            {
                if (_isDisposed) return;
                _isCapturing = true;
                _capturedPackets.Clear();
                _capturedByteCount = 0;
                try
                {
                    _dumpIdleTimer?.Stop();
                }
                catch (ObjectDisposedException) { }
            }

            CaptureProgress?.Invoke(this, new SysExCaptureEventArgs
            {
                PacketCount = 0,
                ByteCount = 0,
                StatusMessage = "Listening for PSS-790 / PSS-680 SysEx Bulk Dump... Trigger Bulk Dump on the keyboard front panel."
            });
        }

        public void StopCapture(bool autoSave = true)
        {
            List<byte[]> packetsToSave = new();

            lock (_captureLock)
            {
                if (!_isCapturing) return;
                _isCapturing = false;

                try
                {
                    _dumpIdleTimer?.Stop();
                }
                catch (ObjectDisposedException) { }

                if (autoSave && _capturedPackets.Count > 0)
                {
                    packetsToSave = new List<byte[]>(_capturedPackets);
                    _capturedPackets.Clear();
                    _capturedByteCount = 0;
                }
            }

            if (packetsToSave.Count > 0)
            {
                SaveCapturedDumpInternal(packetsToSave);
            }
        }

        private void OnSysExReceived(object? sender, NormalSysExEvent e)
        {
            if (e.Data == null || e.Data.Length == 0) return;

            // Strip any stray MIDI Real-Time bytes (>= 0xF8) and ensure exact single F0 ... F7 wire framing.
            // DryWetMIDI NormalSysExEvent.Data EXCLUDES leading 0xF0 and normally INCLUDES trailing 0xF7.
            var cleaned = new List<byte>(e.Data.Length + 2) { 0xF0 };
            for (int i = 0; i < e.Data.Length; i++)
            {
                byte b = e.Data[i];
                if (b == 0xF7)
                {
                    break; // Terminate cleanly at first EOX
                }
                if (b >= 0xF8)
                {
                    continue; // Ignore interleaved MIDI Real-Time (Clock / Active Sensing)
                }
                if (b >= 0x80)
                {
                    return; // Corrupt embedded Channel / System Common status byte -> discard packet
                }
                cleaned.Add(b);
            }

            if (cleaned.Count < 2) return; // Empty SysEx payload
            cleaned.Add(0xF7);
            byte[] fullMessage = cleaned.ToArray();

            int totalBytes;
            int packetCount;

            lock (_captureLock)
            {
                if (_isDisposed || !_isCapturing) return;

                _capturedPackets.Add(fullMessage);
                _capturedByteCount += fullMessage.Length;
                packetCount = _capturedPackets.Count;
                totalBytes = _capturedByteCount;

                try
                {
                    _dumpIdleTimer?.Stop();
                    _dumpIdleTimer?.Start();
                }
                catch (ObjectDisposedException) { }
            }

            CaptureProgress?.Invoke(this, new SysExCaptureEventArgs
            {
                PacketCount = packetCount,
                ByteCount = totalBytes,
                StatusMessage = $"Streaming packet #{packetCount} ({totalBytes:N0} bytes received)... PSS display counting down."
            });
        }

        private void OnDumpCompletedAutoSave()
        {
            if (_isDisposed) return;
            try
            {
                List<byte[]> packetsToSave = new();

                lock (_captureLock)
                {
                    if (_isDisposed || !_isCapturing || _capturedPackets.Count == 0) return;
                    _isCapturing = false;

                    packetsToSave = new List<byte[]>(_capturedPackets);
                    _capturedPackets.Clear();
                    _capturedByteCount = 0;
                }

                if (packetsToSave.Count > 0)
                {
                    SaveCapturedDumpInternal(packetsToSave);
                }
            }
            catch { }
        }

        private void SaveCapturedDumpInternal(List<byte[]> packets)
        {
            try
            {
                AppPaths.EnsureDirectoriesExist();

                // Select model-aware filename prefix from first captured packet header
                string prefix = "SysEx_Backup";
                var first = packets[0];
                if (first.Length >= 3 && first[0] == 0xF0 && first[1] == Pss680Constants.ManufacturerYamaha)
                {
                    prefix = first[2] == Pss680Constants.InstrumentClassPss
                        ? "PSS680_Backup"
                        : "PSS790_Backup";
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fullPath = CreateUniqueSysExFilePath(AppPaths.SysExDir, prefix, timestamp, packets);

                var info = ParseDumpInfo(fullPath);
                DumpCaptured?.Invoke(this, info);
            }
            catch (Exception ex)
            {
                CaptureProgress?.Invoke(this, new SysExCaptureEventArgs
                {
                    PacketCount = packets.Count,
                    ByteCount = packets.Sum(p => p.Length),
                    StatusMessage = $"Error auto-saving SysEx dump: {ex.Message}"
                });
            }
        }

        private static string CreateUniqueSysExFilePath(string directory, string prefix, string timestamp, List<byte[]> packets)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string fileName = attempt == 0
                    ? $"{prefix}_{timestamp}.syx"
                    : $"{prefix}_{timestamp}_{attempt}.syx";
                string candidatePath = Path.Combine(directory, fileName);

                try
                {
                    using var fs = new FileStream(candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    foreach (var packet in packets)
                    {
                        fs.Write(packet, 0, packet.Length);
                    }
                    return candidatePath;
                }
                catch (IOException) when (File.Exists(candidatePath))
                {
                    // Collision within the same second -> increment suffix and retry
                }
            }

            throw new IOException("Unable to allocate a unique SysEx backup filename.");
        }

        public List<SysExDumpInfo> GetSysExLibrary() => GetSysExLibraryFromDisk();

        public static List<SysExDumpInfo> GetSysExLibraryFromDisk()
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
                // Stream the file in 4 KB chunks to count valid packets and extract only the 128-byte header/preview
                const int PreviewMax = 128;
                byte[] previewBuffer = new byte[Math.Min(PreviewMax, (int)Math.Min(fi.Length, PreviewMax))];
                int previewFilled = 0;
                int packetCount = 0;

                bool inPacket = false;
                int currentPayloadLen = 0;
                byte[] readBuf = new byte[4096];

                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    int read;
                    while ((read = fs.Read(readBuf, 0, readBuf.Length)) > 0)
                    {
                        if (previewFilled < previewBuffer.Length)
                        {
                            int toCopy = Math.Min(read, previewBuffer.Length - previewFilled);
                            Buffer.BlockCopy(readBuf, 0, previewBuffer, previewFilled, toCopy);
                            previewFilled += toCopy;
                        }

                        for (int i = 0; i < read; i++)
                        {
                            byte b = readBuf[i];
                            if (b == 0xF0)
                            {
                                inPacket = true;
                                currentPayloadLen = 0;
                            }
                            else if (b == 0xF7)
                            {
                                if (inPacket && currentPayloadLen > 0)
                                {
                                    packetCount++;
                                }
                                inPacket = false;
                                currentPayloadLen = 0;
                            }
                            else if (b >= 0xF8)
                            {
                                // Interleaved MIDI Real-Time byte: ignore without breaking packet
                            }
                            else if (b >= 0x80)
                            {
                                // Stray non-realtime status byte aborts corrupt SysEx stream
                                inPacket = false;
                                currentPayloadLen = 0;
                            }
                            else if (inPacket)
                            {
                                currentPayloadLen++;
                            }
                        }
                    }
                }

                info.PacketCount = packetCount;

                if (previewFilled > 1 && previewBuffer[0] == 0xF0)
                {
                    byte mfg = previewBuffer[1];
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
                        if (previewFilled > 2 && previewBuffer[2] == Pss680Constants.InstrumentClassPss)
                        {
                            byte format = previewFilled > 3 ? previewBuffer[3] : (byte)0xFF;
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

                info.HexPreview = GenerateHexPreview(previewBuffer, previewFilled, fi.Length);
            }

            return info;
        }

        public async Task<bool> TransmitDumpAsync(string filePath, int packetDelayMs = 30, IProgress<SysExTransmitEventArgs>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("SysEx dump file not found.", filePath);

            // Cancel any in-flight transmission BEFORE awaiting the semaphore so new transmits preempt promptly
            lock (_transmitStateLock)
            {
                if (_isDisposed) return false;
                try { _transmitCts?.Cancel(); } catch (ObjectDisposedException) { }
            }

            bool semaphoreAcquired = false;
            try
            {
                await _transmitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                semaphoreAcquired = true;
            }
            catch (OperationCanceledException)
            {
                ReportTransmitCancelled(progress, 0, 0, 0);
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            CancellationTokenSource? localCts = null;
            int currentPacket = 0;
            int totalPackets = 0;
            int pct = 0;

            try
            {
                lock (_transmitStateLock)
                {
                    if (_isDisposed) return false;
                    localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _transmitCts = localCts;
                }

                var token = localCts.Token;

                byte[] allBytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
                var packets = ExtractPackets(allBytes);

                if (packets.Count == 0)
                    throw new InvalidOperationException("No valid SysEx messages found in file.");

                totalPackets = packets.Count;
                int totalBytes = allBytes.Length;
                int bytesSent = 0;

                for (int i = 0; i < totalPackets; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        ReportTransmitCancelled(progress, currentPacket, totalPackets, pct);
                        return false;
                    }

                    var packet = packets[i];
                    bytesSent += packet.Length;

                    // Convert raw wire packet (F0 ... F7) to DryWetMIDI NormalSysExEvent payload (excludes F0, includes F7)
                    byte[] dataPayload = ToDryWetMidiPayload(packet);
                    _player.SendEvent(new NormalSysExEvent(dataPayload));

                    currentPacket = i + 1;
                    pct = (int)((double)currentPacket / totalPackets * 100);
                    var args = new SysExTransmitEventArgs
                    {
                        CurrentPacket = currentPacket,
                        TotalPackets = totalPackets,
                        CurrentBytes = bytesSent,
                        TotalBytes = totalBytes,
                        Percent = pct,
                        StatusMessage = $"Sending packet {currentPacket} of {totalPackets} ({pct}%)... PSS display shows countdown."
                    };

                    progress?.Report(args);
                    TransmitProgress?.Invoke(this, args);

                    if (packetDelayMs > 0 && i < totalPackets - 1)
                    {
                        await Task.Delay(packetDelayMs, token).ConfigureAwait(false);
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
            catch (OperationCanceledException)
            {
                ReportTransmitCancelled(progress, currentPacket, totalPackets, pct);
                return false;
            }
            finally
            {
                if (localCts != null)
                {
                    lock (_transmitStateLock)
                    {
                        if (ReferenceEquals(_transmitCts, localCts))
                        {
                            _transmitCts = null;
                        }
                    }
                    try { localCts.Dispose(); } catch { }
                }

                if (semaphoreAcquired)
                {
                    try { _transmitSemaphore.Release(); } catch (ObjectDisposedException) { }
                }
            }
        }

        private void ReportTransmitCancelled(IProgress<SysExTransmitEventArgs>? progress, int currentPacket, int totalPackets, int percent)
        {
            var cancelArgs = new SysExTransmitEventArgs
            {
                CurrentPacket = currentPacket,
                TotalPackets = totalPackets,
                Percent = percent,
                StatusMessage = "Transmission cancelled.",
                IsCompleted = true
            };
            progress?.Report(cancelArgs);
            TransmitProgress?.Invoke(this, cancelArgs);
        }

        public void CancelTransmission()
        {
            lock (_transmitStateLock)
            {
                try { _transmitCts?.Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        public void SendQuickCommand(string commandType)
        {
            if (string.IsNullOrWhiteSpace(commandType)) return;
            var cmd = commandType.ToLowerInvariant();

            // PSS-680/480/780 hardware firmware does not support remote MIDI bulk dump requests;
            // dumps must be triggered from the synthesizer front panel while capture is armed.
            if (cmd is "req_voice_dump" or "pss680_req_voice"
                or "req_melody_dump" or "pss680_req_melody"
                or "req_chord_dump" or "pss680_req_chord"
                or "req_rhythm_dump" or "pss680_req_rhythm")
            {
                CaptureProgress?.Invoke(this, new SysExCaptureEventArgs
                {
                    PacketCount = CapturedPacketCount,
                    ByteCount = _capturedByteCount,
                    StatusMessage = "PSS-680/480/780 dumps must be initiated from the keyboard front-panel Bulk Dump button while Capture is active."
                });
                return;
            }

            // DryWetMIDI NormalSysExEvent payloads exclude leading 0xF0 and MUST end with 0xF7
            byte[]? data = cmd switch
            {
                "gm_on" => new byte[] { 0x7E, 0x7F, 0x09, 0x01, 0xF7 },
                "xg_on" => new byte[] { 0x43, 0x10, 0x4C, 0x00, 0x00, 0x7E, 0x00, 0xF7 },
                "master_vol_100" or "master_volume_max" => new byte[] { 0x7F, 0x7F, 0x04, 0x01, 0x00, 0x7F, 0xF7 },
                "all_reset" => new byte[] { 0x7E, 0x7F, 0x09, 0x01, 0xF7 },
                _ => null
            };

            if (data != null)
            {
                _player.SendEvent(new NormalSysExEvent(data));
            }

            if (cmd is "all_reset" or "mode_99" or "pss680_mode_99")
            {
                _player.SendPanicNoteOff();
            }
        }

        public bool DeleteDump(string filePath, string? baseDir = null)
        {
            var targetDir = baseDir ?? (AppPaths.IsPathInsideDirectory(filePath, AppPaths.SysExDir) ? AppPaths.SysExDir : Path.GetDirectoryName(filePath));
            if (string.IsNullOrEmpty(targetDir) || !AppPaths.IsPathInsideDirectory(filePath, targetDir))
            {
                return false;
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }

        public string RenameDump(string filePath, string newName, string? baseDir = null)
        {
            var targetDir = baseDir ?? (AppPaths.IsPathInsideDirectory(filePath, AppPaths.SysExDir) ? AppPaths.SysExDir : Path.GetDirectoryName(filePath));
            if (string.IsNullOrEmpty(targetDir) || !AppPaths.IsPathInsideDirectory(filePath, targetDir))
            {
                throw new UnauthorizedAccessException("Cannot rename files outside target directory.");
            }

            if (!File.Exists(filePath)) throw new FileNotFoundException("File not found.", filePath);

            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name cannot be empty.", nameof(newName));

            string trimmed = newName.Trim();

            // Strict path traversal, relative dot segment, separator, and invalid character validation
            if (trimmed is "." or ".." ||
                trimmed.Contains("..", StringComparison.Ordinal) ||
                trimmed.Contains('/') ||
                trimmed.Contains('\\') ||
                Path.IsPathRooted(trimmed) ||
                Path.GetFileName(trimmed) != trimmed ||
                trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Invalid file name: path traversal or illegal filename characters are not permitted.", nameof(newName));
            }

            string baseName = Path.GetFileNameWithoutExtension(trimmed);
            if (string.IsNullOrWhiteSpace(baseName) || WindowsReservedFileNames.Contains(baseName))
            {
                throw new ArgumentException("Invalid file name: reserved or empty file names are not permitted.", nameof(newName));
            }

            string finalName = trimmed.EndsWith(".syx", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + ".syx";

            string dir = Path.GetFullPath(Path.GetDirectoryName(filePath) ?? AppPaths.SysExDir);
            string newPath = Path.GetFullPath(Path.Combine(dir, finalName));

            // Defense-in-depth containment verification
            string dirPrefix = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
            if (!newPath.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid file name: resolved path escapes target directory.", nameof(newName));
            }

            bool isCaseOnlyRename = string.Equals(Path.GetFullPath(filePath), newPath, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(newPath) && !isCaseOnlyRename)
            {
                throw new InvalidOperationException($"A SysEx file named '{finalName}' already exists.");
            }

            File.Move(filePath, newPath, overwrite: isCaseOnlyRename);
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

        /// <summary>
        /// Extracts valid wire-framed SysEx packets (0xF0 ... 0xF7).
        /// Strips interleaved MIDI Real-Time bytes (>= 0xF8) and discards truncated/corrupt packets
        /// containing stray channel or system-common status bytes (0x80..0xF6).
        /// </summary>
        public static List<byte[]> ExtractPackets(byte[] stream)
        {
            var packets = new List<byte[]>();
            if (stream == null || stream.Length == 0) return packets;

            List<byte>? current = null;

            for (int i = 0; i < stream.Length; i++)
            {
                byte b = stream[i];

                if (b == 0xF0)
                {
                    current = new List<byte>(64) { 0xF0 };
                }
                else if (b == 0xF7)
                {
                    if (current != null && current.Count >= 2)
                    {
                        current.Add(0xF7);
                        packets.Add(current.ToArray());
                    }
                    current = null;
                }
                else if (b >= 0xF8)
                {
                    // Interleaved MIDI Real-Time byte (Clock, Active Sensing, etc.): ignore cleanly
                }
                else if (b >= 0x80)
                {
                    // Unexpected non-realtime status byte aborts incomplete SysEx packet
                    current = null;
                }
                else if (current != null)
                {
                    current.Add(b);
                }
            }

            // Never wrap unframed or truncated streams in fallback F0..F7
            return packets;
        }

        /// <summary>
        /// Converts a raw SysEx wire packet (F0 ... F7) into a DryWetMIDI NormalSysExEvent data payload
        /// (excludes leading 0xF0, guarantees single trailing 0xF7).
        /// </summary>
        public static byte[] ToDryWetMidiPayload(byte[] packet)
        {
            if (packet == null || packet.Length == 0) return Array.Empty<byte>();

            int start = (packet[0] == 0xF0) ? 1 : 0;
            bool hasF7 = (packet.Length > start && packet[^1] == 0xF7);
            int dataLen = packet.Length - start;

            if (hasF7)
            {
                byte[] data = new byte[dataLen];
                Buffer.BlockCopy(packet, start, data, 0, dataLen);
                return data;
            }
            else
            {
                byte[] data = new byte[dataLen + 1];
                Buffer.BlockCopy(packet, start, data, 0, dataLen);
                data[^1] = 0xF7;
                return data;
            }
        }

        public static string GenerateHexPreview(byte[] bytes, int maxBytes = 128)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            int count = Math.Min(bytes.Length, maxBytes);
            return GenerateHexPreview(bytes, count, bytes.Length);
        }

        private static string GenerateHexPreview(byte[] bytes, int previewCount, long totalFileLength)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < previewCount; i += 16)
            {
                sb.AppendFormat("{0:X4}: ", i);
                int lineLen = Math.Min(16, previewCount - i);

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

            if (totalFileLength > previewCount)
            {
                sb.AppendFormat("... and {0:N0} more bytes ({1:N0} total bytes)\n", totalFileLength - previewCount, totalFileLength);
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
            lock (_captureLock)
            {
                _isCapturing = false;
                try
                {
                    _dumpIdleTimer?.Stop();
                    _dumpIdleTimer?.Dispose();
                }
                catch { }
                _dumpIdleTimer = null;
            }

            lock (_transmitStateLock)
            {
                _isDisposed = true;
                try { _transmitCts?.Cancel(); } catch (ObjectDisposedException) { }
            }

            _recorder.SysExReceived -= OnSysExReceived;
            GC.SuppressFinalize(this);
        }
    }
}
