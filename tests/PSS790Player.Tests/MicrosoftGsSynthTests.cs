using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class MicrosoftGsSynthTests
    {
        [Theory]
        [InlineData("Microsoft GS Wavetable Synth", true)]
        [InlineData("Microsoft GS Wavetable DLS Synth", true)]
        [InlineData("GS Wavetable Synth", true)]
        [InlineData("microsoft gs wavetable synth", true)]
        [InlineData("Yamaha PSS-790", false)]
        [InlineData("Roland UM-ONE", false)]
        [InlineData("loopMIDI Port 1", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        public void DeviceDetection_IdentifiesMicrosoftGsWavetableSynthCorrectly(string? deviceName, bool expected)
        {
            bool isMsGs = Constants.IsMicrosoftGsWavetableSynth(deviceName);
            Assert.Equal(expected, isMsGs);
        }

        [Fact]
        public void GeneralMidiFile_PreservesChannel10DrumsAndGmPrograms_WhenUnmodified()
        {
            // Create a synthetic General MIDI file with Channel 10 drums and Channel 1 GM Acoustic Piano
            var midi = new MidiFile(
                new TrackChunk(
                    // Ch 1: Acoustic Grand Piano (GM Prog 0)
                    new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 },
                    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0 },
                    new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0 },
                    // Ch 10 (Channel index 9): Standard GM Bass Drum (Note 35)
                    new NoteOnEvent((SevenBitNumber)35, (SevenBitNumber)110) { Channel = (FourBitNumber)9 },
                    new NoteOffEvent((SevenBitNumber)35, (SevenBitNumber)0) { Channel = (FourBitNumber)9 }
                )
            );

            var format = Remapper.DetectMidiFormat(midi);
            Assert.Equal(MidiFormat.GeneralMidi, format);

            // In unmodified mode (Microsoft GS Wavetable Synth), the original file keeps Channel 10 drums:
            var noteEvents = midi.GetTrackChunks().SelectMany(t => t.Events).OfType<NoteOnEvent>().ToList();
            Assert.Contains(noteEvents, n => n.Channel == (FourBitNumber)9 && n.NoteNumber == (SevenBitNumber)35);
            Assert.DoesNotContain(noteEvents, n => n.Channel == (FourBitNumber)15);

            // In contrast, when remapped for Yamaha PSS-790 hardware:
            var remapped = Remapper.Remap(midi);
            var remappedNotes = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<NoteOnEvent>().ToList();
            // Drums moved to Channel 16 (FourBitNumber 15) and mapped to Chart 1
            Assert.Contains(remappedNotes, n => n.Channel == (FourBitNumber)15);
            Assert.DoesNotContain(remappedNotes, n => n.Channel == (FourBitNumber)9);
        }

        [Fact]
        public void GeneralMidiFile_MaintainsNativeExpressionAndVelocities_WhenUnmodified()
        {
            // Create GM file with CC 11 Expression and delicate velocity (e.g. 15)
            var midi = new MidiFile(
                new TrackChunk(
                    new ControlChangeEvent((SevenBitNumber)11, (SevenBitNumber)64) { Channel = (FourBitNumber)0 },
                    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)15) { Channel = (FourBitNumber)0 },
                    new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0 }
                )
            );

            // In unmodified GM passthrough for Windows GS Synth:
            var cc11Event = midi.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>().FirstOrDefault(c => c.ControlNumber == 11);
            Assert.NotNull(cc11Event);
            Assert.Equal(64, (int)cc11Event.ControlValue);

            var originalNote = midi.GetTrackChunks().SelectMany(t => t.Events).OfType<NoteOnEvent>().First();
            Assert.Equal(15, (int)originalNote.Velocity);

            // In contrast, PSS-790 remapping normalizes velocities into 36-118 sweet-spot and folds CC 11 into CC 7:
            var remapped = Remapper.Remap(midi, new RemapperOptions { FoldExpressionPedal = true, NormalizeAwmVelocities = true });
            var remappedCc11 = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<ControlChangeEvent>().FirstOrDefault(c => c.ControlNumber == 11);
            Assert.Null(remappedCc11); // CC 11 stripped/folded

            var remappedNote = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<NoteOnEvent>().First();
            Assert.True((int)remappedNote.Velocity >= 36); // Lifted into AWM sweet-spot
        }

        [Fact]
        public void Pss790NativeFile_RetainsPss790Format_RegardlessOfOutputDevice()
        {
            // Create native PSS-790 format file (with marker or Ch 16 drums)
            var midi = new MidiFile(
                new TrackChunk(
                    new SequenceTrackNameEvent("PSS-790 Converted"),
                    new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)90) { Channel = (FourBitNumber)15 },
                    new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { Channel = (FourBitNumber)15 }
                )
            );

            var format = Remapper.DetectMidiFormat(midi);
            Assert.Equal(MidiFormat.Pss790Native, format);
        }
    }
}
