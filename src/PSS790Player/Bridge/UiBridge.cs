using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using PSS790Player.Core;

namespace PSS790Player.Bridge
{
    public class UiBridge : IDisposable
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly WebView2 _webView;
        private readonly MidiPlayer _player;
        private readonly MidiRecorder _recorder;
        private readonly SysExManager _sysexManager;
        private readonly MidiDeviceMonitor _deviceMonitor;
        private readonly RemapperOptions _options = new();
        private Dictionary<int, int?>? _preVectorVoiceOverrides;
        private string _activeModel = "pss790";
        private MidiFile? _loadedOriginalMidi;
        private MidiFile? _loadedRemappedMidi;
        private string? _currentFilePath;
        private CancellationTokenSource? _batchCts;
        private CancellationTokenSource? _syncCts;
        private readonly object _ctsLock = new();
        private readonly object _loadFileLock = new();
        private readonly object _seekLock = new();
        private double _pendingSeekSeconds;
        private int _hasPendingSeek;
        private int _seekWorkerRunning;
        private int _loadGeneration;
        private volatile bool _isLoadingSong;
        private volatile bool _isDisposed;

        private readonly bool[] _channelActiveStates = new bool[16];
        private readonly byte[] _channelVelocities = new byte[16];
        private readonly object _channelActivityLock = new();
        private volatile bool _channelActivityDirty = false;
        private readonly System.Threading.Timer _channelBatchTimer;

        public UiBridge(WebView2 webView)
        {
            _webView = webView;
            _player = new MidiPlayer();
            _recorder = new MidiRecorder
            {
                ThruTargetPlayer = _player
            };

            AppPaths.EnsureDirectoriesExist();

            _player.ChannelActivity += (s, e) =>
            {
                if (_isDisposed) return;
                if (e.Channel >= 0 && e.Channel < 16)
                {
                    lock (_channelActivityLock)
                    {
                        _channelActiveStates[e.Channel] = e.IsActive;
                        _channelVelocities[e.Channel] = e.Velocity;
                        _channelActivityDirty = true;
                    }
                }
            };

            // 30 FPS display timer: batches channel activity frames only when state actually changes
            _channelBatchTimer = new System.Threading.Timer(_ =>
            {
                if (_isDisposed) return;
                try
                {
                    if (!_channelActivityDirty) return;

                    bool[] activeCopy;
                    byte[] velCopy;
                    lock (_channelActivityLock)
                    {
                        if (!_channelActivityDirty) return;
                        _channelActivityDirty = false;
                        activeCopy = (bool[])_channelActiveStates.Clone();
                        velCopy = (byte[])_channelVelocities.Clone();
                    }

                    SendToJs("channel_activity_batch", new
                    {
                        active = activeCopy,
                        velocities = velCopy
                    });
                }
                catch { }
            }, null, 33, 33);

            _player.PlaybackStateChanged += (s, e) =>
            {
                if (_isDisposed) return;
                SendToJs("playback_state", new
                {
                    isPlaying = e.IsPlaying,
                    isPaused = e.IsPaused,
                    currentSeconds = e.CurrentTime.TotalSeconds,
                    totalSeconds = e.TotalTime.TotalSeconds
                });
            };

            _player.PlaybackFinished += (s, e) =>
            {
                if (_isDisposed) return;
                ResetChannelActivity();
                SendToJs("playback_finished", new { });
            };

            _recorder.InputActivity += (s, e) =>
            {
                if (_isDisposed) return;
                SendToJs("recorder_input_activity", new
                {
                    channel = e.Channel,
                    isActive = e.IsActive,
                    velocity = e.Velocity,
                    noteNumber = e.NoteNumber
                });
            };

            _recorder.StateChanged += (s, e) =>
            {
                if (_isDisposed) return;
                SendToJs("recorder_state", new
                {
                    state = e.State.ToString().ToLowerInvariant(),
                    countInBeat = e.CountInBeat,
                    bpm = e.Bpm
                });
            };

            _recorder.Tick += (s, e) =>
            {
                if (_isDisposed) return;
                SendToJs("recorder_tick", new
                {
                    elapsedSeconds = e.ElapsedSeconds,
                    eventCount = e.EventCount,
                    noteCount = e.NoteCount,
                    bar = e.CurrentBar,
                    beat = e.CurrentBeat
                });
            };

            _sysexManager = new SysExManager(_player, _recorder);

            // Real-time bidirectional capture of PSS-680 Format 00H FM Voice Dumps
            _recorder.SysExReceived += (s, sysexEvent) =>
            {
                if (_isDisposed) return;
                try
                {
                    var data = sysexEvent.Data;
                    if (data == null || data.Length < 70) return;

                    var incomingVoice = PSS680Voice.FromSysExMessage(data);
                    SendToJs("fm_voice_dump_received", VoiceToObject(incomingVoice));
                    SendToJs("fm_active_bank_changed", new { bank = incomingVoice.BankNumber, voiceNumber = 100 + incomingVoice.BankNumber });
                    SendToJs("notification", new { message = $"Captured FM Voice Dump from PSS-680: Bank {incomingVoice.BankNumber + 1}" });
                }
                catch { }
            };

            _sysexManager.CaptureProgress += (s, e) =>
            {
                if (_isDisposed) return;
                SendToJs("sysex_capture_progress", new
                {
                    packetCount = e.PacketCount,
                    byteCount = e.ByteCount,
                    statusMessage = e.StatusMessage
                });
            };

            _sysexManager.DumpCaptured += (s, dump) =>
            {
                if (_isDisposed) return;
                SendToJs("sysex_dump_captured", new
                {
                    fileName = dump.FileName,
                    filePath = dump.FilePath,
                    fileSizeBytes = dump.FileSizeBytes,
                    fileSize = dump.FileSizeFormatted,
                    fileSizeFormatted = dump.FileSizeFormatted,
                    totalBytes = dump.FileSizeBytes,
                    createdAt = dump.CreatedAtFormatted,
                    createdAtFormatted = dump.CreatedAtFormatted,
                    packetCount = dump.PacketCount,
                    totalPackets = dump.PacketCount,
                    manufacturer = dump.Manufacturer,
                    dumpType = dump.DumpType,
                    hexPreview = dump.HexPreview
                });
                SendSysExLibraryUpdate();
                SendToJs("notification", new { message = $"SysEx bulk dump auto-saved: {dump.FileName}" });
            };

            _sysexManager.TransmitProgress += (s, e) =>
            {
                if (_isDisposed) return;
                SendToJs("sysex_transmit_progress", new
                {
                    currentPacket = e.CurrentPacket,
                    totalPackets = e.TotalPackets,
                    currentBytes = e.CurrentBytes,
                    totalBytes = e.TotalBytes,
                    percent = e.Percent,
                    statusMessage = e.StatusMessage,
                    isCompleted = e.IsCompleted,
                    isCancelled = e.IsCancelled
                });
            };

            // Hot-swap MIDI device monitoring
            _deviceMonitor = new MidiDeviceMonitor();
            _deviceMonitor.DevicesChanged += OnMidiDevicesChanged;

            _player.OutputDeviceDisconnected += (s, devName) =>
            {
                if (_isDisposed) return;
                SendDevicesUpdated($"MIDI Output disconnected: {devName}");
            };

            _recorder.InputDeviceDisconnected += (s, devName) =>
            {
                if (_isDisposed) return;
                SendDevicesUpdated($"MIDI Input disconnected: {devName}");
            };
        }

        public void HandleDeviceChangeNotification()
        {
            if (_isDisposed) return;
            _deviceMonitor.TriggerRescan(300);
        }

        private void OnMidiDevicesChanged(object? sender, MidiDevicesChangedEventArgs e)
        {
            if (_isDisposed) return;
            string? toast = null;

            // Output Device Hot-Swap
            var currentOut = _player.CurrentOutputDeviceName;
            if (!string.IsNullOrEmpty(currentOut) && e.RemovedOutputs.Contains(currentOut, StringComparer.OrdinalIgnoreCase))
            {
                _player.SetOutputDevice((string?)null);
                HandleDeviceSelectionChanged();
                toast = $"MIDI Output disconnected: {currentOut}";
            }
            else if (string.IsNullOrEmpty(currentOut) && e.OutputDevices.Count > 0)
            {
                var targetOut = e.AddedOutputs.FirstOrDefault() ?? e.OutputDevices.FirstOrDefault();
                if (!string.IsNullOrEmpty(targetOut))
                {
                    _player.SetOutputDevice(targetOut);
                    HandleDeviceSelectionChanged();
                    toast = $"MIDI Output connected: {targetOut}";
                }
            }
            else if (e.AddedOutputs.Count > 0)
            {
                toast = $"New MIDI Output detected: {string.Join(", ", e.AddedOutputs)}";
            }

            // Input Device Hot-Swap
            var currentIn = _recorder.CurrentInputDeviceName;
            if (!string.IsNullOrEmpty(currentIn) && e.RemovedInputs.Contains(currentIn, StringComparer.OrdinalIgnoreCase))
            {
                _recorder.SetInputDevice(null);
                if (toast == null) toast = $"MIDI Input disconnected: {currentIn}";
            }
            else if (string.IsNullOrEmpty(currentIn) && e.InputDevices.Count > 0)
            {
                var targetIn = e.AddedInputs.FirstOrDefault() ?? e.InputDevices.FirstOrDefault();
                if (!string.IsNullOrEmpty(targetIn))
                {
                    _recorder.SetInputDevice(targetIn);
                    if (toast == null) toast = $"MIDI Input connected: {targetIn}";
                }
            }
            else if (e.AddedInputs.Count > 0 && toast == null)
            {
                toast = $"New MIDI Input detected: {string.Join(", ", e.AddedInputs)}";
            }

            SendDevicesUpdated(toast);
        }

        private void SendDevicesUpdated(string? toastMessage = null)
        {
            var outputDevices = MidiPlayer.GetOutputDeviceNames();
            var inputDevices = MidiRecorder.GetInputDeviceNames();
            var currentOutput = _player.CurrentOutputDeviceName;
            var currentInput = _recorder.CurrentInputDeviceName;

            SendToJs("devices_updated", new
            {
                devices = outputDevices,
                inputDevices = inputDevices,
                currentOutputDevice = currentOutput,
                currentInputDevice = currentInput,
                toastMessage = toastMessage
            });
        }

        private void SchedulePendingSeekDrain()
        {
            if (Interlocked.CompareExchange(ref _seekWorkerRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    DrainPendingSeeks();
                }
                finally
                {
                    Volatile.Write(ref _seekWorkerRunning, 0);
                    if (!_isDisposed && Volatile.Read(ref _hasPendingSeek) != 0)
                    {
                        SchedulePendingSeekDrain();
                    }
                }
            });
        }

        private void DrainPendingSeeks()
        {
            while (!_isDisposed && Interlocked.Exchange(ref _hasPendingSeek, 0) != 0)
            {
                double targetSeconds;
                lock (_seekLock)
                {
                    targetSeconds = _pendingSeekSeconds;
                }
                _player.Seek(TimeSpan.FromSeconds(targetSeconds));
            }
        }

        private void UpdateCurrentFilePathIfMatch(string oldPath, string newPath)
        {
            lock (_loadFileLock)
            {
                if (string.Equals(_currentFilePath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    _currentFilePath = newPath;
                }
            }
        }

        private void UpdateCurrentFilePathAfterFolderMove(string oldFolderPath, string newFolderPath)
        {
            lock (_loadFileLock)
            {
                if (_currentFilePath != null &&
                    _currentFilePath.StartsWith(oldFolderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    _currentFilePath = newFolderPath + _currentFilePath.Substring(oldFolderPath.Length);
                }
            }
        }

        private void ClearCurrentFileIfMatches(string targetPath, bool isFolder)
        {
            bool shouldStop = false;
            lock (_loadFileLock)
            {
                if (_currentFilePath != null &&
                    (isFolder
                        ? _currentFilePath.StartsWith(targetPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(_currentFilePath, targetPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _currentFilePath = null;
                    _loadedOriginalMidi = null;
                    _loadedRemappedMidi = null;
                    shouldStop = true;
                }
            }
            if (shouldStop)
            {
                _player.Stop();
            }
        }

        private static string FormatDurationMmSs(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            return $"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}";
        }

        public void HandleMessage(string json)
        {
            if (_isDisposed) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var action = root.GetProperty("action").GetString();

                switch (action)
                {
                    case "get_initial_data":
                        HandleGetInitialData();
                        break;
                    case "open_explorer":
                        AppPaths.OpenFolderInExplorer();
                        break;
                    case "reveal_in_explorer":
                        HandleRevealInExplorer(root);
                        break;
                    case "create_folder":
                        HandleCreateFolder(root);
                        break;
                    case "rename_item":
                        HandleRenameItem(root);
                        break;
                    case "move_file":
                        HandleMoveFile(root);
                        break;
                    case "batch_move_files":
                        HandleBatchMoveFiles(root);
                        break;
                    case "move_folder":
                        HandleMoveFolder(root);
                        break;
                    case "check_linked_file":
                        HandleCheckLinkedFile(root);
                        break;
                    case "delete_item":
                        HandleDeleteItem(root);
                        break;
                    case "batch_delete_files":
                        HandleBatchDeleteFiles(root);
                        break;
                    case "refresh_library":
                        SendLibraryUpdate();
                        SendFmVoiceLibraryUpdate();
                        SendVectorLibraryUpdate();
                        break;
                    case "analyze_library_sync":
                        HandleAnalyzeLibrarySync();
                        break;
                    case "execute_library_sync":
                        HandleExecuteLibrarySync(root);
                        break;
                    case "cancel_library_sync":
                        lock (_ctsLock)
                        {
                            try { _syncCts?.Cancel(); } catch { }
                        }
                        break;
                    case "prune_empty_folders":
                        _ = Task.Run(() =>
                        {
                            int pruned = 0;
                            pruned += BatchConverter.PruneEmptyDirectories(AppPaths.GmMidiDir);
                            pruned += BatchConverter.PruneEmptyDirectories(AppPaths.Pss790MidiDir);
                            pruned += BatchConverter.PruneEmptyDirectories(AppPaths.Pss680MidiDir);
                            SendLibraryUpdate();
                            SendToJs("notification", new { message = $"Pruned {pruned} empty folders across all 3 libraries." });
                        });
                        break;
                    case "toggle_favorite":
                        HandleToggleFavorite(root);
                        break;
                    case "get_favorites":
                        SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
                        break;
                    case "rescan_devices":
                        _deviceMonitor.TriggerRescan(0);
                        SendDevicesUpdated();
                        break;
                    case "select_device":
                        var dev = root.GetProperty("deviceName").GetString();
                        if (dev != null)
                        {
                            _player.SetOutputDevice(dev);
                            HandleDeviceSelectionChanged();
                            SendDevicesUpdated();
                        }
                        break;
                    case "load_file":
                        var path = root.TryGetProperty("filePath", out var fp) ? fp.GetString() : (root.TryGetProperty("path", out var p) ? p.GetString() : null);
                        if (!string.IsNullOrEmpty(path))
                        {
                            string? curPathSnap;
                            lock (_loadFileLock)
                            {
                                curPathSnap = _currentFilePath;
                            }
                            if (string.Equals(curPathSnap, path, StringComparison.OrdinalIgnoreCase) && _player.IsPlaying)
                            {
                                break;
                            }

                            bool autoPlay = root.TryGetProperty("autoPlay", out var ap) && ap.GetBoolean();
                            LoadFileAsync(path, autoPlay: autoPlay);
                        }
                        break;
                    case "load_midi_metadata":
                        var metaPath = root.TryGetProperty("filePath", out var mfp) ? mfp.GetString() : (root.TryGetProperty("path", out var mp) ? mp.GetString() : null);
                        if (!string.IsNullOrEmpty(metaPath) && !_player.IsPlaying && !_isLoadingSong)
                        {
                            LoadFileAsync(metaPath, autoPlay: false);
                        }
                        break;
                    case "load_midi_and_play":
                        var playPath = root.TryGetProperty("filePath", out var fpp) ? fpp.GetString() : (root.TryGetProperty("path", out var pp) ? pp.GetString() : null);
                        if (!string.IsNullOrEmpty(playPath))
                        {
                            LoadFileAsync(playPath, autoPlay: true);
                        }
                        break;
                    case "reset_voice_overrides":
                        string? filePathSnapshot;
                        MidiFile? origCheck;
                        lock (_loadFileLock)
                        {
                            filePathSnapshot = _currentFilePath;
                            origCheck = _loadedOriginalMidi;
                        }
                        if (filePathSnapshot != null && origCheck != null)
                        {
                            var modelSnapshot = _activeModel;
                            VoiceOverrideStore.Instance.ResetOverrides(filePathSnapshot, modelSnapshot);
                            lock (_loadFileLock)
                            {
                                _options.ChannelVoiceOverrides.Clear();
                            }
                            _ = Task.Run(() =>
                            {
                                ReapplyRemapping(preservePosition: true);
                                lock (_loadFileLock)
                                {
                                    if (_loadedOriginalMidi == null) return;
                                    var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                                    var channelInfo = AnalyzeChannels(_loadedOriginalMidi, _loadedRemappedMidi, format);
                                    SendToJs("channels_updated", new { channels = channelInfo });
                                }
                                SendToJs("notification", new { message = "Reset voices to default mapping." });
                            });
                        }
                        break;
                    case "browse_file":
                        BrowseAndLoadFile();
                        break;
                    case "play":
                        _player.Play();
                        break;
                    case "pause":
                        _player.Pause();
                        break;
                    case "stop":
                        _player.Stop();
                        ResetChannelActivity();
                        break;
                    case "seek":
                        var seconds = root.GetProperty("seconds").GetDouble();
                        lock (_seekLock)
                        {
                            _pendingSeekSeconds = seconds;
                        }
                        Volatile.Write(ref _hasPendingSeek, 1);
                        SchedulePendingSeekDrain();
                        break;
                    case "set_active_model":
                        var selectedModel = root.GetProperty("model").GetString() ?? "pss790";
                        _activeModel = selectedModel;
                        _player.ActiveModel = selectedModel;
                        bool modelIs680 = string.Equals(selectedModel, "pss680", StringComparison.OrdinalIgnoreCase);

                        string? activeFileSnap;
                        MidiFile? activeOrigSnap;
                        lock (_loadFileLock)
                        {
                            _options.TargetModel = selectedModel;
                            _options.NormalizeAwmVelocities = !modelIs680;
                            _options.DefaultReverbBoost = !modelIs680;
                            activeFileSnap = _currentFilePath;
                            activeOrigSnap = _loadedOriginalMidi;
                        }

                        if (activeFileSnap != null && activeOrigSnap != null)
                        {
                            _ = Task.Run(() =>
                            {
                                lock (_loadFileLock)
                                {
                                    if (_loadedOriginalMidi == null) return;
                                    _options.ChannelVoiceOverrides.Clear();
                                    var savedModelOverrides = VoiceOverrideStore.Instance.GetOverrides(activeFileSnap, selectedModel);
                                    foreach (var (oCh, oVoice) in savedModelOverrides)
                                    {
                                        _options.ChannelVoiceOverrides[oCh] = oVoice;
                                    }
                                }
                                ReapplyRemapping(preservePosition: true);
                                lock (_loadFileLock)
                                {
                                    if (_loadedOriginalMidi == null) return;
                                    var activeFormat = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                                    var activeChannels = AnalyzeChannels(_loadedOriginalMidi, _loadedRemappedMidi, activeFormat);
                                    var activeHealth = MidiHealthInspector.Analyze(_loadedOriginalMidi, _loadedRemappedMidi, Path.GetFileName(activeFileSnap), selectedModel);
                                    SendToJs("channels_updated", new { channels = activeChannels });
                                    SendToJs("health_updated", BuildHealthPayload(activeHealth));
                                }
                            });
                        }

                        SendLibraryUpdate();
                        SendFmVoiceLibraryUpdate();
                        SendVectorLibraryUpdate();
                        SendToJs("model_changed", new
                        {
                            model = selectedModel,
                            title = selectedModel == "pss680" ? "PSS-680 Studio" : "PSS-790 Studio",
                            readyLabel = selectedModel == "pss680" ? "PSS-680 Ready" : "PSS-790 Ready",
                            drumLabel = selectedModel == "pss680" ? "PSS-680 Percussion (32 Hand Percussion Instruments)" : "PSS-790 Percussion (Chart 1 Fixed)",
                            polyphonyLimit = selectedModel == "pss680" ? 12 : 28
                        });
                        break;
                    case "set_speed":
                        var speed = root.GetProperty("speed").GetDouble();
                        _player.SetSpeed(speed);
                        break;
                    case "set_loop":
                        var loop = root.GetProperty("loop").GetBoolean();
                        _player.SetLoop(loop);
                        break;
                    case "override_voice":
                        var ch = root.GetProperty("channel").GetInt32();
                        int? voice = root.GetProperty("voice").ValueKind == JsonValueKind.Null ? null : root.GetProperty("voice").GetInt32();
                        lock (_loadFileLock)
                        {
                            _options.ChannelVoiceOverrides[ch] = voice;
                        }
                        if (_currentFilePath != null)
                        {
                            VoiceOverrideStore.Instance.SetOverride(_currentFilePath, ch, voice, _activeModel);
                        }
                        if (voice.HasValue)
                        {
                            _player.SendVoiceOverride(ch, voice.Value, _activeModel);
                        }
                        _ = Task.Run(() => ReapplyRemapping(preservePosition: true));
                        break;
                    case "override_voice_fm_preset":
                        {
                            var fmCh = root.GetProperty("channel").GetInt32();
                            int? explicitBank = root.TryGetProperty("bank", out var bP) ? Math.Clamp(bP.GetInt32(), 0, 4) : null;
                            PSS680Voice fmVoice;
                            byte[] sysex;

                            if (root.TryGetProperty("filePath", out var fpProp) && !string.IsNullOrEmpty(fpProp.GetString()))
                            {
                                var vPath = fpProp.GetString()!;
                                if (!File.Exists(vPath) || !IsPathInsideDirectory(vPath, AppPaths.FmVoicesDir))
                                {
                                    SendToJs("error", new { message = "FM Voice patch file not found." });
                                    break;
                                }
                                sysex = File.ReadAllBytes(vPath);
                                fmVoice = PSS680Voice.FromSysExMessage(sysex);
                                fmVoice.Name = Path.GetFileNameWithoutExtension(vPath);
                                if (explicitBank.HasValue && explicitBank.Value != fmVoice.BankNumber)
                                {
                                    fmVoice.BankNumber = explicitBank.Value;
                                    sysex = fmVoice.ToSysExMessage();
                                }
                            }
                            else
                            {
                                fmVoice = ParseVoiceFromJson(root);
                                fmVoice.BankNumber = explicitBank ?? 0;
                                sysex = fmVoice.ToSysExMessage();
                            }

                            int bank = Math.Clamp(fmVoice.BankNumber, 0, 4);
                            _player.SendSysEx(sysex);

                            int voiceNumber = 100 + bank;
                            lock (_loadFileLock)
                            {
                                _options.ChannelVoiceOverrides[fmCh] = voiceNumber;
                            }
                            if (_currentFilePath != null)
                            {
                                VoiceOverrideStore.Instance.SetOverride(_currentFilePath, fmCh, voiceNumber, _activeModel);
                            }
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(35);
                                _player.SendVoiceOverride(fmCh, voiceNumber, "pss680");
                                ReapplyRemapping(preservePosition: true);
                            });
                            SendToJs("channel_voice_overridden", new { channel = fmCh, voice = voiceNumber, voiceName = fmVoice.Name });
                        }
                        break;
                    case "set_master_transpose":
                        {
                            var transpose = root.TryGetProperty("transpose", out var tP) ? Math.Clamp(tP.GetInt32(), -12, 12) : 0;
                            lock (_loadFileLock)
                            {
                                _options.MasterTranspose = transpose;
                            }
                            _ = Task.Run(() => ReapplyRemapping(preservePosition: true));
                            SendToJs("master_transpose_changed", new { transpose });
                        }
                        break;
                    case "set_channel_octave":
                        {
                            var octCh = root.GetProperty("channel").GetInt32();
                            var octShift = root.TryGetProperty("octaveShift", out var oP) ? Math.Clamp(oP.GetInt32(), -24, 24) : 0;
                            lock (_loadFileLock)
                            {
                                _options.ChannelOctaveShifts[octCh] = octShift;
                            }
                            _ = Task.Run(() => ReapplyRemapping(preservePosition: true));
                            SendToJs("channel_octave_changed", new { channel = octCh, octaveShift = octShift });
                        }
                        break;
                    case "set_pitch_bend_mode":
                        {
                            var modeStr = root.TryGetProperty("mode", out var mP) ? mP.GetString() : "auto";
                            PitchBendScaleMode resolvedMode;
                            lock (_loadFileLock)
                            {
                                _options.PitchBendMode = modeStr?.ToLowerInvariant() switch
                                {
                                    "preserve1to1" => PitchBendScaleMode.Preserve1to1,
                                    "divideby6" => PitchBendScaleMode.DivideBy6,
                                    "disable" => PitchBendScaleMode.Disable,
                                    _ => PitchBendScaleMode.Auto
                                };
                                resolvedMode = _options.PitchBendMode;
                            }
                            _ = Task.Run(() => ReapplyRemapping(preservePosition: true));
                            SendToJs("pitch_bend_mode_changed", new { mode = resolvedMode.ToString() });
                        }
                        break;
                    case "set_channel_volume":
                        var vCh = root.GetProperty("channel").GetInt32();
                        var vol = (byte)root.GetProperty("volume").GetInt32();
                        _player.SetChannelVolume(vCh, vol);
                        break;
                    case "set_channel_mute":
                        var mCh = root.GetProperty("channel").GetInt32();
                        var mute = root.GetProperty("mute").GetBoolean();
                        _player.SetChannelMute(mCh, mute);
                        break;
                    case "set_channel_solo":
                        var sCh = root.GetProperty("channel").GetInt32();
                        var solo = root.GetProperty("solo").GetBoolean();
                        _player.SetChannelSolo(sCh, solo);
                        break;
                    case "clear_all_solo":
                        _player.ClearAllSolo();
                        break;
                    case "toggle_polyphony_guard":
                        var guard = root.GetProperty("enabled").GetBoolean();
                        lock (_loadFileLock)
                        {
                            _options.EnablePolyphonyGuard = guard;
                        }
                        _ = Task.Run(() => ReapplyRemapping(preservePosition: true));
                        break;
                    case "save_remapped":
                        SaveRemappedDialog();
                        break;
                    case "start_batch":
                        var sourceDir = root.TryGetProperty("sourceDir", out var sProp) && !string.IsNullOrEmpty(sProp.GetString())
                            ? sProp.GetString()! : AppPaths.GmMidiDir;
                        var target790 = root.TryGetProperty("target790Dir", out var t790Prop) && !string.IsNullOrEmpty(t790Prop.GetString())
                            ? t790Prop.GetString()! : (root.TryGetProperty("targetDir", out var legacyTgt) && !string.IsNullOrEmpty(legacyTgt.GetString()) ? legacyTgt.GetString()! : AppPaths.Pss790MidiDir);
                        var target680 = root.TryGetProperty("target680Dir", out var t680Prop) && !string.IsNullOrEmpty(t680Prop.GetString())
                            ? t680Prop.GetString()! : AppPaths.Pss680MidiDir;

                        bool convert790 = !root.TryGetProperty("convert790", out var c790Prop) || c790Prop.GetBoolean();
                        bool convert680 = !root.TryGetProperty("convert680", out var c680Prop) || c680Prop.GetBoolean();

                        var opt790 = new RemapperOptions
                        {
                            TargetModel = "pss790",
                            EnablePolyphonyGuard = !root.TryGetProperty("polyphonyGuard790", out var pol790Prop)
                                ? (!root.TryGetProperty("polyphonyGuard", out var polProp) || polProp.GetBoolean())
                                : pol790Prop.GetBoolean(),
                            DefaultReverbBoost = !root.TryGetProperty("reverbBoost790", out var rev790Prop)
                                ? (!root.TryGetProperty("reverbBoost", out var revProp) || revProp.GetBoolean())
                                : rev790Prop.GetBoolean(),
                            NormalizeAwmVelocities = !root.TryGetProperty("velocityNorm790", out var vel790Prop)
                                ? (!root.TryGetProperty("normalizeVelocities", out var velProp) || velProp.GetBoolean())
                                : vel790Prop.GetBoolean(),
                            EmulateSustainPedal = !root.TryGetProperty("sustain790", out var sus790Prop)
                                ? (!root.TryGetProperty("emulateSustain", out var susProp) || susProp.GetBoolean())
                                : sus790Prop.GetBoolean(),
                            FoldExpressionPedal = !root.TryGetProperty("expressionFold790", out var exp790Prop)
                                ? (!root.TryGetProperty("foldExpression", out var expProp) || expProp.GetBoolean())
                                : exp790Prop.GetBoolean(),
                            NeutralizeAcousticPitchBends = !root.TryGetProperty("pitchTune790", out var pit790Prop)
                                ? (!root.TryGetProperty("neutralizePitch", out var pitProp) || pitProp.GetBoolean())
                                : pit790Prop.GetBoolean(),
                            EnsureSetupPreRoll = !root.TryGetProperty("preRoll790", out var pre790Prop)
                                ? (!root.TryGetProperty("setupPreRoll", out var preProp) || preProp.GetBoolean())
                                : pre790Prop.GetBoolean(),
                            AutoCenterDanglingPitchBends = true
                        };

                        var opt680 = new RemapperOptions
                        {
                            TargetModel = "pss680",
                            EnablePolyphonyGuard = !root.TryGetProperty("polyphonyGuard680", out var pol680Prop)
                                ? (!root.TryGetProperty("polyphonyGuard", out var polProp2) || polProp2.GetBoolean())
                                : pol680Prop.GetBoolean(),
                            DefaultReverbBoost = false,
                            FilterUnsupportedControllers = !root.TryGetProperty("cleanControllers680", out var cleanProp) || cleanProp.GetBoolean(),
                            NormalizeAwmVelocities = false,
                            BakeFmVelocity = !root.TryGetProperty("fmVelocity680", out var fmVel680Prop) || fmVel680Prop.GetBoolean(),
                            TranslateDrums = !root.TryGetProperty("drums680", out var drums680Prop) || drums680Prop.GetBoolean(),
                            EmulateSustainPedal = !root.TryGetProperty("sustain680", out var sus680Prop)
                                ? (!root.TryGetProperty("emulateSustain", out var susProp2) || susProp2.GetBoolean())
                                : sus680Prop.GetBoolean(),
                            FoldExpressionPedal = !root.TryGetProperty("expressionFold680", out var exp680Prop)
                                ? (!root.TryGetProperty("foldExpression", out var expProp2) || expProp2.GetBoolean())
                                : exp680Prop.GetBoolean(),
                            NeutralizeAcousticPitchBends = !root.TryGetProperty("pitchTune680", out var pit680Prop)
                                ? (!root.TryGetProperty("neutralizePitch", out var pitProp2) || pitProp2.GetBoolean())
                                : pit680Prop.GetBoolean(),
                            EnsureSetupPreRoll = !root.TryGetProperty("preRoll680", out var pre680Prop)
                                ? (!root.TryGetProperty("setupPreRoll", out var preProp2) || preProp2.GetBoolean())
                                : pre680Prop.GetBoolean(),
                            AutoCenterDanglingPitchBends = true
                        };

                        StartBatchConversion(sourceDir, target790, target680, opt790, opt680, convert790, convert680);
                        break;
                    case "cancel_batch":
                        lock (_ctsLock)
                        {
                            try { _batchCts?.Cancel(); } catch { }
                        }
                        break;
                    case "run_health_audit":
                        var auditDir = root.TryGetProperty("directory", out var aProp) && !string.IsNullOrEmpty(aProp.GetString())
                            ? aProp.GetString()! : AppPaths.GmMidiDir;
                        RunHealthAudit(auditDir);
                        break;
                    case "select_input_device":
                        var inDev = root.GetProperty("deviceName").GetString();
                        if (inDev != null)
                        {
                            _recorder.SetInputDevice(inDev);
                            SendDevicesUpdated();
                        }
                        break;
                    case "refresh_input_devices":
                        _deviceMonitor.TriggerRescan(0);
                        SendDevicesUpdated();
                        break;
                    case "start_recording":
                        var bpm = root.TryGetProperty("bpm", out var bProp) ? bProp.GetInt32() : 120;
                        var countIn = root.TryGetProperty("countIn", out var cProp) && cProp.GetBoolean();
                        _ = _recorder.StartRecordingAsync(bpm, countIn);
                        break;
                    case "stop_recording":
                        {
                            var activeModelSnap = _activeModel;
                            bool autoTrim = !root.TryGetProperty("autoTrim", out var atP) || atP.GetBoolean();
                            _ = Task.Run(() =>
                            {
                                _recorder.StopRecording();
                                int trimmedTicks = autoTrim ? _recorder.TrimCurrentTakeSilence() : 0;
                                if (_recorder.LastRecordedFile != null)
                                {
                                    MidiRecorder.ApplyHardwareTrackLabels(_recorder.LastRecordedFile, activeModelSnap);
                                }
                                var stats = _recorder.GetLastTakeStats();
                                var autoSavedPath = _recorder.AutoSaveTake(activeModelSnap);
                                if (!string.IsNullOrEmpty(autoSavedPath))
                                {
                                    SendRecordingsLibraryUpdate();
                                }
                                SendToJs("recorder_take_ready", new
                                {
                                    noteCount = stats.noteCount,
                                    eventCount = stats.eventCount,
                                    durationSeconds = stats.durationSeconds,
                                    durationFormatted = TimeSpan.FromSeconds(stats.durationSeconds).ToString(@"mm\:ss"),
                                    trimmedTicks = trimmedTicks,
                                    autoSavedPath = autoSavedPath
                                });
                            });
                        }
                        break;
                    case "trim_current_take":
                        {
                            var statsBefore = _recorder.GetLastTakeStats();
                            int trimmed = _recorder.TrimCurrentTakeSilence();
                            var stats = _recorder.GetLastTakeStats();
                            SendToJs("recorder_take_ready", new
                            {
                                noteCount = stats.noteCount,
                                eventCount = stats.eventCount,
                                durationSeconds = stats.durationSeconds,
                                durationFormatted = TimeSpan.FromSeconds(stats.durationSeconds).ToString(@"mm\:ss"),
                                trimmedTicks = trimmed
                            });
                            SendToJs("notification", new
                            {
                                message = trimmed > 0
                                    ? $"Trimmed leading silence ({statsBefore.durationSeconds:F1}s -> {stats.durationSeconds:F1}s)."
                                    : "Take already starts cleanly at Beat 1."
                            });
                        }
                        break;
                    case "quantize_current_take":
                        {
                            int grid = root.TryGetProperty("grid", out var gP) ? Math.Clamp(gP.GetInt32(), 4, 32) : 16;
                            int snapped = _recorder.QuantizeCurrentTake(grid);
                            var stats = _recorder.GetLastTakeStats();
                            SendToJs("recorder_take_ready", new
                            {
                                noteCount = stats.noteCount,
                                eventCount = stats.eventCount,
                                durationSeconds = stats.durationSeconds,
                                durationFormatted = TimeSpan.FromSeconds(stats.durationSeconds).ToString(@"mm\:ss"),
                                quantizedGrid = grid
                            });
                            SendToJs("notification", new { message = $"Quantized {snapped} notes cleanly to 1/{grid} grid." });
                        }
                        break;
                    case "preview_take":
                        if (_recorder.LastRecordedFile != null)
                        {
                            _player.LoadMidi(_recorder.LastRecordedFile);
                            _player.Play();
                        }
                        break;
                    case "stop_preview":
                        _player.Stop();
                        break;
                    case "get_recordings_library":
                        SendRecordingsLibraryUpdate();
                        break;
                    case "preview_recording":
                        var prevPath = root.TryGetProperty("filePath", out var ppProp) ? ppProp.GetString() : null;
                        if (!string.IsNullOrEmpty(prevPath) && File.Exists(prevPath))
                        {
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    var prevMidi = MidiFile.Read(prevPath, Constants.DefaultReadingSettings);
                                    _player.LoadMidi(prevMidi);
                                    _player.Play();
                                    SendToJs("recording_preview_started", new { filePath = prevPath, fileName = Path.GetFileName(prevPath) });
                                }
                                catch (Exception ex)
                                {
                                    SendToJs("error", new { message = $"Failed to preview recording: {ex.Message}" });
                                }
                            });
                        }
                        break;
                    case "stop_recording_preview":
                        _player.Stop();
                        SendToJs("recording_preview_stopped", new { });
                        break;
                    case "load_recording_in_player":
                        var recToLoad = root.TryGetProperty("filePath", out var rlp) ? rlp.GetString() : null;
                        if (!string.IsNullOrEmpty(recToLoad) && File.Exists(recToLoad) && IsPathInsideDirectory(recToLoad, AppPaths.RecordingsDir))
                        {
                            LoadFileAsync(recToLoad, autoPlay: false);
                            SendToJs("switch_tab", new { tab = "player" });
                        }
                        break;
                    case "export_recording_to_gm":
                        var expGmPath = root.TryGetProperty("filePath", out var egProp) ? egProp.GetString() : null;
                        var customGmName = root.TryGetProperty("fileName", out var cgnProp) ? cgnProp.GetString() : null;
                        if (!string.IsNullOrEmpty(expGmPath))
                        {
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    var exportedGm = _recorder.ExportRecordingToGeneralMidi(expGmPath, customGmName);
                                    SendLibraryUpdate();
                                    SendToJs("notification", new { message = $"Exported to General MIDI library: {Path.GetFileName(exportedGm)}" });
                                }
                                catch (Exception ex)
                                {
                                    SendToJs("error", new { message = $"Failed to export to GM: {ex.Message}" });
                                }
                            });
                        }
                        break;
                    case "export_recording_to_pss":
                        var expPssPath = root.TryGetProperty("filePath", out var epProp) ? epProp.GetString() : null;
                        var customPssName = root.TryGetProperty("fileName", out var cpnProp) ? cpnProp.GetString() : null;
                        if (!string.IsNullOrEmpty(expPssPath))
                        {
                            var targetModelSnap = _activeModel;
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    var modelLabel = targetModelSnap == "pss680" ? "PSS-680" : "PSS-790";
                                    var exportedPss = _recorder.ExportRecordingToPss(expPssPath, customPssName, targetModelSnap);
                                    SendLibraryUpdate();
                                    SendToJs("notification", new { message = $"Exported to {modelLabel} library: {Path.GetFileName(exportedPss)}" });
                                }
                                catch (Exception ex)
                                {
                                    SendToJs("error", new { message = $"Failed to export to {(targetModelSnap == "pss680" ? "PSS-680" : "PSS-790")}: {ex.Message}" });
                                }
                            });
                        }
                        break;
                    case "delete_recording":
                        var delRecPath = root.TryGetProperty("filePath", out var drProp) ? drProp.GetString() : null;
                        if (!string.IsNullOrEmpty(delRecPath) && AppPaths.IsPathInsideDirectory(delRecPath, AppPaths.RecordingsDir))
                        {
                            _recorder.DeleteRecording(delRecPath, AppPaths.RecordingsDir);
                            SendRecordingsLibraryUpdate();
                            SendToJs("notification", new { message = $"Deleted recording take: {Path.GetFileName(delRecPath)}" });
                        }
                        break;
                    case "rename_recording":
                        var renRecPath = root.TryGetProperty("filePath", out var rrProp) ? rrProp.GetString() : null;
                        var newRecName = root.TryGetProperty("newName", out var nrProp) ? nrProp.GetString() : null;
                        if (!string.IsNullOrEmpty(renRecPath) && !string.IsNullOrWhiteSpace(newRecName) && AppPaths.IsPathInsideDirectory(renRecPath, AppPaths.RecordingsDir))
                        {
                            try
                            {
                                var renamedPath = _recorder.RenameRecording(renRecPath, newRecName, AppPaths.RecordingsDir);
                                SendRecordingsLibraryUpdate();
                                SendToJs("notification", new { message = $"Renamed recording to: {Path.GetFileName(renamedPath)}" });
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = $"Failed to rename recording: {ex.Message}" });
                            }
                        }
                        break;
                    case "open_recordings_folder":
                        AppPaths.EnsureDirectoriesExist();
                        AppPaths.OpenFolderInExplorer(AppPaths.RecordingsDir);
                        break;
                    case "save_take":
                        var takeFileName = root.TryGetProperty("fileName", out var fnProp) ? fnProp.GetString() ?? $"Take_{DateTime.Now:yyyyMMdd_HHmmss}.mid" : $"Take_{DateTime.Now:yyyyMMdd_HHmmss}.mid";
                        var isNativePss = root.TryGetProperty("isNativePss", out var nProp) ? nProp.GetBoolean() : (root.TryGetProperty("remapForPss790", out var rProp) && rProp.GetBoolean());
                        var takeModelSnap = _activeModel;
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                var savedPath = isNativePss
                                    ? _recorder.SaveNativeTake(takeFileName, takeModelSnap)
                                    : _recorder.ExportTakeToGeneralMidi(takeFileName, takeModelSnap);
                                SendLibraryUpdate();
                                SendRecordingsLibraryUpdate();
                                SendToJs("take_saved", new
                                {
                                    filePath = savedPath,
                                    fileName = Path.GetFileName(savedPath),
                                    isPss790 = isNativePss && takeModelSnap != "pss680",
                                    isPss680 = isNativePss && takeModelSnap == "pss680",
                                    activeModel = takeModelSnap
                                });
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = $"Failed to save take: {ex.Message}" });
                            }
                        });
                        break;
                    case "send_take_to_player":
                        var takeFile = _recorder.LastRecordedFile;
                        if (takeFile != null)
                        {
                            var title = root.TryGetProperty("title", out var tTitle) ? tTitle.GetString() ?? "Recorded Take" : "Recorded Take";
                            var cloned = takeFile.Clone();
                            MidiRecorder.EmbedRecordingMarker(cloned, _activeModel);
                            LoadMidiObject(cloned, title);
                            SendToJs("switch_tab", new { tab = "player" });
                        }
                        break;
                    case "set_midi_thru":
                        var thru = root.GetProperty("enabled").GetBoolean();
                        _recorder.MidiThruEnabled = thru;
                        break;
                    case "get_sysex_library":
                        SendSysExLibraryUpdate();
                        break;
                    case "start_sysex_capture":
                        _sysexManager.StartCapture();
                        break;
                    case "stop_sysex_capture":
                        _sysexManager.StopCapture(autoSave: true);
                        break;
                    case "transmit_sysex_dump":
                        var txPath = root.TryGetProperty("filePath", out var txP) ? txP.GetString() : null;
                        var txDelay = root.TryGetProperty("packetDelayMs", out var pdP)
                            ? pdP.GetInt32()
                            : (root.TryGetProperty("delayMs", out var txdP) ? txdP.GetInt32() : 30);
                        if (!string.IsNullOrEmpty(txPath))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _sysexManager.TransmitDumpAsync(txPath, txDelay);
                                }
                                catch (Exception ex)
                                {
                                    SendToJs("error", new { message = $"SysEx transmission failed: {ex.Message}" });
                                }
                            });
                        }
                        break;
                    case "cancel_sysex_transmit":
                        _sysexManager.CancelTransmission();
                        break;
                    case "delete_sysex_dump":
                        var delPath = root.TryGetProperty("filePath", out var delP) ? delP.GetString() : null;
                        if (!string.IsNullOrEmpty(delPath) && AppPaths.IsPathInsideDirectory(delPath, AppPaths.SysExDir))
                        {
                            _sysexManager.DeleteDump(delPath, AppPaths.SysExDir);
                            SendSysExLibraryUpdate();
                            SendToJs("notification", new { message = "SysEx backup deleted." });
                        }
                        break;
                    case "rename_sysex_dump":
                        var renPath = root.TryGetProperty("filePath", out var renP) ? renP.GetString() : null;
                        var newName = root.TryGetProperty("newFileName", out var nfnP)
                            ? nfnP.GetString()
                            : (root.TryGetProperty("newName", out var nnP) ? nnP.GetString() : null);
                        if (!string.IsNullOrEmpty(renPath) && !string.IsNullOrWhiteSpace(newName) && AppPaths.IsPathInsideDirectory(renPath, AppPaths.SysExDir))
                        {
                            try
                            {
                                var renamed = _sysexManager.RenameDump(renPath, newName, AppPaths.SysExDir);
                                SendSysExLibraryUpdate();
                                SendToJs("notification", new { message = $"Renamed to: {Path.GetFileName(renamed)}" });
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = ex.Message });
                            }
                        }
                        break;
                    case "import_sysex_dump":
                        if (root.TryGetProperty("base64Data", out var b64Prop) && !string.IsNullOrWhiteSpace(b64Prop.GetString()))
                        {
                            try
                            {
                                var rawName = root.TryGetProperty("fileName", out var impNameProp) ? impNameProp.GetString() : null;
                                var safeBase = SanitizeFileName(Path.GetFileNameWithoutExtension(rawName ?? "Imported_SysEx"));
                                byte[] bytes = Convert.FromBase64String(b64Prop.GetString()!);
                                var packets = SysExManager.ExtractPackets(bytes);
                                if (packets.Count == 0)
                                {
                                    SendToJs("error", new { message = "Selected file contains no valid SysEx packets (F0 ... F7)." });
                                    break;
                                }

                                AppPaths.EnsureDirectoriesExist();
                                var outPath = Path.Combine(AppPaths.SysExDir, safeBase + ".syx");
                                int suffix = 1;
                                while (File.Exists(outPath))
                                {
                                    outPath = Path.Combine(AppPaths.SysExDir, $"{safeBase}_{suffix++}.syx");
                                }

                                if (AppPaths.IsPathInsideDirectory(outPath, AppPaths.SysExDir))
                                {
                                    File.WriteAllBytes(outPath, bytes);
                                    SendSysExLibraryUpdate();
                                    SendToJs("notification", new { message = $"Imported SysEx dump: {Path.GetFileName(outPath)}" });
                                }
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = $"Failed to import SysEx dump: {ex.Message}" });
                            }
                        }
                        else
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    var dlg = new OpenFileDialog
                                    {
                                        Filter = "SysEx Files (*.syx)|*.syx|All Files (*.*)|*.*",
                                        Title = "Import System Exclusive Dump File"
                                    };
                                    if (dlg.ShowDialog() == true)
                                    {
                                        var imported = _sysexManager.ImportDump(dlg.FileName);
                                        SendSysExLibraryUpdate();
                                        SendToJs("notification", new { message = $"Imported SysEx dump: {Path.GetFileName(imported)}" });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    SendToJs("error", new { message = $"Failed to import SysEx dump: {ex.Message}" });
                                }
                            });
                        }
                        break;
                    case "inspect_sysex_dump":
                        var inspPath = root.TryGetProperty("filePath", out var ipP) ? ipP.GetString() : null;
                        if (!string.IsNullOrEmpty(inspPath))
                        {
                            if (!AppPaths.IsPathInsideDirectory(inspPath, AppPaths.SysExDir))
                            {
                                SendToJs("error", new { message = "Cannot inspect files outside the SysEx Vault." });
                                break;
                            }
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    var info = SysExManager.ParseDumpInfo(inspPath);
                                    var meta = InspectPss680SysExMetadata(inspPath);
                                    SendToJs("sysex_dump_inspected", new
                                    {
                                        fileName = info.FileName,
                                        filePath = info.FilePath,
                                        fileSizeBytes = info.FileSizeBytes,
                                        fileSizeFormatted = info.FileSizeFormatted,
                                        createdAtFormatted = info.CreatedAtFormatted,
                                        packetCount = info.PacketCount,
                                        manufacturer = info.Manufacturer,
                                        dumpType = info.DumpType,
                                        hexPreview = info.HexPreview,
                                        formatCode = meta.FormatCode,
                                        canExtractMidi = meta.CanExtractMidi,
                                        canOpenDrummerGrid = meta.CanOpenDrummerGrid,
                                        decodedSummary = meta.SummaryText
                                    });
                                }
                                catch (Exception ex)
                                {
                                    SendToJs("error", new { message = $"Failed to inspect SysEx dump: {ex.Message}" });
                                }
                            });
                        }
                        break;
                    case "extract_sysex_to_midi":
                        {
                            var extPath = root.TryGetProperty("filePath", out var epP) ? epP.GetString() : null;
                            if (string.IsNullOrEmpty(extPath) || !File.Exists(extPath) || !AppPaths.IsPathInsideDirectory(extPath, AppPaths.SysExDir))
                            {
                                SendToJs("error", new { message = "SysEx file not found inside SysEx Vault." });
                                break;
                            }
                            _ = Task.Run(() => HandleExtractSysExToMidi(extPath));
                        }
                        break;
                    case "load_drummer_pattern_sysex":
                        {
                            var drPath = root.TryGetProperty("filePath", out var drP) ? drP.GetString() : null;
                            if (string.IsNullOrEmpty(drPath) || !File.Exists(drPath) || !AppPaths.IsPathInsideDirectory(drPath, AppPaths.SysExDir))
                            {
                                SendToJs("error", new { message = "Custom Drummer SysEx file not found." });
                                break;
                            }
                            _ = Task.Run(() => HandleLoadDrummerSysEx(drPath));
                        }
                        break;
                    case "build_drummer_sysex":
                        {
                            HandleBuildDrummerSysEx(root);
                        }
                        break;
                    case "trigger_drum_pad":
                        {
                            int padNote = root.TryGetProperty("note", out var pnP) ? Math.Clamp(pnP.GetInt32(), 24, 108) : 45;
                            int padVel = root.TryGetProperty("velocity", out var pvP) ? Math.Clamp(pvP.GetInt32(), 1, 127) : 110;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    _player.SendEvent(new NoteOnEvent((SevenBitNumber)padNote, (SevenBitNumber)padVel) { Channel = (FourBitNumber)15 });
                                    await Task.Delay(140);
                                    _player.SendEvent(new NoteOffEvent((SevenBitNumber)padNote, (SevenBitNumber)0) { Channel = (FourBitNumber)15 });
                                }
                                catch { }
                            });
                        }
                        break;
                    case "send_quick_sysex":
                        var cmd = root.TryGetProperty("command", out var cCmd) ? cCmd.GetString() : null;
                        if (!string.IsNullOrEmpty(cmd))
                        {
                            _sysexManager.SendQuickCommand(cmd);
                            var lowerCmd = cmd.ToLowerInvariant();
                            if (lowerCmd is "mode_99" or "pss680_mode_99")
                            {
                                SendToJs("notification", new { message = "PSS-680 Sound Source Mode: Press [MIDI MODE] on keyboard, then press [+] so display shows 'c.99'. (Channels reset)" });
                            }
                            else if (lowerCmd is "req_voice_dump" or "pss680_req_voice"
                                or "req_melody_dump" or "pss680_req_melody"
                                or "req_chord_dump" or "pss680_req_chord"
                                or "req_rhythm_dump" or "pss680_req_rhythm")
                            {
                                SendToJs("notification", new { message = "PSS-680/480/780 ignores remote dump requests. Arm SysEx Capture, then hold [MIDI] + press the front-panel dump key." });
                            }
                            else if (lowerCmd == "all_reset")
                            {
                                SendToJs("notification", new { message = "Sent All Notes Off & Reset Controllers panic across all 16 MIDI channels." });
                            }
                            else
                            {
                                SendToJs("notification", new { message = $"Transmitted SysEx command: {cmd.ToUpperInvariant()}" });
                            }
                        }
                        break;
                    case "open_sysex_folder":
                        AppPaths.OpenSysExFolderInExplorer();
                        break;
                    case "transmit_fm_voice":
                        {
                            var fmVoice = ParseVoiceFromJson(root);
                            byte[] sysex = fmVoice.ToSysExMessage();
                            _player.SendSysEx(sysex);
                            var isSilent = root.TryGetProperty("silent", out var s) && s.GetBoolean();

                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(35);
                                _player.SendVoiceOverride(0, 100 + fmVoice.BankNumber, "pss680");
                            });

                            SendToJs("fm_active_bank_changed", new { bank = fmVoice.BankNumber, voiceNumber = 100 + fmVoice.BankNumber });
                            if (!isSilent)
                            {
                                SendToJs("notification", new { message = $"Transmitted FM Voice '{fmVoice.Name}' to PSS-680 (Bank {fmVoice.BankNumber + 1})" });
                            }
                        }
                        break;
                    case "select_fm_bank":
                        {
                            var bank = root.TryGetProperty("bank", out var bP) ? Math.Clamp(bP.GetInt32(), 0, 4) : 0;
                            _player.SendVoiceOverride(0, 100 + bank, "pss680");
                            SendToJs("fm_active_bank_changed", new { bank, voiceNumber = 100 + bank });
                            SendToJs("notification", new { message = $"Switched PSS-680 to Bank {bank + 1} (Voice {100 + bank})" });
                        }
                        break;
                    case "audition_fm_voice_note":
                        {
                            var note = root.TryGetProperty("note", out var nP) ? Math.Clamp(nP.GetInt32(), 24, 108) : 60;
                            var bank = root.TryGetProperty("bank", out var bP) ? Math.Clamp(bP.GetInt32(), 0, 4) : 0;
                            var isChord = root.TryGetProperty("isChord", out var cP) && cP.GetBoolean();
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    _player.SendVoiceOverride(0, 100 + bank, "pss680");
                                    await Task.Delay(35);

                                    if (isChord)
                                    {
                                        int n1 = note;
                                        int n2 = Math.Clamp(note + 4, 24, 108);
                                        int n3 = Math.Clamp(note + 7, 24, 108);
                                        _player.SendEvent(new NoteOnEvent((SevenBitNumber)n1, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
                                        _player.SendEvent(new NoteOnEvent((SevenBitNumber)n2, (SevenBitNumber)95) { Channel = (FourBitNumber)0 });
                                        _player.SendEvent(new NoteOnEvent((SevenBitNumber)n3, (SevenBitNumber)95) { Channel = (FourBitNumber)0 });
                                        await Task.Delay(850);
                                        _player.SendEvent(new NoteOffEvent((SevenBitNumber)n1, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                                        _player.SendEvent(new NoteOffEvent((SevenBitNumber)n2, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                                        _player.SendEvent(new NoteOffEvent((SevenBitNumber)n3, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                                    }
                                    else
                                    {
                                        _player.SendEvent(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
                                        await Task.Delay(800);
                                        _player.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                                    }
                                }
                                catch { }
                            });
                        }
                        break;
                    case "save_fm_voice_patch":
                        {
                            var fmVoice = ParseVoiceFromJson(root);
                            byte[] sysex = fmVoice.ToSysExMessage();
                            var safeName = SanitizeFileName(fmVoice.Name);
                            if (!safeName.EndsWith(".syx", StringComparison.OrdinalIgnoreCase)) safeName += ".syx";
                            AppPaths.EnsureDirectoriesExist();
                            var outPath = Path.Combine(AppPaths.FmVoicesDir, safeName);
                            File.WriteAllBytes(outPath, sysex);
                            SendFmVoiceLibraryUpdate();
                            SendToJs("notification", new { message = $"Saved FM Voice patch: {Path.GetFileNameWithoutExtension(safeName)}" });
                        }
                        break;
                    case "get_fm_voice_library":
                        SendFmVoiceLibraryUpdate();
                        break;
                    case "load_fm_voice_patch":
                        {
                            var vPath = root.TryGetProperty("filePath", out var vp) ? vp.GetString() : null;
                            if (string.IsNullOrEmpty(vPath) && root.TryGetProperty("fileName", out var fn))
                            {
                                vPath = Path.Combine(AppPaths.FmVoicesDir, Path.GetFileName(fn.GetString() ?? ""));
                            }
                            if (!string.IsNullOrEmpty(vPath) && File.Exists(vPath) && IsPathInsideDirectory(vPath, AppPaths.FmVoicesDir))
                            {
                                byte[] bytes = File.ReadAllBytes(vPath);
                                var fmVoice = PSS680Voice.FromSysExMessage(bytes);
                                fmVoice.Name = Path.GetFileNameWithoutExtension(vPath);
                                _player.SendSysEx(bytes);

                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(35);
                                    _player.SendVoiceOverride(0, 100 + fmVoice.BankNumber, "pss680");
                                });

                                SendToJs("fm_voice_loaded", VoiceToObject(fmVoice));
                                SendToJs("fm_active_bank_changed", new { bank = fmVoice.BankNumber, voiceNumber = 100 + fmVoice.BankNumber });
                                SendToJs("notification", new { message = $"Loaded & Transmitted FM Voice: {fmVoice.Name} (Bank {fmVoice.BankNumber + 1})" });
                            }
                        }
                        break;
                    case "delete_fm_voice_patch":
                        {
                            var delFmPath = root.TryGetProperty("filePath", out var dp) ? dp.GetString() : null;
                            if (!string.IsNullOrEmpty(delFmPath) && File.Exists(delFmPath) && IsPathInsideDirectory(delFmPath, AppPaths.FmVoicesDir))
                            {
                                File.Delete(delFmPath);
                                SendFmVoiceLibraryUpdate();
                                SendToJs("notification", new { message = "FM Voice patch deleted." });
                            }
                        }
                        break;
                    case "set_vector_voices":
                        {
                            var vA = root.TryGetProperty("voiceA", out var vaP) ? Math.Clamp(vaP.GetInt32(), 0, 99) : 0;
                            var vB = root.TryGetProperty("voiceB", out var vbP) ? Math.Clamp(vbP.GetInt32(), 0, 99) : 34;
                            var vC = root.TryGetProperty("voiceC", out var vcP) ? Math.Clamp(vcP.GetInt32(), 0, 99) : 16;
                            var vD = root.TryGetProperty("voiceD", out var vdP) ? Math.Clamp(vdP.GetInt32(), 0, 99) : 56;
                            _player.SendVoiceOverride(0, vA);
                            _player.SendVoiceOverride(1, vB);
                            _player.SendVoiceOverride(2, vC);
                            _player.SendVoiceOverride(3, vD);
                        }
                        break;
                    case "set_vector_volumes":
                        {
                            var volA = root.TryGetProperty("volA", out var va) ? Math.Clamp(va.GetInt32(), 0, 127) : 64;
                            var volB = root.TryGetProperty("volB", out var vb) ? Math.Clamp(vb.GetInt32(), 0, 127) : 64;
                            var volC = root.TryGetProperty("volC", out var vc) ? Math.Clamp(vc.GetInt32(), 0, 127) : 64;
                            var volD = root.TryGetProperty("volD", out var vd) ? Math.Clamp(vd.GetInt32(), 0, 127) : 64;
                            if (_player.VectorQuadPlaybackEnabled)
                            {
                                _player.SetChannelVolume(0, (byte)Math.Clamp((int)Math.Round(volA * 100.0 / 127.0), 0, 100));
                                _player.SetChannelVolume(1, (byte)Math.Clamp((int)Math.Round(volB * 100.0 / 127.0), 0, 100));
                                _player.SetChannelVolume(2, (byte)Math.Clamp((int)Math.Round(volC * 100.0 / 127.0), 0, 100));
                                _player.SetChannelVolume(3, (byte)Math.Clamp((int)Math.Round(volD * 100.0 / 127.0), 0, 100));
                            }
                            else
                            {
                                _player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volA) { Channel = (FourBitNumber)0 });
                                _player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volB) { Channel = (FourBitNumber)1 });
                                _player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volC) { Channel = (FourBitNumber)2 });
                                _player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volD) { Channel = (FourBitNumber)3 });
                            }
                        }
                        break;
                    case "set_vector_channel_effect":
                        {
                            var targetVecCh = root.TryGetProperty("channel", out var chProp) ? Math.Clamp(chProp.GetInt32(), 0, 3) : 0;
                            if (root.TryGetProperty("reverb", out var vRevProp))
                            {
                                var rev = Math.Clamp(vRevProp.GetInt32(), 0, 3);
                                byte cc91Val = rev switch { 1 => 42, 2 => 85, 3 => 127, _ => 0 };
                                _player.SendEvent(new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)cc91Val) { Channel = (FourBitNumber)targetVecCh });
                            }
                            if (root.TryGetProperty("vibrato", out var vVibProp))
                            {
                                var vib = Math.Clamp(vVibProp.GetInt32(), 0, 7);
                                byte cc1Val = (byte)Math.Clamp(vib * 18, 0, 127);
                                _player.SendEvent(new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)cc1Val) { Channel = (FourBitNumber)targetVecCh });
                            }
                        }
                        break;
                    case "get_settings":
                        SendToJs("settings_data", new
                        {
                            libraryRoot = AppPaths.BaseMidiDir,
                            defaultLibraryRoot = AppPaths.DefaultBaseMidiDir,
                            isCustom = !string.IsNullOrWhiteSpace(AppSettings.CustomLibraryRoot)
                        });
                        break;
                    case "browse_library_folder":
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    var dlg = new OpenFolderDialog
                                    {
                                        Title = "Select Yamaha PortaSound Studio Library Root Folder",
                                        InitialDirectory = Directory.Exists(AppPaths.BaseMidiDir) ? AppPaths.BaseMidiDir : AppPaths.DefaultBaseMidiDir
                                    };
                                    if (dlg.ShowDialog() == true)
                                    {
                                        SendToJs("library_folder_selected", new { path = dlg.FolderName });
                                    }
                                }
                                catch (Exception ex)
                                {
                                    SendToJs("notification", new { message = $"Folder selection error: {ex.Message}" });
                                }
                            });
                        }
                        break;
                    case "save_settings":
                        {
                            var newRoot = root.TryGetProperty("libraryRoot", out var lrProp) ? lrProp.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(newRoot))
                            {
                                AppPaths.SetCustomLibraryRoot(newRoot.Trim());
                                _ = Task.Run(() =>
                                {
                                    AppPaths.EnsureDirectoriesExist();
                                    AppPaths.SyncMirroredDirectories();
                                    SendLibraryUpdate();
                                    SendSysExLibraryUpdate();
                                    SendFmVoiceLibraryUpdate();
                                    SendVectorLibraryUpdate();
                                    SendRecordingsLibraryUpdate();
                                    SendToJs("settings_saved", new
                                    {
                                        success = true,
                                        libraryRoot = AppPaths.BaseMidiDir,
                                        defaultLibraryRoot = AppPaths.DefaultBaseMidiDir,
                                        isCustom = !string.IsNullOrWhiteSpace(AppSettings.CustomLibraryRoot),
                                        message = $"Library storage location updated to: {AppPaths.BaseMidiDir}"
                                    });
                                });
                            }
                        }
                        break;
                    case "reset_library_path":
                        {
                            AppPaths.SetCustomLibraryRoot(null);
                            _ = Task.Run(() =>
                            {
                                AppPaths.EnsureDirectoriesExist();
                                AppPaths.SyncMirroredDirectories();
                                SendLibraryUpdate();
                                SendSysExLibraryUpdate();
                                SendFmVoiceLibraryUpdate();
                                SendVectorLibraryUpdate();
                                SendRecordingsLibraryUpdate();
                                SendToJs("settings_saved", new
                                {
                                    success = true,
                                    libraryRoot = AppPaths.BaseMidiDir,
                                    defaultLibraryRoot = AppPaths.DefaultBaseMidiDir,
                                    isCustom = false,
                                    message = $"Library storage location reset to default: {AppPaths.BaseMidiDir}"
                                });
                            });
                        }
                        break;
                    case "audition_vector_chord":
                        {
                            var note = root.TryGetProperty("note", out var nP) ? Math.Clamp(nP.GetInt32(), 24, 108) : 60;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    for (int ch = 0; ch < 4; ch++)
                                    {
                                        _player.SendEvent(new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)100) { Channel = (FourBitNumber)ch });
                                    }
                                    await Task.Delay(850);
                                    for (int ch = 0; ch < 4; ch++)
                                    {
                                        _player.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0) { Channel = (FourBitNumber)ch });
                                    }
                                }
                                catch { }
                            });
                        }
                        break;
                    case "set_vector_playback_mode":
                        {
                            bool enabled = root.TryGetProperty("enabled", out var enP) && enP.GetBoolean();
                            _player.VectorQuadPlaybackEnabled = enabled;

                            if (enabled)
                            {
                                var vA = root.TryGetProperty("voiceA", out var vaP) ? Math.Clamp(vaP.GetInt32(), 0, 99) : 65;
                                var vB = root.TryGetProperty("voiceB", out var vbP) ? Math.Clamp(vbP.GetInt32(), 0, 99) : 65;
                                var vC = root.TryGetProperty("voiceC", out var vcP) ? Math.Clamp(vcP.GetInt32(), 0, 99) : 0;
                                var vD = root.TryGetProperty("voiceD", out var vdP) ? Math.Clamp(vdP.GetInt32(), 0, 99) : 7;

                                bool hasSongLoaded;
                                lock (_loadFileLock)
                                {
                                    if (_preVectorVoiceOverrides == null)
                                    {
                                        _preVectorVoiceOverrides = new Dictionary<int, int?>();
                                        for (int c = 0; c < 4; c++)
                                        {
                                            _preVectorVoiceOverrides[c] = _options.ChannelVoiceOverrides.TryGetValue(c, out var existingOv)
                                                ? existingOv
                                                : null;
                                        }
                                    }
                                    _options.ChannelVoiceOverrides[0] = vA;
                                    _options.ChannelVoiceOverrides[1] = vB;
                                    _options.ChannelVoiceOverrides[2] = vC;
                                    _options.ChannelVoiceOverrides[3] = vD;
                                    hasSongLoaded = _loadedOriginalMidi != null;
                                }

                                _player.SendVoiceOverride(0, vA);
                                _player.SendVoiceOverride(1, vB);
                                _player.SendVoiceOverride(2, vC);
                                _player.SendVoiceOverride(3, vD);

                                if (root.TryGetProperty("volA", out var va) && root.TryGetProperty("volB", out var vb) &&
                                    root.TryGetProperty("volC", out var vc) && root.TryGetProperty("volD", out var vd))
                                {
                                    _player.SetChannelVolume(0, (byte)Math.Clamp((int)Math.Round(va.GetInt32() * 100.0 / 127.0), 0, 100));
                                    _player.SetChannelVolume(1, (byte)Math.Clamp((int)Math.Round(vb.GetInt32() * 100.0 / 127.0), 0, 100));
                                    _player.SetChannelVolume(2, (byte)Math.Clamp((int)Math.Round(vc.GetInt32() * 100.0 / 127.0), 0, 100));
                                    _player.SetChannelVolume(3, (byte)Math.Clamp((int)Math.Round(vd.GetInt32() * 100.0 / 127.0), 0, 100));
                                }

                                if (hasSongLoaded)
                                {
                                    _ = Task.Run(() => ReapplyRemapping(preservePosition: true));
                                }
                            }
                            else
                            {
                                bool hasSongLoaded;
                                lock (_loadFileLock)
                                {
                                    if (_preVectorVoiceOverrides != null)
                                    {
                                        foreach (var (c, prevOv) in _preVectorVoiceOverrides)
                                        {
                                            if (prevOv.HasValue)
                                                _options.ChannelVoiceOverrides[c] = prevOv;
                                            else
                                                _options.ChannelVoiceOverrides.Remove(c);
                                        }
                                        _preVectorVoiceOverrides = null;
                                    }
                                    else
                                    {
                                        for (int c = 0; c < 4; c++)
                                        {
                                            _options.ChannelVoiceOverrides.Remove(c);
                                        }
                                    }
                                    hasSongLoaded = _loadedOriginalMidi != null;
                                }

                                for (int c = 0; c < 4; c++)
                                {
                                    _player.SetChannelVolume(c, 100);
                                }

                                if (hasSongLoaded)
                                {
                                    _ = Task.Run(() =>
                                    {
                                        ReapplyRemapping(preservePosition: true);
                                        lock (_loadFileLock)
                                        {
                                            if (_loadedOriginalMidi != null)
                                            {
                                                var fmt = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                                                var chInfo = AnalyzeChannels(_loadedOriginalMidi, _loadedRemappedMidi, fmt);
                                                SendToJs("channels_updated", new { channels = chInfo });
                                            }
                                        }
                                    });
                                }
                            }
                            SendToJs("vector_playback_mode_changed", new { enabled });
                        }
                        break;
                    case "save_vector_preset":
                        {
                            var pName = root.TryGetProperty("name", out var pnP) ? pnP.GetString() : "Vector Preset";
                            var safeName = SanitizeFileName(pName ?? "Vector Preset");
                            if (!safeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) safeName += ".json";
                            AppPaths.EnsureDirectoriesExist();
                            var outPath = Path.Combine(AppPaths.VectorPresetsDir, safeName);
                            File.WriteAllText(outPath, root.GetRawText());
                            SendVectorLibraryUpdate();
                            SendToJs("notification", new { message = $"Saved Vector preset: {Path.GetFileNameWithoutExtension(safeName)}" });
                        }
                        break;
                    case "get_vector_library":
                        SendVectorLibraryUpdate();
                        break;
                    case "load_vector_preset":
                        {
                            var vPath = root.TryGetProperty("filePath", out var vp) ? vp.GetString() : null;
                            if (string.IsNullOrEmpty(vPath) && root.TryGetProperty("fileName", out var fn))
                            {
                                vPath = Path.Combine(AppPaths.VectorPresetsDir, Path.GetFileName(fn.GetString() ?? ""));
                            }
                            if (!string.IsNullOrEmpty(vPath) && File.Exists(vPath) && IsPathInsideDirectory(vPath, AppPaths.VectorPresetsDir))
                            {
                                var jsonText = File.ReadAllText(vPath);
                                using var vecDoc = JsonDocument.Parse(jsonText);
                                SendToJs("vector_preset_loaded", vecDoc.RootElement.Clone());
                                SendToJs("notification", new { message = $"Loaded Vector preset: {Path.GetFileNameWithoutExtension(vPath)}" });
                            }
                        }
                        break;
                    case "delete_vector_preset":
                        {
                            var delVecPath = root.TryGetProperty("filePath", out var dp) ? dp.GetString() : null;
                            if (!string.IsNullOrEmpty(delVecPath) && File.Exists(delVecPath) && IsPathInsideDirectory(delVecPath, AppPaths.VectorPresetsDir))
                            {
                                File.Delete(delVecPath);
                                SendVectorLibraryUpdate();
                                SendToJs("notification", new { message = "Vector preset deleted." });
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = ex.Message });
            }
        }

        public void HandleGetInitialData()
        {
            if (_isDisposed) return;

            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                try
                {
                    var devices = MidiPlayer.GetOutputDeviceNames();
                    var inputDevices = MidiRecorder.GetInputDeviceNames();
                    var voices = Constants.Pss790Voices.Values
                        .OrderBy(v => v.VoiceNumber)
                        .Select(v => new
                        {
                            number = v.VoiceNumber,
                            name = v.Name,
                            category = v.Category,
                            midiProgram = v.MidiProgram
                        })
                        .ToList();

                    var pss680Voices = Pss680Constants.Voices.Values
                        .OrderBy(v => v.Number)
                        .Select(v => new
                        {
                            number = v.Number,
                            name = v.Name,
                            category = v.Category,
                            midiProgram = (byte)v.Number
                        })
                        .ToList();

                    var pss680Drums = Pss680Constants.DrumMap.Values
                        .Select(d => new
                        {
                            noteNumber = d.NoteNumber,
                            name = d.Name,
                            pad = d.PadSetAndNumber,
                            noteName = d.NoteName
                        })
                        .ToList();

                    var libraryData = ScanLibraryFiles();
                    var sysexLib = _sysexManager.GetSysExLibrary();
                    var recordingsLib = _recorder.GetRecordingsLibrary();
                    var fmLib = GetFmVoiceLibraryData();
                    var vecLib = GetVectorLibraryData();
                    var favs = FavoriteStore.Instance.GetAllFavorites();

                    int masterTranspose;
                    string pitchBendMode;
                    Dictionary<int, int> octaveShifts;
                    lock (_loadFileLock)
                    {
                        masterTranspose = _options.MasterTranspose;
                        pitchBendMode = _options.PitchBendMode.ToString().ToLowerInvariant();
                        octaveShifts = new Dictionary<int, int>(_options.ChannelOctaveShifts);
                    }

                    // Auto-connect default MIDI ports BEFORE sending initial_data so reported port names reflect actual connections
                    if (string.IsNullOrEmpty(_player.CurrentOutputDeviceName) && devices.Count > 0)
                    {
                        _player.SetOutputDevice(devices[0]);
                    }

                    if (string.IsNullOrEmpty(_recorder.CurrentInputDeviceName) && inputDevices.Count > 0)
                    {
                        _recorder.SetInputDevice(inputDevices[0]);
                    }

                    var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.9.4";

                    SendToJs("initial_data", new
                    {
                        devices,
                        inputDevices,
                        voices,
                        pss790Voices = voices,
                        pss680Voices,
                        pss680Drums,
                        library = libraryData,
                        defaultDevice = _player.CurrentOutputDeviceName,
                        defaultInputDevice = _recorder.CurrentInputDeviceName,
                        appVersion,
                        defaultBatchSource = AppPaths.GmMidiDir,
                        defaultBatchTarget = AppPaths.Pss790MidiDir,
                        defaultBatchTarget790 = AppPaths.Pss790MidiDir,
                        defaultBatchTarget680 = AppPaths.Pss680MidiDir,
                        libraryRoot = AppPaths.BaseMidiDir,
                        defaultLibraryRoot = AppPaths.DefaultBaseMidiDir,
                        isCustomLibraryRoot = !string.IsNullOrWhiteSpace(AppSettings.CustomLibraryRoot),
                        midiThru = _recorder.MidiThruEnabled,
                        recorderBpm = _recorder.Bpm,
                        masterTranspose,
                        pitchBendMode,
                        channelOctaveShifts = octaveShifts,
                        sysexLibrary = sysexLib,
                        recordingsLibrary = recordingsLib,
                        fmVoiceLibrary = fmLib,
                        vectorLibrary = vecLib,
                        favorites = favs
                    });
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Failed to get initial data: {ex.Message}" });
                }
            });
        }

        private void HandleCreateFolder(JsonElement root)
        {
            var parentSubFolder = root.TryGetProperty("parentSubFolder", out var pProp) ? pProp.GetString() ?? "" : "";
            var folderName = root.TryGetProperty("folderName", out var fnProp) ? fnProp.GetString()?.Trim() : null;

            if (string.IsNullOrWhiteSpace(folderName))
            {
                SendToJs("error", new { message = "Folder name cannot be empty." });
                return;
            }

            var invalid = Path.GetInvalidFileNameChars();
            if (folderName.IndexOfAny(invalid) >= 0)
            {
                SendToJs("error", new { message = "Folder name contains invalid characters." });
                return;
            }

            var gmTarget = Path.Combine(AppPaths.GmMidiDir, parentSubFolder, folderName);
            var pss790Target = Path.Combine(AppPaths.Pss790MidiDir, parentSubFolder, folderName);
            var pss680Target = Path.Combine(AppPaths.Pss680MidiDir, parentSubFolder, folderName);

            if (!AppPaths.IsPathInsideLibrary(gmTarget) || !AppPaths.IsPathInsideLibrary(pss790Target) || !AppPaths.IsPathInsideLibrary(pss680Target))
            {
                SendToJs("error", new { message = "Invalid directory path." });
                return;
            }

            if (Directory.Exists(gmTarget) || Directory.Exists(pss790Target) || Directory.Exists(pss680Target))
            {
                SendToJs("error", new { message = $"Folder '{folderName}' already exists." });
                return;
            }

            Directory.CreateDirectory(gmTarget);
            Directory.CreateDirectory(pss790Target);
            Directory.CreateDirectory(pss680Target);
            SendLibraryUpdate();
            SendToJs("notification", new { message = $"Created folder across libraries: {folderName}" });
        }

        private void HandleRenameItem(JsonElement root)
        {
            var fullPath = root.TryGetProperty("fullPath", out var fp) ? fp.GetString() : null;
            var newName = root.TryGetProperty("newName", out var nn) ? nn.GetString()?.Trim() : null;
            var isFolder = root.TryGetProperty("isFolder", out var ifProp) && ifProp.GetBoolean();

            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(newName))
            {
                SendToJs("error", new { message = "Invalid path or name." });
                return;
            }

            if (!AppPaths.IsPathInsideLibrary(fullPath))
            {
                SendToJs("error", new { message = "Cannot rename items outside library." });
                return;
            }

            var invalid = Path.GetInvalidFileNameChars();
            if (newName.IndexOfAny(invalid) >= 0)
            {
                SendToJs("error", new { message = "Name contains invalid characters." });
                return;
            }

            var parentDir = Path.GetDirectoryName(fullPath) ?? "";

            if (isFolder)
            {
                if (!Directory.Exists(fullPath))
                {
                    SendToJs("error", new { message = "Folder not found." });
                    return;
                }

                var newPath = Path.Combine(parentDir, newName);
                if (!AppPaths.IsPathInsideLibrary(newPath))
                {
                    SendToJs("error", new { message = "Invalid target folder path." });
                    return;
                }

                if (Directory.Exists(newPath))
                {
                    SendToJs("error", new { message = $"Folder '{newName}' already exists." });
                    return;
                }

                Directory.Move(fullPath, newPath);
                VoiceOverrideStore.Instance.RenameFolder(fullPath, newPath);
                FavoriteStore.Instance.RenameFolder(fullPath, newPath);
                UpdateCurrentFilePathAfterFolderMove(fullPath, newPath);

                try
                {
                    string? srcRoot = null;
                    if (IsPathInsideDirectory(fullPath, AppPaths.GmMidiDir)) srcRoot = AppPaths.GmMidiDir;
                    else if (IsPathInsideDirectory(fullPath, AppPaths.Pss790MidiDir)) srcRoot = AppPaths.Pss790MidiDir;
                    else if (IsPathInsideDirectory(fullPath, AppPaths.Pss680MidiDir)) srcRoot = AppPaths.Pss680MidiDir;

                    if (srcRoot != null)
                    {
                        var allRoots = new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir };
                        var relFolder = Path.GetRelativePath(srcRoot, fullPath);

                        foreach (var mirrorRoot in allRoots)
                        {
                            if (string.Equals(mirrorRoot, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;

                            var mirrorFolder = Path.Combine(mirrorRoot, relFolder);
                            if (Directory.Exists(mirrorFolder))
                            {
                                var mirrorParent = Path.GetDirectoryName(mirrorFolder) ?? mirrorRoot;
                                var newMirrorPath = Path.Combine(mirrorParent, newName);
                                if (!Directory.Exists(newMirrorPath))
                                {
                                    Directory.Move(mirrorFolder, newMirrorPath);
                                    VoiceOverrideStore.Instance.RenameFolder(mirrorFolder, newMirrorPath);
                                    FavoriteStore.Instance.RenameFolder(mirrorFolder, newMirrorPath);
                                    UpdateCurrentFilePathAfterFolderMove(mirrorFolder, newMirrorPath);
                                }
                            }
                        }
                    }
                }
                catch { }

                SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
                SendLibraryUpdate();
                SendToJs("notification", new { message = $"Renamed folder to: {newName}" });
            }
            else
            {
                if (!File.Exists(fullPath))
                {
                    SendToJs("error", new { message = "File not found." });
                    return;
                }

                if (!newName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
                    !newName.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                {
                    newName += ".mid";
                }

                var newPath = Path.Combine(parentDir, newName);
                if (!AppPaths.IsPathInsideLibrary(newPath))
                {
                    SendToJs("error", new { message = "Invalid target file path." });
                    return;
                }

                if (File.Exists(newPath))
                {
                    SendToJs("error", new { message = $"File '{newName}' already exists." });
                    return;
                }

                File.Move(fullPath, newPath);
                VoiceOverrideStore.Instance.RenameFile(fullPath, newPath);
                FavoriteStore.Instance.RenameFile(fullPath, newPath);
                UpdateCurrentFilePathIfMatch(fullPath, newPath);

                SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
                SendLibraryUpdate();
                SendToJs("notification", new { message = $"Renamed song to: {newName}" });
            }
        }

        private void HandleMoveFile(JsonElement root)
        {
            var sourcePath = root.TryGetProperty("sourcePath", out var sp) ? sp.GetString() : null;
            var targetCategory = root.TryGetProperty("targetCategory", out var tc) ? tc.GetString() : "gm";
            var targetSubFolder = root.TryGetProperty("targetSubFolder", out var tsf) ? tsf.GetString() ?? "" : "";
            var alsoMoveLinked = root.TryGetProperty("alsoMoveLinked", out var aml) && aml.GetBoolean();
            var linkedPath = root.TryGetProperty("linkedPath", out var lp) ? lp.GetString() : null;

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                SendToJs("error", new { message = "Source file not found." });
                return;
            }

            if (!AppPaths.IsPathInsideLibrary(sourcePath))
            {
                SendToJs("error", new { message = "Cannot move files outside library." });
                return;
            }

            var baseDir = string.Equals(targetCategory, "pss680", StringComparison.OrdinalIgnoreCase)
                ? AppPaths.Pss680MidiDir
                : string.Equals(targetCategory, "pss790", StringComparison.OrdinalIgnoreCase)
                    ? AppPaths.Pss790MidiDir
                    : AppPaths.GmMidiDir;

            var destDir = Path.Combine(baseDir, targetSubFolder);
            if (!AppPaths.IsPathInsideLibrary(destDir) && !string.Equals(Path.GetFullPath(destDir), Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase))
            {
                SendToJs("error", new { message = "Invalid destination folder." });
                return;
            }

            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(destDir, fileName);

            if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(destPath))
            {
                SendToJs("error", new { message = $"A file named '{fileName}' already exists in the destination folder." });
                return;
            }

            File.Move(sourcePath, destPath);
            VoiceOverrideStore.Instance.RenameFile(sourcePath, destPath);
            FavoriteStore.Instance.RenameFile(sourcePath, destPath);
            UpdateCurrentFilePathIfMatch(sourcePath, destPath);

            var movedLinkedCount = 0;
            if (alsoMoveLinked)
            {
                var linkedFilesToMove = new List<(string filePath, string targetDir)>();

                var isSourceGm = IsPathInsideDirectory(sourcePath, AppPaths.GmMidiDir);
                if (isSourceGm)
                {
                    var link790 = AppPaths.FindMatchingLinkedFile(sourcePath, "pss790");
                    if (!string.IsNullOrEmpty(link790) && File.Exists(link790))
                    {
                        linkedFilesToMove.Add((link790, Path.Combine(AppPaths.Pss790MidiDir, targetSubFolder)));
                    }

                    var link680 = AppPaths.FindMatchingLinkedFile(sourcePath, "pss680");
                    if (!string.IsNullOrEmpty(link680) && File.Exists(link680))
                    {
                        linkedFilesToMove.Add((link680, Path.Combine(AppPaths.Pss680MidiDir, targetSubFolder)));
                    }
                }
                else
                {
                    var linkGm = AppPaths.FindMatchingLinkedFile(sourcePath);
                    if (!string.IsNullOrEmpty(linkGm) && File.Exists(linkGm))
                    {
                        linkedFilesToMove.Add((linkGm, Path.Combine(AppPaths.GmMidiDir, targetSubFolder)));

                        var is680 = IsPathInsideDirectory(sourcePath, AppPaths.Pss680MidiDir);
                        var otherModel = is680 ? "pss790" : "pss680";
                        var otherRoot = is680 ? AppPaths.Pss790MidiDir : AppPaths.Pss680MidiDir;
                        var otherLink = AppPaths.FindMatchingLinkedFile(linkGm, otherModel);
                        if (!string.IsNullOrEmpty(otherLink) && File.Exists(otherLink))
                        {
                            linkedFilesToMove.Add((otherLink, Path.Combine(otherRoot, targetSubFolder)));
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(linkedPath) && File.Exists(linkedPath) && !linkedFilesToMove.Any(x => string.Equals(x.filePath, linkedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    var isLinkedGm = IsPathInsideDirectory(linkedPath, AppPaths.GmMidiDir);
                    var mirrorBase = isLinkedGm ? AppPaths.GmMidiDir : (IsPathInsideDirectory(linkedPath, AppPaths.Pss680MidiDir) ? AppPaths.Pss680MidiDir : AppPaths.Pss790MidiDir);
                    linkedFilesToMove.Add((linkedPath, Path.Combine(mirrorBase, targetSubFolder)));
                }

                foreach (var (lFile, mDestDir) in linkedFilesToMove)
                {
                    if (!Directory.Exists(mDestDir))
                    {
                        Directory.CreateDirectory(mDestDir);
                    }

                    var lFileName = Path.GetFileName(lFile);
                    var mDestPath = Path.Combine(mDestDir, lFileName);

                    if (!string.Equals(lFile, mDestPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(mDestPath))
                    {
                        File.Move(lFile, mDestPath);
                        VoiceOverrideStore.Instance.RenameFile(lFile, mDestPath);
                        FavoriteStore.Instance.RenameFile(lFile, mDestPath);
                        UpdateCurrentFilePathIfMatch(lFile, mDestPath);
                        movedLinkedCount++;
                    }
                }
            }

            SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
            SendLibraryUpdate();
            var folderLabel = string.IsNullOrEmpty(targetSubFolder) ? "(Root)" : targetSubFolder;
            SendToJs("notification", new { message = movedLinkedCount > 0 ? $"Moved song (+ {movedLinkedCount} linked across libraries) to {folderLabel}" : $"Moved '{fileName}' to {folderLabel}" });
        }

        private void HandleBatchMoveFiles(JsonElement root)
        {
            var targetCategory = root.TryGetProperty("targetCategory", out var tc) ? tc.GetString() : "gm";
            var targetSubFolder = root.TryGetProperty("targetSubFolder", out var tsf) ? tsf.GetString() ?? "" : "";
            var alsoMoveLinked = root.TryGetProperty("alsoMoveLinked", out var aml) && aml.GetBoolean();

            var sourcePaths = new List<string>();
            if (root.TryGetProperty("sourcePaths", out var spArray) && spArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in spArray.EnumerateArray())
                {
                    var p = el.GetString();
                    if (!string.IsNullOrEmpty(p)) sourcePaths.Add(p);
                }
            }
            else if (root.TryGetProperty("sourcePath", out var spProp) && !string.IsNullOrEmpty(spProp.GetString()))
            {
                sourcePaths.Add(spProp.GetString()!);
            }

            if (sourcePaths.Count == 0)
            {
                SendToJs("error", new { message = "No files selected to move." });
                return;
            }

            var baseDir = string.Equals(targetCategory, "pss680", StringComparison.OrdinalIgnoreCase)
                ? AppPaths.Pss680MidiDir
                : string.Equals(targetCategory, "pss790", StringComparison.OrdinalIgnoreCase)
                    ? AppPaths.Pss790MidiDir
                    : AppPaths.GmMidiDir;

            var destDir = Path.Combine(baseDir, targetSubFolder);
            if (!AppPaths.IsPathInsideLibrary(destDir) && !string.Equals(Path.GetFullPath(destDir), Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase))
            {
                SendToJs("error", new { message = "Invalid destination folder." });
                return;
            }

            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            int movedCount = 0;
            int movedLinkedCount = 0;

            foreach (var sourcePath in sourcePaths)
            {
                if (!File.Exists(sourcePath) || !AppPaths.IsPathInsideLibrary(sourcePath)) continue;

                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(destDir, fileName);

                if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(destPath)) continue;

                    File.Move(sourcePath, destPath);
                    VoiceOverrideStore.Instance.RenameFile(sourcePath, destPath);
                    FavoriteStore.Instance.RenameFile(sourcePath, destPath);
                    UpdateCurrentFilePathIfMatch(sourcePath, destPath);
                    movedCount++;
                }

                if (alsoMoveLinked)
                {
                    var isSourceGm = IsPathInsideDirectory(sourcePath, AppPaths.GmMidiDir);
                    var linkedFilesToMove = new List<(string filePath, string targetDir)>();

                    if (isSourceGm)
                    {
                        var link790 = AppPaths.FindMatchingLinkedFile(sourcePath, "pss790");
                        if (!string.IsNullOrEmpty(link790) && File.Exists(link790))
                        {
                            linkedFilesToMove.Add((link790, Path.Combine(AppPaths.Pss790MidiDir, targetSubFolder)));
                        }

                        var link680 = AppPaths.FindMatchingLinkedFile(sourcePath, "pss680");
                        if (!string.IsNullOrEmpty(link680) && File.Exists(link680))
                        {
                            linkedFilesToMove.Add((link680, Path.Combine(AppPaths.Pss680MidiDir, targetSubFolder)));
                        }
                    }
                    else
                    {
                        var linkGm = AppPaths.FindMatchingLinkedFile(sourcePath);
                        if (!string.IsNullOrEmpty(linkGm) && File.Exists(linkGm))
                        {
                            linkedFilesToMove.Add((linkGm, Path.Combine(AppPaths.GmMidiDir, targetSubFolder)));

                            var is680 = IsPathInsideDirectory(sourcePath, AppPaths.Pss680MidiDir);
                            var otherModel = is680 ? "pss790" : "pss680";
                            var otherRoot = is680 ? AppPaths.Pss790MidiDir : AppPaths.Pss680MidiDir;
                            var otherLink = AppPaths.FindMatchingLinkedFile(linkGm, otherModel);
                            if (!string.IsNullOrEmpty(otherLink) && File.Exists(otherLink))
                            {
                                linkedFilesToMove.Add((otherLink, Path.Combine(otherRoot, targetSubFolder)));
                            }
                        }
                    }

                    foreach (var (lFile, mDestDir) in linkedFilesToMove)
                    {
                        if (!Directory.Exists(mDestDir))
                        {
                            Directory.CreateDirectory(mDestDir);
                        }

                        var lFileName = Path.GetFileName(lFile);
                        var mDestPath = Path.Combine(mDestDir, lFileName);

                        if (!string.Equals(lFile, mDestPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(mDestPath))
                        {
                            File.Move(lFile, mDestPath);
                            VoiceOverrideStore.Instance.RenameFile(lFile, mDestPath);
                            FavoriteStore.Instance.RenameFile(lFile, mDestPath);
                            UpdateCurrentFilePathIfMatch(lFile, mDestPath);
                            movedLinkedCount++;
                        }
                    }
                }
            }

            SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
            SendLibraryUpdate();
            var folderLabel = string.IsNullOrEmpty(targetSubFolder) ? "(Root)" : targetSubFolder;
            var msg = movedLinkedCount > 0
                ? $"Moved {movedCount} songs (+ {movedLinkedCount} linked across libraries) to {folderLabel}"
                : $"Moved {movedCount} song{(movedCount == 1 ? "" : "s")} to {folderLabel}";
            SendToJs("notification", new { message = msg });
        }

        private void HandleMoveFolder(JsonElement root)
        {
            var sourceFolderPath = root.TryGetProperty("sourceFolderPath", out var sfp) ? sfp.GetString() : null;
            var targetCategory = root.TryGetProperty("targetCategory", out var tc) ? tc.GetString() : "gm";
            var targetParentSubFolder = root.TryGetProperty("targetParentSubFolder", out var tps) ? tps.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
            {
                SendToJs("error", new { message = "Source folder not found." });
                return;
            }

            if (!AppPaths.IsPathInsideLibrary(sourceFolderPath))
            {
                SendToJs("error", new { message = "Cannot move folders outside library." });
                return;
            }

            var baseDir = string.Equals(targetCategory, "pss680", StringComparison.OrdinalIgnoreCase)
                ? AppPaths.Pss680MidiDir
                : string.Equals(targetCategory, "pss790", StringComparison.OrdinalIgnoreCase)
                    ? AppPaths.Pss790MidiDir
                    : AppPaths.GmMidiDir;

            var folderName = Path.GetFileName(sourceFolderPath);
            var destDir = Path.Combine(baseDir, targetParentSubFolder, folderName);

            if (!AppPaths.IsPathInsideLibrary(destDir))
            {
                SendToJs("error", new { message = "Invalid destination folder path." });
                return;
            }

            // Prevent moving folder into itself or its own subfolder
            if (IsPathInsideDirectory(destDir, sourceFolderPath) ||
                string.Equals(Path.GetFullPath(destDir), Path.GetFullPath(sourceFolderPath), StringComparison.OrdinalIgnoreCase))
            {
                SendToJs("error", new { message = "Cannot move a folder into itself." });
                return;
            }

            if (Directory.Exists(destDir))
            {
                SendToJs("error", new { message = $"Folder '{folderName}' already exists in destination." });
                return;
            }

            var destParent = Path.GetDirectoryName(destDir) ?? baseDir;
            if (!Directory.Exists(destParent))
            {
                Directory.CreateDirectory(destParent);
            }

            Directory.Move(sourceFolderPath, destDir);
            VoiceOverrideStore.Instance.RenameFolder(sourceFolderPath, destDir);
            FavoriteStore.Instance.RenameFolder(sourceFolderPath, destDir);
            UpdateCurrentFilePathAfterFolderMove(sourceFolderPath, destDir);

            try
            {
                string? srcRoot = null;
                if (IsPathInsideDirectory(sourceFolderPath, AppPaths.GmMidiDir)) srcRoot = AppPaths.GmMidiDir;
                else if (IsPathInsideDirectory(sourceFolderPath, AppPaths.Pss790MidiDir)) srcRoot = AppPaths.Pss790MidiDir;
                else if (IsPathInsideDirectory(sourceFolderPath, AppPaths.Pss680MidiDir)) srcRoot = AppPaths.Pss680MidiDir;

                if (srcRoot != null)
                {
                    var allRoots = new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir };
                    var relFolder = Path.GetRelativePath(srcRoot, sourceFolderPath);

                    foreach (var mirrorRoot in allRoots)
                    {
                        if (string.Equals(mirrorRoot, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;

                        var mirrorSourceFolder = Path.Combine(mirrorRoot, relFolder);
                        if (Directory.Exists(mirrorSourceFolder))
                        {
                            var mirrorDestDir = Path.Combine(mirrorRoot, targetParentSubFolder, folderName);
                            if (!AppPaths.IsPathInsideLibrary(mirrorDestDir)) continue;

                            var mirrorDestParent = Path.GetDirectoryName(mirrorDestDir) ?? mirrorRoot;
                            if (!Directory.Exists(mirrorDestParent))
                            {
                                Directory.CreateDirectory(mirrorDestParent);
                            }

                            if (!Directory.Exists(mirrorDestDir))
                            {
                                Directory.Move(mirrorSourceFolder, mirrorDestDir);
                                VoiceOverrideStore.Instance.RenameFolder(mirrorSourceFolder, mirrorDestDir);
                                FavoriteStore.Instance.RenameFolder(mirrorSourceFolder, mirrorDestDir);
                                UpdateCurrentFilePathAfterFolderMove(mirrorSourceFolder, mirrorDestDir);
                            }
                        }
                    }
                }
            }
            catch { }

            SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
            SendLibraryUpdate();
            SendToJs("notification", new { message = $"Moved folder '{folderName}' across libraries" });
        }

        private void HandleCheckLinkedFile(JsonElement root)
        {
            var isFolder = root.TryGetProperty("isFolder", out var ifProp) && ifProp.GetBoolean();

            if (root.TryGetProperty("filePaths", out var fpArray) && fpArray.ValueKind == JsonValueKind.Array)
            {
                var paths = new List<string>();
                int linkedFound = 0;
                foreach (var el in fpArray.EnumerateArray())
                {
                    var p = el.GetString();
                    if (!string.IsNullOrEmpty(p))
                    {
                        paths.Add(p);
                        if (AppPaths.FindAllMatchingLinkedFiles(p).Count > 0) linkedFound++;
                    }
                }
                SendToJs("linked_file_checked", new
                {
                    isBatch = true,
                    totalCount = paths.Count,
                    hasLinked = linkedFound > 0,
                    linkedCount = linkedFound,
                    isFolder = false
                });
                return;
            }

            var filePath = root.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                SendToJs("linked_file_checked", new { filePath = "", hasLinked = false, linkedPath = (string?)null, linkedName = (string?)null, isFolder = false });
                return;
            }

            if (isFolder)
            {
                string? srcRoot = null;
                if (IsPathInsideDirectory(filePath, AppPaths.GmMidiDir)) srcRoot = AppPaths.GmMidiDir;
                else if (IsPathInsideDirectory(filePath, AppPaths.Pss790MidiDir)) srcRoot = AppPaths.Pss790MidiDir;
                else if (IsPathInsideDirectory(filePath, AppPaths.Pss680MidiDir)) srcRoot = AppPaths.Pss680MidiDir;

                var counterpartFound = false;
                string? firstCounterpartPath = null;

                if (srcRoot != null)
                {
                    var allRoots = new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir };
                    var rel = Path.GetRelativePath(srcRoot, filePath);

                    foreach (var r in allRoots)
                    {
                        if (string.Equals(r, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;
                        var cand = Path.Combine(r, rel);
                        if (Directory.Exists(cand))
                        {
                            counterpartFound = true;
                            firstCounterpartPath ??= cand;
                        }
                    }
                }

                SendToJs("linked_file_checked", new
                {
                    filePath,
                    hasLinked = counterpartFound,
                    linkedPath = counterpartFound ? firstCounterpartPath : null,
                    linkedName = counterpartFound && firstCounterpartPath != null ? AppPaths.GetRelativeLibraryPath(firstCounterpartPath) : null,
                    isFolder = true
                });
                return;
            }

            var allLinked = AppPaths.FindAllMatchingLinkedFiles(filePath);
            var linked = allLinked.FirstOrDefault(f => f.Contains(_activeModel == "pss680" ? "PSS-680" : "PSS-790")) ?? allLinked.FirstOrDefault();

            SendToJs("linked_file_checked", new
            {
                filePath,
                hasLinked = linked != null,
                linkedPath = linked,
                linkedName = linked != null ? AppPaths.GetRelativeLibraryPath(linked) : null,
                isFolder = false
            });
        }

        private void HandleBatchDeleteFiles(JsonElement root)
        {
            var alsoDeleteLinked = root.TryGetProperty("alsoDeleteLinked", out var adl) && adl.GetBoolean();
            var filePaths = new List<string>();

            if (root.TryGetProperty("filePaths", out var fpArray) && fpArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in fpArray.EnumerateArray())
                {
                    var p = el.GetString();
                    if (!string.IsNullOrEmpty(p)) filePaths.Add(p);
                }
            }
            else if (root.TryGetProperty("fullPath", out var fpProp) && !string.IsNullOrEmpty(fpProp.GetString()))
            {
                filePaths.Add(fpProp.GetString()!);
            }

            if (filePaths.Count == 0)
            {
                SendToJs("error", new { message = "No files selected to delete." });
                return;
            }

            int deletedCount = 0;
            int deletedLinkedCount = 0;

            foreach (var fullPath in filePaths)
            {
                if (!File.Exists(fullPath) || !AppPaths.IsPathInsideLibrary(fullPath)) continue;

                ClearCurrentFileIfMatches(fullPath, isFolder: false);

                VoiceOverrideStore.Instance.RemoveOverrides(fullPath);
                File.Delete(fullPath);
                deletedCount++;

                if (alsoDeleteLinked)
                {
                    var allLinked = AppPaths.FindAllMatchingLinkedFiles(fullPath);
                    foreach (var linkedPath in allLinked)
                    {
                        if (File.Exists(linkedPath) && AppPaths.IsPathInsideLibrary(linkedPath))
                        {
                            ClearCurrentFileIfMatches(linkedPath, isFolder: false);

                            VoiceOverrideStore.Instance.RemoveOverrides(linkedPath);
                            File.Delete(linkedPath);
                            deletedLinkedCount++;
                        }
                    }
                }

                if (alsoDeleteLinked || AppPaths.FindAllMatchingLinkedFiles(fullPath).Count == 0)
                {
                    FavoriteStore.Instance.RemoveFavorite(fullPath);
                }
            }

            SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
            SendLibraryUpdate();
            var msg = deletedLinkedCount > 0
                ? $"Deleted {deletedCount} songs (+ {deletedLinkedCount} linked across libraries)"
                : $"Deleted {deletedCount} song{(deletedCount == 1 ? "" : "s")}";
            SendToJs("notification", new { message = msg });
        }

        private void HandleDeleteItem(JsonElement root)
        {
            var fullPath = root.TryGetProperty("fullPath", out var fp) ? fp.GetString() : null;
            var isFolder = root.TryGetProperty("isFolder", out var ifProp) && ifProp.GetBoolean();
            var alsoDeleteLinked = root.TryGetProperty("alsoDeleteLinked", out var adl) && adl.GetBoolean();
            var linkedPath = root.TryGetProperty("linkedPath", out var lp) ? lp.GetString() : null;

            if (string.IsNullOrWhiteSpace(fullPath))
            {
                SendToJs("error", new { message = "Invalid path." });
                return;
            }

            if (!AppPaths.IsPathInsideLibrary(fullPath))
            {
                SendToJs("error", new { message = "Cannot delete files outside library." });
                return;
            }

            if (isFolder)
            {
                if (Directory.Exists(fullPath))
                {
                    ClearCurrentFileIfMatches(fullPath, isFolder: true);

                    VoiceOverrideStore.Instance.RemoveFolderOverrides(fullPath);
                    Directory.Delete(fullPath, recursive: true);

                    var deletedMirrored = false;
                    if (alsoDeleteLinked)
                    {
                        string? srcRoot = null;
                        if (IsPathInsideDirectory(fullPath, AppPaths.GmMidiDir)) srcRoot = AppPaths.GmMidiDir;
                        else if (IsPathInsideDirectory(fullPath, AppPaths.Pss790MidiDir)) srcRoot = AppPaths.Pss790MidiDir;
                        else if (IsPathInsideDirectory(fullPath, AppPaths.Pss680MidiDir)) srcRoot = AppPaths.Pss680MidiDir;

                        if (srcRoot != null)
                        {
                            var allRoots = new[] { AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir };
                            var relFolder = Path.GetRelativePath(srcRoot, fullPath);

                            foreach (var mirrorRoot in allRoots)
                            {
                                if (string.Equals(mirrorRoot, srcRoot, StringComparison.OrdinalIgnoreCase)) continue;

                                var mirrorFolder = Path.Combine(mirrorRoot, relFolder);
                                if (Directory.Exists(mirrorFolder) && AppPaths.IsPathInsideLibrary(mirrorFolder))
                                {
                                    ClearCurrentFileIfMatches(mirrorFolder, isFolder: true);

                                    VoiceOverrideStore.Instance.RemoveFolderOverrides(mirrorFolder);
                                    Directory.Delete(mirrorFolder, recursive: true);
                                    deletedMirrored = true;
                                }
                            }
                        }
                    }

                    if (alsoDeleteLinked)
                    {
                        FavoriteStore.Instance.RemoveFolder(fullPath);
                    }

                    SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
                    SendLibraryUpdate();
                    SendToJs("notification", new { message = deletedMirrored ? $"Deleted folder across libraries: {Path.GetFileName(fullPath)}" : $"Deleted folder: {Path.GetFileName(fullPath)}" });
                }
            }
            else
            {
                if (File.Exists(fullPath))
                {
                    ClearCurrentFileIfMatches(fullPath, isFolder: false);

                    VoiceOverrideStore.Instance.RemoveOverrides(fullPath);
                    File.Delete(fullPath);

                    var deletedLinkedCount = 0;
                    if (alsoDeleteLinked)
                    {
                        var allLinked = AppPaths.FindAllMatchingLinkedFiles(fullPath);
                        if (!string.IsNullOrWhiteSpace(linkedPath) && File.Exists(linkedPath) && !allLinked.Contains(linkedPath, StringComparer.OrdinalIgnoreCase))
                        {
                            allLinked.Add(linkedPath);
                        }

                        foreach (var lPath in allLinked)
                        {
                            if (File.Exists(lPath) && AppPaths.IsPathInsideLibrary(lPath))
                            {
                                ClearCurrentFileIfMatches(lPath, isFolder: false);

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

                    SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
                    SendLibraryUpdate();
                    var notificationMsg = deletedLinkedCount > 0
                        ? $"Deleted song (+ {deletedLinkedCount} linked across libraries): {Path.GetFileName(fullPath)}"
                        : $"Deleted: {Path.GetFileName(fullPath)}";
                    SendToJs("notification", new { message = notificationMsg });
                }
            }
        }

        private void HandleRevealInExplorer(JsonElement root)
        {
            var fullPath = root.TryGetProperty("fullPath", out var fp) ? fp.GetString() : null;
            if (!string.IsNullOrWhiteSpace(fullPath))
            {
                AppPaths.RevealInExplorer(fullPath);
            }
        }

        private void SendLibraryUpdate()
        {
            if (_isDisposed) return;

            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                var libraryData = ScanLibraryFiles();
                SendToJs("library_updated", libraryData);
            });
        }

        private object ScanLibraryFiles()
        {
            AppPaths.EnsureDirectoriesExist();
            AppPaths.SyncMirroredDirectories();

            var allFlat = new List<FileItemDto>();

            var gmTree = BuildCategoryTree(AppPaths.GmMidiDir, "GM Midi", "gm", allFlat);
            var pss790Tree = BuildCategoryTree(AppPaths.Pss790MidiDir, "PSS-790 Midi", "pss790", allFlat);
            var pss680Tree = BuildCategoryTree(AppPaths.Pss680MidiDir, "PSS-680 Midi", "pss680", allFlat);

            bool is680 = string.Equals(_activeModel, "pss680", StringComparison.OrdinalIgnoreCase);
            var pssTree = is680 ? pss680Tree : pss790Tree;

            var gmFiles = allFlat.Where(f => f.format == "gm").ToList();
            var pss790Files = allFlat.Where(f => f.format == "pss790").ToList();
            var pss680Files = allFlat.Where(f => f.format == "pss680").ToList();
            var pssFiles = is680 ? pss680Files : pss790Files;

            return new
            {
                gmTree,
                pssTree,
                pss790Tree,
                pss680Tree,
                gmFiles,
                pssFiles,
                pss790Files,
                pss680Files,
                flatFiles = allFlat,
                hasFiles = allFlat.Count > 0,
                baseDir = AppPaths.BaseMidiDir,
                activeModel = _activeModel,
                favorites = FavoriteStore.Instance.GetAllFavorites()
            };
        }

        public class FileItemDto
        {
            public string fileName { get; set; } = "";
            public string fullPath { get; set; } = "";
            public string relativePath { get; set; } = "";
            public string subFolder { get; set; } = "";
            public string category { get; set; } = "";
            public string format { get; set; } = "";
            public bool isFavorite { get; set; }
        }

        public class FolderNodeDto
        {
            public string name { get; set; } = "";
            public string fullPath { get; set; } = "";
            public string relativePath { get; set; } = "";
            public List<FolderNodeDto> subFolders { get; set; } = new();
            public List<FileItemDto> files { get; set; } = new();
            public int directCount { get; set; } = 0;
            public int totalCount { get; set; } = 0;
        }

        private static object BuildCategoryTree(string rootDir, string categoryName, string format, List<FileItemDto> flatCollector)
        {
            if (!Directory.Exists(rootDir))
            {
                return new
                {
                    name = categoryName,
                    fullPath = rootDir,
                    format,
                    subFolders = new List<FolderNodeDto>(),
                    folders = new List<object>(),
                    allFolders = new List<object>(),
                    files = new List<FileItemDto>(),
                    directCount = 0,
                    count = 0
                };
            }

            var allFoldersList = new List<object>();
            var folderGroups = new Dictionary<string, List<FileItemDto>>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in Directory.GetDirectories(rootDir, "*", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(rootDir, dir);
                allFoldersList.Add(new
                {
                    name = rel,
                    fullPath = dir
                });
                folderGroups[rel] = new List<FileItemDto>();
            }

            var rootNode = BuildDirectoryNode(rootDir, rootDir, categoryName, format, flatCollector);

            foreach (var file in flatCollector.Where(f => f.category == categoryName))
            {
                var sub = file.subFolder;
                if (!string.IsNullOrEmpty(sub) && folderGroups.TryGetValue(sub, out var list))
                {
                    list.Add(file);
                }
            }

            var legacyFoldersList = folderGroups
                .Where(kvp => kvp.Value.Count > 0)
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new
                {
                    name = kvp.Key,
                    fullPath = Path.Combine(rootDir, kvp.Key),
                    files = kvp.Value,
                    count = kvp.Value.Count
                })
                .ToList();

            return new
            {
                name = categoryName,
                fullPath = rootDir,
                format,
                subFolders = rootNode.subFolders,
                folders = legacyFoldersList,
                allFolders = allFoldersList,
                files = rootNode.files,
                directCount = rootNode.directCount,
                count = rootNode.totalCount
            };
        }

        private static FolderNodeDto BuildDirectoryNode(string currentDir, string rootDir, string categoryName, string format, List<FileItemDto> flatCollector)
        {
            var dirName = Path.GetFileName(currentDir);
            var relPath = string.Equals(currentDir, rootDir, StringComparison.OrdinalIgnoreCase)
                ? ""
                : Path.GetRelativePath(rootDir, currentDir);

            var directFiles = new List<FileItemDto>();
            if (Directory.Exists(currentDir))
            {
                var filePaths = Directory.GetFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                foreach (var file in filePaths)
                {
                    var fileName = Path.GetFileName(file);
                    var fileRel = Path.GetRelativePath(rootDir, file);
                    var fileSub = Path.GetDirectoryName(fileRel) ?? "";

                    var fileItem = new FileItemDto
                    {
                        fileName = fileName,
                        fullPath = file,
                        relativePath = fileRel,
                        subFolder = fileSub,
                        category = categoryName,
                        format = format,
                        isFavorite = FavoriteStore.Instance.IsFavorite(file)
                    };

                    flatCollector.Add(fileItem);
                    directFiles.Add(fileItem);
                }
            }

            var subFolderNodes = new List<FolderNodeDto>();
            if (Directory.Exists(currentDir))
            {
                var subDirs = Directory.GetDirectories(currentDir, "*", SearchOption.TopDirectoryOnly)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                foreach (var subDir in subDirs)
                {
                    var child = BuildDirectoryNode(subDir, rootDir, categoryName, format, flatCollector);
                    if (child.totalCount > 0)
                    {
                        subFolderNodes.Add(child);
                    }
                }
            }

            var directCount = directFiles.Count;
            var totalCount = directCount + subFolderNodes.Sum(s => s.totalCount);

            return new FolderNodeDto
            {
                name = string.IsNullOrEmpty(dirName) ? categoryName : dirName,
                fullPath = currentDir,
                relativePath = relPath,
                subFolders = subFolderNodes,
                files = directFiles,
                directCount = directCount,
                totalCount = totalCount
            };
        }

        private void BrowseAndLoadFile()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "MIDI Files (*.mid;*.midi)|*.mid;*.midi|All Files (*.*)|*.*",
                    InitialDirectory = AppPaths.BaseMidiDir,
                    Title = "Open Standard MIDI File"
                };

                if (dialog.ShowDialog() == true)
                {
                    LoadFileAsync(dialog.FileName);
                }
            });
        }

        public void LoadMidiObject(MidiFile midi, string title, string? virtualPath = null)
        {
            int gen = Interlocked.Increment(ref _loadGeneration);
            _ = Task.Run(() =>
            {
                if (_isDisposed || Volatile.Read(ref _loadGeneration) != gen) return;

                try
                {
                    _player.Stop();
                    ResetChannelActivity();

                    var modelSnapshot = _activeModel;
                    lock (_loadFileLock)
                    {
                        if (Volatile.Read(ref _loadGeneration) != gen) return;
                        _currentFilePath = virtualPath ?? title;
                        _loadedOriginalMidi = midi;
                    }

                    var format = Remapper.DetectMidiFormat(midi);
                    bool isRec = Remapper.IsRecording(midi);
                    ReapplyRemapping(preservePosition: false);

                    MidiFile? remappedSnapshot;
                    lock (_loadFileLock)
                    {
                        remappedSnapshot = _loadedRemappedMidi;
                    }

                    var channelInfo = AnalyzeChannels(midi, remappedSnapshot, format);
                    var duration = _player.TotalDuration;
                    if (duration <= TimeSpan.Zero)
                    {
                        duration = (remappedSnapshot ?? midi).GetDuration<MetricTimeSpan>();
                    }

                    var modelTag = modelSnapshot == "pss680" ? "PSS-680" : "PSS-790";
                    bool isNativeThisModel = modelSnapshot == "pss680" ? (format == MidiFormat.Pss680Native) : (format == MidiFormat.Pss790Native);
                    bool isNativeCounterpart = modelSnapshot == "pss680" ? (format == MidiFormat.Pss790Native) : (format == MidiFormat.Pss680Native);

                    string fmtCode = isRec ? "rec" : (isNativeThisModel ? modelSnapshot : (isNativeCounterpart ? (modelSnapshot == "pss680" ? "pss790" : "pss680") : "gm"));
                    string fmtLabel = isRec ? $"{modelTag} Studio Recording (Passthrough)" :
                                      (isNativeThisModel ? $"{modelTag} Ready (Passthrough)" :
                                      (isNativeCounterpart ? (modelSnapshot == "pss680" ? "PSS-790 Ready (Remapped for PSS-680)" : "PSS-680 Ready (Remapped for PSS-790)") :
                                      $"General MIDI (Auto-Remapped for {modelTag})"));

                    if (Volatile.Read(ref _loadGeneration) != gen) return;

                    SendToJs("file_loaded", new
                    {
                        loadGen = gen,
                        fileName = title,
                        filePath = virtualPath ?? title,
                        relativeDir = "",
                        format = fmtCode,
                        formatLabel = fmtLabel,
                        durationSeconds = duration.TotalSeconds,
                        durationFormatted = FormatDurationMmSs(duration),
                        channels = channelInfo,
                        hasCustomOverrides = false,
                        health = (object?)null
                    });

                    _ = Task.Run(() =>
                    {
                        if (Volatile.Read(ref _loadGeneration) != gen) return;
                        try
                        {
                            var health = MidiHealthInspector.Analyze(midi, remappedSnapshot, title, modelSnapshot);
                            if (Volatile.Read(ref _loadGeneration) == gen)
                            {
                                SendToJs("health_updated", new
                                {
                                    health = BuildHealthPayload(health)
                                });
                            }
                        }
                        catch { }
                    });
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Error loading recorded take: {ex.Message}" });
                }
            });
        }

        private void LoadFileAsync(string pathOrName, bool autoPlay = false)
        {
            int gen = Interlocked.Increment(ref _loadGeneration);
            _ = Task.Run(() =>
            {
                if (_isDisposed || Volatile.Read(ref _loadGeneration) != gen) return;
                LoadFileInternal(pathOrName, autoPlay, gen);
            });
        }

        public void LoadFile(string pathOrName, bool autoPlay = false)
        {
            int gen = Interlocked.Increment(ref _loadGeneration);
            LoadFileInternal(pathOrName, autoPlay, gen);
        }

        private void LoadFileInternal(string pathOrName, bool autoPlay, int gen)
        {
            _isLoadingSong = true;
            try
            {
                lock (_loadFileLock)
                {
                    if (_isDisposed || Volatile.Read(ref _loadGeneration) != gen) return;

                    try
                    {
                        string resolvedPath = pathOrName;
                        if (!File.Exists(resolvedPath))
                        {
                            var gmCandidate = Path.Combine(AppPaths.GmMidiDir, pathOrName);
                            var pss790Candidate = Path.Combine(AppPaths.Pss790MidiDir, pathOrName);
                            var pss680Candidate = Path.Combine(AppPaths.Pss680MidiDir, pathOrName);

                            if (File.Exists(gmCandidate)) resolvedPath = gmCandidate;
                            else if (File.Exists(pss790Candidate)) resolvedPath = pss790Candidate;
                            else if (File.Exists(pss680Candidate)) resolvedPath = pss680Candidate;
                        }

                        if (!File.Exists(resolvedPath))
                        {
                            SendToJs("error", new { message = $"File not found: {pathOrName}" });
                            return;
                        }

                        if (string.Equals(_currentFilePath, resolvedPath, StringComparison.OrdinalIgnoreCase) && !autoPlay && !_player.IsPlaying && _loadedOriginalMidi != null)
                        {
                            return;
                        }

                        _player.Stop();
                        ResetChannelActivity();

                        var originalMidi = MidiFile.Read(resolvedPath, Constants.DefaultReadingSettings);
                        if (Volatile.Read(ref _loadGeneration) != gen) return;

                    _currentFilePath = resolvedPath;
                    _loadedOriginalMidi = originalMidi;

                    var modelSnapshot = _activeModel;
                    _options.ChannelVoiceOverrides.Clear();
                    _options.ChannelOctaveShifts.Clear();
                    _options.TargetModel = modelSnapshot;
                    var savedOverrides = VoiceOverrideStore.Instance.GetOverrides(resolvedPath, modelSnapshot);
                    foreach (var (ch, voice) in savedOverrides)
                    {
                        _options.ChannelVoiceOverrides[ch] = voice;
                    }

                    var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                    bool isRec = Remapper.IsRecording(_loadedOriginalMidi);
                    ReapplyRemapping(preservePosition: false);

                    if (Volatile.Read(ref _loadGeneration) != gen) return;

                    bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
                    bool isNative = (format == MidiFormat.Pss790Native) || (format == MidiFormat.Pss680Native);
                    bool isGmPassthrough = isMsGs && !isNative;

                    var channelInfo = AnalyzeChannels(_loadedOriginalMidi, _loadedRemappedMidi, format);
                    var duration = _player.TotalDuration;
                    if (duration <= TimeSpan.Zero)
                    {
                        duration = (_loadedRemappedMidi ?? _loadedOriginalMidi).GetDuration<MetricTimeSpan>();
                    }

                    var modelTag = modelSnapshot == "pss680" ? "PSS-680" : "PSS-790";
                    bool isNativeThisModel = modelSnapshot == "pss680" ? (format == MidiFormat.Pss680Native) : (format == MidiFormat.Pss790Native);
                    bool isNativeCounterpart = modelSnapshot == "pss680" ? (format == MidiFormat.Pss790Native) : (format == MidiFormat.Pss680Native);

                    string fmtCode = isRec ? "rec" : (isNativeThisModel ? modelSnapshot : (isNativeCounterpart ? (modelSnapshot == "pss680" ? "pss790" : "pss680") : "gm"));
                    string fmtLabel = isRec ? $"{modelTag} Studio Recording (Passthrough)" :
                                      (isNativeThisModel ? $"{modelTag} Ready (Passthrough)" :
                                      (isNativeCounterpart ? (modelSnapshot == "pss680" ? "PSS-790 Ready (Remapped for PSS-680)" : "PSS-680 Ready (Remapped for PSS-790)") :
                                      (isGmPassthrough ? "Windows GS Synth (Original GM Passthrough)" : $"General MIDI (Auto-Remapped for {modelTag})")));

                    SendToJs("file_loaded", new
                    {
                        loadGen = gen,
                        fileName = Path.GetFileName(resolvedPath),
                        filePath = resolvedPath,
                        relativeDir = Path.GetDirectoryName(AppPaths.GetRelativeLibraryPath(resolvedPath)) ?? "",
                        format = fmtCode,
                        formatLabel = fmtLabel,
                        isGmPassthrough = isGmPassthrough,
                        durationSeconds = duration.TotalSeconds,
                        durationFormatted = FormatDurationMmSs(duration),
                        channels = channelInfo,
                        masterTranspose = _options.MasterTranspose,
                        channelOctaveShifts = new Dictionary<int, int>(_options.ChannelOctaveShifts),
                        pitchBendMode = _options.PitchBendMode.ToString().ToLowerInvariant(),
                        hasCustomOverrides = VoiceOverrideStore.Instance.HasOverrides(resolvedPath, modelSnapshot),
                        health = (object?)null
                    });

                    if (autoPlay && Volatile.Read(ref _loadGeneration) == gen)
                    {
                        _player.Play();
                    }

                    var origSnap = _loadedOriginalMidi;
                    var remapSnap = _loadedRemappedMidi;
                    var fileNameSnap = Path.GetFileName(resolvedPath);
                    _ = Task.Run(() =>
                    {
                        if (Volatile.Read(ref _loadGeneration) != gen) return;
                        try
                        {
                            var health = MidiHealthInspector.Analyze(origSnap, remapSnap, fileNameSnap, modelSnapshot);
                            if (Volatile.Read(ref _loadGeneration) == gen)
                            {
                                SendToJs("health_updated", new
                                {
                                    health = BuildHealthPayload(health)
                                });
                            }
                        }
                        catch { }
                    });
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Error loading MIDI: {ex.Message}" });
                }
            }
            }
            finally
            {
                if (Volatile.Read(ref _loadGeneration) == gen)
                {
                    _isLoadingSong = false;
                }
            }
        }

        private void HandleDeviceSelectionChanged()
        {
            if (_loadedOriginalMidi == null) return;

            _ = Task.Run(() =>
            {
                MidiFile? originalSnap;
                lock (_loadFileLock)
                {
                    originalSnap = _loadedOriginalMidi;
                }
                if (originalSnap == null) return;

                var modelSnapshot = _activeModel;
                var format = Remapper.DetectMidiFormat(originalSnap);
                bool isRec = Remapper.IsRecording(originalSnap);
                bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
                bool isNative = (format == MidiFormat.Pss790Native) || (format == MidiFormat.Pss680Native);
                bool isGmPassthrough = isMsGs && !isNative;

                ReapplyRemapping(preservePosition: true);

                MidiFile? remappedSnap;
                lock (_loadFileLock)
                {
                    remappedSnap = _loadedRemappedMidi;
                }

                var modelTag = modelSnapshot == "pss680" ? "PSS-680" : "PSS-790";
                bool isNativeThisModel = modelSnapshot == "pss680" ? (format == MidiFormat.Pss680Native) : (format == MidiFormat.Pss790Native);
                bool isNativeCounterpart = modelSnapshot == "pss680" ? (format == MidiFormat.Pss790Native) : (format == MidiFormat.Pss680Native);

                string fmtCode = isRec ? "rec" : (isNativeThisModel ? modelSnapshot : (isNativeCounterpart ? (modelSnapshot == "pss680" ? "pss790" : "pss680") : "gm"));
                string fmtLabel = isRec ? $"{modelTag} Studio Recording (Passthrough)" :
                                  (isNativeThisModel ? $"{modelTag} Ready (Passthrough)" :
                                  (isNativeCounterpart ? (modelSnapshot == "pss680" ? "PSS-790 Ready (Remapped for PSS-680)" : "PSS-680 Ready (Remapped for PSS-790)") :
                                  (isGmPassthrough ? "Windows GS Synth (Original GM Passthrough)" : $"General MIDI (Auto-Remapped for {modelTag})")));

                var channelInfo = AnalyzeChannels(originalSnap, remappedSnap, format);
                SendToJs("playback_format_updated", new
                {
                    format = fmtCode,
                    formatLabel = fmtLabel,
                    isGmPassthrough = isGmPassthrough,
                    channels = channelInfo
                });
            });
        }

        private void ReapplyRemapping(bool preservePosition = false)
        {
            try
            {
                int startGen = Volatile.Read(ref _loadGeneration);
                MidiFile? originalSnap;
                RemapperOptions optionsSnap;

                lock (_loadFileLock)
                {
                    if (_loadedOriginalMidi == null || Volatile.Read(ref _loadGeneration) != startGen) return;
                    originalSnap = _loadedOriginalMidi;
                    _options.TargetModel = _activeModel;
                    optionsSnap = new RemapperOptions
                    {
                        TargetModel = _options.TargetModel,
                        EnablePolyphonyGuard = _options.EnablePolyphonyGuard,
                        ScalePitchBend = _options.ScalePitchBend,
                        PitchBendMode = _options.PitchBendMode,
                        NeutralizeAcousticPitchBends = _options.NeutralizeAcousticPitchBends,
                        AutoCenterDanglingPitchBends = _options.AutoCenterDanglingPitchBends,
                        EmulateSustainPedal = _options.EmulateSustainPedal,
                        FoldExpressionPedal = _options.FoldExpressionPedal,
                        NormalizeAwmVelocities = _options.NormalizeAwmVelocities,
                        BakeFmVelocity = _options.BakeFmVelocity,
                        TranslateDrums = _options.TranslateDrums,
                        VectorQuadLayering = !string.Equals(_options.TargetModel, "pss680", StringComparison.OrdinalIgnoreCase) && _player.VectorQuadPlaybackEnabled,
                        EnsureSetupPreRoll = _options.EnsureSetupPreRoll,
                        DefaultReverbBoost = _options.DefaultReverbBoost,
                        FilterUnsupportedControllers = _options.FilterUnsupportedControllers,
                        ForceRemap = _options.ForceRemap,
                        TempoMultiplier = _options.TempoMultiplier,
                        AutoNormalizeSluggishTempo = _options.AutoNormalizeSluggishTempo,
                        MasterTranspose = _options.MasterTranspose,
                        ChannelOctaveShifts = new Dictionary<int, int>(_options.ChannelOctaveShifts),
                        ChannelVoiceOverrides = new Dictionary<int, int?>(_options.ChannelVoiceOverrides)
                    };
                }

                var wasPlaying = _player.IsPlaying;
                var pos = preservePosition ? _player.CurrentPosition : TimeSpan.Zero;

                var remapped = Remapper.Remap(originalSnap, optionsSnap);
                lock (_loadFileLock)
                {
                    if (Volatile.Read(ref _loadGeneration) != startGen || !ReferenceEquals(_loadedOriginalMidi, originalSnap))
                    {
                        return;
                    }
                    _loadedRemappedMidi = remapped;
                }

                if (Volatile.Read(ref _loadGeneration) != startGen) return;

                var format = Remapper.DetectMidiFormat(originalSnap);
                bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
                bool isNative = (format == MidiFormat.Pss790Native) || (format == MidiFormat.Pss680Native);

                if (isMsGs && !isNative)
                {
                    var previewMidi = Remapper.BuildPreviewTransformForMsGs(originalSnap, optionsSnap);
                    _player.LoadMidi(previewMidi);
                }
                else
                {
                    _player.LoadMidi(remapped);
                }

                if (wasPlaying && preservePosition)
                {
                    _player.Seek(pos);
                    _player.Play();
                }
            }
            catch (Exception ex)
            {
                App.WriteCrashLog("UiBridge.ReapplyRemapping", ex, isFatal: false);
                SendToJs("error", new { message = $"Remapping error: {ex.Message}" });
            }
        }

        private List<object> AnalyzeChannels(MidiFile midi, MidiFile? remappedMidi, MidiFormat format)
        {
            var channelPrograms = new Dictionary<int, byte>();
            var channelNotes = new HashSet<int>();
            var drumChannels = new HashSet<int> { 9 };
            var inferredPrograms = new Dictionary<int, (byte Prog, string InferredSource)>();

            foreach (var te in midi.GetTimedEvents())
            {
                if (te.Event is ChannelEvent ce)
                {
                    int ch = ce.Channel;
                    if (ce is ProgramChangeEvent pc && !channelPrograms.ContainsKey(ch))
                    {
                        channelPrograms[ch] = (byte)pc.ProgramNumber;
                    }
                    else if (ce is ControlChangeEvent cc && cc.ControlNumber == 0 &&
                             (cc.ControlValue == 126 || cc.ControlValue == 127))
                    {
                        drumChannels.Add(ch);
                    }
                    if (ce is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0)
                    {
                        channelNotes.Add(ch);
                    }
                }
            }

            // Only scan General MIDI files for XG SysEx and track-name instrument heuristics
            if (format != MidiFormat.Pss790Native && format != MidiFormat.Pss680Native)
            {
                var tracksWithNotes = new Dictionary<int, TrackChunk>();
                foreach (var track in midi.GetTrackChunks())
                {
                    foreach (var evt in track.Events)
                    {
                        if (evt is NoteOnEvent n && n.Velocity > 0 && !drumChannels.Contains((int)n.Channel))
                        {
                            tracksWithNotes.TryAdd((int)n.Channel, track);
                        }
                        else if (evt is NormalSysExEvent sysex && sysex.Data != null && sysex.Data.Length > 6)
                        {
                            var d = sysex.Data;
                            if (d[0] == 0x43 && d[2] == 0x4C)
                            {
                                if (d[1] == 0x00 && d[5] == 0x08 && d[7] == 0x00 && d.Length > 10)
                                {
                                    int part = d[6];
                                    byte p = d[10];
                                    if (part >= 0 && part < 16 && !channelPrograms.ContainsKey(part))
                                    {
                                        inferredPrograms[part] = (p, "XG SysEx");
                                    }
                                }
                                else if (d[1] == 0x10 && d[3] == 0x08 && d[5] == 0x03 && d.Length > 6)
                                {
                                    int part = d[4];
                                    byte p = d[6];
                                    if (part >= 0 && part < 16 && !channelPrograms.ContainsKey(part))
                                    {
                                        inferredPrograms[part] = (p, "XG SysEx");
                                    }
                                }
                                else if (d[1] == 0x10 && d[3] == 0x08 && d[5] == 0x07 && d.Length > 6 && d[6] > 0)
                                {
                                    drumChannels.Add(d[4]);
                                }
                            }
                        }
                    }
                }

                foreach (var (ch, tr) in tracksWithNotes)
                {
                    if (!channelPrograms.ContainsKey(ch) && !inferredPrograms.ContainsKey(ch))
                    {
                        foreach (var textEvt in tr.Events.OfType<BaseTextEvent>())
                        {
                            var inf = Remapper.InferGmProgramFromText(textEvt.Text);
                            if (inf.HasValue)
                            {
                                inferredPrograms[ch] = (inf.Value, "Inferred");
                                break;
                            }
                        }
                    }
                }
            }

            bool isNative = format == MidiFormat.Pss790Native || format == MidiFormat.Pss680Native;
            bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
            bool isGmPassthrough = isMsGs && !isNative;
            bool useRemappedRouting = !isGmPassthrough && (isNative || remappedMidi != null);

            var remappedNotes = new HashSet<int>();
            int? relocatedMelodicChannel = null;
            if (!isGmPassthrough && format == MidiFormat.GeneralMidi && remappedMidi != null)
            {
                foreach (var te in remappedMidi.GetTimedEvents())
                {
                    if (te.Event is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0)
                    {
                        remappedNotes.Add(noteOn.Channel);
                    }
                }

                if (channelNotes.Contains(15) && !drumChannels.Contains(15))
                {
                    for (int cand = 0; cand <= 14; cand++)
                    {
                        if (remappedNotes.Contains(cand) && (!channelNotes.Contains(cand) || drumChannels.Contains(cand)))
                        {
                            relocatedMelodicChannel = cand;
                            break;
                        }
                    }
                }
            }

            Dictionary<int, int?> voiceOverridesSnap;
            Dictionary<int, int> octaveShiftsSnap;
            lock (_loadFileLock)
            {
                voiceOverridesSnap = new Dictionary<int, int?>(_options.ChannelVoiceOverrides);
                octaveShiftsSnap = new Dictionary<int, int>(_options.ChannelOctaveShifts);
            }

            var list = new List<object>();
            for (int ch = 0; ch < 16; ch++)
            {
                bool hasNotes = (!isGmPassthrough && format == MidiFormat.GeneralMidi && remappedMidi != null)
                    ? remappedNotes.Contains(ch)
                    : channelNotes.Contains(ch);

                bool isRelocatedTarget = relocatedMelodicChannel.HasValue && ch == relocatedMelodicChannel.Value;
                int lookupOrigCh = isRelocatedTarget ? 15 : ch;
                bool suppressOrigDrumSource = !isGmPassthrough && format == MidiFormat.GeneralMidi &&
                                              drumChannels.Contains(ch) && !isRelocatedTarget;

                byte? prog = (!suppressOrigDrumSource && channelPrograms.TryGetValue(lookupOrigCh, out var p)) ? p : null;
                string? inferredTag = null;
                if (!suppressOrigDrumSource && !prog.HasValue && inferredPrograms.TryGetValue(lookupOrigCh, out var inf))
                {
                    prog = inf.Prog;
                    inferredTag = inf.InferredSource;
                }
                if (isRelocatedTarget)
                {
                    inferredTag = inferredTag != null ? $"{inferredTag}, Ch 16" : "Ch 16";
                }

                bool is680 = string.Equals(_activeModel, "pss680", StringComparison.OrdinalIgnoreCase);
                bool isDrum = useRemappedRouting ? (ch == 15) : (ch == 9);
                bool isNativeForActiveModel = (format == MidiFormat.Pss680Native && is680) || (format == MidiFormat.Pss790Native && !is680);

                // Resolve cross-model native programs through their reverse GM mapping when translating to opposing hardware
                if (!isNativeForActiveModel && !isDrum && prog.HasValue && (format == MidiFormat.Pss790Native || format == MidiFormat.Pss680Native))
                {
                    if (format == MidiFormat.Pss790Native && Constants.Pss790MidiProgramToGmMap.TryGetValue(prog.Value, out byte revGm790))
                    {
                        prog = revGm790;
                    }
                    else if (format == MidiFormat.Pss680Native)
                    {
                        prog = prog.Value is >= 100 and <= 104
                            ? (byte)80
                            : (Pss680Constants.Pss680ToGmVoiceMap.TryGetValue((int)prog.Value, out var gm680) ? (byte)gm680 : (byte)0);
                    }
                }

                string role = isDrum
                    ? (isGmPassthrough ? "GM Standard Drums (Ch 10)" : (is680 ? "Hand Percussion (Ch 16)" : "Fixed Rhythm (Ch 16)"))
                    : $"Channel {ch + 1}";

                string detectedInstrument;
                int defaultPssVoice;
                string defaultPssVoiceName;

                if (format == MidiFormat.Pss680Native && is680)
                {
                    if (isDrum)
                    {
                        detectedInstrument = "PSS-680 Percussion (32 Hand Percussion Instruments)";
                        defaultPssVoice = 99;
                        defaultPssVoiceName = "Hand Percussion (Ch 16)";
                    }
                    else if (prog.HasValue && Pss680Constants.Voices.TryGetValue((int)prog.Value, out var vInfo))
                    {
                        defaultPssVoice = vInfo.VoiceNumber;
                        defaultPssVoiceName = vInfo.Name;
                        detectedInstrument = vInfo.Name;
                    }
                    else
                    {
                        defaultPssVoice = 3;
                        defaultPssVoiceName = "Piano 1";
                        detectedInstrument = "Default Piano";
                    }
                }
                else if (format == MidiFormat.Pss790Native && !is680)
                {
                    if (isDrum)
                    {
                        detectedInstrument = "PSS-790 Percussion (Chart 1)";
                        defaultPssVoice = 96;
                        defaultPssVoiceName = "PERCUSSIONS";
                    }
                    else if (prog.HasValue && Constants.Pss790MidiProgramToVoice.TryGetValue(prog.Value, out var voiceInfo))
                    {
                        defaultPssVoice = voiceInfo.VoiceNumber;
                        defaultPssVoiceName = voiceInfo.Name;
                        detectedInstrument = voiceInfo.Name;
                    }
                    else
                    {
                        defaultPssVoice = 0;
                        defaultPssVoiceName = "PIANO";
                        detectedInstrument = "Default Piano";
                    }
                }
                else
                {
                    if (isDrum)
                    {
                        detectedInstrument = isGmPassthrough
                            ? "GM Standard Drum Kit"
                            : (is680 ? "PSS-680 Percussion (32 Hand Percussion Instruments)" : "PSS-790 Percussion (Chart 1)");
                        defaultPssVoice = is680 ? 99 : 96;
                        defaultPssVoiceName = is680 ? "Hand Percussion (Ch 16)" : "PERCUSSIONS";
                    }
                    else if (prog.HasValue)
                    {
                        var gmName = Remapper.GetGmInstrumentName(prog.Value);
                        detectedInstrument = inferredTag != null ? $"{gmName} [{inferredTag}]" : gmName;
                        if (is680)
                        {
                            defaultPssVoice = Pss680Constants.GmToPss680VoiceMap.GetValueOrDefault(prog.Value, 3);
                            defaultPssVoiceName = Pss680Constants.Voices.GetValueOrDefault(defaultPssVoice)?.Name ?? "Piano 1";
                        }
                        else
                        {
                            defaultPssVoice = Constants.GmToPss790VoiceMap.GetValueOrDefault(prog.Value, 0);
                            defaultPssVoiceName = Constants.Pss790Voices.GetValueOrDefault(defaultPssVoice)?.Name ?? "PIANO";
                        }
                    }
                    else
                    {
                        detectedInstrument = "Default Piano";
                        defaultPssVoice = is680 ? 3 : 0;
                        defaultPssVoiceName = is680 ? "Piano 1" : "PIANO";
                    }
                }

                int? voiceOverride = voiceOverridesSnap.TryGetValue(ch, out var vo) ? vo : null;

                list.Add(new
                {
                    channel = ch,
                    channelDisplay = ch + 1,
                    hasNotes,
                    isDrum,
                    role,
                    gmProgram = prog,
                    detectedInstrument,
                    defaultPssVoice,
                    defaultPssVoiceName,
                    voiceOverride,
                    octaveShift = octaveShiftsSnap.TryGetValue(ch, out var os) ? os : 0
                });
            }

            return list;
        }

        private static object BuildHealthPayload(MidiHealthReport health)
        {
            return new
            {
                peakPolyphony = health.PeakPolyphony,
                voiceStress = health.VoiceStressRating,
                activeChannels = health.ActiveChannelCount,
                totalNotes = health.TotalNoteCount,
                stuckPedals = health.StuckPedalIntervalsCount,
                danglingBends = health.DanglingPitchBendsCount,
                microBends = health.AcousticMicroBendsNeutralized,
                outOfBounds = health.OutOfBoundsNotesCount,
                inferredCount = health.InferredProgramChannels.Count,
                inferredDetails = health.InferredProgramChannels,
                xgSysExCount = health.XgSysExChannels.Count,
                xgDetails = health.XgSysExChannels,
                autoFixes = health.AppliedAutoFixes,
                badgeType = health.BadgeType,
                badgeLabel = health.BadgeLabel,
                badgeTooltip = health.BadgeTooltip
            };
        }

        private void RunHealthAudit(string directory)
        {
            var resolvedDir = Directory.Exists(directory) ? directory : AppPaths.GmMidiDir;
            var modelSnapshot = _activeModel;

            _ = Task.Run(() =>
            {
                try
                {
                    SendToJs("audit_status", new { status = "scanning", message = "Scanning MIDI library files..." });

                    var files = Directory.GetFiles(resolvedDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var reports = new List<MidiHealthReport>();
                    int processed = 0;
                    int total = files.Count;

                    foreach (var file in files)
                    {
                        try
                        {
                            var midi = MidiFile.Read(file, Constants.DefaultReadingSettings);
                            var report = MidiHealthInspector.Analyze(midi, fileName: Path.GetRelativePath(resolvedDir, file), model: modelSnapshot);
                            reports.Add(report);
                        }
                        catch (Exception) { }
                        processed++;
                        if (processed % 10 == 0 || processed == total)
                        {
                            SendToJs("audit_progress", new { current = processed, total });
                        }
                    }

                    int totalWithFixes = reports.Count(r => r.AppliedAutoFixes.Count > 0);
                    int totalHighPolyphony = reports.Count(r => string.Equals(r.VoiceStressRating, "HighPolyphony", StringComparison.OrdinalIgnoreCase));
                    int totalInferred = reports.Count(r => r.InferredProgramChannels.Count > 0);
                    int totalXg = reports.Count(r => r.XgSysExChannels.Count > 0);
                    int totalStuckPedal = reports.Count(r => r.StuckPedalIntervalsCount > 0);
                    int totalDanglingBends = reports.Count(r => r.DanglingPitchBendsCount > 0);
                    int totalMicroBends = reports.Count(r => r.AcousticMicroBendsNeutralized > 0);

                    var fileList = reports
                        .OrderByDescending(r => r.AppliedAutoFixes.Count)
                        .ThenByDescending(r => r.PeakPolyphony)
                        .Select(r => new
                        {
                            fileName = r.FileName,
                            peakPolyphony = r.PeakPolyphony,
                            voiceStress = r.VoiceStressRating,
                            autoFixesCount = r.AppliedAutoFixes.Count,
                            autoFixes = r.AppliedAutoFixes,
                            badgeType = r.BadgeType,
                            badgeLabel = r.BadgeLabel,
                            badgeTooltip = r.BadgeTooltip
                        })
                        .ToList();

                    SendToJs("health_audit_result", new
                    {
                        totalFiles = reports.Count,
                        filesWithFixes = totalWithFixes,
                        filesHighPolyphony = totalHighPolyphony,
                        filesInferred = totalInferred,
                        filesXg = totalXg,
                        filesStuckPedal = totalStuckPedal,
                        filesDanglingBends = totalDanglingBends,
                        filesMicroBends = totalMicroBends,
                        files = fileList
                    });
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Health audit failed: {ex.Message}" });
                }
            });
        }

        private void SaveRemappedDialog()
        {
            MidiFile? remappedSnapshot;
            string? filePathSnap;
            lock (_loadFileLock)
            {
                remappedSnapshot = _loadedRemappedMidi;
                filePathSnap = _currentFilePath;
            }

            if (remappedSnapshot == null)
            {
                SendToJs("error", new { message = "No MIDI file is currently loaded." });
                return;
            }

            var modelSnapshot = _activeModel;
            Application.Current.Dispatcher.Invoke(() =>
            {
                bool is680 = string.Equals(modelSnapshot, "pss680", StringComparison.OrdinalIgnoreCase);
                string suffix = is680 ? "_PSS680" : "_PSS790";
                var originalName = filePathSnap != null ? Path.GetFileNameWithoutExtension(filePathSnap) : "Remapped";
                if (!originalName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    originalName += suffix;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "MIDI File (*.mid)|*.mid",
                    InitialDirectory = AppPaths.GetModelMidiDir(modelSnapshot),
                    FileName = $"{originalName}.mid",
                    Title = $"Save Remapped {(is680 ? "PSS-680" : "PSS-790")} MIDI File"
                };

                if (dialog.ShowDialog() == true)
                {
                    Remapper.SaveRemapped(remappedSnapshot, dialog.FileName, modelSnapshot);
                    SendToJs("notification", new { message = $"Saved to: {Path.GetFileName(dialog.FileName)}" });
                    SendLibraryUpdate();
                }
            });
        }

        private void StartBatchConversion(
            string sourceDir,
            string target790Dir,
            string target680Dir,
            RemapperOptions? options790 = null,
            RemapperOptions? options680 = null,
            bool convert790 = true,
            bool convert680 = true)
        {
            CancellationTokenSource newCts;
            lock (_ctsLock)
            {
                try { _batchCts?.Cancel(); } catch { }
                try { _batchCts?.Dispose(); } catch { }
                newCts = new CancellationTokenSource();
                _batchCts = newCts;
            }

            var resolvedSource = Directory.Exists(sourceDir) ? sourceDir : AppPaths.GmMidiDir;
            var resolvedTarget790 = !string.IsNullOrEmpty(target790Dir) ? target790Dir : AppPaths.Pss790MidiDir;
            var resolvedTarget680 = !string.IsNullOrEmpty(target680Dir) ? target680Dir : AppPaths.Pss680MidiDir;
            var token = newCts.Token;

            _ = Task.Run(async () =>
            {
                var progress = new Progress<(int Current, int Total, string FileName)>(p =>
                {
                    SendToJs("batch_progress", new
                    {
                        current = p.Current,
                        total = p.Total,
                        fileName = p.FileName,
                        percent = p.Total > 0 ? (int)((double)p.Current / p.Total * 100) : 0
                    });
                });

                try
                {
                    int count = await BatchConverter.ConvertDirectoryDualAsync(
                        resolvedSource,
                        resolvedTarget790,
                        resolvedTarget680,
                        options790,
                        options680,
                        convert790,
                        convert680,
                        progress,
                        token);

                    var targetsSummary = (convert790 && convert680)
                        ? $"{resolvedTarget790} & {resolvedTarget680}"
                        : (convert790 ? resolvedTarget790 : resolvedTarget680);

                    SendToJs("batch_complete", new
                    {
                        convertedCount = count,
                        targetDirectory = targetsSummary
                    });
                    SendLibraryUpdate();
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Batch error: {ex.Message}" });
                }
            });
        }

        private void HandleAnalyzeLibrarySync()
        {
            if (_isDisposed) return;

            _ = Task.Run(() =>
            {
                try
                {
                    var dual = BatchConverter.AnalyzeDualSync(AppPaths.GmMidiDir, AppPaths.Pss790MidiDir, AppPaths.Pss680MidiDir);
                    SendToJs("library_sync_analysis", new
                    {
                        missingCount = dual.TotalMissing,
                        outdatedCount = dual.TotalOutdated,
                        orphanCount = dual.TotalOrphans,
                        totalGm = dual.TotalGmFiles,
                        totalPss = dual.TotalPss790Files + dual.TotalPss680Files,
                        totalPss790 = dual.TotalPss790Files,
                        totalPss680 = dual.TotalPss680Files,
                        isInSync = dual.IsInSync,
                        totalXg = dual.TotalXgFiles,
                        xgUpgradesAvailable = dual.TotalXgUpgrades,
                        missingFiles = dual.Analysis790.MissingInTarget.Concat(dual.Analysis680.MissingInTarget).Distinct().Take(50).ToList(),
                        outdatedFiles = dual.Analysis790.OutdatedInTarget.Concat(dual.Analysis680.OutdatedInTarget).Distinct().Take(50).ToList(),
                        orphanFiles = dual.Analysis790.OrphanInTarget.Concat(dual.Analysis680.OrphanInTarget).Distinct().Take(50).ToList()
                    });
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Sync analysis error: {ex.Message}" });
                }
            });
        }

        private void HandleExecuteLibrarySync(JsonElement root)
        {
            CancellationTokenSource newCts;
            lock (_ctsLock)
            {
                try { _syncCts?.Cancel(); } catch { }
                try { _syncCts?.Dispose(); } catch { }
                newCts = new CancellationTokenSource();
                _syncCts = newCts;
            }

            var convertMissing = !root.TryGetProperty("convertMissing", out var cm) || cm.GetBoolean();
            var updateOutdated = !root.TryGetProperty("updateOutdated", out var uo) || uo.GetBoolean();
            var removeOrphans = !root.TryGetProperty("removeOrphans", out var ro) || ro.GetBoolean();
            var pruneEmpty = !root.TryGetProperty("pruneEmpty", out var pe) || pe.GetBoolean();
            var forceReconvertAll = root.TryGetProperty("forceReconvertAll", out var fr) && fr.GetBoolean();

            RemapperOptions optionsSnap;
            lock (_loadFileLock)
            {
                optionsSnap = new RemapperOptions
                {
                    EnablePolyphonyGuard = _options.EnablePolyphonyGuard,
                    ScalePitchBend = _options.ScalePitchBend,
                    PitchBendMode = _options.PitchBendMode,
                    NeutralizeAcousticPitchBends = _options.NeutralizeAcousticPitchBends,
                    AutoCenterDanglingPitchBends = _options.AutoCenterDanglingPitchBends,
                    EmulateSustainPedal = _options.EmulateSustainPedal,
                    FoldExpressionPedal = _options.FoldExpressionPedal,
                    NormalizeAwmVelocities = _options.NormalizeAwmVelocities,
                    EnsureSetupPreRoll = _options.EnsureSetupPreRoll,
                    DefaultReverbBoost = _options.DefaultReverbBoost,
                    FilterUnsupportedControllers = _options.FilterUnsupportedControllers,
                    TempoMultiplier = _options.TempoMultiplier,
                    AutoNormalizeSluggishTempo = _options.AutoNormalizeSluggishTempo,
                    MasterTranspose = _options.MasterTranspose
                };
            }

            var syncOptions = new LibrarySyncOptions
            {
                ConvertMissing = convertMissing,
                UpdateOutdated = updateOutdated,
                RemoveOrphans = removeOrphans,
                PruneEmptyDirectories = pruneEmpty,
                ForceReconvertAll = forceReconvertAll,
                RemapperOptions = optionsSnap
            };

            var token = newCts.Token;

            _ = Task.Run(async () =>
            {
                var progress = new Progress<(int Current, int Total, string FileName, string Action)>(p =>
                {
                    SendToJs("sync_progress", new
                    {
                        current = p.Current,
                        total = p.Total,
                        fileName = p.FileName,
                        action = p.Action,
                        percent = p.Total > 0 ? (int)((double)p.Current / p.Total * 100) : 0
                    });
                });

                try
                {
                    var result = await BatchConverter.ExecuteDualSyncAsync(
                        AppPaths.GmMidiDir,
                        AppPaths.Pss790MidiDir,
                        AppPaths.Pss680MidiDir,
                        syncOptions,
                        VoiceOverrideStore.Instance,
                        progress,
                        token);

                    SendToJs("sync_completed", new
                    {
                        convertedCount = result.ConvertedCount,
                        updatedCount = result.UpdatedCount,
                        removedOrphansCount = result.RemovedOrphansCount,
                        prunedDirectoriesCount = result.PrunedDirectoriesCount,
                        totalOps = result.TotalOperations
                    });

                    try
                    {
                        var existingFiles = Directory.GetFiles(AppPaths.GmMidiDir, "*", SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(AppPaths.Pss790MidiDir, "*", SearchOption.AllDirectories))
                            .Concat(Directory.GetFiles(AppPaths.Pss680MidiDir, "*", SearchOption.AllDirectories));
                        FavoriteStore.Instance.PruneMissing(existingFiles);
                        SendToJs("favorites_updated", new { favorites = FavoriteStore.Instance.GetAllFavorites() });
                    }
                    catch { }

                    SendLibraryUpdate();
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Sync execution error: {ex.Message}" });
                }
            });
        }

        private void HandleToggleFavorite(JsonElement root)
        {
            try
            {
                var path = root.TryGetProperty("path", out var p) ? p.GetString() : (root.TryGetProperty("filePath", out var fp) ? fp.GetString() : null);
                if (string.IsNullOrWhiteSpace(path)) return;

                bool isFav = FavoriteStore.Instance.ToggleFavorite(path);
                var subPath = AppPaths.GetLibrarySubPath(path);
                var fileName = Path.GetFileName(path);

                SendToJs("favorites_updated", new
                {
                    favorites = FavoriteStore.Instance.GetAllFavorites(),
                    path = subPath,
                    isFavorite = isFav,
                    toastMessage = isFav ? $"Added '{fileName}' to Favourites" : $"Removed '{fileName}' from Favourites"
                });

                SendLibraryUpdate();
            }
            catch (Exception ex)
            {
                SendToJs("notification", new { message = $"Failed to update favorites: {ex.Message}" });
            }
        }

        private void SendToJs(string type, object data)
        {
            if (_isDisposed) return;
            var app = Application.Current;
            if (app == null) return;

            var action = new Action(() =>
            {
                if (_isDisposed) return;
                try
                {
                    if (_webView.CoreWebView2 != null)
                    {
                        var payload = JsonSerializer.Serialize(new { type, data }, s_jsonOptions);
                        _webView.CoreWebView2.PostWebMessageAsJson(payload);
                    }
                }
                catch { }
            });

            if (app.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                app.Dispatcher.BeginInvoke(action);
            }
        }

        private void SendSysExLibraryUpdate()
        {
            if (_isDisposed) return;
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                var list = _sysexManager.GetSysExLibrary();
                SendToJs("sysex_library_updated", new { dumps = list });
            });
        }

        private void SendRecordingsLibraryUpdate()
        {
            if (_isDisposed) return;
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                var list = _recorder.GetRecordingsLibrary();
                SendToJs("recordings_library_updated", new { recordings = list });
            });
        }

        private List<object> GetFmVoiceLibraryData()
        {
            AppPaths.EnsureDirectoriesExist();
            var list = new List<object>();
            if (Directory.Exists(AppPaths.FmVoicesDir))
            {
                var files = Directory.GetFiles(AppPaths.FmVoicesDir, "*.syx")
                    .OrderByDescending(f => File.GetLastWriteTime(f));
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        byte[] bytes = File.ReadAllBytes(file);
                        var voice = PSS680Voice.FromSysExMessage(bytes);
                        string resolvedName = (string.IsNullOrWhiteSpace(voice.Name) || voice.Name == "Custom Voice")
                            ? Path.GetFileNameWithoutExtension(fi.Name)
                            : voice.Name;
                        list.Add(new
                        {
                            fileName = fi.Name,
                            filePath = fi.FullName,
                            voiceName = resolvedName,
                            bankNumber = voice.BankNumber,
                            bankDisplay = voice.BankNumber + 1,
                            fileSize = SysExManager.FormatBytes(fi.Length),
                            updatedAt = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                    catch { }
                }
            }
            return list;
        }

        private void SendFmVoiceLibraryUpdate()
        {
            if (_isDisposed) return;
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                var list = GetFmVoiceLibraryData();
                SendToJs("fm_voice_library_updated", new { voices = list });
            });
        }

        private List<object> GetVectorLibraryData()
        {
            AppPaths.EnsureDirectoriesExist();
            var list = new List<object>();
            if (Directory.Exists(AppPaths.VectorPresetsDir))
            {
                var files = Directory.GetFiles(AppPaths.VectorPresetsDir, "*.json")
                    .OrderByDescending(f => File.GetLastWriteTime(f));
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        var text = File.ReadAllText(file);
                        using var doc = JsonDocument.Parse(text);
                        var root = doc.RootElement;
                        var name = root.TryGetProperty("name", out var np) ? np.GetString() : Path.GetFileNameWithoutExtension(fi.Name);
                        var vA = root.TryGetProperty("voiceA", out var va) ? va.GetInt32() : 0;
                        var vB = root.TryGetProperty("voiceB", out var vb) ? vb.GetInt32() : 34;
                        var vC = root.TryGetProperty("voiceC", out var vc) ? vc.GetInt32() : 16;
                        var vD = root.TryGetProperty("voiceD", out var vd) ? vd.GetInt32() : 56;
                        var posX = root.TryGetProperty("posX", out var px) ? px.GetDouble() : 0.0;
                        var posY = root.TryGetProperty("posY", out var py) ? py.GetDouble() : 0.0;
                        var orbitMode = root.TryGetProperty("orbitMode", out var om) ? om.GetString() : "manual";
                        var orbitSpeed = root.TryGetProperty("orbitSpeed", out var os) ? os.GetDouble() : 3.0;
                        object? channelEffects = null;
                        if (root.TryGetProperty("channelEffects", out var ceProp) || root.TryGetProperty("effects", out ceProp))
                        {
                            channelEffects = JsonSerializer.Deserialize<object>(ceProp.GetRawText());
                        }
                        list.Add(new
                        {
                            fileName = fi.Name,
                            filePath = fi.FullName,
                            name = name ?? Path.GetFileNameWithoutExtension(fi.Name),
                            voiceA = vA,
                            voiceB = vB,
                            voiceC = vC,
                            voiceD = vD,
                            posX,
                            posY,
                            orbitMode = orbitMode ?? "manual",
                            orbitSpeed,
                            channelEffects,
                            fileSize = SysExManager.FormatBytes(fi.Length),
                            updatedAt = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                    catch { }
                }
            }
            return list;
        }

        private void SendVectorLibraryUpdate()
        {
            if (_isDisposed) return;
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                var list = GetVectorLibraryData();
                SendToJs("vector_library_updated", new { presets = list });
            });
        }

        private static bool IsPathInsideDirectory(string candidatePath, string directoryPath) =>
            AppPaths.IsPathInsideDirectory(candidatePath, directoryPath);

        private static PSS680Voice ParseVoiceFromJson(JsonElement el)
        {
            if (el.TryGetProperty("voice", out var vSub) && vSub.ValueKind == JsonValueKind.Object)
            {
                el = vSub;
            }
            var voice = new PSS680Voice();
            if (el.TryGetProperty("name", out var n)) voice.Name = n.GetString() ?? "Custom Voice";
            if (el.TryGetProperty("bankNumber", out var bn)) voice.BankNumber = Math.Clamp(bn.GetInt32(), 0, 4);
            if (el.TryGetProperty("feedback", out var fb)) voice.Feedback = Math.Clamp(fb.GetInt32(), 0, 7);
            if (el.TryGetProperty("pitchModulationSensitivity", out var pms)) voice.PitchModulationSensitivity = Math.Clamp(pms.GetInt32(), 0, 7);
            if (el.TryGetProperty("amplitudeModulationSensitivity", out var ams)) voice.AmplitudeModulationSensitivity = Math.Clamp(ams.GetInt32(), 0, 3);
            if (el.TryGetProperty("vibratoDelayTime", out var vdt)) voice.VibratoDelayTime = Math.Clamp(vdt.GetInt32(), 0, 127);
            if (el.TryGetProperty("vibrato", out var vib)) voice.Vibrato = vib.GetBoolean();
            if (el.TryGetProperty("sustain", out var sus)) voice.Sustain = sus.GetBoolean();

            if (el.TryGetProperty("modulator", out var mod))
            {
                ParseOperatorFromJson(mod, voice.Modulator);
            }
            if (el.TryGetProperty("carrier", out var car))
            {
                ParseOperatorFromJson(car, voice.Carrier);
            }
            return voice;
        }

        private static void ParseOperatorFromJson(JsonElement el, FmOperatorParameters op)
        {
            if (el.TryGetProperty("fineDetune", out var dt1)) op.FineDetune = Math.Clamp(dt1.GetInt32(), -3, 3);
            if (el.TryGetProperty("multiplier", out var mul)) op.Multiplier = Math.Clamp(mul.GetInt32(), 0, 15);
            if (el.TryGetProperty("panelLevel", out var pl)) op.PanelLevel = Math.Clamp(pl.GetInt32(), 0, 99);
            if (el.TryGetProperty("levelKeyScalingHigh", out var lkh)) op.LevelKeyScalingHigh = Math.Clamp(lkh.GetInt32(), 0, 15);
            if (el.TryGetProperty("levelKeyScalingLow", out var lkl)) op.LevelKeyScalingLow = Math.Clamp(lkl.GetInt32(), 0, 15);
            if (el.TryGetProperty("rateKeyScaling", out var rks)) op.RateKeyScaling = Math.Clamp(rks.GetInt32(), 0, 3);
            if (el.TryGetProperty("attackRate", out var ar)) op.AttackRate = Math.Clamp(ar.GetInt32(), 0, 63);
            if (el.TryGetProperty("amplitudeModulationEnable", out var ame)) op.AmplitudeModulationEnable = ame.GetBoolean();
            if (el.TryGetProperty("coarseDetune", out var cd)) op.CoarseDetune = cd.GetBoolean();
            if (el.TryGetProperty("decay1Rate", out var d1r)) op.Decay1Rate = Math.Clamp(d1r.GetInt32(), 0, 63);
            if (el.TryGetProperty("waveform", out var wf)) op.Waveform = (FmWaveform)Math.Clamp(wf.GetInt32(), 0, 3);
            if (el.TryGetProperty("decay2Rate", out var d2r)) op.Decay2Rate = Math.Clamp(d2r.GetInt32(), 0, 31);
            if (el.TryGetProperty("decay1Level", out var d1l)) op.Decay1Level = Math.Clamp(d1l.GetInt32(), 0, 15);
            if (el.TryGetProperty("releaseRate", out var rr)) op.ReleaseRate = Math.Clamp(rr.GetInt32(), 0, 15);
            if (el.TryGetProperty("sustainReleaseRate", out var srr)) op.SustainReleaseRate = Math.Clamp(srr.GetInt32(), 0, 15);
        }

        private static object VoiceToObject(PSS680Voice v) => new
        {
            name = v.Name,
            bankNumber = v.BankNumber,
            feedback = v.Feedback,
            pitchModulationSensitivity = v.PitchModulationSensitivity,
            amplitudeModulationSensitivity = v.AmplitudeModulationSensitivity,
            vibratoDelayTime = v.VibratoDelayTime,
            vibrato = v.Vibrato,
            sustain = v.Sustain,
            modulator = OperatorToObject(v.Modulator),
            carrier = OperatorToObject(v.Carrier)
        };

        private static object OperatorToObject(FmOperatorParameters op) => new
        {
            fineDetune = op.FineDetune,
            multiplier = op.Multiplier,
            panelLevel = op.PanelLevel,
            levelKeyScalingHigh = op.LevelKeyScalingHigh,
            levelKeyScalingLow = op.LevelKeyScalingLow,
            rateKeyScaling = op.RateKeyScaling,
            attackRate = op.AttackRate,
            amplitudeModulationEnable = op.AmplitudeModulationEnable,
            coarseDetune = op.CoarseDetune,
            decay1Rate = op.Decay1Rate,
            waveform = (int)op.Waveform,
            decay2Rate = op.Decay2Rate,
            decay1Level = op.Decay1Level,
            releaseRate = op.ReleaseRate,
            sustainReleaseRate = op.SustainReleaseRate
        };

        private sealed class SysExInspectMetadata
        {
            public string? FormatCode { get; set; }
            public bool CanExtractMidi { get; set; }
            public bool CanOpenDrummerGrid { get; set; }
            public string? SummaryText { get; set; }
        }

        private static SysExInspectMetadata InspectPss680SysExMetadata(string filePath)
        {
            var result = new SysExInspectMetadata();
            try
            {
                var packets = SysExManager.ExtractPackets(File.ReadAllBytes(filePath));
                foreach (var pkt in packets)
                {
                    if (pkt.Length < 6 || pkt[1] != Pss680Constants.ManufacturerYamaha || pkt[2] != Pss680Constants.InstrumentClassPss)
                        continue;

                    var parsed = Pss680SysExParser.Parse(pkt);
                    result.FormatCode = $"0x{parsed.FormatByte:X2}";
                    if (parsed.Format == Pss680SysExFormat.MelodyMemory)
                    {
                        var banks = Pss680SysExTranslators.AnalyzeMelodyMemory(parsed.DecodedBytes);
                        int activeBanks = banks.Count(b => b.NoteCount > 0);
                        int totalNotes = banks.Sum(b => b.NoteCount);
                        result.CanExtractMidi = totalNotes > 0;
                        result.SummaryText = $"PSS-680 Melody Memory: {activeBanks}/5 active banks, {totalNotes:N0} notes";
                        break;
                    }
                    if (parsed.Format == Pss680SysExFormat.ChordMemory)
                    {
                        var summary = Pss680SysExTranslators.AnalyzeChordMemory(parsed.DecodedBytes);
                        result.CanExtractMidi = summary.Chords.Count > 0;
                        string chordSeq = summary.Chords.Count > 0
                            ? string.Join(" - ", summary.Chords.Take(8).Select(c => c.ChordName))
                            : "No chords";
                        result.SummaryText = $"PSS-680 Chord Bank {summary.BankNumber} • Style: {summary.InitialStyleName} • {summary.Tempo} BPM • {chordSeq}";
                        break;
                    }
                    if (parsed.Format == Pss680SysExFormat.RhythmPattern)
                    {
                        var pat = Pss680CustomDrummerPattern.FromSysExBytes(parsed.DecodedBytes);
                        int activeTracks = pat.Tracks.Count(t => t.StepBytes.Any(b => b != 0));
                        result.CanExtractMidi = activeTracks > 0;
                        result.CanOpenDrummerGrid = true;
                        result.SummaryText = $"PSS-680 Custom Drummer: {pat.BeatsLength} beats ({(pat.IsTripletQuantize ? "Triplet" : "1/8")} grid), {activeTracks} active drum track(s)";
                        break;
                    }
                }
            }
            catch { }
            return result;
        }

        private void HandleExtractSysExToMidi(string filePath)
        {
            try
            {
                var packets = SysExManager.ExtractPackets(File.ReadAllBytes(filePath));
                var baseName = Path.GetFileNameWithoutExtension(filePath);
                AppPaths.EnsureDirectoriesExist();

                MidiFile? extracted = null;
                string suffix = "_Extracted";

                foreach (var pkt in packets)
                {
                    if (pkt.Length < 6 || pkt[1] != Pss680Constants.ManufacturerYamaha || pkt[2] != Pss680Constants.InstrumentClassPss)
                        continue;

                    var parsed = Pss680SysExParser.Parse(pkt);
                    if (parsed.Format == Pss680SysExFormat.MelodyMemory)
                    {
                        extracted = Pss680SysExTranslators.DecodeMelodyMemoryToMidi(parsed.DecodedBytes);
                        suffix = "_Melody";
                        break;
                    }
                    if (parsed.Format == Pss680SysExFormat.ChordMemory)
                    {
                        extracted = Pss680SysExTranslators.DecodeChordMemoryToMidi(parsed.DecodedBytes);
                        suffix = "_Chords";
                        break;
                    }
                    if (parsed.Format == Pss680SysExFormat.RhythmPattern)
                    {
                        extracted = Pss680CustomDrummerPattern.FromSysExBytes(parsed.DecodedBytes).ToMidiFile();
                        suffix = "_Drummer";
                        break;
                    }
                }

                if (extracted == null)
                {
                    SendToJs("notification", new { message = "Only PSS-680/780 Formats 01H (Melody), 02H (Chord), and 03H (Drummer) support MIDI extraction." });
                    return;
                }

                string safeOut = SanitizeFileName(baseName + suffix);
                string destPath = Path.Combine(AppPaths.Pss680MidiDir, $"{safeOut}.mid");
                extracted.Write(destPath, overwriteFile: true);
                SendLibraryUpdate();
                SendToJs("notification", new { message = $"Extracted SysEx to PSS-680 MIDI library: {Path.GetFileName(destPath)}" });
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = $"SysEx MIDI extraction failed: {ex.Message}" });
            }
        }

        private void HandleLoadDrummerSysEx(string filePath)
        {
            try
            {
                var packets = SysExManager.ExtractPackets(File.ReadAllBytes(filePath));
                foreach (var pkt in packets)
                {
                    if (pkt.Length < 6 || pkt[1] != Pss680Constants.ManufacturerYamaha || pkt[2] != Pss680Constants.InstrumentClassPss)
                        continue;

                    var parsed = Pss680SysExParser.Parse(pkt);
                    if (parsed.Format != Pss680SysExFormat.RhythmPattern) continue;

                    var pat = Pss680CustomDrummerPattern.FromSysExBytes(parsed.DecodedBytes);
                    SendToJs("drummer_pattern_loaded", new
                    {
                        fileName = Path.GetFileNameWithoutExtension(filePath),
                        beatsLength = pat.BeatsLength,
                        isTripletQuantize = pat.IsTripletQuantize,
                        tracks = pat.Tracks.Select(t => new
                        {
                            trackIndex = t.TrackIndex,
                            instrumentId = t.InstrumentId,
                            midiNote = t.MidiNote,
                            padLabel = t.PadLabel,
                            instrumentName = t.InstrumentName,
                            roll = t.Roll,
                            accent1 = t.Accent1,
                            accent2 = t.Accent2,
                            stepBytes = t.StepBytes.Select(b => (int)b).ToArray()
                        }).ToList()
                    });
                    return;
                }

                SendToJs("error", new { message = "Selected file does not contain a PSS-680 Format 03H Custom Drummer packet." });
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = $"Failed to load Custom Drummer pattern: {ex.Message}" });
            }
        }

        private void HandleBuildDrummerSysEx(JsonElement root)
        {
            try
            {
                var action = root.TryGetProperty("action", out var actP) ? actP.GetString() : "export_midi";
                var name = root.TryGetProperty("name", out var nP) ? nP.GetString() : "Custom_Drummer";
                int beats = root.TryGetProperty("beatsLength", out var bP) ? Math.Clamp(bP.GetInt32(), 1, 8) : 4;
                bool isTriplet = root.TryGetProperty("isTripletQuantize", out var tP) && tP.GetBoolean();
                int bpm = root.TryGetProperty("bpm", out var bpmP) ? Math.Clamp(bpmP.GetInt32(), 40, 240) : 120;

                var pattern = new Pss680CustomDrummerPattern
                {
                    BeatsLength = beats,
                    IsTripletQuantize = isTriplet
                };

                if (root.TryGetProperty("tracks", out var trArr) && trArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in trArr.EnumerateArray())
                    {
                        int idx = item.TryGetProperty("trackIndex", out var idxP) ? idxP.GetInt32() : -1;
                        if (idx < 0 || idx >= 32) continue;
                        var row = pattern.Tracks[idx];
                        row.Roll = item.TryGetProperty("roll", out var rP) && rP.GetBoolean();
                        row.Accent1 = item.TryGetProperty("accent1", out var a1P) && a1P.GetBoolean();
                        row.Accent2 = item.TryGetProperty("accent2", out var a2P) && a2P.GetBoolean();
                        if (item.TryGetProperty("stepBytes", out var sbArr) && sbArr.ValueKind == JsonValueKind.Array)
                        {
                            int bi = 0;
                            foreach (var sbVal in sbArr.EnumerateArray())
                            {
                                if (bi >= 8) break;
                                row.StepBytes[bi++] = (byte)Math.Clamp(sbVal.GetInt32(), 0, 255);
                            }
                        }
                    }
                }

                AppPaths.EnsureDirectoriesExist();
                string safeBase = SanitizeFileName(name ?? "Custom_Drummer");

                if (action == "transmit")
                {
                    byte[] sysex = pattern.ToSysExMessage();
                    _player.SendSysEx(sysex);
                    SendToJs("notification", new { message = $"Transmitted Format 03H Custom Drummer pattern '{safeBase}' ({sysex.Length} bytes) to PSS-680." });
                }
                else if (action == "save_sysex")
                {
                    byte[] sysex = pattern.ToSysExMessage();
                    string outPath = Path.Combine(AppPaths.SysExDir, $"{safeBase}_Drummer.syx");
                    File.WriteAllBytes(outPath, sysex);
                    SendSysExLibraryUpdate();
                    SendToJs("notification", new { message = $"Saved Format 03H SysEx: {Path.GetFileName(outPath)}" });
                }
                else
                {
                    var midi = pattern.ToMidiFile(bpm, loopCount: 4);
                    if (action == "audition")
                    {
                        LoadMidiObject(midi, $"{safeBase} (Drummer Loop)");
                        _player.Play();
                    }
                    else
                    {
                        string outPath = Path.Combine(AppPaths.Pss680MidiDir, $"{safeBase}_Drummer.mid");
                        midi.Write(outPath, overwriteFile: true);
                        SendLibraryUpdate();
                        SendToJs("notification", new { message = $"Exported Custom Drummer MIDI: {Path.GetFileName(outPath)}" });
                    }
                }
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = $"Custom Drummer build error: {ex.Message}" });
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "Preset" : clean;
        }

        private void ResetChannelActivity()
        {
            lock (_channelActivityLock)
            {
                Array.Clear(_channelActiveStates, 0, 16);
                Array.Clear(_channelVelocities, 0, 16);
                _channelActivityDirty = false;
            }
            SendToJs("channel_activity_batch", new
            {
                active = new bool[16],
                velocities = new byte[16]
            });
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try { _channelBatchTimer?.Dispose(); } catch { }

            lock (_ctsLock)
            {
                try { _batchCts?.Cancel(); } catch { }
                try { _batchCts?.Dispose(); } catch { }
                _batchCts = null;

                try { _syncCts?.Cancel(); } catch { }
                try { _syncCts?.Dispose(); } catch { }
                _syncCts = null;
            }

            try { _deviceMonitor?.Dispose(); } catch { }
            try { _sysexManager?.Dispose(); } catch { }
            try { _recorder?.Dispose(); } catch { }
            try { _player?.Dispose(); } catch { }
            GC.SuppressFinalize(this);
        }
    }
}
