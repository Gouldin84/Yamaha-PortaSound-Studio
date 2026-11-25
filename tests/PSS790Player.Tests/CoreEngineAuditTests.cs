using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class CoreEngineAuditTests : IDisposable
    {
        private readonly string _testDir;

        public CoreEngineAuditTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "CoreEngineAudit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        private sealed class ThreadSafeMockDevice : IOutputDevice
        {
            private readonly object _lock = new();
            private readonly List<MidiEvent> _sentEvents = new();

            public event EventHandler<MidiEventSentEventArgs>? EventSent;

            public void SendEvent(MidiEvent midiEvent)
            {
                lock (_lock)
                {
                    _sentEvents.Add(midiEvent);
                }
                EventSent?.Invoke(this, new MidiEventSentEventArgs(midiEvent));
            }

            public List<MidiEvent> SnapshotEvents()
            {
                lock (_lock)
                {
                    return _sentEvents.ToList();
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _sentEvents.Clear();
                }
            }

            public bool WaitForEvent(Func<MidiEvent, bool> predicate, int timeoutMs = 1500)
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    lock (_lock)
                    {
                        if (_sentEvents.Any(predicate))
                            return true;
                    }
                    Thread.Sleep(10);
                }
                return false;
            }

            public void PrepareForEventsSending() { }
            public void Dispose() { }
        }

        #region Section 1: MidiPlayer Concurrency, Lifecycle, Framing & Filtering

        [Fact]
        public async Task MidiPlayer_ConcurrentStress_PlayStopSeekLoadDisconnect_DoesNotDeadlock()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);

            var midi = new MidiFile();
            midi.TimeDivision = new TicksPerQuarterNoteTimeDivision(480);
            var trk = new TrackChunk();
            for (int i = 0; i < 32; i++)
            {
                trk.Events.Add(new NoteOnEvent((SevenBitNumber)(48 + (i % 24)), (SevenBitNumber)90) { Channel = (FourBitNumber)(i % 4), DeltaTime = i == 0 ? 0 : 30 });
                trk.Events.Add(new NoteOffEvent((SevenBitNumber)(48 + (i % 24)), (SevenBitNumber)0) { Channel = (FourBitNumber)(i % 4), DeltaTime = 30 });
            }
            midi.Chunks.Add(trk);

            player.LoadMidi(midi);
            player.SetLoop(true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var tasks = new[]
            {
                Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        player.Play();
                        Thread.Sleep(5);
                        player.Pause();
                        Thread.Sleep(5);
                        player.Stop();
                    }
                }),
                Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        player.Seek(TimeSpan.FromMilliseconds(150));
                        player.SetChannelVolume(0, 75);
                        player.SetChannelMute(1, true);
                        player.SetChannelMute(1, false);
                        Thread.Sleep(7);
                    }
                }),
                Task.Run(() =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        player.SetOutputDevice(mockDev);
                        Thread.Sleep(12);
                    }
                })
            };

            var allTasks = Task.WhenAll(tasks);
            var completedTask = await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(completedTask == allTasks, "MidiPlayer deadlocked under concurrent Play/Stop/Seek/SetOutputDevice stress.");
            await allTasks;
            player.Stop();
        }

        [Fact]
        public void MidiPlayer_Seek_RestoresMidSongProgramAndControlChanges()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);

            var midi = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            var trk = new TrackChunk();
            trk.Events.Add(new ProgramChangeEvent((SevenBitNumber)1) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)0, DeltaTime = 480 });
            trk.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)50) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 1520 });
            midi.Chunks.Add(trk);

            player.LoadMidi(midi);
            player.Play();
            mockDev.Clear();

            player.Seek(TimeSpan.FromMilliseconds(700));
            player.Stop();

            var events = mockDev.SnapshotEvents();
            var pc = events.OfType<ProgramChangeEvent>().LastOrDefault();
            Assert.NotNull(pc);
            Assert.Equal((SevenBitNumber)40, pc.ProgramNumber);

            var cc7 = events.OfType<ControlChangeEvent>().Where(c => c.ControlNumber == 7).LastOrDefault();
            Assert.NotNull(cc7);
            Assert.Equal((SevenBitNumber)50, cc7.ControlValue);
        }

        [Fact]
        public void MidiPlayer_Stop_Then_Play_Resumes_And_IsPausedFalseAtEof()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);

            var finishedSignal = new ManualResetEventSlim(false);
            player.PlaybackFinished += (_, _) => finishedSignal.Set();

            var midi = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 40 }
            ));
            player.LoadMidi(midi);

            player.Play();
            Assert.True(player.IsPlaying);

            player.Stop();
            Assert.False(player.IsPlaying);
            Assert.False(player.IsPaused);

            // Subsequent Play() must resume cleanly
            player.Play();
            Assert.True(finishedSignal.Wait(TimeSpan.FromSeconds(2)), "Playback did not finish after Stop() -> Play().");
            Thread.Sleep(30);

            Assert.False(player.IsPlaying);
            Assert.False(player.IsPaused);
        }

        [Fact]
        public void MidiPlayer_SendPanicNoteOff_SendsAllSoundOff_ModulationZero_AndPanicControllers()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);
            mockDev.Clear();

            player.SendPanicNoteOff();

            var events = mockDev.SnapshotEvents();
            var ccs = events.OfType<ControlChangeEvent>().ToList();
            var pitchBends = events.OfType<PitchBendEvent>().ToList();

            for (int ch = 0; ch < 16; ch++)
            {
                var channelFourBit = (FourBitNumber)ch;
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 120 && c.ControlValue == 0);
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 123 && c.ControlValue == 0);
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 64 && c.ControlValue == 0);
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 121 && c.ControlValue == 0);
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 1 && c.ControlValue == 0);
                Assert.Contains(pitchBends, pb => pb.Channel == channelFourBit && pb.PitchValue == 8192);
            }
        }

        [Fact]
        public void MidiPlayer_SendSysEx_EnsuresTrailingF7_AndStripsLeadingF0()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);

            mockDev.Clear();
            player.SendSysEx(new byte[] { 0xF0, 0x43, 0x76, 0x00, 0x05, 0xF7 });
            var sysEx1 = Assert.Single(mockDev.SnapshotEvents().OfType<NormalSysExEvent>());
            Assert.Equal(0x43, sysEx1.Data[0]);
            Assert.Equal(0xF7, sysEx1.Data[^1]);
            Assert.True(sysEx1.Completed);

            mockDev.Clear();
            player.SendSysEx(new byte[] { 0x43, 0x76, 0x00, 0x05 });
            var sysEx2 = Assert.Single(mockDev.SnapshotEvents().OfType<NormalSysExEvent>());
            Assert.Equal(0x43, sysEx2.Data[0]);
            Assert.Equal(0xF7, sysEx2.Data[^1]);
            Assert.True(sysEx2.Completed);
        }

        [Fact]
        public void MidiPlayer_FilteringOutputDevice_ForwardsNoteOff_WhenChannelIsMuted()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);

            var midi = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 10 }
            ));
            player.LoadMidi(midi);
            player.SetChannelMute(0, true);

            mockDev.Clear();
            player.Play();
            Assert.True(mockDev.WaitForEvent(e => e is NoteOffEvent no && no.Channel == (FourBitNumber)0 && no.NoteNumber == 60));
            player.Stop();

            var events = mockDev.SnapshotEvents();
            Assert.DoesNotContain(events.OfType<NoteOnEvent>(), n => n.Velocity > 0 && n.Channel == (FourBitNumber)0);
            Assert.Contains(events.OfType<NoteOffEvent>(), n => n.Channel == (FourBitNumber)0 && n.NoteNumber == 60);
        }

        [Fact]
        public void MidiPlayer_Pss680_ChannelVolumeZero_SuppressesNoteOn_AndOmitsCc7()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);
            player.ActiveModel = "pss680";
            player.SetChannelVolume(0, 0);

            var midi = new MidiFile(new TrackChunk(
                new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 10 }
            ));
            player.LoadMidi(midi);

            mockDev.Clear();
            player.Play();
            Assert.True(mockDev.WaitForEvent(e => e is NoteOffEvent no && no.Channel == (FourBitNumber)0));
            player.Stop();

            var events = mockDev.SnapshotEvents();
            Assert.DoesNotContain(events.OfType<NoteOnEvent>(), n => n.Velocity > 0 && n.Channel == (FourBitNumber)0);
            Assert.DoesNotContain(events.OfType<ControlChangeEvent>(), cc => cc.ControlNumber == 7);
        }

        [Fact]
        public void MidiPlayer_VectorQuadPlayback_AllowsSoloingVoicesBCD_WhenCh0Muted_AndFlushesOnDisable()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);

            player.VectorQuadPlaybackEnabled = true;
            player.SetChannelSolo(1, true); // Mutes Ch 0, 2, 3 while soloing Voice B (Ch 1)

            var midi = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 20 }
            ));
            player.LoadMidi(midi);

            mockDev.Clear();
            player.Play();
            Assert.True(mockDev.WaitForEvent(e => e is NoteOnEvent no && no.Channel == (FourBitNumber)1 && no.Velocity > 0));
            player.Stop();

            var playedEvents = mockDev.SnapshotEvents();
            Assert.DoesNotContain(playedEvents.OfType<NoteOnEvent>(), n => n.Velocity > 0 && n.Channel == (FourBitNumber)0);
            Assert.Contains(playedEvents.OfType<NoteOnEvent>(), n => n.Velocity > 0 && n.Channel == (FourBitNumber)1);

            mockDev.Clear();
            player.VectorQuadPlaybackEnabled = false;

            var ccs = mockDev.SnapshotEvents().OfType<ControlChangeEvent>().ToList();
            for (int ch = 1; ch <= 3; ch++)
            {
                var channelFourBit = (FourBitNumber)ch;
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 123);
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 120);
                Assert.Contains(ccs, c => c.Channel == channelFourBit && c.ControlNumber == 64);
            }
        }

        #endregion

        #region Section 2: PolyphonyOptimizer Accuracy & Budgets

        [Fact]
        public void PolyphonyOptimizer_LengthConverter_ClampsDrumDecayAccuratelyAcrossTempoChanges()
        {
            var midi = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            var trk = new TrackChunk();
            trk.Events.Add(new SetTempoEvent(500_000) { DeltaTime = 0 });      // 120 BPM
            trk.Events.Add(new SetTempoEvent(1_000_000) { DeltaTime = 960 });  // 60 BPM at tick 960
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)57, (SevenBitNumber)90) { Channel = (FourBitNumber)15, DeltaTime = 240 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)57, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 800 });
            midi.Chunks.Add(trk);

            var optimizer = new PolyphonyOptimizer(true, 28, "pss790");
            var optimized = optimizer.Optimize(midi);

            var drumNote = Assert.Single(optimized.GetNotes().Where(n => n.Channel == (FourBitNumber)15));
            Assert.Equal(120, drumNote.Length); // 250ms at 60 BPM (480 TPQN) == 120 ticks
        }

        [Fact]
        public void PolyphonyOptimizer_GlobalCrossTrackDeduplication_PreventsType1CrashAndDoubleTrigger()
        {
            var midi = new MidiFile();
            // Two separate tracks in a Type-1 MIDI file striking the exact same pitch/channel/tick
            var trk1 = new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)50) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 200 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)70) { Channel = (FourBitNumber)0, DeltaTime = 50 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 200 }
            );
            var trk2 = new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)110) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 400 }
            );
            midi.Chunks.Add(trk1);
            midi.Chunks.Add(trk2);

            var optimizer = new PolyphonyOptimizer(true, 28, "pss790");
            var optimized = optimizer.Optimize(midi);

            var notesAtZero = optimized.GetNotes().Where(n => n.Time == 0 && n.Channel == (FourBitNumber)0 && n.NoteNumber == 60).ToList();
            var survivor = Assert.Single(notesAtZero);
            Assert.Equal((SevenBitNumber)110, survivor.Velocity);
        }

        [Fact]
        public void PolyphonyOptimizer_Pss680_SeparatesFmAndPcmPercussionBudgets_WithIncidentalDrums()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk();

            // 12 simultaneous melodic FM notes on Channels 0..11
            for (int ch = 0; ch < 12; ch++)
            {
                trk.Events.Add(new NoteOnEvent((SevenBitNumber)(50 + ch), (SevenBitNumber)90) { Channel = (FourBitNumber)ch, DeltaTime = 0 });
                trk.Events.Add(new NoteOffEvent((SevenBitNumber)(50 + ch), (SevenBitNumber)0) { Channel = (FourBitNumber)ch, DeltaTime = 480 });
            }

            // 4 simultaneous PCM percussion notes on Ch 15 INCLUDING IncidentalDrums (55 = Cowbell, 71 = Tambourine)
            byte[] drumNotes = { 45, 49, 55, 71 };
            foreach (byte dNote in drumNotes)
            {
                trk.Events.Add(new NoteOnEvent((SevenBitNumber)dNote, (SevenBitNumber)100) { Channel = (FourBitNumber)15, DeltaTime = 0 });
                trk.Events.Add(new NoteOffEvent((SevenBitNumber)dNote, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 480 });
            }
            midi.Chunks.Add(trk);

            var optimizer = new PolyphonyOptimizer(true, 12, "pss680");
            var optimized = optimizer.Optimize(midi);

            var notes = optimized.GetNotes().ToList();
            Assert.Equal(12, notes.Count(n => n.Channel != (FourBitNumber)15));
            Assert.Equal(4, notes.Count(n => n.Channel == (FourBitNumber)15));
        }

        [Fact]
        public void PolyphonyOptimizer_Pss680_ExcludesChannel15Drums_FromLeadAndBassProtection()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk();

            trk.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)80) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)38, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 480 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)79, (SevenBitNumber)80) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)79, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 480 });

            trk.Events.Add(new NoteOnEvent((SevenBitNumber)40, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)40, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 480 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)72, (SevenBitNumber)90) { Channel = (FourBitNumber)1, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)72, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 480 });

            for (int i = 0; i < 12; i++)
            {
                trk.Events.Add(new NoteOnEvent((SevenBitNumber)(55 + (i % 5)), (SevenBitNumber)60) { Channel = (FourBitNumber)(2 + (i % 10)), DeltaTime = 0 });
                trk.Events.Add(new NoteOffEvent((SevenBitNumber)(55 + (i % 5)), (SevenBitNumber)0) { Channel = (FourBitNumber)(2 + (i % 10)), DeltaTime = 480 });
            }
            midi.Chunks.Add(trk);

            var optimizer = new PolyphonyOptimizer(true, 12, "pss680");
            var optimized = optimizer.Optimize(midi);

            var notes = optimized.GetNotes().ToList();
            Assert.Contains(notes, n => n.Channel == (FourBitNumber)0 && n.NoteNumber == 40);
            Assert.Contains(notes, n => n.Channel == (FourBitNumber)1 && n.NoteNumber == 72);
        }

        #endregion

        #region Section 3: SysEx Security, Framing & Protocol Hardening

        [Fact]
        public void PSS680Voice_And_Parser_RoundTrip_ValidatesWireNibbleChecksum_AndClampsFields()
        {
            var voice = new PSS680Voice
            {
                Name = "Audit Lead",
                BankNumber = 2,
                Feedback = 99 // Out-of-range -> must clamp to 7 instead of wrapping to 3
            };
            voice.Carrier.AttackRate = 99; // Out-of-range -> must clamp to 63 instead of wrapping
            voice.Carrier.PanelLevel = 85;

            byte[] sysex = voice.ToSysExMessage();
            Assert.Equal(Pss680Constants.Format00TotalSysExLength, sysex.Length);
            Assert.Equal(0xF0, sysex[0]);
            Assert.Equal(0xF7, sysex[^1]);

            var parsedPacket = Pss680SysExParser.Parse(sysex);
            Assert.True(parsedPacket.IsChecksumValid);
            Assert.Equal(Pss680SysExFormat.FmVoiceData, parsedPacket.Format);

            var restoredVoice = PSS680Voice.FromSysExMessage(sysex);
            Assert.Equal(2, restoredVoice.BankNumber);
            Assert.Equal(7, restoredVoice.Feedback);
            Assert.Equal(63, restoredVoice.Carrier.AttackRate);
            Assert.Equal(85, restoredVoice.Carrier.PanelLevel);
        }

        [Fact]
        public void PSS680SysExParser_RejectsCorruptNibbles_HighBitChecksum_AndWrongPayloadLength()
        {
            Assert.Throws<FormatException>(() => Pss680SysExParser.Denibblize(new byte[] { 0x05, 0x12 }));

            byte[] badChecksumMsb = new byte[72];
            badChecksumMsb[0] = 0xF0;
            badChecksumMsb[1] = 0x43;
            badChecksumMsb[2] = 0x76;
            badChecksumMsb[3] = 0x00;
            badChecksumMsb[70] = 0x85;
            badChecksumMsb[71] = 0xF7;
            Assert.Throws<FormatException>(() => Pss680SysExParser.Parse(badChecksumMsb));

            Assert.Throws<ArgumentException>(() => Pss680SysExParser.CreateSysEx(0x80, new byte[33]));
            Assert.Throws<ArgumentException>(() => Pss680SysExParser.CreateSysEx(Pss680Constants.FormatFmVoice, new byte[5]));
        }

        [Fact]
        public void SysExManager_ExtractPackets_StripsRealtimeBytes_RejectsCorruptStatus_AndNoFallback()
        {
            // Packet with interleaved Active Sensing (0xFE) + truncated packet + unframed trailing bytes
            byte[] stream =
            {
                0xF0, 0x43, 0xFE, 0x76, 0x00, 0x01, 0x02, 0x03, 0xF7, // Valid with 0xFE inside
                0xF0, 0x43, 0x90, 0x3C, 0x64, 0xF7,                   // Corrupt status 0x90 inside -> rejected
                0x43, 0x76, 0x00, 0x01                                // Unframed -> no fallback wrapping
            };

            var packets = SysExManager.ExtractPackets(stream);
            var single = Assert.Single(packets);
            Assert.Equal(new byte[] { 0xF0, 0x43, 0x76, 0x00, 0x01, 0x02, 0x03, 0xF7 }, single);
        }

        [Fact]
        public async Task SysExManager_TransmitDumpAsync_SendsSingleF7_AndHandlesCancellationCleanly()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            player.SetOutputDevice(mockDev);
            using var rec = new MidiRecorder();
            using var sysex = new SysExManager(player, rec);

            var dumpPath = Path.Combine(_testDir, "multi_packet.syx");
            byte[] twoPackets =
            {
                0xF0, 0x43, 0x76, 0x00, 0x01, 0x02, 0x7D, 0xF7,
                0xF0, 0x43, 0x76, 0x00, 0x03, 0x04, 0x79, 0xF7
            };
            await File.WriteAllBytesAsync(dumpPath, twoPackets);

            mockDev.Clear();
            bool ok = await sysex.TransmitDumpAsync(dumpPath, packetDelayMs: 5);
            Assert.True(ok);

            var sentSysEx = mockDev.SnapshotEvents().OfType<NormalSysExEvent>().ToList();
            Assert.Equal(2, sentSysEx.Count);
            Assert.All(sentSysEx, pkt =>
            {
                Assert.NotEqual(0xF0, pkt.Data[0]);
                Assert.Equal(0xF7, pkt.Data[^1]);
                Assert.NotEqual(0xF7, pkt.Data[^2]); // Exactly one trailing 0xF7
                Assert.True(pkt.Completed);
            });

            // Verify cancellation returns false gracefully without throwing OperationCanceledException
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelledResult = await sysex.TransmitDumpAsync(dumpPath, packetDelayMs: 50, cancellationToken: cts.Token);
            Assert.False(cancelledResult);
        }

        [Fact]
        public void SysExManager_RenameDump_RejectsPathTraversal_Dots_AndReservedNames()
        {
            using var player = new MidiPlayer();
            using var rec = new MidiRecorder();
            using var sysex = new SysExManager(player, rec);

            var validFile = Path.Combine(_testDir, "preset.syx");
            File.WriteAllBytes(validFile, new byte[] { 0xF0, 0x43, 0x76, 0x00, 0xF7 });

            Assert.Throws<ArgumentException>(() => sysex.RenameDump(validFile, "../escape.syx", _testDir));
            Assert.Throws<ArgumentException>(() => sysex.RenameDump(validFile, "..\\escape.syx", _testDir));
            Assert.Throws<ArgumentException>(() => sysex.RenameDump(validFile, "..", _testDir));
            Assert.Throws<ArgumentException>(() => sysex.RenameDump(validFile, "CON", _testDir));
            Assert.Throws<ArgumentException>(() => sysex.RenameDump(validFile, "sub/dir/preset.syx", _testDir));
        }

        #endregion

        #region Section 4: Remapper Timing, Relocation, Velocity Baking & ReverseRemapper

        [Fact]
        public void Remapper_UnconditionalMelodicRelocation_WithoutForceRemap_PreservesCh15Melody()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk();
            // Drumless GM file using Channel 15 (GM Ch 16) with note 60 (which also exists in Pss790Drums!)
            trk.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 480 });
            midi.Chunks.Add(trk);

            // ForceRemap = false must still detect GeneralMidi and relocate Ch 15 -> Ch 0
            var remapped = Remapper.Remap(midi, new RemapperOptions { ForceRemap = false });
            var note = Assert.Single(remapped.GetNotes());

            Assert.Equal((FourBitNumber)0, note.Channel);
            Assert.Equal((SevenBitNumber)60, note.NoteNumber);
        }

        [Fact]
        public void Remapper_EnsureSetupPreRoll_ShiftsNotesAndMidSongAutomationTogether_KeepingTick0SetupAt0()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk();
            // Tick 0: Setup events + Note 1
            trk.Events.Add(new ProgramChangeEvent((SevenBitNumber)10) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)110) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 200 });

            // Tick 300: Mid-song tempo change + Note 2
            trk.Events.Add(new SetTempoEvent(400_000) { DeltaTime = 100 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 200 });
            midi.Chunks.Add(trk);

            var remapped = Remapper.Remap(midi, new RemapperOptions { EnsureSetupPreRoll = true });
            var timedEvents = remapped.GetTimedEvents().ToList();

            Assert.Equal(0, timedEvents.Single(te => te.Event is ProgramChangeEvent).Time);
            Assert.Equal(0, timedEvents.Single(te => te.Event is ControlChangeEvent cc && cc.ControlNumber == 7).Time);

            var notes = remapped.GetNotes().OrderBy(n => n.Time).ToList();
            Assert.Equal(48, notes[0].Time);
            Assert.Equal(348, notes[1].Time);

            // Mid-song tempo event must shift by 48 ticks alongside Note 2 (300 -> 348)
            var tempoEvt = timedEvents.Single(te => te.Event is SetTempoEvent);
            Assert.Equal(348, tempoEvt.Time);
        }

        [Fact]
        public void Remapper_Pss680_Type1VelocityBaking_ScalesBounds_CullsZeroVolume_AndStripsCc7()
        {
            var midi = new MidiFile();
            // Track 0: Controller automation track (Type-1)
            var ctrlTrack = new TrackChunk(
                new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)127) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new ControlChangeEvent((SevenBitNumber)11, (SevenBitNumber)127) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 480 }
            );
            // Track 1: Notes (full volume at tick 0; muted fade-out at tick 480)
            var noteTrack = new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)127) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 },
                new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 240 },
                new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 }
            );
            midi.Chunks.Add(ctrlTrack);
            midi.Chunks.Add(noteTrack);

            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss680" });

            var survivingNotes = remapped.GetNotes().ToList();
            var loudNote = Assert.Single(survivingNotes);
            Assert.Equal((SevenBitNumber)60, loudNote.NoteNumber);
            Assert.Equal((SevenBitNumber)118, loudNote.Velocity);

            // Zero-volume fade-out note must be completely culled (no orphaned NoteOffs)
            Assert.Empty(remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<NoteOffEvent>().Where(no => no.NoteNumber == 64));

            // CC 7 and CC 11 must be stripped from PSS-680 output
            Assert.Empty(remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>().Where(cc => (int)cc.ControlNumber is 7 or 11));
        }

        [Fact]
        public void Remapper_Pss790_PreservesPanCc10_AndStripsBakedKeyboardSustainCc64()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 0 }, // Acoustic Grand Piano
                new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)24) { Channel = (FourBitNumber)0, DeltaTime = 0 }, // Pan Left
                new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127) { Channel = (FourBitNumber)0, DeltaTime = 0 }, // Unreleased sustain
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 120 }
            );
            midi.Chunks.Add(trk);

            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790", EmulateSustainPedal = true });
            var ccs = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>().ToList();

            Assert.Contains(ccs, c => c.ControlNumber == 10 && c.ControlValue == 24);
            Assert.DoesNotContain(ccs, c => c.ControlNumber == 64);
        }

        [Fact]
        public void Remapper_SerialCcThinning_RemovesDuplicates_PreservesExtremes_AndFinalRampEndpoint()
        {
            var midi = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            var trk = new TrackChunk(
                new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)50) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)50) { Channel = (FourBitNumber)0, DeltaTime = 5 },
                new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 5 },
                new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)127) { Channel = (FourBitNumber)0, DeltaTime = 5 },
                new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)64) { Channel = (FourBitNumber)0, DeltaTime = 5 } // Final ramp settling value
            );
            midi.Chunks.Add(trk);

            var remapped = Remapper.Remap(midi, new RemapperOptions { FilterUnsupportedControllers = false });
            var ccValues = remapped.GetTimedEvents()
                .Where(te => te.Event is ControlChangeEvent cc && cc.ControlNumber == 1)
                .Select(te => (byte)((ControlChangeEvent)te.Event).ControlValue)
                .ToList();

            Assert.Equal(new byte[] { 50, 0, 127, 64 }, ccValues);
        }

        [Fact]
        public void ReverseRemapper_Pss680_Preserves1to1PitchBend_AndTimestamps()
        {
            var pss680Midi = new MidiFile(new TrackChunk(
                new TextEvent(Remapper.Pss680MarkerText) { DeltaTime = 0 },
                new PitchBendEvent(10000) { Channel = (FourBitNumber)0, DeltaTime = 120 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 240 }
            ));

            var gmMidi = ReverseRemapper.ReverseRemapToGeneralMidi(pss680Midi, "pss680");
            var pb = Assert.Single(gmMidi.GetTimedEvents().Where(te => te.Event is PitchBendEvent));
            Assert.Equal(120, pb.Time);
            Assert.Equal(10000, ((PitchBendEvent)pb.Event).PitchValue);
        }

        #endregion

        #region Section 5: MidiHealthInspector, BatchConverter, Stores & Paths

        [Fact]
        public void MidiHealthInspector_GmMelodicCh15_CountsOutOfBounds_AndPss680SeparatesBudgets()
        {
            var gmMidi = new MidiFile(new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)15, (SevenBitNumber)100) { Channel = (FourBitNumber)15, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)15, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 480 }
            ));

            var gmReport = MidiHealthInspector.Analyze(gmMidi, null, "GmCh15.mid", "pss790");
            Assert.Equal(1, gmReport.OutOfBoundsNotesCount);

            // PSS-680: 10 FM notes + 3 PCM drums = 13 total notes, but within 12 FM and 4 PCM limits ("Moderate", not "HighPolyphony")
            var pss680StressTrk = new TrackChunk();
            for (int ch = 0; ch < 10; ch++)
            {
                pss680StressTrk.Events.Add(new NoteOnEvent((SevenBitNumber)(50 + ch), (SevenBitNumber)80) { Channel = (FourBitNumber)ch, DeltaTime = 0 });
            }
            for (int d = 0; d < 3; d++)
            {
                pss680StressTrk.Events.Add(new NoteOnEvent((SevenBitNumber)(45 + d), (SevenBitNumber)80) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            }
            pss680StressTrk.Events.Add(new NoteOffEvent((SevenBitNumber)50, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 480 });
            for (int ch = 1; ch < 10; ch++)
            {
                pss680StressTrk.Events.Add(new NoteOffEvent((SevenBitNumber)(50 + ch), (SevenBitNumber)0) { Channel = (FourBitNumber)ch, DeltaTime = 0 });
            }
            for (int d = 0; d < 3; d++)
            {
                pss680StressTrk.Events.Add(new NoteOffEvent((SevenBitNumber)(45 + d), (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            }

            var report680 = MidiHealthInspector.Analyze(new MidiFile(pss680StressTrk), null, "FmPcmSplit.mid", "pss680");
            Assert.Equal(13, report680.PeakPolyphony);
            Assert.Equal("Moderate", report680.VoiceStressRating);
            Assert.NotEqual("warning", report680.BadgeType);
        }

        [Fact]
        public async Task BatchConverter_ConvertDirectoryDualAsync_DoesNotMutateSharedRemapperOptions()
        {
            var srcDir = Path.Combine(_testDir, "GM");
            var dir790 = Path.Combine(_testDir, "PSS790");
            var dir680 = Path.Combine(_testDir, "PSS680");
            Directory.CreateDirectory(srcDir);

            var srcMidi = new MidiFile(new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 480 }
            ));
            var srcFile = Path.Combine(srcDir, "Song.mid");
            srcMidi.Write(srcFile);

            var sharedOptions = new RemapperOptions { DefaultReverbBoost = true };
            await BatchConverter.ConvertDirectoryDualAsync(srcDir, dir790, dir680, sharedOptions);

            var out790 = MidiFile.Read(Path.Combine(dir790, "Song.mid"));
            var out680 = MidiFile.Read(Path.Combine(dir680, "Song.mid"));

            Assert.True(Remapper.HasPss790Marker(out790));
            Assert.False(Remapper.HasPss680Marker(out790));
            Assert.Contains(out790.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>(), cc => cc.ControlNumber == 91);

            Assert.True(Remapper.HasPss680Marker(out680));
            Assert.DoesNotContain(out680.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>(), cc => cc.ControlNumber == 91);
        }

        [Fact]
        public void VoiceOverrideStore_And_AppPaths_ManageBothModels_AndEnforceDirectoryBoundaries()
        {
            var storePath = Path.Combine(_testDir, "overrides.json");
            var store = new VoiceOverrideStore(storePath);

            store.SetOverride("Rock\\TrackA.mid", 0, 42, "pss680");
            Assert.True(store.HasOverrides("Rock\\TrackA.mid", "pss680"));

            store.RenameFile("Rock\\TrackA.mid", "Rock\\TrackB.mid");
            Assert.False(store.HasOverrides("Rock\\TrackA.mid", "pss680"));
            Assert.Equal(42, store.GetOverrides("Rock\\TrackB.mid", "pss680")[0]);

            store.RemoveOverrides("Rock\\TrackB.mid");
            Assert.False(store.HasOverrides("Rock\\TrackB.mid"));

            var gmRoot = Path.Combine(_testDir, "GM Midi");
            var siblingBackup = Path.Combine(_testDir, "GM Midi Backup", "Song.mid");
            Assert.False(AppPaths.IsPathInsideLibrary(siblingBackup, gmRoot));
        }

        [Fact]
        public void MidiPlayer_StopAndDetachPlayback_DoesNotFireFinished()
        {
            var dev = new ThreadSafeMockDevice();
            var player = new MidiPlayer();

            var deviceField = typeof(MidiPlayer).GetField("_outputDevice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            deviceField?.SetValue(player, dev);

            var midi1 = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 4800 }
            ));

            var midi2 = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 4800 }
            ));

            bool finishedFired = false;
            player.PlaybackFinished += (s, e) => finishedFired = true;

            player.LoadMidi(midi1);
            player.Play();
            Thread.Sleep(50);

            // Switching songs while playing must detach Finished on old playback and NOT fire PlaybackFinished
            player.LoadMidi(midi2);
            player.Play();
            Thread.Sleep(50);

            Assert.False(finishedFired, "Displacing an active playback must not fire PlaybackFinished");
            player.Stop();
        }

        [Fact]
        public void PolyphonyOptimizer_Pss790_StealsMelodicPadsAbove28Voices_ShieldingLeadAndBass()
        {
            var events = new List<MidiEvent>();
            events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)90) { Channel = (FourBitNumber)1, DeltaTime = 0 }); // Bass
            events.Add(new NoteOnEvent((SevenBitNumber)88, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 }); // Lead
            for (int i = 0; i < 30; i++)
            {
                events.Add(new NoteOnEvent((SevenBitNumber)(45 + i), (SevenBitNumber)(50 + i)) { Channel = (FourBitNumber)2, DeltaTime = 0 });
            }
            events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 480 });
            events.Add(new NoteOffEvent((SevenBitNumber)88, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            for (int i = 0; i < 30; i++)
            {
                events.Add(new NoteOffEvent((SevenBitNumber)(45 + i), (SevenBitNumber)0) { Channel = (FourBitNumber)2, DeltaTime = 0 });
            }

            var midi = new MidiFile(new TrackChunk(events));
            var programs = new Dictionary<int, byte> { [0] = 80, [1] = 33, [2] = 48 }; // Lead, Bass, Strings (Tier 1)
            var optimizer = new PolyphonyOptimizer(enabled: true, threshold: 26, model: "pss790");
            var optimized = optimizer.Optimize(midi, programs);

            var surviving = optimized.GetNotes().ToList();
            Assert.Equal(28, surviving.Count);
            Assert.Contains(surviving, n => (int)n.NoteNumber == 36);
            Assert.Contains(surviving, n => (int)n.NoteNumber == 88);
        }

        [Fact]
        public void PolyphonyOptimizer_Pss790_VectorQuadWeighting_BudgetsCh0At4xCost()
        {
            var events = new List<MidiEvent>();
            for (int i = 0; i < 9; i++)
            {
                events.Add(new NoteOnEvent((SevenBitNumber)(48 + i), (SevenBitNumber)(70 + i)) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            }
            for (int i = 0; i < 9; i++)
            {
                events.Add(new NoteOffEvent((SevenBitNumber)(48 + i), (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 480 });
            }

            var midi = new MidiFile(new TrackChunk(events));
            var optimizer = new PolyphonyOptimizer(enabled: true, threshold: 26, model: "pss790");
            var weights = new Dictionary<int, int> { [0] = 4 };
            var optimized = optimizer.Optimize(midi, new Dictionary<int, byte> { [0] = 48 }, weights);

            // 7 * 4 = 28 oscillators max on PSS-790
            Assert.Equal(7, optimized.GetNotes().Count());
        }

        [Fact]
        public void Remapper_ChronologicalProgramTimeline_EvaluatesMidSongInstrumentSwitchForBends()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk(
                // Bar 1: Piano (GM 0) -> acoustic bend must be neutralized to 8192
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new PitchBendEvent(12000) { Channel = (FourBitNumber)0, DeltaTime = 60 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 60 },
                // Bar 2: Mid-song switch to Overdriven Guitar (GM 29) -> guitar bend must be scaled & preserved
                new ProgramChangeEvent((SevenBitNumber)29) { Channel = (FourBitNumber)0, DeltaTime = 120 },
                new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new PitchBendEvent(16383) { Channel = (FourBitNumber)0, DeltaTime = 60 },
                new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 60 }
            );
            midi.Chunks.Add(trk);

            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790", NeutralizeAcousticPitchBends = true, ScalePitchBend = true });
            var pbs = remapped.GetTimedEvents().Where(te => te.Event is PitchBendEvent).ToList();

            Assert.Contains(pbs, te => ((PitchBendEvent)te.Event).PitchValue == 8192);
            Assert.Contains(pbs, te => ((PitchBendEvent)te.Event).PitchValue is >= 9550 and <= 9565);
        }

        [Fact]
        public void Remapper_ContourPreservingBassLift_LiftsSub24PhraseWithoutInvertingIntervals()
        {
            var midi = new MidiFile();
            var trk = new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)1, DeltaTime = 0 }, // Electric Bass
                new NoteOnEvent((SevenBitNumber)14, (SevenBitNumber)95) { Channel = (FourBitNumber)1, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)14, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 120 },
                new NoteOnEvent((SevenBitNumber)19, (SevenBitNumber)95) { Channel = (FourBitNumber)1, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)19, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 120 },
                new NoteOnEvent((SevenBitNumber)26, (SevenBitNumber)95) { Channel = (FourBitNumber)1, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)26, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 120 }
            );
            midi.Chunks.Add(trk);

            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790" });
            var notes = remapped.GetNotes().OrderBy(n => n.Time).Select(n => (byte)n.NoteNumber).ToList();

            // Entire phrase shifts up +12 so 14 < 19 < 26 becomes 26 < 31 < 38 (preserving ascending contour)
            Assert.Equal(new byte[] { 26, 31, 38 }, notes);
        }

        [Fact]
        public void Remapper_Cc10PanThinning_And_EarlyPreRollUniformShift()
        {
            var midi = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            var trk = new TrackChunk(
                new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)20) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)40) { Channel = (FourBitNumber)0, DeltaTime = 5 }, // < 18ms, dropped
                new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)60) { Channel = (FourBitNumber)0, DeltaTime = 5 }, // < 18ms, dropped (Note at tick 10)
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 200 },
                new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 20 } // ramp settle endpoint
            );
            midi.Chunks.Add(trk);

            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790", EnsureSetupPreRoll = true });
            var firstNote = remapped.GetNotes().First();
            Assert.True(firstNote.Time >= 48);

            var panValues = remapped.GetTimedEvents()
                .Where(te => te.Event is ControlChangeEvent cc && cc.ControlNumber == 10)
                .Select(te => (byte)((ControlChangeEvent)te.Event).ControlValue)
                .ToList();
            Assert.DoesNotContain((byte)40, panValues);
            Assert.Contains((byte)20, panValues);
            Assert.Contains((byte)90, panValues);
        }

        [Fact]
        public void ReverseRemapper_Ch9Collision_AvoidsMergingOntoCh0_WhenLowerDensityChannelExists()
        {
            var pssMidi = new MidiFile();
            var trk = new TrackChunk();
            // Ch 0: 4 notes (lead), Ch 1..14 except Ch 14: active; Ch 14: 1 note (least dense), Ch 9: 2 melodic notes, Ch 15: 1 drum note
            for (int c = 0; c < 15; c++)
            {
                if (c == 9) continue;
                int count = c == 0 ? 4 : (c == 14 ? 1 : 2);
                for (int i = 0; i < count; i++)
                {
                    trk.Events.Add(new NoteOnEvent((SevenBitNumber)(60 + i), (SevenBitNumber)90) { Channel = (FourBitNumber)c, DeltaTime = 0 });
                    trk.Events.Add(new NoteOffEvent((SevenBitNumber)(60 + i), (SevenBitNumber)0) { Channel = (FourBitNumber)c, DeltaTime = 120 });
                }
            }
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)65, (SevenBitNumber)90) { Channel = (FourBitNumber)9, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)65, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 120 });
            trk.Events.Add(new NoteOnEvent((SevenBitNumber)45, (SevenBitNumber)100) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            trk.Events.Add(new NoteOffEvent((SevenBitNumber)45, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 120 });
            pssMidi.Chunks.Add(trk);

            var gmMidi = ReverseRemapper.ReverseRemapToGeneralMidi(pssMidi, "pss790");
            var ch0Notes = gmMidi.GetNotes().Where(n => n.Channel == 0).ToList();
            var ch14Notes = gmMidi.GetNotes().Where(n => n.Channel == 14).ToList();

            // Ch 0 lead must stay untouched (4 notes), while Ch 9 melodic note (65) moves to Ch 14 (1 + 1 = 2 notes)
            Assert.Equal(4, ch0Notes.Count);
            Assert.Equal(2, ch14Notes.Count);
            Assert.Contains(ch14Notes, n => n.NoteNumber == 65);
        }

        #endregion
    }
}
