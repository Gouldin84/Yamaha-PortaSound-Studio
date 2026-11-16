using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class PolyphonyTests
    {
        [Fact]
        public void Polyphony_ChokesOpenHiHatWhenClosedHit()
        {
            // Arrange
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();

            // Open Hi-Hat (59) on Channel 15 at time 0 with length 1000
            trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)59, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)15 });
            
            // Closed Hi-Hat (57) on Channel 15 at time 300
            trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)57, (SevenBitNumber)100) { DeltaTime = 300, Channel = (FourBitNumber)15 });
            trackChunk.Events.Add(new NoteOffEvent((SevenBitNumber)57, (SevenBitNumber)0) { DeltaTime = 100, Channel = (FourBitNumber)15 });

            // Original Open Hi-Hat Note-Off at time 1000
            trackChunk.Events.Add(new NoteOffEvent((SevenBitNumber)59, (SevenBitNumber)0) { DeltaTime = 600, Channel = (FourBitNumber)15 });

            midiFile.Chunks.Add(trackChunk);

            // Act
            var optimizer = new PolyphonyOptimizer(enabled: true);
            var optimized = optimizer.Optimize(midiFile);

            // Assert
            var notes = optimized.GetNotes().ToList();
            var openHh = notes.First(n => n.NoteNumber == (SevenBitNumber)59);

            // Open Hi-Hat should be choked: length should end at or around 300
            Assert.True(openHh.EndTime <= 300);
        }

        [Fact]
        public void Polyphony_CullsIncidentalDrumsWhenCongested()
        {
            // Arrange: create a congestion peak of 25 simultaneous notes (threshold = 22)
            // 23 melodic notes on Channel 0, plus 2 incidental drum notes (Triangle note 37, Cowbell note 55) on Channel 15
            var midiFile = new MidiFile();
            var trackChunk = new TrackChunk();

            // 23 melodic notes
            for (byte n = 40; n < 63; n++)
            {
                trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)n, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            }

            // 2 incidental drum notes
            trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)37, (SevenBitNumber)60) { DeltaTime = 0, Channel = (FourBitNumber)15 }); // Triangle
            trackChunk.Events.Add(new NoteOnEvent((SevenBitNumber)55, (SevenBitNumber)60) { DeltaTime = 0, Channel = (FourBitNumber)15 }); // Cowbell

            // End all notes at time 500
            trackChunk.Events.Add(new NoteOffEvent((SevenBitNumber)40, (SevenBitNumber)0) { DeltaTime = 500, Channel = (FourBitNumber)0 });
            for (byte n = 41; n < 63; n++)
            {
                trackChunk.Events.Add(new NoteOffEvent((SevenBitNumber)n, (SevenBitNumber)0) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            }
            trackChunk.Events.Add(new NoteOffEvent((SevenBitNumber)37, (SevenBitNumber)0) { DeltaTime = 0, Channel = (FourBitNumber)15 });
            trackChunk.Events.Add(new NoteOffEvent((SevenBitNumber)55, (SevenBitNumber)0) { DeltaTime = 0, Channel = (FourBitNumber)15 });

            midiFile.Chunks.Add(trackChunk);

            // Act
            var optimizer = new PolyphonyOptimizer(enabled: true, threshold: 22);
            var optimized = optimizer.Optimize(midiFile);

            // Assert: Melodic notes are strictly preserved, incidental drums were culled
            var remainingNotes = optimized.GetNotes().ToList();
            var melodicCount = remainingNotes.Count(n => n.Channel == (FourBitNumber)0);
            Assert.Equal(23, melodicCount); // Melody strictly untouched!

            var drumNotes = remainingNotes.Where(n => n.Channel == (FourBitNumber)15).ToList();
            Assert.True(drumNotes.Count < 2); // At least one incidental drum note culled
        }
    }
}
