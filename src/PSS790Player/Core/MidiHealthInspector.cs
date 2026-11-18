using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PSS790Player.Core
{
    public class MidiHealthReport
    {
        public string FileName { get; set; } = string.Empty;
        public int PeakPolyphony { get; set; }
        public string VoiceStressRating { get; set; } = "Optimal"; // "Optimal", "Moderate", "HighPolyphony"
        public int ActiveChannelCount { get; set; }
        public int TotalNoteCount { get; set; }
        public List<int> ExplicitProgramChannels { get; set; } = new();
        public List<string> InferredProgramChannels { get; set; } = new();
        public List<string> XgSysExChannels { get; set; } = new();
        public List<int> UnassignedChannels { get; set; } = new();
        public int StuckPedalIntervalsCount { get; set; }
        public bool RunawayPedalCapped { get; set; }
        public int DanglingPitchBendsCount { get; set; }
        public bool PitchBendsCenteredAtEof { get; set; }
        public int AcousticMicroBendsNeutralized { get; set; }
        public int OutOfBoundsNotesCount { get; set; }
        public List<int> NonStandardDrumChannels { get; set; } = new();
        public List<string> AppliedAutoFixes { get; set; } = new();

        public string BadgeType { get; set; } = "optimal"; // "optimal", "autofixed", "warning"
        public string BadgeLabel { get; set; } = "Optimal";
        public string BadgeTooltip { get; set; } = string.Empty;
    }

    public static class MidiHealthInspector
    {
        public static MidiHealthReport Analyze(MidiFile originalMidi, MidiFile? remappedMidi = null, string? fileName = null, string model = "pss790")
        {
            var report = new MidiHealthReport
            {
                FileName = fileName ?? "MIDI Track"
            };

            if (originalMidi == null) return report;

            bool is680 = string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase);
            int hardwareLimit = is680 ? 12 : 28;
            string modelName = is680 ? "Yamaha PSS-680" : "Yamaha PSS-790";

            var ticksPerQuarter = (originalMidi.TimeDivision as TicksPerQuarterNoteTimeDivision)?.TicksPerQuarterNote ?? (short)480;
            long fourMeasuresTicks = ticksPerQuarter * 16;

            var allNotes = originalMidi.GetNotes().ToList();
            report.TotalNoteCount = allNotes.Count;

            // 1. Exact Peak Polyphony via Sweep-Line
            report.PeakPolyphony = CalculatePeakPolyphony(allNotes);
            if (is680)
            {
                if (report.PeakPolyphony <= 9) report.VoiceStressRating = "Optimal";
                else if (report.PeakPolyphony <= 12) report.VoiceStressRating = "Moderate";
                else report.VoiceStressRating = "HighPolyphony";
            }
            else
            {
                if (report.PeakPolyphony <= 24) report.VoiceStressRating = "Optimal";
                else if (report.PeakPolyphony <= 28) report.VoiceStressRating = "Moderate";
                else report.VoiceStressRating = "HighPolyphony";
            }

            // 2. Active Channels and Out-of-Bounds Notes
            var activeChannels = new HashSet<int>();
            int outOfBoundsCount = 0;
            foreach (var note in allNotes)
            {
                activeChannels.Add(note.Channel);
                byte noteNum = (byte)note.NoteNumber;
                if (noteNum < 24 || noteNum > 96) // C1 to C7
                {
                    outOfBoundsCount++;
                }
            }
            report.ActiveChannelCount = activeChannels.Count;
            report.OutOfBoundsNotesCount = outOfBoundsCount;

            // 3. Scan for Program Changes, XG SysEx, and Inferred Instruments
            var channelPrograms = new Dictionary<int, byte>();
            var tracksWithNotes = new Dictionary<int, TrackChunk>();

            foreach (var track in originalMidi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ProgramChangeEvent pc && !channelPrograms.ContainsKey(pc.Channel))
                    {
                        channelPrograms[pc.Channel] = (byte)pc.ProgramNumber;
                        if (!report.ExplicitProgramChannels.Contains(pc.Channel))
                        {
                            report.ExplicitProgramChannels.Add(pc.Channel);
                        }
                    }
                    else if (evt is NoteOnEvent n && n.Velocity > 0 && n.Channel != (FourBitNumber)9)
                    {
                        tracksWithNotes.TryAdd((int)n.Channel, track);
                    }
                    else if (evt is NormalSysExEvent sysex && sysex.Data.Length > 6)
                    {
                        var d = sysex.Data;
                        // Yamaha XG SysEx
                        if (d[0] == 0x43 && d[2] == 0x4C)
                        {
                            // Bulk Dump: 43 00 4C [sizeH] [sizeL] [addrH] [addrM] [addrL]
                            if (d[1] == 0x00 && d[5] == 0x08 && d[7] == 0x00 && d.Length > 10)
                            {
                                int part = d[6];
                                byte prog = d[10];
                                if (part >= 0 && part < 16 && !channelPrograms.ContainsKey(part))
                                {
                                    channelPrograms[part] = prog;
                                    var voiceName = Remapper.GetGmInstrumentName(prog);
                                    report.XgSysExChannels.Add($"Ch {part + 1}: {voiceName} [XG SysEx]");
                                }
                            }
                            // Parameter Change: 43 10 4C [addrH] [addrM] [addrL] [data...]
                            else if (d[1] == 0x10 && d[3] == 0x08 && d[5] == 0x03 && d.Length > 6)
                            {
                                int part = d[4];
                                byte prog = d[6];
                                if (part >= 0 && part < 16 && !channelPrograms.ContainsKey(part))
                                {
                                    channelPrograms[part] = prog;
                                    var voiceName = Remapper.GetGmInstrumentName(prog);
                                    report.XgSysExChannels.Add($"Ch {part + 1}: {voiceName} [XG SysEx]");
                                }
                            }
                            // Part Mode Drum
                            else if (d[1] == 0x10 && d[3] == 0x08 && d[5] == 0x07 && d.Length > 6)
                            {
                                int part = d[4];
                                if (d[6] > 0 && !report.NonStandardDrumChannels.Contains(part))
                                {
                                    report.NonStandardDrumChannels.Add(part);
                                }
                            }
                        }
                    }
                }
            }

            // Check for Inferred Instruments from Track Names
            foreach (var (ch, tr) in tracksWithNotes)
            {
                if (!channelPrograms.ContainsKey(ch))
                {
                    byte? inferred = null;
                    foreach (var textEvt in tr.Events.OfType<BaseTextEvent>())
                    {
                        inferred = Remapper.InferGmProgramFromText(textEvt.Text);
                        if (inferred.HasValue)
                        {
                            var voiceName = Remapper.GetGmInstrumentName(inferred.Value);
                            report.InferredProgramChannels.Add($"Ch {ch + 1}: {voiceName} [Track Name '{textEvt.Text}']");
                            break;
                        }
                    }

                    if (!inferred.HasValue)
                    {
                        report.UnassignedChannels.Add(ch);
                    }
                }
            }

            // 4. Scan Controllers & Pedals (CC 64 Sustain, Bank 127 Drums)
            int stuckPedalCount = 0;
            var timedEvents = originalMidi.GetTimedEvents().ToList();

            for (int ch = 0; ch < 16; ch++)
            {
                long? pedalDownTick = null;
                var chEvents = timedEvents.Where(te => te.Event is ChannelEvent ce && ce.Channel == ch).ToList();

                foreach (var te in chEvents)
                {
                    if (te.Event is ControlChangeEvent cc)
                    {
                        if (cc.ControlNumber == 64) // Sustain
                        {
                            if (cc.ControlValue >= 64)
                            {
                                if (!pedalDownTick.HasValue) pedalDownTick = te.Time;
                            }
                            else
                            {
                                if (pedalDownTick.HasValue)
                                {
                                    long dur = te.Time - pedalDownTick.Value;
                                    if (dur > fourMeasuresTicks) stuckPedalCount++;
                                    pedalDownTick = null;
                                }
                            }
                        }
                        else if (cc.ControlNumber == 0 && (cc.ControlValue == 127 || cc.ControlValue == 128) && ch != 9)
                        {
                            if (!report.NonStandardDrumChannels.Contains(ch))
                            {
                                report.NonStandardDrumChannels.Add(ch);
                            }
                        }
                    }
                }

                if (pedalDownTick.HasValue) // Never released before track end
                {
                    stuckPedalCount++;
                }
            }
            report.StuckPedalIntervalsCount = stuckPedalCount;

            // 5. Scan Pitch Bends (Dangling at Track End & Acoustic Micro-Bends)
            int danglingBends = 0;
            int microBendsNeutralized = 0;

            foreach (var track in originalMidi.GetTrackChunks())
            {
                var bendsPerChannel = new Dictionary<int, PitchBendEvent>();
                foreach (var evt in track.Events)
                {
                    if (evt is PitchBendEvent pb)
                    {
                        bendsPerChannel[pb.Channel] = pb;
                        int ch = pb.Channel;
                        byte gmProg = channelPrograms.TryGetValue(ch, out var p) ? p : (byte)0;
                        if (Remapper.IsAcousticOrKeyboardInstrument(gmProg))
                        {
                            if (pb.PitchValue is >= 8000 and <= 8384 && pb.PitchValue != 8192)
                            {
                                microBendsNeutralized++;
                            }
                        }
                    }
                }

                foreach (var pb in bendsPerChannel.Values)
                {
                    if (pb.PitchValue != 8192)
                    {
                        danglingBends++;
                    }
                }
            }
            report.DanglingPitchBendsCount = danglingBends;
            report.AcousticMicroBendsNeutralized = microBendsNeutralized;

            // 6. Record Auto-Fixes Applied
            report.RunawayPedalCapped = stuckPedalCount > 0;
            report.PitchBendsCenteredAtEof = danglingBends > 0;

            if (report.InferredProgramChannels.Count > 0)
            {
                report.AppliedAutoFixes.Add($"Auto-inferred {report.InferredProgramChannels.Count} missing instruments from track names");
            }
            if (report.XgSysExChannels.Count > 0)
            {
                report.AppliedAutoFixes.Add($"Extracted Yamaha XG Multi-Part SysEx voicing for {report.XgSysExChannels.Count} channels");
            }
            if (report.StuckPedalIntervalsCount > 0)
            {
                report.AppliedAutoFixes.Add($"Capped {report.StuckPedalIntervalsCount} runaway sustain pedal intervals to prevent voice bloat");
            }
            if (report.DanglingPitchBendsCount > 0)
            {
                report.AppliedAutoFixes.Add($"Auto-centered {report.DanglingPitchBendsCount} dangling pitch bends before track end");
            }
            if (report.AcousticMicroBendsNeutralized > 0)
            {
                report.AppliedAutoFixes.Add($"Neutralized {report.AcousticMicroBendsNeutralized} micro-bends on acoustic/keyboard channels");
            }
            if (report.NonStandardDrumChannels.Count > 0)
            {
                report.AppliedAutoFixes.Add($"Remapped GS/XG secondary drum parts (Ch {string.Join(", ", report.NonStandardDrumChannels.Select(c => c + 1))}) to Channel 16");
            }
            if (report.OutOfBoundsNotesCount > 0)
            {
                report.AppliedAutoFixes.Add($"Clamped {report.OutOfBoundsNotesCount} out-of-range notes to {modelName} keyboard bounds");
            }

            // 7. Compose Badge
            if (report.PeakPolyphony > hardwareLimit)
            {
                report.BadgeType = "warning";
                report.BadgeLabel = $"{report.PeakPolyphony} Voices (Guarded)";
                report.BadgeTooltip = $"Peak polyphony of {report.PeakPolyphony} voices exceeds the {modelName} {hardwareLimit}-voice hardware limit. Polyphony Guard active.";
            }
            else if (report.AppliedAutoFixes.Count > 0)
            {
                report.BadgeType = "autofixed";
                report.BadgeLabel = report.AppliedAutoFixes.Count == 1 ? "1 Auto-Fix Applied" : $"{report.AppliedAutoFixes.Count} Auto-Fixes Applied";
                report.BadgeTooltip = string.Join(" • ", report.AppliedAutoFixes);
            }
            else
            {
                report.BadgeType = "optimal";
                report.BadgeLabel = $"Optimal ({report.PeakPolyphony} Voices)";
                report.BadgeTooltip = $"Fully compliant General MIDI. Peak polyphony {report.PeakPolyphony}/{hardwareLimit} voices within safe {modelName} limits.";
            }

            return report;
        }

        private static int CalculatePeakPolyphony(IReadOnlyList<Note> notes)
        {
            if (notes.Count == 0) return 0;

            var events = new List<(long Time, int Type)>(notes.Count * 2);
            foreach (var note in notes)
            {
                events.Add((note.Time, 1));
                events.Add((note.EndTime, -1));
            }

            events.Sort((a, b) =>
            {
                int cmp = a.Time.CompareTo(b.Time);
                if (cmp != 0) return cmp;
                return a.Type.CompareTo(b.Type); // -1 ends before +1 starts
            });

            int current = 0;
            int peak = 0;
            foreach (var evt in events)
            {
                current += evt.Type;
                if (current > peak) peak = current;
            }

            return peak;
        }
    }
}
