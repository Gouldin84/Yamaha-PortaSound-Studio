using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private TimeSpan _lastKnownPosition = TimeSpan.Zero;
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
        private readonly object _transportLock = new();
        private readonly TransportActor _actor = new("MidiTransportActor");

        private int _stopEpoch;
        private int _playbackSessionEpoch;

        private readonly Dictionary<(int Channel, byte Note), int> _activeNotes = new();
        private readonly int[] _activeNoteCounts = new int[16];

        private readonly bool[] _channelMutes = new bool[16];
        private readonly bool[] _channelSolos = new bool[16];
        private readonly byte[] _channelVolumes = new byte[16];
        private readonly byte[] _lastFileChannelVolumes = new byte[16];
        private readonly byte[] _lastPss680Modulation = new byte[16];
        private readonly long[] _playedNoteMask = new long[32]; // Lock-free 16ch x 128-note unclosed hardware TX bitmap
        private readonly byte[] _hwActiveNoteCount = new byte[16 * 128]; // Protected strictly by _deviceLock
        private int _stickyPss680ChannelMask;

        // Lock-free atomic state for real-time audio path filtering
        private int _muteMask;
        private int _soloMask;
        private int _usedChannelMask;
        private int _dirtyBendMask;
        private int _dirtyModMask;
        private int _dirtySustainMask;
        private int _dirtyVolumeMask;
        private volatile bool _isPss680;
        private volatile bool _stripHardwareCc10;
        private string _activeModel = "pss790";
        private int _vectorQuadPlaybackEnabled;

        // One-shot cold-start setup deduplication so Playback.Start() does not immediately
        // re-blast the identical Tick-0 ProgramChange / CC wave already sent by PrimeInitialVoicesForHardwareStart.
        private int _primedTickZeroPcMask;
        private readonly byte[] _primedTickZeroPcValues = new byte[16];
        private long _primedTickZeroCcMask;
        private readonly byte[] _primedTickZeroCcValues = new byte[64];
        private long _primedDedupExpiryTimestamp;

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
        public int StopGeneration => Volatile.Read(ref _stopEpoch);

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
                lock (_deviceLock)
                {
                    UpdateHardwareCc10FilterLocked();
                }
            }
        }

        private void UpdateHardwareCc10FilterLocked()
        {
            _stripHardwareCc10 = !_isPss680 &&
                                 _outputDevice is OutputDevice &&
                                 !Constants.IsMicrosoftGsWavetableSynth(_currentOutputDeviceName);
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
                    int epochBefore = Volatile.Read(ref _playbackSessionEpoch);
                    var snapshot = CaptureStateSnapshot();
                    if (snapshot.IsPlaying && Volatile.Read(ref _playbackSessionEpoch) == epochBefore)
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

        public void SetOutputDevice(string? deviceName) => _actor.Invoke(() => SetOutputDeviceByNameInternal(deviceName));

        public Task SetOutputDeviceAsync(string? deviceName) => _actor.InvokeAsync(() => SetOutputDeviceByNameInternal(deviceName));

        private void SetOutputDeviceByNameInternal(string? deviceName)
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

        public void SetOutputDevice(IOutputDevice? outputDevice) => _actor.Invoke(() =>
        {
            string? resolvedName = outputDevice is OutputDevice concrete ? concrete.Name : null;
            SetOutputDeviceInternal(outputDevice, resolvedName);
        });

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
                UpdateHardwareCc10FilterLocked();
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

        public void HandleDeviceDisconnected() =>
            _actor.Invoke(() => HandleDeviceDisconnectedInternal(expectedDevice: null, deferStop: false));

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
                UpdateHardwareCc10FilterLocked();
            }

            if (oldDevice != null)
            {
                try { oldDevice.Dispose(); } catch { }
            }

            if (deferStop)
            {
                // Post asynchronously onto the single-writer actor so DryWetMIDI's callback thread never blocks
                _actor.Post(() =>
                {
                    try
                    {
                        StopInternal();
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
                StopInternal();
                if (!string.IsNullOrEmpty(disconnectedName))
                {
                    OutputDeviceDisconnected?.Invoke(this, disconnectedName);
                }
            }
        }

        public void LoadMidi(MidiFile midiFile) => _actor.Invoke(() => LoadMidiInternal(midiFile));

        public Task LoadMidiAsync(MidiFile midiFile) => _actor.InvokeAsync(() => LoadMidiInternal(midiFile));

        private void LoadMidiInternal(MidiFile midiFile)
        {
            Interlocked.Increment(ref _stopEpoch);
            Interlocked.Increment(ref _playbackSessionEpoch);
            ClearPrimedSetupDedup();
            int prevVolumeMask = Interlocked.Exchange(ref _dirtyVolumeMask, 0);
            StopAndDetachPlayback(disposePlayback: true, forceFullPanic: false);

            for (int i = 0; i < 16; i++)
            {
                Volatile.Write(ref _lastFileChannelVolumes[i], (byte)100);
                Volatile.Write(ref _lastPss680Modulation[i], (byte)0);
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
                _lastKnownPosition = TimeSpan.Zero;
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

        private void StopAndDetachPlayback(bool disposePlayback, bool forceFullPanic = false)
        {
            ClearPrimedSetupDedup();
            Playback? oldPlayback = null;
            FilteringOutputDevice? oldFiltering = null;

            lock (_transportLock)
            {
                lock (_stateLock)
                {
                    try { _progressTimer.Stop(); } catch { }
                    _lastKnownPosition = TimeSpan.Zero;
                    _hasFinished = false;

                    oldPlayback = _playback;
                    oldFiltering = _filteringDevice;

                    if (disposePlayback)
                    {
                        _playback = null;
                        _filteringDevice = null;
                    }
                }

                // Gate all playback output BEFORE stopping the clock so in-flight tick callbacks cannot leak late Note-Ons
                oldFiltering?.SuppressOutput();
                if (disposePlayback)
                {
                    oldFiltering?.Dispose();
                }

                if (oldPlayback != null)
                {
                    if (disposePlayback)
                    {
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
                        oldPlayback.EventPlayed -= OnPlaybackEventPlayed;
                        Thread.Sleep(15);
                        try { oldPlayback.Dispose(); } catch { }
                    }
                    else
                    {
                        try
                        {
                            oldPlayback.Stop();
                            for (int i = 0; i < 20 && SafeIsRunning(oldPlayback); i++)
                            {
                                Thread.Sleep(5);
                            }
                            oldPlayback.MoveToStart();
                        }
                        catch { }
                    }
                }
            }

            SendPanicNoteOffInternal(forceAllChannels: forceFullPanic);
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
                        _actor.Post(() =>
                        {
                            if (_isDisposed) return;
                            try
                            {
                                lock (_stateLock)
                                {
                                    if (!ReferenceEquals(_playback, newPlayback)) return;
                                    _hasFinished = true;
                                    try { _progressTimer.Stop(); } catch { }
                                }
                                Interlocked.Increment(ref _playbackSessionEpoch);
                                SendPanicNoteOffInternal(forceAllChannels: false);
                                NotifyState();
                                PlaybackFinished?.Invoke(this, EventArgs.Empty);
                            }
                            catch { }
                        });
                    };

                    _repeatStartedHandler = (s, e) =>
                    {
                        if (_isDisposed) return;
                        _actor.Post(() =>
                        {
                            if (_isDisposed) return;
                            try
                            {
                                lock (_stateLock)
                                {
                                    if (!ReferenceEquals(_playback, newPlayback)) return;
                                    _hasFinished = false;
                                }
                                ClearPrimedSetupDedup();
                                SendPanicNoteOffInternal(forceAllChannels: false);
                                RePrimeInitialControllers();
                            }
                            catch { }
                        });
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

        public void Play() => _actor.Invoke(PlayInternal);

        public Task PlayAsync() => _actor.InvokeAsync(PlayInternal);

        private void PlayInternal()
        {
            int stopEpoch = Volatile.Read(ref _stopEpoch);
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

            if (_isDisposed || Volatile.Read(ref _stopEpoch) != stopEpoch)
            {
                NotifyState();
                return;
            }

            Playback? p;
            MidiFile? fileSnapshot;
            bool restartFromBeginning;
            lock (_stateLock)
            {
                p = _playback;
                fileSnapshot = _currentFile;
                restartFromBeginning = _hasFinished;
            }

            if (p != null)
            {
                try
                {
                    if (!SafeIsRunning(p))
                    {
                        TimeSpan currentPos = TimeSpan.Zero;
                        try
                        {
                            currentPos = p.GetCurrentTime<MetricTimeSpan>();
                        }
                        catch { }

                        if (restartFromBeginning || currentPos <= TimeSpan.FromMilliseconds(5))
                        {
                            if (restartFromBeginning)
                            {
                                lock (_transportLock)
                                {
                                    if (_isDisposed || !ReferenceEquals(_playback, p) || Volatile.Read(ref _stopEpoch) != stopEpoch)
                                    {
                                        NotifyState();
                                        return;
                                    }
                                    p.MoveToStart();
                                }
                            }

                            if (fileSnapshot != null)
                            {
                                PrimeInitialVoicesForHardwareStart(fileSnapshot, stopEpoch, p);
                            }
                        }
                        else
                        {
                            ClearPrimedSetupDedup();
                        }

                        bool started = false;
                        lock (_transportLock)
                        {
                            if (_isDisposed || Volatile.Read(ref _stopEpoch) != stopEpoch)
                            {
                                NotifyState();
                                return;
                            }

                            FilteringOutputDevice? filtering;
                            lock (_stateLock)
                            {
                                if (_isDisposed || !ReferenceEquals(_playback, p))
                                {
                                    p = null;
                                    filtering = null;
                                }
                                else
                                {
                                    filtering = _filteringDevice;
                                    _hasFinished = false;
                                    if (restartFromBeginning)
                                    {
                                        _lastKnownPosition = TimeSpan.Zero;
                                    }
                                }
                            }

                            if (p != null && !SafeIsRunning(p))
                            {
                                filtering?.AllowOutput();
                                Interlocked.Increment(ref _playbackSessionEpoch);
                                if (Volatile.Read(ref _primedTickZeroPcMask) != 0 || Volatile.Read(ref _primedTickZeroCcMask) != 0)
                                {
                                    Volatile.Write(ref _primedDedupExpiryTimestamp, Stopwatch.GetTimestamp() + (long)(0.25 * Stopwatch.Frequency));
                                }
                                p.Start();
                                started = true;
                            }
                        }

                        if (started)
                        {
                            lock (_stateLock)
                            {
                                if (!_isDisposed && ReferenceEquals(_playback, p) && Volatile.Read(ref _stopEpoch) == stopEpoch)
                                {
                                    try { _progressTimer.Start(); } catch { }
                                }
                            }
                            NotifyState();
                        }
                        else if (p == null)
                        {
                            NotifyState();
                        }
                    }
                }
                catch
                {
                    NotifyState();
                }
            }
            else
            {
                NotifyState();
            }
        }

        private void PrimeInitialVoicesForHardwareStart(MidiFile file, int expectedStopEpoch, Playback expectedPlayback)
        {
            if (_isDisposed || Volatile.Read(ref _stopEpoch) != expectedStopEpoch) return;

            IOutputDevice? target;
            string? devName;
            bool is680;
            lock (_deviceLock)
            {
                target = _outputDevice;
                devName = _currentOutputDeviceName;
                is680 = _isPss680;
            }

            // Only prime & pace on actual hardware/OS WinMM ports (skip mock unit-test devices)
            if (target is not OutputDevice)
            {
                return;
            }

            bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(devName);

            try
            {
                var minNoteTime = new Dictionary<int, long>();
                var initialPcs = new Dictionary<int, (long Time, byte Program)>();
                var initialCcs = new Dictionary<(int Channel, byte ControlNumber), byte>();

                foreach (var track in file.GetTrackChunks())
                {
                    foreach (var te in track.GetTimedEvents())
                    {
                        if (te.Event is NoteOnEvent noteOn && noteOn.Velocity > 0)
                        {
                            int ch = noteOn.Channel;
                            if (!minNoteTime.TryGetValue(ch, out var curMin) || te.Time < curMin)
                            {
                                minNoteTime[ch] = te.Time;
                            }
                        }
                    }
                }

                long setupWindowTicks = 96;
                if (file.TimeDivision is TicksPerQuarterNoteTimeDivision tpqnDiv && tpqnDiv.TicksPerQuarterNote > 0)
                {
                    setupWindowTicks = Math.Max(48, tpqnDiv.TicksPerQuarterNote / 4);
                }

                foreach (var track in file.GetTrackChunks())
                {
                    foreach (var te in track.GetTimedEvents())
                    {
                        if (te.Event is ProgramChangeEvent pc && pc.Channel != 15)
                        {
                            int ch = pc.Channel;
                            long limit = minNoteTime.TryGetValue(ch, out var fn) ? fn + setupWindowTicks : setupWindowTicks;
                            if (te.Time <= limit)
                            {
                                if (!initialPcs.TryGetValue(ch, out var existing) || te.Time < existing.Time)
                                {
                                    initialPcs[ch] = (te.Time, (byte)pc.ProgramNumber);
                                }
                            }
                        }
                        else if (te.Event is ControlChangeEvent cc && te.Time == 0 && cc.Channel != 15 &&
                                 (cc.ControlNumber == 1 || cc.ControlNumber == 7 || (cc.ControlNumber == 10 && (is680 || isMsGs)) || cc.ControlNumber == 91))
                        {
                            initialCcs.TryAdd((cc.Channel, (byte)cc.ControlNumber), (byte)cc.ControlValue);
                        }
                    }
                }

                if (initialPcs.Count == 0 && initialCcs.Count == 0)
                {
                    return;
                }

                ClearPrimedSetupDedup();
                int sentCount = 0;
                int primedPcMask = 0;
                long primedCcMask = 0;
                bool isQuadActive = !is680 && Volatile.Read(ref _vectorQuadPlaybackEnabled) != 0;

                lock (_deviceLock)
                {
                    if (!ReferenceEquals(_outputDevice, target) || Volatile.Read(ref _stopEpoch) != expectedStopEpoch) return;

                    foreach (var ch in initialPcs.Keys.OrderBy(k => k))
                    {
                        if (Volatile.Read(ref _stopEpoch) != expectedStopEpoch) return;
                        try
                        {
                            byte pcProg = initialPcs[ch].Program;
                            target.SendEvent(new ProgramChangeEvent((SevenBitNumber)pcProg)
                            {
                                Channel = (FourBitNumber)ch
                            });
                            _primedTickZeroPcValues[ch] = pcProg;
                            primedPcMask |= (1 << ch);
                            sentCount++;

                            // PSS-680 Manual p.35: every ProgramChange resets internal Sustain/Vibrato flags to factory preset defaults.
                            // Explicitly clear Sustain (CC 64 = 0) and reset Vibrato (CC 1) after PC so short/plucked voices don't smear or wobble.
                            if (is680)
                            {
                                byte initMod = initialCcs.TryGetValue((ch, 1), out var m0) ? m0 : (byte)0;
                                Volatile.Write(ref _lastPss680Modulation[ch], initMod);
                                ResetPss680DefaultEffectsForChannel(target, ch, initMod);
                                target.SendEvent(new PitchBendEvent(8192) { Channel = (FourBitNumber)ch });
                                if (initialCcs.ContainsKey((ch, 1)))
                                {
                                    int slotIdx = ch * 4;
                                    _primedTickZeroCcValues[slotIdx] = initMod;
                                    primedCcMask |= (1L << slotIdx);
                                }
                                sentCount += 3;
                            }

                            if (!isMsGs && (sentCount % 4) == 0)
                            {
                                Thread.Sleep(4);
                            }
                        }
                        catch { }
                    }

                    foreach (var kvp in initialCcs)
                    {
                        if (Volatile.Read(ref _stopEpoch) != expectedStopEpoch) return;
                        int ch = kvp.Key.Channel;
                        byte ccNum = kvp.Key.ControlNumber;
                        byte ccVal = kvp.Value;

                        if (is680 && ccNum == 1 && initialPcs.ContainsKey(ch))
                        {
                            // Already transmitted cleanly right after ProgramChange above
                            continue;
                        }

                        if (ccNum == 1 && is680)
                        {
                            Volatile.Write(ref _lastPss680Modulation[ch], ccVal);
                        }

                        if (ccNum == 7)
                        {
                            Volatile.Write(ref _lastFileChannelVolumes[ch], ccVal);
                            if (is680) continue;
                            byte userVol = Volatile.Read(ref _channelVolumes[ch]);
                            ccVal = (byte)Math.Clamp((int)Math.Round((double)ccVal * userVol / 100.0), 0, 127);
                        }

                        try
                        {
                            target.SendEvent(new ControlChangeEvent((SevenBitNumber)ccNum, (SevenBitNumber)ccVal)
                            {
                                Channel = (FourBitNumber)ch
                            });
                            bool skipQuadCc = isQuadActive && ch == 0 && (ccNum == 1 || ccNum == 7 || ccNum == 91);
                            if (!skipQuadCc && TryMapPrimedCcSlot(ccNum, out int slotOffset))
                            {
                                int slotIdx = ch * 4 + slotOffset;
                                _primedTickZeroCcValues[slotIdx] = ccVal;
                                primedCcMask |= (1L << slotIdx);
                            }
                            sentCount++;

                            if (!isMsGs && (sentCount % 4) == 0)
                            {
                                Thread.Sleep(4);
                            }
                        }
                        catch { }
                    }
                }

                // Allow 31.25 kbps serial DIN MIDI UART & hardware wave ROM lookup to settle before Note 1,
                // aborting immediately if Stop/LoadMidi is called during settle.
                if (!isMsGs && sentCount > 0)
                {
                    int settleMs = Math.Clamp(sentCount * 3 + 35, 45, 140);
                    int elapsed = 0;
                    while (elapsed < settleMs)
                    {
                        if (_isDisposed || Volatile.Read(ref _stopEpoch) != expectedStopEpoch) return;
                        lock (_stateLock)
                        {
                            if (!ReferenceEquals(_playback, expectedPlayback)) return;
                        }
                        int step = Math.Min(10, settleMs - elapsed);
                        Thread.Sleep(step);
                        elapsed += step;
                    }
                }

                if (sentCount > 0 && !_isDisposed && Volatile.Read(ref _stopEpoch) == expectedStopEpoch)
                {
                    Volatile.Write(ref _primedTickZeroPcMask, primedPcMask);
                    Volatile.Write(ref _primedTickZeroCcMask, primedCcMask);
                }
            }
            catch { }
        }

        private static bool TryMapPrimedCcSlot(byte ccNum, out int slotOffset)
        {
            switch (ccNum)
            {
                case 1: slotOffset = 0; return true;
                case 7: slotOffset = 1; return true;
                case 10: slotOffset = 2; return true;
                case 91: slotOffset = 3; return true;
                default: slotOffset = -1; return false;
            }
        }

        private void ClearPrimedSetupDedup()
        {
            Volatile.Write(ref _primedDedupExpiryTimestamp, 0);
            Interlocked.Exchange(ref _primedTickZeroPcMask, 0);
            Interlocked.Exchange(ref _primedTickZeroCcMask, 0);
        }

        private bool TryConsumePrimedPc(int ch, byte prog)
        {
            if ((uint)ch >= 15) return false;
            long expiry = Volatile.Read(ref _primedDedupExpiryTimestamp);
            if (expiry == 0) return false;
            if (Stopwatch.GetTimestamp() > expiry)
            {
                ClearPrimedSetupDedup();
                return false;
            }

            int bit = 1 << ch;
            if ((Volatile.Read(ref _primedTickZeroPcMask) & bit) == 0) return false;
            if (_primedTickZeroPcValues[ch] != prog) return false;
            Interlocked.And(ref _primedTickZeroPcMask, ~bit);
            return true;
        }

        private bool TryConsumePrimedCc(int ch, byte ccNum, byte val)
        {
            if ((uint)ch >= 15 || !TryMapPrimedCcSlot(ccNum, out int slotOffset)) return false;
            long expiry = Volatile.Read(ref _primedDedupExpiryTimestamp);
            if (expiry == 0) return false;
            if (Stopwatch.GetTimestamp() > expiry)
            {
                ClearPrimedSetupDedup();
                return false;
            }

            long bit = 1L << (ch * 4 + slotOffset);
            if ((Volatile.Read(ref _primedTickZeroCcMask) & bit) == 0) return false;
            if (_primedTickZeroCcValues[ch * 4 + slotOffset] != val) return false;
            Interlocked.And(ref _primedTickZeroCcMask, ~bit);
            return true;
        }

        public void Pause() => _actor.Invoke(PauseInternal);

        public Task PauseAsync() => _actor.InvokeAsync(PauseInternal);

        private void PauseInternal()
        {
            Interlocked.Increment(ref _stopEpoch);
            Interlocked.Increment(ref _playbackSessionEpoch);
            ClearPrimedSetupDedup();

            Playback? p;
            FilteringOutputDevice? filtering;
            lock (_stateLock)
            {
                p = _playback;
                filtering = _filteringDevice;
                try { _progressTimer.Stop(); } catch { }
            }

            if (p != null)
            {
                bool wasRunning = false;
                lock (_transportLock)
                {
                    filtering?.SuppressOutput();
                    try
                    {
                        wasRunning = SafeIsRunning(p);
                        if (wasRunning)
                        {
                            p.Stop();
                            for (int i = 0; i < 10 && SafeIsRunning(p); i++)
                            {
                                Thread.Sleep(5);
                            }
                        }
                    }
                    catch { }
                }

                if (wasRunning)
                {
                    // Kill active sounding notes without resetting Pitch Bend / Modulation so Resume stays expressive
                    SendActiveNotesOffFast();
                    NotifyState();
                }
            }
        }

        public void Stop() => _actor.Invoke(StopInternal);

        public Task StopAsync() => _actor.InvokeAsync(StopInternal);

        private void StopInternal()
        {
            Interlocked.Increment(ref _stopEpoch);
            Interlocked.Increment(ref _playbackSessionEpoch);
            StopAndDetachPlayback(disposePlayback: false, forceFullPanic: true);
            NotifyState();
        }

        public void Seek(TimeSpan position) => _actor.Invoke(() => SeekInternal(position));

        public Task SeekAsync(TimeSpan position) => _actor.InvokeAsync(() => SeekInternal(position));

        private void SeekInternal(TimeSpan position)
        {
            ClearPrimedSetupDedup();
            int stopEpoch = Volatile.Read(ref _stopEpoch);
            Playback? p;
            bool needsInit;
            lock (_stateLock)
            {
                p = _playback;
                needsInit = p == null && _currentFile != null;
                _hasFinished = false;
            }

            if (needsInit && HasOutputDevice())
            {
                InitializePlayback();
                lock (_stateLock) { p = _playback; }
            }

            var clampedPos = position < TimeSpan.Zero ? TimeSpan.Zero : position;
            // Use fast targeted active-note silencing during scrubbing/seeking so serial MIDI
            // buses (31.25 kbps) and the UI bridge don't stall on 96-event full controller resets.
            SendActiveNotesOffFast();

            if (p != null)
            {
                try
                {
                    lock (_transportLock)
                    {
                        if (_isDisposed || Volatile.Read(ref _stopEpoch) != stopEpoch)
                        {
                            return;
                        }

                        FilteringOutputDevice? filtering;
                        lock (_stateLock)
                        {
                            if (!ReferenceEquals(_playback, p))
                            {
                                return;
                            }
                            _lastKnownPosition = clampedPos;
                            filtering = _filteringDevice;
                        }

                        // Allow MoveToTime to emit tracked Program/Control/Pitch updates to restore mid-song state
                        filtering?.AllowOutput();
                        p.MoveToTime(new MetricTimeSpan(clampedPos));
                    }
                }
                catch { }
            }
            else
            {
                lock (_stateLock) { _lastKnownPosition = clampedPos; }
            }

            NotifyState();
        }

        public void SetSpeed(double speed) => _actor.Invoke(() => SetSpeedInternal(speed));

        private void SetSpeedInternal(double speed)
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

        public void SetLoop(bool loop) => _actor.Invoke(() => SetLoopInternal(loop));

        private void SetLoopInternal(bool loop)
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
                    TrackSentHardwareNoteLocked(this, midiEvent);
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
            ClearPrimedSetupDedup();

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

            if (is680 && channel is >= 0 and < 15)
            {
                lock (_deviceLock)
                {
                    if (_outputDevice is OutputDevice hwTarget)
                    {
                        byte restoreMod = Volatile.Read(ref _lastPss680Modulation[channel]);
                        ResetPss680DefaultEffectsForChannel(hwTarget, channel, restoreMod);
                    }
                }
            }
        }

        public (bool IsMuted, bool IsSolo, byte Volume) GetChannelMixerState(int channel)
        {
            if (channel < 0 || channel >= 16) return (false, false, 100);
            int mMask = Volatile.Read(ref _muteMask);
            int sMask = Volatile.Read(ref _soloMask);
            bool muted = (mMask & (1 << channel)) != 0;
            bool solo = (sMask & (1 << channel)) != 0;
            byte vol;
            lock (_stateLock) { vol = _channelVolumes[channel]; }
            return (muted, solo, vol);
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

        public void SendActiveNotesOffFast() => _actor.Invoke(SendActiveNotesOffFastInternal);

        private void SendActiveNotesOffFastInternal()
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

        public void SendPanicNoteOff() => _actor.Invoke(() => SendPanicNoteOffInternal(forceAllChannels: true));

        private void SendPanicNoteOffInternal(bool forceAllChannels)
        {
            if (forceAllChannels)
            {
                for (int i = 0; i < 16; i++)
                {
                    Volatile.Write(ref _lastPss680Modulation[i], (byte)0);
                }
            }

            var killSet = new HashSet<(int Channel, byte Note)>();
            var orderedKillList = new List<(int Channel, byte Note)>();
            lock (_notesLock)
            {
                foreach (var key in _activeNotes.Keys)
                {
                    if (killSet.Add(key))
                    {
                        orderedKillList.Add(key);
                    }
                }
                _activeNotes.Clear();
                Array.Clear(_activeNoteCounts, 0, 16);
            }

            // Drain any remaining unclosed (Channel, NoteNumber) touched on hardware since the last clean stop so
            // notes cleared earlier by Pause/Seek or overlapping same-pitch triggers still receive an explicit Note-Off.
            for (int wordIdx = 0; wordIdx < 32; wordIdx++)
            {
                long mask = Interlocked.Exchange(ref _playedNoteMask[wordIdx], 0L);
                if (mask == 0L) continue;

                int ch = wordIdx >> 1;
                int noteBase = (wordIdx & 1) << 6;
                ulong uMask = (ulong)mask;
                while (uMask != 0UL)
                {
                    int bit = System.Numerics.BitOperations.TrailingZeroCount(uMask);
                    var item = (ch, (byte)(noteBase + bit));
                    if (killSet.Add(item))
                    {
                        orderedKillList.Add(item);
                    }
                    uMask &= uMask - 1;
                }
            }

            int usedMask = Interlocked.Exchange(ref _usedChannelMask, 0);
            int bendMask = Interlocked.Exchange(ref _dirtyBendMask, 0);
            int modMask = Interlocked.Exchange(ref _dirtyModMask, 0);
            int sustainMask = Interlocked.Exchange(ref _dirtySustainMask, 0);
            if (forceAllChannels) Interlocked.Exchange(ref _dirtyVolumeMask, 0);

            foreach (var (ch, _) in orderedKillList)
            {
                if (ch >= 0 && ch < 16) usedMask |= (1 << ch);
            }

            if (usedMask != 0)
            {
                SetMaskBit(ref _stickyPss680ChannelMask, usedMask);
            }

            bool is680 = _isPss680;
            bool shouldDrainHardware = false;
            int txPackets = 0;

            lock (_deviceLock)
            {
                Array.Clear(_hwActiveNoteCount, 0, _hwActiveNoteCount.Length);
                var target = _outputDevice;
                if (target != null)
                {
                    bool isHardwarePort = target is OutputDevice && !Constants.IsMicrosoftGsWavetableSynth(_currentOutputDeviceName);

                    void PaceHardwareTx()
                    {
                        if (isHardwarePort && (++txPackets % 10) == 0)
                        {
                            Thread.Sleep(3);
                        }
                    }

                    // 1. Hardware controller cleanup BEFORE Note-Offs so sustain dampers (CC 64) release immediately.
                    // Note: Yamaha PSS-680 Manual p.46 marks All Notes Off (CC 123), All Sound Off (CC 120), and Reset (CC 121) as X (Ignored);
                    // skip blasting those ignored CCs on PSS-680 to conserve 31.25 kbps serial UART bandwidth for real Note-Offs.
                    for (int ch = 0; ch < 16; ch++)
                    {
                        int bit = 1 << ch;
                        bool sendAllNotesOff = !is680 && (forceAllChannels || (usedMask & bit) != 0);
                        bool sendMod = forceAllChannels || (ch < 15 && (modMask & bit) != 0);
                        bool sendBend = forceAllChannels || (ch < 15 && (bendMask & bit) != 0);
                        bool sendSustain = forceAllChannels || (ch < 15 && (sustainMask & bit) != 0) || (is680 && ch < 15 && (usedMask & bit) != 0);

                        if (!sendAllNotesOff && !sendMod && !sendBend && !sendSustain)
                        {
                            continue;
                        }

                        try
                        {
                            var fourBitCh = (FourBitNumber)ch;
                            if (sendSustain)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = fourBitCh });  // Sustain Off
                                PaceHardwareTx();
                            }
                            if (sendAllNotesOff)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)120, (SevenBitNumber)0) { Channel = fourBitCh }); // All Sound Off
                                PaceHardwareTx();
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0) { Channel = fourBitCh }); // All Notes Off
                                PaceHardwareTx();
                            }
                            if (forceAllChannels && !is680)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)121, (SevenBitNumber)0) { Channel = fourBitCh }); // Reset All Controllers
                                PaceHardwareTx();
                            }
                            if (sendMod)
                            {
                                target.SendEvent(new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)0) { Channel = fourBitCh });   // Modulation Off
                                PaceHardwareTx();
                            }
                            if (sendBend)
                            {
                                target.SendEvent(new PitchBendEvent(8192) { Channel = fourBitCh });                                       // Pitch Bend Center
                                PaceHardwareTx();
                            }
                        }
                        catch { }
                    }

                    // 2. Explicit Note-Off prioritizing currently active sounding notes first (PSS-680 stops notes exclusively via 8nH / 9nH v=0)
                    foreach (var (ch, note) in orderedKillList)
                    {
                        try
                        {
                            target.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0)
                            {
                                Channel = (FourBitNumber)ch
                            });
                            PaceHardwareTx();
                        }
                        catch { }
                    }

                    // 3. Fallback idle Panic sweep for PSS-680 hardware when user clicks Stop a second time with empty killSet
                    if (is680 && forceAllChannels && orderedKillList.Count == 0 && target is OutputDevice && !_isDisposed)
                    {
                        int stickyChannels = Interlocked.Exchange(ref _stickyPss680ChannelMask, 0);
                        int melodicMask = (stickyChannels != 0 ? stickyChannels : 0x7FFF) & 0x7FFF;
                        for (int ch = 0; ch < 15; ch++)
                        {
                            if ((melodicMask & (1 << ch)) == 0) continue;
                            var fourBitCh = (FourBitNumber)ch;
                            for (int n = 24; n <= 108; n++)
                            {
                                try
                                {
                                    target.SendEvent(new NoteOffEvent((SevenBitNumber)n, (SevenBitNumber)0) { Channel = fourBitCh });
                                    PaceHardwareTx();
                                }
                                catch { }
                            }
                        }
                        if ((stickyChannels & (1 << 15)) != 0 || stickyChannels == 0)
                        {
                            for (int n = 38; n <= 79; n++)
                            {
                                try
                                {
                                    target.SendEvent(new NoteOffEvent((SevenBitNumber)n, (SevenBitNumber)0) { Channel = (FourBitNumber)15 });
                                    PaceHardwareTx();
                                }
                                catch { }
                            }
                        }
                        shouldDrainHardware = isHardwarePort;
                    }
                    else if (isHardwarePort && txPackets > 0 && !_isDisposed)
                    {
                        shouldDrainHardware = true;
                    }
                }
            }

            if (shouldDrainHardware)
            {
                // Allow USB-MIDI / WinMM to clock explicit Note-Off & cleanup packets out onto the 31.25 kbps bus before the next song primes.
                int drainMs = Math.Clamp((txPackets * 3 * 1000) / 3125 + 16, 16, 220);
                Thread.Sleep(drainMs);
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
            TimeSpan lastKnownPos;
            bool hasFinished;

            lock (_stateLock)
            {
                p = _playback;
                totalDuration = _cachedTotalDuration;
                lastKnownPos = _lastKnownPosition;
                hasFinished = _hasFinished;
            }

            bool isPlaying = false;
            TimeSpan currentPos = p != null ? lastKnownPos : TimeSpan.Zero;

            if (p != null)
            {
                try
                {
                    isPlaying = SafeIsRunning(p);
                    currentPos = p.GetCurrentTime<MetricTimeSpan>();
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_playback, p))
                        {
                            _lastKnownPosition = currentPos;
                        }
                    }
                }
                catch (Exception)
                {
                    isPlaying = false;
                    currentPos = lastKnownPos;
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
            _actor.Shutdown(DisposeOnActorThread);
            GC.SuppressFinalize(this);
        }

        private void DisposeOnActorThread()
        {
            lock (_stateLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }

            Interlocked.Increment(ref _stopEpoch);
            Interlocked.Increment(ref _playbackSessionEpoch);

            try
            {
                _progressTimer.Stop();
                _progressTimer.Dispose();
            }
            catch { }

            StopAndDetachPlayback(disposePlayback: true, forceFullPanic: true);

            IOutputDevice? oldDevice;
            bool wasHardwarePort;
            lock (_deviceLock)
            {
                oldDevice = _outputDevice;
                wasHardwarePort = oldDevice is OutputDevice;
                _outputDevice = null;
                _currentOutputDeviceName = null;
                UpdateHardwareCc10FilterLocked();
            }

            if (wasHardwarePort)
            {
                // Allow Windows WinMM / USB-MIDI TX queue to clock out final Note-Off & Panic controllers
                // before closing the MIDI port handle (preventing midiOutReset from truncating in-flight cleanup).
                Thread.Sleep(75);
            }

            try { oldDevice?.Dispose(); } catch { }
        }

        private sealed class FilteringOutputDevice : IOutputDevice, IDisposable
        {
            private readonly MidiPlayer _player;
            private volatile bool _isDisposed;
            private volatile bool _outputSuppressed;

            public event EventHandler<MidiEventSentEventArgs>? EventSent;

            public FilteringOutputDevice(MidiPlayer player)
            {
                _player = player;
            }

            public void SuppressOutput() => _outputSuppressed = true;
            public void AllowOutput() => _outputSuppressed = false;

            public void Dispose()
            {
                _outputSuppressed = true;
                _isDisposed = true;
            }

            public void SendEvent(MidiEvent midiEvent)
            {
                if (_isDisposed || _outputSuppressed) return;

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

                        if (isNoteOnAudible && (uint)ch < 15 && ((Volatile.Read(ref _player._primedTickZeroPcMask) & (1 << ch)) != 0))
                        {
                            Interlocked.And(ref _player._primedTickZeroPcMask, ~(1 << ch));
                        }

                        if (channelEvent is ProgramChangeEvent pcEvt && _player.TryConsumePrimedPc(ch, (byte)pcEvt.ProgramNumber))
                        {
                            return;
                        }

                        bool is680 = _player._isPss680;

                        if (is680 && channelEvent is ControlChangeEvent modCc && modCc.ControlNumber == 1)
                        {
                            Volatile.Write(ref _player._lastPss680Modulation[ch], (byte)modCc.ControlValue);
                        }

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

                        // Proportional Mixer Scaling & Hardware Filtering for CCs
                        if (channelEvent is ControlChangeEvent cc)
                        {
                            if (cc.ControlNumber == 10 && _player._stripHardwareCc10)
                            {
                                // Omit unsupported Pan (CC 10) on physical PSS-790 UART to prevent <bu full> buffer drops
                                return;
                            }

                            if (cc.ControlNumber == 7)
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
                    }

                    if (midiEvent is ControlChangeEvent finalCc &&
                        _player.TryConsumePrimedCc(finalCc.Channel, (byte)finalCc.ControlNumber, (byte)finalCc.ControlValue))
                    {
                        return;
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
                        if (cc.ControlNumber == 10 && _player._stripHardwareCc10) return null;
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
                    if (target == null || _isDisposed || _outputSuppressed) return;

                    try
                    {
                        for (int i = 0; i < eventsToSend.Count; i++)
                        {
                            target.SendEvent(eventsToSend[i]);
                            TrackSentHardwareNoteLocked(_player, eventsToSend[i]);
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
                    if (target == null || _isDisposed || _outputSuppressed) return;

                    try
                    {
                        target.SendEvent(midiEvent);
                        sent = true;
                        TrackSentHardwareNoteLocked(_player, midiEvent);

                        // Manual p.35: Clear factory Sustain/Vibrato flags immediately after mid-song ProgramChanges on PSS-680 hardware
                        if (_player._isPss680 && target is OutputDevice && midiEvent is ProgramChangeEvent pcEvt && pcEvt.Channel != 15)
                        {
                            int pcCh = pcEvt.Channel;
                            byte restoreMod = Volatile.Read(ref _player._lastPss680Modulation[pcCh]);
                            ResetPss680DefaultEffectsForChannel(target, pcCh, restoreMod);
                        }
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

        private static void TrackSentHardwareNoteLocked(MidiPlayer player, MidiEvent midiEvent)
        {
            if (midiEvent is NoteOnEvent noteOn)
            {
                int ch = noteOn.Channel;
                int note = noteOn.NoteNumber;
                if ((uint)ch >= 16 || (uint)note >= 128) return;

                int idx = (ch << 7) | note;
                int wordIdx = (ch << 1) + (note >> 6);
                long bit = 1L << (note & 63);

                if (noteOn.Velocity > 0)
                {
                    if (player._hwActiveNoteCount[idx] < byte.MaxValue)
                    {
                        player._hwActiveNoteCount[idx]++;
                    }
                    Interlocked.Or(ref player._playedNoteMask[wordIdx], bit);
                    SetMaskBit(ref player._usedChannelMask, 1 << ch);
                }
                else
                {
                    DecrementHardwareNoteLocked(player, idx, wordIdx, bit);
                }
            }
            else if (midiEvent is NoteOffEvent noteOff)
            {
                int ch = noteOff.Channel;
                int note = noteOff.NoteNumber;
                if ((uint)ch >= 16 || (uint)note >= 128) return;

                DecrementHardwareNoteLocked(player, (ch << 7) | note, (ch << 1) + (note >> 6), 1L << (note & 63));
            }
        }

        private static void DecrementHardwareNoteLocked(MidiPlayer player, int idx, int wordIdx, long bit)
        {
            byte cur = player._hwActiveNoteCount[idx];
            if (cur > 1)
            {
                player._hwActiveNoteCount[idx] = (byte)(cur - 1);
            }
            else
            {
                player._hwActiveNoteCount[idx] = 0;
                Interlocked.And(ref player._playedNoteMask[wordIdx], ~bit);
            }
        }

        private static void ResetPss680DefaultEffectsForChannel(IOutputDevice target, int ch, byte restoreModulation)
        {
            if (ch < 0 || ch >= 15) return;
            var fourBitCh = (FourBitNumber)ch;
            try { target.SendEvent(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = fourBitCh }); } catch { }
            try { target.SendEvent(new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)restoreModulation) { Channel = fourBitCh }); } catch { }
        }
    }
}
