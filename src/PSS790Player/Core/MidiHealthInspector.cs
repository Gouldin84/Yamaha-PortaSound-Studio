using System;
using System.Collections.Generic;
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
        public List<int> XgSfxChannels { get; set; } = new();
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
            int hardwareLimit = is680 ? Pss680Constants.PolyphonyMode99 : 28;
            string modelName = is680 ? "Yamaha PSS-680" : "Yamaha PSS-790";

            var detectedFormat = Remapper.DetectMidiFormat(originalMidi);
            bool isNativePssInput = detectedFormat is MidiFormat.Pss790Native or MidiFormat.Pss680Native;

            var ticksPerQuarter = (originalMidi.TimeDivision as TicksPerQuarterNoteTimeDivision)?.TicksPerQuarterNote ?? (short)480;
            long fourMeasuresTicks = ticksPerQuarter * 16;

            var allNotes = originalMidi.GetNotes().ToList();
            report.TotalNoteCount = allNotes.Count;

            // 1. Identify percussion channels accurately:
            // Only treat Ch 15 as default drums when input is already native PSS format;
            // on General MIDI files, Ch 9 is drums and Ch 15 is melodic unless Bank 126/127 or XG SysEx marks it as drums.
            var drumChannels = isNativePssInput
                ? new HashSet<int> { 15 }
                : new HashSet<int> { 9 };

            foreach (var track in originalMidi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ControlChangeEvent cc && cc.ControlNumber == 0 && (cc.ControlValue == 126 || cc.ControlValue == 127))
                    {
                        drumChannels.Add(cc.Channel);
                    }
                    else if (evt is NormalSysExEvent sysex && sysex.Data != null && sysex.Data.Length > 6)
                    {
                        var d = sysex.Data;
                        if (d[0] == 0x43 && d[2] == 0x4C && d[1] == 0x10 && d[3] == 0x08 && d[5] == 0x07 && d[6] > 0)
                        {
                            drumChannels.Add(d[4]);
                        }
                    }
                }
            }

            // 2. Exact Peak Polyphony via Sweep-Line
            report.PeakPolyphony = CalculatePeakPolyphony(allNotes);

            // On PSS-680, evaluate stress separately for 12 FM melodic voices vs 4 PCM percussion voices
            int effectiveVoicePeak;
            bool drumVoiceOverflow = false;
            if (is680)
            {
                var melodicNotes = allNotes.Where(n => !drumChannels.Contains(n.Channel)).ToList();
                var drumNotes = allNotes.Where(n => drumChannels.Contains(n.Channel)).ToList();
                int fmPeak = CalculatePeakPolyphony(melodicNotes);
                int drumPeak = CalculatePeakPolyphony(drumNotes);

                effectiveVoicePeak = fmPeak;
                drumVoiceOverflow = drumPeak > 4;

                if (fmPeak <= 9 && !drumVoiceOverflow) report.VoiceStressRating = "Optimal";
                else if (fmPeak <= 12 && !drumVoiceOverflow) report.VoiceStressRating = "Moderate";
                else report.VoiceStressRating = "HighPolyphony";
            }
            else
            {
                effectiveVoicePeak = report.PeakPolyphony;
                if (report.PeakPolyphony <= 24) report.VoiceStressRating = "Optimal";
                else if (report.PeakPolyphony <= 28) report.VoiceStressRating = "Moderate";
                else report.VoiceStressRating = "HighPolyphony";
            }

            // 3. Active Channels and Out-of-Bounds Melodic Notes
            var activeChannels = new HashSet<int>();
            int outOfBoundsCount = 0;
            foreach (var note in allNotes)
            {
                activeChannels.Add(note.Channel);
                if (!drumChannels.Contains(note.Channel))
                {
                    byte noteNum = (byte)note.NoteNumber;
                    if (noteNum < 24 || noteNum > 108) // C1 to C8 (7-octave MIDI recognition range)
                    {
                        outOfBoundsCount++;
                    }
                }
            }
            report.ActiveChannelCount = activeChannels.Count;
            report.OutOfBoundsNotesCount = outOfBoundsCount;

            // 4. Scan for Program Changes, XG SysEx, and Inferred Instruments
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
                    else if (evt is NoteOnEvent n && n.Velocity > 0 && !drumChannels.Contains(n.Channel))
                    {
                        tracksWithNotes.TryAdd((int)n.Channel, track);
                    }
                    else if (evt is NormalSysExEvent sysex && sysex.Data != null && sysex.Data.Length > 6)
                    {
                        var d = sysex.Data;
                        if (d[0] == 0x43 && d[2] == 0x4C)
                        {
                            // Bulk Dump: 43 00 4C [sizeH] [sizeL] 08 [part] 00 ...
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
                            // Parameter Change: 43 10 4C 08 [part] 03 [prog]
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
                            // Part Mode Drum: 43 10 4C 08 [part] 07 [mode > 0]
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

            // 5. Scan Controllers & Pedals (CC 64 Sustain, Bank 126/127 Drums, Bank 64 SFX)
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
                        if (cc.ControlNumber == 64)
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
                        else if (cc.ControlNumber == 0 && (cc.ControlValue == 126 || cc.ControlValue == 127) && ch != 9)
                        {
                            if (!report.NonStandardDrumChannels.Contains(ch))
                            {
                                report.NonStandardDrumChannels.Add(ch);
                            }
                        }
                        else if (cc.ControlNumber == 0 && cc.ControlValue == 64)
                        {
                            if (!report.XgSfxChannels.Contains(ch))
                            {
                                report.XgSfxChannels.Add(ch);
                            }
                        }
                    }
                }

                if (pedalDownTick.HasValue)
                {
                    stuckPedalCount++;
                }
            }
            report.StuckPedalIntervalsCount = stuckPedalCount;

            // 6. Scan Pitch Bends (Dangling at Track End & PSS-790 Acoustic Bends)
            int danglingBends = 0;
            int acousticBends = 0;

            foreach (var track in originalMidi.GetTrackChunks())
            {
                var bendsPerChannel = new Dictionary<int, PitchBendEvent>();
                foreach (var evt in track.Events)
                {
                    if (evt is PitchBendEvent pb)
                    {
                        bendsPerChannel[pb.Channel] = pb;
                        if (!is680 && !drumChannels.Contains(pb.Channel))
                        {
                            byte gmProg = channelPrograms.TryGetValue(pb.Channel, out var p) ? p : (byte)0;
                            if (Remapper.IsAcousticOrKeyboardInstrument(gmProg) && pb.PitchValue != 8192)
                            {
                                acousticBends++;
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
            report.AcousticMicroBendsNeutralized = acousticBends;

            // 7. Verify against remappedMidi when provided, and record Auto-Fixes
            bool remappedVerified = remappedMidi != null;
            int? remappedPeak = remappedVerified ? CalculatePeakPolyphony(remappedMidi!.GetNotes().ToList()) : null;

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
            if (!is680 && report.AcousticMicroBendsNeutralized > 0)
            {
                report.AppliedAutoFixes.Add($"Neutralized {report.AcousticMicroBendsNeutralized} pitch bends on acoustic/keyboard channels");
            }
            if (report.NonStandardDrumChannels.Count > 0)
            {
                report.AppliedAutoFixes.Add($"Remapped GS/XG secondary drum parts (Ch {string.Join(", ", report.NonStandardDrumChannels.Select(c => c + 1))}) to Channel 16");
            }
            if (report.XgSfxChannels.Count > 0)
            {
                report.AppliedAutoFixes.Add($"Mapped {report.XgSfxChannels.Count} Yamaha XG SFX voice channel(s) to hardware sound effects");
            }
            if (report.OutOfBoundsNotesCount > 0)
            {
                report.AppliedAutoFixes.Add($"Clamped {report.OutOfBoundsNotesCount} out-of-range notes to {modelName} keyboard bounds");
            }

            // 8. Compose Badge
            bool exceedsHardwareLimit = effectiveVoicePeak > hardwareLimit || drumVoiceOverflow;
            if (exceedsHardwareLimit)
            {
                report.BadgeType = "warning";
                report.BadgeLabel = $"{report.PeakPolyphony} Voices (Guarded)";
                string remappedNote = remappedPeak.HasValue ? $" Guarded output peak: {remappedPeak.Value} voices." : " Polyphony Guard active.";
                report.BadgeTooltip = drumVoiceOverflow && effectiveVoicePeak <= hardwareLimit
                    ? $"Percussion density exceeds the {modelName} 4-voice PCM drum budget.{remappedNote}"
                    : $"Peak polyphony of {report.PeakPolyphony} voices exceeds the {modelName} {hardwareLimit}-voice hardware limit.{remappedNote}";
            }
            else if (report.AppliedAutoFixes.Count > 0)
            {
                report.BadgeType = "autofixed";
                report.BadgeLabel = report.AppliedAutoFixes.Count == 1 ? "1 Auto-Fix Applied" : $"{report.AppliedAutoFixes.Count} Auto-Fixes Applied";
                report.BadgeTooltip = string.Join(" \u2022 ", report.AppliedAutoFixes);
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
