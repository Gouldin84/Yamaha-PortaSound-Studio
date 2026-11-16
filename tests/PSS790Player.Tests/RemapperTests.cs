using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class RemapperTests
    {
        [Fact]
        public void Remap_RelocatesGmDrumsChannel9ToChannel15()
        {
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();
            trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)42, (SevenBitNumber)100) { Channel = (FourBitNumber)9 });
            trackChunk.Events.Add(new NoteOffEvent((SevenBitNumber)42, (SevenBitNumber)0) { DeltaTime = 100, Channel = (FourBitNumber)9 });
            midiFile.Chunks.Add(trackChunk);

            var remapped = Remapper.Remap(midiFile);

            var noteOns = remapped.GetTimedEvents().Select(te => te.Event).OfType<NoteOnEvent>().ToList();
            Assert.Single(noteOns);
            Assert.Equal((FourBitNumber)15, noteOns[0].Channel);
            Assert.Equal((SevenBitNumber)57, noteOns[0].NoteNumber);
        }

        [Fact]
        public void Remap_RelocatesChannel15MelodyToFreeChannel()
        {
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();
            trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)90) { Channel = (FourBitNumber)9 });
            trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { Channel = (FourBitNumber)15 });
            midiFile.Chunks.Add(trackChunk);

            var remapped = Remapper.Remap(midiFile);

            var noteOns = remapped.GetTimedEvents().Select(te => te.Event).OfType<NoteOnEvent>().ToList();
            Assert.Equal(2, noteOns.Count);

            var drumNote = noteOns.FirstOrDefault(n => n.NoteNumber == (SevenBitNumber)49);
            Assert.NotNull(drumNote);
            Assert.Equal((FourBitNumber)15, drumNote.Channel);

            var melodicNote = noteOns.FirstOrDefault(n => n.NoteNumber == (SevenBitNumber)60);
            Assert.NotNull(melodicNote);
            Assert.NotEqual((FourBitNumber)15, melodicNote.Channel);
        }

        [Fact]
        public void Remap_TranslatesMelodicVoicesUsingChart2()
        {
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();
            trackChunk.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            trackChunk.Events.Add(new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)1 });
            midiFile.Chunks.Add(trackChunk);

            var remapped = Remapper.Remap(midiFile);

            var pcs = remapped.GetTimedEvents().Select(te => te.Event).OfType<ProgramChangeEvent>().ToList();
            Assert.Equal(2, pcs.Count);

            var pianoPc = pcs.First(p => p.Channel == (FourBitNumber)0);
            Assert.Equal((SevenBitNumber)3, pianoPc.ProgramNumber);

            var bassPc = pcs.First(p => p.Channel == (FourBitNumber)1);
            Assert.Equal((SevenBitNumber)38, bassPc.ProgramNumber);
        }

        [Fact]
        public void Remap_AppliesChannelVoiceOverrides()
        {
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();
            trackChunk.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            midiFile.Chunks.Add(trackChunk);

            var options = new RemapperOptions
            {
                ChannelVoiceOverrides = new Dictionary<int, int?>
                {
                    { 0, 99 } // Override Ch 0 to Voice 99 (Orchestra Hit -> MIDI Program 90)
                }
            };

            var remapped = Remapper.Remap(midiFile, options);

            var pc = remapped.GetTimedEvents().Select(te => te.Event).OfType<ProgramChangeEvent>().Single();
            Assert.Equal((SevenBitNumber)90, pc.ProgramNumber);
        }

        [Fact]
        public void Remap_ScalesPitchBend()
        {
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();
            trackChunk.Events.Add(new PitchBendEvent(16383) { Channel = (FourBitNumber)0 });
            midiFile.Chunks.Add(trackChunk);

            var remapped = Remapper.Remap(midiFile);

            var pb = remapped.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().Single();
            Assert.InRange(pb.PitchValue, 9550, 9565);
        }

        [Fact]
        public void Remap_FiltersUnsupportedCCsAndAftertouch()
        {
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();
            trackChunk.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { Channel = (FourBitNumber)0 });
            trackChunk.Events.Add(new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)64) { Channel = (FourBitNumber)0 });
            trackChunk.Events.Add(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127) { Channel = (FourBitNumber)0 });
            trackChunk.Events.Add(new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)64) { Channel = (FourBitNumber)0 });
            trackChunk.Events.Add(new ChannelAftertouchEvent((SevenBitNumber)80) { Channel = (FourBitNumber)0 });
            midiFile.Chunks.Add(trackChunk);

            var remapped = Remapper.Remap(midiFile);

            var events = remapped.GetTimedEvents().Select(te => te.Event).ToList();
            Assert.Contains(events, e => e is TextEvent t && t.Text.Contains("PSS790_REMAPPED"));
            var channelEvents = events.OfType<ChannelEvent>().ToList();
            Assert.Equal(2, channelEvents.Count);
            Assert.All(channelEvents.OfType<ControlChangeEvent>(), cc => Assert.True((byte)cc.ControlNumber is 1 or 7));
            Assert.Empty(channelEvents.OfType<ChannelAftertouchEvent>());
        }

        [Fact]
        public void DetectMidiFormat_IdentifiesGeneralMidiAndPss790Native()
        {
            // Standard GM file
            var gmMidi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)90) { Channel = (FourBitNumber)9 });
            gmMidi.Chunks.Add(track);

            Assert.Equal(MidiFormat.GeneralMidi, Remapper.DetectMidiFormat(gmMidi));

            // Remap and verify marker is embedded and detected as Pss790Native
            var remapped = Remapper.Remap(gmMidi);
            Assert.Equal(MidiFormat.Pss790Native, Remapper.DetectMidiFormat(remapped));
        }

        [Fact]
        public void Remap_IsIdempotent_PreventsDoublePitchBendScaling()
        {
            var gmMidi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new PitchBendEvent(16383) { Channel = (FourBitNumber)0 });
            gmMidi.Chunks.Add(track);

            // First pass: scaled down by 1/6 (~9557)
            var pass1 = Remapper.Remap(gmMidi);
            var pb1 = pass1.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().Single();
            Assert.InRange(pb1.PitchValue, 9550, 9565);

            // Second pass: should NOT scale down again by another 1/6 (would be ~8419 if double-scaled)
            var pass2 = Remapper.Remap(pass1);
            var pb2 = pass2.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().Single();
            Assert.Equal(pb1.PitchValue, pb2.PitchValue); // Exactly identical, no double scaling!
        }
    }
}
