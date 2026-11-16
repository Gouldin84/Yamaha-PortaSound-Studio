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
        private readonly RemapperOptions _options = new();
        private MidiFile? _loadedOriginalMidi;
        private MidiFile? _loadedRemappedMidi;
        private string? _currentFilePath;
        private CancellationTokenSource? _batchCts;

        public UiBridge(WebView2 webView)
        {
            _webView = webView;
            _player = new MidiPlayer();

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
                        var path = root.GetProperty("filePath").GetString();
                        if (!string.IsNullOrEmpty(path)) LoadFile(path);
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
                voices,
                library = libraryData,
                defaultDevice = devices.FirstOrDefault(),
                defaultBatchSource = AppPaths.GmMidiDir,
                defaultBatchTarget = AppPaths.Pss790MidiDir
            });

            if (devices.Count > 0)
            {
                _player.SetOutputDevice(devices[0]);
            }
        }

        private void SendLibraryUpdate()
        {
            var libraryData = ScanLibraryFiles();
            SendToJs("library_updated", libraryData);
        }

        private object ScanLibraryFiles()
        {
            AppPaths.EnsureDirectoriesExist();

            var gmFiles = Directory.GetFiles(AppPaths.GmMidiDir, "*.*")
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .Select(f => new
                {
                    fileName = Path.GetFileName(f),
                    fullPath = f,
                    format = "gm",
                    category = "GM Midi"
                })
                .OrderBy(f => f.fileName)
                .ToList();

            var pssFiles = Directory.GetFiles(AppPaths.Pss790MidiDir, "*.*")
                .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
                .Select(f => new
                {
                    fileName = Path.GetFileName(f),
                    fullPath = f,
                    format = "pss790",
                    category = "PSS-790 Midi"
                })
                .OrderBy(f => f.fileName)
                .ToList();

            return new
            {
                gmFiles,
                pssFiles,
                hasFiles = gmFiles.Count > 0 || pssFiles.Count > 0,
                baseDir = AppPaths.BaseMidiDir
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

        public void LoadFile(string pathOrName)
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

                var format = Remapper.DetectMidiFormat(_loadedOriginalMidi);
                ReapplyRemapping();

                var channelInfo = AnalyzeChannels(_loadedOriginalMidi);
                var duration = _loadedOriginalMidi.GetDuration<MetricTimeSpan>();

                SendToJs("file_loaded", new
                {
                    fileName = Path.GetFileName(resolvedPath),
                    filePath = resolvedPath,
                    format = format == MidiFormat.Pss790Native ? "pss790" : "gm",
                    formatLabel = format == MidiFormat.Pss790Native ? "PSS-790 Ready (Passthrough)" : "General MIDI (Auto-Remapped)",
                    durationSeconds = duration.TotalSeconds,
                    durationFormatted = $"{duration.Minutes:D2}:{duration.Seconds:D2}",
                    channels = channelInfo
                });
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

        private static List<object> AnalyzeChannels(MidiFile midi)
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
                    defaultPssVoiceName = Constants.Pss790Voices.GetValueOrDefault(defaultPssVoice)?.Name ?? "PIANO"
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
            _player.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
