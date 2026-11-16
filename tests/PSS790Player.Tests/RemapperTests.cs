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

        [Fact]
        public void Pss790Voices_Chart2_MatchesOfficialManualExactly()
        {
            Assert.Equal(100, Constants.Pss790Voices.Count);

            // Spot check specific verified entries from manual Chart 2
            Assert.Equal((byte)3, Constants.Pss790Voices[0].MidiProgram);     // PIANO
            Assert.Equal((byte)5, Constants.Pss790Voices[1].MidiProgram);     // ELECTRIC PIANO 1
            Assert.Equal((byte)54, Constants.Pss790Voices[2].MidiProgram);    // ELECTRIC PIANO 2
            Assert.Equal((byte)4, Constants.Pss790Voices[3].MidiProgram);     // HARPSICHORD 1
            Assert.Equal((byte)53, Constants.Pss790Voices[4].MidiProgram);    // HARPSICHORD 2
            Assert.Equal((byte)25, Constants.Pss790Voices[5].MidiProgram);    // HONKY-TONK PIANO
            Assert.Equal((byte)30, Constants.Pss790Voices[6].MidiProgram);    // CRAVI
            Assert.Equal((byte)6, Constants.Pss790Voices[7].MidiProgram);     // CELESTA
            Assert.Equal((byte)2, Constants.Pss790Voices[8].MidiProgram);     // PIPE ORGAN
            Assert.Equal((byte)49, Constants.Pss790Voices[9].MidiProgram);    // REED ORGAN
            Assert.Equal((byte)1, Constants.Pss790Voices[10].MidiProgram);    // JAZZ ORGAN 1
            Assert.Equal((byte)27, Constants.Pss790Voices[11].MidiProgram);   // JAZZ ORGAN 2
            Assert.Equal((byte)28, Constants.Pss790Voices[12].MidiProgram);   // TREMOLO ORGAN
            Assert.Equal((byte)29, Constants.Pss790Voices[13].MidiProgram);   // FULL ORGAN
            Assert.Equal((byte)31, Constants.Pss790Voices[14].MidiProgram);   // ACCORDION
            Assert.Equal((byte)13, Constants.Pss790Voices[15].MidiProgram);   // DISTORTION GUITAR
            Assert.Equal((byte)68, Constants.Pss790Voices[16].MidiProgram);   // ELECTRIC GUITAR
            Assert.Equal((byte)41, Constants.Pss790Voices[34].MidiProgram);   // STRINGS 1
            Assert.Equal((byte)50, Constants.Pss790Voices[35].MidiProgram);   // STRINGS 2
            Assert.Equal((byte)78, Constants.Pss790Voices[36].MidiProgram);   // PIZZICATO STRINGS
            Assert.Equal((byte)37, Constants.Pss790Voices[37].MidiProgram);   // HARP
            Assert.Equal((byte)40, Constants.Pss790Voices[38].MidiProgram);   // UKULELE
            Assert.Equal((byte)34, Constants.Pss790Voices[39].MidiProgram);   // BANJO
            Assert.Equal((byte)7, Constants.Pss790Voices[40].MidiProgram);    // VIBRAPHONE
            Assert.Equal((byte)8, Constants.Pss790Voices[41].MidiProgram);    // MARIMBA
            Assert.Equal((byte)59, Constants.Pss790Voices[42].MidiProgram);   // XYLOPHONE
            Assert.Equal((byte)32, Constants.Pss790Voices[43].MidiProgram);   // GLOCKENSPIEL
            Assert.Equal((byte)15, Constants.Pss790Voices[44].MidiProgram);   // TRUMPET
            Assert.Equal((byte)44, Constants.Pss790Voices[45].MidiProgram);   // MUTE TRUMPET
            Assert.Equal((byte)118, Constants.Pss790Voices[46].MidiProgram);  // TRUMPET ECHO
            Assert.Equal((byte)120, Constants.Pss790Voices[47].MidiProgram);  // MUTE TRUMPET ECHO
            Assert.Equal((byte)87, Constants.Pss790Voices[48].MidiProgram);   // FLUGELHORN
            Assert.Equal((byte)16, Constants.Pss790Voices[49].MidiProgram);   // TROMBONE
            Assert.Equal((byte)17, Constants.Pss790Voices[50].MidiProgram);   // HORN
            Assert.Equal((byte)84, Constants.Pss790Voices[51].MidiProgram);   // TUBA
            Assert.Equal((byte)92, Constants.Pss790Voices[52].MidiProgram);   // BRASS ENSEMBLE
            Assert.Equal((byte)104, Constants.Pss790Voices[53].MidiProgram);  // BRASS HIT
            Assert.Equal((byte)19, Constants.Pss790Voices[54].MidiProgram);   // CLARINET
            Assert.Equal((byte)20, Constants.Pss790Voices[55].MidiProgram);   // FLUTE
            Assert.Equal((byte)103, Constants.Pss790Voices[56].MidiProgram);  // PICCOLO
            Assert.Equal((byte)101, Constants.Pss790Voices[57].MidiProgram);  // SOPRANO SAX
            Assert.Equal((byte)18, Constants.Pss790Voices[58].MidiProgram);   // ALTO SAX
            Assert.Equal((byte)102, Constants.Pss790Voices[59].MidiProgram);  // TENOR SAX
            Assert.Equal((byte)21, Constants.Pss790Voices[60].MidiProgram);   // BASSOON
            Assert.Equal((byte)88, Constants.Pss790Voices[61].MidiProgram);   // RECORDER
            Assert.Equal((byte)91, Constants.Pss790Voices[62].MidiProgram);   // SAMBA WHISTLE
            Assert.Equal((byte)93, Constants.Pss790Voices[63].MidiProgram);   // WOODWIND ENSEMBLE
            Assert.Equal((byte)96, Constants.Pss790Voices[64].MidiProgram);   // CHORUS
            Assert.Equal((byte)0, Constants.Pss790Voices[65].MidiProgram);    // SYNTH BRASS 1
            Assert.Equal((byte)82, Constants.Pss790Voices[66].MidiProgram);   // SYNTH BRASS 2
            Assert.Equal((byte)115, Constants.Pss790Voices[67].MidiProgram);  // SYNTH BRASS 3
            Assert.Equal((byte)94, Constants.Pss790Voices[68].MidiProgram);   // SYNTH CHORUS
            Assert.Equal((byte)112, Constants.Pss790Voices[69].MidiProgram);  // SYNTH FLUTE 1
            Assert.Equal((byte)47, Constants.Pss790Voices[70].MidiProgram);   // SYNTH FLUTE 2
            Assert.Equal((byte)45, Constants.Pss790Voices[71].MidiProgram);   // SYNTH REED 1
            Assert.Equal((byte)85, Constants.Pss790Voices[72].MidiProgram);   // SYNTH REED 2
            Assert.Equal((byte)113, Constants.Pss790Voices[73].MidiProgram);  // SYNTH REED 3
            Assert.Equal((byte)51, Constants.Pss790Voices[74].MidiProgram);   // SYNTH STRINGS 1
            Assert.Equal((byte)109, Constants.Pss790Voices[75].MidiProgram);  // SYNTH STRINGS 2
            Assert.Equal((byte)119, Constants.Pss790Voices[76].MidiProgram);  // SYNTH STRINGS 3
            Assert.Equal((byte)60, Constants.Pss790Voices[77].MidiProgram);   // SYNTH PIANO 1
            Assert.Equal((byte)117, Constants.Pss790Voices[78].MidiProgram);  // SYNTH PIANO 2
            Assert.Equal((byte)110, Constants.Pss790Voices[79].MidiProgram);  // SYNTH PIANO 3
            Assert.Equal((byte)95, Constants.Pss790Voices[80].MidiProgram);   // SYNTH PIANO 4
            Assert.Equal((byte)111, Constants.Pss790Voices[81].MidiProgram);  // SYNTH PAN VOICE
            Assert.Equal((byte)64, Constants.Pss790Voices[82].MidiProgram);   // FANTASY
            Assert.Equal((byte)38, Constants.Pss790Voices[83].MidiProgram);   // ELECTRIC BASS
            Assert.Equal((byte)39, Constants.Pss790Voices[84].MidiProgram);   // SLAP BASS
            Assert.Equal((byte)80, Constants.Pss790Voices[85].MidiProgram);   // FRETLESS BASS
            Assert.Equal((byte)79, Constants.Pss790Voices[86].MidiProgram);   // PICK BASS
            Assert.Equal((byte)114, Constants.Pss790Voices[87].MidiProgram);  // MUTE BASS
            Assert.Equal((byte)107, Constants.Pss790Voices[88].MidiProgram);  // MUTE BASS ECHO
            Assert.Equal((byte)14, Constants.Pss790Voices[89].MidiProgram);   // WOOD BASS 1
            Assert.Equal((byte)81, Constants.Pss790Voices[90].MidiProgram);   // WOOD BASS 2
            Assert.Equal((byte)58, Constants.Pss790Voices[91].MidiProgram);   // SYNTH BASS 1
            Assert.Equal((byte)86, Constants.Pss790Voices[92].MidiProgram);   // SYNTH BASS 2
            Assert.Equal((byte)35, Constants.Pss790Voices[93].MidiProgram);   // BOWED BASS
            Assert.Equal((byte)9, Constants.Pss790Voices[94].MidiProgram);    // STEEL DRUM
            Assert.Equal((byte)66, Constants.Pss790Voices[95].MidiProgram);   // TIMPANI
            Assert.Equal((byte)100, Constants.Pss790Voices[96].MidiProgram);  // PERCUSSIONS
            Assert.Equal((byte)22, Constants.Pss790Voices[97].MidiProgram);   // HARMONICA
            Assert.Equal((byte)24, Constants.Pss790Voices[98].MidiProgram);   // MUSIC BOX
            Assert.Equal((byte)90, Constants.Pss790Voices[99].MidiProgram);   // ORCHESTRA HIT
        }

        [Fact]
        public void ReverseRemap_RestoresSymmetricalPitchBend()
        {
            var gmMidi = new MidiFile();
            var track = new TrackChunk();
            // Start with a standard GM pitch bend (+2 semitones = full scale 16383)
            track.Events.Add(new PitchBendEvent(16383) { Channel = (FourBitNumber)0 });
            gmMidi.Chunks.Add(track);

            // Remap for PSS-790
            var pssMidi = Remapper.Remap(gmMidi);
            var pssPb = pssMidi.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().Single();
            Assert.InRange(pssPb.PitchValue, 9550, 9565);

            // Reverse remap back to GM
            var restoredGm = ReverseRemapper.ReverseRemapToGeneralMidi(pssMidi);
            var restoredPb = restoredGm.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().Single();
            Assert.InRange(restoredPb.PitchValue, 16375, 16383);
        }
    }
}
