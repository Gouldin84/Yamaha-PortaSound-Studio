using System;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class PSS680SysExTests : IDisposable
    {
        private readonly string _testDir;

        public PSS680SysExTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "PSS680_SysEx_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void Pss680_Format00_SysExMessage_HasCorrectLengthAndFraming()
        {
            var voice = new PSS680Voice
            {
                BankNumber = 1 // Bank 2
            };

            byte[] sysex = voice.ToSysExMessage();

            // Total length: F0 + 43 + 76 + 00 + (33 * 2 nibbles) + checksum + F7 = 72 bytes
            Assert.Equal(72, sysex.Length);
            Assert.Equal(0xF0, sysex[0]);
            Assert.Equal(0x43, sysex[1]); // Yamaha ID
            Assert.Equal(0x76, sysex[2]); // PSS Class ID
            Assert.Equal(0x00, sysex[3]); // Format 00H (FM Voice)
            Assert.Equal(0xF7, sysex[^1]); // EOX
        }

        [Fact]
        public void Pss680_Format00_Nibbles_TransmittedHighNibbleFirst()
        {
            var voice = new PSS680Voice
            {
                BankNumber = 3 // Bank 4: Decoded Byte 0 = 0x03 -> High nibble = 0x00, Low nibble = 0x03
            };
            // Set Carrier Total Level to panel level 99 -> SysEx raw = 0x00
            // Set Modulator Total Level to panel level 0 -> SysEx raw = 0x63 (99) -> High nibble = 0x06, Low nibble = 0x03
            voice.Carrier.PanelLevel = 99;
            voice.Modulator.PanelLevel = 0;

            byte[] sysex = voice.ToSysExMessage();

            // Bank number is Byte 0 -> nibbles at index 4 and 5
            Assert.Equal(0x00, sysex[4]); // High nibble of 0x03
            Assert.Equal(0x03, sysex[5]); // Low nibble of 0x03

            // Byte 3 is Modulator TL: 0x63 (99 decimal) -> nibbles at index 4 + 3*2 = 10, 11
            Assert.Equal(0x06, sysex[10]); // High nibble of 0x63
            Assert.Equal(0x03, sysex[11]); // Low nibble of 0x63
        }

        [Fact]
        public void Pss680_Format00_Checksum_MatchesManualFormula()
        {
            var voice = new PSS680Voice
            {
                BankNumber = 0
            };

            byte[] decoded = voice.ToDecodedBytes();
            int sum = decoded.Sum(b => (int)b);
            byte expectedChecksum = (byte)((128 - (sum & 0x7F)) & 0x7F);

            byte[] sysex = voice.ToSysExMessage();
            byte actualChecksum = sysex[^2];

            Assert.Equal(expectedChecksum, actualChecksum);
        }

        [Fact]
        public void Pss680_Format00_RoundTrip_PreservesAllParameters()
        {
            var original = new PSS680Voice
            {
                BankNumber = 2,
                Feedback = 6,
                PitchModulationSensitivity = 5,
                AmplitudeModulationSensitivity = 2,
                VibratoDelayTime = 75,
                Vibrato = true,
                Sustain = true
            };

            // Modulator setup
            original.Modulator.FineDetune = -2;
            original.Modulator.Multiplier = 3;
            original.Modulator.PanelLevel = 77;
            original.Modulator.LevelKeyScalingHigh = 11;
            original.Modulator.LevelKeyScalingLow = 4;
            original.Modulator.RateKeyScaling = 2;
            original.Modulator.AttackRate = 55;
            original.Modulator.AmplitudeModulationEnable = true;
            original.Modulator.CoarseDetune = true;
            original.Modulator.Decay1Rate = 42;
            original.Modulator.Waveform = FmWaveform.SquaredSine;
            original.Modulator.Decay2Rate = 18;
            original.Modulator.Decay1Level = 9;
            original.Modulator.ReleaseRate = 8;
            original.Modulator.SustainReleaseRate = 12;

            // Carrier setup
            original.Carrier.FineDetune = 3;
            original.Carrier.Multiplier = 1;
            original.Carrier.PanelLevel = 92;
            original.Carrier.LevelKeyScalingHigh = 0;
            original.Carrier.LevelKeyScalingLow = 7;
            original.Carrier.RateKeyScaling = 1;
            original.Carrier.AttackRate = 63;
            original.Carrier.AmplitudeModulationEnable = false;
            original.Carrier.CoarseDetune = false;
            original.Carrier.Decay1Rate = 15;
            original.Carrier.Waveform = FmWaveform.SineHalfWave;
            original.Carrier.Decay2Rate = 25;
            original.Carrier.Decay1Level = 4;
            original.Carrier.ReleaseRate = 10;
            original.Carrier.SustainReleaseRate = 3;

            byte[] sysex = original.ToSysExMessage();
            var parsed = PSS680Voice.FromSysExMessage(sysex);

            Assert.Equal(original.BankNumber, parsed.BankNumber);
            Assert.Equal(original.Feedback, parsed.Feedback);
            Assert.Equal(original.PitchModulationSensitivity, parsed.PitchModulationSensitivity);
            Assert.Equal(original.AmplitudeModulationSensitivity, parsed.AmplitudeModulationSensitivity);
            Assert.Equal(original.VibratoDelayTime, parsed.VibratoDelayTime);
            Assert.True(parsed.Vibrato);
            Assert.True(parsed.Sustain);

            // Verify Modulator
            Assert.Equal(-2, parsed.Modulator.FineDetune);
            Assert.Equal(3, parsed.Modulator.Multiplier);
            Assert.Equal(77, parsed.Modulator.PanelLevel);
            Assert.Equal(11, parsed.Modulator.LevelKeyScalingHigh);
            Assert.Equal(4, parsed.Modulator.LevelKeyScalingLow);
            Assert.Equal(2, parsed.Modulator.RateKeyScaling);
            Assert.Equal(55, parsed.Modulator.AttackRate);
            Assert.True(parsed.Modulator.AmplitudeModulationEnable);
            Assert.True(parsed.Modulator.CoarseDetune);
            Assert.Equal(42, parsed.Modulator.Decay1Rate);
            Assert.Equal(FmWaveform.SquaredSine, parsed.Modulator.Waveform);
            Assert.Equal(18, parsed.Modulator.Decay2Rate);
            Assert.Equal(9, parsed.Modulator.Decay1Level);
            Assert.Equal(8, parsed.Modulator.ReleaseRate);
            Assert.Equal(12, parsed.Modulator.SustainReleaseRate);

            // Verify Carrier
            Assert.Equal(3, parsed.Carrier.FineDetune);
            Assert.Equal(1, parsed.Carrier.Multiplier);
            Assert.Equal(92, parsed.Carrier.PanelLevel);
            Assert.Equal(0, parsed.Carrier.LevelKeyScalingHigh);
            Assert.Equal(7, parsed.Carrier.LevelKeyScalingLow);
            Assert.Equal(1, parsed.Carrier.RateKeyScaling);
            Assert.Equal(63, parsed.Carrier.AttackRate);
            Assert.False(parsed.Carrier.AmplitudeModulationEnable);
            Assert.False(parsed.Carrier.CoarseDetune);
            Assert.Equal(15, parsed.Carrier.Decay1Rate);
            Assert.Equal(FmWaveform.SineHalfWave, parsed.Carrier.Waveform);
            Assert.Equal(25, parsed.Carrier.Decay2Rate);
            Assert.Equal(4, parsed.Carrier.Decay1Level);
            Assert.Equal(10, parsed.Carrier.ReleaseRate);
            Assert.Equal(3, parsed.Carrier.SustainReleaseRate);
        }

        [Fact]
        public void Pss680_Format00_CorruptChecksum_ThrowsInvalidOperationException()
        {
            var voice = new PSS680Voice();
            byte[] sysex = voice.ToSysExMessage();

            // Corrupt checksum byte (second to last)
            sysex[^2] = (byte)((sysex[^2] + 1) % 128);

            Assert.Throws<InvalidOperationException>(() => PSS680Voice.FromSysExMessage(sysex));
        }

        [Fact]
        public void Pss680_Format00_TotalLevelInversion_CompliesWithPanelSpecification()
        {
            var voice = new PSS680Voice();

            // When PanelLevel = 99 (Max volume), raw SysEx TL should be 00
            voice.Carrier.PanelLevel = 99;
            Assert.Equal(0, voice.Carrier.RawSysExTotalLevel);

            // When PanelLevel = 00 (Min volume), raw SysEx TL should be 99
            voice.Carrier.PanelLevel = 0;
            Assert.Equal(99, voice.Carrier.RawSysExTotalLevel);

            // Setting raw SysEx TL = 0 should give PanelLevel = 99
            voice.Carrier.RawSysExTotalLevel = 0;
            Assert.Equal(99, voice.Carrier.PanelLevel);

            // Setting raw SysEx TL = 99 should give PanelLevel = 0
            voice.Carrier.RawSysExTotalLevel = 99;
            Assert.Equal(0, voice.Carrier.PanelLevel);
        }

        [Fact]
        public void Pss680_SysExParser_ParsesAll4FormatsCorrectly()
        {
            // Format 00H: FM Voice (33 decoded bytes)
            byte[] data00 = new byte[Pss680Constants.Format00DecodedByteCount];
            data00[0] = 0x02; // Bank 3
            byte[] sysex00 = Pss680SysExParser.CreateSysEx(Pss680Constants.FormatFmVoice, data00);
            var parsed00 = Pss680SysExParser.Parse(sysex00);

            Assert.Equal(Pss680SysExFormat.FmVoiceData, parsed00.Format);
            Assert.True(parsed00.IsChecksumValid);
            Assert.Equal(33, parsed00.DecodedBytes.Length);
            Assert.Contains("Bank 3", parsed00.Description);

            // Format 01H: Melody Memory (5026 decoded bytes)
            byte[] data01 = new byte[Pss680Constants.Format01DecodedByteCount];
            byte[] sysex01 = Pss680SysExParser.CreateSysEx(Pss680Constants.FormatMelodyMemory, data01);
            var parsed01 = Pss680SysExParser.Parse(sysex01);

            Assert.Equal(Pss680SysExFormat.MelodyMemory, parsed01.Format);
            Assert.True(parsed01.IsChecksumValid);
            Assert.Equal(5026, parsed01.DecodedBytes.Length);

            // Format 02H: Chord Memory (202 decoded bytes)
            byte[] data02 = new byte[Pss680Constants.Format02DecodedByteCount];
            data02[0] = 0x04; // Bank 5
            byte[] sysex02 = Pss680SysExParser.CreateSysEx(Pss680Constants.FormatChordMemory, data02);
            var parsed02 = Pss680SysExParser.Parse(sysex02);

            Assert.Equal(Pss680SysExFormat.ChordMemory, parsed02.Format);
            Assert.True(parsed02.IsChecksumValid);
            Assert.Equal(202, parsed02.DecodedBytes.Length);
            Assert.Contains("Bank 5", parsed02.Description);

            // Format 03H: Rhythm Pattern / Custom Drummer (358 decoded bytes)
            byte[] data03 = new byte[Pss680Constants.Format03DecodedByteCount];
            byte[] sysex03 = Pss680SysExParser.CreateSysEx(Pss680Constants.FormatRhythmPattern, data03);
            var parsed03 = Pss680SysExParser.Parse(sysex03);

            Assert.Equal(Pss680SysExFormat.RhythmPattern, parsed03.Format);
            Assert.True(parsed03.IsChecksumValid);
            Assert.Equal(358, parsed03.DecodedBytes.Length);
            Assert.Contains("Custom Drummer", parsed03.Description);
        }

        [Fact]
        public void Pss680_SysExManager_DetectsPss680DumpType()
        {
            var voice = new PSS680Voice { BankNumber = 0 };
            byte[] sysex = voice.ToSysExMessage();
            var filePath = Path.Combine(_testDir, "test_pss680_voice.syx");
            File.WriteAllBytes(filePath, sysex);

            var info = SysExManager.ParseDumpInfo(filePath);

            Assert.Equal("Yamaha", info.Manufacturer);
            Assert.Equal("PSS-680/480/780 FM Voice Data (Format 00H)", info.DumpType);
            Assert.Equal(1, info.PacketCount);
            Assert.Equal(72, info.FileSizeBytes);
        }

        [Fact]
        public void Pss680_VoiceAndStyleTables_ContainExpectedEntries()
        {
            Assert.Equal(108, Pss680Constants.Voices.Count);
            Assert.Equal(100, Pss680Constants.Styles.Count);

            Assert.Equal("Synth Brass", Pss680Constants.Voices[0].Name);
            Assert.Equal("Sine Wave", Pss680Constants.Voices[99].Name);
            Assert.Equal("User FM Synth Bank 1", Pss680Constants.Voices[100].Name);

            Assert.Equal("Rhythm & Blues 1", Pss680Constants.Styles[0].Name);
            Assert.Equal("Barbershop Quartet", Pss680Constants.Styles[99].Name);
        }

        [Fact]
        public void Pss680_DrumMap_Contains32PercussionInstruments()
        {
            // Verify core drum mapping notes
            Assert.True(Pss680Constants.DrumMap.ContainsKey(45)); // Bass Drum
            Assert.Equal("Bass Drum", Pss680Constants.DrumMap[45].Name);

            Assert.True(Pss680Constants.DrumMap.ContainsKey(49)); // Snare Hi
            Assert.Equal("Snare Hi", Pss680Constants.DrumMap[49].Name);

            Assert.True(Pss680Constants.DrumMap.ContainsKey(57)); // Hi-Hat Closed
            Assert.Equal("Hi-Hat Closed", Pss680Constants.DrumMap[57].Name);

            Assert.True(Pss680Constants.DrumMap.ContainsKey(59)); // Hi-Hat Open
            Assert.Equal("Hi-Hat Open", Pss680Constants.DrumMap[59].Name);

            Assert.True(Pss680Constants.DrumMap.ContainsKey(60)); // Crash Cymbal
            Assert.Equal("Crash Cymbal", Pss680Constants.DrumMap[60].Name);

            Assert.True(Pss680Constants.DrumMap.ContainsKey(63)); // Ride Cymbal
            Assert.Equal("Ride Cymbal", Pss680Constants.DrumMap[63].Name);
        }

        [Fact]
        public void Remapper_MasterTranspose_ShiftsMelodicChannels_LeavesDrumsIntact()
        {
            var midi = new MidiFile();
            var trMelodic = new TrackChunk();
            trMelodic.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            trMelodic.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0 });

            var trDrum = new TrackChunk();
            trDrum.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)9 });
            midi.Chunks.Add(trMelodic);
            midi.Chunks.Add(trDrum);

            var options = new RemapperOptions
            {
                TargetModel = "pss680",
                MasterTranspose = 3 // Transpose up 3 semitones (e.g. C -> Eb)
            };

            var remapped = Remapper.Remap(midi, options);
            var melodicNotes = remapped.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<NoteOnEvent>()
                .Where(n => n.Channel == (FourBitNumber)0)
                .ToList();

            var drumNotes = remapped.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<NoteOnEvent>()
                .Where(n => n.Channel == (FourBitNumber)15) // drums mapped to ch 15 on PSS-680
                .ToList();

            Assert.Single(melodicNotes);
            Assert.Equal(63, (int)melodicNotes[0].NoteNumber); // 60 + 3 = 63
            Assert.Single(drumNotes);
            // Drum note should be remapped to PSS drum note (Bass Drum 45), not transposed by MasterTranspose!
            Assert.Equal(45, (int)drumNotes[0].NoteNumber);
        }

        [Fact]
        public void Remapper_ChannelOctaveShift_AppliesIndependentlyPerChannel()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)32) { Channel = (FourBitNumber)1 }); // Acoustic Bass
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)48, (SevenBitNumber)90) { Channel = (FourBitNumber)1 });
            midi.Chunks.Add(tr);

            var options = new RemapperOptions { TargetModel = "pss680" };
            options.ChannelOctaveShifts[1] = -12; // Shift bass down 1 octave

            var remapped = Remapper.Remap(midi, options);
            var notes = remapped.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<NoteOnEvent>()
                .Where(n => n.Channel == (FourBitNumber)1)
                .ToList();

            Assert.Single(notes);
            Assert.Equal(36, (int)notes[0].NoteNumber); // 48 - 12 = 36
        }

        [Fact]
        public void Remapper_Pss680_ContourPreservation_ShiftsEntireChannelUpWhenMinNoteBelow24()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)2 }); // Fingered Bass
            // Descending bassline: 26 (D1), 24 (C1), 22 (Bb0 - below 24 floor)
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)26, (SevenBitNumber)90) { Channel = (FourBitNumber)2 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)24, (SevenBitNumber)90) { Channel = (FourBitNumber)2 });
            tr.Events.Add(new NoteOnEvent((SevenBitNumber)22, (SevenBitNumber)90) { Channel = (FourBitNumber)2 });
            midi.Chunks.Add(tr);

            var options = new RemapperOptions { TargetModel = "pss680" };
            var remapped = Remapper.Remap(midi, options);

            var notes = remapped.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<NoteOnEvent>()
                .Where(n => n.Channel == (FourBitNumber)2)
                .Select(n => (int)n.NoteNumber)
                .ToList();

            // All notes are shifted up by 12 st so the descending contour is preserved:
            // 26 -> 38, 24 -> 36, 22 -> 34
            Assert.Equal(3, notes.Count);
            Assert.Equal(38, notes[0]);
            Assert.Equal(36, notes[1]);
            Assert.Equal(34, notes[2]);
            // Descending order is preserved!
            Assert.True(notes[0] > notes[1]);
            Assert.True(notes[1] > notes[2]);
        }

        [Fact]
        public void Remapper_Pss680_PitchBendBypassesAcousticNeutralization()
        {
            var midi = new MidiFile();
            var tr = new TrackChunk();
            // Piano on Channel 0 (normally acoustic neutralized on PSS-790 AWM)
            tr.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 });
            tr.Events.Add(new PitchBendEvent(9000) { Channel = (FourBitNumber)0 });
            midi.Chunks.Add(tr);

            var options680 = new RemapperOptions
            {
                TargetModel = "pss680",
                NeutralizeAcousticPitchBends = true
            };

            // Remap for PSS-680
            var remapped680 = Remapper.Remap(midi, options680);
            var bends680 = remapped680.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<PitchBendEvent>()
                .Where(b => b.PitchValue != 8192)
                .ToList();

            // Pitch bends are kept for 2-op FM synthesis
            Assert.NotEmpty(bends680);

            // In contrast, on PSS-790 AWM, acoustic pitch bend neutralization strips piano pitch bend to 8192
            var options790 = new RemapperOptions
            {
                TargetModel = "pss790",
                NeutralizeAcousticPitchBends = true
            };
            var remapped790 = Remapper.Remap(midi, options790);
            var bends790 = remapped790.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<PitchBendEvent>()
                .Where(b => b.PitchValue != 8192)
                .ToList();

            Assert.Empty(bends790);
        }
    }
}
