using System;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class XgMidiCompatibilityTests
    {
        [Fact]
        public void Remap_XgSysExVoicing_ExtractsVoicingAndStripsSysEx()
        {
            // Arrange: Track with XG Parameter Change SysEx: Part 0 (Ch 1) set to GM 40 (Violin)
            // SysEx: 43 10 4C 08 00 03 28 (where 28h = 40 decimal = Violin)
            var midi = new MidiFile();
            var track = new TrackChunk();

            var sysexData = new byte[] { 0x43, 0x10, 0x4C, 0x08, 0x00, 0x03, 0x28 };
            track.Events.Add(new NormalSysExEvent(sysexData) { DeltaTime = 0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            midi.Chunks.Add(track);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "XgSysExTest.mid");
            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790" });

            // Assert
            Assert.NotEmpty(report.XgSysExChannels);
            Assert.Contains(report.XgSysExChannels, s => s.Contains("Ch 1") && s.Contains("VIOLIN", StringComparison.OrdinalIgnoreCase));

            // SysEx must be completely stripped from output
            var remappedSysEx = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<SysExEvent>().ToList();
            Assert.Empty(remappedSysEx);

            // Channel 0 must receive PSS-790 VIOLIN 1 (Voice 30, MidiProgram 10)
            var pcs = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<ProgramChangeEvent>().ToList();
            Assert.NotEmpty(pcs);
            var ch0Pc = pcs.First(p => p.Channel == (FourBitNumber)0);
            Assert.Equal((SevenBitNumber)10, ch0Pc.ProgramNumber);
        }

        [Fact]
        public void Remap_XgSecondaryDrumChannel_RelocatesToChannel16AndProtectsMelodic()
        {
            // Arrange:
            // - Ch 9 (Channel 10) standard drums
            // - Ch 8 (Channel 9) secondary drums via Bank Select CC 0 = 127
            // - Ch 15 (Channel 16) has melodic bass notes
            var midi = new MidiFile();

            var drumTrack = new TrackChunk();
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)9 }); // Standard Kick
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)9 });
            midi.Chunks.Add(drumTrack);

            var secDrumTrack = new TrackChunk();
            secDrumTrack.Events.Add(new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)127) { Channel = (FourBitNumber)8 });
            secDrumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)100) { Channel = (FourBitNumber)8 }); // Snare on Ch 9
            secDrumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)38, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)8 });
            midi.Chunks.Add(secDrumTrack);

            var melodicTrack = new TrackChunk();
            melodicTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)32) { Channel = (FourBitNumber)15 }); // Acoustic Bass
            melodicTrack.Events.Add(new NoteOnEvent((SevenBitNumber)48, (SevenBitNumber)90) { Channel = (FourBitNumber)15 }); // C2
            melodicTrack.Events.Add(new NoteOffEvent((SevenBitNumber)48, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)15 });
            midi.Chunks.Add(melodicTrack);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "DualDrums.mid");
            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790" });

            // Assert
            Assert.Contains(8, report.NonStandardDrumChannels);

            var allNotes = remapped.GetNotes().ToList();
            var ch15Notes = allNotes.Where(n => (int)n.Channel == 15).ToList();

            // All drums (Ch 9 and Ch 8) must now be on Channel 15 (Ch 16)
            Assert.True(ch15Notes.Count >= 2);
            // GM 36 -> PSS 45 (BASS DRUM HI)
            Assert.Contains(ch15Notes, n => n.NoteNumber == (SevenBitNumber)45);
            // GM 38 -> PSS 49 (SNARE HI)
            Assert.Contains(ch15Notes, n => n.NoteNumber == (SevenBitNumber)49);

            // Melodic bass from Ch 15 must be safely relocated to an unused channel (e.g. Ch 0)
            var melodicNotes = allNotes.Where(n => (int)n.Channel != 15).ToList();
            Assert.NotEmpty(melodicNotes);
            Assert.Contains(melodicNotes, n => n.Channel != (FourBitNumber)8 && n.Channel != (FourBitNumber)9);
        }

        [Fact]
        public void Remap_XgExtendedDrumNotes_TranslatesAllNotesMusically_OnPss790AndPss680()
        {
            // Note 25: Snare Roll -> Snare (49 on 790, 52 on 680)
            Assert.Equal((byte)49, Remapper.TranslateDrumNote(25));
            Assert.Equal((byte)52, Pss680Constants.TranslateDrumNote(25));

            // Note 26: Finger Snap -> High Q / Rimshot (64 on 790, 51 on 680)
            Assert.Equal((byte)64, Remapper.TranslateDrumNote(26));
            Assert.Equal((byte)51, Pss680Constants.TranslateDrumNote(26));

            // Notes 29 & 30: Scratch Push/Pull -> Guiro / Hi-Hat
            Assert.Equal((byte)79, Remapper.TranslateDrumNote(29));
            Assert.Equal((byte)57, Pss680Constants.TranslateDrumNote(29));
            Assert.Equal((byte)79, Remapper.TranslateDrumNote(30));
            Assert.Equal((byte)57, Pss680Constants.TranslateDrumNote(30));

            // Note 34: Metronome Bell -> Triangle Open (39 on 790, 61 on 680)
            Assert.Equal((byte)39, Remapper.TranslateDrumNote(34));
            Assert.Equal((byte)61, Pss680Constants.TranslateDrumNote(34));

            // Note 86 & 87: Surdo Mute/Open -> Bass Drum
            Assert.Equal((byte)45, Remapper.TranslateDrumNote(86));
            Assert.Equal((byte)45, Pss680Constants.TranslateDrumNote(86));
            Assert.Equal((byte)45, Remapper.TranslateDrumNote(87));
            Assert.Equal((byte)45, Pss680Constants.TranslateDrumNote(87));

            // Notes 90 & 91: Triangle Mute / Open
            Assert.Equal((byte)37, Remapper.TranslateDrumNote(90));
            Assert.Equal((byte)39, Remapper.TranslateDrumNote(91));
        }

        [Fact]
        public void Remap_XgSfxBank_MapsToHardwareSfxVoices_OnPss790AndPss680()
        {
            // PSS-790 SFX Translation:
            // PC 49 (Laser Gun / Explosion) -> PSS-790 ORCHESTRA HIT (Voice 99, MidiProgram 90)
            byte pss790Laser = Remapper.TranslateXgSfxVoice(49, is680: false);
            Assert.Equal((byte)90, pss790Laser);

            // PC 90 (Helicopter) -> PSS-790 DISTORTION GUITAR (Voice 15, MidiProgram 13)
            byte pss790Heli = Remapper.TranslateXgSfxVoice(90, is680: false);
            Assert.Equal((byte)13, pss790Heli);

            // PC 88 (Siren) -> PSS-790 GLOCKENSPIEL / SYNTH LEAD (Voice 43, MidiProgram 32)
            byte pss790Siren = Remapper.TranslateXgSfxVoice(88, is680: false);
            Assert.Equal((byte)32, pss790Siren);

            // PSS-680 FM SFX Translation:
            // PC 49 (Laser Gun) -> PSS-680 Voice 97 (LASER)
            byte pss680Laser = Remapper.TranslateXgSfxVoice(49, is680: true);
            Assert.Equal((byte)97, pss680Laser);

            // PC 90 (Helicopter) -> PSS-680 Voice 99 (UFO)
            byte pss680Heli = Remapper.TranslateXgSfxVoice(90, is680: true);
            Assert.Equal((byte)99, pss680Heli);

            // PC 88 (Siren) -> PSS-680 Voice 95 (SIREN)
            byte pss680Siren = Remapper.TranslateXgSfxVoice(88, is680: true);
            Assert.Equal((byte)95, pss680Siren);
        }

        [Fact]
        public void Remap_XgSfxBankTrack_RemapsChannelVoiceCleanly()
        {
            // Arrange: Track with Bank Select CC 0 = 64 (SFX Bank) and PC = 49 (Laser Gun)
            var midi = new MidiFile();
            var track = new TrackChunk();
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)64) { DeltaTime = 0, Channel = (FourBitNumber)3 });
            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)49) { DeltaTime = 0, Channel = (FourBitNumber)3 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)3 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)3 });
            midi.Chunks.Add(track);

            // Act
            var report = MidiHealthInspector.Analyze(midi, fileName: "SfxTrack.mid");
            var remapped790 = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790" });
            var remapped680 = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss680" });

            // Assert
            Assert.Contains(3, report.XgSfxChannels);

            // PSS-790: Ch 3 Program Change must be 90 (ORCHESTRA HIT)
            var pc790 = remapped790.GetTrackChunks().SelectMany(t => t.Events).OfType<ProgramChangeEvent>().First(p => p.Channel == (FourBitNumber)3);
            Assert.Equal((SevenBitNumber)90, pc790.ProgramNumber);

            // PSS-680: Ch 3 Program Change must be 97 (LASER)
            var pc680 = remapped680.GetTrackChunks().SelectMany(t => t.Events).OfType<ProgramChangeEvent>().First(p => p.Channel == (FourBitNumber)3);
            Assert.Equal((SevenBitNumber)97, pc680.ProgramNumber);
        }

        [Fact]
        public void Remap_SequentialMidTrackProgramChanges_RemapsEachVoiceInPlace()
        {
            // Arrange: Track that switches instruments midway through:
            // 1. GM 0 (Acoustic Piano) -> PSS-790 Voice 00 (Piano, MidiProgram 3)
            // 2. GM 73 (Flute) -> PSS-790 Voice 70 (Flute)
            // 3. GM 56 (Trumpet) -> PSS-790 Voice 50 (Trumpet)
            var midi = new MidiFile();
            var track = new TrackChunk();

            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });

            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)73) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });

            track.Events.Add(new ProgramChangeEvent((SevenBitNumber)56) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)67, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber)67, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });

            midi.Chunks.Add(track);

            // Act
            var remapped = Remapper.Remap(midi, new RemapperOptions { TargetModel = "pss790" });

            // Assert
            var pcs = remapped.GetTrackChunks().SelectMany(t => t.Events).OfType<ProgramChangeEvent>().ToList();
            Assert.Equal(3, pcs.Count);

            // 1. GM 0 -> PSS-790 Piano (MidiProgram 3)
            byte pianoProg = Constants.Pss790Voices[Constants.GmToPss790VoiceMap[0]].MidiProgram;
            Assert.Equal((SevenBitNumber)pianoProg, pcs[0].ProgramNumber);
            // 2. GM 73 (Flute) -> PSS-790 Voice 70 (MidiProgram 18)
            byte fluteProg = Constants.Pss790Voices[Constants.GmToPss790VoiceMap[73]].MidiProgram;
            Assert.Equal((SevenBitNumber)fluteProg, pcs[1].ProgramNumber);
            // 3. GM 56 (Trumpet) -> PSS-790 Voice 50 (MidiProgram 13)
            byte trumpetProg = Constants.Pss790Voices[Constants.GmToPss790VoiceMap[56]].MidiProgram;
            Assert.Equal((SevenBitNumber)trumpetProg, pcs[2].ProgramNumber);
        }
    }
}
