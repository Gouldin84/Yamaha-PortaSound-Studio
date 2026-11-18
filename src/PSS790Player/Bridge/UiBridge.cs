using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly WebView2 _webView;
        private readonly MidiPlayer _player;
        private readonly MidiRecorder _recorder;
        private readonly SysExManager _sysexManager;
        private readonly MidiDeviceMonitor _deviceMonitor;
        private readonly RemapperOptions _options = new();
        private string _activeModel = "pss790";
        private MidiFile? _loadedOriginalMidi;
        private MidiFile? _loadedRemappedMidi;
        private string? _currentFilePath;
        private CancellationTokenSource? _batchCts;
        private CancellationTokenSource? _syncCts;

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
                SendToJs("channel_activity", new
                {
                    channel = e.Channel,
                    isActive = e.IsActive,
                    velocity = e.Velocity,
                    noteNumber = e.NoteNumber
                });
            };

            _player.PlaybackStateChanged += (s, e) =>
            {
                SendToJs("playback_state", new
                {
                    isPlaying = e.IsPlaying,
                    isPaused = e.IsPaused,
                    currentSeconds = e.CurrentTime.TotalSeconds,
                    totalSeconds = e.TotalTime.TotalSeconds
                });
            };

            _recorder.InputActivity += (s, e) =>
            {
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
                SendToJs("recorder_state", new
                {
                    state = e.State.ToString().ToLowerInvariant(),
                    countInBeat = e.CountInBeat,
                    bpm = e.Bpm
                });
            };

            _recorder.Tick += (s, e) =>
            {
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

            _sysexManager.CaptureProgress += (s, e) =>
            {
                SendToJs("sysex_capture_progress", new
                {
                    packetCount = e.PacketCount,
                    byteCount = e.ByteCount,
                    statusMessage = e.StatusMessage
                });
            };

            _sysexManager.DumpCaptured += (s, dump) =>
            {
                SendToJs("sysex_dump_captured", new
                {
                    fileName = dump.FileName,
                    filePath = dump.FilePath,
                    fileSize = dump.FileSizeFormatted,
                    createdAt = dump.CreatedAtFormatted,
                    packetCount = dump.PacketCount,
                    manufacturer = dump.Manufacturer,
                    dumpType = dump.DumpType,
                    hexPreview = dump.HexPreview
                });
                SendSysExLibraryUpdate();
                SendToJs("notification", new { message = $"SysEx bulk dump auto-saved: {dump.FileName}" });
            };

            _sysexManager.TransmitProgress += (s, e) =>
            {
                SendToJs("sysex_transmit_progress", new
                {
                    currentPacket = e.CurrentPacket,
                    totalPackets = e.TotalPackets,
                    percent = e.Percent,
                    statusMessage = e.StatusMessage,
                    isCompleted = e.IsCompleted
                });
            };

            // Hot-swap MIDI device monitoring
            _deviceMonitor = new MidiDeviceMonitor();
            _deviceMonitor.DevicesChanged += OnMidiDevicesChanged;

            _player.OutputDeviceDisconnected += (s, devName) =>
            {
                SendDevicesUpdated($"MIDI Output disconnected: {devName}");
            };

            _recorder.InputDeviceDisconnected += (s, devName) =>
            {
                SendDevicesUpdated($"MIDI Input disconnected: {devName}");
            };
        }

        public void HandleDeviceChangeNotification()
        {
            _deviceMonitor.TriggerRescan(300);
        }

        private void OnMidiDevicesChanged(object? sender, MidiDevicesChangedEventArgs e)
        {
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
            var currentOutput = _player.CurrentOutputDeviceName ?? outputDevices.FirstOrDefault();
            var currentInput = _recorder.CurrentInputDeviceName ?? inputDevices.FirstOrDefault();

            SendToJs("devices_updated", new
            {
                devices = outputDevices,
                inputDevices = inputDevices,
                currentOutputDevice = currentOutput,
                currentInputDevice = currentInput,
                toastMessage = toastMessage
            });
        }

        public void HandleMessage(string json)
        {
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
                        break;
                    case "analyze_library_sync":
                        HandleAnalyzeLibrarySync();
                        break;
                    case "execute_library_sync":
                        HandleExecuteLibrarySync(root);
                        break;
                    case "cancel_library_sync":
                        _syncCts?.Cancel();
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
                    case "load_midi_metadata":
                        var path = root.TryGetProperty("filePath", out var fp) ? fp.GetString() : (root.TryGetProperty("path", out var p) ? p.GetString() : null);
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (string.Equals(_currentFilePath, path, StringComparison.OrdinalIgnoreCase) && _player.IsPlaying)
                            {
                                break;
                            }

                            bool shouldPlay = _player.IsPlaying;
                            LoadFile(path, autoPlay: shouldPlay);
                        }
                        break;
                    case "load_midi_and_play":
                        var playPath = root.TryGetProperty("filePath", out var fpp) ? fpp.GetString() : (root.TryGetProperty("path", out var pp) ? pp.GetString() : null);
                        if (!string.IsNullOrEmpty(playPath)) LoadFile(playPath, autoPlay: true);
                        break;
                    case "reset_voice_overrides":
                        if (_currentFilePath != null && _loadedOriginalMidi != null)
                        {
                            VoiceOverrideStore.Instance.ResetOverrides(_currentFilePath);
                            _options.ChannelVoiceOverrides.Clear();
                            ReapplyRemapping(preservePosition: true);
                            var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                            var channelInfo = AnalyzeChannels(_loadedOriginalMidi, format);
                            SendToJs("channels_updated", new { channels = channelInfo });
                            SendToJs("notification", new { message = "Reset voices to default GM mapping." });
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
                        break;
                    case "seek":
                        var seconds = root.GetProperty("seconds").GetDouble();
                        _player.Seek(TimeSpan.FromSeconds(seconds));
                        break;
                    case "set_active_model":
                        var selectedModel = root.GetProperty("model").GetString() ?? "pss790";
                        _activeModel = selectedModel;
                        _options.TargetModel = selectedModel;
                        if (_currentFilePath != null && _loadedOriginalMidi != null)
                        {
                            _options.ChannelVoiceOverrides.Clear();
                            var savedModelOverrides = VoiceOverrideStore.Instance.GetOverrides(_currentFilePath, _activeModel);
                            foreach (var (oCh, oVoice) in savedModelOverrides)
                            {
                                _options.ChannelVoiceOverrides[oCh] = oVoice;
                            }
                            ReapplyRemapping(preservePosition: true);
                            var activeFormat = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                            var activeChannels = AnalyzeChannels(_loadedOriginalMidi, activeFormat);
                            var activeHealth = MidiHealthInspector.Analyze(_loadedOriginalMidi, _loadedRemappedMidi, Path.GetFileName(_currentFilePath), _activeModel);
                            SendToJs("channels_updated", new { channels = activeChannels });
                            SendToJs("health_updated", BuildHealthPayload(activeHealth));
                        }
                        SendLibraryUpdate();
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
                        _options.ChannelVoiceOverrides[ch] = voice;
                        if (_currentFilePath != null)
                        {
                            VoiceOverrideStore.Instance.SetOverride(_currentFilePath, ch, voice, _activeModel);
                        }
                        if (voice.HasValue)
                        {
                            _player.SendVoiceOverride(ch, voice.Value);
                        }
                        ReapplyRemapping(preservePosition: true);
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
                    case "toggle_polyphony_guard":
                        var guard = root.GetProperty("enabled").GetBoolean();
                        _options.EnablePolyphonyGuard = guard;
                        ReapplyRemapping(preservePosition: true);
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

                        // PSS-790 Remapper Options
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

                        // PSS-680 Remapper Options
                        var opt680 = new RemapperOptions
                        {
                            TargetModel = "pss680",
                            EnablePolyphonyGuard = !root.TryGetProperty("polyphonyGuard680", out var pol680Prop) 
                                ? (!root.TryGetProperty("polyphonyGuard", out var polProp2) || polProp2.GetBoolean()) 
                                : pol680Prop.GetBoolean(),
                            DefaultReverbBoost = false, // PSS-680 has no onboard DSP reverb
                            FilterUnsupportedControllers = !root.TryGetProperty("cleanControllers680", out var cleanProp) || cleanProp.GetBoolean(),
                            NormalizeAwmVelocities = false, // FM engine uses natural 1-127 linear velocity
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
                        _batchCts?.Cancel();
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
                        _recorder.StopRecording();
                        var stats = _recorder.GetLastTakeStats();
                        var autoSavedPath = _recorder.AutoSaveTake();
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
                            autoSavedPath = autoSavedPath
                        });
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
                            try
                            {
                                var prevMidi = Melanchall.DryWetMidi.Core.MidiFile.Read(prevPath);
                                _player.LoadMidi(prevMidi);
                                _player.Play();
                                SendToJs("recording_preview_started", new { filePath = prevPath, fileName = Path.GetFileName(prevPath) });
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = $"Failed to preview recording: {ex.Message}" });
                            }
                        }
                        break;
                    case "stop_recording_preview":
                        _player.Stop();
                        SendToJs("recording_preview_stopped", new { });
                        break;
                    case "export_recording_to_gm":
                        var expGmPath = root.TryGetProperty("filePath", out var egProp) ? egProp.GetString() : null;
                        var customGmName = root.TryGetProperty("fileName", out var cgnProp) ? cgnProp.GetString() : null;
                        if (!string.IsNullOrEmpty(expGmPath))
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
                        }
                        break;
                    case "export_recording_to_pss":
                        var expPssPath = root.TryGetProperty("filePath", out var epProp) ? epProp.GetString() : null;
                        var customPssName = root.TryGetProperty("fileName", out var cpnProp) ? cpnProp.GetString() : null;
                        if (!string.IsNullOrEmpty(expPssPath))
                        {
                            try
                            {
                                var modelLabel = _activeModel == "pss680" ? "PSS-680" : "PSS-790";
                                var exportedPss = _recorder.ExportRecordingToPss(expPssPath, customPssName, _activeModel);
                                SendLibraryUpdate();
                                SendToJs("notification", new { message = $"Exported to {modelLabel} library: {Path.GetFileName(exportedPss)}" });
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = $"Failed to export to {(_activeModel == "pss680" ? "PSS-680" : "PSS-790")}: {ex.Message}" });
                            }
                        }
                        break;
                    case "delete_recording":
                        var delRecPath = root.TryGetProperty("filePath", out var drProp) ? drProp.GetString() : null;
                        if (!string.IsNullOrEmpty(delRecPath))
                        {
                            _recorder.DeleteRecording(delRecPath);
                            SendRecordingsLibraryUpdate();
                            SendToJs("notification", new { message = $"Deleted recording take: {Path.GetFileName(delRecPath)}" });
                        }
                        break;
                    case "rename_recording":
                        var renRecPath = root.TryGetProperty("filePath", out var rrProp) ? rrProp.GetString() : null;
                        var newRecName = root.TryGetProperty("newName", out var nrProp) ? nrProp.GetString() : null;
                        if (!string.IsNullOrEmpty(renRecPath) && !string.IsNullOrWhiteSpace(newRecName))
                        {
                            try
                            {
                                var renamedPath = _recorder.RenameRecording(renRecPath, newRecName);
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
                        var savedPath = isNativePss 
                            ? _recorder.SaveNativeTake(takeFileName, _activeModel) 
                            : _recorder.ExportTakeToGeneralMidi(takeFileName, _activeModel);
                        SendLibraryUpdate();
                        SendToJs("take_saved", new
                        {
                            filePath = savedPath,
                            fileName = Path.GetFileName(savedPath),
                            isPss790 = isNativePss && _activeModel != "pss680",
                            isPss680 = isNativePss && _activeModel == "pss680",
                            activeModel = _activeModel
                        });
                        break;
                    case "send_take_to_player":
                        var takeFile = _recorder.LastRecordedFile;
                        if (takeFile != null)
                        {
                            var title = root.TryGetProperty("title", out var tTitle) ? tTitle.GetString() ?? "Recorded Take" : "Recorded Take";
                            var cloned = takeFile.Clone();
                            MidiRecorder.EmbedRecordingMarker(cloned);
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
                        var txDelay = root.TryGetProperty("delayMs", out var txdP) ? txdP.GetInt32() : 30;
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
                        if (!string.IsNullOrEmpty(delPath))
                        {
                            _sysexManager.DeleteDump(delPath);
                            SendSysExLibraryUpdate();
                            SendToJs("notification", new { message = "SysEx backup deleted." });
                        }
                        break;
                    case "rename_sysex_dump":
                        var renPath = root.TryGetProperty("filePath", out var renP) ? renP.GetString() : null;
                        var newName = root.TryGetProperty("newName", out var nnP) ? nnP.GetString() : null;
                        if (!string.IsNullOrEmpty(renPath) && !string.IsNullOrEmpty(newName))
                        {
                            try
                            {
                                _sysexManager.RenameDump(renPath, newName);
                                SendSysExLibraryUpdate();
                                SendToJs("notification", new { message = $"Renamed to: {newName}" });
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = ex.Message });
                            }
                        }
                        break;
                    case "import_sysex_dump":
                        Application.Current.Dispatcher.Invoke(() =>
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
                        });
                        break;
                    case "inspect_sysex_dump":
                        var inspPath = root.TryGetProperty("filePath", out var ipP) ? ipP.GetString() : null;
                        if (!string.IsNullOrEmpty(inspPath))
                        {
                            try
                            {
                                var info = SysExManager.ParseDumpInfo(inspPath);
                                SendToJs("sysex_dump_inspected", info);
                            }
                            catch (Exception ex)
                            {
                                SendToJs("error", new { message = $"Failed to inspect SysEx dump: {ex.Message}" });
                            }
                        }
                        break;
                    case "send_quick_sysex":
                        var cmd = root.TryGetProperty("command", out var cCmd) ? cCmd.GetString() : null;
                        if (!string.IsNullOrEmpty(cmd))
                        {
                            _sysexManager.SendQuickCommand(cmd);
                            SendToJs("notification", new { message = $"Transmitted SysEx command: {cmd.ToUpperInvariant()}" });
                        }
                        break;
                    case "open_sysex_folder":
                        AppPaths.OpenSysExFolderInExplorer();
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

                SendToJs("initial_data", new
                {
                    devices,
                    inputDevices,
                    voices,
                    pss790Voices = voices,
                    pss680Voices,
                    pss680Drums,
                    library = libraryData,
                    defaultDevice = devices.FirstOrDefault(),
                    defaultInputDevice = inputDevices.FirstOrDefault(),
                    defaultBatchSource = AppPaths.GmMidiDir,
                    defaultBatchTarget = AppPaths.Pss790MidiDir,
                    defaultBatchTarget790 = AppPaths.Pss790MidiDir,
                    defaultBatchTarget680 = AppPaths.Pss680MidiDir,
                    midiThru = _recorder.MidiThruEnabled,
                    recorderBpm = _recorder.Bpm,
                    sysexLibrary = _sysexManager.GetSysExLibrary(),
                    recordingsLibrary = _recorder.GetRecordingsLibrary()
                });

                if (devices.Count > 0)
                {
                    _player.SetOutputDevice(devices[0]);
                }

                if (inputDevices.Count > 0)
                {
                    _recorder.SetInputDevice(inputDevices[0]);
                }
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = $"Failed to get initial data: {ex.Message}" });
            }
        }

        private void HandleCreateFolder(JsonElement root)
        {
            var category = root.TryGetProperty("category", out var catProp) ? catProp.GetString() : "gm";
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
            var pssTarget = Path.Combine(AppPaths.Pss790MidiDir, parentSubFolder, folderName);

            if (!AppPaths.IsPathInsideLibrary(gmTarget) || !AppPaths.IsPathInsideLibrary(pssTarget))
            {
                SendToJs("error", new { message = "Invalid directory path." });
                return;
            }

            if (Directory.Exists(gmTarget) || Directory.Exists(pssTarget))
            {
                SendToJs("error", new { message = $"Folder '{folderName}' already exists." });
                return;
            }

            Directory.CreateDirectory(gmTarget);
            Directory.CreateDirectory(pssTarget);
            SendLibraryUpdate();
            SendToJs("notification", new { message = $"Created folder: {folderName}" });
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
                if (Directory.Exists(newPath))
                {
                    SendToJs("error", new { message = $"Folder '{newName}' already exists." });
                    return;
                }

                Directory.Move(fullPath, newPath);
                VoiceOverrideStore.Instance.RenameFolder(fullPath, newPath);

                if (_currentFilePath != null && _currentFilePath.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    _currentFilePath = newPath + _currentFilePath.Substring(fullPath.Length);
                }

                // Symmetrical folder rename in mirrored counterpart library
                try
                {
                    var isGm = fullPath.StartsWith(AppPaths.GmMidiDir, StringComparison.OrdinalIgnoreCase);
                    var srcRoot = isGm ? AppPaths.GmMidiDir : AppPaths.Pss790MidiDir;
                    var mirrorRoot = isGm ? AppPaths.Pss790MidiDir : AppPaths.GmMidiDir;

                    var relFolder = Path.GetRelativePath(srcRoot, fullPath);
                    var mirrorFolder = Path.Combine(mirrorRoot, relFolder);

                    if (Directory.Exists(mirrorFolder))
                    {
                        var mirrorParent = Path.GetDirectoryName(mirrorFolder) ?? mirrorRoot;
                        var newMirrorPath = Path.Combine(mirrorParent, newName);
                        if (!Directory.Exists(newMirrorPath))
                        {
                            Directory.Move(mirrorFolder, newMirrorPath);
                            VoiceOverrideStore.Instance.RenameFolder(mirrorFolder, newMirrorPath);

                            if (_currentFilePath != null && _currentFilePath.StartsWith(mirrorFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            {
                                _currentFilePath = newMirrorPath + _currentFilePath.Substring(mirrorFolder.Length);
                            }
                        }
                    }
                }
                catch { }

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
                if (File.Exists(newPath))
                {
                    SendToJs("error", new { message = $"File '{newName}' already exists." });
                    return;
                }

                File.Move(fullPath, newPath);
                VoiceOverrideStore.Instance.RenameFile(fullPath, newPath);

                if (string.Equals(_currentFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _currentFilePath = newPath;
                }

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

            var baseDir = string.Equals(targetCategory, "pss790", StringComparison.OrdinalIgnoreCase)
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

            if (string.Equals(_currentFilePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentFilePath = destPath;
            }

            // Linked file move across mirrored destination
            var movedLinked = false;
            if (alsoMoveLinked)
            {
                if (string.IsNullOrWhiteSpace(linkedPath) || !File.Exists(linkedPath))
                {
                    linkedPath = AppPaths.FindMatchingLinkedFile(sourcePath);
                }

                if (!string.IsNullOrWhiteSpace(linkedPath) && File.Exists(linkedPath) && AppPaths.IsPathInsideLibrary(linkedPath))
                {
                    var isSourceGm = sourcePath.StartsWith(AppPaths.GmMidiDir, StringComparison.OrdinalIgnoreCase);
                    var mirrorBaseDir = isSourceGm ? AppPaths.Pss790MidiDir : AppPaths.GmMidiDir;
                    var mirrorDestDir = Path.Combine(mirrorBaseDir, targetSubFolder);

                    if (!Directory.Exists(mirrorDestDir))
                    {
                        Directory.CreateDirectory(mirrorDestDir);
                    }

                    var linkedFileName = Path.GetFileName(linkedPath);
                    var mirrorDestPath = Path.Combine(mirrorDestDir, linkedFileName);

                    if (!string.Equals(linkedPath, mirrorDestPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(mirrorDestPath))
                    {
                        File.Move(linkedPath, mirrorDestPath);
                        VoiceOverrideStore.Instance.RenameFile(linkedPath, mirrorDestPath);

                        if (string.Equals(_currentFilePath, linkedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _currentFilePath = mirrorDestPath;
                        }
                        movedLinked = true;
                    }
                }
            }

            SendLibraryUpdate();
            var folderLabel = string.IsNullOrEmpty(targetSubFolder) ? "(Root)" : targetSubFolder;
            SendToJs("notification", new { message = movedLinked ? $"Moved both GM and PSS-790 versions to {folderLabel}" : $"Moved '{fileName}' to {folderLabel}" });
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

            var baseDir = string.Equals(targetCategory, "pss790", StringComparison.OrdinalIgnoreCase)
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

            var isTargetGm = string.Equals(targetCategory, "gm", StringComparison.OrdinalIgnoreCase);
            var mirrorBaseDir = isTargetGm ? AppPaths.Pss790MidiDir : AppPaths.GmMidiDir;
            var mirrorDestDir = Path.Combine(mirrorBaseDir, targetSubFolder);

            int movedCount = 0;
            int movedLinkedCount = 0;

            foreach (var sourcePath in sourcePaths)
            {
                if (!File.Exists(sourcePath) || !AppPaths.IsPathInsideLibrary(sourcePath)) continue;

                var fileName = Path.GetFileName(sourcePath);
                var destPath = Path.Combine(destDir, fileName);

                if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(destPath))
                    {
                        continue;
                    }

                    File.Move(sourcePath, destPath);
                    VoiceOverrideStore.Instance.RenameFile(sourcePath, destPath);

                    if (string.Equals(_currentFilePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentFilePath = destPath;
                    }
                    movedCount++;
                }

                if (alsoMoveLinked)
                {
                    var linkedPath = AppPaths.FindMatchingLinkedFile(sourcePath);
                    if (!string.IsNullOrWhiteSpace(linkedPath) && File.Exists(linkedPath) && AppPaths.IsPathInsideLibrary(linkedPath))
                    {
                        if (!Directory.Exists(mirrorDestDir))
                        {
                            Directory.CreateDirectory(mirrorDestDir);
                        }

                        var linkedFileName = Path.GetFileName(linkedPath);
                        var mirrorDestPath = Path.Combine(mirrorDestDir, linkedFileName);

                        if (!string.Equals(linkedPath, mirrorDestPath, StringComparison.OrdinalIgnoreCase) && !File.Exists(mirrorDestPath))
                        {
                            File.Move(linkedPath, mirrorDestPath);
                            VoiceOverrideStore.Instance.RenameFile(linkedPath, mirrorDestPath);

                            if (string.Equals(_currentFilePath, linkedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                _currentFilePath = mirrorDestPath;
                            }
                            movedLinkedCount++;
                        }
                    }
                }
            }

            SendLibraryUpdate();
            var folderLabel = string.IsNullOrEmpty(targetSubFolder) ? "(Root)" : targetSubFolder;
            var msg = movedLinkedCount > 0
                ? $"Moved {movedCount} songs (+ {movedLinkedCount} linked) to {folderLabel}"
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

            var baseDir = string.Equals(targetCategory, "pss790", StringComparison.OrdinalIgnoreCase)
                ? AppPaths.Pss790MidiDir
                : AppPaths.GmMidiDir;

            var folderName = Path.GetFileName(sourceFolderPath);
            var destDir = Path.Combine(baseDir, targetParentSubFolder, folderName);

            // Prevent moving folder into itself or its own subfolder
            if (destDir.StartsWith(sourceFolderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
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

            if (_currentFilePath != null && _currentFilePath.StartsWith(sourceFolderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _currentFilePath = destDir + _currentFilePath.Substring(sourceFolderPath.Length);
            }

            // Symmetrically move mirrored folder in counterpart library
            try
            {
                var isGm = sourceFolderPath.StartsWith(AppPaths.GmMidiDir, StringComparison.OrdinalIgnoreCase);
                var srcRoot = isGm ? AppPaths.GmMidiDir : AppPaths.Pss790MidiDir;
                var mirrorRoot = isGm ? AppPaths.Pss790MidiDir : AppPaths.GmMidiDir;

                var relFolder = Path.GetRelativePath(srcRoot, sourceFolderPath);
                var mirrorSourceFolder = Path.Combine(mirrorRoot, relFolder);

                if (Directory.Exists(mirrorSourceFolder))
                {
                    var mirrorDestDir = Path.Combine(mirrorRoot, targetParentSubFolder, folderName);
                    var mirrorDestParent = Path.GetDirectoryName(mirrorDestDir) ?? mirrorRoot;
                    if (!Directory.Exists(mirrorDestParent))
                    {
                        Directory.CreateDirectory(mirrorDestParent);
                    }

                    if (!Directory.Exists(mirrorDestDir))
                    {
                        Directory.Move(mirrorSourceFolder, mirrorDestDir);
                        VoiceOverrideStore.Instance.RenameFolder(mirrorSourceFolder, mirrorDestDir);

                        if (_currentFilePath != null && _currentFilePath.StartsWith(mirrorSourceFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            _currentFilePath = mirrorDestDir + _currentFilePath.Substring(mirrorSourceFolder.Length);
                        }
                    }
                }
            }
            catch { }

            SendLibraryUpdate();
            SendToJs("notification", new { message = $"Moved folder '{folderName}'" });
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
                        if (AppPaths.FindMatchingLinkedFile(p) != null) linkedFound++;
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
                var isGm = filePath.StartsWith(AppPaths.GmMidiDir, StringComparison.OrdinalIgnoreCase);
                var srcRoot = isGm ? AppPaths.GmMidiDir : AppPaths.Pss790MidiDir;
                var mirrorRoot = isGm ? AppPaths.Pss790MidiDir : AppPaths.GmMidiDir;
                var rel = Path.GetRelativePath(srcRoot, filePath);
                var mirrorFolder = Path.Combine(mirrorRoot, rel);
                var exists = Directory.Exists(mirrorFolder);
                SendToJs("linked_file_checked", new
                {
                    filePath,
                    hasLinked = exists,
                    linkedPath = exists ? mirrorFolder : null,
                    linkedName = exists ? AppPaths.GetRelativeLibraryPath(mirrorFolder) : null,
                    isFolder = true
                });
                return;
            }

            var linked = AppPaths.FindMatchingLinkedFile(filePath);
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

                if (string.Equals(_currentFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _player.Stop();
                    _currentFilePath = null;
                    _loadedOriginalMidi = null;
                    _loadedRemappedMidi = null;
                }

                VoiceOverrideStore.Instance.RemoveOverrides(fullPath);
                File.Delete(fullPath);
                deletedCount++;

                if (alsoDeleteLinked)
                {
                    var linkedPath = AppPaths.FindMatchingLinkedFile(fullPath);
                    if (!string.IsNullOrWhiteSpace(linkedPath) && File.Exists(linkedPath) && AppPaths.IsPathInsideLibrary(linkedPath))
                    {
                        if (string.Equals(_currentFilePath, linkedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _player.Stop();
                            _currentFilePath = null;
                            _loadedOriginalMidi = null;
                            _loadedRemappedMidi = null;
                        }

                        VoiceOverrideStore.Instance.RemoveOverrides(linkedPath);
                        File.Delete(linkedPath);
                        deletedLinkedCount++;
                    }
                }
            }

            SendLibraryUpdate();
            var msg = deletedLinkedCount > 0
                ? $"Deleted {deletedCount} songs (+ {deletedLinkedCount} linked)"
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
                    if (_currentFilePath != null && _currentFilePath.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        _player.Stop();
                        _currentFilePath = null;
                        _loadedOriginalMidi = null;
                        _loadedRemappedMidi = null;
                    }

                    VoiceOverrideStore.Instance.RemoveFolderOverrides(fullPath);
                    Directory.Delete(fullPath, recursive: true);

                    var deletedMirrored = false;
                    if (alsoDeleteLinked)
                    {
                        if (string.IsNullOrWhiteSpace(linkedPath) || !Directory.Exists(linkedPath))
                        {
                            var isGm = fullPath.StartsWith(AppPaths.GmMidiDir, StringComparison.OrdinalIgnoreCase);
                            var srcRoot = isGm ? AppPaths.GmMidiDir : AppPaths.Pss790MidiDir;
                            var mirrorRoot = isGm ? AppPaths.Pss790MidiDir : AppPaths.GmMidiDir;
                            var relFolder = Path.GetRelativePath(srcRoot, fullPath);
                            linkedPath = Path.Combine(mirrorRoot, relFolder);
                        }

                        if (!string.IsNullOrWhiteSpace(linkedPath) && Directory.Exists(linkedPath) && AppPaths.IsPathInsideLibrary(linkedPath))
                        {
                            if (_currentFilePath != null && _currentFilePath.StartsWith(linkedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            {
                                _player.Stop();
                                _currentFilePath = null;
                                _loadedOriginalMidi = null;
                                _loadedRemappedMidi = null;
                            }

                            VoiceOverrideStore.Instance.RemoveFolderOverrides(linkedPath);
                            Directory.Delete(linkedPath, recursive: true);
                            deletedMirrored = true;
                        }
                    }

                    SendLibraryUpdate();
                    SendToJs("notification", new { message = deletedMirrored ? $"Deleted mirrored folder: {Path.GetFileName(fullPath)}" : $"Deleted folder: {Path.GetFileName(fullPath)}" });
                }
            }
            else
            {
                if (File.Exists(fullPath))
                {
                    if (string.Equals(_currentFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _player.Stop();
                        _currentFilePath = null;
                        _loadedOriginalMidi = null;
                        _loadedRemappedMidi = null;
                    }

                    VoiceOverrideStore.Instance.RemoveOverrides(fullPath);
                    File.Delete(fullPath);

                    var deletedBoth = false;
                    if (alsoDeleteLinked && !string.IsNullOrWhiteSpace(linkedPath) && File.Exists(linkedPath) && AppPaths.IsPathInsideLibrary(linkedPath))
                    {
                        if (string.Equals(_currentFilePath, linkedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _player.Stop();
                            _currentFilePath = null;
                            _loadedOriginalMidi = null;
                            _loadedRemappedMidi = null;
                        }

                        VoiceOverrideStore.Instance.RemoveOverrides(linkedPath);
                        File.Delete(linkedPath);
                        deletedBoth = true;
                    }

                    SendLibraryUpdate();
                    SendToJs("notification", new { message = deletedBoth ? "Deleted both GM and PSS-790 versions." : $"Deleted: {Path.GetFileName(fullPath)}" });
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

        private void SendInputDevices()
        {
            var inputDevices = MidiRecorder.GetInputDeviceNames();
            SendToJs("input_devices_updated", new
            {
                inputDevices,
                currentDevice = _recorder.CurrentInputDeviceName
            });
        }

        private void SendLibraryUpdate()
        {
            var libraryData = ScanLibraryFiles();
            SendToJs("library_updated", libraryData);
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
                activeModel = _activeModel
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

            // Populate legacy folderGroups with files so legacy/fallback 'folders' has .files and .count
            foreach (var file in flatCollector.Where(f => f.category == categoryName))
            {
                var sub = file.subFolder;
                if (!string.IsNullOrEmpty(sub) && folderGroups.TryGetValue(sub, out var list))
                {
                    list.Add(file);
                }
            }

            var legacyFoldersList = folderGroups
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
                        format = format
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
                    subFolderNodes.Add(BuildDirectoryNode(subDir, rootDir, categoryName, format, flatCollector));
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
                    LoadFile(dialog.FileName);
                }
            });
        }

        public void LoadMidiObject(MidiFile midi, string title, string? virtualPath = null)
        {
            try
            {
                _player.Stop();
                _player.Seek(TimeSpan.Zero);
                _currentFilePath = virtualPath ?? title;
                _loadedOriginalMidi = midi;

                var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                bool isRec = Remapper.IsRecording(_loadedOriginalMidi);
                ReapplyRemapping(preservePosition: false);

                var channelInfo = AnalyzeChannels(_loadedOriginalMidi, format);
                var duration = _loadedOriginalMidi.GetDuration<MetricTimeSpan>();
                var health = MidiHealthInspector.Analyze(_loadedOriginalMidi, _loadedRemappedMidi, title, _activeModel);

                var modelTag = _activeModel == "pss680" ? "PSS-680" : "PSS-790";
                bool isNativeThisModel = _activeModel == "pss680" ? (format == MidiFormat.Pss680Native) : (format == MidiFormat.Pss790Native);
                bool isNativeCounterpart = _activeModel == "pss680" ? (format == MidiFormat.Pss790Native) : (format == MidiFormat.Pss680Native);

                string fmtCode = isRec ? "rec" : (isNativeThisModel ? _activeModel : (isNativeCounterpart ? (_activeModel == "pss680" ? "pss790" : "pss680") : "gm"));
                string fmtLabel = isRec ? $"{modelTag} Studio Recording (Passthrough)" :
                                  (isNativeThisModel ? $"{modelTag} Ready (Passthrough)" :
                                  (isNativeCounterpart ? (_activeModel == "pss680" ? "PSS-790 Ready (Remapped for PSS-680)" : "PSS-680 Ready (Remapped for PSS-790)") :
                                  $"General MIDI (Auto-Remapped for {modelTag})"));

                SendToJs("file_loaded", new
                {
                    fileName = title,
                    filePath = virtualPath ?? title,
                    relativeDir = "",
                    format = fmtCode,
                    formatLabel = fmtLabel,
                    durationSeconds = duration.TotalSeconds,
                    durationFormatted = $"{duration.Minutes:D2}:{duration.Seconds:D2}",
                    channels = channelInfo,
                    hasCustomOverrides = false,
                    health = BuildHealthPayload(health)
                });
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Error loading recorded take: {ex.Message}" });
                }
            }

            public void LoadFile(string pathOrName, bool autoPlay = false)
            {
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

                    _player.Stop();
                    _player.Seek(TimeSpan.Zero);

                    _currentFilePath = resolvedPath;
                    _loadedOriginalMidi = MidiFile.Read(resolvedPath, Constants.DefaultReadingSettings);

                    // Restore saved per-song voice overrides for the active model from VoiceOverrideStore
                    _options.ChannelVoiceOverrides.Clear();
                    _options.TargetModel = _activeModel;
                    var savedOverrides = VoiceOverrideStore.Instance.GetOverrides(resolvedPath, _activeModel);
                    foreach (var (ch, voice) in savedOverrides)
                    {
                        _options.ChannelVoiceOverrides[ch] = voice;
                    }

                    var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                    bool isRec = Remapper.IsRecording(_loadedOriginalMidi);
                    ReapplyRemapping(preservePosition: false);

                    bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
                    bool isNative = (format == MidiFormat.Pss790Native) || (format == MidiFormat.Pss680Native);
                    bool isGmPassthrough = isMsGs && !isNative;

                    var channelInfo = AnalyzeChannels(_loadedOriginalMidi, format);
                    var duration = _loadedOriginalMidi.GetDuration<MetricTimeSpan>();
                    var health = MidiHealthInspector.Analyze(_loadedOriginalMidi, _loadedRemappedMidi, Path.GetFileName(resolvedPath), _activeModel);

                    var modelTag = _activeModel == "pss680" ? "PSS-680" : "PSS-790";
                    bool isNativeThisModel = _activeModel == "pss680" ? (format == MidiFormat.Pss680Native) : (format == MidiFormat.Pss790Native);
                    bool isNativeCounterpart = _activeModel == "pss680" ? (format == MidiFormat.Pss790Native) : (format == MidiFormat.Pss680Native);

                    string fmtCode = isRec ? "rec" : (isNativeThisModel ? _activeModel : (isNativeCounterpart ? (_activeModel == "pss680" ? "pss790" : "pss680") : "gm"));
                    string fmtLabel = isRec ? $"{modelTag} Studio Recording (Passthrough)" :
                                      (isNativeThisModel ? $"{modelTag} Ready (Passthrough)" :
                                      (isNativeCounterpart ? (_activeModel == "pss680" ? "PSS-790 Ready (Remapped for PSS-680)" : "PSS-680 Ready (Remapped for PSS-790)") :
                                      (isGmPassthrough ? "Windows GS Synth (Original GM Passthrough)" : $"General MIDI (Auto-Remapped for {modelTag})")));

                    SendToJs("file_loaded", new
                    {
                        fileName = Path.GetFileName(resolvedPath),
                        filePath = resolvedPath,
                        relativeDir = Path.GetDirectoryName(AppPaths.GetRelativeLibraryPath(resolvedPath)) ?? "",
                        format = fmtCode,
                        formatLabel = fmtLabel,
                        isGmPassthrough = isGmPassthrough,
                        durationSeconds = duration.TotalSeconds,
                        durationFormatted = $"{duration.Minutes:D2}:{duration.Seconds:D2}",
                        channels = channelInfo,
                        hasCustomOverrides = VoiceOverrideStore.Instance.HasOverrides(resolvedPath),
                        health = BuildHealthPayload(health)
                    });

                    if (autoPlay)
                    {
                        _player.Play();
                    }
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Error loading MIDI: {ex.Message}" });
                }
            }

            private void HandleDeviceSelectionChanged()
            {
                if (_loadedOriginalMidi != null)
                {
                    var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                    bool isRec = Remapper.IsRecording(_loadedOriginalMidi);
                    bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
                    bool isNative = (format == MidiFormat.Pss790Native) || (format == MidiFormat.Pss680Native);
                    bool isGmPassthrough = isMsGs && !isNative;

                    ReapplyRemapping(preservePosition: true);

                    var modelTag = _activeModel == "pss680" ? "PSS-680" : "PSS-790";
                    bool isNativeThisModel = _activeModel == "pss680" ? (format == MidiFormat.Pss680Native) : (format == MidiFormat.Pss790Native);
                    bool isNativeCounterpart = _activeModel == "pss680" ? (format == MidiFormat.Pss790Native) : (format == MidiFormat.Pss680Native);

                    string fmtCode = isRec ? "rec" : (isNativeThisModel ? _activeModel : (isNativeCounterpart ? (_activeModel == "pss680" ? "pss790" : "pss680") : "gm"));
                    string fmtLabel = isRec ? $"{modelTag} Studio Recording (Passthrough)" :
                                      (isNativeThisModel ? $"{modelTag} Ready (Passthrough)" :
                                      (isNativeCounterpart ? (_activeModel == "pss680" ? "PSS-790 Ready (Remapped for PSS-680)" : "PSS-680 Ready (Remapped for PSS-790)") :
                                      (isGmPassthrough ? "Windows GS Synth (Original GM Passthrough)" : $"General MIDI (Auto-Remapped for {modelTag})")));

                    var channelInfo = AnalyzeChannels(_loadedOriginalMidi, format);
                    SendToJs("playback_format_updated", new
                    {
                        format = fmtCode,
                        formatLabel = fmtLabel,
                        isGmPassthrough = isGmPassthrough,
                        channels = channelInfo
                    });
                }
            }

            private void ReapplyRemapping(bool preservePosition = false)
            {
                if (_loadedOriginalMidi == null) return;

                var wasPlaying = _player.IsPlaying;
                var pos = preservePosition ? _player.CurrentPosition : TimeSpan.Zero;

                _options.TargetModel = _activeModel;
                _loadedRemappedMidi = Remapper.Remap(_loadedOriginalMidi, _options);

                var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
                bool isNative = (format == MidiFormat.Pss790Native) || (format == MidiFormat.Pss680Native);

                if (isMsGs && !isNative)
                {
                    // Play unmodified General MIDI directly through Microsoft GS Wavetable Synth
                    _player.LoadMidi(_loadedOriginalMidi);
                }
                else
                {
                    _player.LoadMidi(_loadedRemappedMidi);
                }

            if (wasPlaying && preservePosition)
            {
                _player.Seek(pos);
                _player.Play();
            }
        }

        private List<object> AnalyzeChannels(MidiFile midi, MidiFormat format)
        {
            var channelPrograms = new Dictionary<int, byte>();
            var channelNotes = new HashSet<int>();
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
                    if (ce is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0)
                    {
                        channelNotes.Add(ch);
                    }
                }
            }

            // If General MIDI has active channels missing explicit ProgramChange, scan for XG SysEx and track-text heuristics
            if (format != MidiFormat.Pss790Native)
            {
                var tracksWithNotes = new Dictionary<int, TrackChunk>();
                foreach (var track in midi.GetTrackChunks())
                {
                    foreach (var evt in track.Events)
                    {
                        if (evt is NoteOnEvent n && n.Velocity > 0 && n.Channel != (FourBitNumber)9)
                        {
                            tracksWithNotes.TryAdd((int)n.Channel, track);
                        }
                        else if (evt is NormalSysExEvent sysex && sysex.Data.Length > 6)
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

            var list = new List<object>();
            for (int ch = 0; ch < 16; ch++)
            {
                bool hasNotes = channelNotes.Contains(ch);
                byte? prog = channelPrograms.TryGetValue(ch, out var p) ? p : null;
                string? inferredTag = null;
                if (!prog.HasValue && inferredPrograms.TryGetValue(ch, out var inf))
                {
                    prog = inf.Prog;
                    inferredTag = inf.InferredSource;
                }

                bool is680 = string.Equals(_activeModel, "pss680", StringComparison.OrdinalIgnoreCase);
                bool isDrum = (format == MidiFormat.Pss790Native || format == MidiFormat.Pss680Native) ? (ch == 15) : (ch == 9);

                bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(_player.CurrentOutputDeviceName);
                bool isGmPassthrough = isMsGs && (format != MidiFormat.Pss790Native && format != MidiFormat.Pss680Native);

                string role = isDrum
                    ? (is680 ? "Hand Percussion (Ch 16)" : "Fixed Rhythm (Ch 16)")
                    : $"Channel {ch + 1}";

                string detectedInstrument;
                int defaultPssVoice;
                string defaultPssVoiceName;

                if (format == MidiFormat.Pss680Native)
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
                else if (format == MidiFormat.Pss790Native)
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
                else // General MIDI
                {
                    if (isDrum)
                    {
                        detectedInstrument = "GM Standard Drum Kit";
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

                int? voiceOverride = _options.ChannelVoiceOverrides.TryGetValue(ch, out var vo) ? vo : null;

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
                    voiceOverride
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
            Task.Run(() =>
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
                            var report = MidiHealthInspector.Analyze(midi, fileName: Path.GetRelativePath(resolvedDir, file), model: _activeModel);
                            reports.Add(report);
                        }
                        catch (Exception)
                        {
                            // Skip corrupt files in audit
                        }
                        processed++;
                        if (processed % 10 == 0 || processed == total)
                        {
                            SendToJs("audit_progress", new { current = processed, total });
                        }
                    }

                    int polyLimit = _activeModel == "pss680" ? 12 : 28;
                    int totalWithFixes = reports.Count(r => r.AppliedAutoFixes.Count > 0);
                    int totalHighPolyphony = reports.Count(r => r.PeakPolyphony > polyLimit);
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

        private static string GetGmInstrumentName(byte program)
        {
            if (Constants.GmToPss790VoiceMap.TryGetValue(program, out var pssVoice) &&
                Constants.Pss790Voices.TryGetValue(pssVoice, out var voiceInfo))
            {
                return voiceInfo.Name;
            }
            return $"GM Instrument {program}";
        }

        private void SaveRemappedDialog()
        {
            if (_loadedRemappedMidi == null)
            {
                SendToJs("error", new { message = "No MIDI file is currently loaded." });
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                bool is680 = string.Equals(_activeModel, "pss680", StringComparison.OrdinalIgnoreCase);
                string suffix = is680 ? "_PSS680" : "_PSS790";
                var originalName = _currentFilePath != null ? Path.GetFileNameWithoutExtension(_currentFilePath) : "Remapped";
                if (!originalName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    originalName += suffix;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "MIDI File (*.mid)|*.mid",
                    InitialDirectory = AppPaths.GetModelMidiDir(_activeModel),
                    FileName = $"{originalName}.mid",
                    Title = $"Save Remapped {(is680 ? "PSS-680" : "PSS-790")} MIDI File"
                };

                if (dialog.ShowDialog() == true)
                {
                    Remapper.SaveRemapped(_loadedRemappedMidi, dialog.FileName, _activeModel);
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
            _batchCts?.Cancel();
            _batchCts = new CancellationTokenSource();

            var resolvedSource = Directory.Exists(sourceDir) ? sourceDir : AppPaths.GmMidiDir;
            var resolvedTarget790 = !string.IsNullOrEmpty(target790Dir) ? target790Dir : AppPaths.Pss790MidiDir;
            var resolvedTarget680 = !string.IsNullOrEmpty(target680Dir) ? target680Dir : AppPaths.Pss680MidiDir;

            Task.Run(async () =>
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
                        _batchCts.Token);

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

        private void StartBatchConversion(string sourceDir, string targetDir, RemapperOptions? options = null)
        {
            StartBatchConversion(sourceDir, targetDir, AppPaths.Pss680MidiDir, options, options, true, true);
        }

        private void HandleAnalyzeLibrarySync()
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
                    missingFiles = dual.Analysis790.MissingInTarget.Concat(dual.Analysis680.MissingInTarget).Distinct().Take(50).ToList(),
                    outdatedFiles = dual.Analysis790.OutdatedInTarget.Concat(dual.Analysis680.OutdatedInTarget).Distinct().Take(50).ToList(),
                    orphanFiles = dual.Analysis790.OrphanInTarget.Concat(dual.Analysis680.OrphanInTarget).Distinct().Take(50).ToList()
                });
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = $"Sync analysis error: {ex.Message}" });
            }
        }

        private void HandleExecuteLibrarySync(JsonElement root)
        {
            _syncCts?.Cancel();
            _syncCts = new CancellationTokenSource();

            var convertMissing = !root.TryGetProperty("convertMissing", out var cm) || cm.GetBoolean();
            var updateOutdated = !root.TryGetProperty("updateOutdated", out var uo) || uo.GetBoolean();
            var removeOrphans = !root.TryGetProperty("removeOrphans", out var ro) || ro.GetBoolean();
            var forceReconvertAll = root.TryGetProperty("forceReconvertAll", out var fr) && fr.GetBoolean();

            var syncOptions = new LibrarySyncOptions
            {
                ConvertMissing = convertMissing,
                UpdateOutdated = updateOutdated,
                RemoveOrphans = removeOrphans,
                ForceReconvertAll = forceReconvertAll,
                RemapperOptions = _options
            };

            Task.Run(async () =>
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
                    // Execute dual library synchronization for both PSS-790 and PSS-680
                    var result = await BatchConverter.ExecuteDualSyncAsync(
                        AppPaths.GmMidiDir,
                        AppPaths.Pss790MidiDir,
                        AppPaths.Pss680MidiDir,
                        syncOptions,
                        VoiceOverrideStore.Instance,
                        progress,
                        _syncCts.Token);

                    SendToJs("sync_completed", new
                    {
                        convertedCount = result.ConvertedCount,
                        updatedCount = result.UpdatedCount,
                        removedOrphansCount = result.RemovedOrphansCount,
                        totalOps = result.TotalOperations
                    });

                    SendLibraryUpdate();
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Sync execution error: {ex.Message}" });
                }
            });
        }

        private void SendToJs(string type, object data)
        {
            var app = Application.Current;
            if (app == null) return;

            var action = new Action(async () =>
            {
                try
                {
                    if (_webView.CoreWebView2 != null)
                    {
                        var payload = JsonSerializer.Serialize(new { type, data });
                        _webView.CoreWebView2.PostWebMessageAsJson(payload);

                        if (type == "initial_data")
                        {
                            try
                            {
                                await _webView.CoreWebView2.ExecuteScriptAsync($"if (window.handleHostMessage) {{ window.handleHostMessage({payload}); }}");
                            }
                            catch { }
                        }
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
            var list = _sysexManager.GetSysExLibrary();
            SendToJs("sysex_library_updated", new { dumps = list });
        }

        private void SendRecordingsLibraryUpdate()
        {
            var list = _recorder.GetRecordingsLibrary();
            SendToJs("recordings_library_updated", new { recordings = list });
        }

        public void Dispose()
        {
            _batchCts?.Cancel();
            _syncCts?.Cancel();
            _deviceMonitor.Dispose();
            _sysexManager.Dispose();
            _recorder.Dispose();
            _player.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
