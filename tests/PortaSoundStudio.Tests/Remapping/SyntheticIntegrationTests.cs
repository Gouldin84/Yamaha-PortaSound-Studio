using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class SyntheticIntegrationTests
    {
        [Fact]
        public void Remap_SyntheticGeneralMidi_PreservesIntegrityAndAppliesPSS790Rules()
        {
            var sourceMidi = new MidiFile();

            // Track 1: Drums on Channel 9 (10 in 1-based)
            var drumTrack = new TrackChunk();
            drumTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)90) { DeltaTime = 120, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)42, (SevenBitNumber)80) { DeltaTime = 120, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)38, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)42, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            sourceMidi.Chunks.Add(drumTrack);

            // Track 2: Piano on Channel 0
            var pianoTrack = new TrackChunk();
            pianoTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            pianoTrack.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            pianoTrack.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            pianoTrack.Events.Add(new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)64) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            pianoTrack.Events.Add(new ChannelAftertouchEvent((SevenBitNumber)80) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            pianoTrack.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            sourceMidi.Chunks.Add(pianoTrack);

            // Track 3: Melodic voice on Channel 15 (Collision test)
            var collisionTrack = new TrackChunk();
            collisionTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)15 });
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

            using var ms = new MemoryStream();
            remapped.Write(ms);
            ms.Position = 0;
            var reloaded = MidiFile.Read(ms);

            Assert.NotNull(reloaded);
            var timedEvents = reloaded.GetTimedEvents().ToList();
            Assert.NotEmpty(timedEvents);

            foreach (var te in timedEvents)
            {
                Assert.False(te.Event is ChannelAftertouchEvent or NoteAftertouchEvent);
                if (te.Event is ControlChangeEvent cc)
                {
                    Assert.Contains((byte)cc.ControlNumber, Constants.AllowedControlChanges);
                }
            }

            var drumNoteOns = timedEvents.Select(te => te.Event).OfType<NoteOnEvent>().Where(n => n.Channel == (FourBitNumber)15).ToList();
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)45);
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)49);
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)57);
        }

        [Fact]
        public void ManageTimedEvents_PreservesAbsoluteTimeWhenRemovingEvents()
        {
            var track = new TrackChunk();
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)64) { DeltaTime = 500 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 1000 });

            Assert.Equal(1500, track.GetTimedEvents().ElementAt(1).Time);

            using (var mgr = track.ManageTimedEvents())
            {
                mgr.Objects.RemoveAll(te => te.Event is ControlChangeEvent);
            }

            Assert.Single(track.Events);
            Assert.Equal(1500, track.Events[0].DeltaTime);
            Assert.Equal(1500, track.GetTimedEvents().ElementAt(0).Time);
        }
    }
}
