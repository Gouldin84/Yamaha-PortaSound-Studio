using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
        private readonly RemapperOptions _options = new();
        private MidiFile? _loadedOriginalMidi;
        private MidiFile? _loadedRemappedMidi;
        private string? _currentFilePath;
        private CancellationTokenSource? _batchCts;

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
                    velocity = e.Velocity
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
                    velocity = e.Velocity
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
                    case "refresh_library":
                        SendLibraryUpdate();
                        break;
                    case "select_device":
                        var dev = root.GetProperty("deviceName").GetString();
                        if (dev != null) _player.SetOutputDevice(dev);
                        break;
                    case "load_file":
                    case "load_midi_metadata":
                        var path = root.TryGetProperty("filePath", out var fp) ? fp.GetString() : (root.TryGetProperty("path", out var p) ? p.GetString() : null);
                        if (!string.IsNullOrEmpty(path)) LoadFile(path, autoPlay: false);
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
                            ReapplyRemapping();
                            var channelInfo = AnalyzeChannels(_loadedOriginalMidi);
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
                            VoiceOverrideStore.Instance.SetOverride(_currentFilePath, ch, voice);
                        }
                        if (voice.HasValue)
                        {
                            _player.SendVoiceOverride(ch, voice.Value);
                        }
                        ReapplyRemapping();
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
                        ReapplyRemapping();
                        break;
                    case "save_remapped":
                        SaveRemappedDialog();
                        break;
                    case "start_batch":
                        var sourceDir = root.TryGetProperty("sourceDir", out var sProp) && !string.IsNullOrEmpty(sProp.GetString())
                            ? sProp.GetString()! : AppPaths.GmMidiDir;
                        var targetDir = root.TryGetProperty("targetDir", out var tProp) && !string.IsNullOrEmpty(tProp.GetString())
                            ? tProp.GetString()! : AppPaths.Pss790MidiDir;
                        StartBatchConversion(sourceDir, targetDir);
                        break;
                    case "cancel_batch":
                        _batchCts?.Cancel();
                        break;
                    case "select_input_device":
                        var inDev = root.GetProperty("deviceName").GetString();
                        _recorder.SetInputDevice(inDev);
                        break;
                    case "refresh_input_devices":
                        SendInputDevices();
                        break;
                    case "start_recording":
                        var bpm = root.TryGetProperty("bpm", out var bProp) ? bProp.GetInt32() : 120;
                        var countIn = root.TryGetProperty("countIn", out var cProp) && cProp.GetBoolean();
                        _ = _recorder.StartRecordingAsync(bpm, countIn);
                        break;
                    case "stop_recording":
                        _recorder.StopRecording();
                        var stats = _recorder.GetLastTakeStats();
                        SendToJs("recorder_take_ready", new
                        {
                            noteCount = stats.noteCount,
                            eventCount = stats.eventCount,
                            durationSeconds = stats.durationSeconds,
                            durationFormatted = TimeSpan.FromSeconds(stats.durationSeconds).ToString(@"mm\:ss")
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
                    case "save_take":
                        var takeFileName = root.TryGetProperty("fileName", out var fnProp) ? fnProp.GetString() ?? $"Take_{DateTime.Now:yyyyMMdd_HHmmss}.mid" : $"Take_{DateTime.Now:yyyyMMdd_HHmmss}.mid";
                        var isNativePss = root.TryGetProperty("isNativePss", out var nProp) ? nProp.GetBoolean() : (root.TryGetProperty("remapForPss790", out var rProp) && rProp.GetBoolean());
                        var savedPath = isNativePss 
                            ? _recorder.SaveNativeTake(takeFileName) 
                            : _recorder.ExportTakeToGeneralMidi(takeFileName);
                        SendLibraryUpdate();
                        SendToJs("take_saved", new
                        {
                            filePath = savedPath,
                            fileName = Path.GetFileName(savedPath),
                            isPss790 = isNativePss
                        });
                        break;
                    case "send_take_to_player":
                        var takeFile = _recorder.LastRecordedFile;
                        if (takeFile != null)
                        {
                            var title = root.TryGetProperty("title", out var tTitle) ? tTitle.GetString() ?? "Recorded Take" : "Recorded Take";
                            var cloned = takeFile.Clone();
                            var firstTrack = cloned.GetTrackChunks().FirstOrDefault();
                            if (firstTrack != null)
                            {
                                firstTrack.Events.Insert(0, new TextEvent(Remapper.Pss790MarkerText) { DeltaTime = 0 });
                            }
                            LoadMidiObject(cloned, title);
                            SendToJs("switch_tab", new { tab = "player" });
                        }
                        break;
                    case "set_midi_thru":
                        var thru = root.GetProperty("enabled").GetBoolean();
                        _recorder.MidiThruEnabled = thru;
                        break;
                }
            }
            catch (Exception ex)
            {
                SendToJs("error", new { message = ex.Message });
            }
        }

        private void HandleGetInitialData()
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

            var libraryData = ScanLibraryFiles();

            SendToJs("initial_data", new
            {
                devices,
                inputDevices,
                voices,
                library = libraryData,
                defaultDevice = devices.FirstOrDefault(),
                defaultInputDevice = inputDevices.FirstOrDefault(),
                defaultBatchSource = AppPaths.GmMidiDir,
                defaultBatchTarget = AppPaths.Pss790MidiDir,
                midiThru = _recorder.MidiThruEnabled,
                recorderBpm = _recorder.Bpm
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

            var allFlat = new List<object>();

            var gmTree = BuildCategoryTree(AppPaths.GmMidiDir, "GM Midi", "gm", allFlat);
            var pssTree = BuildCategoryTree(AppPaths.Pss790MidiDir, "PSS-790 Midi", "pss790", allFlat);

            var gmFiles = allFlat.Where(f => ((dynamic)f).format == "gm").ToList();
            var pssFiles = allFlat.Where(f => ((dynamic)f).format == "pss790").ToList();

            return new
            {
                gmTree,
                pssTree,
                gmFiles,
                pssFiles,
                flatFiles = allFlat,
                hasFiles = allFlat.Count > 0,
                baseDir = AppPaths.BaseMidiDir
            };
        }

        private static object BuildCategoryTree(string rootDir, string categoryName, string format, List<object> flatCollector)
        {
            if (!Directory.Exists(rootDir))
            {
                return new { name = categoryName, format, folders = new List<object>(), files = new List<object>(), count = 0 };
            }

            var files = Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var folderGroups = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
            var rootFiles = new List<object>();

            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(rootDir, file);
                var dirName = Path.GetDirectoryName(rel) ?? "";
                var fileName = Path.GetFileName(file);

                var fileItem = new
                {
                    fileName,
                    fullPath = file,
                    relativePath = rel,
                    subFolder = dirName,
                    category = categoryName,
                    format
                };

                flatCollector.Add(fileItem);

                if (string.IsNullOrEmpty(dirName))
                {
                    rootFiles.Add(fileItem);
                }
                else
                {
                    if (!folderGroups.TryGetValue(dirName, out var list))
                    {
                        list = new List<object>();
                        folderGroups[dirName] = list;
                    }
                    list.Add(fileItem);
                }
            }

            var foldersList = folderGroups
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new
                {
                    name = kvp.Key,
                    files = kvp.Value.OrderBy(f => ((dynamic)f).fileName).ToList(),
                    count = kvp.Value.Count
                })
                .ToList();

            return new
            {
                name = categoryName,
                format,
                folders = foldersList,
                files = rootFiles.OrderBy(f => ((dynamic)f).fileName).ToList(),
                count = files.Count
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
                _currentFilePath = virtualPath ?? title;
                _loadedOriginalMidi = midi;

                var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                ReapplyRemapping();

                var channelInfo = AnalyzeChannels(_loadedOriginalMidi);
                var duration = _loadedOriginalMidi.GetDuration<MetricTimeSpan>();

                SendToJs("file_loaded", new
                {
                    fileName = title,
                    filePath = virtualPath ?? title,
                    relativeDir = "",
                    format = format == MidiFormat.Pss790Native ? "pss790" : "gm",
                    formatLabel = format == MidiFormat.Pss790Native ? "PSS-790 Ready (Passthrough)" : "General MIDI (Auto-Remapped)",
                    durationSeconds = duration.TotalSeconds,
                    durationFormatted = $"{duration.Minutes:D2}:{duration.Seconds:D2}",
                    channels = channelInfo,
                    hasCustomOverrides = false
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
                    var pssCandidate = Path.Combine(AppPaths.Pss790MidiDir, pathOrName);

                    if (File.Exists(gmCandidate)) resolvedPath = gmCandidate;
                    else if (File.Exists(pssCandidate)) resolvedPath = pssCandidate;
                }

                if (!File.Exists(resolvedPath))
                {
                    SendToJs("error", new { message = $"File not found: {pathOrName}" });
                    return;
                }

                _currentFilePath = resolvedPath;
                _loadedOriginalMidi = MidiFile.Read(resolvedPath);

                // Restore saved per-song voice overrides from VoiceOverrideStore!
                _options.ChannelVoiceOverrides.Clear();
                var savedOverrides = VoiceOverrideStore.Instance.GetOverrides(resolvedPath);
                foreach (var (ch, voice) in savedOverrides)
                {
                    _options.ChannelVoiceOverrides[ch] = voice;
                }

                var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                ReapplyRemapping();

                var channelInfo = AnalyzeChannels(_loadedOriginalMidi);
                var duration = _loadedOriginalMidi.GetDuration<MetricTimeSpan>();

                SendToJs("file_loaded", new
                {
                    fileName = Path.GetFileName(resolvedPath),
                    filePath = resolvedPath,
                    relativeDir = Path.GetDirectoryName(AppPaths.GetRelativeLibraryPath(resolvedPath)) ?? "",
                    format = format == MidiFormat.Pss790Native ? "pss790" : "gm",
                    formatLabel = format == MidiFormat.Pss790Native ? "PSS-790 Ready (Passthrough)" : "General MIDI (Auto-Remapped)",
                    durationSeconds = duration.TotalSeconds,
                    durationFormatted = $"{duration.Minutes:D2}:{duration.Seconds:D2}",
                    channels = channelInfo,
                    hasCustomOverrides = VoiceOverrideStore.Instance.HasOverrides(resolvedPath)
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

        private void ReapplyRemapping()
        {
            if (_loadedOriginalMidi == null) return;

            var wasPlaying = _player.IsPlaying;
            var pos = _player.CurrentPosition;

            _loadedRemappedMidi = Remapper.Remap(_loadedOriginalMidi, _options);
            _player.LoadMidi(_loadedRemappedMidi);

            if (wasPlaying)
            {
                _player.Seek(pos);
                _player.Play();
            }
        }

        private List<object> AnalyzeChannels(MidiFile midi)
        {
            var channelPrograms = new Dictionary<int, byte>();
            var channelNotes = new HashSet<int>();

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

            var list = new List<object>();
            for (int ch = 0; ch < 16; ch++)
            {
                bool hasNotes = channelNotes.Contains(ch);
                byte? prog = channelPrograms.TryGetValue(ch, out var p) ? p : null;
                bool isDrum = ch == 9; // GM Drum channel

                string role = isDrum ? "Drums (Remapped to Ch 16)" : (ch == 15 ? "Channel 16" : $"Channel {ch + 1}");
                string detectedInstrument = isDrum
                    ? "GM Standard Drum Kit"
                    : (prog.HasValue ? GetGmInstrumentName(prog.Value) : "Default Piano");

                int defaultPssVoice = isDrum ? 96 : (prog.HasValue ? Constants.GmToPss790VoiceMap.GetValueOrDefault(prog.Value, 0) : 0);
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
                    defaultPssVoiceName = Constants.Pss790Voices.GetValueOrDefault(defaultPssVoice)?.Name ?? "PIANO",
                    voiceOverride
                });
            }

            return list;
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
                var originalName = _currentFilePath != null ? Path.GetFileNameWithoutExtension(_currentFilePath) : "Remapped";
                if (!originalName.EndsWith("_PSS790", StringComparison.OrdinalIgnoreCase))
                {
                    originalName += "_PSS790";
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "MIDI File (*.mid)|*.mid",
                    InitialDirectory = AppPaths.Pss790MidiDir,
                    FileName = $"{originalName}.mid",
                    Title = "Save Remapped PSS-790 MIDI File"
                };

                if (dialog.ShowDialog() == true)
                {
                    Remapper.SaveRemapped(_loadedRemappedMidi, dialog.FileName);
                    SendToJs("notification", new { message = $"Saved to: {Path.GetFileName(dialog.FileName)}" });
                    SendLibraryUpdate();
                }
            });
        }

        private void StartBatchConversion(string sourceDir, string targetDir)
        {
            _batchCts?.Cancel();
            _batchCts = new CancellationTokenSource();

            var resolvedSource = Directory.Exists(sourceDir) ? sourceDir : AppPaths.GmMidiDir;
            var resolvedTarget = Directory.Exists(targetDir) ? targetDir : AppPaths.Pss790MidiDir;

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
                    int count = await BatchConverter.ConvertDirectoryAsync(resolvedSource, resolvedTarget, _options, progress, _batchCts.Token);
                    SendToJs("batch_complete", new
                    {
                        convertedCount = count,
                        targetDirectory = resolvedTarget
                    });
                    SendLibraryUpdate();
                }
                catch (Exception ex)
                {
                    SendToJs("error", new { message = $"Batch error: {ex.Message}" });
                }
            });
        }

        private void SendToJs(string type, object data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_webView.CoreWebView2 != null)
                {
                    var payload = JsonSerializer.Serialize(new { type, data });
                    _webView.CoreWebView2.PostWebMessageAsJson(payload);
                }
            });
        }

        public void Dispose()
        {
            _batchCts?.Cancel();
            _recorder.Dispose();
            _player.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
