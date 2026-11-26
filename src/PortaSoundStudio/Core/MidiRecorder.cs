using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

namespace PSS790Player.Core
{
    public enum RecorderState
    {
        Idle,
        CountIn,
        Recording,
        Stopped
    }

    public class RecorderStateEventArgs : EventArgs
    {
        public RecorderState State { get; set; }
        public int CountInBeat { get; set; }
        public int Bpm { get; set; }
    }

    public class RecorderTickEventArgs : EventArgs
    {
        public double ElapsedSeconds { get; set; }
        public int EventCount { get; set; }
        public int NoteCount { get; set; }
        public int CurrentBar { get; set; }
        public int CurrentBeat { get; set; }
    }

    public class RecordingTakeInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string CreatedAtFormatted { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public string DurationFormatted { get; set; } = string.Empty;
        public int NoteCount { get; set; }
        public int TrackCount { get; set; }
    }

    public class MidiRecorder : IDisposable
    {
        private static readonly HashSet<string> WindowsReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private IInputDevice? _inputDevice;
        private string? _inputDeviceName;
        private Recording? _recording;
        private MidiFile? _lastRecordedFile;
        private readonly object _lock = new();
        private readonly object _sysExLock = new();

        // Reassembly buffer for multi-packet WinMM SysEx streams (NormalSysExEvent + EscapeSysExEvent chunks)
        private readonly List<byte> _sysExAssemblyBuffer = new(256);

        private volatile RecorderState _state = RecorderState.Idle;
        private int _bpm = 120;
        private CancellationTokenSource? _countInCts;
        private readonly System.Timers.Timer _tickTimer;
        private readonly Stopwatch _elapsedStopwatch = new();
        private int _totalEventsRecorded;
        private int _totalNotesRecorded;
        private volatile bool _isDisposed;

        public bool MidiThruEnabled { get; set; } = false;
        public MidiPlayer? ThruTargetPlayer { get; set; }

        public RecorderState State => _state;
        public int Bpm
        {
            get { lock (_lock) return _bpm; }
        }
        public MidiFile? LastRecordedFile
        {
            get { lock (_lock) return _lastRecordedFile; }
        }
        public string? CurrentInputDeviceName
        {
            get
            {
                lock (_lock)
                {
                    try { return _inputDeviceName ?? (_inputDevice as InputDevice)?.Name; }
                    catch { return null; }
                }
            }
        }

        public event EventHandler<ChannelActivityEventArgs>? InputActivity;
        public event EventHandler<RecorderStateEventArgs>? StateChanged;
        public event EventHandler<RecorderTickEventArgs>? Tick;
        public event EventHandler<NormalSysExEvent>? SysExReceived;
        public event EventHandler<string>? InputDeviceDisconnected;

        public MidiRecorder()
        {
            _tickTimer = new System.Timers.Timer(100);
            _tickTimer.Elapsed += (s, e) =>
            {
                try { OnTickTimer(); } catch { }
            };
        }

        public static List<string> GetInputDeviceNames()
        {
            try
            {
                return InputDevice.GetAll().Select(d => d.Name).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public void SetInputDevice(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName) || _isDisposed)
            {
                SetInputDeviceInstance(null, null);
                return;
            }

            InputDevice? newDevice = null;
            try
            {
                newDevice = InputDevice.GetByName(deviceName);
                SetInputDeviceInstance(newDevice, newDevice.Name ?? deviceName);
            }
            catch
            {
                SetInputDeviceInstance(null, null);
                if (newDevice != null)
                {
                    try { newDevice.Dispose(); } catch { }
                }
            }
        }

        public void SetInputDeviceInstance(IInputDevice? newDevice, string? deviceName = null)
        {
            if (_state is RecorderState.Recording or RecorderState.CountIn)
            {
                StopRecording();
            }

            IInputDevice? oldDevice;
            lock (_lock)
            {
                oldDevice = _inputDevice;
                _inputDevice = null;
                _inputDeviceName = null;
            }

            lock (_sysExLock)
            {
                _sysExAssemblyBuffer.Clear();
            }

            if (oldDevice != null)
            {
                try
                {
                    if (oldDevice.IsListeningForEvents)
                    {
                        oldDevice.StopEventsListening();
                    }
                }
                catch { }
                try { oldDevice.EventReceived -= OnInputEventReceived; } catch { }
                try { oldDevice.Dispose(); } catch { }
            }

            if (newDevice == null || _isDisposed)
            {
                return;
            }

            try
            {
                newDevice.EventReceived += OnInputEventReceived;
                newDevice.StartEventsListening();

                lock (_lock)
                {
                    if (_isDisposed)
                    {
                        try { newDevice.StopEventsListening(); } catch { }
                        newDevice.EventReceived -= OnInputEventReceived;
                        newDevice.Dispose();
                        return;
                    }
                    _inputDevice = newDevice;
                    _inputDeviceName = deviceName ?? (newDevice as InputDevice)?.Name;
                }
            }
            catch
            {
                try { newDevice.EventReceived -= OnInputEventReceived; } catch { }
                try { newDevice.Dispose(); } catch { }
            }
        }

        public void HandleDeviceDisconnected()
        {
            string? disconnectedName = CurrentInputDeviceName;
            SetInputDevice(null);

            if (!string.IsNullOrEmpty(disconnectedName))
            {
                InputDeviceDisconnected?.Invoke(this, disconnectedName);
            }
        }

        private void OnInputEventReceived(object? sender, MidiEventReceivedEventArgs e)
        {
            if (_isDisposed || e.Event == null) return;

            // Ignore standalone MIDI Real-Time status events (Active Sensing 0xFE, Clock 0xF8, etc.)
            if (e.Event is SystemRealTimeEvent)
            {
                return;
            }

            // Reassemble single- or multi-packet WinMM SysEx streams
            if (e.Event is NormalSysExEvent normalSysEx)
            {
                ProcessIncomingSysExChunk(normalSysEx.Data, isStartChunk: true);
                return;
            }

            if (e.Event is EscapeSysExEvent escapeSysEx)
            {
                ProcessIncomingSysExChunk(escapeSysEx.Data, isStartChunk: false);
                return;
            }

            if (e.Event is ChannelEvent channelEvent)
            {
                int ch = channelEvent.Channel;

                if (channelEvent is NoteOnEvent noteOn)
                {
                    bool isActive = noteOn.Velocity > 0;
                    InputActivity?.Invoke(this, new ChannelActivityEventArgs
                    {
                        Channel = ch,
                        IsActive = isActive,
                        Velocity = (byte)noteOn.Velocity,
                        NoteNumber = (byte)noteOn.NoteNumber
                    });

                    if (_state == RecorderState.Recording && isActive)
                    {
                        Interlocked.Increment(ref _totalNotesRecorded);
                    }
                }
                else if (channelEvent is NoteOffEvent noteOff)
                {
                    InputActivity?.Invoke(this, new ChannelActivityEventArgs
                    {
                        Channel = ch,
                        IsActive = false,
                        Velocity = 0,
                        NoteNumber = (byte)noteOff.NoteNumber
                    });
                }

                if (_state == RecorderState.Recording)
                {
                    Interlocked.Increment(ref _totalEventsRecorded);
                }

                if (MidiThruEnabled && ThruTargetPlayer != null)
                {
                    ThruTargetPlayer.SendEvent(e.Event);
                }
            }
        }

        private void ProcessIncomingSysExChunk(byte[]? data, bool isStartChunk)
        {
            if (data == null || data.Length == 0) return;

            NormalSysExEvent? completedPacket = null;

            lock (_sysExLock)
            {
                if (isStartChunk)
                {
                    _sysExAssemblyBuffer.Clear();
                }
                else if (_sysExAssemblyBuffer.Count == 0)
                {
                    // Orphaned continuation chunk without a leading NormalSysExEvent -> ignore
                    return;
                }

                for (int i = 0; i < data.Length; i++)
                {
                    byte b = data[i];

                    if (b == 0xF7)
                    {
                        if (_sysExAssemblyBuffer.Count > 0)
                        {
                            _sysExAssemblyBuffer.Add(0xF7);
                            completedPacket = new NormalSysExEvent(_sysExAssemblyBuffer.ToArray());
                        }
                        _sysExAssemblyBuffer.Clear();
                        break;
                    }

                    if (b >= 0xF8)
                    {
                        // Strip interleaved MIDI Real-Time bytes (Active Sensing 0xFE, Clock 0xF8)
                        continue;
                    }

                    if (b >= 0x80)
                    {
                        // Stray non-realtime status byte aborts corrupt SysEx stream
                        _sysExAssemblyBuffer.Clear();
                        return;
                    }

                    _sysExAssemblyBuffer.Add(b);
                }
            }

            if (completedPacket != null)
            {
                SysExReceived?.Invoke(this, completedPacket);
            }
        }

        public async Task StartRecordingAsync(int bpm, bool countIn = false)
        {
            CancellationTokenSource? localCts = null;
            CancellationToken token = CancellationToken.None;
            int clampedBpm;

            lock (_lock)
            {
                if (_isDisposed) return;

                if (_inputDevice == null)
                {
                    throw new InvalidOperationException("No MIDI input device selected.");
                }

                if (_state is RecorderState.Recording or RecorderState.CountIn)
                {
                    return;
                }

                clampedBpm = Math.Clamp(bpm, 40, 240);
                _bpm = clampedBpm;
                Interlocked.Exchange(ref _totalEventsRecorded, 0);
                Interlocked.Exchange(ref _totalNotesRecorded, 0);

                if (countIn)
                {
                    _state = RecorderState.CountIn;
                    localCts = new CancellationTokenSource();
                    _countInCts = localCts;
                    token = localCts.Token;
                }
            }

            if (countIn && localCts != null)
            {
                int beatIntervalMs = (int)(60000.0 / clampedBpm);
                try
                {
                    for (int beat = 1; beat <= 4; beat++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            bool shouldNotify = false;
                            lock (_lock)
                            {
                                if (ReferenceEquals(_countInCts, localCts))
                                {
                                    _countInCts = null;
                                    if (_state == RecorderState.CountIn)
                                    {
                                        _state = RecorderState.Idle;
                                    }
                                    shouldNotify = true;
                                }
                            }
                            if (shouldNotify) NotifyState();
                            return;
                        }

                        StateChanged?.Invoke(this, new RecorderStateEventArgs
                        {
                            State = RecorderState.CountIn,
                            CountInBeat = beat,
                            Bpm = clampedBpm
                        });

                        await Task.Delay(beatIntervalMs, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    bool shouldNotify = false;
                    lock (_lock)
                    {
                        if (ReferenceEquals(_countInCts, localCts))
                        {
                            _countInCts = null;
                            if (_state == RecorderState.CountIn)
                            {
                                _state = RecorderState.Idle;
                            }
                            shouldNotify = true;
                        }
                    }
                    if (shouldNotify) NotifyState();
                    return;
                }
                finally
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_countInCts, localCts))
                        {
                            _countInCts = null;
                        }
                    }
                    try { localCts.Dispose(); } catch { }
                }
            }

            Recording? newRecording = null;
            bool started = false;

            lock (_lock)
            {
                if (_isDisposed || _inputDevice == null)
                {
                    _state = RecorderState.Idle;
                }
                else if ((!countIn || _state == RecorderState.CountIn) && !token.IsCancellationRequested)
                {
                    var tempoMap = TempoMap.Create(Tempo.FromBeatsPerMinute(_bpm));
                    _recording?.Dispose();
                    newRecording = new Recording(tempoMap, _inputDevice);
                    _recording = newRecording;
                    _elapsedStopwatch.Restart();
                    started = true;
                }
            }

            if (started && newRecording != null)
            {
                bool orphaned = false;
                try
                {
                    newRecording.Start();
                    lock (_lock)
                    {
                        if (ReferenceEquals(_recording, newRecording) && !_isDisposed)
                        {
                            _state = RecorderState.Recording;
                        }
                        else
                        {
                            orphaned = true;
                        }
                    }

                    if (!orphaned)
                    {
                        try { _tickTimer.Start(); } catch (ObjectDisposedException) { }
                    }
                    else
                    {
                        try { if (newRecording.IsRunning) newRecording.Stop(); } catch { }
                        try { newRecording.Dispose(); } catch { }
                    }
                }
                catch
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_recording, newRecording))
                        {
                            _recording = null;
                            _state = RecorderState.Idle;
                        }
                    }
                    try { newRecording.Dispose(); } catch { }
                    throw;
                }
            }

            NotifyState();
        }

        public void StopRecording()
        {
            CancellationTokenSource? ctsToCancel;
            Recording? recordingToStop;

            lock (_lock)
            {
                ctsToCancel = _countInCts;
                _countInCts = null;
                recordingToStop = _recording;
                _recording = null;
                _state = RecorderState.Stopped;
            }

            if (ctsToCancel != null)
            {
                try { ctsToCancel.Cancel(); } catch { }
            }

            try { _tickTimer.Stop(); } catch (ObjectDisposedException) { }
            _elapsedStopwatch.Stop();

            MidiFile? capturedFile = null;
            if (recordingToStop != null)
            {
                try
                {
                    if (recordingToStop.IsRunning)
                    {
                        recordingToStop.Stop();
                    }
                    capturedFile = recordingToStop.ToFile();
                }
                catch
                {
                    capturedFile = null;
                }
                finally
                {
                    try { recordingToStop.Dispose(); } catch { }
                }
            }

            lock (_lock)
            {
                _lastRecordedFile = capturedFile;
            }

            NotifyState();
        }

        private void OnTickTimer()
        {
            if (_isDisposed || _state != RecorderState.Recording) return;
            try
            {
                int bpmSnapshot;
                lock (_lock)
                {
                    bpmSnapshot = _bpm;
                }

                double elapsedSec = _elapsedStopwatch.Elapsed.TotalSeconds;
                double beatsPerSec = bpmSnapshot / 60.0;
                double totalBeats = elapsedSec * beatsPerSec;
                int bar = (int)(totalBeats / 4.0) + 1;
                int beat = (int)(totalBeats % 4.0) + 1;

                Tick?.Invoke(this, new RecorderTickEventArgs
                {
                    ElapsedSeconds = elapsedSec,
                    EventCount = Volatile.Read(ref _totalEventsRecorded),
                    NoteCount = Volatile.Read(ref _totalNotesRecorded),
                    CurrentBar = bar,
                    CurrentBeat = beat
                });
            }
            catch { }
        }

        private void NotifyState()
        {
            int bpmSnapshot;
            lock (_lock)
            {
                bpmSnapshot = _bpm;
            }

            StateChanged?.Invoke(this, new RecorderStateEventArgs
            {
                State = _state,
                CountInBeat = 0,
                Bpm = bpmSnapshot
            });
        }

        public (int noteCount, int eventCount, double durationSeconds) GetLastTakeStats()
        {
            MidiFile? fileSnapshot;
            lock (_lock)
            {
                fileSnapshot = _lastRecordedFile;
            }

            if (fileSnapshot == null) return (0, 0, 0);

            var notes = fileSnapshot.GetNotes().Count;
            var timedEvents = fileSnapshot.GetTimedEvents().Count();
            var duration = fileSnapshot.GetDuration<MetricTimeSpan>().TotalSeconds;

            return (notes, timedEvents, duration);
        }

        public static void EmbedRecordingMarker(MidiFile midi, string model = "pss790")
        {
            var firstTrack = midi.GetTrackChunks().FirstOrDefault();
            if (firstTrack == null)
            {
                firstTrack = new TrackChunk();
                midi.Chunks.Add(firstTrack);
            }

            bool is680 = string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase);
            string targetMarker = is680 ? Remapper.Pss680RecordingMarkerText : Remapper.Pss790RecordingMarkerText;

            // Strip any prior recording markers via ManageTimedEvents so Track 0 timestamps never shift
            foreach (var track in midi.GetTrackChunks())
            {
                using var mgr = track.ManageTimedEvents();
                mgr.Objects.RemoveAll(te =>
                    (te.Event is TextEvent t && (t.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase) ||
                                                 t.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase))) ||
                    (te.Event is MarkerEvent m && (m.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase) ||
                                                   m.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase))));
            }

            firstTrack.Events.Insert(0, new TextEvent(targetMarker) { DeltaTime = 0 });
        }

        /// <summary>
        /// Shifts all notes and performance events backward so the first sounding note starts at tick 0,
        /// while clamping preceding setup events (Program Changes, Control Changes, Tempo) cleanly to tick 0.
        /// </summary>
        public static long TrimLeadingSilence(MidiFile midi)
        {
            if (midi == null) return 0;

            long firstNoteTick = midi.GetTimedEvents()
                .Where(te => te.Event is NoteOnEvent on && on.Velocity > 0)
                .Select(te => (long?)te.Time)
                .Min() ?? 0;

            if (firstNoteTick <= 0) return 0;

            foreach (var track in midi.GetTrackChunks())
            {
                using var mgr = track.ManageTimedEvents();
                foreach (var te in mgr.Objects)
                {
                    if (te.Event is EndOfTrackEvent) continue;
                    te.Time = te.Time >= firstNoteTick
                        ? te.Time - firstNoteTick
                        : 0;
                }
            }

            return firstNoteTick;
        }

        /// <summary>
        /// Snaps note start times cleanly to a musical beat subdivision (e.g., 16 for 1/16, 8 for 1/8)
        /// while preserving note durations to prevent accidental zero-length or inverted note pairs.
        /// </summary>
        public static int QuantizeTake(MidiFile midi, int gridSubdivision)
        {
            if (midi == null || gridSubdivision <= 0) return 0;

            short tpqn = (midi.TimeDivision as TicksPerQuarterNoteTimeDivision)?.TicksPerQuarterNote ?? (short)480;
            long stepTicks = Math.Max(1, (tpqn * 4L) / gridSubdivision);
            int adjustedCount = 0;

            foreach (var track in midi.GetTrackChunks())
            {
                using var notesMgr = track.ManageNotes();
                foreach (var note in notesMgr.Objects)
                {
                    long snapped = ((note.Time + (stepTicks / 2)) / stepTicks) * stepTicks;
                    if (snapped != note.Time)
                    {
                        note.Time = snapped;
                        adjustedCount++;
                    }
                }
            }

            return adjustedCount;
        }

        /// <summary>
        /// Tags tracks lacking explicit names with authentic PSS-790 8-track or PSS-680 channel roles.
        /// </summary>
        public static void ApplyHardwareTrackLabels(MidiFile midi, string model = "pss790")
        {
            if (midi == null) return;
            bool is680 = string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase);

            foreach (var track in midi.GetTrackChunks())
            {
                if (track.Events.OfType<SequenceTrackNameEvent>().Any()) continue;

                var primaryNote = track.Events.OfType<NoteOnEvent>().FirstOrDefault(n => n.Velocity > 0);
                if (primaryNote == null) continue;

                int ch = primaryNote.Channel;
                string label = is680
                    ? ch switch
                    {
                        0 => "PSS-680 Melody / Lead (Ch 1)",
                        10 => "PSS-680 Chord Backing (Ch 11)",
                        11 or 12 or 13 => $"PSS-680 Orchestra (Ch {ch + 1})",
                        14 => "PSS-680 Bass (Ch 15)",
                        15 => "PSS-680 Rhythm / Custom Drummer (Ch 16)",
                        _ => $"PSS-680 Track (Ch {ch + 1})"
                    }
                    : ch switch
                    {
                        >= 0 and <= 3 => $"PSS-790 Vector / Synth (Ch {ch + 1})",
                        >= 4 and <= 8 => $"PSS-790 Melody {ch - 3} (Ch {ch + 1})",
                        >= 9 and <= 13 => $"PSS-790 Accomp Chord/Arp (Ch {ch + 1})",
                        14 => "PSS-790 Accomp Bass (Ch 15)",
                        15 => "PSS-790 Rhythm / Drum Pads (Ch 16)",
                        _ => $"PSS-790 Track (Ch {ch + 1})"
                    };

                track.Events.Insert(0, new SequenceTrackNameEvent(label) { DeltaTime = 0 });
            }
        }

        public int TrimCurrentTakeSilence()
        {
            MidiFile? fileSnapshot;
            lock (_lock)
            {
                fileSnapshot = _lastRecordedFile;
            }
            if (fileSnapshot == null) return 0;

            long trimmed = TrimLeadingSilence(fileSnapshot);
            return trimmed > 0 ? (int)Math.Min(int.MaxValue, trimmed) : 0;
        }

        public int QuantizeCurrentTake(int gridSubdivision)
        {
            MidiFile? fileSnapshot;
            lock (_lock)
            {
                fileSnapshot = _lastRecordedFile;
            }
            if (fileSnapshot == null) return 0;

            return QuantizeTake(fileSnapshot, gridSubdivision);
        }

        public string SaveNativeTake(string fileName, string model = "pss790")
        {
            MidiFile? fileSnapshot;
            lock (_lock)
            {
                fileSnapshot = _lastRecordedFile;
            }

            if (fileSnapshot == null)
            {
                throw new InvalidOperationException("No recorded take available to save.");
            }

            string sanitized = SanitizeFileName(fileName);
            AppPaths.EnsureDirectoriesExist();
            string targetDir = AppPaths.GetModelMidiDir(model);
            string fullPath = Path.Combine(targetDir, sanitized);

            var fileToSave = fileSnapshot.Clone();
            EmbedRecordingMarker(fileToSave, model);

            fileToSave.Write(fullPath, overwriteFile: true);
            return fullPath;
        }

        public string ExportTakeToGeneralMidi(string fileName, string model = "pss790")
        {
            MidiFile? fileSnapshot;
            lock (_lock)
            {
                fileSnapshot = _lastRecordedFile;
            }

            if (fileSnapshot == null)
            {
                throw new InvalidOperationException("No recorded take available to save.");
            }

            string sanitized = SanitizeFileName(fileName);
            AppPaths.EnsureDirectoriesExist();
            string fullPath = Path.Combine(AppPaths.GmMidiDir, sanitized);

            var fileToSave = ReverseRemapper.ReverseRemapToGeneralMidi(fileSnapshot, model);
            fileToSave.Write(fullPath, overwriteFile: true);
            return fullPath;
        }

        public string SaveTake(string fileName, bool remapForPss, string model = "pss790", RemapperOptions? options = null)
        {
            return remapForPss ? SaveNativeTake(fileName, model) : ExportTakeToGeneralMidi(fileName, model);
        }

        public string? AutoSaveTake(string model = "pss790")
        {
            MidiFile? fileSnapshot;
            lock (_lock)
            {
                fileSnapshot = _lastRecordedFile;
            }

            if (fileSnapshot == null) return null;

            int notes = fileSnapshot.GetNotes().Count;
            if (notes == 0) return null;

            AppPaths.EnsureDirectoriesExist();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fullPath = string.Empty;

            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string fileName = attempt == 0 ? $"Take_{timestamp}.mid" : $"Take_{timestamp}_{attempt}.mid";
                string candidate = Path.Combine(AppPaths.RecordingsDir, fileName);
                if (!File.Exists(candidate))
                {
                    fullPath = candidate;
                    break;
                }
            }

            if (string.IsNullOrEmpty(fullPath))
            {
                fullPath = Path.Combine(AppPaths.RecordingsDir, $"Take_{timestamp}_{Guid.NewGuid():N}.mid");
            }

            var fileToSave = fileSnapshot.Clone();
            EmbedRecordingMarker(fileToSave, model);
            fileToSave.Write(fullPath, overwriteFile: false);
            return fullPath;
        }

        public string SaveTakeToRecordings(string fileName, string model = "pss790")
        {
            MidiFile? fileSnapshot;
            lock (_lock)
            {
                fileSnapshot = _lastRecordedFile;
            }

            if (fileSnapshot == null)
            {
                throw new InvalidOperationException("No recorded take available to save.");
            }

            string sanitized = SanitizeFileName(fileName);
            AppPaths.EnsureDirectoriesExist();
            string fullPath = Path.Combine(AppPaths.RecordingsDir, sanitized);

            var fileToSave = fileSnapshot.Clone();
            EmbedRecordingMarker(fileToSave, model);
            fileToSave.Write(fullPath, overwriteFile: true);
            return fullPath;
        }

        public List<RecordingTakeInfo> GetRecordingsLibrary()
        {
            AppPaths.EnsureDirectoriesExist();
            var list = new List<RecordingTakeInfo>();

            if (!Directory.Exists(AppPaths.RecordingsDir))
                return list;

            var dir = new DirectoryInfo(AppPaths.RecordingsDir);
            var files = dir.GetFiles("*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.Extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                            f.Extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc);

            foreach (var fi in files)
            {
                try
                {
                    double durSeconds = 0;
                    int noteCount = 0;
                    int trackCount = 0;

                    try
                    {
                        var mf = MidiFile.Read(fi.FullName, Constants.DefaultReadingSettings);
                        durSeconds = mf.GetDuration<MetricTimeSpan>().TotalSeconds;
                        noteCount = mf.GetNotes().Count;
                        trackCount = mf.GetTrackChunks().Count();
                    }
                    catch
                    {
                        // Fallback if file is being written or corrupt
                    }

                    list.Add(new RecordingTakeInfo
                    {
                        FileName = fi.Name,
                        FilePath = fi.FullName,
                        FileSizeBytes = fi.Length,
                        FileSizeFormatted = $"{Math.Round(fi.Length / 1024.0, 1)} KB",
                        CreatedAt = fi.LastWriteTime,
                        CreatedAtFormatted = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        DurationSeconds = durSeconds,
                        DurationFormatted = TimeSpan.FromSeconds(durSeconds).ToString(@"mm\:ss"),
                        NoteCount = noteCount,
                        TrackCount = trackCount
                    });
                }
                catch
                {
                    // Skip unreadable files
                }
            }

            return list;
        }

        public string ExportRecordingToGeneralMidi(string sourcePath, string? targetFileName = null)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Recording file not found.", sourcePath);

            AppPaths.EnsureDirectoriesExist();
            var rawName = string.IsNullOrWhiteSpace(targetFileName) ? Path.GetFileName(sourcePath) : targetFileName;
            var outFileName = SanitizeFileName(rawName);

            var destPath = Path.Combine(AppPaths.GmMidiDir, outFileName);
            var sourceMidi = MidiFile.Read(sourcePath, Constants.DefaultReadingSettings);
            var gmFile = ReverseRemapper.ReverseRemapToGeneralMidi(sourceMidi);
            gmFile.Write(destPath, overwriteFile: true);
            return destPath;
        }

        public string ExportRecordingToPss(string sourcePath, string? targetFileName = null, string model = "pss790")
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Recording file not found.", sourcePath);

            AppPaths.EnsureDirectoriesExist();
            var rawName = string.IsNullOrWhiteSpace(targetFileName) ? Path.GetFileName(sourcePath) : targetFileName;
            var outFileName = SanitizeFileName(rawName);

            var destPath = Path.Combine(AppPaths.GetModelMidiDir(model), outFileName);
            var sourceMidi = MidiFile.Read(sourcePath, Constants.DefaultReadingSettings);
            var pssFile = sourceMidi.Clone();
            EmbedRecordingMarker(pssFile, model);
            pssFile.Write(destPath, overwriteFile: true);
            return destPath;
        }

        public string ExportRecordingToPss790(string sourcePath, string? targetFileName = null) =>
            ExportRecordingToPss(sourcePath, targetFileName, "pss790");

        public bool DeleteRecording(string filePath, string? baseDir = null)
        {
            var targetDir = string.IsNullOrWhiteSpace(baseDir) ? AppPaths.RecordingsDir : baseDir;
            if (string.IsNullOrEmpty(targetDir) || !AppPaths.IsPathInsideDirectory(filePath, targetDir))
            {
                return false;
            }

            if (!File.Exists(filePath)) return false;
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string RenameRecording(string filePath, string newFileName, string? baseDir = null)
        {
            var targetDir = string.IsNullOrWhiteSpace(baseDir) ? AppPaths.RecordingsDir : baseDir;
            if (string.IsNullOrEmpty(targetDir) || !AppPaths.IsPathInsideDirectory(filePath, targetDir))
            {
                throw new UnauthorizedAccessException("Cannot rename files outside recordings directory.");
            }

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Recording file not found.", filePath);

            if (string.IsNullOrWhiteSpace(newFileName))
                throw new ArgumentException("New file name cannot be empty.", nameof(newFileName));

            string trimmed = newFileName.Trim();
            if (trimmed is "." or ".." ||
                trimmed.Contains("..", StringComparison.Ordinal) ||
                trimmed.Contains('/') ||
                trimmed.Contains('\\') ||
                Path.IsPathRooted(trimmed) ||
                Path.GetFileName(trimmed) != trimmed ||
                trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Invalid file name: path traversal or illegal filename characters are not permitted.", nameof(newFileName));
            }

            string baseName = Path.GetFileNameWithoutExtension(trimmed);
            if (string.IsNullOrWhiteSpace(baseName) || WindowsReservedFileNames.Contains(baseName))
            {
                throw new ArgumentException("Invalid file name: reserved or empty file names are not permitted.", nameof(newFileName));
            }

            string finalName = trimmed.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + ".mid";

            string dir = Path.GetFullPath(Path.GetDirectoryName(filePath) ?? AppPaths.RecordingsDir);
            string newPath = Path.GetFullPath(Path.Combine(dir, finalName));

            string dirPrefix = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
            if (!newPath.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid file name: resolved path escapes target directory.", nameof(newFileName));
            }

            bool isCaseOnlyRename = string.Equals(Path.GetFullPath(filePath), newPath, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(newPath) && !isCaseOnlyRename)
            {
                throw new InvalidOperationException($"A recording named '{finalName}' already exists.");
            }

            File.Move(filePath, newPath, overwrite: isCaseOnlyRename);
            return newPath;
        }

        private static string SanitizeFileName(string fileName)
        {
            string name = Path.GetFileName(fileName.Trim());
            if (!name.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            {
                name += ".mid";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _tickTimer.Stop();
                _tickTimer.Dispose();
            }
            catch { }

            CancellationTokenSource? cts;
            Recording? rec;
            IInputDevice? dev;

            lock (_lock)
            {
                cts = _countInCts;
                _countInCts = null;
                rec = _recording;
                _recording = null;
                dev = _inputDevice;
                _inputDevice = null;
                _inputDeviceName = null;
            }

            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }

            if (rec != null)
            {
                try { rec.Dispose(); } catch { }
            }

            if (dev != null)
            {
                try
                {
                    if (dev.IsListeningForEvents)
                    {
                        dev.StopEventsListening();
                    }
                }
                catch { }
                try { dev.EventReceived -= OnInputEventReceived; } catch { }
                try { dev.Dispose(); } catch { }
            }

            GC.SuppressFinalize(this);
        }
    }
}
