using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;
using Xunit.Abstractions;

namespace PSS790Player.Tests
{
    public class ShardAAuditTests
    {
        private readonly ITestOutputHelper _output;

        public static readonly string[] ShardAFiles = new[]
        {
            "Afterburner.mid",
            "Alan Walker - Faded.mid",
            "Analog Hats.mid",
            "At Zanarkand Piano Collection (Unofficial).mid",
            "Avicii - Hey Brother.mid",
            "BRITNEY SPEARS.Everytime K.MID",
            "Bleach - Never Meant To Belong.mid",
            "Bleach - Will of the heart.mid",
            "Brothers In Arms 3.mid",
            "Bruno Count On Me.mid",
            "Butterfly.mid",
            "Children.mid",
            "Chillout.mid",
            "Céline Dion - My Heart Will Go On - Titanic Theme - EASY.mid",
            "DKC - Aquatic.mid",
            "DKC - Gang - Plank Galleon.mid",
            "DKC KingKRoolV6 Rabid.mid",
            "DYWBASnowman.mid",
            "Dance Dance Revolution - Butterfly - Converted.mid",
            "Dance Dance Revolution - Butterfly.mid",
            "Despacito.mid",
            "Dr Wily Stage 1.mid",
            "aqua-remix.mid",
            "balamb.mid",
            "beast2.mid",
            "bmintro.mid",
            "bmlevel1.mid",
            "dkckrool1.mid"
        };

        public ShardAAuditTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Audit_All28ShardAFiles_VerifyAnomaliesAndRemapper()
        {
            string midiDir = @"C:\Projects\PSS-790\General MIDI";
            Assert.True(Directory.Exists(midiDir), $"Directory not found: {midiDir}");

            foreach (var filename in ShardAFiles)
            {
                string path = Path.Combine(midiDir, filename);
                Assert.True(File.Exists(path), $"File not found: {path}");

                var midiFile = MidiFile.Read(path, Constants.DefaultReadingSettings);
                Assert.NotNull(midiFile);

                // 1. SysEx inspection
                var sysExEvents = midiFile.GetTrackChunks()
                    .SelectMany(t => t.Events)
                    .OfType<SysExEvent>()
                    .ToList();

                bool hasRolandGs = sysExEvents.Any(s => s.Data.Length > 0 && s.Data[0] == 0x41);
                bool hasYamahaXg = sysExEvents.Any(s => s.Data.Length > 0 && s.Data[0] == 0x43);

                // 2. Pitch bends
                var pitchBends = midiFile.GetTrackChunks()
                    .SelectMany(t => t.Events)
                    .OfType<PitchBendEvent>()
                    .ToList();

                // 3. Program changes & Active note channels
                var activeChannels = midiFile.GetTrackChunks()
                    .SelectMany(t => t.Events)
                    .OfType<NoteOnEvent>()
                    .Where(n => n.Velocity > 0)
                    .Select(n => (int)n.Channel)
                    .Distinct()
                    .ToHashSet();

                var pcChannels = midiFile.GetTrackChunks()
                    .SelectMany(t => t.Events)
                    .OfType<ProgramChangeEvent>()
                    .Select(p => (int)p.Channel)
                    .Distinct()
                    .ToHashSet();

                var missingPc = activeChannels.Where(c => !pcChannels.Contains(c)).ToList();

                // 4. Note range
                var allNotes = midiFile.GetNotes().ToList();
                int minPitch = allNotes.Count > 0 ? allNotes.Min(n => (int)n.NoteNumber) : 0;
                int maxPitch = allNotes.Count > 0 ? allNotes.Max(n => (int)n.NoteNumber) : 0;

                // 5. Tempos
                var tempos = midiFile.GetTrackChunks()
                    .SelectMany(t => t.Events)
                    .OfType<SetTempoEvent>()
                    .Select(st => 60_000_000.0 / st.MicrosecondsPerQuarterNote)
                    .ToList();

                double minBpm = tempos.Count > 0 ? tempos.Min() : 120.0;
                double maxBpm = tempos.Count > 0 ? tempos.Max() : 120.0;

                // Test Remapper
                var remapped = Remapper.Remap(midiFile);
                Assert.NotNull(remapped);

                // After Remap: verify no SysEx remain
                var remappedSysEx = remapped.GetTrackChunks()
                    .SelectMany(t => t.Events)
                    .OfType<SysExEvent>()
                    .ToList();
                Assert.Empty(remappedSysEx);

                // After Remap: verify drum channel is strictly 15 (Ch 16) if channel 9 was active
                var remappedNotes = remapped.GetNotes().ToList();
                if (activeChannels.Contains(9))
                {
                    Assert.Contains(remappedNotes, n => n.Channel == (FourBitNumber)15);
                }

                _output.WriteLine($"[SHARD A AUDIT] {filename,-45} | Notes: {allNotes.Count,5} | Range: [{minPitch,3}, {maxPitch,3}] | PB: {pitchBends.Count,5} | MissingPC: [{string.Join(",", missingPc)}] | BPM: [{minBpm,5:F1}, {maxBpm,5:F1}] | GS: {hasRolandGs,-5} | XG: {hasYamahaXg,-5}");
            }
        }
    }
}
