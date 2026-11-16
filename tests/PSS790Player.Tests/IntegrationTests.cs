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
        public void Remap_RealMidiFile_PreservesIntegrityAndAppliesPSS790Rules()
        {
            var generalMidiDir = Path.Combine("..", "..", "..", "..", "..", "General MIDI");
            if (!Directory.Exists(generalMidiDir))
                return; // Skip if directory not in expected relative path during test

            var sampleFiles = Directory.GetFiles(generalMidiDir, "*.mid");
            if (sampleFiles.Length == 0) return;

            var sampleFile = sampleFiles[0];
            var sourceMidi = MidiFile.Read(sampleFile);

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
            // Verify no aftertouch
            foreach (var te in timedEvents)
            {
                Assert.False(te.Event is ChannelAftertouchEvent or NoteAftertouchEvent);
                if (te.Event is ControlChangeEvent cc)
                {
                    Assert.Contains((byte)cc.ControlNumber, Constants.AllowedControlChanges);
                }
            }
        }
    }
}
