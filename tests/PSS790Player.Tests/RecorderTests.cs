using System;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class RecorderTests
    {
        [Fact]
        public void GetInputDeviceNames_DoesNotThrow()
        {
            var devices = MidiRecorder.GetInputDeviceNames();
            Assert.NotNull(devices);
        }

        [Fact]
        public void Recorder_InitialState_IsIdleAndMidiThruDisabled()
        {
            using var recorder = new MidiRecorder();
            Assert.Equal(RecorderState.Idle, recorder.State);
            Assert.False(recorder.MidiThruEnabled, "MIDI Thru must default to false to protect against PSS-790 feedback loops");
            Assert.Equal(120, recorder.Bpm);
        }

        [Fact]
        public void SaveNativeTake_WithoutRecording_ThrowsInvalidOperationException()
        {
            using var recorder = new MidiRecorder();
            Assert.Throws<InvalidOperationException>(() => recorder.SaveNativeTake("test_take.mid"));
        }

        [Fact]
        public void SaveNativeTake_PreservesExact1to1DataAndInjectsMarker()
        {
            using var recorder = new MidiRecorder();

            // Create a synthetic native PSS-790 take:
            // Drum note on Channel 15 with Chart 1 note 45 (BASS DRUM HI), and Piano on Channel 0 with Chart 2 program 3
            var syntheticFile = new MidiFile();
            var drumTrack = new TrackChunk();
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)45, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)15 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)45, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)15 });
            syntheticFile.Chunks.Add(drumTrack);

            var melodicTrack = new TrackChunk();
            melodicTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)3) { Channel = (FourBitNumber)0 }); // Chart 2 Piano
            melodicTrack.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            melodicTrack.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            syntheticFile.Chunks.Add(melodicTrack);

            var field = typeof(MidiRecorder).GetField("_lastRecordedFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(recorder, syntheticFile);

            var testFileName = $"Test_NativeTake_{Guid.NewGuid():N}.mid";
            string savedPath = recorder.SaveNativeTake(testFileName);

            try
            {
                Assert.True(File.Exists(savedPath));
                Assert.Contains("PSS-790 Midi", savedPath);

                var reloaded = MidiFile.Read(savedPath);
                var format = Remapper.DetectMidiFormat(reloaded);
                Assert.Equal(MidiFormat.Pss790Native, format);

                // Verify 1:1 integrity: Drums remain on Channel 15 and note is still 45
                var drumNotes = reloaded.GetNotes().Where(n => n.Channel == 15).ToList();
                Assert.Single(drumNotes);
                Assert.Equal((byte)45, (byte)drumNotes[0].NoteNumber);

                // Verify melodic program is still 3 on Channel 0
                var pc = reloaded.GetTimedEvents().Select(te => te.Event).OfType<ProgramChangeEvent>().FirstOrDefault();
                Assert.NotNull(pc);
                Assert.Equal((byte)3, (byte)pc.ProgramNumber);
                Assert.Equal(0, pc.Channel);
            }
            finally
            {
                if (File.Exists(savedPath)) File.Delete(savedPath);
            }
        }

        [Fact]
        public void ExportTakeToGeneralMidi_ReverseRemapsDrumsAndVoices()
        {
            using var recorder = new MidiRecorder();

            // Native PSS-790 take:
            // Channel 15 has Chart 1 note 57 (HI HAT CLOSED)
            // Channel 0 has Chart 2 program 3 (PIANO)
            var syntheticFile = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)3) { Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)57, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)15 }); // Ch 16 PSS drum
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)57, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)15 });
            syntheticFile.Chunks.Add(track);

            var field = typeof(MidiRecorder).GetField("_lastRecordedFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(recorder, syntheticFile);

            var testFileName = $"Test_GmExport_{Guid.NewGuid():N}.mid";
            string savedPath = recorder.ExportTakeToGeneralMidi(testFileName);

            try
            {
                Assert.True(File.Exists(savedPath));
                Assert.Contains("GM Midi", savedPath);

                var reloaded = MidiFile.Read(savedPath);
                var format = Remapper.DetectMidiFormat(reloaded);
                Assert.Equal(MidiFormat.GeneralMidi, format);

                // Verify reverse remapping:
                // Drums should now be on Channel 9 (GM Drum Ch 10)
                var gmDrumNotes = reloaded.GetNotes().Where(n => n.Channel == 9).ToList();
                Assert.Single(gmDrumNotes);
                // Chart 1 note 57 (HI HAT CLOSED) -> GM note 42 (Closed Hi-Hat)
                Assert.Equal((byte)42, (byte)gmDrumNotes[0].NoteNumber);

                // Chart 2 program 3 (PIANO) -> GM program 0 (Acoustic Grand Piano)
                var pc = reloaded.GetTimedEvents().Select(te => te.Event).OfType<ProgramChangeEvent>().FirstOrDefault();
                Assert.NotNull(pc);
                Assert.Equal((byte)0, (byte)pc.ProgramNumber);
            }
            finally
            {
                if (File.Exists(savedPath)) File.Delete(savedPath);
            }
        }

        [Fact]
        public void ExportTakeToGeneralMidi_ReverseRemapsVoicesAndDrums_Accurately()
        {
            var pssMidi = new MidiFile();
            var drumTrack = new TrackChunk();
            // PSS-790 drum notes on Ch 15: Note 36 (BASS DRUM REVERB), Note 38 (SYNTH SNARE)
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)15 });
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)90) { DeltaTime = 120, Channel = (FourBitNumber)15 });
            pssMidi.Chunks.Add(drumTrack);

            var melodicTrack = new TrackChunk();
            // Voice 99 ORCHESTRA HIT -> MIDI Prog 90
            melodicTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)90) { Channel = (FourBitNumber)0 });
            // Voice 97 HARMONICA -> MIDI Prog 22
            melodicTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)22) { Channel = (FourBitNumber)1 });
            // Voice 95 TIMPANI -> MIDI Prog 66
            melodicTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)66) { Channel = (FourBitNumber)2 });
            // Voice 83 ELECTRIC BASS -> MIDI Prog 38
            melodicTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)38) { Channel = (FourBitNumber)3 });
            pssMidi.Chunks.Add(melodicTrack);

            var gmMidi = ReverseRemapper.ReverseRemapToGeneralMidi(pssMidi);

            var pcs = gmMidi.GetTimedEvents().Select(te => te.Event).OfType<ProgramChangeEvent>().ToList();
            Assert.Equal(4, pcs.Count);
            Assert.Equal((byte)55, (byte)pcs.First(p => p.Channel == 0).ProgramNumber); // GM Orchestra Hit
            Assert.Equal((byte)22, (byte)pcs.First(p => p.Channel == 1).ProgramNumber); // GM Harmonica
            Assert.Equal((byte)47, (byte)pcs.First(p => p.Channel == 2).ProgramNumber); // GM Timpani
            Assert.Equal((byte)33, (byte)pcs.First(p => p.Channel == 3).ProgramNumber); // GM Electric Bass (finger)

            var drumNotes = gmMidi.GetTimedEvents().Select(te => te.Event).OfType<NoteOnEvent>().Where(n => n.Channel == 9).ToList();
            Assert.Equal(2, drumNotes.Count);
            Assert.Equal((byte)35, (byte)drumNotes[0].NoteNumber); // GM Acoustic Bass Drum
            Assert.Equal((byte)40, (byte)drumNotes[1].NoteNumber); // GM Electric Snare
        }
    }
}
