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
using Microsoft.Win32;
using PSS790Player.Core;
using PSS790Player.Models;

namespace PSS790Player.Services
{
    /// <summary>
    /// Coordinates PSS-680 FM Voice Designer, PSS-790 Vector Synth X/Y morphing,
    /// Hand Percussion pads, Format 03H Custom Drummer step sequencer, and SysEx Bulk Vault operations.
    /// </summary>
    public sealed class SoundDesignService : IDisposable
    {
        private readonly AppStateStore _state;
        private readonly HardwareDeviceService _devices;
        private readonly PlaybackCoordinator _playback;
        private readonly LibraryService _library;
        private readonly SysExManager _sysexManager;
        private readonly object _fmTimerLock = new();
        private CancellationTokenSource? _fmLiveDebounceCts;
        private volatile bool _isDisposed;

        public SoundDesignService(
            AppStateStore state,
            HardwareDeviceService devices,
            PlaybackCoordinator playback,
            LibraryService library)
        {
            _state = state;
            _devices = devices;
            _playback = playback;
            _library = library;

            _sysexManager = new SysExManager(_devices.Player, _devices.Recorder);
            _sysexManager.CaptureProgress += OnCaptureProgress;
            _sysexManager.DumpCaptured += OnDumpCaptured;
            _sysexManager.TransmitProgress += OnTransmitProgress;
            _devices.Recorder.SysExReceived += OnRecorderSysExReceived;
            _library.LibraryRootChanged += RefreshAllLibraries;
        }

        public void Initialize() => RefreshAllLibraries();

        public void RefreshAllLibraries()
        {
            RefreshFmVoiceLibrary();
            RefreshVectorLibrary();
            RefreshSysExLibrary();
        }

        // --- PSS-680 FM Voice Designer ---

        public void SelectFmBank(int bankNumber)
        {
            int bank = Math.Clamp(bankNumber, 0, 4);
            _devices.Player.SendVoiceOverride(0, 100 + bank, "pss680");
            _state.Mutate(s =>
            {
                s.SelectedFmBank = bank;
                s.FmVoice.BankNumber = bank;
            });
            _state.AddToast($"Switched PSS-680 to Bank {bank + 1} (Prog {100 + bank}).", ToastSeverity.Info);
        }

        public void ScheduleFmLiveSync()
        {
            if (!_state.FmLiveSyncEnabled) return;

            _state.Mutate(s => s.FmLiveStatus = "Updating...");
            CancellationToken token;
            lock (_fmTimerLock)
            {
                try { _fmLiveDebounceCts?.Cancel(); } catch { }
                try { _fmLiveDebounceCts?.Dispose(); } catch { }
                _fmLiveDebounceCts = new CancellationTokenSource();
                token = _fmLiveDebounceCts.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested || _isDisposed) return;
                    TransmitFmVoice(silent: true);
                    _state.Mutate(s => s.FmLiveStatus = _state.SelectedOutputDevice != null ? "Synced" : "Offline");
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        public void TransmitFmVoice(bool silent = false)
        {
            var voice = _state.FmVoice.ToCoreVoice();
            byte[] sysex = voice.ToSysExMessage();
            _devices.Player.SendSysEx(sysex);

            int bank = Math.Clamp(voice.BankNumber, 0, 4);
            _ = Task.Run(async () =>
            {
                await Task.Delay(35).ConfigureAwait(false);
                _devices.Player.SendVoiceOverride(0, 100 + bank, "pss680");
            });

            _state.Mutate(s =>
            {
                s.SelectedFmBank = bank;
                s.FmLiveStatus = _state.SelectedOutputDevice != null ? "Synced" : "Offline";
            });

            if (!silent)
            {
                _state.AddToast($"Transmitted FM Voice '{voice.Name}' to PSS-680 Bank {bank + 1}.", ToastSeverity.Success);
            }
        }

        public void AuditionFmNote(int note = 60, bool isChord = false)
        {
            int clamped = Math.Clamp(note, 24, 108);
            int bank = Math.Clamp(_state.SelectedFmBank, 0, 4);
            _ = Task.Run(async () =>
            {
                try
                {
                    _devices.Player.SendVoiceOverride(0, 100 + bank, "pss680");
                    await Task.Delay(35).ConfigureAwait(false);

                    if (isChord)
                    {
                        int n2 = Math.Clamp(clamped + 4, 24, 108);
                        int n3 = Math.Clamp(clamped + 7, 24, 108);
                        _devices.Player.SendEvent(new NoteOnEvent((SevenBitNumber)clamped, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
                        _devices.Player.SendEvent(new NoteOnEvent((SevenBitNumber)n2, (SevenBitNumber)95) { Channel = (FourBitNumber)0 });
                        _devices.Player.SendEvent(new NoteOnEvent((SevenBitNumber)n3, (SevenBitNumber)95) { Channel = (FourBitNumber)0 });
                        await Task.Delay(850).ConfigureAwait(false);
                        _devices.Player.SendEvent(new NoteOffEvent((SevenBitNumber)clamped, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                        _devices.Player.SendEvent(new NoteOffEvent((SevenBitNumber)n2, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                        _devices.Player.SendEvent(new NoteOffEvent((SevenBitNumber)n3, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                    }
                    else
                    {
                        _devices.Player.SendEvent(new NoteOnEvent((SevenBitNumber)clamped, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
                        await Task.Delay(800).ConfigureAwait(false);
                        _devices.Player.SendEvent(new NoteOffEvent((SevenBitNumber)clamped, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
                    }
                }
                catch { }
            });
        }

        public void ApplyInitFmPreset()
        {
            var init = FactoryPresets.CreateInitFmVoice(_state.SelectedFmBank);
            _state.Mutate(s => s.FmVoice = init);
            ScheduleFmLiveSync();
        }

        public void RandomizeFmVoice()
        {
            var rnd = FactoryPresets.CreateRandomFmVoice(_state.SelectedFmBank);
            _state.Mutate(s => s.FmVoice = rnd);
            ScheduleFmLiveSync();
        }

        public void SaveFmVoicePatch()
        {
            var voice = _state.FmVoice.ToCoreVoice();
            byte[] sysex = voice.ToSysExMessage();
            var safe = SanitizeFileName(voice.Name, ".syx");
            AppPaths.EnsureDirectoriesExist();
            var outPath = Path.Combine(AppPaths.FmVoicesDir, safe);
            File.WriteAllBytes(outPath, sysex);
            RefreshFmVoiceLibrary();
            _state.AddToast($"Saved FM Voice patch: {Path.GetFileNameWithoutExtension(safe)}", ToastSeverity.Success);
        }

        public void LoadFmVoicePatch(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.FmVoicesDir))
            {
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                var voice = PSS680Voice.FromSysExMessage(bytes);
                voice.Name = Path.GetFileNameWithoutExtension(filePath);
                var dto = FmVoiceState.FromCoreVoice(voice);
                dto.BankNumber = Math.Clamp(dto.BankNumber, 0, 4);

                _devices.Player.SendSysEx(bytes);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(35).ConfigureAwait(false);
                    _devices.Player.SendVoiceOverride(0, 100 + dto.BankNumber, "pss680");
                });

                _state.Mutate(s =>
                {
                    s.FmVoice = dto;
                    s.SelectedFmBank = dto.BankNumber;
                    s.FmLiveStatus = _state.SelectedOutputDevice != null ? "Synced" : "Offline";
                });
                _state.AddToast($"Loaded FM Voice: {voice.Name} (Bank {dto.BankNumber + 1})", ToastSeverity.Success);
            }
            catch (Exception ex)
            {
                _state.AddToast($"Failed to load FM Voice: {ex.Message}", ToastSeverity.Error);
            }
        }

        public void DeleteFmVoicePatch(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.FmVoicesDir))
            {
                return;
            }
            File.Delete(filePath);
            RefreshFmVoiceLibrary();
            _state.AddToast("FM Voice patch deleted.", ToastSeverity.Info);
        }

        public void SendCurrentFmVoiceToChannel(int channel)
        {
            var voice = _state.FmVoice.ToCoreVoice();
            byte[] sysex = voice.ToSysExMessage();
            _playback.ApplyFmVoiceToChannel(channel, voice.BankNumber, sysex, voice.Name);
        }

        public void RefreshFmVoiceLibrary()
        {
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                AppPaths.EnsureDirectoriesExist();
                var list = new List<FmVoicePatchItem>();
                if (Directory.Exists(AppPaths.FmVoicesDir))
                {
                    foreach (var file in Directory.GetFiles(AppPaths.FmVoicesDir, "*.syx")
                        .OrderByDescending(File.GetLastWriteTime))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            var voice = PSS680Voice.FromSysExMessage(File.ReadAllBytes(file));
                            string name = (string.IsNullOrWhiteSpace(voice.Name) || voice.Name == "Custom Voice")
                                ? Path.GetFileNameWithoutExtension(fi.Name)
                                : voice.Name;
                            list.Add(new FmVoicePatchItem
                            {
                                FileName = fi.Name,
                                FilePath = fi.FullName,
                                VoiceName = name,
                                BankNumber = voice.BankNumber,
                                BankDisplay = voice.BankNumber + 1,
                                FileSize = SysExManager.FormatBytes(fi.Length),
                                UpdatedAt = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                        catch { }
                    }
                }
                _state.Mutate(s => s.FmVoiceLibrary = list);
            });
        }

        // --- PSS-790 Vector Quad Synthesizer ---

        public static (double wA, double wB, double wC, double wD) CalculateVectorWeights(double x, double y)
        {
            double dist = Math.Sqrt(x * x + y * y);
            if (dist > 1.0)
            {
                x /= dist;
                y /= dist;
                dist = 1.0;
            }

            double rawA = Math.Max(0, y);
            double rawB = Math.Max(0, x);
            double rawC = Math.Max(0, -y);
            double rawD = Math.Max(0, -x);
            double manhattan = Math.Abs(x) + Math.Abs(y);

            double dirA = 0.25, dirB = 0.25, dirC = 0.25, dirD = 0.25;
            if (manhattan > 0.0001)
            {
                dirA = rawA / manhattan;
                dirB = rawB / manhattan;
                dirC = rawC / manhattan;
                dirD = rawD / manhattan;
            }

            return (
                (1.0 - dist) * 0.25 + dist * dirA,
                (1.0 - dist) * 0.25 + dist * dirB,
                (1.0 - dist) * 0.25 + dist * dirC,
                (1.0 - dist) * 0.25 + dist * dirD
            );
        }

        public void SetVectorVoices(int voiceA, int voiceB, int voiceC, int voiceD)
        {
            int vA = Math.Clamp(voiceA, 0, 99);
            int vB = Math.Clamp(voiceB, 0, 99);
            int vC = Math.Clamp(voiceC, 0, 99);
            int vD = Math.Clamp(voiceD, 0, 99);

            _state.Mutate(s =>
            {
                s.VectorVoiceA = vA;
                s.VectorVoiceB = vB;
                s.VectorVoiceC = vC;
                s.VectorVoiceD = vD;
            });

            _devices.Player.SendVoiceOverride(0, vA);
            _devices.Player.SendVoiceOverride(1, vB);
            _devices.Player.SendVoiceOverride(2, vC);
            _devices.Player.SendVoiceOverride(3, vD);
        }

        public void SetVectorPosition(double x, double y, bool sendToHardware = true, bool suppressUiNotify = false)
        {
            double dist = Math.Sqrt(x * x + y * y);
            if (dist > 1.0)
            {
                x /= dist;
                y /= dist;
            }

            if (suppressUiNotify)
            {
                _state.VectorX = x;
                _state.VectorY = y;
            }
            else
            {
                _state.Mutate(s =>
                {
                    s.VectorX = x;
                    s.VectorY = y;
                });
            }

            if (sendToHardware)
            {
                var (wA, wB, wC, wD) = CalculateVectorWeights(x, y);
                SendVectorVolumes(
                    (byte)Math.Round(wA * 127),
                    (byte)Math.Round(wB * 127),
                    (byte)Math.Round(wC * 127),
                    (byte)Math.Round(wD * 127));
            }
        }

        public void SendVectorVolumes(byte volA, byte volB, byte volC, byte volD)
        {
            if (_devices.Player.VectorQuadPlaybackEnabled)
            {
                _devices.Player.SetChannelVolume(0, (byte)Math.Clamp((int)Math.Round(volA * 100.0 / 127.0), 0, 100));
                _devices.Player.SetChannelVolume(1, (byte)Math.Clamp((int)Math.Round(volB * 100.0 / 127.0), 0, 100));
                _devices.Player.SetChannelVolume(2, (byte)Math.Clamp((int)Math.Round(volC * 100.0 / 127.0), 0, 100));
                _devices.Player.SetChannelVolume(3, (byte)Math.Clamp((int)Math.Round(volD * 100.0 / 127.0), 0, 100));
            }
            else
            {
                _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volA) { Channel = (FourBitNumber)0 });
                _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volB) { Channel = (FourBitNumber)1 });
                _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volC) { Channel = (FourBitNumber)2 });
                _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volD) { Channel = (FourBitNumber)3 });
            }
        }

        public void CycleVectorReverb(int channel)
        {
            int ch = Math.Clamp(channel, 0, 3);
            int next = (_state.VectorChannelEffects[ch].Rev + 1) % 4;
            _state.Mutate(s => s.VectorChannelEffects[ch].Rev = next);

            byte cc91 = next switch { 1 => 42, 2 => 85, 3 => 127, _ => 0 };
            _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)cc91) { Channel = (FourBitNumber)ch });
        }

        public void CycleVectorVibrato(int channel)
        {
            int ch = Math.Clamp(channel, 0, 3);
            int next = (_state.VectorChannelEffects[ch].Vib + 1) % 8;
            _state.Mutate(s => s.VectorChannelEffects[ch].Vib = next);

            byte cc1 = (byte)Math.Clamp(next * 18, 0, 127);
            _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)cc1) { Channel = (FourBitNumber)ch });
        }

        public void AuditionVectorChord(int note = 60)
        {
            int clamped = Math.Clamp(note, 24, 108);
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int ch = 0; ch < 4; ch++)
                        _devices.Player.SendEvent(new NoteOnEvent((SevenBitNumber)clamped, (SevenBitNumber)100) { Channel = (FourBitNumber)ch });
                    await Task.Delay(850).ConfigureAwait(false);
                    for (int ch = 0; ch < 4; ch++)
                        _devices.Player.SendEvent(new NoteOffEvent((SevenBitNumber)clamped, (SevenBitNumber)0) { Channel = (FourBitNumber)ch });
                }
                catch { }
            });
        }

        public void ToggleVectorQuadPlayback(bool enabled, string? presetKey = null)
        {
            int vA = _state.VectorVoiceA, vB = _state.VectorVoiceB, vC = _state.VectorVoiceC, vD = _state.VectorVoiceD;
            if (enabled && !string.IsNullOrEmpty(presetKey) && presetKey != "current")
            {
                var found = _state.VectorPresetLibrary.FirstOrDefault(p =>
                    string.Equals(p.FilePath, presetKey, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, presetKey, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    ApplyVectorPresetItem(found);
                    vA = found.VoiceA; vB = found.VoiceB; vC = found.VoiceC; vD = found.VoiceD;
                }
            }
            else if (enabled && _state.LoadedSong != null)
            {
                var chs = _state.LoadedSong.Channels;
                if (chs.Count >= 4)
                {
                    if (chs[0].HasNotes) vA = chs[0].PssVoice;
                    if (chs[1].HasNotes) vB = chs[1].PssVoice;
                    if (chs[2].HasNotes) vC = chs[2].PssVoice;
                    if (chs[3].HasNotes) vD = chs[3].PssVoice;
                }
            }

            var (wA, wB, wC, wD) = CalculateVectorWeights(_state.VectorX, _state.VectorY);
            _playback.SetVectorPlaybackMode(enabled, vA, vB, vC, vD,
                (byte)Math.Round(wA * 127),
                (byte)Math.Round(wB * 127),
                (byte)Math.Round(wC * 127),
                (byte)Math.Round(wD * 127));
        }

        public void SaveVectorPreset()
        {
            var safe = SanitizeFileName(_state.VectorPresetName, ".json");
            AppPaths.EnsureDirectoriesExist();
            var outPath = Path.Combine(AppPaths.VectorPresetsDir, safe);
            var payload = new
            {
                name = _state.VectorPresetName,
                voiceA = _state.VectorVoiceA,
                voiceB = _state.VectorVoiceB,
                voiceC = _state.VectorVoiceC,
                voiceD = _state.VectorVoiceD,
                posX = _state.VectorX,
                posY = _state.VectorY,
                orbitMode = _state.VectorOrbitMode,
                orbitSpeed = _state.VectorOrbitSpeed,
                channelEffects = _state.VectorChannelEffects
            };
            File.WriteAllText(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            RefreshVectorLibrary();
            _state.AddToast($"Saved Vector preset: {Path.GetFileNameWithoutExtension(safe)}", ToastSeverity.Success);
        }

        public void LoadVectorPreset(string filePath)
        {
            var found = _state.VectorPresetLibrary.FirstOrDefault(p => string.Equals(p.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                ApplyVectorPresetItem(found);
                _state.AddToast($"Loaded Vector preset: {found.Name}", ToastSeverity.Success);
            }
        }

        private void ApplyVectorPresetItem(VectorPresetFileItem preset)
        {
            _state.Mutate(s =>
            {
                s.VectorPresetName = preset.Name;
                s.VectorVoiceA = preset.VoiceA;
                s.VectorVoiceB = preset.VoiceB;
                s.VectorVoiceC = preset.VoiceC;
                s.VectorVoiceD = preset.VoiceD;
                s.VectorX = preset.PosX;
                s.VectorY = preset.PosY;
                s.VectorOrbitMode = preset.OrbitMode;
                s.VectorOrbitSpeed = preset.OrbitSpeed;
                for (int ch = 0; ch < 4; ch++)
                {
                    if (preset.ChannelEffects.TryGetValue(ch, out var eff))
                    {
                        s.VectorChannelEffects[ch] = new VectorChannelEffectState { Rev = eff.Rev, Vib = eff.Vib };
                    }
                }
                s.VectorIsOrbiting = preset.OrbitMode != "manual";
            });

            SetVectorVoices(preset.VoiceA, preset.VoiceB, preset.VoiceC, preset.VoiceD);
            for (int ch = 0; ch < 4; ch++)
            {
                var eff = _state.VectorChannelEffects[ch];
                byte cc91 = eff.Rev switch { 1 => 42, 2 => 85, 3 => 127, _ => 0 };
                byte cc1 = (byte)Math.Clamp(eff.Vib * 18, 0, 127);
                _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)cc91) { Channel = (FourBitNumber)ch });
                _devices.Player.SendEvent(new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)cc1) { Channel = (FourBitNumber)ch });
            }
            SetVectorPosition(preset.PosX, preset.PosY, sendToHardware: true);
        }

        public void DeleteVectorPreset(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.VectorPresetsDir))
            {
                return;
            }
            File.Delete(filePath);
            RefreshVectorLibrary();
            _state.AddToast("Vector preset deleted.", ToastSeverity.Info);
        }

        public void RefreshVectorLibrary()
        {
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                AppPaths.EnsureDirectoriesExist();
                var list = new List<VectorPresetFileItem>();
                if (Directory.Exists(AppPaths.VectorPresetsDir))
                {
                    foreach (var file in Directory.GetFiles(AppPaths.VectorPresetsDir, "*.json")
                        .OrderByDescending(File.GetLastWriteTime))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            using var doc = JsonDocument.Parse(File.ReadAllText(file));
                            var root = doc.RootElement;
                            var effs = new Dictionary<int, VectorChannelEffectState>
                            {
                                [0] = new(), [1] = new(), [2] = new(), [3] = new()
                            };
                            if (root.TryGetProperty("channelEffects", out var ceProp) || root.TryGetProperty("effects", out ceProp))
                            {
                                foreach (var prop in ceProp.EnumerateObject())
                                {
                                    if (int.TryParse(prop.Name, out int ch) && ch is >= 0 and <= 3)
                                    {
                                        effs[ch] = new VectorChannelEffectState
                                        {
                                            Rev = prop.Value.TryGetProperty("rev", out var rP) ? rP.GetInt32() : 1,
                                            Vib = prop.Value.TryGetProperty("vib", out var vP) ? vP.GetInt32() : 0
                                        };
                                    }
                                }
                            }
                            list.Add(new VectorPresetFileItem
                            {
                                FileName = fi.Name,
                                FilePath = fi.FullName,
                                Name = root.TryGetProperty("name", out var np) ? (np.GetString() ?? Path.GetFileNameWithoutExtension(fi.Name)) : Path.GetFileNameWithoutExtension(fi.Name),
                                VoiceA = root.TryGetProperty("voiceA", out var va) ? va.GetInt32() : 0,
                                VoiceB = root.TryGetProperty("voiceB", out var vb) ? vb.GetInt32() : 34,
                                VoiceC = root.TryGetProperty("voiceC", out var vc) ? vc.GetInt32() : 16,
                                VoiceD = root.TryGetProperty("voiceD", out var vd) ? vd.GetInt32() : 56,
                                PosX = root.TryGetProperty("posX", out var px) ? px.GetDouble() : 0.0,
                                PosY = root.TryGetProperty("posY", out var py) ? py.GetDouble() : 0.0,
                                OrbitMode = root.TryGetProperty("orbitMode", out var om) ? (om.GetString() ?? "manual") : "manual",
                                OrbitSpeed = root.TryGetProperty("orbitSpeed", out var os) ? os.GetDouble() : 3.0,
                                ChannelEffects = effs,
                                FileSize = SysExManager.FormatBytes(fi.Length),
                                UpdatedAt = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                        catch { }
                    }
                }
                _state.Mutate(s => s.VectorPresetLibrary = list);
            });
        }

        // --- Drum Pads & Format 03H Custom Drummer ---

        public void TriggerDrumPad(int note, int velocity = 110)
        {
            int clampedNote = Math.Clamp(note, 24, 108);
            int clampedVel = Math.Clamp(velocity, 1, 127);
            _ = Task.Run(async () =>
            {
                try
                {
                    _devices.Player.SendEvent(new NoteOnEvent((SevenBitNumber)clampedNote, (SevenBitNumber)clampedVel) { Channel = (FourBitNumber)15 });
                    await Task.Delay(140).ConfigureAwait(false);
                    _devices.Player.SendEvent(new NoteOffEvent((SevenBitNumber)clampedNote, (SevenBitNumber)0) { Channel = (FourBitNumber)15 });
                }
                catch { }
            });
        }

        public void ToggleDrummerStep(int rowIndex, int stepIndex)
        {
            if (rowIndex < 0 || rowIndex >= _state.DrummerRows.Count) return;
            int beat = stepIndex / 4;
            int sub = stepIndex % 4;
            if (beat is < 0 or >= 8) return;

            byte bitMask = (byte)(1 << (7 - (sub * 2)));
            _state.Mutate(s => s.DrummerRows[rowIndex].StepBytes[beat] ^= bitMask);
        }

        public void ClearDrummerPattern()
        {
            _state.Mutate(s =>
            {
                foreach (var row in s.DrummerRows) Array.Clear(row.StepBytes);
            });
        }

        public void ExecuteDrummerAction(string action)
        {
            try
            {
                var pattern = new Pss680CustomDrummerPattern
                {
                    BankNumber = Math.Clamp(_state.DrummerBankNumber, 0, 4),
                    BeatsLength = Math.Clamp(_state.DrummerBeatsLength, 1, 8),
                    IsTripletQuantize = _state.DrummerTripletQuantize
                };

                foreach (var uiRow in _state.DrummerRows)
                {
                    if (uiRow.TrackIndex is >= 0 and < 32)
                    {
                        Buffer.BlockCopy(uiRow.StepBytes, 0, pattern.Tracks[uiRow.TrackIndex].StepBytes, 0, 8);
                        pattern.Tracks[uiRow.TrackIndex].Accent1 = uiRow.Accent1;
                        pattern.Tracks[uiRow.TrackIndex].Accent2 = uiRow.Accent2;
                        pattern.Tracks[uiRow.TrackIndex].Roll = uiRow.Roll;
                    }
                }

                string cleanName = SanitizeFileName(_state.DrummerPatternName, "");
                if (action == "audition")
                {
                    var midi = pattern.ToMidiFile(_state.RecorderBpm, loopCount: 2);
                    _devices.Player.LoadMidi(midi);
                    _devices.Player.Play();
                    _state.AddToast($"Auditioning Custom Drummer pattern ({pattern.BeatsLength} beats at {_state.RecorderBpm} BPM).", ToastSeverity.Info);
                }
                else if (action == "export_midi")
                {
                    var midi = pattern.ToMidiFile(_state.RecorderBpm, loopCount: 4);
                    AppPaths.EnsureDirectoriesExist();
                    string outPath = Path.Combine(AppPaths.Pss680MidiDir, $"{cleanName}_Drummer.mid");
                    midi.Write(outPath, overwriteFile: true);
                    _library.RefreshLibrary();
                    _state.AddToast($"Saved Drummer MIDI track: {Path.GetFileName(outPath)}", ToastSeverity.Success);
                }
                else if (action == "save_sysex")
                {
                    byte[] sysex = pattern.ToSysExMessage();
                    AppPaths.EnsureDirectoriesExist();
                    string outPath = Path.Combine(AppPaths.Pss680SysExDir, $"{cleanName}_03H.syx");
                    File.WriteAllBytes(outPath, sysex);
                    RefreshSysExLibrary();
                    _state.AddToast($"Saved Custom Drummer SysEx backup: {Path.GetFileName(outPath)} (704 B).", ToastSeverity.Success);
                }
                else if (action == "transmit")
                {
                    byte[] sysex = pattern.ToSysExMessage();
                    _devices.Player.SendSysEx(sysex);
                    _state.AddToast("Transmitted Format 03H Custom Drummer SysEx to PSS-680.", ToastSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                _state.AddToast($"Custom Drummer error: {ex.Message}", ToastSeverity.Error);
            }
        }

        public void LoadDrummerPatternFromSysEx(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.SysExDir))
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    var packets = SysExManager.ExtractPackets(File.ReadAllBytes(filePath));
                    foreach (var raw in packets)
                    {
                        var parsed = Pss680SysExParser.Parse(raw);
                        if (parsed.Format == Pss680SysExFormat.RhythmPattern)
                        {
                            var pat = Pss680CustomDrummerPattern.FromSysExBytes(parsed.DecodedBytes);
                            var rows = new List<DrummerTrackState>();
                            int[] coreIds = { 8, 9, 13, 14, 15, 0, 2, 4, 30, 28 };
                            foreach (int idx in coreIds)
                            {
                                var tr = pat.Tracks[idx];
                                var copy = new byte[8];
                                Buffer.BlockCopy(tr.StepBytes, 0, copy, 0, 8);
                                rows.Add(new DrummerTrackState
                                {
                                    TrackIndex = tr.TrackIndex,
                                    InstrumentId = tr.InstrumentId,
                                    MidiNote = tr.MidiNote,
                                    PadLabel = tr.PadLabel,
                                    InstrumentName = tr.InstrumentName,
                                    Accent1 = tr.Accent1,
                                    Accent2 = tr.Accent2,
                                    Roll = tr.Roll,
                                    StepBytes = copy
                                });
                            }

                            _state.Mutate(s =>
                            {
                                s.DrummerBankNumber = pat.BankNumber;
                                s.DrummerBeatsLength = pat.BeatsLength;
                                s.DrummerTripletQuantize = pat.IsTripletQuantize;
                                s.DrummerRows = rows;
                                s.SysExViewMode = "drums";
                                s.ActiveTab = StudioTab.SysEx;
                            });
                            _state.AddToast("Loaded Format 03H Custom Drummer pattern into step grid.", ToastSeverity.Success);
                            return;
                        }
                    }
                    _state.AddToast("No Format 03H Custom Drummer packet found in dump.", ToastSeverity.Warning);
                }
                catch (Exception ex)
                {
                    _state.AddToast($"Failed to decode Custom Drummer dump: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        // --- SysEx Vault ---

        public void RefreshSysExLibrary()
        {
            _ = Task.Run(() =>
            {
                if (_isDisposed) return;
                var list = _sysexManager.GetSysExLibrary();
                _state.Mutate(s => s.SysExDumps = list);
            });
        }

        public void StartSysExCapture() => _sysexManager.StartCapture();

        public void StopSysExCapture()
        {
            _sysexManager.StopCapture(autoSave: true);
            _state.Mutate(s => s.IsSysExCapturing = false);
        }

        public async Task TransmitSysExDumpAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            try
            {
                await _sysexManager.TransmitDumpAsync(filePath, _state.SysExTransmitDelayMs).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _state.AddToast($"SysEx transmission failed: {ex.Message}", ToastSeverity.Error);
            }
        }

        public void CancelSysExTransmit() => _sysexManager.CancelTransmission();

        public void DeleteSysExDump(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !AppPaths.IsPathInsideDirectory(filePath, AppPaths.SysExDir)) return;
            _sysexManager.DeleteDump(filePath, AppPaths.SysExDir);
            RefreshSysExLibrary();
            _state.AddToast("SysEx backup deleted.", ToastSeverity.Info);
        }

        public void RenameSysExDump(string filePath, string newName)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(newName) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.SysExDir))
            {
                return;
            }
            try
            {
                var renamed = _sysexManager.RenameDump(filePath, newName, AppPaths.SysExDir);
                RefreshSysExLibrary();
                _state.AddToast($"Renamed to: {Path.GetFileName(renamed)}", ToastSeverity.Success);
            }
            catch (Exception ex)
            {
                _state.AddToast($"Rename failed: {ex.Message}", ToastSeverity.Error);
            }
        }

        public void ImportSysExWithDialog()
        {
            var app = Application.Current;
            void ShowDlg()
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "SysEx Files (*.syx)|*.syx|All Files (*.*)|*.*",
                    Title = "Import System Exclusive Dump File"
                };
                if (dlg.ShowDialog() == true)
                {
                    var imported = _sysexManager.ImportDump(dlg.FileName, _state.ActiveModel);
                    RefreshSysExLibrary();
                    _state.AddToast($"Imported SysEx dump: {Path.GetFileName(imported)}", ToastSeverity.Success);
                }
            }

            if (app != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(ShowDlg);
            else
                ShowDlg();
        }

        public void InspectSysExDump(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !AppPaths.IsPathInsideDirectory(filePath, AppPaths.SysExDir)) return;
            _ = Task.Run(() =>
            {
                try
                {
                    var info = SysExManager.ParseDumpInfo(filePath);
                    var (formatCode, canExtractMidi, canOpenDrummer, summary) = AnalyzePss680Metadata(filePath);
                    _state.Mutate(s => s.InspectedSysEx = new SysExInspectResultDto
                    {
                        FileName = info.FileName,
                        FilePath = info.FilePath,
                        FileSizeBytes = info.FileSizeBytes,
                        FileSizeFormatted = info.FileSizeFormatted,
                        CreatedAtFormatted = info.CreatedAtFormatted,
                        PacketCount = info.PacketCount,
                        Manufacturer = info.Manufacturer,
                        DumpType = info.DumpType,
                        HexPreview = info.HexPreview,
                        FormatCode = formatCode,
                        CanExtractMidi = canExtractMidi,
                        CanOpenDrummerGrid = canOpenDrummer,
                        DecodedSummary = summary
                    });
                }
                catch (Exception ex)
                {
                    _state.AddToast($"Failed to inspect SysEx dump: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void ExtractSysExToMidi(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
                !AppPaths.IsPathInsideDirectory(filePath, AppPaths.SysExDir))
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    var packets = SysExManager.ExtractPackets(File.ReadAllBytes(filePath));
                    foreach (var raw in packets)
                    {
                        var parsed = Pss680SysExParser.Parse(raw);
                        MidiFile? midi = parsed.Format switch
                        {
                            Pss680SysExFormat.MelodyMemory => Pss680SysExTranslators.DecodeMelodyMemoryToMidi(parsed.DecodedBytes),
                            Pss680SysExFormat.ChordMemory => Pss680SysExTranslators.DecodeChordMemoryToMidi(parsed.DecodedBytes),
                            Pss680SysExFormat.RhythmPattern => Pss680CustomDrummerPattern.FromSysExBytes(parsed.DecodedBytes).ToMidiFile(_state.RecorderBpm, 4),
                            _ => null
                        };

                        if (midi != null)
                        {
                            AppPaths.EnsureDirectoriesExist();
                            string outPath = Path.Combine(AppPaths.Pss680MidiDir, $"{Path.GetFileNameWithoutExtension(filePath)}_Extracted.mid");
                            midi.Write(outPath, overwriteFile: true);
                            _library.RefreshLibrary();
                            _state.AddToast($"Extracted MIDI track: {Path.GetFileName(outPath)}", ToastSeverity.Success);
                            return;
                        }
                    }
                    _state.AddToast("Dump contains no extractable sequence (01H / 02H / 03H).", ToastSeverity.Warning);
                }
                catch (Exception ex)
                {
                    _state.AddToast($"MIDI extraction failed: {ex.Message}", ToastSeverity.Error);
                }
            });
        }

        public void SendQuickSysEx(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            _sysexManager.SendQuickCommand(command);
            var lower = command.ToLowerInvariant();
            string msg = lower switch
            {
                "mode_99" or "pss680_mode_99" => "PSS-680 Sound Source Mode: Press [MIDI MODE] + [+] until display shows 'c.99'.",
                "req_voice_dump" or "pss680_req_voice" or "req_melody_dump" or "pss680_req_melody"
                    or "req_chord_dump" or "pss680_req_chord" or "req_rhythm_dump" or "pss680_req_rhythm"
                    => "PSS-680 ignores remote dump requests. Arm SysEx Capture, then hold [MIDI] + press the panel dump key.",
                "all_reset" => "Sent All Notes Off & Reset Controllers panic across all 16 channels.",
                _ => $"Transmitted SysEx command: {command.ToUpperInvariant()}"
            };
            _state.AddToast(msg, ToastSeverity.Info);
        }

        public void OpenSysExFolder() => AppPaths.OpenSysExFolderInExplorer();

        private static (string? FormatCode, bool CanExtractMidi, bool CanOpenDrummer, string? Summary) AnalyzePss680Metadata(string filePath)
        {
            var packets = SysExManager.ExtractPackets(File.ReadAllBytes(filePath));
            var summaries = new List<string>();
            string? firstCode = null;
            bool canExtract = false, canDrummer = false;

            foreach (var raw in packets)
            {
                var p = Pss680SysExParser.Parse(raw);
                firstCode ??= p.Format switch
                {
                    Pss680SysExFormat.FmVoiceData => "00H",
                    Pss680SysExFormat.MelodyMemory => "01H",
                    Pss680SysExFormat.ChordMemory => "02H",
                    Pss680SysExFormat.RhythmPattern => "03H",
                    _ => null
                };

                if (p.Format == Pss680SysExFormat.FmVoiceData && p.IsChecksumValid)
                {
                    var v = PSS680Voice.FromDecodedBytes(p.DecodedBytes);
                    summaries.Add($"FM Voice Bank {v.BankNumber + 1} (FB:{v.Feedback}, ModWave:{v.Modulator.Waveform}, CarWave:{v.Carrier.Waveform})");
                }
                else if (p.Format == Pss680SysExFormat.MelodyMemory)
                {
                    canExtract = true;
                    var banks = Pss680SysExTranslators.AnalyzeMelodyMemory(p.DecodedBytes);
                    int totalNotes = banks.Sum(b => b.NoteCount);
                    summaries.Add($"Melody Memory 5 Banks • {totalNotes} notes recorded");
                }
                else if (p.Format == Pss680SysExFormat.ChordMemory)
                {
                    canExtract = true;
                    var cm = Pss680SysExTranslators.AnalyzeChordMemory(p.DecodedBytes);
                    summaries.Add($"Chord Memory Bank {cm.BankNumber} • Style #{cm.InitialStyleNumber:D2} ({cm.InitialStyleName}) • {cm.Tempo} BPM • {cm.Chords.Count} steps");
                }
                else if (p.Format == Pss680SysExFormat.RhythmPattern)
                {
                    canExtract = true;
                    canDrummer = true;
                    var pat = Pss680CustomDrummerPattern.FromSysExBytes(p.DecodedBytes);
                    summaries.Add($"Custom Drummer Bank {pat.BankNumber + 1} • {pat.BeatsLength} beats • {(pat.IsTripletQuantize ? "1/3 Triplet" : "1/4 Straight")}");
                }
            }

            return (firstCode, canExtract, canDrummer, summaries.Count > 0 ? string.Join(" | ", summaries) : null);
        }

        private void OnCaptureProgress(object? sender, SysExCaptureEventArgs e)
        {
            _state.Mutate(s =>
            {
                s.IsSysExCapturing = _sysexManager.IsCapturing;
                s.SysExCapturePackets = e.PacketCount;
                s.SysExCaptureBytes = e.ByteCount;
                s.SysExCaptureStatus = e.StatusMessage;
            });
        }

        private void OnDumpCaptured(object? sender, SysExDumpInfo info)
        {
            RefreshSysExLibrary();
            _state.Mutate(s => s.IsSysExCapturing = false);
            _state.AddToast($"Captured SysEx dump: {info.FileName} ({info.PacketCount} packets)", ToastSeverity.Success);
        }

        private void OnTransmitProgress(object? sender, SysExTransmitEventArgs e)
        {
            _state.Mutate(s =>
            {
                s.IsSysExTransmitting = !e.IsCompleted && !e.IsCancelled;
                s.SysExTransmitProgress = e.Percent;
                s.SysExTransmitPacketIndex = e.CurrentPacket;
                s.SysExTransmitPacketTotal = e.TotalPackets;
                s.SysExTransmitStatus = e.StatusMessage;
            });
            if (e.IsCompleted && !e.IsCancelled)
            {
                _state.AddToast("SysEx transmission finished.", ToastSeverity.Success);
            }
        }

        private void OnRecorderSysExReceived(object? sender, NormalSysExEvent sysexEvent)
        {
            if (_isDisposed || sysexEvent.Data == null || sysexEvent.Data.Length < 70) return;
            try
            {
                byte[] fullMsg = new byte[72];
                fullMsg[0] = 0xF0;
                Buffer.BlockCopy(sysexEvent.Data, 0, fullMsg, 1, Math.Min(71, sysexEvent.Data.Length));
                fullMsg[71] = 0xF7;

                var packet = Pss680SysExParser.Parse(fullMsg);
                if (packet.Format == Pss680SysExFormat.FmVoiceData && packet.IsChecksumValid)
                {
                    var captured = PSS680Voice.FromDecodedBytes(packet.DecodedBytes);
                    captured.Name = $"PSS680 Bank {captured.BankNumber + 1}";
                    var dto = FmVoiceState.FromCoreVoice(captured);
                    _state.Mutate(s =>
                    {
                        s.FmVoice = dto;
                        s.SelectedFmBank = dto.BankNumber;
                    });
                    _state.AddToast($"Captured live PSS-680 FM Voice Bank {dto.BankNumber + 1}.", ToastSeverity.Info);
                }
            }
            catch { }
        }

        private static string SanitizeFileName(string? name, string requiredExtension)
        {
            var raw = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                raw = raw.Replace(c, '_');
            }
            if (!string.IsNullOrEmpty(requiredExtension) &&
                !raw.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                raw += requiredExtension;
            }
            return raw;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            lock (_fmTimerLock)
            {
                try { _fmLiveDebounceCts?.Cancel(); _fmLiveDebounceCts?.Dispose(); } catch { }
            }
            _devices.Recorder.SysExReceived -= OnRecorderSysExReceived;
            _library.LibraryRootChanged -= RefreshAllLibraries;
            _sysexManager.CaptureProgress -= OnCaptureProgress;
            _sysexManager.DumpCaptured -= OnDumpCaptured;
            _sysexManager.TransmitProgress -= OnTransmitProgress;
            try { _sysexManager.Dispose(); } catch { }
        }
    }
}
