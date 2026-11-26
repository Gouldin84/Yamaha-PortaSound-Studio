using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class JukeboxTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _tempOverridesFile;

        public JukeboxTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"PSS790_JukeboxTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _tempOverridesFile = Path.Combine(_tempDir, "overrides.json");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch { }
        }

        [Fact]
        public void VoiceOverrideStore_SavesAndRetrievesPerSongOverrides()
        {
            var store = new VoiceOverrideStore(_tempOverridesFile);

            var songPath = Path.Combine(AppPaths.GmMidiDir, "Doom", "E1M1.mid");
            Assert.False(store.HasOverrides(songPath));

            // Set custom overrides: Ch 0 -> Voice 15 (Distortion Guitar), Ch 1 -> Voice 84 (Slap Bass)
            store.SetOverride(songPath, 0, 15);
            store.SetOverride(songPath, 1, 84);

            Assert.True(store.HasOverrides(songPath));
            var overrides = store.GetOverrides(songPath);
            Assert.Equal(2, overrides.Count);
            Assert.Equal(15, overrides[0]);
            Assert.Equal(84, overrides[1]);

            // Create a second store instance pointing to the same file to verify disk persistence
            var reloadedStore = new VoiceOverrideStore(_tempOverridesFile);
            Assert.True(reloadedStore.HasOverrides(songPath));
            var reloadedOverrides = reloadedStore.GetOverrides(songPath);
            Assert.Equal(2, reloadedOverrides.Count);
            Assert.Equal(15, reloadedOverrides[0]);
            Assert.Equal(84, reloadedOverrides[1]);

            // Reset overrides
            reloadedStore.ResetOverrides(songPath);
            Assert.False(reloadedStore.HasOverrides(songPath));
            Assert.Empty(reloadedStore.GetOverrides(songPath));
        }

        [Fact]
        public void AppPaths_GetRelativeLibraryPath_FormatsCleanRelativeKeys()
        {
            var internalGm = Path.Combine(AppPaths.BaseMidiDir, "GM Midi", "Doom", "E1M1.mid");
            var rel = AppPaths.GetRelativeLibraryPath(internalGm);
            Assert.Contains("GM Midi", rel);
            Assert.Contains("Doom", rel);
            Assert.Contains("E1M1.mid", rel);

            var externalPath = @"C:\Games\Doom\D_E1M1.mid";
            var extRel = AppPaths.GetRelativeLibraryPath(externalPath);
            Assert.Equal("D_E1M1.mid", extRel);
        }

        private class DummyOutputDevice : IOutputDevice
        {
            public List<MidiEvent> SentEvents { get; } = new();
            public event EventHandler<MidiEventSentEventArgs>? EventSent;
            public void SendEvent(MidiEvent midiEvent) { SentEvents.Add(midiEvent); EventSent?.Invoke(this, new MidiEventSentEventArgs(midiEvent)); }
            public void PrepareForEventsSending() { }
            public void Dispose() { }
        }

        [Fact]
        public void MidiPlayer_InitialStateAndProgressReporting_IsConfiguredProperly()
        {
            using var player = new MidiPlayer();
            Assert.False(player.IsPlaying);
            Assert.False(player.IsPaused);
            Assert.Equal(TimeSpan.Zero, player.CurrentPosition);
            Assert.Equal(TimeSpan.Zero, player.TotalDuration);
        }

        [Fact]
        public void MidiPlayer_PlaysSong_FiresChannelActivity()
        {
            using var player = new MidiPlayer();
            var dummyDevice = new DummyOutputDevice();
            player.SetOutputDevice(dummyDevice);

            var midi = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)2, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)2, DeltaTime = 100 }
            ));

            var activities = new List<ChannelActivityEventArgs>();
            player.ChannelActivity += (s, e) => activities.Add(e);

            player.LoadMidi(midi);
            player.Play();
            System.Threading.Thread.Sleep(200);
            player.Stop();

            Assert.NotEmpty(activities);
            Assert.Contains(activities, a => a.Channel == 2 && a.IsActive && a.Velocity == 100);
            Assert.Contains(activities, a => a.Channel == 2 && !a.IsActive);
            Assert.Contains(dummyDevice.SentEvents, ev => ev is NoteOnEvent no && no.Channel == (FourBitNumber)2);
        }

        [Fact]
        public void MidiPlayer_MutedChannel_SuppressesNotesAndActivity()
        {
            using var player = new MidiPlayer();
            var dummyDevice = new DummyOutputDevice();
            player.SetOutputDevice(dummyDevice);

            player.SetChannelMute(2, true);

            var midi = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)2, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)2, DeltaTime = 100 }
            ));

            var activities = new List<ChannelActivityEventArgs>();
            player.ChannelActivity += (s, e) => activities.Add(e);

            player.LoadMidi(midi);
            player.Play();
            System.Threading.Thread.Sleep(200);
            player.Stop();

            // Notes on channel 2 must be suppressed from output device
            Assert.DoesNotContain(dummyDevice.SentEvents, ev => ev is NoteOnEvent no && no.Channel == (FourBitNumber)2);
            // No active note event should have fired for muted channel 2
            Assert.DoesNotContain(activities, a => a.Channel == 2 && a.IsActive);
        }

        [Fact]
        public void MidiPlayer_LoadingNewMidiDuringPlayback_ResetsPositionToZeroAndSilencesDevice()
        {
            using var player = new MidiPlayer();
            var dummyDevice = new DummyOutputDevice();
            player.SetOutputDevice(dummyDevice);

            // Song A: 10 seconds long
            var songA = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 5000 },
                new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 5000 }
            ));

            player.LoadMidi(songA);
            player.Play();
            player.Seek(TimeSpan.FromSeconds(3));

            Assert.True(player.CurrentPosition.TotalSeconds >= 2.9);

            // Song B: Loaded while song A was at ~3s
            var songB = new MidiFile(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)72, (SevenBitNumber)90) { Channel = (FourBitNumber)1, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)72, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 2000 }
            ));

            // Loading Song B must cleanly stop Song A and reset position to Zero
            player.LoadMidi(songB);

            Assert.False(player.IsPlaying);
            Assert.Equal(TimeSpan.Zero, player.CurrentPosition);

            // All Notes Off / Panic CC events must have been sent
            Assert.Contains(dummyDevice.SentEvents, ev => ev is ControlChangeEvent cc && cc.ControlNumber == (SevenBitNumber)123);

            // Playing Song B must start from the beginning
            player.Play();
            Assert.True(player.IsPlaying);
            Assert.True(player.CurrentPosition.TotalSeconds < 1.0);
            player.Stop();
        }

        [Fact]
        public void Remapper_ElectricGrandPiano_MapsToAcousticGrandPiano()
        {
            // GM Program 2 (Electric Grand Piano / CP-80) must map to Voice 00: PIANO (MidiProgram 3)
            var midi = new MidiFile(new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)2) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { Channel = (FourBitNumber)0, DeltaTime = 0 }
            ));

            var remapped = Remapper.Remap(midi, new RemapperOptions());
            var pcs = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<ProgramChangeEvent>().ToList();
            Assert.NotEmpty(pcs);
            Assert.Equal(3, (byte)pcs[0].ProgramNumber); // PSS-790 Voice 00 is MidiProgram 3
        }
    }
}
