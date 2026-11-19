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
    public class ShardBAuditTests
    {
        private readonly ITestOutputHelper _output;

        public static readonly string[] ShardBFiles = new[]
        {
            "Elf Forest.mid",
            "Eminem - Mockingbird.mid",
            "Evanescence - My Immortal.mid",
            "EyesOnMePiano.mid",
            "FF6 Terra.mid",
            "FF7-OtherSideOfTheMountain.mid",
            "FF7-SephirothsTheme.mid",
            "FF7AC-ThoseWhoFight.mid",
            "Ff7 - One Winged.mid",
            "Ffvii Battle.mid",
            "Final Countdown.mid",
            "From Now On.mid",
            "Full Metal Alchemist Brotherhood - Let It Out (TV Size).mid",
            "Gang Plank.mid",
            "Great War.mid",
            "Greatest Showman Theme - Never Enough.mid",
            "GyakutenSistersBalladev11.mid",
            "HP Theme.mid",
            "Halo Maine Theme Extended Maw Version.mid",
            "Hurt.mid",
            "Hybris.mid",
            "Ice Mountain Zone Act 1.mid",
            "Idina Menzel Let It Go (Piano Version).mid",
            "Investigation Cornered.mid",
            "JB WDUMean.mid",
            "John Murphy - Adagio In D Minor.mid",
            "Justin Bieber - Baby.mid",
            "Justin Bieber - Mistletoe.mid",
            "KRool.mid",
            "KingKRoolRemixModifSlightly.mid",
            "Kingdom Hearts - Dearly Beloved.mid",
            "ff8-lfp.mid",
            "gerudo.mid"
        };

        public ShardBAuditTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Audit_All33ShardBFiles_VerifyAnomaliesAndRemapper()
        {
            string midiDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Midi", "GM Midi");
            if (!Directory.Exists(midiDir) || !ShardBFiles.All(f => File.Exists(Path.Combine(midiDir, f))))
            {
                _output.WriteLine($"[AUDIT SKIPPED] Local legacy directory or shard files not found: {midiDir}");
                return;
            }

            foreach (var filename in ShardBFiles)
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

                // After Remap: verify drum channel is strictly 15 (Ch 16)
                var remappedNotes = remapped.GetNotes().ToList();
                if (activeChannels.Contains(9))
                {
                    Assert.Contains(remappedNotes, n => n.Channel == (FourBitNumber)15);
                }

                _output.WriteLine($"[AUDIT OK] {filename:40} | Notes: {allNotes.Count,5} | Range: [{minPitch,3}, {maxPitch,3}] | PB: {pitchBends.Count,2} | MissingPC: [{string.Join(",", missingPc)}] | BPM: [{minBpm:F1}, {maxBpm:F1}] | GS: {hasRolandGs} | XG: {hasYamahaXg}");
            }
        }
    }
}
