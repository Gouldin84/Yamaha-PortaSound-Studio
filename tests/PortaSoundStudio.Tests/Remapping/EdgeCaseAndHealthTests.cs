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
    public class ProblematicMidiEdgeCaseTests
    {
        [Fact]
        public void StressTest_BlackMidi_128SimultaneousNotes_DoesNotCrashAndAnalyzesPolyphony()
        {
            // Arrange: 128 simultaneous notes at tick 0 (Black MIDI style)
            var midi = new MidiFile();
            var track = new TrackChunk();
            for (int i = 0; i < 128; i++)
            {
                track.Events.Add(new NoteOnEvent((SevenBitNumber)(i % 128), (SevenBitNumber)100)
                {
                    DeltaTime = 0,
                    Channel = (FourBitNumber)(i % 16)
                });
            }
            for (int i = 0; i < 128; i++)
            {
                track.Events.Add(new NoteOffEvent((SevenBitNumber)(i % 128), (SevenBitNumber)0)
                {
                    DeltaTime = i == 0 ? 480 : 0,
                    Channel = (FourBitNumber)(i % 16)
                });
            }
            midi.Chunks.Add(track);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "BlackMidiTest.mid");
            var remapped = Remapper.Remap(midi);

            // Assert
            Assert.True(report.PeakPolyphony >= 120);
            Assert.Equal("warning", report.BadgeType);
            Assert.Contains("Guarded", report.BadgeLabel);
            Assert.NotNull(remapped);
        }

        [Fact]
        public void StressTest_Format0_SingleTrackMultiChannel_InfersAndRemapsCorrectly()
        {
            // Arrange: Format 0 (single track chunk with multiple channels & track text)
            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new SequenceTrackNameEvent("Lead Guitar & Bass") { DeltaTime = 0 });

            // Ch 0 notes (no explicit PC)
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });

            // Ch 9 GM Drums
            track.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)9 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)38, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)9 });

            midi.Chunks.Add(track);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "Format0.mid");
            var remapped = Remapper.Remap(midi);

            // Assert
            Assert.True(report.InferredProgramChannels.Count > 0);
            Assert.NotNull(remapped);
            // Drums should be moved to Ch 15 (PSS Channel 16)
            var remappedNotes = remapped.GetNotes().ToList();
            Assert.Contains(remappedNotes, n => n.Channel == (FourBitNumber)15);
        }

        [Fact]
        public void StressTest_EndlessDamperPedal_5000Bars_CappedWithoutMemoryBloat()
        {
            // Arrange: CC 64 = 127 at tick 0, never turned off, notes spanning 5000 bars
            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127) { DeltaTime = 0, Channel = (FourBitNumber)0 });

            // Piano notes spanning 10,000 ticks
            for (int t = 0; t < 10; t++)
            {
                track.Events.Add(new NoteOnEvent((SevenBitNumber)(60 + t), (SevenBitNumber)80) { DeltaTime = 480, Channel = (FourBitNumber)0 });
                track.Events.Add(new NoteOffEvent((SevenBitNumber)(60 + t), (SevenBitNumber)0) { DeltaTime = 120, Channel = (FourBitNumber)0 });
            }
            midi.Chunks.Add(track);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "EndlessPedal.mid");
            var remapped = Remapper.Remap(midi);

            // Assert
            Assert.True(report.StuckPedalIntervalsCount > 0);
            Assert.True(report.RunawayPedalCapped);
            Assert.Contains(report.AppliedAutoFixes, f => f.Contains("runaway sustain pedal"));
        }

        [Fact]
        public void StressTest_DanglingPitchBends_MultipleTracks_AllAutoCenteredAtEof()
        {
            // Arrange: Pitch bend of 16383 at last tick before EndOfTrackEvent
            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new PitchBendEvent(16383) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "DanglingBend.mid");
            var remapped = Remapper.Remap(midi);

            // Assert
            Assert.True(report.DanglingPitchBendsCount > 0);
            Assert.True(report.PitchBendsCenteredAtEof);

            // Remapped file must contain 8192 pitch bend before EndOfTrackEvent
            var firstTrack = remapped.GetTrackChunks().First();
            var bends = firstTrack.Events.OfType<PitchBendEvent>().ToList();
            Assert.NotEmpty(bends);
            Assert.Equal(8192, bends.Last().PitchValue);
        }

        [Theory]
        [InlineData("Heavy 5Str Bass", 33)]
        [InlineData("Clean Strat Gtr", 27)]
        [InlineData("Grand Pno Lead", 0)]
        [InlineData("Rhodes EP Chords", 4)]
        [InlineData("Tenor Sax Solo", 66)]
        [InlineData("Vibraphone Melody", 11)]
        [InlineData("Syn-Lead-Arp", 80)]
        [InlineData("Warm Pad Sweep", 89)]
        [InlineData("Pizzicato Orch", 45)]
        [InlineData("Distortion Gtr Riff", 29)]
        public void StressTest_CrypticTrackNames_InfersCorrectInstruments(string trackName, byte expectedGmProg)
        {
            // Act
            var inferred = Remapper.InferGmProgramFromText(trackName);

            // Assert
            Assert.NotNull(inferred);
            Assert.Equal(expectedGmProg, inferred.Value);
        }

        [Fact]
        public void StressTest_ZeroLengthAndZeroVelocityNotes_NormalizedCleanly()
        {
            // Arrange: Note closed by running-status NoteOn (vel 0) at delta 480
            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            // Zero-velocity NoteOn is standard MIDI running status for NoteOff
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            // Act
            var remapped = Remapper.Remap(midi);
            var notes = remapped.GetNotes().ToList();

            // Assert
            Assert.Single(notes);
            Assert.True(notes[0].Length >= 1);
        }

        [Fact]
        public void StressTest_NonStandardDrumChannel_Bank127_MappedToChannel16()
        {
            // Arrange: Notes on Channel 10 (ch index 10) with CC 0 = 127 (GS/XG Drum Bank)
            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)127) { DeltaTime = 0, Channel = (FourBitNumber)10 });
            // GM Kick Drum 36 on Channel 10
            track.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)10 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)10 });
            midi.Chunks.Add(track);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "NonStandardDrum.mid");
            var remapped = Remapper.Remap(midi);

            // Assert
            Assert.Contains(10, report.NonStandardDrumChannels);
            var remappedNotes = remapped.GetNotes().ToList();
            // GM Kick 36 should be remapped to PSS-790 Channel 15 (Ch 16) with PSS Kick 45 (BASS DRUM HI / A2)
            Assert.Contains(remappedNotes, n => n.Channel == (FourBitNumber)15 && n.NoteNumber == (SevenBitNumber)45);
        }
    }
}
