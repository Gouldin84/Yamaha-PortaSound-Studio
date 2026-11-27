using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using PSS790Player.Models;

namespace PSS790Player.Services
{
    /// <summary>
    /// Pure stateless analyzer producing 16 UI-ready <see cref="ChannelStateDto"/> rows
    /// for GM, PSS-790 Native, PSS-680 Native, cross-model, and XG SysEx MIDI files.
    /// </summary>
    public static class ChannelStateExtractor
    {
        public static List<ChannelStateDto> Analyze(
            MidiFile originalMidi,
            MidiFile? remappedMidi,
            MidiFormat format,
            string activeModel,
            bool isGmPassthrough,
            IReadOnlyDictionary<int, int?> voiceOverrides,
            IReadOnlyDictionary<int, int> octaveShifts,
            Func<int, (bool IsMuted, bool IsSolo, byte Volume)>? getMixerState = null)
        {
            var list = new List<ChannelStateDto>(16);
            if (originalMidi == null) return list;

            bool is680 = string.Equals(activeModel, "pss680", StringComparison.OrdinalIgnoreCase);
            bool hasNativeDrums = format is MidiFormat.Pss790Native or MidiFormat.Pss680Native;
            bool useRemappedRouting = !isGmPassthrough && (hasNativeDrums || format == MidiFormat.GeneralMidi);

            var drumChannels = hasNativeDrums
                ? new HashSet<int> { 15 }
                : new HashSet<int> { 9 };

            var channelNotes = new HashSet<int>();
            var channelNoteCounts = new int[16];
            var firstNoteTime = new long[16];
            Array.Fill(firstNoteTime, long.MaxValue);
            var channelPrograms = new Dictionary<int, byte>();
            var zeroPcPrograms = new Dictionary<int, byte>();
            var chronologicalPcs = new Dictionary<int, List<(long Time, byte Prog)>>();
            var inferredPrograms = new Dictionary<int, (byte Prog, string Source)>();
            var trackNotes = new Dictionary<int, TrackChunk>();

            foreach (var track in originalMidi.GetTrackChunks())
            {
                foreach (var te in track.GetTimedEvents())
                {
                    switch (te.Event)
                    {
                        case ProgramChangeEvent pc:
                            if (!chronologicalPcs.TryGetValue(pc.Channel, out var pcList))
                            {
                                pcList = new List<(long Time, byte Prog)>();
                                chronologicalPcs[pc.Channel] = pcList;
                            }
                            pcList.Add((te.Time, pc.ProgramNumber));
                            if (te.Time == 0 && !zeroPcPrograms.ContainsKey(pc.Channel))
                            {
                                zeroPcPrograms[pc.Channel] = pc.ProgramNumber;
                            }
                            break;
                        case NoteOnEvent note when note.Velocity > 0:
                            channelNotes.Add(note.Channel);
                            channelNoteCounts[note.Channel]++;
                            if (te.Time < firstNoteTime[note.Channel]) firstNoteTime[note.Channel] = te.Time;
                            trackNotes.TryAdd(note.Channel, track);
                            break;
                        case ControlChangeEvent cc when cc.ControlNumber == 0 && (cc.ControlValue == 126 || cc.ControlValue == 127):
                            drumChannels.Add(cc.Channel);
                            break;
                        case NormalSysExEvent sysex when sysex.Data != null && sysex.Data.Length > 6:
                            InspectXgSysEx(sysex.Data, channelPrograms, drumChannels);
                            break;
                    }
                }
            }

            // Resolve each channel's initial program chronologically from its first note setup window
            // rather than first-track-order PC (fixes late PCs > tick 0 and GM reset PC 0 at tick 0 before Bar 2 PC).
            foreach (var (ch, pcList) in chronologicalPcs)
            {
                if (pcList.Count == 0) continue;
                pcList.Sort((a, b) => a.Time.CompareTo(b.Time));
                long fnt = firstNoteTime[ch];
                if (fnt == long.MaxValue)
                {
                    channelPrograms[ch] = zeroPcPrograms.TryGetValue(ch, out var zp) ? zp : pcList[0].Prog;
                }
                else
                {
                    int idx = -1;
                    for (int i = 0; i < pcList.Count; i++)
                    {
                        if (pcList[i].Time <= fnt) idx = i;
                        else break;
                    }
                    if (idx >= 0)
                    {
                        channelPrograms[ch] = pcList[idx].Prog;
                    }
                    else if (pcList[0].Time <= fnt + 16)
                    {
                        channelPrograms[ch] = pcList[0].Prog;
                    }
                }
            }

            foreach (var (ch, tr) in trackNotes)
            {
                if (channelPrograms.ContainsKey(ch) || drumChannels.Contains(ch)) continue;
                foreach (var textEvt in tr.Events.OfType<BaseTextEvent>())
                {
                    var inferred = Remapper.InferGmProgramFromText(textEvt.Text);
                    if (inferred.HasValue)
                    {
                        inferredPrograms[ch] = (inferred.Value, $"Track '{textEvt.Text}'");
                        break;
                    }
                }
            }

            var remappedNotes = new HashSet<int>();
            var remappedPrograms = new Dictionary<int, byte>();
            int? relocatedMelodicChannel = null;
            if (!isGmPassthrough && format == MidiFormat.GeneralMidi && remappedMidi != null)
            {
                foreach (var te in remappedMidi.GetTimedEvents())
                {
                    if (te.Event is NoteOnEvent noteOn && noteOn.Velocity > 0)
                    {
                        remappedNotes.Add(noteOn.Channel);
                    }
                    else if (te.Event is ProgramChangeEvent remPc && remPc.Channel != 15 && !remappedPrograms.ContainsKey(remPc.Channel))
                    {
                        remappedPrograms[remPc.Channel] = remPc.ProgramNumber;
                    }
                }

                if (channelNotes.Contains(15) && !drumChannels.Contains(15))
                {
                    // Mirror Remapper.cs Step 6 deterministic relocation selection
                    for (int cand = 0; cand <= 14; cand++)
                    {
                        if (!drumChannels.Contains(cand) && !channelNotes.Contains(cand) && remappedNotes.Contains(cand))
                        {
                            relocatedMelodicChannel = cand;
                            break;
                        }
                    }
                    if (!relocatedMelodicChannel.HasValue)
                    {
                        foreach (int d in drumChannels.Where(d => d != 15))
                        {
                            if (remappedNotes.Contains(d)) { relocatedMelodicChannel = d; break; }
                        }
                    }
                    if (!relocatedMelodicChannel.HasValue)
                    {
                        relocatedMelodicChannel = Enumerable.Range(0, 15)
                            .Where(c => !drumChannels.Contains(c) && remappedNotes.Contains(c))
                            .OrderBy(c => c == 0 ? 1 : 0)
                            .ThenBy(c => channelNoteCounts[c])
                            .ThenByDescending(c => c)
                            .Cast<int?>()
                            .FirstOrDefault();
                    }
                }
            }

            bool isNativeForActiveModel = (format == MidiFormat.Pss680Native && is680) ||
                                          (format == MidiFormat.Pss790Native && !is680);

            bool remappedIsFullHardwareRemap = remappedMidi != null &&
                                             (!drumChannels.Contains(9) || !remappedNotes.Contains(9) || remappedNotes.Contains(15));

            for (int ch = 0; ch < 16; ch++)
            {
                bool hasNotes = (!isGmPassthrough && format == MidiFormat.GeneralMidi && remappedIsFullHardwareRemap)
                    ? remappedNotes.Contains(ch)
                    : channelNotes.Contains(ch);

                bool isRelocatedTarget = relocatedMelodicChannel.HasValue && ch == relocatedMelodicChannel.Value;
                int lookupOrigCh = isRelocatedTarget && !channelNotes.Contains(ch) ? 15 : ch;
                bool suppressOrigDrumSource = !isGmPassthrough && format == MidiFormat.GeneralMidi &&
                                              drumChannels.Contains(ch) && !isRelocatedTarget;

                byte? prog = (!suppressOrigDrumSource && channelPrograms.TryGetValue(lookupOrigCh, out var p)) ? p : null;
                if (isRelocatedTarget && !prog.HasValue && channelPrograms.TryGetValue(15, out var p15))
                {
                    prog = p15;
                }
                string? inferredTag = null;
                if (!suppressOrigDrumSource && !prog.HasValue && inferredPrograms.TryGetValue(lookupOrigCh, out var inf))
                {
                    prog = inf.Prog;
                    inferredTag = inf.Source;
                }
                if (isRelocatedTarget)
                {
                    inferredTag = inferredTag != null ? $"{inferredTag}, Ch 16" : "Ch 16";
                }

                bool isDrum = useRemappedRouting ? (ch == 15) : (ch == 9);

                // Resolve cross-model native programs through reverse GM map when targeting opposing hardware
                if (!isNativeForActiveModel && !isDrum && prog.HasValue && (format == MidiFormat.Pss790Native || format == MidiFormat.Pss680Native))
                {
                    if (format == MidiFormat.Pss790Native && Constants.Pss790MidiProgramToGmMap.TryGetValue(prog.Value, out byte revGm790))
                    {
                        prog = revGm790;
                    }
                    else if (format == MidiFormat.Pss680Native)
                    {
                        prog = prog.Value is >= 100 and <= 104
                            ? (byte)80
                            : (Pss680Constants.Pss680ToGmVoiceMap.TryGetValue(prog.Value, out var gm680) ? (byte)gm680 : (byte)0);
                    }
                }

                string role = isDrum
                    ? (isGmPassthrough ? "GM Standard Drums (Ch 10)" : (is680 ? "Hand Percussion (Ch 16)" : "Fixed Rhythm (Ch 16)"))
                    : $"Channel {ch + 1}";

                (string detectedInstrument, int defaultPssVoice, string defaultPssVoiceName) = ResolveVoiceMetadata(
                    isNativeForActiveModel, is680, isDrum, isGmPassthrough, prog);

                // Cross-check against actual remapped output PC so UI voice strip always matches hardware output
                if (!isGmPassthrough && format == MidiFormat.GeneralMidi && remappedIsFullHardwareRemap && !isDrum &&
                    remappedPrograms.TryGetValue(ch, out byte remProg) &&
                    (!voiceOverrides.TryGetValue(ch, out var ovChk) || !ovChk.HasValue))
                {
                    if (is680)
                    {
                        int actualV680 = Math.Clamp((int)remProg, 0, 104);
                        if (actualV680 != defaultPssVoice)
                        {
                            defaultPssVoice = actualV680;
                            defaultPssVoiceName = LookupVoiceName(actualV680, is680: true);
                        }
                    }
                    else if (Constants.Pss790MidiProgramToVoice.TryGetValue(remProg, out var actualV790) &&
                             actualV790.VoiceNumber != defaultPssVoice)
                    {
                        defaultPssVoice = actualV790.VoiceNumber;
                        defaultPssVoiceName = actualV790.Name;
                    }
                }

                int activeVoice = defaultPssVoice;
                string activeVoiceName = defaultPssVoiceName;
                bool isOverridden = false;

                if (!isDrum && !isGmPassthrough && voiceOverrides.TryGetValue(ch, out var ov) && ov.HasValue)
                {
                    isOverridden = true;
                    activeVoice = ov.Value;
                    activeVoiceName = LookupVoiceName(activeVoice, is680);
                }

                int octShift = octaveShifts.TryGetValue(ch, out int os) ? os : 0;
                var mixer = getMixerState?.Invoke(ch) ?? (false, false, (byte)100);

                list.Add(new ChannelStateDto
                {
                    Channel = ch,
                    Role = role,
                    HasNotes = hasNotes,
                    DetectedInstrument = detectedInstrument,
                    InferredSource = inferredTag,
                    PssVoice = activeVoice,
                    PssVoiceName = activeVoiceName,
                    DefaultVoice = defaultPssVoice,
                    DefaultVoiceName = defaultPssVoiceName,
                    IsOverridden = isOverridden,
                    OctaveShift = octShift,
                    IsMuted = mixer.IsMuted,
                    IsSolo = mixer.IsSolo,
                    Volume = mixer.Volume,
                    IsDrum = isDrum
                });
            }

            return list;
        }

        public static string LookupVoiceName(int voiceNumber, bool is680)
        {
            if (is680)
            {
                return Pss680Constants.Voices.TryGetValue(voiceNumber, out var v680)
                    ? v680.Name
                    : $"Voice {voiceNumber}";
            }
            return Constants.Pss790Voices.TryGetValue(voiceNumber, out var v790)
                ? v790.Name
                : $"Voice {voiceNumber}";
        }

        private static (string detectedInstrument, int defaultVoice, string defaultVoiceName) ResolveVoiceMetadata(
            bool isNativeForActiveModel,
            bool is680,
            bool isDrum,
            bool isGmPassthrough,
            byte? prog)
        {
            if (isDrum)
            {
                if (isGmPassthrough) return ("GM Drums (Standard Kit)", -1, "GM Standard Kit");
                return is680
                    ? ("PSS-680 Percussion (32 Hand Percussion)", 99, "Hand Percussion (Ch 16)")
                    : ("PSS-790 Rhythm (32 Fixed Perc)", -1, "Fixed Rhythm (Ch 16)");
            }

            if (isNativeForActiveModel && is680)
            {
                if (prog.HasValue && Pss680Constants.Voices.TryGetValue(prog.Value, out var v680))
                {
                    return (v680.Name, v680.VoiceNumber, v680.Name);
                }
                return ("Piano 1 (Default)", 3, "Piano 1");
            }

            if (isNativeForActiveModel && !is680)
            {
                if (prog.HasValue && Constants.Pss790MidiProgramToVoice.TryGetValue(prog.Value, out var v790))
                {
                    return (v790.Name, v790.VoiceNumber, v790.Name);
                }
                return ("PIANO (Default)", 0, "PIANO");
            }

            // General MIDI (or cross-converted)
            byte gmProg = prog ?? 0;
            string gmName = prog.HasValue
                ? Remapper.GetGmInstrumentName(gmProg)
                : "Acoustic Grand Piano (Default)";

            if (isGmPassthrough)
            {
                return (gmName, gmProg, gmName);
            }

            if (is680)
            {
                int vNum = Pss680Constants.GmToPss680VoiceMap.TryGetValue(gmProg, out var m680) ? m680 : 3;
                return (gmName, vNum, LookupVoiceName(vNum, is680: true));
            }
            else
            {
                int vNum = Constants.GmToPss790VoiceMap.TryGetValue(gmProg, out var m790) ? m790 : 0;
                return (gmName, vNum, LookupVoiceName(vNum, is680: false));
            }
        }

        private static void InspectXgSysEx(
            byte[] d,
            Dictionary<int, byte> channelPrograms,
            HashSet<int> drumChannels)
        {
            if (d[0] != 0x43 || d[2] != 0x4C) return;

            if (d[1] == 0x00 && d[5] == 0x08 && d[7] == 0x00 && d.Length > 10)
            {
                int part = d[6];
                if (part is >= 0 and < 16) channelPrograms.TryAdd(part, d[10]);
            }
            else if ((d[1] & 0xF0) == 0x10 && d[3] == 0x08 && d[5] == 0x03 && d.Length > 6)
            {
                int part = d[4];
                if (part is >= 0 and < 16) channelPrograms.TryAdd(part, d[6]);
            }
            else if ((d[1] & 0xF0) == 0x10 && d[3] == 0x08 && d[5] == 0x07 && d.Length > 6 && d[6] > 0)
            {
                drumChannels.Add(d[4]);
            }
        }
    }
}
