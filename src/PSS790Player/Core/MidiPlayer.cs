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
        private OutputDevice? _outputDevice;
        private Playback? _playback;
        private MidiFile? _currentFile;
        private readonly object _lock = new();

        private readonly HashSet<(int Channel, byte Note)> _activeNotes = new();
        private readonly bool[] _channelMutes = new bool[16];
        private readonly bool[] _channelSolos = new bool[16];
        private readonly byte[] _channelVolumes = new byte[16];

        public event EventHandler<ChannelActivityEventArgs>? ChannelActivity;
        public event EventHandler<PlaybackStateEventArgs>? PlaybackStateChanged;

        public bool IsPlaying => _playback?.IsRunning ?? false;
        public bool IsPaused => (!IsPlaying) && (_playback?.GetCurrentTime<MetricTimeSpan>().TotalMilliseconds > 0);
        public TimeSpan TotalDuration => _currentFile?.GetDuration<MetricTimeSpan>() ?? TimeSpan.Zero;
        public TimeSpan CurrentPosition => _playback?.GetCurrentTime<MetricTimeSpan>() ?? TimeSpan.Zero;

        public MidiPlayer()
        {
            for (int i = 0; i < 16; i++)
            {
                _channelVolumes[i] = 100;
            }
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

        public void SetOutputDevice(string deviceName)
        {
            lock (_lock)
            {
                var wasPlaying = IsPlaying;
                var currentPos = CurrentPosition;

                Stop();
                _outputDevice?.Dispose();
                _outputDevice = null;

                try
                {
                    _outputDevice = OutputDevice.GetByName(deviceName);
                }
                catch
                {
                    _outputDevice = null;
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

            _playback?.Dispose();
            _playback = _currentFile.GetPlayback(_outputDevice);

            _playback.EventCallback = (midiEvent, rawTime, totalTime) =>
            {
                if (midiEvent is ChannelEvent channelEvent)
                {
                    int ch = channelEvent.Channel;

                    // Mute / Solo handling
                    bool hasSolo = _channelSolos.Any(s => s);
                    bool isMuted = _channelMutes[ch] || (hasSolo && !_channelSolos[ch]);

                    if (isMuted && channelEvent is NoteOnEvent or NoteOffEvent)
                    {
                        return null; // Suppress note event
                    }

                    if (channelEvent is NoteOnEvent noteOn)
                    {
                        if (noteOn.Velocity > 0)
                        {
                            lock (_activeNotes)
                            {
                                _activeNotes.Add((ch, (byte)noteOn.NoteNumber));
                            }
                            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                            {
                                Channel = ch,
                                IsActive = true,
                                Velocity = (byte)noteOn.Velocity
                            });
                        }
                        else
                        {
                            lock (_activeNotes)
                            {
                                _activeNotes.Remove((ch, (byte)noteOn.NoteNumber));
                            }
                            ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                            {
                                Channel = ch,
                                IsActive = false,
                                Velocity = 0
                            });
                        }
                    }
                    else if (channelEvent is NoteOffEvent noteOff)
                    {
                        lock (_activeNotes)
                        {
                            _activeNotes.Remove((ch, (byte)noteOff.NoteNumber));
                        }
                        ChannelActivity?.Invoke(this, new ChannelActivityEventArgs
                        {
                            Channel = ch,
                            IsActive = false,
                            Velocity = 0
                        });
                    }
                }

                return midiEvent;
            };

            _playback.Finished += (s, e) =>
            {
                SendPanicNoteOff();
                NotifyState();
            };
        }

        public void Play()
        {
            lock (_lock)
            {
                if (_playback != null && !_playback.IsRunning)
                {
                    _playback.Start();
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
                _outputDevice?.SendEvent(midiEvent);
            }
        }

        public void SendVoiceOverride(int channel, int voiceNumber)
        {
            lock (_lock)
            {
                if (_outputDevice == null || channel < 0 || channel > 14) return; // Ch 15/16 is drum, ignores PC

                if (Constants.Pss790Voices.TryGetValue(voiceNumber, out var voiceInfo))
                {
                    var pc = new ProgramChangeEvent((SevenBitNumber)voiceInfo.MidiProgram)
                    {
                        Channel = (FourBitNumber)channel
                    };
                    _outputDevice.SendEvent(pc);
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
                        var cc = new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)volume)
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

                // 2. Clear all 16 channel activity callbacks
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
            Stop();
            _playback?.Dispose();
            _outputDevice?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
