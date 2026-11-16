using System;
using System.IO;
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
        public void SaveTake_WithoutRecording_ThrowsInvalidOperationException()
        {
            using var recorder = new MidiRecorder();
            Assert.Throws<InvalidOperationException>(() => recorder.SaveTake("test_take.mid", false));
        }

        [Fact]
        public void SaveTake_WithRemapping_AppliesPSS790RulesAndMarker()
        {
            using var recorder = new MidiRecorder();

            // Create a synthetic recorded take with GM drums on Channel 9
            var syntheticFile = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)9 }); // Kick
            track.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)9 });
            syntheticFile.Chunks.Add(track);

            // Use reflection to set _lastRecordedFile for testing SaveTake
            var field = typeof(MidiRecorder).GetField("_lastRecordedFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(recorder, syntheticFile);

            var testFileName = $"Test_SaveTake_{Guid.NewGuid():N}.mid";
            string savedPath = recorder.SaveTake(testFileName, remapForPss790: true);

            try
            {
                Assert.True(File.Exists(savedPath));
                Assert.Contains("PSS-790 Midi", savedPath);

                var reloaded = MidiFile.Read(savedPath);
                var format = Remapper.DetectMidiFormat(reloaded);
                Assert.Equal(MidiFormat.Pss790Native, format);

                // Check marker
                bool hasMarker = false;
                foreach (var chunk in reloaded.GetTrackChunks())
                {
                    foreach (var evt in chunk.Events)
                    {
                        if (evt is TextEvent te && te.Text.Contains(Remapper.Pss790MarkerText))
                        {
                            hasMarker = true;
                            break;
                        }
                    }
                }
                Assert.True(hasMarker);
            }
            finally
            {
                if (File.Exists(savedPath)) File.Delete(savedPath);
            }
        }

        [Fact]
        public void SaveTake_WithoutRemapping_SavesRawToGmMidiFolder()
        {
            using var recorder = new MidiRecorder();

            var syntheticFile = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            syntheticFile.Chunks.Add(track);

            var field = typeof(MidiRecorder).GetField("_lastRecordedFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(recorder, syntheticFile);

            var testFileName = $"Test_RawTake_{Guid.NewGuid():N}.mid";
            string savedPath = recorder.SaveTake(testFileName, remapForPss790: false);

            try
            {
                Assert.True(File.Exists(savedPath));
                Assert.Contains("GM Midi", savedPath);

                var reloaded = MidiFile.Read(savedPath);
                var format = Remapper.DetectMidiFormat(reloaded);
                Assert.Equal(MidiFormat.GeneralMidi, format);
            }
            finally
            {
                if (File.Exists(savedPath)) File.Delete(savedPath);
            }
        }
    }
}
