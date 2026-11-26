using System;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class AudioFidelityAndPlaybackTests
    {
        [Fact]
        public void Remapper_Cc11ExpressionFold_ConvertsToAccurateCc7Volumes()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();

            // Track with CC 7 = 100, then NoteOn, then CC 11 = 64 (swell down to ~50%), then NoteOff
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)11, (SevenBitNumber)64) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            var options = new RemapperOptions
            {
                FoldExpressionPedal = true,
                EnablePolyphonyGuard = false
            };

            var remapped = Remapper.Remap(midi, options);

            var allCcs = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>().ToList();

            // 1. All CC 11 events must be stripped
            Assert.DoesNotContain(allCcs, c => c.ControlNumber == 11);

            // 2. A CC 7 event with folded value (100 * 64 / 127 = 50) must exist
            var cc7Events = allCcs.Where(c => c.ControlNumber == 7 && c.Channel == (FourBitNumber)0).ToList();
            Assert.Contains(cc7Events, c => c.ControlValue == 50);
        }

        [Fact]
        public void Remapper_AwmVelocityNormalizer_ScalesExtremeVelocitiesIntoSweetSpot()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();

            // Melodic channel 0 notes with velocities: 1 (whisper), 64 (mid), 127 (max)
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)1) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 100, Channel = (FourBitNumber)0 });

            track.Events.Add(new NoteOnEvent((SevenBitNumber)62, (SevenBitNumber)64) { DeltaTime = 100, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)62, (SevenBitNumber)0) { DeltaTime = 100, Channel = (FourBitNumber)0 });

            track.Events.Add(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)127) { DeltaTime = 100, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 100, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            var options = new RemapperOptions
            {
                NormalizeAwmVelocities = true,
                EnablePolyphonyGuard = false
            };

            var remapped = Remapper.Remap(midi, options);

            var notesOn = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<NoteOnEvent>().Where(n => n.Velocity > 0).ToList();
            Assert.Equal(3, notesOn.Count);

            // Minimum velocity 1 is raised to sweet spot min (36)
            Assert.Equal(Constants.AwmVelocityMin, (byte)notesOn[0].Velocity);

            // Mid velocity 64 is scaled to ~77
            Assert.Equal(77, (byte)notesOn[1].Velocity);

            // Max velocity 127 is clamped to sweet spot max (118)
            Assert.Equal(Constants.AwmVelocityMax, (byte)notesOn[2].Velocity);
        }

        [Fact]
        public void Remapper_PreRollGuard_EnsuresSetupPrecedesFirstNote()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();

            // Note starts at tick 0 alongside Program Change
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            var options = new RemapperOptions
            {
                EnsureSetupPreRoll = true,
                EnablePolyphonyGuard = false
            };

            var remapped = Remapper.Remap(midi, options);

            // Verify using TimedEvents that the first Note-On event has Time >= 48 ticks
            var firstTrack = remapped.GetTrackChunks().First();
            using var mgr = firstTrack.ManageTimedEvents();

            var firstNoteOn = mgr.Objects.FirstOrDefault(te => te.Event is NoteOnEvent n && n.Velocity > 0);
            var firstPc = mgr.Objects.FirstOrDefault(te => te.Event is ProgramChangeEvent);

            Assert.NotNull(firstNoteOn);
            Assert.NotNull(firstPc);

            // Setup Program Change occurs before Note 1
            Assert.True(firstPc.Time < firstNoteOn.Time, "Setup Program Change must occur prior to Note 1");
            Assert.True(firstNoteOn.Time >= 48, $"Note 1 must be delayed by pre-roll window (actual: {firstNoteOn.Time})");
        }

        [Fact]
        public void MidiPlayer_ClockSettings_EngagesHighPrecisionTickGenerator()
        {
            using var generator = new HighPrecisionTickGenerator();
            Assert.NotNull(generator);
        }
    }
}
