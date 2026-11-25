using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;

namespace PSS790Player.Core
{
    public class ChannelActivityEventArgs : EventArgs
    {
        public int Channel { get; set; }
        public bool IsActive { get; set; }
        public byte Velocity { get; set; }
        public byte NoteNumber { get; set; }
    }

    public class PlaybackStateEventArgs : EventArgs
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public TimeSpan CurrentTime { get; set; }
        public TimeSpan TotalTime { get; set; }
    }

    public class MidiPlayer : IDisposable
    {
        private IOutputDevice? _outputDevice;
        private string? _currentOutputDeviceName;
        private FilteringOutputDevice? _filteringDevice;
        private Playback? _playback;
        private MidiFile? _currentFile;
        private TimeSpan _cachedTotalDuration = TimeSpan.Zero;
        private double _speed = 1.0;
        private bool _loop;
        private bool _hasFinished;
        private volatile bool _isDisposed;

        // Strict non-nested lock boundaries:
        // _stateLock  : _currentFile, _playback, _filteringDevice, _cachedTotalDuration, _speed, _loop, _hasFinished, _channelMutes, _channelSolos, _isDisposed
        // _deviceLock : _outputDevice, _currentOutputDeviceName, hardware SendEvent / PrepareForEventsSending
        // _notesLock  : _activeNotes, _activeNoteCounts
        // INVARIANT: Never nest these locks, never call Playback.Start/Stop/MoveTo*/Dispose under a lock,
        // and never raise events under a lock.
        private readonly object _stateLock = new();
        private readonly object _deviceLock = new();
        private readonly object _notesLock = new();
        private readonly object _initLock = new();

        private readonly Dictionary<(int Channel, byte Note), int> _activeNotes = new();
        private readonly int[] _activeNoteCounts = new int[16];

        private readonly bool[] _channelMutes = new bool[16];
        private readonly bool[] _channelSolos = new bool[16];
        private readonly byte[] _channelVolumes = new byte[16];
        private readonly byte[] _lastFileChannelVolumes = new byte[16];

        // Lock-free atomic state for real-time audio path filtering
        private int _muteMask;
        private int _soloMask;
        private int _usedChannelMask;
        private int _dirtyBendMask;
        private int _dirtyModMask;
        private int _dirtySustainMask;
        private int _dirtyVolumeMask;
        private volatile bool _isPss680;
        private string _activeModel = "pss790";
        private int _vectorQuadPlaybackEnabled;

        private EventHandler? _playbackFinishedHandler;
        private EventHandler? _repeatStartedHandler;

        private readonly System.Timers.Timer _progressTimer;

        public event EventHandler<ChannelActivityEventArgs>? ChannelActivity;
        public event EventHandler<PlaybackStateEventArgs>? PlaybackStateChanged;
        public event EventHandler<string>? OutputDeviceDisconnected;
        public event EventHandler? PlaybackFinished;

        public bool IsPlaying => CaptureStateSnapshot().IsPlaying;
        public bool IsPaused => CaptureStateSnapshot().IsPaused;
        public TimeSpan TotalDuration => CaptureStateSnapshot().TotalTime;
        public TimeSpan CurrentPosition => CaptureStateSnapshot().CurrentTime;

        private static bool SafeIsRunning(Playback? p)
        {
            try
            {
                return p != null && p.IsRunning;
            }
            catch
            {
                return false;
            }
        }

        public string? CurrentOutputDeviceName
        {
            get
            {
                lock (_deviceLock)
                {
                    return _currentOutputDeviceName;
                }
            }
        }

        public string ActiveModel
        {
            get => _activeModel;
            set
            {
                var normalized = value ?? "pss790";
                _activeModel = normalized;
                _isPss680 = string.Equals(normalized, "pss680", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool VectorQuadPlaybackEnabled
        {
            get => Volatile.Read(ref _vectorQuadPlaybackEnabled) != 0;
            set
            {
                int previous = Interlocked.Exchange(ref _vectorQuadPlaybackEnabled, value ? 1 : 0);
                if (previous != 0 && !value)
                {
                    // Flush active notes on replicated quad channels 1-3 so held voices never hang
                    SendChannelNoteOff(1);
                    SendChannelNoteOff(2);
                    SendChannelNoteOff(3);
                }
            }
        }

        public MidiPlayer()
        {
            for (int i = 0; i < 16; i++)
            {
                Volatile.Write(ref _channelVolumes[i], (byte)100);
                Volatile.Write(ref _lastFileChannelVolumes[i], (byte)100);
            }

            _isPss680 = string.Equals(_activeModel, "pss680", StringComparison.OrdinalIgnoreCase);

            _progressTimer = new System.Timers.Timer(100);
            _progressTimer.Elapsed += (s, e) =>
            {
                try
                {
                    if (_isDisposed) return;
                    var snapshot = CaptureStateSnapshot();
                    if (snapshot.IsPlaying)
                    {
                        PlaybackStateChanged?.Invoke(this, snapshot);
                    }
                }
                catch { }
            };
        }

        public static List<string> GetOutputDeviceNames()
        {
            try
            {
                return OutputDevice.GetAll().Select(d => d.Name).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        public void SetOutputDevice(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                SetOutputDeviceInternal(null, null);
                return;
            }

            try
            {
                var dev = OutputDevice.GetByName(deviceName);
                SetOutputDeviceInternal(dev, deviceName);
            }
            catch
            {
                SetOutputDeviceInternal(null, null);
            }
        }

        public void SetOutputDevice(IOutputDevice? outputDevice)
        {
            string? resolvedName = outputDevice is OutputDevice concrete ? concrete.Name : null;
            SetOutputDeviceInternal(outputDevice, resolvedName);
        }

        private void SetOutputDeviceInternal(IOutputDevice? outputDevice, string? deviceName)
        {
            lock (_deviceLock)
            {
                if (ReferenceEquals(_outputDevice, outputDevice) && string.Equals(_currentOutputDeviceName, deviceName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            var preState = CaptureStateSnapshot();
            bool wasPlaying = preState.IsPlaying;
            TimeSpan currentPos = preState.CurrentTime;

            // 1. Stop and tear down existing playback outside all locks
            StopAndDetachPlayback(disposePlayback: true);

            // 2. Swap hardware device under _deviceLock, then dispose old device outside the lock
            IOutputDevice? oldDevice;
            lock (_deviceLock)
            {
                oldDevice = _outputDevice;
                _outputDevice = outputDevice;
                _currentOutputDeviceName = outputDevice != null ? deviceName : null;
            }

            if (oldDevice != null && !ReferenceEquals(oldDevice, outputDevice))
            {
                try { oldDevice.Dispose(); } catch { }
            }

            // 3. Re-initialize playback if both file and device are ready
            bool hasFile;
            lock (_stateLock)
            {
                hasFile = _currentFile != null && !_isDisposed;
            }

            if (hasFile && outputDevice != null)
            {
                InitializePlayback();
                if (wasPlaying)
                {
                    Seek(currentPos);
                    Play();
                    return;
                }
            }

            NotifyState();
        }

        public void HandleDeviceDisconnected()
        {
            HandleDeviceDisconnectedInternal(expectedDevice: null, deferStop: false);
        }

        private void HandleDeviceDisconnectedInternal(IOutputDevice? expectedDevice, bool deferStop)
        {
            IOutputDevice? oldDevice;
            string? disconnectedName;

            lock (_deviceLock)
            {
                // Ignore stale failure notifications if the UI already swapped in a new device
                if (expectedDevice != null && !ReferenceEquals(_outputDevice, expectedDevice))
                {
                    return;
                }

                oldDevice = _outputDevice;
                disconnectedName = _currentOutputDeviceName;
                _outputDevice = null;
                _currentOutputDeviceName = null;
            }

            if (oldDevice != null)
            {
                try { oldDevice.Dispose(); } catch { }
            }

            if (deferStop)
            {
                // Never call Playback.Stop() synchronously from DryWetMIDI's own clock callback thread
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Stop();
                        if (!string.IsNullOrEmpty(disconnectedName))
                        {
                            OutputDeviceDisconnected?.Invoke(this, disconnectedName);
                        }
                    }
                    catch { }
                });
            }
            else
            {
                Stop();
                if (!string.IsNullOrEmpty(disconnectedName))
                {
                    OutputDeviceDisconnected?.Invoke(this, disconnectedName);
                }
            }
        }

        public void LoadMidi(MidiFile midiFile)
        {
            int prevVolumeMask = Interlocked.Exchange(ref _dirtyVolumeMask, 0);
            StopAndDetachPlayback(disposePlayback: true);

            for (int i = 0; i < 16; i++)
            {
                Volatile.Write(ref _lastFileChannelVolumes[i], (byte)100);
            }

            if (prevVolumeMask != 0 && !_isPss680)
            {
                lock (_deviceLock)
                {
                    var target = _outputDevice;
                    if (target != null)
                    {
                        for (int ch = 0; ch < 15; ch++)
                        {
                            if ((prevVolumeMask & (1 << ch)) == 0) continue;
                            try
                            {
                                byte userVol = Volatile.Read(ref _channelVolumes[ch]);
                                byte restored = (byte)Math.Clamp((int)Math.Round(100.0 * userVol / 100.0), 0, 127);
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)restored) { Channel = (FourBitNumber)ch });
                            }
                            catch { }
                        }
                    }
                }
            }

            TimeSpan duration = midiFile != null ? (TimeSpan)midiFile.GetDuration<MetricTimeSpan>() : TimeSpan.Zero;

            lock (_stateLock)
            {
                if (_isDisposed) return;
                _currentFile = midiFile;
                _cachedTotalDuration = duration;
                _hasFinished = false;
            }

            if (midiFile != null && HasOutputDevice())
            {
                InitializePlayback();
            }

            NotifyState();
        }

        private bool HasOutputDevice()
        {
            lock (_deviceLock)
            {
                return _outputDevice != null;
            }
        }

        private void StopAndDetachPlayback(bool disposePlayback)
        {
            Playback? oldPlayback = null;
            FilteringOutputDevice? oldFiltering = null;

            lock (_stateLock)
            {
                try { _progressTimer.Stop(); } catch { }
                _hasFinished = false;

                if (disposePlayback)
                {
                    oldPlayback = _playback;
                    oldFiltering = _filteringDevice;
                    _playback = null;
                    _filteringDevice = null;
                }
                else
                {
                    oldPlayback = _playback;
                }
            }

            if (disposePlayback)
            {
                oldFiltering?.Dispose();
                if (oldPlayback != null)
                {
                    oldPlayback.EventPlayed -= OnPlaybackEventPlayed;
                    if (_playbackFinishedHandler != null)
                    {
                        oldPlayback.Finished -= _playbackFinishedHandler;
                    }
                    if (_repeatStartedHandler != null)
                    {
                        oldPlayback.RepeatStarted -= _repeatStartedHandler;
                    }
                    try { oldPlayback.Stop(); } catch { }
                    for (int i = 0; i < 20 && SafeIsRunning(oldPlayback); i++)
                    {
                        Thread.Sleep(5);
                    }
                    Thread.Sleep(15);
                    try { oldPlayback.Dispose(); } catch { }
                }
            }
            else if (oldPlayback != null)
            {
                try
                {
                    oldPlayback.Stop();
                    for (int i = 0; i < 10 && SafeIsRunning(oldPlayback); i++)
                    {
                        Thread.Sleep(5);
                    }
                    oldPlayback.MoveToStart();
                }
                catch { }
            }

            SendPanicNoteOffInternal(forceAllChannels: false);
        }

        private void InitializePlayback()
        {
            lock (_initLock)
            {
                MidiFile? fileSnapshot;
                double speedSnapshot;
                bool loopSnapshot;

                lock (_stateLock)
                {
                    if (_isDisposed || _currentFile == null) return;
                    if (_playback != null) return;
                    fileSnapshot = _currentFile;
                    speedSnapshot = _speed;
                    loopSnapshot = _loop;
                }

                if (!HasOutputDevice()) return;

                try
                {
                    var settings = new PlaybackSettings
                    {
                        ClockSettings = new MidiClockSettings
                        {
                            CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator()
                        }
                    };

                    var newFiltering = new FilteringOutputDevice(this);
                    var newPlayback = fileSnapshot.GetPlayback(newFiltering, settings);
                    newPlayback.TrackProgram = true;
                    newPlayback.TrackControlValue = true;
                    newPlayback.TrackPitchValue = true;
                    newPlayback.TrackNotes = false;
                    newPlayback.Speed = speedSnapshot;
                    newPlayback.Loop = loopSnapshot;
                    newPlayback.EventPlayed += OnPlaybackEventPlayed;

                    _playbackFinishedHandler = (s, e) =>
                    {
                        if (_isDisposed) return;
                        try
                        {
                            lock (_stateLock)
                            {
                                _hasFinished = true;
                                try { _progressTimer.Stop(); } catch { }
                            }
                            SendPanicNoteOffInternal(forceAllChannels: false);
                            NotifyState();
                            PlaybackFinished?.Invoke(this, EventArgs.Empty);
                        }
                        catch { }
                    };

                    _repeatStartedHandler = (s, e) =>
                    {
                        if (_isDisposed) return;
                        try
                        {
                            lock (_stateLock)
                            {
                                _hasFinished = false;
                            }
                            SendPanicNoteOffInternal(forceAllChannels: false);
                            RePrimeInitialControllers();
                        }
                        catch { }
                    };

                    newPlayback.Finished += _playbackFinishedHandler;
                    newPlayback.RepeatStarted += _repeatStartedHandler;

                    Playback? displacedPlayback = null;
                    FilteringOutputDevice? displacedFiltering = null;
                    bool commitFailed = false;

                    lock (_stateLock)
                    {
                        if (_isDisposed || !ReferenceEquals(_currentFile, fileSnapshot))
                        {
                            commitFailed = true;
                        }
                        else
                        {
                            displacedPlayback = _playback;
                            displacedFiltering = _filteringDevice;
                            _playback = newPlayback;
                            _filteringDevice = newFiltering;
                            _hasFinished = false;
                        }
                    }

                    if (commitFailed)
                    {
                        newFiltering.Dispose();
                        newPlayback.EventPlayed -= OnPlaybackEventPlayed;
                        if (_playbackFinishedHandler != null) newPlayback.Finished -= _playbackFinishedHandler;
                        if (_repeatStartedHandler != null) newPlayback.RepeatStarted -= _repeatStartedHandler;
                        try { newPlayback.Stop(); } catch { }
                        Thread.Sleep(15);
                        try { newPlayback.Dispose(); } catch { }
                        return;
                    }

                    displacedFiltering?.Dispose();
                    if (displacedPlayback != null)
                    {
                        displacedPlayback.EventPlayed -= OnPlaybackEventPlayed;
                        if (_playbackFinishedHandler != null) displacedPlayback.Finished -= _playbackFinishedHandler;
                        if (_repeatStartedHandler != null) displacedPlayback.RepeatStarted -= _repeatStartedHandler;
                        try { displacedPlayback.Stop(); } catch { }
                        Thread.Sleep(15);
                        try { displacedPlayback.Dispose(); } catch { }
                    }
                }
                catch { }
            }
        }

        private void RePrimeInitialControllers()
        {
            IOutputDevice? failedDevice = null;

            lock (_deviceLock)
            {
                var target = _outputDevice;
                if (target == null) return;

                bool is680 = _isPss680;

                // Re-prime controllers across all 16 channels to guarantee seamless looping
                for (int ch = 0; ch < 16; ch++)
                {
                    try
                    {
                        var fourBitCh = (FourBitNumber)ch;
                        if (!is680)
                        {
                            byte fileVol = Volatile.Read(ref _lastFileChannelVolumes[ch]);
                            byte userVol = Volatile.Read(ref _channelVolumes[ch]);
                            int scaled = Math.Clamp((int)Math.Round((double)fileVol * userVol / 100.0), 0, 127);

                            target.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)scaled) { Channel = fourBitCh });
                            target.SendEvent(new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)56) { Channel = fourBitCh });
                        }
                        target.SendEvent(new PitchBendEvent(8192) { Channel = fourBitCh });
                    }
                    catch
                    {
                        failedDevice = target;
                        break;
                    }
                }
            }

            if (failedDevice != null)
            {
                HandleDeviceDisconnectedInternal(failedDevice, deferStop: true);
            }
        }

        private void OnPlaybackEventPlayed(object? sender, MidiEventPlayedEventArgs e)
        {
            if (_isDisposed) return;
            try
            {
                if (e.Event is not ChannelEvent channelEvent) return;

                int ch = channelEvent.Channel;
                int soloMask = Volatile.Read(ref _soloMask);
                int muteMask = Volatile.Read(ref _muteMask);
                bool isQuad = Volatile.Read(ref _vectorQuadPlaybackEnabled) != 0 && ch == 0;
                bool is680 = _isPss680;

                if (channelEvent is NoteOnEvent noteOn)
                {
                    byte noteNum = (byte)noteOn.NoteNumber;
                    byte vel = (byte)noteOn.Velocity;

                    if (vel > 0)
                    {
                        bool chMuted = IsChannelMuted(ch, muteMask, soloMask) ||
                                       (is680 && Volatile.Read(ref _channelVolumes[ch]) == 0);

                        bool q1Active = false, q2Active = false, q3Active = false;

                        lock (_notesLock)
                        {
                            if (!chMuted)
                            {
                                IncrementActiveNoteLocked(ch, noteNum);
                            }

                            if (isQuad)
                            {
                                for (int q = 1; q <= 3; q++)
                                {
                                    bool qMuted = IsChannelMuted(q, muteMask, soloMask) ||
                                                  (is680 && Volatile.Read(ref _channelVolumes[q]) == 0);
                                    if (!qMuted)
                                    {
                                        IncrementActiveNoteLocked(q, noteNum);
                                        if (q == 1) q1Active = true;
                                        else if (q == 2) q2Active = true;
                                        else q3Active = true;
                                    }
                                }
                            }
                        }

                        if (!chMuted)
                        {
                            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                            {
                                Channel = ch,
                                IsActive = true,
                                Velocity = vel,
                                NoteNumber = noteNum
                            });
                        }

                        if (q1Active)
                        {
                            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs { Channel = 1, IsActive = true, Velocity = vel, NoteNumber = noteNum });
                        }
                        if (q2Active)
                        {
                            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs { Channel = 2, IsActive = true, Velocity = vel, NoteNumber = noteNum });
                        }
                        if (q3Active)
                        {
                            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs { Channel = 3, IsActive = true, Velocity = vel, NoteNumber = noteNum });
                        }
                    }
                    else
                    {
                        HandleNoteOffActivity(ch, noteNum, isQuad);
                    }
                }
                else if (channelEvent is NoteOffEvent noteOff)
                {
                    HandleNoteOffActivity(ch, (byte)noteOff.NoteNumber, isQuad);
                }
            }
            catch { }
        }

        private void IncrementActiveNoteLocked(int ch, byte noteNum)
        {
            var key = (ch, noteNum);
            if (_activeNotes.TryGetValue(key, out int count))
            {
                _activeNotes[key] = count + 1;
            }
            else
            {
                _activeNotes[key] = 1;
                _activeNoteCounts[ch]++;
            }
        }

        private bool DecrementActiveNoteLocked(int ch, byte noteNum)
        {
            var key = (ch, noteNum);
            if (_activeNotes.TryGetValue(key, out int count))
            {
                if (count <= 1)
                {
                    _activeNotes.Remove(key);
                    _activeNoteCounts[ch] = Math.Max(0, _activeNoteCounts[ch] - 1);
                }
                else
                {
                    _activeNotes[key] = count - 1;
                }
            }
            return _activeNoteCounts[ch] == 0;
        }

        private void HandleNoteOffActivity(int ch, byte noteNum, bool isQuad)
        {
            if (_isDisposed) return;
            try
            {
                bool chInactive;
                bool q1Inactive = false;
                bool q2Inactive = false;
                bool q3Inactive = false;

                lock (_notesLock)
                {
                    chInactive = DecrementActiveNoteLocked(ch, noteNum);

                    if (isQuad)
                    {
                        q1Inactive = DecrementActiveNoteLocked(1, noteNum);
                        q2Inactive = DecrementActiveNoteLocked(2, noteNum);
                        q3Inactive = DecrementActiveNoteLocked(3, noteNum);
                    }
                }

                if (chInactive)
                {
                    ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                    {
                        Channel = ch,
                        IsActive = false,
                        Velocity = 0,
                        NoteNumber = noteNum
                    });
                }

                if (isQuad)
                {
                    if (q1Inactive)
                    {
                        ChannelActivity?.Invoke(this, new ChannelActivityEventArgs { Channel = 1, IsActive = false, Velocity = 0, NoteNumber = noteNum });
                    }
                    if (q2Inactive)
                    {
                        ChannelActivity?.Invoke(this, new ChannelActivityEventArgs { Channel = 2, IsActive = false, Velocity = 0, NoteNumber = noteNum });
                    }
                    if (q3Inactive)
                    {
                        ChannelActivity?.Invoke(this, new ChannelActivityEventArgs { Channel = 3, IsActive = false, Velocity = 0, NoteNumber = noteNum });
                    }
                }
            }
            catch { }
        }

        private static bool IsChannelMuted(int ch, int muteMask, int soloMask)
        {
            int bit = 1 << ch;
            return ((muteMask & bit) != 0) || (soloMask != 0 && ((soloMask & bit) == 0));
        }

        public void Play()
        {
            bool needsInit;
            lock (_stateLock)
            {
                if (_isDisposed) return;
                needsInit = _playback == null && _currentFile != null;
            }

            if (needsInit && HasOutputDevice())
            {
                InitializePlayback();
            }

            Playback? p;
            bool restartFromBeginning;
            lock (_stateLock)
            {
                p = _playback;
                restartFromBeginning = _hasFinished;
                if (p != null)
                {
                    _hasFinished = false;
                }
            }

            if (p != null)
            {
                try
                {
                    if (!SafeIsRunning(p))
                    {
                        if (restartFromBeginning)
                        {
                            p.MoveToStart();
                        }
                        p.Start();
                        try { _progressTimer.Start(); } catch { }
                        NotifyState();
                    }
                }
                catch { }
            }
        }

        public void Pause()
        {
            Playback? p;
            lock (_stateLock)
            {
                p = _playback;
                try { _progressTimer.Stop(); } catch { }
            }

            if (p != null)
            {
                bool wasRunning = false;
                try
                {
                    wasRunning = SafeIsRunning(p);
                    if (wasRunning)
                    {
                        p.Stop();
                    }
                }
                catch { }

                if (wasRunning)
                {
                    // Kill active sounding notes without resetting Pitch Bend / Modulation so Resume stays expressive
                    SendActiveNotesOffFast();
                    NotifyState();
                }
            }
        }

        public void Stop()
        {
            StopAndDetachPlayback(disposePlayback: false);
            NotifyState();
        }

        public void Seek(TimeSpan position)
        {
            Playback? p;
            lock (_stateLock)
            {
                p = _playback;
                _hasFinished = false;
            }

            // Use fast targeted active-note silencing during scrubbing/seeking so serial MIDI
            // buses (31.25 kbps) and the UI bridge don't stall on 96-event full controller resets.
            SendActiveNotesOffFast();

            if (p != null)
            {
                try
                {
                    var clampedPos = position < TimeSpan.Zero ? TimeSpan.Zero : position;
                    p.MoveToTime(new MetricTimeSpan(clampedPos));
                }
                catch { }
            }

            NotifyState();
        }

        public void SetSpeed(double speed)
        {
            if (speed <= 0.1 || speed >= 3.0) return;

            Playback? p;
            lock (_stateLock)
            {
                _speed = speed;
                p = _playback;
            }

            if (p != null)
            {
                try { p.Speed = speed; } catch (ObjectDisposedException) { }
            }
        }

        public void SetLoop(bool loop)
        {
            Playback? p;
            lock (_stateLock)
            {
                _loop = loop;
                p = _playback;
            }

            if (p != null)
            {
                try { p.Loop = loop; } catch (ObjectDisposedException) { }
            }
        }

        public void SendEvent(MidiEvent midiEvent)
        {
            if (midiEvent == null) return;
            IOutputDevice? failedDevice = null;
            bool sent = false;

            lock (_deviceLock)
            {
                var target = _outputDevice;
                if (target == null) return;

                try
                {
                    target.SendEvent(midiEvent);
                    sent = true;
                }
                catch
                {
                    failedDevice = target;
                }
            }

            if (sent)
            {
                TrackLiveEventState(midiEvent);
            }

            if (failedDevice != null)
            {
                HandleDeviceDisconnectedInternal(failedDevice, deferStop: false);
            }
        }

        private void TrackLiveEventState(MidiEvent midiEvent)
        {
            if (midiEvent is not ChannelEvent ce) return;

            int ch = ce.Channel;
            TrackDirtyControllerState(this, ch, ce);

            if (ce is NoteOnEvent noteOn)
            {
                lock (_notesLock)
                {
                    if (noteOn.Velocity > 0)
                        IncrementActiveNoteLocked(ch, (byte)noteOn.NoteNumber);
                    else
                        DecrementActiveNoteLocked(ch, (byte)noteOn.NoteNumber);
                }
            }
            else if (ce is NoteOffEvent noteOff)
            {
                lock (_notesLock)
                {
                    DecrementActiveNoteLocked(ch, (byte)noteOff.NoteNumber);
                }
            }
        }

        private static void TrackDirtyControllerState(MidiPlayer player, int ch, ChannelEvent evt)
        {
            if (ch < 0 || ch >= 16) return;
            int bit = 1 << ch;
            SetMaskBit(ref player._usedChannelMask, bit);

            if (evt is PitchBendEvent)
            {
                SetMaskBit(ref player._dirtyBendMask, bit);
            }
            else if (evt is ControlChangeEvent cc)
            {
                if (cc.ControlNumber == 1) SetMaskBit(ref player._dirtyModMask, bit);
                else if (cc.ControlNumber == 64) SetMaskBit(ref player._dirtySustainMask, bit);
                else if (cc.ControlNumber == 7) SetMaskBit(ref player._dirtyVolumeMask, bit);
            }
        }

        private static void SetMaskBit(ref int field, int bit)
        {
            int initial, computed;
            do
            {
                initial = Volatile.Read(ref field);
                if ((initial & bit) == bit) return;
                computed = initial | bit;
            }
            while (Interlocked.CompareExchange(ref field, computed, initial) != initial);
        }

        public void SendSysEx(byte[] rawSysEx)
        {
            if (rawSysEx == null || rawSysEx.Length == 0) return;

            // DryWetMIDI NormalSysExEvent.Data EXCLUDES leading 0xF0 and MUST INCLUDE trailing 0xF7 (Completed == true)
            int start = (rawSysEx[0] == 0xF0) ? 1 : 0;
            bool hasF7 = (rawSysEx.Length > start && rawSysEx[^1] == 0xF7);
            int dataLen = rawSysEx.Length - start;

            byte[] dataPayload;
            if (hasF7)
            {
                dataPayload = new byte[dataLen];
                Buffer.BlockCopy(rawSysEx, start, dataPayload, 0, dataLen);
            }
            else
            {
                dataPayload = new byte[dataLen + 1];
                Buffer.BlockCopy(rawSysEx, start, dataPayload, 0, dataLen);
                dataPayload[^1] = 0xF7;
            }

            SendEvent(new NormalSysExEvent(dataPayload));
        }

        public void SendVoiceOverride(int channel, int voiceNumber, string? model = null)
        {
            if (channel < 0 || channel > 14) return; // Ch 15/16 is drum, ignores PC

            var effectiveModel = model ?? ActiveModel;
            bool is680 = string.Equals(effectiveModel, "pss680", StringComparison.OrdinalIgnoreCase);

            byte programNumber;
            if (is680)
            {
                programNumber = (byte)Math.Clamp(voiceNumber, 0, 104);
            }
            else if (Constants.Pss790Voices.TryGetValue(voiceNumber, out var voiceInfo))
            {
                programNumber = voiceInfo.MidiProgram;
            }
            else
            {
                return;
            }

            var pc = new ProgramChangeEvent((SevenBitNumber)programNumber)
            {
                Channel = (FourBitNumber)channel
            };

            SendEvent(pc);
        }

        public void SetChannelVolume(int channel, byte volume)
        {
            if (channel < 0 || channel >= 16) return;

            Volatile.Write(ref _channelVolumes[channel], volume);

            // PSS-680 ignores CC 7 in Mode 99; live mixer scales Note-On velocities in FilteringOutputDevice
            if (_isPss680) return;

            byte fileVol = Volatile.Read(ref _lastFileChannelVolumes[channel]);
            byte scaled = (byte)Math.Clamp((int)Math.Round((double)fileVol * volume / 100.0), 0, 127);

            var cc = new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)scaled)
            {
                Channel = (FourBitNumber)channel
            };

            SendEvent(cc);
        }

        public void SetChannelMute(int channel, bool isMuted)
        {
            if (channel < 0 || channel >= 16) return;

            lock (_stateLock)
            {
                _channelMutes[channel] = isMuted;
                int mask = Volatile.Read(ref _muteMask);
                mask = isMuted ? (mask | (1 << channel)) : (mask & ~(1 << channel));
                Volatile.Write(ref _muteMask, mask);
            }

            if (isMuted)
            {
                SendChannelNoteOff(channel);
            }
        }

        public void SetChannelSolo(int channel, bool isSolo)
        {
            if (channel < 0 || channel >= 16) return;

            List<int> channelsToSilence = new(16);
            lock (_stateLock)
            {
                _channelSolos[channel] = isSolo;
                int soloMask = Volatile.Read(ref _soloMask);
                soloMask = isSolo ? (soloMask | (1 << channel)) : (soloMask & ~(1 << channel));
                Volatile.Write(ref _soloMask, soloMask);

                int muteMask = Volatile.Read(ref _muteMask);
                for (int ch = 0; ch < 16; ch++)
                {
                    if (IsChannelMuted(ch, muteMask, soloMask))
                    {
                        channelsToSilence.Add(ch);
                    }
                }
            }

            foreach (var ch in channelsToSilence)
            {
                SendChannelNoteOff(ch);
            }
        }

        public void ClearAllSolo()
        {
            lock (_stateLock)
            {
                Array.Clear(_channelSolos, 0, 16);
                Volatile.Write(ref _soloMask, 0);
            }
        }

        public void SendActiveNotesOffFast()
        {
            List<(int Channel, byte Note)> notesToKill;
            bool[] activeChannels = new bool[16];
            lock (_notesLock)
            {
                notesToKill = new List<(int Channel, byte Note)>(_activeNotes.Count);
                foreach (var key in _activeNotes.Keys)
                {
                    notesToKill.Add(key);
                    if (key.Channel >= 0 && key.Channel < 16)
                        activeChannels[key.Channel] = true;
                }
                _activeNotes.Clear();
                Array.Clear(_activeNoteCounts, 0, 16);
            }

            lock (_deviceLock)
            {
                var target = _outputDevice;
                if (target != null)
                {
                    foreach (var (ch, note) in notesToKill)
                    {
                        try
                        {
                            target.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0)
                            {
                                Channel = (FourBitNumber)ch
                            });
                        }
                        catch { }
                    }

                    for (int ch = 0; ch < 16; ch++)
                    {
                        if (!activeChannels[ch]) continue;
                        try
                        {
                            target.SendEvent(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0)
                            {
                                Channel = (FourBitNumber)ch
                            });
                        }
                        catch { }
                    }
                }
            }

            for (int ch = 0; ch < 16; ch++)
            {
                if (!activeChannels[ch]) continue;
                ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                {
                    Channel = ch,
                    IsActive = false,
                    Velocity = 0
                });
            }
        }

        public void SendPanicNoteOff() => SendPanicNoteOffInternal(forceAllChannels: true);

        private void SendPanicNoteOffInternal(bool forceAllChannels)
        {
            List<(int Channel, byte Note)> notesToKill;
            lock (_notesLock)
            {
                notesToKill = new List<(int Channel, byte Note)>(_activeNotes.Count);
                foreach (var key in _activeNotes.Keys)
                {
                    notesToKill.Add(key);
                }
                _activeNotes.Clear();
                Array.Clear(_activeNoteCounts, 0, 16);
            }

            int usedMask = Interlocked.Exchange(ref _usedChannelMask, 0);
            int bendMask = Interlocked.Exchange(ref _dirtyBendMask, 0);
            int modMask = Interlocked.Exchange(ref _dirtyModMask, 0);
            int sustainMask = Interlocked.Exchange(ref _dirtySustainMask, 0);
            if (forceAllChannels) Interlocked.Exchange(ref _dirtyVolumeMask, 0);

            foreach (var (ch, _) in notesToKill)
            {
                if (ch >= 0 && ch < 16) usedMask |= (1 << ch);
            }

            lock (_deviceLock)
            {
                var target = _outputDevice;
                if (target != null)
                {
                    // 1. Explicit Note-Off for currently tracked sounding notes
                    foreach (var (ch, note) in notesToKill)
                    {
                        try
                        {
                            target.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0)
                            {
                                Channel = (FourBitNumber)ch
                            });
                        }
                        catch { }
                    }

                    // 2. Targeted hardware controller cleanup across active/dirty channels (or all 16 on manual Panic)
                    for (int ch = 0; ch < 16; ch++)
                    {
                        int bit = 1 << ch;
                        bool sendAllNotesOff = forceAllChannels || (usedMask & bit) != 0;
                        bool sendMod = forceAllChannels || (ch < 15 && (modMask & bit) != 0);
                        bool sendBend = forceAllChannels || (ch < 15 && (bendMask & bit) != 0);
                        bool sendSustain = forceAllChannels || (ch < 15 && (sustainMask & bit) != 0);

                        if (!sendAllNotesOff && !sendMod && !sendBend && !sendSustain)
                        {
                            continue;
                        }

                        try
                        {
                            var fourBitCh = (FourBitNumber)ch;
                            if (sendAllNotesOff)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)120, (SevenBitNumber)0) { Channel = fourBitCh }); // All Sound Off
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0) { Channel = fourBitCh }); // All Notes Off
                            }
                            if (sendSustain)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = fourBitCh });  // Sustain Off
                            }
                            if (forceAllChannels)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)121, (SevenBitNumber)0) { Channel = fourBitCh }); // Reset All Controllers
                            }
                            if (sendMod)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)0) { Channel = fourBitCh });   // Modulation Off
                            }
                            if (sendBend)
                            {
                                target.SendEvent(new PitchBendEvent(8192) { Channel = fourBitCh });                                       // Pitch Bend Center
                            }
                        }
                        catch { }
                    }
                }
            }

            // 3. Clear all 16 channel activity indicators outside locks
            for (int ch = 0; ch < 16; ch++)
            {
                ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                {
                    Channel = ch,
                    IsActive = false,
                    Velocity = 0
                });
            }
        }

        private void SendChannelNoteOff(int channel)
        {
            List<byte> notesOnChannel = new();
            lock (_notesLock)
            {
                foreach (var key in _activeNotes.Keys)
                {
                    if (key.Channel == channel)
                    {
                        notesOnChannel.Add(key.Note);
                    }
                }
                foreach (var note in notesOnChannel)
                {
                    _activeNotes.Remove((channel, note));
                }
                _activeNoteCounts[channel] = 0;
            }

            lock (_deviceLock)
            {
                var target = _outputDevice;
                if (target != null)
                {
                    foreach (var note in notesOnChannel)
                    {
                        try
                        {
                            target.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0)
                            {
                                Channel = (FourBitNumber)channel
                            });
                        }
                        catch { }
                    }

                    try
                    {
                        var ch = (FourBitNumber)channel;
                        target.SendEvent(new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0) { Channel = ch });
                        target.SendEvent(new ControlChangeEvent((SevenBitNumber)120, (SevenBitNumber)0) { Channel = ch });
                        target.SendEvent(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = ch });
                    }
                    catch { }
                }
            }

            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
            {
                Channel = channel,
                IsActive = false,
                Velocity = 0
            });
        }

        private PlaybackStateEventArgs CaptureStateSnapshot()
        {
            Playback? p;
            TimeSpan totalDuration;
            bool hasFinished;

            lock (_stateLock)
            {
                p = _playback;
                totalDuration = _cachedTotalDuration;
                hasFinished = _hasFinished;
            }

            bool isPlaying = false;
            TimeSpan currentPos = TimeSpan.Zero;

            if (p != null)
            {
                try
                {
                    isPlaying = SafeIsRunning(p);
                    currentPos = p.GetCurrentTime<MetricTimeSpan>();
                }
                catch (Exception)
                {
                    isPlaying = false;
                    currentPos = TimeSpan.Zero;
                }
            }

            return new PlaybackStateEventArgs
            {
                IsPlaying = isPlaying,
                IsPaused = !isPlaying && !hasFinished && currentPos.TotalMilliseconds > 0,
                CurrentTime = currentPos,
                TotalTime = totalDuration
            };
        }

        private void NotifyState()
        {
            if (_isDisposed) return;
            try
            {
                PlaybackStateChanged?.Invoke(this, CaptureStateSnapshot());
            }
            catch { }
        }

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }

            try
            {
                _progressTimer.Stop();
                _progressTimer.Dispose();
            }
            catch { }

            StopAndDetachPlayback(disposePlayback: true);

            IOutputDevice? oldDevice;
            lock (_deviceLock)
            {
                oldDevice = _outputDevice;
                _outputDevice = null;
                _currentOutputDeviceName = null;
            }

            try { oldDevice?.Dispose(); } catch { }

            GC.SuppressFinalize(this);
        }

        private sealed class FilteringOutputDevice : IOutputDevice, IDisposable
        {
            private readonly MidiPlayer _player;
            private volatile bool _isDisposed;

            public event EventHandler<MidiEventSentEventArgs>? EventSent;

            public FilteringOutputDevice(MidiPlayer player)
            {
                _player = player;
            }

            public void Dispose()
            {
                _isDisposed = true;
            }

            public void SendEvent(MidiEvent midiEvent)
            {
                if (_isDisposed) return;

                try
                {
                    if (midiEvent is ChannelEvent channelEvent)
                    {
                        int ch = channelEvent.Channel;
                        TrackDirtyControllerState(_player, ch, channelEvent);
                        int soloMask = Volatile.Read(ref _player._soloMask);
                        int muteMask = Volatile.Read(ref _player._muteMask);
                        bool isMuted = IsChannelMuted(ch, muteMask, soloMask);
                        bool isNoteOnAudible = channelEvent is NoteOnEvent noteOnCheck && noteOnCheck.Velocity > 0;
                        bool isQuad = Volatile.Read(ref _player._vectorQuadPlaybackEnabled) != 0 &&
                                      ch == 0 &&
                                      (channelEvent is NoteOnEvent or NoteOffEvent or PitchBendEvent ||
                                       (channelEvent is ControlChangeEvent qCc && (qCc.ControlNumber == 1 || qCc.ControlNumber == 7 || qCc.ControlNumber == 91)));

                        // Vector Quad Layering & Replication (PSS-790):
                        // Handled BEFORE single-channel mute check so soloing Voices B/C/D (Ch 1-3) still receives notes/expression when Ch 0 is muted.
                        if (isQuad)
                        {
                            SendVectorQuadEvent(channelEvent, isMuted, isNoteOnAudible, muteMask, soloMask);
                            return;
                        }

                        // NEVER suppress NoteOffEvent or zero-velocity NoteOnEvent on muted channels.
                        if (isMuted && isNoteOnAudible)
                        {
                            return;
                        }

                        bool is680 = _player._isPss680;

                        // PSS-680 Live Mixer Volume: scales Note-On velocity in real time (0% suppresses Note-On completely).
                        if (is680 && channelEvent is NoteOnEvent pss680NoteOn && pss680NoteOn.Velocity > 0)
                        {
                            byte userVol = Volatile.Read(ref _player._channelVolumes[ch]);
                            if (userVol == 0)
                            {
                                return;
                            }
                            if (userVol != 100)
                            {
                                int scaledV = Math.Clamp((int)Math.Round((byte)pss680NoteOn.Velocity * (userVol / 100.0)), 1, 127);
                                channelEvent = new NoteOnEvent(pss680NoteOn.NoteNumber, (SevenBitNumber)scaledV) { Channel = pss680NoteOn.Channel };
                                midiEvent = channelEvent;
                            }
                        }

                        // Proportional Mixer Scaling for CC 7 (Volume)
                        if (channelEvent is ControlChangeEvent cc && cc.ControlNumber == 7)
                        {
                            Volatile.Write(ref _player._lastFileChannelVolumes[ch], (byte)cc.ControlValue);
                            if (is680)
                            {
                                // PSS-680 ignores CC 7 in Mode 99; omit from wire to conserve 31.25 kbps MIDI bandwidth
                                return;
                            }

                            byte userVolume = Volatile.Read(ref _player._channelVolumes[ch]);
                            int scaled = Math.Clamp((int)Math.Round((double)cc.ControlValue * userVolume / 100.0), 0, 127);

                            midiEvent = new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)scaled)
                            {
                                Channel = cc.Channel
                            };
                        }
                    }

                    TransmitSingle(midiEvent);
                }
                catch { }
            }

            private void SendVectorQuadEvent(ChannelEvent baseEvent, bool ch0Muted, bool isNoteOnAudible, int muteMask, int soloMask)
            {
                bool is680 = _player._isPss680;
                List<MidiEvent> eventsToSend = new(4);

                ChannelEvent? BuildQuadVariant(byte qCh)
                {
                    if (baseEvent is NoteOnEvent nOn)
                    {
                        var ev = new NoteOnEvent(nOn.NoteNumber, nOn.Velocity) { Channel = (FourBitNumber)qCh };
                        return TryApplyPss680Scale(ev, qCh, is680, out var scaled) ? scaled : null;
                    }
                    if (baseEvent is NoteOffEvent nOff)
                    {
                        return new NoteOffEvent(nOff.NoteNumber, nOff.Velocity) { Channel = (FourBitNumber)qCh };
                    }
                    if (baseEvent is PitchBendEvent pb)
                    {
                        TrackDirtyControllerState(_player, qCh, baseEvent);
                        return new PitchBendEvent(pb.PitchValue) { Channel = (FourBitNumber)qCh };
                    }
                    if (baseEvent is ControlChangeEvent cc)
                    {
                        TrackDirtyControllerState(_player, qCh, baseEvent);
                        if (cc.ControlNumber == 7)
                        {
                            Volatile.Write(ref _player._lastFileChannelVolumes[qCh], (byte)cc.ControlValue);
                            if (is680) return null;
                            byte userVol = Volatile.Read(ref _player._channelVolumes[qCh]);
                            int scaled = Math.Clamp((int)Math.Round((double)cc.ControlValue * userVol / 100.0), 0, 127);
                            return new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)scaled) { Channel = (FourBitNumber)qCh };
                        }
                        return new ControlChangeEvent(cc.ControlNumber, cc.ControlValue) { Channel = (FourBitNumber)qCh };
                    }
                    return null;
                }

                if (!ch0Muted || !isNoteOnAudible)
                {
                    var ev0 = BuildQuadVariant(0);
                    if (ev0 != null) eventsToSend.Add(ev0);
                }

                for (byte qCh = 1; qCh <= 3; qCh++)
                {
                    bool qMuted = IsChannelMuted(qCh, muteMask, soloMask);
                    if (qMuted && isNoteOnAudible) continue;

                    var evQ = BuildQuadVariant(qCh);
                    if (evQ != null) eventsToSend.Add(evQ);
                }

                if (eventsToSend.Count == 0) return;

                IOutputDevice? failedDevice = null;
                int sentCount = 0;

                lock (_player._deviceLock)
                {
                    var target = _player._outputDevice;
                    if (target == null || _isDisposed) return;

                    try
                    {
                        for (int i = 0; i < eventsToSend.Count; i++)
                        {
                            target.SendEvent(eventsToSend[i]);
                            sentCount++;
                        }
                    }
                    catch
                    {
                        failedDevice = target;
                    }
                }

                for (int i = 0; i < sentCount; i++)
                {
                    try { EventSent?.Invoke(this, new MidiEventSentEventArgs(eventsToSend[i])); } catch { }
                }

                if (failedDevice != null)
                {
                    _player.HandleDeviceDisconnectedInternal(failedDevice, deferStop: true);
                }
            }

            private bool TryApplyPss680Scale(ChannelEvent evt, int ch, bool is680, out ChannelEvent result)
            {
                if (is680 && evt is NoteOnEvent noteOn && noteOn.Velocity > 0)
                {
                    byte userVol = Volatile.Read(ref _player._channelVolumes[ch]);
                    if (userVol == 0)
                    {
                        result = evt;
                        return false;
                    }
                    if (userVol != 100)
                    {
                        int scaledV = Math.Clamp((int)Math.Round((byte)noteOn.Velocity * (userVol / 100.0)), 1, 127);
                        result = new NoteOnEvent(noteOn.NoteNumber, (SevenBitNumber)scaledV) { Channel = noteOn.Channel };
                        return true;
                    }
                }

                result = evt;
                return true;
            }

            private void TransmitSingle(MidiEvent midiEvent)
            {
                IOutputDevice? failedDevice = null;
                bool sent = false;

                lock (_player._deviceLock)
                {
                    var target = _player._outputDevice;
                    if (target == null || _isDisposed) return;

                    try
                    {
                        target.SendEvent(midiEvent);
                        sent = true;
                    }
                    catch
                    {
                        failedDevice = target;
                    }
                }

                if (sent)
                {
                    try { EventSent?.Invoke(this, new MidiEventSentEventArgs(midiEvent)); } catch { }
                }

                if (failedDevice != null)
                {
                    _player.HandleDeviceDisconnectedInternal(failedDevice, deferStop: true);
                }
            }

            public void PrepareForEventsSending()
            {
                try
                {
                    lock (_player._deviceLock)
                    {
                        _player._outputDevice?.PrepareForEventsSending();
                    }
                }
                catch { }
            }
        }
    }
}
