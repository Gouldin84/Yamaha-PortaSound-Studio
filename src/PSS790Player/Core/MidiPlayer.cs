using System;
using System.Collections.Generic;
using System.Linq;
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
        private FilteringOutputDevice? _filteringDevice;
        private Playback? _playback;
        private MidiFile? _currentFile;
        private readonly object _lock = new();

        private readonly HashSet<(int Channel, byte Note)> _activeNotes = new();
        private readonly bool[] _channelMutes = new bool[16];
        private readonly bool[] _channelSolos = new bool[16];
        private readonly byte[] _channelVolumes = new byte[16];
        private readonly byte[] _lastFileChannelVolumes = new byte[16];
        private readonly System.Timers.Timer _progressTimer;

        public event EventHandler<ChannelActivityEventArgs>? ChannelActivity;
        public event EventHandler<PlaybackStateEventArgs>? PlaybackStateChanged;
        public event EventHandler<string>? OutputDeviceDisconnected;

        public bool IsPlaying => _playback?.IsRunning ?? false;
        public bool IsPaused => (!IsPlaying) && (_playback?.GetCurrentTime<MetricTimeSpan>().TotalMilliseconds > 0);
        public TimeSpan TotalDuration => _currentFile?.GetDuration<MetricTimeSpan>() ?? TimeSpan.Zero;
        public TimeSpan CurrentPosition => _playback?.GetCurrentTime<MetricTimeSpan>() ?? TimeSpan.Zero;
        public string? CurrentOutputDeviceName { get; private set; }
        public string ActiveModel { get; set; } = "pss790";
        public bool VectorQuadPlaybackEnabled { get; set; } = false;

        public MidiPlayer()
        {
            for (int i = 0; i < 16; i++)
            {
                _channelVolumes[i] = 100;
                _lastFileChannelVolumes[i] = 100;
            }

            _progressTimer = new System.Timers.Timer(100);
            _progressTimer.Elapsed += (s, e) =>
            {
                if (IsPlaying)
                {
                    NotifyState();
                }
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
                lock (_lock)
                {
                    Stop();
                    _outputDevice?.Dispose();
                    _outputDevice = null;
                    CurrentOutputDeviceName = null;
                }
                return;
            }

            try
            {
                var dev = OutputDevice.GetByName(deviceName);
                CurrentOutputDeviceName = deviceName;
                SetOutputDevice(dev);
            }
            catch
            {
                lock (_lock)
                {
                    Stop();
                    _outputDevice?.Dispose();
                    _outputDevice = null;
                    CurrentOutputDeviceName = null;
                }
            }
        }

        public void SetOutputDevice(IOutputDevice? outputDevice)
        {
            lock (_lock)
            {
                var wasPlaying = IsPlaying;
                var currentPos = CurrentPosition;

                Stop();
                _outputDevice?.Dispose();
                _outputDevice = outputDevice;
                if (outputDevice == null)
                {
                    CurrentOutputDeviceName = null;
                }

                if (_currentFile != null && _outputDevice != null)
                {
                    InitializePlayback();
                    if (wasPlaying)
                    {
                        Seek(currentPos);
                        Play();
                    }
                }
            }
        }

        public void HandleDeviceDisconnected()
        {
            string? disconnectedName;
            lock (_lock)
            {
                disconnectedName = CurrentOutputDeviceName;
                Stop();
                try
                {
                    _outputDevice?.Dispose();
                }
                catch { }
                _outputDevice = null;
                CurrentOutputDeviceName = null;
            }

            if (!string.IsNullOrEmpty(disconnectedName))
            {
                OutputDeviceDisconnected?.Invoke(this, disconnectedName);
            }
        }

        public void LoadMidi(MidiFile midiFile)
        {
            lock (_lock)
            {
                Stop();
                _currentFile = midiFile;

                if (_outputDevice != null)
                {
                    InitializePlayback();
                }

                NotifyState();
            }
        }

        private void InitializePlayback()
        {
            if (_currentFile == null || _outputDevice == null) return;

            if (_playback != null)
            {
                _playback.EventPlayed -= OnPlaybackEventPlayed;
                _playback.Dispose();
                _playback = null;
            }

            var settings = new PlaybackSettings
            {
                ClockSettings = new MidiClockSettings
                {
                    CreateTickGeneratorCallback = () => new HighPrecisionTickGenerator()
                }
            };

            _filteringDevice = new FilteringOutputDevice(this);
            _playback = _currentFile.GetPlayback(_filteringDevice, settings);
            _playback.EventPlayed += OnPlaybackEventPlayed;

            _playback.Finished += (s, e) =>
            {
                _progressTimer.Stop();
                SendPanicNoteOff();
                NotifyState();
            };

            _playback.RepeatStarted += (s, e) =>
            {
                SendPanicNoteOff();
                RePrimeInitialControllers();
            };
        }

        private void RePrimeInitialControllers()
        {
            lock (_lock)
            {
                if (_outputDevice == null || _currentFile == null) return;

                // Re-prime controllers across all 16 channels to guarantee seamless looping
                for (int ch = 0; ch < 16; ch++)
                {
                    try
                    {
                        var fourBitCh = (FourBitNumber)ch;
                        byte fileVol = _lastFileChannelVolumes[ch];
                        byte userVol = _channelVolumes[ch];
                        int scaled = (int)Math.Round((double)fileVol * userVol / 100.0);
                        scaled = Math.Clamp(scaled, 0, 127);

                        _outputDevice.SendEvent(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)scaled) { Channel = fourBitCh });
                        if (!string.Equals(ActiveModel, "pss680", StringComparison.OrdinalIgnoreCase))
                        {
                            _outputDevice.SendEvent(new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)56) { Channel = fourBitCh });
                        }
                        _outputDevice.SendEvent(new PitchBendEvent(8192) { Channel = fourBitCh });
                    }
                    catch { }
                }
            }
        }

        private void OnPlaybackEventPlayed(object? sender, MidiEventPlayedEventArgs e)
        {
            if (e.Event is ChannelEvent channelEvent)
            {
                int ch = channelEvent.Channel;

                // Mute / Solo handling
                bool hasSolo = _channelSolos.Any(s => s);
                bool isMuted = _channelMutes[ch] || (hasSolo && !_channelSolos[ch]);

                if (isMuted)
                {
                    return;
                }

                if (channelEvent is NoteOnEvent noteOn)
                {
                    if (noteOn.Velocity > 0)
                    {
                        lock (_activeNotes)
                        {
                            _activeNotes.Add((ch, (byte)noteOn.NoteNumber));
                            if (VectorQuadPlaybackEnabled && ch == 0)
                            {
                                for (int q = 1; q <= 3; q++) _activeNotes.Add((q, (byte)noteOn.NoteNumber));
                            }
                        }
                        ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                        {
                            Channel = ch,
                            IsActive = true,
                            Velocity = (byte)noteOn.Velocity,
                            NoteNumber = (byte)noteOn.NoteNumber
                        });

                        if (VectorQuadPlaybackEnabled && ch == 0)
                        {
                            for (int q = 1; q <= 3; q++)
                            {
                                ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                                {
                                    Channel = q,
                                    IsActive = true,
                                    Velocity = (byte)noteOn.Velocity,
                                    NoteNumber = (byte)noteOn.NoteNumber
                                });
                            }
                        }
                    }
                    else
                    {
                        bool hasRemaining;
                        lock (_activeNotes)
                        {
                            _activeNotes.Remove((ch, (byte)noteOn.NoteNumber));
                            if (VectorQuadPlaybackEnabled && ch == 0)
                            {
                                for (int q = 1; q <= 3; q++) _activeNotes.Remove((q, (byte)noteOn.NoteNumber));
                            }
                            hasRemaining = _activeNotes.Any(n => n.Channel == ch);
                        }
                        if (!hasRemaining)
                        {
                            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                            {
                                Channel = ch,
                                IsActive = false,
                                Velocity = 0
                            });
                            if (VectorQuadPlaybackEnabled && ch == 0)
                            {
                                for (int q = 1; q <= 3; q++)
                                {
                                    ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                                    {
                                        Channel = q,
                                        IsActive = false,
                                        Velocity = 0
                                    });
                                }
                            }
                        }
                    }
                }
                else if (channelEvent is NoteOffEvent noteOff)
                {
                    bool hasRemaining;
                    lock (_activeNotes)
                    {
                        _activeNotes.Remove((ch, (byte)noteOff.NoteNumber));
                        if (VectorQuadPlaybackEnabled && ch == 0)
                        {
                            for (int q = 1; q <= 3; q++) _activeNotes.Remove((q, (byte)noteOff.NoteNumber));
                        }
                        hasRemaining = _activeNotes.Any(n => n.Channel == ch);
                    }
                    if (!hasRemaining)
                    {
                        ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                        {
                            Channel = ch,
                            IsActive = false,
                            Velocity = 0
                        });
                        if (VectorQuadPlaybackEnabled && ch == 0)
                        {
                            for (int q = 1; q <= 3; q++)
                            {
                                ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                                {
                                    Channel = q,
                                    IsActive = false,
                                    Velocity = 0
                                });
                            }
                        }
                    }
                }
            }
        }

        public void Play()
        {
            lock (_lock)
            {
                if (_playback != null && !_playback.IsRunning)
                {
                    _playback.Start();
                    _progressTimer.Start();
                    NotifyState();
                }
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                if (_playback != null && _playback.IsRunning)
                {
                    _progressTimer.Stop();
                    _playback.Stop();
                    SendPanicNoteOff();
                    NotifyState();
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _progressTimer.Stop();
                if (_playback != null)
                {
                    _playback.Stop();
                    _playback.MoveToStart();
                }

                SendPanicNoteOff();
                NotifyState();
            }
        }

        public void Seek(TimeSpan position)
        {
            lock (_lock)
            {
                SendPanicNoteOff();
                if (_playback != null)
                {
                    _playback.MoveToTime(new MetricTimeSpan(position));
                }
                NotifyState();
            }
        }

        public void SetSpeed(double speed)
        {
            lock (_lock)
            {
                if (_playback != null && speed > 0.1 && speed < 3.0)
                {
                    _playback.Speed = speed;
                }
            }
        }

        public void SetLoop(bool loop)
        {
            lock (_lock)
            {
                if (_playback != null)
                {
                    _playback.Loop = loop;
                }
            }
        }

        public void SendEvent(MidiEvent midiEvent)
        {
            lock (_lock)
            {
                try
                {
                    _outputDevice?.SendEvent(midiEvent);
                }
                catch
                {
                    HandleDeviceDisconnected();
                }
            }
        }

        public void SendSysEx(byte[] rawSysEx)
        {
            if (rawSysEx == null || rawSysEx.Length == 0) return;

            byte[] dataPayload;
            if (rawSysEx.Length >= 2 && rawSysEx[0] == 0xF0 && rawSysEx[^1] == 0xF7)
            {
                dataPayload = new byte[rawSysEx.Length - 2];
                Buffer.BlockCopy(rawSysEx, 1, dataPayload, 0, dataPayload.Length);
            }
            else if (rawSysEx.Length >= 1 && rawSysEx[0] == 0xF0)
            {
                dataPayload = new byte[rawSysEx.Length - 1];
                Buffer.BlockCopy(rawSysEx, 1, dataPayload, 0, dataPayload.Length);
            }
            else
            {
                dataPayload = rawSysEx;
            }

            SendEvent(new Melanchall.DryWetMidi.Core.NormalSysExEvent(dataPayload));
        }

        public void SendVoiceOverride(int channel, int voiceNumber, string? model = null)
        {
            lock (_lock)
            {
                if (_outputDevice == null || channel < 0 || channel > 14) return; // Ch 15/16 is drum, ignores PC

                var effectiveModel = model ?? ActiveModel;
                bool is680 = string.Equals(effectiveModel, "pss680", StringComparison.OrdinalIgnoreCase);

                byte programNumber;
                if (is680)
                {
                    programNumber = (byte)Math.Clamp(voiceNumber, 0, 107);
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
                try
                {
                    _outputDevice.SendEvent(pc);
                }
                catch
                {
                    HandleDeviceDisconnected();
                }
            }
        }

        public void SetChannelVolume(int channel, byte volume)
        {
            lock (_lock)
            {
                if (channel >= 0 && channel < 16)
                {
                    _channelVolumes[channel] = volume;
                    if (_outputDevice != null)
                    {
                        byte fileVol = _lastFileChannelVolumes[channel];
                        int scaled = (int)Math.Round((double)fileVol * volume / 100.0);
                        scaled = Math.Clamp(scaled, 0, 127);

                        var cc = new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)scaled)
                        {
                            Channel = (FourBitNumber)channel
                        };
                        _outputDevice.SendEvent(cc);
                    }
                }
            }
        }

        public void SetChannelMute(int channel, bool isMuted)
        {
            lock (_lock)
            {
                if (channel >= 0 && channel < 16)
                {
                    _channelMutes[channel] = isMuted;
                    if (isMuted)
                    {
                        SendChannelNoteOff(channel);
                    }
                }
            }
        }

        public void SetChannelSolo(int channel, bool isSolo)
        {
            lock (_lock)
            {
                if (channel >= 0 && channel < 16)
                {
                    _channelSolos[channel] = isSolo;
                    // Silence notes on newly muted channels
                    bool hasSolo = _channelSolos.Any(s => s);
                    for (int ch = 0; ch < 16; ch++)
                    {
                        if (_channelMutes[ch] || (hasSolo && !_channelSolos[ch]))
                        {
                            SendChannelNoteOff(ch);
                        }
                    }
                }
            }
        }

        public void SendPanicNoteOff()
        {
            lock (_lock)
            {
                if (_outputDevice == null) return;

                // 1. Explicit Note-Off for currently tracked sounding notes
                lock (_activeNotes)
                {
                    foreach (var (ch, note) in _activeNotes)
                    {
                        try
                        {
                            _outputDevice.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0)
                            {
                                Channel = (FourBitNumber)ch
                            });
                        }
                        catch { }
                    }
                    _activeNotes.Clear();
                }

                // 2. Also send All Notes Off (CC 123), Sustain Release (CC 64), Reset Controllers (CC 121), and Pitch Center (8192)
                for (int ch = 0; ch < 16; ch++)
                {
                    try
                    {
                        var fourBitCh = (FourBitNumber)ch;
                        // All Notes Off
                        _outputDevice.SendEvent(new ControlChangeEvent((SevenBitNumber)123, (SevenBitNumber)0) { Channel = fourBitCh });
                        // Damper / Sustain Pedal Release (CC 64 = 0)
                        _outputDevice.SendEvent(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = fourBitCh });
                        // Reset All Controllers (CC 121 = 0)
                        _outputDevice.SendEvent(new ControlChangeEvent((SevenBitNumber)121, (SevenBitNumber)0) { Channel = fourBitCh });
                        // Center Pitch Bend (8192)
                        _outputDevice.SendEvent(new PitchBendEvent(8192) { Channel = fourBitCh });
                    }
                    catch { }
                }

                // 3. Clear all 16 channel activity callbacks
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
        }

        private void SendChannelNoteOff(int channel)
        {
            if (_outputDevice == null) return;

            lock (_activeNotes)
            {
                var notesOnChannel = _activeNotes.Where(n => n.Channel == channel).ToList();
                foreach (var (ch, note) in notesOnChannel)
                {
                    try
                    {
                        _outputDevice.SendEvent(new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0)
                        {
                            Channel = (FourBitNumber)ch
                        });
                    }
                    catch { }
                    _activeNotes.Remove((ch, note));
                }
            }

            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
            {
                Channel = channel,
                IsActive = false,
                Velocity = 0
            });
        }

        private void NotifyState()
        {
            PlaybackStateChanged?.Invoke(this, new PlaybackStateEventArgs
            {
                IsPlaying = IsPlaying,
                IsPaused = IsPaused,
                CurrentTime = CurrentPosition,
                TotalTime = TotalDuration
            });
        }

        public void Dispose()
        {
            _progressTimer.Stop();
            _progressTimer.Dispose();
            Stop();
            if (_playback != null)
            {
                _playback.EventPlayed -= OnPlaybackEventPlayed;
                _playback.Dispose();
                _playback = null;
            }
            _outputDevice?.Dispose();
            _outputDevice = null;
            GC.SuppressFinalize(this);
        }

        private sealed class FilteringOutputDevice : IOutputDevice
        {
            private readonly MidiPlayer _player;

            public event EventHandler<MidiEventSentEventArgs>? EventSent;

            public FilteringOutputDevice(MidiPlayer player)
            {
                _player = player;
            }

            public void SendEvent(MidiEvent midiEvent)
            {
                var target = _player._outputDevice;
                if (target == null) return;

                if (midiEvent is ChannelEvent channelEvent)
                {
                    int ch = channelEvent.Channel;
                    bool hasSolo = _player._channelSolos.Any(s => s);
                    bool isMuted = _player._channelMutes[ch] || (hasSolo && !_player._channelSolos[ch]);

                    if (isMuted && (channelEvent is NoteOnEvent or NoteOffEvent))
                    {
                        return; // Suppress note from reaching hardware output
                    }

                    // Vector Quad Note Replication: When Vector Quad playback is enabled on PSS-790 and event is NoteOn/NoteOff on Channel 0,
                    // send the note to Channel 0 and replicate to Channels 1, 2, 3 (Voice B, C, D)
                    if (_player.VectorQuadPlaybackEnabled && ch == 0 && (channelEvent is NoteOnEvent or NoteOffEvent))
                    {
                        try
                        {
                            target.SendEvent(channelEvent);
                            EventSent?.Invoke(this, new MidiEventSentEventArgs(channelEvent));

                            for (byte qCh = 1; qCh <= 3; qCh++)
                            {
                                bool qMuted = _player._channelMutes[qCh] || (hasSolo && !_player._channelSolos[qCh]);
                                if (qMuted) continue;

                                ChannelEvent qEvt = channelEvent is NoteOnEvent nOn
                                    ? new NoteOnEvent(nOn.NoteNumber, nOn.Velocity) { Channel = (FourBitNumber)qCh }
                                    : new NoteOffEvent(((NoteOffEvent)channelEvent).NoteNumber, ((NoteOffEvent)channelEvent).Velocity) { Channel = (FourBitNumber)qCh };

                                target.SendEvent(qEvt);
                                EventSent?.Invoke(this, new MidiEventSentEventArgs(qEvt));
                            }
                        }
                        catch
                        {
                            _player.HandleDeviceDisconnected();
                        }
                        return;
                    }

                    // Proportional Mixer Scaling: scale incoming file CC 7 by user channel volume slider %
                    if (channelEvent is ControlChangeEvent cc && cc.ControlNumber == 7)
                    {
                        _player._lastFileChannelVolumes[ch] = (byte)cc.ControlValue;
                        byte userVolume = _player._channelVolumes[ch];
                        int scaled = (int)Math.Round((double)cc.ControlValue * userVolume / 100.0);
                        scaled = Math.Clamp(scaled, 0, 127);

                        var scaledCc = new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)scaled)
                        {
                            Channel = cc.Channel
                        };
                        try
                        {
                            target.SendEvent(scaledCc);
                            EventSent?.Invoke(this, new MidiEventSentEventArgs(scaledCc));
                        }
                        catch
                        {
                            _player.HandleDeviceDisconnected();
                        }
                        return;
                    }
                }

                try
                {
                    target.SendEvent(midiEvent);
                    EventSent?.Invoke(this, new MidiEventSentEventArgs(midiEvent));
                }
                catch
                {
                    _player.HandleDeviceDisconnected();
                }
            }

            public void PrepareForEventsSending()
            {
                _player._outputDevice?.PrepareForEventsSending();
            }

            public void Dispose()
            {
                // MidiPlayer manages the lifecycle of the underlying target device
            }
        }
    }
}
