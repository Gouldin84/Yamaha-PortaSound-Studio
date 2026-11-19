using System.Collections.Generic;
using System.IO;
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

        [Fact]
        public void Pss790MidiProgramToVoice_MapsAllProgramsAccurately()
        {
            Assert.Equal(100, Constants.Pss790MidiProgramToVoice.Count);

            // Program 3 -> Voice 0: PIANO
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue(3, out var vPiano));
            Assert.Equal(0, vPiano.VoiceNumber);
            Assert.Equal("PIANO", vPiano.Name);

            // Program 10 -> Voice 30: VIOLIN 1
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue(10, out var vViolin));
            Assert.Equal(30, vViolin.VoiceNumber);
            Assert.Equal("VIOLIN 1", vViolin.Name);

            // Program 25 -> Voice 5: HONKY-TONK PIANO
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue(25, out var vHonky));
            Assert.Equal(5, vHonky.VoiceNumber);
            Assert.Equal("HONKY-TONK PIANO", vHonky.Name);

            // Program 15 -> Voice 44: TRUMPET
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue(15, out var vTrumpet));
            Assert.Equal(44, vTrumpet.VoiceNumber);
            Assert.Equal("TRUMPET", vTrumpet.Name);

            // Program 20 -> Voice 55: FLUTE
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue(20, out var vFlute));
            Assert.Equal(55, vFlute.VoiceNumber);
            Assert.Equal("FLUTE", vFlute.Name);
        }

        [Fact]
        public async System.Threading.Tasks.Task BatchConverter_RecursiveSubdirectories_PreservesHierarchy()
        {
            var tempSource = Path.Combine(Path.GetTempPath(), "PSS790_Batch_Source_" + System.Guid.NewGuid());
            var tempTarget = Path.Combine(Path.GetTempPath(), "PSS790_Batch_Target_" + System.Guid.NewGuid());

            try
            {
                Directory.CreateDirectory(Path.Combine(tempSource, "Anime", "Bleach"));
                Directory.CreateDirectory(Path.Combine(tempSource, "Pop Music"));

                var m1 = new MidiFile(new TrackChunk(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90)));
                m1.Write(Path.Combine(tempSource, "RootSong.mid"));
                m1.Write(Path.Combine(tempSource, "Pop Music", "PopSong.mid"));
                m1.Write(Path.Combine(tempSource, "Anime", "Bleach", "BleachSong.mid"));

                int converted = await BatchConverter.ConvertDirectoryAsync(tempSource, tempTarget);
                Assert.Equal(3, converted);

                Assert.True(File.Exists(Path.Combine(tempTarget, "RootSong.mid")));
                Assert.True(File.Exists(Path.Combine(tempTarget, "Pop Music", "PopSong.mid")));
                Assert.True(File.Exists(Path.Combine(tempTarget, "Anime", "Bleach", "BleachSong.mid")));
            }
            finally
            {
                if (Directory.Exists(tempSource)) Directory.Delete(tempSource, true);
                if (Directory.Exists(tempTarget)) Directory.Delete(tempTarget, true);
            }
        }

        [Fact]
        public void Remap_WithRpn12PitchBend_RetainsFullOctaveScaleFactor()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();
            // Send RPN 0,0 with Data Entry MSB = 12 (1 full octave)
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)101, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)100, (SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)6, (SevenBitNumber)12) { Channel = (FourBitNumber)0 });

            // Send pitch bend (+1 octave swing = 16383)
            track.Events.Add(new PitchBendEvent(16383) { Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            var remapped = Remapper.Remap(midi);
            var pb = remapped.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().Single();

            // With RPN 12 detected, scale factor is 12/12 = 1.0 (retains full 16383 swing instead of 9557)
            Assert.Equal(16383, pb.PitchValue);
        }

        [Fact]
        public void PolyphonyOptimizer_OverlappingSamePitchNotes_ClampsPriorNote()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();

            // Note 1: Pitch 60 from tick 0 to 480
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            // Note 2: Same pitch 60 arrives at tick 240 (before Note 1 finishes)
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            var optimizer = new PolyphonyOptimizer(enabled: true);
            var optimized = optimizer.Optimize(midi);

            var notes = optimized.GetNotes().OrderBy(n => n.Time).ToList();
            Assert.Equal(2, notes.Count);
            // Note 1 must be clamped so its EndTime is <= Note 2's start Time
            Assert.True(notes[0].EndTime <= notes[1].Time);
        }

        [Fact]
        public void Remap_FiltersForeignSysEx_ProtectsHardwareReceiveBuffers()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new NormalSysExEvent(new byte[] { 0x41, 0x10, 0x42, 0x12, 0x40, 0x00, 0x7F, 0x00, 0x41 })); // Roland GS Reset
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            var remapped = Remapper.Remap(midi);
            var sysExEvents = remapped.GetTimedEvents().Where(te => te.Event is NormalSysExEvent or EscapeSysExEvent).ToList();
            Assert.Empty(sysExEvents);
        }

        [Fact]
        public void Remap_ActiveChannelMissingProgramChange_InjectsDefaultPiano()
        {
            var midi = new MidiFile();
            var track = new TrackChunk();
            // Channel 0 has notes but NO ProgramChange
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            var remapped = Remapper.Remap(midi);
            var pcs = remapped.GetTimedEvents().Where(te => te.Event is ProgramChangeEvent).Select(te => (ProgramChangeEvent)te.Event).ToList();

            // Must have injected ProgramChangeEvent(3) (Voice 00: PIANO)
            var ch0Pc = pcs.FirstOrDefault(p => p.Channel == (FourBitNumber)0);
            Assert.NotNull(ch0Pc);
            Assert.Equal((SevenBitNumber)3, ch0Pc.ProgramNumber);
        }

        [Fact]
        public void TranslateDrumNote_ExtendedNotes_ReturnsMusicalFallbacks()
        {
            Assert.Equal((byte)64, Remapper.TranslateDrumNote(28)); // Slap -> Rimshot
            Assert.Equal((byte)75, Remapper.TranslateDrumNote(31)); // Sticks -> Woodblock
            Assert.Equal((byte)58, Remapper.TranslateDrumNote(82)); // Shaker -> Maracas
            Assert.Equal((byte)39, Remapper.TranslateDrumNote(84)); // Bell Tree -> Triangle Open
        }

        [Fact]
        public void Remap_GsXgChannel16Drums_KeepsOnChannel16AndTranslatesNotes()
        {
            var midi = new MidiFile();
            var tr10 = new TrackChunk();
            tr10.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 });
            tr10.Events.Add(new NoteOnEvent((SevenBitNumber)35, (SevenBitNumber)100) { Channel = (FourBitNumber)9 }); // Ch 10 Kick
            midi.Chunks.Add(tr10);

            var tr16 = new TrackChunk();
            tr16.Events.Add(new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)127) { Channel = (FourBitNumber)15 }); // Drum Bank 127
            tr16.Events.Add(new ProgramChangeEvent((SevenBitNumber)25) { Channel = (FourBitNumber)15 }); // TR-909 Kit
            tr16.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)15 }); // Kick
            tr16.Events.Add(new NoteOnEvent((SevenBitNumber)40, (SevenBitNumber)100) { Channel = (FourBitNumber)15 }); // Snare
            midi.Chunks.Add(tr16);

            var remapped = Remapper.Remap(midi);
            var events = remapped.GetTimedEvents().Select(te => te.Event).ToList();

            // Channel 15 must NOT have ProgramChangeEvent 29 (Steel Guitar)
            var ch15Pcs = events.OfType<ProgramChangeEvent>().Where(p => p.Channel == (FourBitNumber)15).ToList();
            Assert.Empty(ch15Pcs);

            // Channel 9 (Ch 10) and Channel 15 notes must both end up on Channel 15 as PSS-790 Chart 1 drums
            var ch15Notes = events.OfType<NoteOnEvent>().Where(n => n.Channel == (FourBitNumber)15).Select(n => (byte)n.NoteNumber).ToList();
            Assert.Contains((byte)45, ch15Notes); // GM 36 -> PSS 45 (BASS DRUM HI)
            Assert.Contains((byte)49, ch15Notes); // GM 40 -> PSS 49 (SNARE HI)
            Assert.Contains((byte)44, ch15Notes); // GM 35 -> PSS 44 (BASS DRUM LO)

            // Channel 9 should have zero notes
            var ch9Notes = events.OfType<NoteOnEvent>().Where(n => n.Channel == (FourBitNumber)9).ToList();
            Assert.Empty(ch9Notes);
        }

        [Fact]
        public void Remap_OutOfRangeMelodicNotes_FoldsOctavesWithinPhysicalMultisampleRange()
        {
            // Floor is Note 24 (C1), preserving all 4-string (E1=28) and 5-string (B0/C1) bass notes
            Assert.Equal((byte)28, Remapper.ClampMelodicNote(28)); // E1 bass note stays 28
            Assert.Equal((byte)29, Remapper.ClampMelodicNote(29)); // F1 stays 29
            Assert.Equal((byte)34, Remapper.ClampMelodicNote(34)); // Bb1 stays 34
            Assert.Equal((byte)60, Remapper.ClampMelodicNote(60)); // C4 stays 60
            Assert.Equal((byte)108, Remapper.ClampMelodicNote(108)); // C8 high note stays 108

            // Outliers < 24 fold up by octaves
            Assert.Equal((byte)25, Remapper.ClampMelodicNote(1));  // Note 1 -> Note 25 (>= 24)
            Assert.Equal((byte)32, Remapper.ClampMelodicNote(20)); // Note 20 -> Note 32 (>= 24)

            // Outliers > 108 fold down by octaves
            Assert.Equal((byte)103, Remapper.ClampMelodicNote(115)); // Note 115 -> Note 103 (<= 108)
        }

        [Fact]
        public void Remap_CelestaGm8_MapsToCelestaVoice()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)8) { Channel = (FourBitNumber)0 }); // GM 8 Celesta
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0 });
            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi);
            var pc = remapped.GetTimedEvents().Select(te => te.Event).OfType<ProgramChangeEvent>().First();

            // Must map to PSS-790 Voice 7: CELESTA (MidiProgram 6)
            Assert.Equal((SevenBitNumber)6, pc.ProgramNumber);
        }

        [Fact]
        public void Remap_RelocatedChannel15Melody_PreservesMidSongProgramChanges()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();

            // Occupy channels 0-8 and 10-14 with background notes so that all 16 channels are active
            for (int ch = 0; ch < 15; ch++)
            {
                if (ch == 9) continue;
                tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80) { Channel = (FourBitNumber)ch, DeltaTime = 0 });
                tr.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)ch, DeltaTime = 240 });
            }

            // Channel 9: GM Drums (Kick note)
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)9, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 240 });

            // Channel 15: Melodic instrument starting on GM 0 (Piano), then switching mid-song to GM 40 (Violin 1)
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 240 });

            // Mid-song program change on melodic channel 15
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)40) { Channel = (FourBitNumber)15, DeltaTime = 240 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)90) { Channel = (FourBitNumber)15, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)15, DeltaTime = 240 });

            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi);
            var timedEvents = remapped.GetTimedEvents().ToList();

            // Channel 9 (vacated by drums) is now the relocated melodic channel
            var ch9ProgramChanges = timedEvents
                .Where(te => te.Event is ProgramChangeEvent pc && pc.Channel == (FourBitNumber)9)
                .Select(te => (ProgramChangeEvent)te.Event)
                .ToList();

            // Both program changes must be preserved and translated to PSS-790 voices
            Assert.Equal(2, ch9ProgramChanges.Count);
            // GM 0 (Piano) -> PSS-790 Voice 0 (MidiProgram 3)
            Assert.Equal((SevenBitNumber)3, ch9ProgramChanges[0].ProgramNumber);
            // GM 40 (Violin 1) -> PSS-790 Voice 30 (MidiProgram 10)
            Assert.Equal((SevenBitNumber)10, ch9ProgramChanges[1].ProgramNumber);
        }

        [Fact]
        public void Remap_EmulateSustainPedal_ExtendsStaccatoNotesAndFiltersCC64()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            // Sustain pedal DOWN at tick 0
            tr.Events.Add(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)127) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            // Note On at tick 0
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            // Staccato key release at tick 100
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 100 });
            // Sustain pedal UP at tick 800 (DeltaTime 700)
            tr.Events.Add(new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 700 });
            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi, new RemapperOptions { EmulateSustainPedal = true });

            var notes = remapped.GetNotes().ToList();
            Assert.Single(notes);
            // Note should be extended from 100 ticks to at least 750+ ticks
            Assert.True(notes[0].Length >= 750, $"Expected length >= 750, got {notes[0].Length}");

            // CC 64 should be filtered out to protect PSS-790 serial buffer
            var ccs = remapped.GetTimedEvents().Select(te => te.Event).OfType<ControlChangeEvent>().ToList();
            Assert.DoesNotContain(ccs, cc => (byte)cc.ControlNumber == 64);
        }

        [Fact]
        public void Remap_NeutralizeAcousticPitchBends_CentersPianoPitchBend()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 0 }); // GM 0 Grand Piano
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new PitchBendEvent(4000) { Channel = (FourBitNumber)0, DeltaTime = 50 }); // Detuned flat pitch bend
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 100 });
            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi, new RemapperOptions { NeutralizeAcousticPitchBends = true });

            var pbs = remapped.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().ToList();
            Assert.NotEmpty(pbs);
            // All pitch bends on piano channel must be neutralized to pure 8192
            Assert.All(pbs, pb => Assert.Equal(8192, pb.PitchValue));
        }

        [Fact]
        public void Remap_ScalesPitchBend_PreservesGuitarPitchBend()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)29) { Channel = (FourBitNumber)0, DeltaTime = 0 }); // GM 29 Overdriven Guitar
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new PitchBendEvent(16383) { Channel = (FourBitNumber)0, DeltaTime = 50 }); // Full pitch bend
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 100 });
            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi, new RemapperOptions { NeutralizeAcousticPitchBends = true, ScalePitchBend = true });

            var pbs = remapped.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().ToList();
            // Guitar must retain scaled pitch bend
            var scaledPb = pbs.FirstOrDefault(p => p.PitchValue > 8192);
            Assert.NotNull(scaledPb);
            Assert.InRange(scaledPb.PitchValue, 9550, 9565);
        }

        [Fact]
        public void Remap_AutoCenterDanglingPitchBends_InsertsCenterBendAtTrackEnd()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)30) { Channel = (FourBitNumber)0, DeltaTime = 0 }); // Distortion Guitar
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new PitchBendEvent(12000) { Channel = (FourBitNumber)0, DeltaTime = 50 }); // Dangling sharp bend
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 100 });
            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi, new RemapperOptions { AutoCenterDanglingPitchBends = true });

            var pbs = remapped.GetTimedEvents().Select(te => te.Event).OfType<PitchBendEvent>().ToList();
            Assert.NotEmpty(pbs);
            // The final pitch bend on channel 0 must be centered at 8192
            Assert.Equal(8192, pbs.Last().PitchValue);
        }

        [Fact]
        public void Remap_DefaultReverbBoost_InjectsReverbOnChannelsWithoutReverb()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { Channel = (FourBitNumber)0, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 100 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)100) { Channel = (FourBitNumber)9, DeltaTime = 0 }); // Drum
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)38, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 100 });
            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi, new RemapperOptions { DefaultReverbBoost = true });

            var ccs = remapped.GetTimedEvents().Select(te => te.Event).OfType<ControlChangeEvent>().ToList();
            var ch0Reverb = ccs.FirstOrDefault(c => c.Channel == (FourBitNumber)0 && c.ControlNumber == (SevenBitNumber)91);
            var ch15Reverb = ccs.FirstOrDefault(c => c.Channel == (FourBitNumber)15 && c.ControlNumber == (SevenBitNumber)91);

            Assert.NotNull(ch0Reverb);
            Assert.Equal((SevenBitNumber)54, ch0Reverb.ControlValue); // Melodic reverb
            Assert.NotNull(ch15Reverb);
            Assert.Equal((SevenBitNumber)32, ch15Reverb.ControlValue); // Drum reverb
        }

        [Fact]
        public void Remap_AcousticMelodicNoteClamping_FoldsNotesAbove88()
        {
            // For Piano (GM 0), note 92 (E6+) should fold down by 1 octave to 80 on PSS-790
            Assert.Equal((byte)80, Remapper.ClampMelodicNote(92, 0, is680: false));
            // On PSS-680 (2-Op FM), note 92 stays 92 (no AWM aliasing ceiling)
            Assert.Equal((byte)92, Remapper.ClampMelodicNote(92, 0, is680: true));
            // For Flute / Synth (GM 80), note 92 stays 92, and note 108 stays 108
            Assert.Equal((byte)92, Remapper.ClampMelodicNote(92, 80));
            Assert.Equal((byte)108, Remapper.ClampMelodicNote(108, 80));
            // For Flute / Synth, note 115 folds down to 103 (<= 108)
            Assert.Equal((byte)103, Remapper.ClampMelodicNote(115, 80));
        }

        [Fact]
        public void Remap_DoomE1M1_BasslineAndDrumsPreserved()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            
            // Channel 3 (index 2): Pick Bass (GM 34) playing E1M1 riff (notes 28, 33, 35, 37, 40)
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)34) { Channel = (FourBitNumber)2 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)28, (SevenBitNumber)100) { Channel = (FourBitNumber)2, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)28, (SevenBitNumber)0) { Channel = (FourBitNumber)2, DeltaTime = 100 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)40, (SevenBitNumber)100) { Channel = (FourBitNumber)2, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)40, (SevenBitNumber)0) { Channel = (FourBitNumber)2, DeltaTime = 100 });

            // Channel 10 (index 9): Drums with GM 40 (Electric Snare) and GM 51 (Ride Cymbal 1)
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)40, (SevenBitNumber)100) { Channel = (FourBitNumber)9, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)40, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 100 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)51, (SevenBitNumber)100) { Channel = (FourBitNumber)9, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)51, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 100 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)53, (SevenBitNumber)100) { Channel = (FourBitNumber)9, DeltaTime = 0 });
            tr.Events.Add(new NoteOffEvent((SevenBitNumber)53, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 100 });

            midi.Chunks.Add(tr);

            var remapped = Remapper.Remap(midi);
            var remappedNotes = remapped.GetNotes().ToList();

            // Bass notes on Channel 3 should retain exact pitches 28 and 40 (no folding to unison!)
            var ch2Notes = remappedNotes.Where(n => n.Channel == 2).ToList();
            Assert.Equal(2, ch2Notes.Count);
            Assert.Equal((byte)28, (byte)ch2Notes[0].NoteNumber);
            Assert.Equal((byte)40, (byte)ch2Notes[1].NoteNumber);

            // Drum notes on Channel 16:
            var ch15Notes = remappedNotes.Where(n => n.Channel == 15).ToList();
            Assert.Equal(3, ch15Notes.Count);
            // GM 40 -> PSS-790 Note 49 (SNARE HI)
            Assert.Equal((byte)49, (byte)ch15Notes[0].NoteNumber);
            // GM 51 -> PSS-790 Note 63 (RIDE CYMBAL EDGE)
            Assert.Equal((byte)63, (byte)ch15Notes[1].NoteNumber);
            // GM 53 -> PSS-790 Note 62 (RIDE CYMBAL CUP)
            Assert.Equal((byte)62, (byte)ch15Notes[2].NoteNumber);
        }
    }
}
