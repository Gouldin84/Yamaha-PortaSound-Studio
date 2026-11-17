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

    public class MidiRecorder : IDisposable
    {
        private InputDevice? _inputDevice;
        private Recording? _recording;
        private MidiFile? _lastRecordedFile;
        private readonly object _lock = new();

        private RecorderState _state = RecorderState.Idle;
        private int _bpm = 120;
        private CancellationTokenSource? _countInCts;
        private System.Timers.Timer? _tickTimer;
        private Stopwatch _elapsedStopwatch = new();
        private int _totalEventsRecorded;
        private int _totalNotesRecorded;

        public bool MidiThruEnabled { get; set; } = false;
        public MidiPlayer? ThruTargetPlayer { get; set; }

        public RecorderState State => _state;
        public int Bpm => _bpm;
        public MidiFile? LastRecordedFile => _lastRecordedFile;
        public string? CurrentInputDeviceName => _inputDevice?.Name;

        public event EventHandler<ChannelActivityEventArgs>? InputActivity;
        public event EventHandler<RecorderStateEventArgs>? StateChanged;
        public event EventHandler<RecorderTickEventArgs>? Tick;
        public event EventHandler<NormalSysExEvent>? SysExReceived;

        public MidiRecorder()
        {
            _tickTimer = new System.Timers.Timer(100);
            _tickTimer.Elapsed += (s, e) => OnTickTimer();
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
            lock (_lock)
            {
                if (_state is RecorderState.Recording or RecorderState.CountIn)
                {
                    StopRecording();
                }

                if (_inputDevice != null)
                {
                    try
                    {
                        if (_inputDevice.IsListeningForEvents)
                        {
                            _inputDevice.StopEventsListening();
                        }
                        _inputDevice.EventReceived -= OnInputEventReceived;
                        _inputDevice.Dispose();
                    }
                    catch { }
                    _inputDevice = null;
                }

                if (!string.IsNullOrEmpty(deviceName))
                {
                    try
                    {
                        _inputDevice = InputDevice.GetByName(deviceName);
                        _inputDevice.EventReceived += OnInputEventReceived;
                        _inputDevice.StartEventsListening();
                    }
                    catch
                    {
                        _inputDevice = null;
                    }
                }
            }
        }

        private void OnInputEventReceived(object? sender, MidiEventReceivedEventArgs e)
        {
            if (e.Event is NormalSysExEvent sysex)
            {
                SysExReceived?.Invoke(this, sysex);
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
                        Velocity = (byte)noteOn.Velocity
                    });

                    if (_state == RecorderState.Recording && isActive)
                    {
                        Interlocked.Increment(ref _totalNotesRecorded);
                    }
                }
                else if (channelEvent is NoteOffEvent)
                {
                    InputActivity?.Invoke(this, new ChannelActivityEventArgs
                    {
                        Channel = ch,
                        IsActive = false,
                        Velocity = 0
                    });
                }

                if (_state == RecorderState.Recording)
                {
                    Interlocked.Increment(ref _totalEventsRecorded);
                }

                // MIDI Thru forwarding (if enabled and target player is set)
                if (MidiThruEnabled && ThruTargetPlayer != null)
                {
                    ThruTargetPlayer.SendEvent(e.Event);
                }
            }
        }

        public async Task StartRecordingAsync(int bpm, bool countIn = false)
        {
            lock (_lock)
            {
                if (_inputDevice == null)
                {
                    throw new InvalidOperationException("No MIDI input device selected.");
                }

                if (_state is RecorderState.Recording or RecorderState.CountIn)
                {
                    return;
                }

                _bpm = Math.Clamp(bpm, 40, 240);
                _totalEventsRecorded = 0;
                _totalNotesRecorded = 0;
            }

            if (countIn)
            {
                _state = RecorderState.CountIn;
                _countInCts = new CancellationTokenSource();
                var beatIntervalMs = (int)(60000.0 / _bpm);

                for (int beat = 1; beat <= 4; beat++)
                {
                    if (_countInCts.IsCancellationRequested)
                    {
                        _state = RecorderState.Idle;
                        NotifyState();
                        return;
                    }

                    StateChanged?.Invoke(this, new RecorderStateEventArgs
                    {
                        State = RecorderState.CountIn,
                        CountInBeat = beat,
                        Bpm = _bpm
                    });

                    try
                    {
                        await Task.Delay(beatIntervalMs, _countInCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        _state = RecorderState.Idle;
                        NotifyState();
                        return;
                    }
                }
            }

            lock (_lock)
            {
                if (_inputDevice == null) return;

                var tempoMap = TempoMap.Create(Tempo.FromBeatsPerMinute(_bpm));
                _recording?.Dispose();
                _recording = new Recording(tempoMap, _inputDevice);

                _state = RecorderState.Recording;
                _elapsedStopwatch.Restart();
                _recording.Start();
                _tickTimer?.Start();

                NotifyState();
            }
        }

        public void StopRecording()
        {
            lock (_lock)
            {
                if (_countInCts != null)
                {
                    _countInCts.Cancel();
                    _countInCts.Dispose();
                    _countInCts = null;
                }

                _tickTimer?.Stop();
                _elapsedStopwatch.Stop();

                if (_recording != null && _recording.IsRunning)
                {
                    try
                    {
                        _recording.Stop();
                        _lastRecordedFile = _recording.ToFile();
                    }
                    catch
                    {
                        _lastRecordedFile = null;
                    }
                }

                _state = RecorderState.Stopped;
                NotifyState();
            }
        }

        private void OnTickTimer()
        {
            if (_state != RecorderState.Recording) return;

            double elapsedSec = _elapsedStopwatch.Elapsed.TotalSeconds;
            double beatsPerSec = _bpm / 60.0;
            double totalBeats = elapsedSec * beatsPerSec;
            int bar = (int)(totalBeats / 4.0) + 1;
            int beat = (int)(totalBeats % 4.0) + 1;

            Tick?.Invoke(this, new RecorderTickEventArgs
            {
                ElapsedSeconds = elapsedSec,
                EventCount = _totalEventsRecorded,
                NoteCount = _totalNotesRecorded,
                CurrentBar = bar,
                CurrentBeat = beat
            });
        }

        private void NotifyState()
        {
            StateChanged?.Invoke(this, new RecorderStateEventArgs
            {
                State = _state,
                CountInBeat = 0,
                Bpm = _bpm
            });
        }

        public (int noteCount, int eventCount, double durationSeconds) GetLastTakeStats()
        {
            if (_lastRecordedFile == null) return (0, 0, 0);

            var notes = _lastRecordedFile.GetNotes().Count;
            var timedEvents = _lastRecordedFile.GetTimedEvents().Count();
            var duration = _lastRecordedFile.GetDuration<MetricTimeSpan>().TotalSeconds;

            return (notes, timedEvents, duration);
        }

        public string SaveNativeTake(string fileName)
        {
            if (_lastRecordedFile == null)
            {
                throw new InvalidOperationException("No recorded take available to save.");
            }

            if (!fileName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".mid";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            AppPaths.EnsureDirectoriesExist();
            string fullPath = Path.Combine(AppPaths.Pss790MidiDir, fileName);

            // Direct 1:1 clone of the PSS-790 take + inject PSS790_REMAPPED:v1 marker in Track 0
            var fileToSave = _lastRecordedFile.Clone();
            var firstTrack = fileToSave.GetTrackChunks().FirstOrDefault();
            if (firstTrack == null)
            {
                firstTrack = new TrackChunk();
                fileToSave.Chunks.Add(firstTrack);
            }

            firstTrack.Events.Insert(0, new TextEvent(Remapper.Pss790MarkerText) { DeltaTime = 0 });

            fileToSave.Write(fullPath, overwriteFile: true);
            return fullPath;
        }

        public string ExportTakeToGeneralMidi(string fileName)
        {
            if (_lastRecordedFile == null)
            {
                throw new InvalidOperationException("No recorded take available to save.");
            }

            if (!fileName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".mid";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            AppPaths.EnsureDirectoriesExist();
            string fullPath = Path.Combine(AppPaths.GmMidiDir, fileName);

            // Reverse-remap: PSS-790 Ch 16 drums -> GM Ch 10, Chart 1 -> GM drum notes, Chart 2 -> GM instruments
            var fileToSave = ReverseRemapper.ReverseRemapToGeneralMidi(_lastRecordedFile);
            fileToSave.Write(fullPath, overwriteFile: true);
            return fullPath;
        }

        public string SaveTake(string fileName, bool remapForPss790, RemapperOptions? options = null)
        {
            return remapForPss790 ? SaveNativeTake(fileName) : ExportTakeToGeneralMidi(fileName);
        }

        public void Dispose()
        {
            _tickTimer?.Dispose();
            _countInCts?.Cancel();
            _countInCts?.Dispose();
            _recording?.Dispose();

            if (_inputDevice != null)
            {
                try
                {
                    if (_inputDevice.IsListeningForEvents)
                    {
                        _inputDevice.StopEventsListening();
                    }
                    _inputDevice.EventReceived -= OnInputEventReceived;
                    _inputDevice.Dispose();
                }
                catch { }
                _inputDevice = null;
            }
        }
    }
}
