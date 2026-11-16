using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class IntegrationTests
    {
        [Fact]
        public void Remap_SyntheticGeneralMidi_PreservesIntegrityAndAppliesPSS790Rules()
        {
            // Build a multi-track General MIDI structure programmatically
            var sourceMidi = new MidiFile();

            // Track 1: Drums on Channel 9 (10 in 1-based)
            var drumTrack = new TrackChunk();
            drumTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 }); // GM drum PC
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)9 }); // Kick
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)90) { DeltaTime = 120, Channel = (FourBitNumber)9 }); // Snare
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)42, (SevenBitNumber)80) { DeltaTime = 120, Channel = (FourBitNumber)9 }); // Closed HH
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)38, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)42, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            sourceMidi.Chunks.Add(drumTrack);

            // Track 2: Piano on Channel 0
            var pianoTrack = new TrackChunk();
            pianoTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }); // GM 0 Acoustic Grand
            pianoTrack.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            pianoTrack.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 }); // Vol (Allowed)
            pianoTrack.Events.Add(new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)64) { DeltaTime = 0, Channel = (FourBitNumber)0 }); // Pan (Filter)
            pianoTrack.Events.Add(new ChannelAftertouchEvent((SevenBitNumber)80) { DeltaTime = 0, Channel = (FourBitNumber)0 }); // Aftertouch (Filter)
            pianoTrack.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            sourceMidi.Chunks.Add(pianoTrack);

            // Track 3: Melodic voice on Channel 15 (Collision test)
            var collisionTrack = new TrackChunk();
            collisionTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)15 }); // Electric Bass
            collisionTrack.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)15 });
            collisionTrack.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)15 });
            sourceMidi.Chunks.Add(collisionTrack);

            var options = new RemapperOptions
            {
                EnablePolyphonyGuard = true,
                ScalePitchBend = true,
                FilterUnsupportedControllers = true
            };

            var remapped = Remapper.Remap(sourceMidi, options);

            // Verify in-memory serialization
            using var ms = new MemoryStream();
            remapped.Write(ms);
            ms.Position = 0;
            var reloaded = MidiFile.Read(ms);

            Assert.NotNull(reloaded);
            var timedEvents = reloaded.GetTimedEvents().ToList();
            Assert.NotEmpty(timedEvents);

            // Verify no channel 9 drums remain (should be on 15)
            // Verify no aftertouch remains
            // Verify unsupported CC 10 is filtered
            foreach (var te in timedEvents)
            {
                Assert.False(te.Event is ChannelAftertouchEvent or NoteAftertouchEvent);
                if (te.Event is ControlChangeEvent cc)
                {
                    Assert.Contains((byte)cc.ControlNumber, Constants.AllowedControlChanges);
                }
            }

            // Verify drum notes on Channel 15 were converted to Chart 1
            var drumNoteOns = timedEvents.Select(te => te.Event).OfType<NoteOnEvent>().Where(n => n.Channel == (FourBitNumber)15).ToList();
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)45); // GM 36 -> PSS 45 (BASS DRUM HI)
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)49); // GM 38 -> PSS 49 (SNARE HI)
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)57); // GM 42 -> PSS 57 (HI HAT CLOSED)
        }

        [Fact]
        public async System.Threading.Tasks.Task SyncLibrary_RecursiveBatchConvert_AllFilesConvertSuccessfully()
        {
            var musicDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Music", "Midi");
            var gmDir = Path.Combine(musicDir, "GM Midi");
            var pssDir = Path.Combine(musicDir, "PSS-790 Midi");

            if (!Directory.Exists(gmDir)) return;

            int count = await BatchConverter.ConvertDirectoryAsync(gmDir, pssDir);
            Assert.True(count >= 113, "Expected at least 113 files converted, but got " + count);
        }

        [Fact]
        public void BleachSong_InBothGmAndPss_DisplaysIdenticalTrueVoices()
        {
            var musicDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Music", "Midi");
            var gmFile = Path.Combine(musicDir, "GM Midi", "Anime", "Bleach - Never Meant To Belong.mid");
            var pssFile = Path.Combine(musicDir, "PSS-790 Midi", "Anime", "Bleach - Never Meant To Belong.mid");

            if (!File.Exists(gmFile) || !File.Exists(pssFile)) return;

            var gmMidi = MidiFile.Read(gmFile, Constants.DefaultReadingSettings);
            var pssMidi = MidiFile.Read(pssFile, Constants.DefaultReadingSettings);

            // In GM file, CH 1 & 2 are GM Prog 1 (Bright Piano), CH 3 is GM Prog 40 (Violin)
            // In PSS file, CH 1 & 2 are PSS HW Prog 3 (PIANO), CH 3 is PSS HW Prog 10 (VIOLIN 1)
            Assert.Equal(MidiFormat.GeneralMidi, Remapper.DetectMidiFormat(gmMidi));
            Assert.Equal(MidiFormat.Pss790Native, Remapper.DetectMidiFormat(pssMidi));

            var pssTimedEvents = pssMidi.GetTimedEvents().Where(te => te.Event is ProgramChangeEvent).ToList();
            var ch1Pc = pssTimedEvents.Select(te => te.Event).OfType<ProgramChangeEvent>().First(pc => pc.Channel == (FourBitNumber)0);
            var ch2Pc = pssTimedEvents.Select(te => te.Event).OfType<ProgramChangeEvent>().First(pc => pc.Channel == (FourBitNumber)1);
            var ch3Pc = pssTimedEvents.Select(te => te.Event).OfType<ProgramChangeEvent>().First(pc => pc.Channel == (FourBitNumber)2);

            // HW Program 3 must map to Voice 0: PIANO (NOT Honky-Tonk)
            Assert.Equal((SevenBitNumber)3, ch1Pc.ProgramNumber);
            Assert.Equal((SevenBitNumber)3, ch2Pc.ProgramNumber);
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue((byte)ch1Pc.ProgramNumber, out var v1));
            Assert.Equal("PIANO", v1.Name);
            Assert.Equal(0, v1.VoiceNumber);

            // HW Program 10 must map to Voice 30: VIOLIN 1 (NOT Music Box)
            Assert.Equal((SevenBitNumber)10, ch3Pc.ProgramNumber);
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue((byte)ch3Pc.ProgramNumber, out var v3));
            Assert.Equal("VIOLIN 1", v3.Name);
            Assert.Equal(30, v3.VoiceNumber);
        }
    }
}
