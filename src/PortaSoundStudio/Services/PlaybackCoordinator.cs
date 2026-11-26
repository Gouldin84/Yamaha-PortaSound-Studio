using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Microsoft.Win32;
using PSS790Player.Core;
using PSS790Player.Models;

namespace PSS790Player.Services
{
    /// <summary>
    /// Coordinates song loading, remapping, transport control, mixer parameters,
    /// per-song voice overrides, jukebox navigation, and real-time VU/LED meter batching.
    /// </summary>
    public sealed class PlaybackCoordinator : IDisposable
    {
        private readonly AppStateStore _state;
        private readonly HardwareDeviceService _devices;
        private readonly LibraryService _library;

        private readonly object _stateLock = new();
        private RemapperOptions _options = new() { EnablePolyphonyGuard = true };
        private MidiFile? _loadedOriginalMidi;
        private MidiFile? _loadedRemappedMidi;
        private string? _currentFilePath;
        private string? _currentDisplayName;
        private bool _isGmPassthrough;
        private Dictionary<int, int?>? _preVectorVoiceOverrides;

        private readonly bool[] _pendingChannelActive = new bool[16];
        private readonly byte[] _pendingChannelVel = new byte[16];
        private readonly object _meterLock = new();
        private readonly System.Threading.Timer _meterTimer;

        private CancellationTokenSource? _loadCts;
        private long _loadSequence;
        private volatile bool _userRequestedStop;
        private volatile bool _isDisposed;

        public PlaybackCoordinator(
            AppStateStore state,
            HardwareDeviceService devices,
            LibraryService library)
        {
            _state = state;
            _devices = devices;
            _library = library;

            var player = _devices.Player;
            player.PlaybackStateChanged += OnPlayerStateChanged;
            player.PlaybackFinished += OnPlayerPlaybackFinished;
            player.ChannelActivity += OnPlayerChannelActivity;
            player.OutputDeviceDisconnected += OnOutputDeviceDisconnected;

            _devices.HardwareModelAutoDetected += OnHardwareModelAutoDetected;
            _devices.OutputDeviceChanged += OnOutputDeviceChanged;
            _library.SongPathRelocated += OnSongPathRelocated;
            _library.SongPathDeleted += OnSongPathDeleted;

            _meterTimer = new System.Threading.Timer(_ => FlushChannelActivityBatch(), null, 33, 33);
        }

        public string? CurrentFilePath
        {
            get { lock (_stateLock) return _currentFilePath; }
        }

        public async Task LoadFileAsync(string filePath, bool autoPlay = false)
        {
            if (_isDisposed || string.IsNullOrWhiteSpace(filePath)) return;
            if (!File.Exists(filePath))
            {
                _state.AddToast($"File not found: {Path.GetFileName(filePath)}", ToastSeverity.Error);
                return;
            }

            long seq = Interlocked.Increment(ref _loadSequence);
            _userRequestedStop = false;

            CancellationToken token;
            lock (_stateLock)
            {
                try { _loadCts?.Cancel(); } catch { }
                try { _loadCts?.Dispose(); } catch { }
                _loadCts = new CancellationTokenSource();
                token = _loadCts.Token;
            }

            _state.Mutate(s =>
            {
                s.SelectedSongPath = filePath;
                if (autoPlay) s.PlayingSongPath = filePath;
                s.IsLoadingSong = true;
            }, StateScope.Transport | StateScope.Library);

            // Stop active audio immediately on the Transport Actor before parsing/remapping
            await _devices.Player.StopAsync().ConfigureAwait(false);
            if (token.IsCancellationRequested || seq != Interlocked.Read(ref _loadSequence)) return;

            try
            {
                var (loadedState, playbackMidi, originalMidi, remappedMidi) = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var orig = MidiFile.Read(filePath, Constants.DefaultReadingSettings);
                    token.ThrowIfCancellationRequested();

                    var activeModel = _state.ActiveModel;
                    var format = Remapper.DetectMidiFormat(orig);
                    bool isNative790 = format == MidiFormat.Pss790Native;
                    bool isNative680 = format == MidiFormat.Pss680Native;
                    bool isGmFile = format == MidiFormat.GeneralMidi;

                    var overrides = VoiceOverrideStore.Instance.GetOverrides(filePath, activeModel);
                    var opts = BuildCleanOptionsLocked(activeModel, overrides);

                    var transformed = PreparePlaybackMidi(orig, format, activeModel, opts, _isGmPassthrough, isMsGs: IsMsGsOutput());
                    token.ThrowIfCancellationRequested();

                    var channels = ChannelStateExtractor.Analyze(
                        orig, transformed, format, activeModel, _isGmPassthrough,
                        opts.ChannelVoiceOverrides, opts.ChannelOctaveShifts,
                        GetPlayerMixerSnapshot);

                    var health = MapHealth(MidiHealthInspector.Analyze(orig, transformed, Path.GetFileName(filePath), activeModel));
                    double durationSec = transformed.GetDuration<MetricTimeSpan>().TotalSeconds;

                    string? optPath = isGmFile ? AppPaths.FindMatchingOptimizedFile(filePath, activeModel) : null;
                    string? gmPath = !isGmFile ? AppPaths.FindMatchingLinkedFile(filePath, activeModel) : null;

                    var dto = new LoadedSongState
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        IsPss790 = isNative790,
                        IsPss680 = isNative680,
                        IsGmFile = isGmFile,
                        IsGmPassthrough = _isGmPassthrough,
                        FormatTag = FormatTagLabel(format),
                        ActiveModel = activeModel,
                        CanSaveRemapped = isGmFile && !_isGmPassthrough,
                        DurationSeconds = durationSec,
                        DurationFormatted = FormatDuration(durationSec),
                        MasterTranspose = opts.MasterTranspose,
                        PitchBendMode = opts.PitchBendMode.ToString(),
                        PolyphonyGuard = opts.EnablePolyphonyGuard,
                        IsOptimizedAvailable = optPath != null,
                        OptimizedFilePath = optPath,
                        LinkedGmPath = gmPath,
                        Channels = channels,
                        Health = health,
                        ChannelPrograms = channels.Where(c => c.HasNotes).ToDictionary(c => c.Channel, c => c.PssVoiceName)
                    };

                    return (dto, transformed, orig, isGmFile ? transformed : null);
                }, token).ConfigureAwait(false);

                if (token.IsCancellationRequested || seq != Interlocked.Read(ref _loadSequence)) return;

                lock (_stateLock)
                {
                    _currentFilePath = filePath;
                    _currentDisplayName = loadedState.FileName;
                    _loadedOriginalMidi = originalMidi;
                    _loadedRemappedMidi = remappedMidi;
                }

                await _devices.Player.LoadMidiAsync(playbackMidi).ConfigureAwait(false);
                if (token.IsCancellationRequested || seq != Interlocked.Read(ref _loadSequence)) return;

                _state.Mutate(s =>
                {
                    s.LoadedSong = loadedState;
                    s.DurationSeconds = loadedState.DurationSeconds;
                    s.CurrentSeconds = 0;
                    s.IsLoadingSong = false;
                }, StateScope.Transport | StateScope.Channels | StateScope.Library | StateScope.Shell);

                if (autoPlay && !_userRequestedStop && seq == Interlocked.Read(ref _loadSequence))
                {
                    if (_devices.Player.CurrentOutputDeviceName == null)
                    {
                        _state.AddToast("No MIDI output device selected — select a synthesizer in the top bar.", ToastSeverity.Warning);
                        return;
                    }
                    await _devices.Player.PlayAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (seq == Interlocked.Read(ref _loadSequence))
                {
                    _state.Mutate(s => s.IsLoadingSong = false);
                    _state.AddToast($"Failed to load MIDI: {ex.Message}", ToastSeverity.Error);
                }
            }
        }

        public void LoadMidiObject(MidiFile midi, string title)
        {
            if (_isDisposed || midi == null) return;
            long seq = Interlocked.Increment(ref _loadSequence);
            _userRequestedStop = false;

            var cloned = midi.Clone();
            var activeModel = _state.ActiveModel;
            var format = Remapper.DetectMidiFormat(cloned);
            var opts = BuildCleanOptionsLocked(activeModel, new Dictionary<int, int?>());
            var transformed = PreparePlaybackMidi(cloned, format, activeModel, opts, _isGmPassthrough, isMsGs: IsMsGsOutput());
            var channels = ChannelStateExtractor.Analyze(cloned, transformed, format, activeModel, _isGmPassthrough,
                opts.ChannelVoiceOverrides, opts.ChannelOctaveShifts, GetPlayerMixerSnapshot);
            var health = MapHealth(MidiHealthInspector.Analyze(cloned, transformed, title, activeModel));
            double durationSec = transformed.GetDuration<MetricTimeSpan>().TotalSeconds;

            lock (_stateLock)
            {
                _currentFilePath = null;
                _currentDisplayName = title;
                _loadedOriginalMidi = cloned;
                _loadedRemappedMidi = transformed;
            }

            _devices.Player.LoadMidi(transformed);

            if (seq != Interlocked.Read(ref _loadSequence)) return;
            _state.Mutate(s =>
            {
                s.PlayingSongPath = null;
                s.DurationSeconds = durationSec;
                s.CurrentSeconds = 0;
                s.LoadedSong = new LoadedSongState
                {
                    FilePath = string.Empty,
                    FileName = title,
                    IsPss790 = format == MidiFormat.Pss790Native,
                    IsPss680 = format == MidiFormat.Pss680Native,
                    IsGmFile = format == MidiFormat.GeneralMidi,
                    IsGmPassthrough = _isGmPassthrough,
                    FormatTag = FormatTagLabel(format),
                    ActiveModel = activeModel,
                    CanSaveRemapped = !_isGmPassthrough,
                    DurationSeconds = durationSec,
                    DurationFormatted = FormatDuration(durationSec),
                    MasterTranspose = opts.MasterTranspose,
                    PitchBendMode = opts.PitchBendMode.ToString(),
                    PolyphonyGuard = opts.EnablePolyphonyGuard,
                    Channels = channels,
                    Health = health,
                    ChannelPrograms = channels.Where(c => c.HasNotes).ToDictionary(c => c.Channel, c => c.PssVoiceName)
                };
            });
        }

        public void TogglePlayPause()
        {
            if (_devices.Player.IsPlaying)
            {
                _devices.Player.Pause();
                return;
            }

            if (_devices.Player.CurrentOutputDeviceName == null)
            {
                _state.AddToast("Select a MIDI output device in the header first.", ToastSeverity.Warning);
                return;
            }

            _userRequestedStop = false;
            _devices.Player.Play();
        }

        public void Play()
        {
            if (_devices.Player.CurrentOutputDeviceName == null)
            {
                _state.AddToast("Select a MIDI output device in the header first.", ToastSeverity.Warning);
                return;
            }
            _userRequestedStop = false;
            _ = _devices.Player.PlayAsync();
        }

        public void Pause() => _ = _devices.Player.PauseAsync();

        public void Stop()
        {
            _userRequestedStop = true;
            _ = _devices.Player.StopAsync();
            ResetChannelActivity();
            _state.Mutate(s =>
            {
                s.IsPlaying = false;
                s.IsPaused = false;
                s.CurrentSeconds = 0;
            }, StateScope.Transport);
        }

        public void PanicStop() => Stop();

        public void Seek(double seconds)
        {
            double clamped = Math.Max(0, seconds);
            _state.Mutate(s => s.CurrentSeconds = clamped, StateScope.Transport);
            _ = _devices.Player.SeekAsync(TimeSpan.FromSeconds(clamped));
        }

        public void SetSpeed(double speed)
        {
            double clamped = Math.Clamp(speed, 0.25, 2.5);
            _devices.Player.SetSpeed(clamped);
            _state.Mutate(s => s.Speed = clamped, StateScope.Transport);
        }

        public void SetLoop(bool loop)
        {
            _devices.Player.SetLoop(loop);
            _state.Mutate(s => s.LoopEnabled = loop, StateScope.Transport);
        }

        public void SetGmPassthrough(bool enabled)
        {
            _isGmPassthrough = enabled;
            _state.Mutate(s => s.GmPassthrough = enabled);
            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void SetActiveModel(string model)
        {
            string normalized = string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase) ? "pss680" : "pss790";
            _devices.Player.ActiveModel = normalized;

            if (normalized == "pss680" && _state.VectorQuadPlaybackEnabled)
            {
                SetVectorPlaybackMode(false, 0, 34, 16, 56, 64, 64, 64, 64);
            }

            string? pathSnap;
            lock (_stateLock)
            {
                pathSnap = _currentFilePath;
                _options.ChannelVoiceOverrides.Clear();
                if (pathSnap != null)
                {
                    foreach (var (ch, v) in VoiceOverrideStore.Instance.GetOverrides(pathSnap, normalized))
                    {
                        _options.ChannelVoiceOverrides[ch] = v;
                    }
                }
            }

            _state.Mutate(s =>
            {
                s.ActiveModel = normalized;
                s.StudioGuideModelTab = normalized;
                s.SysExGuideModelTab = normalized;
            });

            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void SetMasterTranspose(int transpose)
        {
            int clamped = Math.Clamp(transpose, -12, 12);
            lock (_stateLock) { _options.MasterTranspose = clamped; }
            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void SetChannelOctave(int channel, int octaveShift)
        {
            int clamped = Math.Clamp(octaveShift, -24, 24);
            lock (_stateLock) { _options.ChannelOctaveShifts[channel] = clamped; }
            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void SetPitchBendMode(string? mode)
        {
            PitchBendScaleMode resolved = mode?.ToLowerInvariant() switch
            {
                "preserve1to1" => PitchBendScaleMode.Preserve1to1,
                "divideby6" => PitchBendScaleMode.DivideBy6,
                "disable" => PitchBendScaleMode.Disable,
                _ => PitchBendScaleMode.Auto
            };
            lock (_stateLock) { _options.PitchBendMode = resolved; }
            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void SetPolyphonyGuard(bool enabled)
        {
            lock (_stateLock) { _options.EnablePolyphonyGuard = enabled; }
            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void SetChannelVoiceOverride(int channel, int? voiceNumber)
        {
            string model = _state.ActiveModel;
            string? pathSnap;
            lock (_stateLock)
            {
                pathSnap = _currentFilePath;
                if (voiceNumber.HasValue)
                    _options.ChannelVoiceOverrides[channel] = voiceNumber.Value;
                else
                    _options.ChannelVoiceOverrides.Remove(channel);
            }

            if (pathSnap != null)
            {
                VoiceOverrideStore.Instance.SetOverride(pathSnap, channel, voiceNumber, model);
            }

            if (voiceNumber.HasValue)
            {
                _devices.Player.SendVoiceOverride(channel, voiceNumber.Value, model);
            }

            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void ResetAllVoiceOverrides()
        {
            string model = _state.ActiveModel;
            string? pathSnap;
            lock (_stateLock)
            {
                pathSnap = _currentFilePath;
                _options.ChannelVoiceOverrides.Clear();
            }

            if (pathSnap != null)
            {
                VoiceOverrideStore.Instance.ResetOverrides(pathSnap, model);
            }
            _ = ReapplyRemappingAsync(preservePosition: true);
        }

        public void ApplyFmVoiceToChannel(int channel, int bankNumber, byte[] sysExMessage, string voiceName)
        {
            int bank = Math.Clamp(bankNumber, 0, 4);
            int voiceNumber = 100 + bank;
            _devices.Player.SendSysEx(sysExMessage);

            string? pathSnap;
            lock (_stateLock)
            {
                pathSnap = _currentFilePath;
                _options.ChannelVoiceOverrides[channel] = voiceNumber;
            }
            if (pathSnap != null)
            {
                VoiceOverrideStore.Instance.SetOverride(pathSnap, channel, voiceNumber, "pss680");
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(35).ConfigureAwait(false);
                _devices.Player.SendVoiceOverride(channel, voiceNumber, "pss680");
                await ReapplyRemappingAsync(preservePosition: true).ConfigureAwait(false);
            });
            _state.AddToast($"Assigned FM patch '{voiceName}' to Channel {channel + 1} (Bank {bank + 1}).", ToastSeverity.Success);
        }

        public void SetChannelVolume(int channel, byte volume)
        {
            _devices.Player.SetChannelVolume(channel, volume);
            _state.Mutate(s =>
            {
                var ch = s.LoadedSong?.Channels.FirstOrDefault(c => c.Channel == channel);
                if (ch != null) ch.Volume = volume;
            });
        }

        public void SetChannelMute(int channel, bool mute)
        {
            _devices.Player.SetChannelMute(channel, mute);
            _state.Mutate(s =>
            {
                var ch = s.LoadedSong?.Channels.FirstOrDefault(c => c.Channel == channel);
                if (ch != null) ch.IsMuted = mute;
            });
        }

        public void SetChannelSolo(int channel, bool solo)
        {
            _devices.Player.SetChannelSolo(channel, solo);
            _state.Mutate(s =>
            {
                var ch = s.LoadedSong?.Channels.FirstOrDefault(c => c.Channel == channel);
                if (ch != null) ch.IsSolo = solo;
            });
        }

        public void ClearAllSolo()
        {
            _devices.Player.ClearAllSolo();
            _state.Mutate(s =>
            {
                if (s.LoadedSong != null)
                {
                    foreach (var ch in s.LoadedSong.Channels) ch.IsSolo = false;
                }
            });
        }

        public void SetVectorPlaybackMode(bool enabled, int voiceA, int voiceB, int voiceC, int voiceD, byte volA, byte volB, byte volC, byte volD)
        {
            _devices.Player.VectorQuadPlaybackEnabled = enabled;
            bool hasSong;

            if (enabled)
            {
                lock (_stateLock)
                {
                    if (_preVectorVoiceOverrides == null)
                    {
                        _preVectorVoiceOverrides = new Dictionary<int, int?>();
                        for (int c = 0; c < 4; c++)
                        {
                            _preVectorVoiceOverrides[c] = _options.ChannelVoiceOverrides.TryGetValue(c, out var ov) ? ov : null;
                        }
                    }
                    _options.ChannelVoiceOverrides[0] = voiceA;
                    _options.ChannelVoiceOverrides[1] = voiceB;
                    _options.ChannelVoiceOverrides[2] = voiceC;
                    _options.ChannelVoiceOverrides[3] = voiceD;
                    hasSong = _loadedOriginalMidi != null;
                }

                _devices.Player.SendVoiceOverride(0, voiceA);
                _devices.Player.SendVoiceOverride(1, voiceB);
                _devices.Player.SendVoiceOverride(2, voiceC);
                _devices.Player.SendVoiceOverride(3, voiceD);

                _devices.Player.SetChannelVolume(0, (byte)Math.Clamp((int)Math.Round(volA * 100.0 / 127.0), 0, 100));
                _devices.Player.SetChannelVolume(1, (byte)Math.Clamp((int)Math.Round(volB * 100.0 / 127.0), 0, 100));
                _devices.Player.SetChannelVolume(2, (byte)Math.Clamp((int)Math.Round(volC * 100.0 / 127.0), 0, 100));
                _devices.Player.SetChannelVolume(3, (byte)Math.Clamp((int)Math.Round(volD * 100.0 / 127.0), 0, 100));
            }
            else
            {
                lock (_stateLock)
                {
                    if (_preVectorVoiceOverrides != null)
                    {
                        foreach (var (c, prevOv) in _preVectorVoiceOverrides)
                        {
                            if (prevOv.HasValue) _options.ChannelVoiceOverrides[c] = prevOv;
                            else _options.ChannelVoiceOverrides.Remove(c);
                        }
                        _preVectorVoiceOverrides = null;
                    }
                    else
                    {
                        for (int c = 0; c < 4; c++) _options.ChannelVoiceOverrides.Remove(c);
                    }
                    hasSong = _loadedOriginalMidi != null;
                }

                for (int c = 0; c < 4; c++) _devices.Player.SetChannelVolume(c, 100);
            }

            _state.Mutate(s => s.VectorQuadPlaybackEnabled = enabled);
            if (hasSong)
            {
                _ = ReapplyRemappingAsync(preservePosition: true);
            }
        }

        public void BrowseAndLoadMidiWithDialog()
        {
            string? picked = null;
            var app = Application.Current;
            void ShowDlg()
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "MIDI Files (*.mid;*.midi)|*.mid;*.midi|All Files (*.*)|*.*",
                    InitialDirectory = Directory.Exists(AppPaths.GmMidiDir) ? AppPaths.GmMidiDir : AppPaths.BaseMidiDir,
                    Title = "Open MIDI Song"
                };
                if (dlg.ShowDialog() == true)
                {
                    picked = dlg.FileName;
                }
            }

            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(ShowDlg);
            else
                ShowDlg();

            if (!string.IsNullOrEmpty(picked))
            {
                _ = LoadFileAsync(picked, autoPlay: true);
            }
        }

        public void SaveRemappedWithDialog()
        {
            MidiFile? remappedSnap;
            string defaultName;
            string targetModel = _state.ActiveModel;

            lock (_stateLock)
            {
                remappedSnap = _loadedRemappedMidi?.Clone();
                defaultName = string.IsNullOrWhiteSpace(_currentDisplayName)
                    ? "Remapped_Track.mid"
                    : Path.GetFileNameWithoutExtension(_currentDisplayName) + $"_{(targetModel == "pss680" ? "PSS680" : "PSS790")}.mid";
            }

            if (remappedSnap == null)
            {
                _state.AddToast("Load and optimize a song first before saving.", ToastSeverity.Warning);
                return;
            }

            string targetDir = AppPaths.GetModelMidiDir(targetModel);
            AppPaths.EnsureDirectoriesExist();

            var app = Application.Current;
            void ShowSaveDialog()
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "MIDI Files (*.mid)|*.mid",
                    InitialDirectory = targetDir,
                    FileName = defaultName,
                    Title = $"Save Remapped MIDI for {(targetModel == "pss680" ? "PSS-680" : "PSS-790")}"
                };

                if (dlg.ShowDialog() == true)
                {
                    Remapper.SaveRemapped(remappedSnap, dlg.FileName, targetModel);
                    _library.RefreshLibrary();
                    _state.AddToast($"Saved remapped file: {Path.GetFileName(dlg.FileName)}", ToastSeverity.Success);
                }
            }

            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(ShowSaveDialog);
            else
                ShowSaveDialog();
        }

        public async Task PlayNextSongAsync()
        {
            var songs = GetFilteredSongList();
            if (songs.Count == 0) return;

            var current = _state.PlayingSongPath ?? _state.SelectedSongPath;
            int idx = songs.FindIndex(f => string.Equals(f.FullPath, current, StringComparison.OrdinalIgnoreCase));

            int nextIdx;
            if (_state.JukeboxShuffle && songs.Count > 1)
            {
                do { nextIdx = Random.Shared.Next(songs.Count); } while (nextIdx == idx);
            }
            else
            {
                nextIdx = idx >= 0 && idx + 1 < songs.Count ? idx + 1 : 0;
            }

            await LoadFileAsync(songs[nextIdx].FullPath, autoPlay: true).ConfigureAwait(false);
        }

        public async Task PlayPreviousSongAsync()
        {
            var songs = GetFilteredSongList();
            if (songs.Count == 0) return;

            var current = _state.PlayingSongPath ?? _state.SelectedSongPath;
            int idx = songs.FindIndex(f => string.Equals(f.FullPath, current, StringComparison.OrdinalIgnoreCase));
            int prevIdx = idx > 0 ? idx - 1 : songs.Count - 1;

            await LoadFileAsync(songs[prevIdx].FullPath, autoPlay: true).ConfigureAwait(false);
        }

        public List<MidiFileItem> GetFilteredSongList()
        {
            string hiddenFormat = string.Equals(_state.ActiveModel, "pss680", StringComparison.OrdinalIgnoreCase)
                ? "pss790"
                : "pss680";
            var visible = _state.Library.AllFiles.Where(f => !string.Equals(f.Format, hiddenFormat, StringComparison.OrdinalIgnoreCase));

            IEnumerable<MidiFileItem> query = _state.LibraryFilter switch
            {
                "gm" => visible.Where(f => f.Format == "gm"),
                "pss790" => visible.Where(f => f.Format == "pss790"),
                "pss680" => visible.Where(f => f.Format == "pss680"),
                "favorites" => visible.Where(f => f.IsFavorite || _state.Favorites.Contains(f.FullPath)),
                _ => visible
            };

            if (!string.IsNullOrWhiteSpace(_state.SearchQuery))
            {
                var q = _state.SearchQuery.Trim();
                query = query.Where(f => f.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                         f.RelativePath.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }

        public async Task ReapplyRemappingAsync(bool preservePosition = true)
        {
            if (_isDisposed) return;

            MidiFile? origSnap;
            string? pathSnap;
            string? displaySnap;
            bool passthrough = _isGmPassthrough;
            string activeModel = _state.ActiveModel;

            lock (_stateLock)
            {
                origSnap = _loadedOriginalMidi;
                pathSnap = _currentFilePath;
                displaySnap = _currentDisplayName;
            }
            if (origSnap == null) return;

            bool wasPlaying = _devices.Player.IsPlaying;
            bool wasPaused = _devices.Player.IsPaused;
            TimeSpan savedPos = preservePosition ? _devices.Player.CurrentPosition : TimeSpan.Zero;

            var format = Remapper.DetectMidiFormat(origSnap);
            var opts = BuildCleanOptionsLocked(activeModel, null);
            var transformed = PreparePlaybackMidi(origSnap, format, activeModel, opts, passthrough, isMsGs: IsMsGsOutput());
            var channels = ChannelStateExtractor.Analyze(origSnap, transformed, format, activeModel, passthrough,
                opts.ChannelVoiceOverrides, opts.ChannelOctaveShifts, GetPlayerMixerSnapshot);
            var health = MapHealth(MidiHealthInspector.Analyze(origSnap, transformed, displaySnap ?? "MIDI Track", activeModel));
            double durationSec = transformed.GetDuration<MetricTimeSpan>().TotalSeconds;

            lock (_stateLock)
            {
                _loadedRemappedMidi = format == MidiFormat.GeneralMidi || !passthrough ? transformed : null;
            }

            await _devices.Player.LoadMidiAsync(transformed).ConfigureAwait(false);
            if (preservePosition && savedPos > TimeSpan.Zero && savedPos.TotalSeconds < durationSec)
            {
                await _devices.Player.SeekAsync(savedPos).ConfigureAwait(false);
            }
            if (wasPlaying && !wasPaused)
            {
                await _devices.Player.PlayAsync().ConfigureAwait(false);
            }

            _state.Mutate(s =>
            {
                s.DurationSeconds = durationSec;
                if (s.LoadedSong != null)
                {
                    s.LoadedSong.ActiveModel = activeModel;
                    s.LoadedSong.IsGmPassthrough = passthrough;
                    s.LoadedSong.CanSaveRemapped = format == MidiFormat.GeneralMidi && !passthrough;
                    s.LoadedSong.DurationSeconds = durationSec;
                    s.LoadedSong.DurationFormatted = FormatDuration(durationSec);
                    s.LoadedSong.MasterTranspose = opts.MasterTranspose;
                    s.LoadedSong.PitchBendMode = opts.PitchBendMode.ToString();
                    s.LoadedSong.PolyphonyGuard = opts.EnablePolyphonyGuard;
                    s.LoadedSong.Channels = channels;
                    s.LoadedSong.Health = health;
                    s.LoadedSong.ChannelPrograms = channels.Where(c => c.HasNotes).ToDictionary(c => c.Channel, c => c.PssVoiceName);
                }
            });
        }

        private RemapperOptions BuildCleanOptionsLocked(string activeModel, Dictionary<int, int?>? loadedOverrides)
        {
            lock (_stateLock)
            {
                if (loadedOverrides != null)
                {
                    _options.ChannelVoiceOverrides.Clear();
                    foreach (var kv in loadedOverrides) _options.ChannelVoiceOverrides[kv.Key] = kv.Value;
                }

                return new RemapperOptions
                {
                    TargetModel = activeModel,
                    EnablePolyphonyGuard = _options.EnablePolyphonyGuard,
                    DefaultReverbBoost = !string.Equals(activeModel, "pss680", StringComparison.OrdinalIgnoreCase),
                    FilterUnsupportedControllers = string.Equals(activeModel, "pss680", StringComparison.OrdinalIgnoreCase),
                    NormalizeAwmVelocities = !string.Equals(activeModel, "pss680", StringComparison.OrdinalIgnoreCase),
                    BakeFmVelocity = string.Equals(activeModel, "pss680", StringComparison.OrdinalIgnoreCase),
                    TranslateDrums = string.Equals(activeModel, "pss680", StringComparison.OrdinalIgnoreCase),
                    MasterTranspose = _options.MasterTranspose,
                    PitchBendMode = _options.PitchBendMode,
                    ChannelVoiceOverrides = new Dictionary<int, int?>(_options.ChannelVoiceOverrides),
                    ChannelOctaveShifts = new Dictionary<int, int>(_options.ChannelOctaveShifts)
                };
            }
        }

        private MidiFile PreparePlaybackMidi(
            MidiFile originalMidi,
            MidiFormat format,
            string activeModel,
            RemapperOptions opts,
            bool isGmPassthrough,
            bool isMsGs)
        {
            bool is680 = string.Equals(activeModel, "pss680", StringComparison.OrdinalIgnoreCase);
            bool needsRemap = !isGmPassthrough &&
                              ((format == MidiFormat.GeneralMidi) ||
                               (format == MidiFormat.Pss790Native && is680) ||
                               (format == MidiFormat.Pss680Native && !is680) ||
                               opts.MasterTranspose != 0 ||
                               opts.ChannelVoiceOverrides.Count > 0 ||
                               opts.ChannelOctaveShifts.Values.Any(v => v != 0));

            if (needsRemap)
            {
                return Remapper.Remap(originalMidi, opts);
            }

            if (isMsGs && !isGmPassthrough)
            {
                return Remapper.BuildPreviewTransformForMsGs(originalMidi, opts);
            }

            return originalMidi.Clone();
        }

        private bool IsMsGsOutput() =>
            _devices.Player.CurrentOutputDeviceName?.Contains("Microsoft GS", StringComparison.OrdinalIgnoreCase) == true;

        private (bool IsMuted, bool IsSolo, byte Volume) GetPlayerMixerSnapshot(int channel) =>
            _devices.Player.GetChannelMixerState(channel);

        private void OnPlayerStateChanged(object? sender, PlaybackStateEventArgs e)
        {
            if (_isDisposed) return;
            _state.Mutate(s =>
            {
                s.IsPlaying = e.IsPlaying;
                s.IsPaused = e.IsPaused;
                s.CurrentSeconds = e.CurrentTime.TotalSeconds;
                if (e.TotalTime.TotalSeconds > 0)
                {
                    s.DurationSeconds = e.TotalTime.TotalSeconds;
                }
            }, StateScope.Transport);
        }

        private void OnPlayerPlaybackFinished(object? sender, EventArgs e)
        {
            if (_isDisposed) return;
            ResetChannelActivity();

            if (_state.JukeboxContinuous && !_userRequestedStop && !_state.LoopEnabled)
            {
                _ = PlayNextSongAsync();
            }
        }

        private void OnPlayerChannelActivity(object? sender, ChannelActivityEventArgs e)
        {
            if (_isDisposed || e.Channel is < 0 or >= 16) return;
            lock (_meterLock)
            {
                _pendingChannelActive[e.Channel] = e.IsActive;
                _pendingChannelVel[e.Channel] = e.Velocity;
            }
        }

        private void FlushChannelActivityBatch()
        {
            if (_isDisposed) return;
            bool[] activeCopy = new bool[16];
            byte[] velCopy = new byte[16];
            bool anyChange = false;

            lock (_meterLock)
            {
                for (int i = 0; i < 16; i++)
                {
                    activeCopy[i] = _pendingChannelActive[i];
                    velCopy[i] = _pendingChannelVel[i];
                    if (activeCopy[i] != _state.ChannelActive[i] || velCopy[i] != _state.ChannelVelocities[i])
                    {
                        anyChange = true;
                    }
                    if (!_pendingChannelActive[i])
                    {
                        _pendingChannelVel[i] = 0;
                    }
                }
            }

            if (anyChange)
            {
                _state.Mutate(s =>
                {
                    Array.Copy(activeCopy, s.ChannelActive, 16);
                    Array.Copy(velCopy, s.ChannelVelocities, 16);
                }, StateScope.Channels);
            }
        }

        private void ResetChannelActivity()
        {
            lock (_meterLock)
            {
                Array.Clear(_pendingChannelActive);
                Array.Clear(_pendingChannelVel);
            }
            _state.Mutate(s =>
            {
                Array.Clear(s.ChannelActive);
                Array.Clear(s.ChannelVelocities);
            }, StateScope.Channels);
        }

        private void OnHardwareModelAutoDetected(HardwareModel model) => SetActiveModel(model.ToModelKey());
        private void OnOutputDeviceChanged(string? deviceName) => _ = ReapplyRemappingAsync(preservePosition: true);

        private void OnOutputDeviceDisconnected(object? sender, string deviceName)
        {
            ResetChannelActivity();
            _state.Mutate(s =>
            {
                s.IsPlaying = false;
                s.IsPaused = false;
                s.SelectedOutputDevice = null;
            });
        }

        private void OnSongPathRelocated(string oldPath, string? newPath)
        {
            lock (_stateLock)
            {
                if (_currentFilePath != null && string.Equals(_currentFilePath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    _currentFilePath = newPath;
                    _currentDisplayName = newPath != null ? Path.GetFileName(newPath) : _currentDisplayName;
                }
            }
            _state.Mutate(s =>
            {
                if (s.SelectedSongPath != null && string.Equals(s.SelectedSongPath, oldPath, StringComparison.OrdinalIgnoreCase))
                    s.SelectedSongPath = newPath;
                if (s.PlayingSongPath != null && string.Equals(s.PlayingSongPath, oldPath, StringComparison.OrdinalIgnoreCase))
                    s.PlayingSongPath = newPath;
                if (s.LoadedSong != null && string.Equals(s.LoadedSong.FilePath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    s.LoadedSong.FilePath = newPath ?? string.Empty;
                    if (newPath != null) s.LoadedSong.FileName = Path.GetFileName(newPath);
                }
            });
        }

        private void OnSongPathDeleted(string deletedPath, bool isFolder)
        {
            bool matchCurrent = false;
            lock (_stateLock)
            {
                if (_currentFilePath != null)
                {
                    matchCurrent = isFolder
                        ? AppPaths.IsPathInsideDirectory(_currentFilePath, deletedPath)
                        : string.Equals(_currentFilePath, deletedPath, StringComparison.OrdinalIgnoreCase);
                    if (matchCurrent)
                    {
                        _currentFilePath = null;
                        _loadedOriginalMidi = null;
                        _loadedRemappedMidi = null;
                    }
                }
            }

            if (matchCurrent)
            {
                Stop();
                _state.Mutate(s =>
                {
                    s.LoadedSong = null;
                    s.PlayingSongPath = null;
                    s.SelectedSongPath = null;
                });
            }
        }

        private static SongHealthDto MapHealth(MidiHealthReport r) => new()
        {
            BadgeType = r.BadgeType,
            BadgeLabel = r.BadgeLabel,
            BadgeTooltip = r.BadgeTooltip,
            PeakPolyphony = r.PeakPolyphony,
            VoiceStressRating = r.VoiceStressRating,
            ActiveChannelCount = r.ActiveChannelCount,
            TotalNoteCount = r.TotalNoteCount,
            ExplicitProgramChannels = r.ExplicitProgramChannels,
            InferredProgramChannels = r.InferredProgramChannels,
            XgSysExChannels = r.XgSysExChannels,
            UnassignedChannels = r.UnassignedChannels,
            StuckPedalIntervalsCount = r.StuckPedalIntervalsCount,
            DanglingPitchBendsCount = r.DanglingPitchBendsCount,
            AcousticMicroBendsNeutralized = r.AcousticMicroBendsNeutralized,
            OutOfBoundsNotesCount = r.OutOfBoundsNotesCount,
            NonStandardDrumChannels = r.NonStandardDrumChannels,
            XgSfxChannels = r.XgSfxChannels,
            AppliedAutoFixes = r.AppliedAutoFixes
        };

        private static string FormatTagLabel(MidiFormat fmt) => fmt switch
        {
            MidiFormat.Pss790Native => "PSS-790 Native",
            MidiFormat.Pss680Native => "PSS-680 Native",
            _ => "General MIDI"
        };

        public static string FormatDuration(double totalSeconds)
        {
            if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0)
                totalSeconds = 0;
            var ts = TimeSpan.FromSeconds(totalSeconds);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try { _meterTimer.Dispose(); } catch { }
            lock (_stateLock)
            {
                try { _loadCts?.Cancel(); _loadCts?.Dispose(); } catch { }
            }
            var player = _devices.Player;
            player.PlaybackStateChanged -= OnPlayerStateChanged;
            player.PlaybackFinished -= OnPlayerPlaybackFinished;
            player.ChannelActivity -= OnPlayerChannelActivity;
            player.OutputDeviceDisconnected -= OnOutputDeviceDisconnected;
            _devices.HardwareModelAutoDetected -= OnHardwareModelAutoDetected;
            _devices.OutputDeviceChanged -= OnOutputDeviceChanged;
            _library.SongPathRelocated -= OnSongPathRelocated;
            _library.SongPathDeleted -= OnSongPathDeleted;
        }
    }
}
