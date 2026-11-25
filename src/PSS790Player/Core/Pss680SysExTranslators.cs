using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PSS790Player.Core
{
    public class Pss680MelodyBankSummary
    {
        public int BankNumber { get; set; }
        public int GroupingGroup { get; set; }
        public int PitchBendRange { get; set; }
        public int Volume { get; set; }
        public int TransmitChannel { get; set; }
        public int PortamentoSpeed { get; set; }
        public int NoteCount { get; set; }
    }

    public class Pss680ChordStepInfo
    {
        public int Measure { get; set; }
        public int Beat { get; set; }
        public string ChordName { get; set; } = string.Empty;
        public string RootName { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public int RootSemitone { get; set; }
        public int QualityCode { get; set; }
    }

    public class Pss680ChordMemorySummary
    {
        public int BankNumber { get; set; }
        public int InitialStyleNumber { get; set; }
        public string InitialStyleName { get; set; } = string.Empty;
        public int Tempo { get; set; }
        public bool DrummerEnabled { get; set; }
        public bool ChordEnabled { get; set; }
        public bool BassEnabled { get; set; }
        public bool OrchestraEnabled { get; set; }
        public bool RhythmEnabled { get; set; }
        public List<Pss680ChordStepInfo> Chords { get; set; } = new();
    }

    public class Pss680DrummerTrackRow
    {
        public int TrackIndex { get; set; }
        public int InstrumentId { get; set; } = 1; // 1..32
        public byte MidiNote { get; set; } = 45;
        public string PadLabel { get; set; } = "Pad B1";
        public string InstrumentName { get; set; } = "Bass Drum";
        public bool Roll { get; set; }
        public bool Accent1 { get; set; }
        public bool Accent2 { get; set; }
        public bool[] BeatFineRes { get; set; } = new bool[8]; // false = 1/8, true = 1/16
        public byte[] StepBytes { get; set; } = new byte[8]; // 8 beats x 8 step bits
    }

    /// <summary>
    /// Decodes and compiles Yamaha PSS-680 / PSS-480 / PSS-780 SysEx Formats 01H (Melody Memory),
    /// 02H (Chord Memory), and 03H (Custom Drummer) per the official Hardware SysEx Specification.
    /// </summary>
    public class Pss680CustomDrummerPattern
    {
        // Ordered 1..32 hardware Hand Percussion pad entries (Sets A-D, Manual pp. 32-33)
        public static readonly (int Id, byte MidiNote, string Pad, string Name)[] PadCatalog =
        {
            (1, 51, "A1", "Rim Shot"),
            (2, 52, "A2", "Snare Low"),
            (3, 48, "A3", "Low Tom"),
            (4, 50, "A4", "Mid Tom"),
            (5, 53, "A5", "Hi Tom"),
            (6, 79, "A6", "Brush"),
            (7, 58, "A7", "Brush Hit"),
            (8, 63, "A8", "Ride Cymbal"),
            (9, 45, "B1", "Bass Drum"),
            (10, 49, "B2", "Snare Hi"),
            (11, 41, "B3", "Synth Tom Low"),
            (12, 42, "B4", "Synth Tom Mid"),
            (13, 43, "B5", "Synth Tom Hi"),
            (14, 57, "B6", "Hi-Hat Closed"),
            (15, 59, "B7", "Hi-Hat Open"),
            (16, 60, "B8", "Crash Cymbal"),
            (17, 67, "C1", "Bongo Low"),
            (18, 68, "C2", "Bongo Hi"),
            (19, 74, "C3", "Agogo Low"),
            (20, 75, "C4", "Agogo Hi"),
            (21, 76, "C5", "Cuica Low"),
            (22, 77, "C6", "Cuica Hi"),
            (23, 71, "C7", "Tambourine"),
            (24, 61, "C8", "Splash Cymbal"),
            (25, 64, "D1", "Conga Low"),
            (26, 65, "D2", "Conga Hi"),
            (27, 69, "D3", "Timbale Low"),
            (28, 70, "D4", "Timbale Hi"),
            (29, 55, "D5", "Cowbell"),
            (30, 56, "D6", "Cabasa"),
            (31, 54, "D7", "Hand Clap"),
            (32, 78, "D8", "Whistle")
        };

        public int BankNumber { get; set; }
        public bool IsTripletQuantize { get; set; } // Q bit (false=1/4, true=1/3)
        public int BeatsLength { get; set; } = 4; // 1..8 beats
        public byte NormalVolume { get; set; } = 6; // 0=Max .. 31=Min
        public byte Accent1Volume { get; set; } = 2;
        public byte NormalRollVolume { get; set; } = 8;
        public byte Accent1RollVolume { get; set; } = 4;
        public List<Pss680DrummerTrackRow> Tracks { get; set; } = new();

        public Pss680CustomDrummerPattern()
        {
            for (int i = 0; i < 32; i++)
            {
                var info = PadCatalog[i];
                Tracks.Add(new Pss680DrummerTrackRow
                {
                    TrackIndex = i,
                    InstrumentId = info.Id,
                    MidiNote = info.MidiNote,
                    PadLabel = $"Pad {info.Pad}",
                    InstrumentName = info.Name
                });
            }
        }

        public static Pss680CustomDrummerPattern FromSysExBytes(byte[] decodedBytes)
        {
            if (decodedBytes == null || decodedBytes.Length != Pss680Constants.Format03DecodedByteCount)
                throw new ArgumentException($"Format 03H expects {Pss680Constants.Format03DecodedByteCount} decoded bytes.");

            var pattern = new Pss680CustomDrummerPattern
            {
                BankNumber = decodedBytes[0] & 0x07,
                IsTripletQuantize = (decodedBytes[1] & 0x08) != 0,
                BeatsLength = (decodedBytes[1] & 0x07) + 1,
                NormalVolume = (byte)(decodedBytes[322] & 0x1F),
                Accent1Volume = (byte)(decodedBytes[323] & 0x1F),
                NormalRollVolume = (byte)(decodedBytes[324] & 0x1F),
                Accent1RollVolume = (byte)(decodedBytes[325] & 0x1F)
            };

            for (int t = 0; t < 32; t++)
            {
                byte instByte = decodedBytes[2 + t];
                int rawId = instByte & 0x3F;
                int clampedId = Math.Clamp(rawId == 0 ? t + 1 : rawId, 1, 32);
                var catalog = PadCatalog[clampedId - 1];

                var row = pattern.Tracks[t];
                row.InstrumentId = clampedId;
                row.MidiNote = catalog.MidiNote;
                row.PadLabel = $"Pad {catalog.Pad}";
                row.InstrumentName = catalog.Name;
                row.Roll = (instByte & 0x80) != 0;
                row.Accent1 = (instByte & 0x40) != 0;
                row.Accent2 = (decodedBytes[326 + t] & 0x01) != 0;

                byte beatResByte = decodedBytes[34 + t];
                for (int b = 0; b < 8; b++)
                {
                    row.BeatFineRes[b] = (beatResByte & (1 << (7 - b))) != 0;
                    row.StepBytes[b] = decodedBytes[66 + (t * 8) + b];
                }
            }

            return pattern;
        }

        public byte[] ToDecodedBytes()
        {
            byte[] decoded = new byte[Pss680Constants.Format03DecodedByteCount];
            decoded[0] = (byte)(BankNumber & 0x07);
            decoded[1] = (byte)((IsTripletQuantize ? 0x08 : 0x00) | (Math.Clamp(BeatsLength - 1, 0, 7) & 0x07));

            for (int t = 0; t < 32; t++)
            {
                var row = t < Tracks.Count ? Tracks[t] : new Pss680DrummerTrackRow { TrackIndex = t, InstrumentId = t + 1 };
                int id = Math.Clamp(row.InstrumentId, 1, 32);
                decoded[2 + t] = (byte)((row.Roll ? 0x80 : 0) | (row.Accent1 ? 0x40 : 0) | (id & 0x3F));

                byte beatRes = 0;
                for (int b = 0; b < 8; b++)
                {
                    if (row.BeatFineRes != null && b < row.BeatFineRes.Length && row.BeatFineRes[b])
                    {
                        beatRes |= (byte)(1 << (7 - b));
                    }
                    decoded[66 + (t * 8) + b] = (row.StepBytes != null && b < row.StepBytes.Length) ? row.StepBytes[b] : (byte)0;
                }
                decoded[34 + t] = beatRes;
                decoded[326 + t] = (byte)(row.Accent2 ? 0x01 : 0x00);
            }

            decoded[322] = (byte)(NormalVolume & 0x1F);
            decoded[323] = (byte)(Accent1Volume & 0x1F);
            decoded[324] = (byte)(NormalRollVolume & 0x1F);
            decoded[325] = (byte)(Accent1RollVolume & 0x1F);
            return decoded;
        }

        public byte[] ToSysExMessage()
        {
            return Pss680SysExParser.CreateSysEx(Pss680Constants.FormatRhythmPattern, ToDecodedBytes());
        }

        public MidiFile ToMidiFile(int bpm = 120, int loopCount = 4)
        {
            short tpqn = 480;
            int clampedBpm = Math.Clamp(bpm, 40, 240);
            int beats = Math.Clamp(BeatsLength, 1, 8);
            int loops = Math.Clamp(loopCount, 1, 16);
            int subsPerBeat = IsTripletQuantize ? 6 : 8;
            long subTicks = Math.Max(1, tpqn / subsPerBeat);

            var events = new List<(long Tick, MidiEvent Event)>();
            int normalVel = Math.Clamp(120 - (NormalVolume * 3), 36, 127);
            int accent1Vel = Math.Clamp(127 - (Accent1Volume * 2), 64, 127);

            for (int loop = 0; loop < loops; loop++)
            {
                long loopOffset = loop * (beats * tpqn);
                for (int beat = 0; beat < beats; beat++)
                {
                    long beatTick = loopOffset + (beat * tpqn);
                    foreach (var row in Tracks)
                    {
                        if (row.StepBytes == null || beat >= row.StepBytes.Length) continue;
                        byte stepBits = row.StepBytes[beat];
                        if (stepBits == 0) continue;

                        int vel = row.Accent1 ? accent1Vel : normalVel;
                        if (row.Accent2) vel = Math.Clamp(vel + 6, 36, 127);

                        for (int sub = 0; sub < subsPerBeat; sub++)
                        {
                            if ((stepBits & (1 << (7 - sub))) == 0) continue;

                            long startTick = beatTick + (sub * subTicks);
                            byte noteNum = Math.Clamp(row.MidiNote, (byte)38, (byte)79);
                            events.Add((startTick, new NoteOnEvent((SevenBitNumber)noteNum, (SevenBitNumber)vel) { Channel = (FourBitNumber)15 }));
                            events.Add((startTick + Math.Max(20, subTicks / 2), new NoteOffEvent((SevenBitNumber)noteNum, (SevenBitNumber)0) { Channel = (FourBitNumber)15 }));
                        }
                    }
                }
            }

            var metaTrack = new TrackChunk(
                new TextEvent(Remapper.Pss680RecordingMarkerText),
                new SequenceTrackNameEvent($"PSS-680 Custom Drummer ({beats} Beats)"),
                new SetTempoEvent(60_000_000 / clampedBpm)
            );

            var drumTrack = new TrackChunk(new SequenceTrackNameEvent("PSS-680 Custom Drummer (Ch 16)"));
            using (var mgr = drumTrack.ManageTimedEvents())
            {
                foreach (var (tick, evt) in events.OrderBy(x => x.Tick))
                {
                    mgr.Objects.Add(new TimedEvent(evt, tick));
                }
            }

            return new MidiFile(metaTrack, drumTrack)
            {
                TimeDivision = new TicksPerQuarterNoteTimeDivision(tpqn)
            };
        }
    }

    public static class Pss680SysExTranslators
    {
        private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        public static List<Pss680MelodyBankSummary> AnalyzeMelodyMemory(byte[] decodedBytes)
        {
            if (decodedBytes == null || decodedBytes.Length != Pss680Constants.Format01DecodedByteCount)
                throw new ArgumentException($"Format 01H expects {Pss680Constants.Format01DecodedByteCount} decoded bytes.");

            var list = new List<Pss680MelodyBankSummary>(5);
            for (int bank = 0; bank < 5; bank++)
            {
                int start = 1 + (bank * 1000);
                int notes = 0;
                for (int i = 0; i < 1000; i++)
                {
                    byte cmd = decodedBytes[start + i];
                    if (cmd == 0xFF) break;
                    if (cmd == 0xFE)
                    {
                        i++;
                        continue;
                    }
                    if ((cmd & 0xC0) == 0x40) notes++;
                }

                list.Add(new Pss680MelodyBankSummary
                {
                    BankNumber = bank + 1,
                    GroupingGroup = decodedBytes[5001 + bank] & 0x1F,
                    PitchBendRange = Math.Clamp((int)decodedBytes[5006 + bank], 1, 12),
                    Volume = Math.Clamp((int)decodedBytes[5011 + bank], 1, 9),
                    TransmitChannel = decodedBytes[5016 + bank] & 0x0F,
                    PortamentoSpeed = Math.Clamp((int)decodedBytes[5021 + bank], 1, 5),
                    NoteCount = notes
                });
            }

            return list;
        }

        public static MidiFile DecodeMelodyMemoryToMidi(byte[] decodedBytes, int bpm = 120)
        {
            if (decodedBytes == null || decodedBytes.Length != Pss680Constants.Format01DecodedByteCount)
                throw new ArgumentException($"Format 01H expects {Pss680Constants.Format01DecodedByteCount} decoded bytes.");

            short tpqn = 480; // 24 step ticks per beat => 20 MIDI ticks per step tick
            long ticksPerStepUnit = tpqn / 24;
            int clampedBpm = Math.Clamp(bpm, 40, 240);

            var midi = new MidiFile
            {
                TimeDivision = new TicksPerQuarterNoteTimeDivision(tpqn)
            };

            var metaTrack = new TrackChunk(
                new TextEvent(Remapper.Pss680RecordingMarkerText),
                new SequenceTrackNameEvent("PSS-680 Format 01H Melody Memory"),
                new SetTempoEvent(60_000_000 / clampedBpm)
            );
            midi.Chunks.Add(metaTrack);

            for (int bank = 0; bank < 5; bank++)
            {
                int start = 1 + (bank * 1000);
                int mvol = Math.Clamp((int)decodedBytes[5011 + bank], 1, 9);
                int ch = Math.Clamp(decodedBytes[5016 + bank] & 0x0F, 0, 14);
                byte noteVel = (byte)Math.Clamp(30 + (mvol * 10), 40, 120);

                var trackEvents = new List<(long Tick, MidiEvent Event)>();
                long currentTick = 0;
                var openNotes = new Dictionary<byte, long>();

                for (int i = 0; i < 1000; i++)
                {
                    byte b = decodedBytes[start + i];
                    if (b == 0xFF) break;

                    if (b == 0xFE)
                    {
                        if (i + 1 < 1000)
                        {
                            byte prog = (byte)Math.Clamp(decodedBytes[start + (++i)] & 0x7F, 0, 104);
                            trackEvents.Add((currentTick, new ProgramChangeEvent((SevenBitNumber)prog) { Channel = (FourBitNumber)ch }));
                        }
                        continue;
                    }

                    if ((b & 0xC0) == 0x80) // Duration step
                    {
                        int stepUnits = (b & 0x3F) + 1;
                        currentTick += stepUnits * ticksPerStepUnit;
                    }
                    else if ((b & 0xC0) == 0x40) // Key On (C1=36 .. C6=96)
                    {
                        byte noteNum = (byte)Math.Clamp(36 + (b & 0x3F), 24, 108);
                        if (!openNotes.ContainsKey(noteNum))
                        {
                            openNotes[noteNum] = currentTick;
                            trackEvents.Add((currentTick, new NoteOnEvent((SevenBitNumber)noteNum, (SevenBitNumber)noteVel) { Channel = (FourBitNumber)ch }));
                        }
                    }
                    else if ((b & 0xC0) == 0x00) // Key Off
                    {
                        byte noteNum = (byte)Math.Clamp(36 + (b & 0x3F), 24, 108);
                        if (openNotes.Remove(noteNum))
                        {
                            trackEvents.Add((currentTick, new NoteOffEvent((SevenBitNumber)noteNum, (SevenBitNumber)0) { Channel = (FourBitNumber)ch }));
                        }
                    }
                    else if ((b & 0xE0) == 0xC0) // 5-bit Pitch Bend
                    {
                        int raw5 = b & 0x1F;
                        int signedOffset = raw5 >= 16 ? raw5 - 32 : raw5;
                        int bend14 = Math.Clamp(8192 + (signedOffset * 512), 0, 16383);
                        trackEvents.Add((currentTick, new PitchBendEvent((ushort)bend14) { Channel = (FourBitNumber)ch }));
                    }
                    else if ((b & 0xF0) == 0xE0) // Effect flags 1110PRSV
                    {
                        trackEvents.Add((currentTick, new ControlChangeEvent((SevenBitNumber)65, (SevenBitNumber)((b & 0x08) != 0 ? 127 : 0)) { Channel = (FourBitNumber)ch }));
                        trackEvents.Add((currentTick, new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)((b & 0x04) != 0 ? 64 : 0)) { Channel = (FourBitNumber)ch }));
                        trackEvents.Add((currentTick, new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)((b & 0x02) != 0 ? 127 : 0)) { Channel = (FourBitNumber)ch }));
                        trackEvents.Add((currentTick, new ControlChangeEvent((SevenBitNumber)1, (SevenBitNumber)((b & 0x01) != 0 ? 64 : 0)) { Channel = (FourBitNumber)ch }));
                    }
                }

                // Close any hanging notes cleanly at end of bank stream
                foreach (var openNote in openNotes.Keys.ToList())
                {
                    trackEvents.Add((Math.Max(currentTick, openNotes[openNote] + 120),
                        new NoteOffEvent((SevenBitNumber)openNote, (SevenBitNumber)0) { Channel = (FourBitNumber)ch }));
                }

                if (trackEvents.Count > 0)
                {
                    var bankTrack = new TrackChunk(new SequenceTrackNameEvent($"PSS-680 Melody Bank {bank + 1}"));
                    using (var mgr = bankTrack.ManageTimedEvents())
                    {
                        foreach (var (tick, evt) in trackEvents.OrderBy(e => e.Tick))
                        {
                            mgr.Objects.Add(new TimedEvent(evt, tick));
                        }
                    }
                    midi.Chunks.Add(bankTrack);
                }
            }

            return midi;
        }

        public static Pss680ChordMemorySummary AnalyzeChordMemory(byte[] decodedBytes)
        {
            if (decodedBytes == null || decodedBytes.Length != Pss680Constants.Format02DecodedByteCount)
                throw new ArgumentException($"Format 02H expects {Pss680Constants.Format02DecodedByteCount} decoded bytes.");

            int styleNum = decodedBytes[1] & 0x7F;
            string styleName = Pss680Constants.Styles.TryGetValue(styleNum, out var styleInfo) ? styleInfo.Name : $"Style #{styleNum:D2}";
            byte orch = decodedBytes[3];
            byte rawTempo = decodedBytes[201];

            var summary = new Pss680ChordMemorySummary
            {
                BankNumber = (decodedBytes[0] & 0x07) + 1,
                InitialStyleNumber = styleNum,
                InitialStyleName = styleName,
                Tempo = rawTempo is >= 10 and <= 240 ? rawTempo : 120,
                DrummerEnabled = (orch & 0x10) != 0,
                ChordEnabled = (orch & 0x08) != 0,
                BassEnabled = (orch & 0x04) != 0,
                OrchestraEnabled = (orch & 0x02) != 0,
                RhythmEnabled = (orch & 0x01) != 0
            };

            for (int pos = 6; pos + 1 <= 200; pos += 2)
            {
                byte hdr = decodedBytes[pos];
                byte data = decodedBytes[pos + 1];
                if (hdr == 0xFF || data == 0xFF || (hdr == 0x00 && data == 0x00)) break;

                if ((hdr & 0x80) == 0)
                {
                    int measure = (hdr >> 3) & 0x0F;
                    int beat = hdr & 0x07;
                    int root = Math.Clamp((data >> 4) & 0x0F, 0, 11);
                    int qualityCode = data & 0x0F;
                    string quality = GetChordQualityLabel(qualityCode);
                    summary.Chords.Add(new Pss680ChordStepInfo
                    {
                        Measure = measure + 1,
                        Beat = beat + 1,
                        RootSemitone = root,
                        RootName = NoteNames[root],
                        QualityCode = qualityCode,
                        Quality = quality,
                        ChordName = $"{NoteNames[root]}{quality}"
                    });
                }
            }

            return summary;
        }

        public static MidiFile DecodeChordMemoryToMidi(byte[] decodedBytes)
        {
            var summary = AnalyzeChordMemory(decodedBytes);
            short tpqn = 480;
            int clampedBpm = Math.Clamp(summary.Tempo, 40, 240);

            var midi = new MidiFile
            {
                TimeDivision = new TicksPerQuarterNoteTimeDivision(tpqn)
            };

            var meta = new TrackChunk(
                new TextEvent(Remapper.Pss680RecordingMarkerText),
                new SequenceTrackNameEvent($"PSS-680 Chord Bank {summary.BankNumber} ({summary.InitialStyleName})"),
                new SetTempoEvent(60_000_000 / clampedBpm)
            );
            midi.Chunks.Add(meta);

            var chordTrack = new TrackChunk(
                new SequenceTrackNameEvent("PSS-680 Chord Progression"),
                new ProgramChangeEvent((SevenBitNumber)3) { Channel = (FourBitNumber)10 } // Piano 1 on Ch 11
            );
            var bassTrack = new TrackChunk(
                new SequenceTrackNameEvent("PSS-680 Chord Bass"),
                new ProgramChangeEvent((SevenBitNumber)14) { Channel = (FourBitNumber)14 } // Wood Bass 1 on Ch 15
            );

            var timedChords = new List<(long Tick, MidiEvent Event)>();
            var timedBass = new List<(long Tick, MidiEvent Event)>();

            for (int i = 0; i < summary.Chords.Count; i++)
            {
                var c = summary.Chords[i];
                long tick = (((c.Measure - 1) * 4L) + (c.Beat - 1)) * tpqn;
                long dur = tpqn * 4L;
                if (i + 1 < summary.Chords.Count)
                {
                    var next = summary.Chords[i + 1];
                    long nextTick = (((next.Measure - 1) * 4L) + (next.Beat - 1)) * tpqn;
                    if (nextTick > tick) dur = nextTick - tick;
                }
                long offTick = tick + Math.Max(120, dur - 20);

                byte bassNote = (byte)(36 + c.RootSemitone);
                timedBass.Add((tick, new NoteOnEvent((SevenBitNumber)bassNote, (SevenBitNumber)100) { Channel = (FourBitNumber)14 }));
                timedBass.Add((offTick, new NoteOffEvent((SevenBitNumber)bassNote, (SevenBitNumber)0) { Channel = (FourBitNumber)14 }));

                foreach (int interval in GetChordIntervals(c.QualityCode))
                {
                    byte n = (byte)(60 + c.RootSemitone + interval);
                    timedChords.Add((tick, new NoteOnEvent((SevenBitNumber)n, (SevenBitNumber)88) { Channel = (FourBitNumber)10 }));
                    timedChords.Add((offTick, new NoteOffEvent((SevenBitNumber)n, (SevenBitNumber)0) { Channel = (FourBitNumber)10 }));
                }
            }

            using (var mgr = chordTrack.ManageTimedEvents())
            {
                foreach (var (t, e) in timedChords.OrderBy(x => x.Tick)) mgr.Objects.Add(new TimedEvent(e, t));
            }
            using (var mgr = bassTrack.ManageTimedEvents())
            {
                foreach (var (t, e) in timedBass.OrderBy(x => x.Tick)) mgr.Objects.Add(new TimedEvent(e, t));
            }

            midi.Chunks.Add(chordTrack);
            midi.Chunks.Add(bassTrack);
            return midi;
        }

        private static string GetChordQualityLabel(int code) => code switch
        {
            0 => "",
            1 => "m",
            2 => "7",
            3 => "m7",
            4 => "Maj7",
            5 => "mMaj7",
            6 => "m7b5",
            7 => "7sus4",
            8 => "aug",
            9 => "dim",
            10 => "6",
            11 => "m6",
            12 => "7b5",
            _ => ""
        };

        private static int[] GetChordIntervals(int code) => code switch
        {
            0 => new[] { 0, 4, 7 },
            1 => new[] { 0, 3, 7 },
            2 => new[] { 0, 4, 7, 10 },
            3 => new[] { 0, 3, 7, 10 },
            4 => new[] { 0, 4, 7, 11 },
            5 => new[] { 0, 3, 7, 11 },
            6 => new[] { 0, 3, 6, 10 },
            7 => new[] { 0, 5, 7, 10 },
            8 => new[] { 0, 4, 8 },
            9 => new[] { 0, 3, 6 },
            10 => new[] { 0, 4, 7, 9 },
            11 => new[] { 0, 3, 7, 9 },
            12 => new[] { 0, 4, 6, 10 },
            _ => new[] { 0, 4, 7 }
        };
    }
}
