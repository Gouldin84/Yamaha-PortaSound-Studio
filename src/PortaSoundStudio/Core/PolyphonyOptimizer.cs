using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PSS790Player.Core
{
    public class PolyphonyOptimizer
    {
        private readonly bool _enabled;
        private readonly int _threshold;
        private readonly string _model;
        private readonly int? _melodicCeilingOverride;

        private sealed class NoteContext
        {
            public long Time { get; }
            public long OriginalLength { get; }
            public long EffectiveLength { get; set; }
            public long EffectiveEndTime => Time + EffectiveLength;
            public long SoundEndTime { get; set; }
            public int Channel { get; }
            public byte NoteNumber { get; }
            public byte Velocity { get; }
            public int Priority { get; set; } = 1;
            public int Weight { get; set; } = 1;

            public NoteContext(Note note)
            {
                Time = note.Time;
                OriginalLength = note.Length;
                EffectiveLength = Math.Max(1, note.Length);
                SoundEndTime = Time + EffectiveLength;
                Channel = (int)note.Channel;
                NoteNumber = (byte)note.NoteNumber;
                Velocity = (byte)note.Velocity;
            }
        }

        public PolyphonyOptimizer(
            bool enabled = true,
            int threshold = Constants.PolyphonyGuardThreshold,
            string model = "pss790",
            int? melodicCeilingOverride = null)
        {
            _enabled = enabled;
            _threshold = threshold;
            _model = model;
            _melodicCeilingOverride = melodicCeilingOverride;
        }

        public MidiFile Optimize(
            MidiFile midiFile,
            IReadOnlyDictionary<int, byte>? channelGmPrograms = null,
            IReadOnlyDictionary<int, int>? channelVoiceWeights = null,
            IReadOnlyDictionary<int, List<(long Time, byte Program)>>? channelProgramTimelines = null)
        {
            if (!_enabled)
                return midiFile;

            var resultFile = midiFile.Clone();
            var tracks = resultFile.GetTrackChunks().ToList();
            if (tracks.Count == 0) return resultFile;

            // Step 0: Global cross-track deduplication of simultaneous same-channel / same-pitch notes.
            // Keeps the single highest-velocity (then longest-duration) note across ALL tracks to prevent
            // hardware voice desync and cross-track key collisions in Type-1 MIDI files.
            var indexedNotes = new List<(int TrackIndex, int Channel, byte NoteNumber, long Time, byte Velocity, long Length)>();
            for (int t = 0; t < tracks.Count; t++)
            {
                foreach (var note in tracks[t].GetNotes())
                {
                    indexedNotes.Add((t, (int)note.Channel, (byte)note.NoteNumber, note.Time, (byte)note.Velocity, note.Length));
                }
            }

            if (indexedNotes.Count == 0) return resultFile;

            var duplicatesByTrack = new Dictionary<int, Dictionary<(int Channel, byte NoteNumber, long Time, byte Velocity, long Length), int>>();
            foreach (var group in indexedNotes.GroupBy(n => (n.Channel, n.NoteNumber, n.Time)))
            {
                if (group.Count() <= 1) continue;

                var ranked = group
                    .OrderByDescending(n => n.Velocity)
                    .ThenByDescending(n => n.Length)
                    .ThenBy(n => n.TrackIndex)
                    .ToList();

                for (int i = 1; i < ranked.Count; i++)
                {
                    var loser = ranked[i];
                    if (!duplicatesByTrack.TryGetValue(loser.TrackIndex, out var trackDict))
                    {
                        trackDict = new Dictionary<(int, byte, long, byte, long), int>();
                        duplicatesByTrack[loser.TrackIndex] = trackDict;
                    }

                    var exactKey = (loser.Channel, loser.NoteNumber, loser.Time, loser.Velocity, loser.Length);
                    trackDict[exactKey] = trackDict.TryGetValue(exactKey, out int count) ? count + 1 : 1;
                }
            }

            foreach (var (trackIndex, removals) in duplicatesByTrack)
            {
                using var notesMgr = tracks[trackIndex].ManageNotes();
                var toRemove = new HashSet<Note>();
                foreach (var note in notesMgr.Objects)
                {
                    var exactKey = ((int)note.Channel, (byte)note.NoteNumber, note.Time, (byte)note.Velocity, note.Length);
                    if (removals.TryGetValue(exactKey, out int remaining) && remaining > 0)
                    {
                        toRemove.Add(note);
                        if (remaining == 1) removals.Remove(exactKey);
                        else removals[exactKey] = remaining - 1;
                    }
                }

                if (toRemove.Count > 0)
                {
                    notesMgr.Objects.RemoveAll(n => toRemove.Contains(n));
                }
            }

            var allNotes = resultFile.GetNotes().ToList();
            if (allNotes.Count == 0) return resultFile;

            var tempoMap = resultFile.GetTempoMap();
            bool is680 = string.Equals(_model, "pss680", StringComparison.OrdinalIgnoreCase);

            // Resolve GM programs per channel for musical priority shielding.
            // Prefer caller-supplied GM programs (from Remapper, before patches were remapped to native PSS numbers).
            var resolvedGmPrograms = new Dictionary<int, byte>();
            if (channelGmPrograms != null)
            {
                foreach (var (ch, gmProg) in channelGmPrograms)
                {
                    resolvedGmPrograms[ch] = gmProg;
                }
            }
            else
            {
                foreach (var track in tracks)
                {
                    foreach (var evt in track.Events)
                    {
                        if (evt is ProgramChangeEvent pc && !resolvedGmPrograms.ContainsKey(pc.Channel))
                        {
                            byte rawProg = (byte)pc.ProgramNumber;
                            if (is680 && Pss680Constants.Pss680ToGmVoiceMap.TryGetValue(rawProg, out byte mappedGm))
                            {
                                resolvedGmPrograms[pc.Channel] = mappedGm;
                            }
                            else
                            {
                                resolvedGmPrograms[pc.Channel] = rawProg;
                            }
                        }
                    }
                }
            }

            // Create tracking contexts for all unique surviving notes with chronological priority & element weight
            var allContexts = allNotes.Select(n => new NoteContext(n)).ToList();
            foreach (var ctx in allContexts)
            {
                byte prog = resolvedGmPrograms.TryGetValue(ctx.Channel, out var p) ? p : (byte)0;
                if (channelProgramTimelines != null &&
                    channelProgramTimelines.TryGetValue(ctx.Channel, out var timeline) &&
                    timeline.Count > 0)
                {
                    for (int i = 0; i < timeline.Count; i++)
                    {
                        if (timeline[i].Time <= ctx.Time) prog = timeline[i].Program;
                        else break;
                    }
                }
                ctx.Priority = GetInstrumentPriority(ctx.Channel, prog);
                if (ctx.Channel != 15 && channelVoiceWeights != null && channelVoiceWeights.TryGetValue(ctx.Channel, out var w))
                {
                    ctx.Weight = Math.Max(1, w);
                }
            }

            // Step 1: Clamp Percussion Note Durations & Choke Open Hi-Hats on Channel 15
            // Uses model-specific drum definitions and tempo-accurate LengthConverter.ConvertFrom(..., ctx.Time, tempoMap).
            var drumContexts = allContexts.Where(c => c.Channel == 15).OrderBy(c => c.Time).ToList();
            NoteContext? activeOpenHiHat = null;

            foreach (var ctx in drumContexts)
            {
                byte noteNumber = ctx.NoteNumber;

                bool isClosedHiHat;
                bool isOpenHiHat;
                bool isCymbal;

                if (is680)
                {
                    // PSS-680: 57 = Hi-Hat Closed, 59 = Hi-Hat Open; Cymbals: 59, 60, 61, 62, 63
                    isClosedHiHat = (noteNumber == 57);
                    isOpenHiHat = (noteNumber == 59);
                    isCymbal = (noteNumber is 59 or 60 or 61 or 62 or 63);
                }
                else
                {
                    // PSS-790: 57 = Hi-Hat Closed, 80 = Hi-Hat Foot, 59 = Hi-Hat Open; Cymbals: 59..63
                    isClosedHiHat = (noteNumber is 57 or 80);
                    isOpenHiHat = (noteNumber == 59);
                    isCymbal = (noteNumber is 59 or 60 or 61 or 62 or 63);
                }

                // Open Hi-Hat Choking
                if (isClosedHiHat)
                {
                    if (activeOpenHiHat != null && activeOpenHiHat.EffectiveEndTime > ctx.Time)
                    {
                        long chokedLength = Math.Max(1, ctx.Time - activeOpenHiHat.Time);
                        activeOpenHiHat.EffectiveLength = Math.Min(activeOpenHiHat.EffectiveLength, chokedLength);
                        activeOpenHiHat = null;
                    }
                }
                else if (isOpenHiHat)
                {
                    activeOpenHiHat = ctx;
                }

                // Decay clamping relative to ctx.Time across mid-song tempo changes
                int maxAllowedMs = is680
                    ? (isCymbal ? 280 : (isClosedHiHat ? 70 : 100))
                    : (isCymbal ? 750 : 250);
                var maxTimeSpan = new MetricTimeSpan(0, 0, 0, maxAllowedMs);
                long maxTicks = LengthConverter.ConvertFrom(maxTimeSpan, ctx.Time, tempoMap);
                if (maxTicks > 0)
                {
                    ctx.EffectiveLength = Math.Min(ctx.EffectiveLength, maxTicks);
                }
            }

            // Step 2: Resolve Overlapping Same-Pitch Notes on Same Channel
            var contextsByChannelPitch = allContexts.GroupBy(c => (c.Channel, c.NoteNumber));
            foreach (var group in contextsByChannelPitch)
            {
                var ordered = group.OrderBy(c => c.Time).ToList();
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    var current = ordered[i];
                    var next = ordered[i + 1];
                    if (current.EffectiveEndTime > next.Time)
                    {
                        long clampedLen = Math.Max(1, next.Time - current.Time);
                        current.EffectiveLength = Math.Min(current.EffectiveLength, clampedLen);
                    }
                }
            }

            // Step 2b: Model hardware envelope release-tail window on melodic voices
            // (60ms fast/plucked vs 100ms pads on PSS-680 12-voice FM, 100ms AWM on PSS-790) so release overlap never saturates the synth.
            var fmFastTailSpan = new MetricTimeSpan(0, 0, 0, 60);
            var stdTailSpan = new MetricTimeSpan(0, 0, 0, 100);
            foreach (var ctx in allContexts)
            {
                if (ctx.Channel != 15)
                {
                    var tailSpan = (is680 && ctx.Priority >= 2) ? fmFastTailSpan : stdTailSpan;
                    long tailTicks = LengthConverter.ConvertFrom(tailSpan, ctx.EffectiveEndTime, tempoMap);
                    ctx.SoundEndTime = ctx.EffectiveEndTime + Math.Max(0, tailTicks);
                }
                else
                {
                    ctx.SoundEndTime = ctx.EffectiveEndTime;
                }
            }

            // Step 3: Dynamic Polyphony Protection & Intelligent Voice Culling
            var contextsToCull = new HashSet<NoteContext>();
            var sortedContexts = allContexts.OrderBy(c => c.Time).ToList();
            var activeContexts = new List<NoteContext>();
            int effectiveLimit = Math.Max(1, _threshold);
            int activeWeightSum = 0;

            foreach (var currentCtx in sortedContexts)
            {
                long currentTime = currentCtx.Time;
                for (int i = activeContexts.Count - 1; i >= 0; i--)
                {
                    if (activeContexts[i].SoundEndTime <= currentTime)
                    {
                        activeWeightSum -= activeContexts[i].Weight;
                        activeContexts.RemoveAt(i);
                    }
                }
                activeContexts.Add(currentCtx);
                activeWeightSum += currentCtx.Weight;

                if (is680)
                {
                    // PSS-680 Architecture: Strictly separate 4-voice PCM Percussion (Ch 15) from 12-voice YM3420 FM (Ch 0-14).
                    if (currentCtx.Channel == 15)
                    {
                        var activeDrums = activeContexts.Where(c => c.Channel == 15).ToList();
                        while (activeDrums.Count > 4)
                        {
                            var drumToDrop = activeDrums
                                .Where(c => Pss680Constants.IncidentalDrums.Contains(c.NoteNumber))
                                .OrderBy(c => c.Velocity)
                                .ThenBy(c => c.Time)
                                .FirstOrDefault()
                                ?? activeDrums
                                    .OrderBy(c => c.Velocity)
                                    .ThenBy(c => c.Time)
                                    .First();

                            contextsToCull.Add(drumToDrop);
                            activeDrums.Remove(drumToDrop);
                            activeContexts.Remove(drumToDrop);
                            activeWeightSum -= drumToDrop.Weight;
                        }
                    }

                    // If overall concurrent weighted notes exceed threshold (12), cull incidental Latin hand percussion on Ch 15 first
                    while (activeWeightSum > effectiveLimit)
                    {
                        var incidentalDrum = activeContexts
                            .Where(c => c.Channel == 15 && Pss680Constants.IncidentalDrums.Contains(c.NoteNumber))
                            .OrderBy(c => c.Velocity)
                            .ThenBy(c => c.Time)
                            .FirstOrDefault();

                        if (incidentalDrum != null)
                        {
                            contextsToCull.Add(incidentalDrum);
                            activeContexts.Remove(incidentalDrum);
                            activeWeightSum -= incidentalDrum.Weight;
                            continue;
                        }

                        break;
                    }

                    var activeMelodic = activeContexts.Where(c => c.Channel != 15).ToList();
                    int melodicWeight = activeMelodic.Sum(c => c.Weight);
                    EvictOrStealMelodicVoices(
                        activeContexts,
                        activeMelodic,
                        contextsToCull,
                        currentCtx,
                        currentTime,
                        effectiveLimit,
                        currentWeight: melodicWeight,
                        prioritizeExpiredTailsFirst: true,
                        stealTier1HeldNotes: true,
                        tempoMap,
                        ref activeWeightSum);
                }
                else
                {
                    // PSS-790 Architecture: 28-voice shared AWM PCM polyphony.
                    // Cull incidental drums above effectiveLimit (e.g. 22/26/28), then gracefully yield expired tails
                    // or release-steal Tier-1 accompaniment chords above melodicCeiling.
                    int melodicCeiling = _melodicCeilingOverride ?? Math.Max(Constants.PolyphonyMax, effectiveLimit);
                    while (activeWeightSum > effectiveLimit)
                    {
                        var incidentalDrum = activeContexts
                            .Where(c => c.Channel == 15 && Constants.IncidentalDrums.Contains(c.NoteNumber))
                            .OrderBy(c => c.Velocity)
                            .ThenBy(c => c.Time)
                            .FirstOrDefault();

                        if (incidentalDrum != null)
                        {
                            contextsToCull.Add(incidentalDrum);
                            activeContexts.Remove(incidentalDrum);
                            activeWeightSum -= incidentalDrum.Weight;
                            continue;
                        }

                        if (activeWeightSum <= melodicCeiling)
                        {
                            break;
                        }

                        var activeMelodic = activeContexts.Where(c => c.Channel != 15).ToList();
                        int beforeWeight = activeWeightSum;
                        EvictOrStealMelodicVoices(
                            activeContexts,
                            activeMelodic,
                            contextsToCull,
                            currentCtx,
                            currentTime,
                            melodicCeiling,
                            currentWeight: activeWeightSum,
                            prioritizeExpiredTailsFirst: true,
                            stealTier1HeldNotes: true,
                            tempoMap,
                            ref activeWeightSum);
                        if (activeWeightSum == beforeWeight)
                        {
                            break;
                        }
                    }
                }
            }

            // Step 4: Apply Clamped Lengths to Surviving Notes (collision-safe map)
            var clampedLengthMap = new Dictionary<(int Channel, byte NoteNumber, long Time), long>();
            foreach (var ctx in allContexts)
            {
                if (contextsToCull.Contains(ctx)) continue;
                if (ctx.EffectiveLength == ctx.OriginalLength) continue;

                var key = (ctx.Channel, ctx.NoteNumber, ctx.Time);
                if (!clampedLengthMap.TryGetValue(key, out long existing) || ctx.EffectiveLength < existing)
                {
                    clampedLengthMap[key] = ctx.EffectiveLength;
                }
            }

            if (clampedLengthMap.Count > 0)
            {
                foreach (var trackChunk in tracks)
                {
                    using var notesManager = trackChunk.ManageNotes();
                    foreach (var note in notesManager.Objects)
                    {
                        var key = ((int)note.Channel, (byte)note.NoteNumber, note.Time);
                        if (clampedLengthMap.TryGetValue(key, out long targetLen))
                        {
                            note.Length = Math.Max(1, Math.Min(note.Length == 0 ? targetLen : note.Length, targetLen));
                        }
                    }
                }
            }

            // Step 5: Remove Culled Notes
            if (contextsToCull.Count > 0)
            {
                var culledKeySet = new HashSet<(int Channel, byte NoteNumber, long Time)>(
                    contextsToCull.Select(c => (c.Channel, c.NoteNumber, c.Time)));

                resultFile.RemoveNotes(n => culledKeySet.Contains(((int)n.Channel, (byte)n.NoteNumber, n.Time)));
            }

            return resultFile;
        }

        private static void EvictOrStealMelodicVoices(
            List<NoteContext> activeContexts,
            List<NoteContext> activeMelodic,
            HashSet<NoteContext> contextsToCull,
            NoteContext currentCtx,
            long currentTime,
            int weightLimit,
            int currentWeight,
            bool prioritizeExpiredTailsFirst,
            bool stealTier1HeldNotes,
            TempoMap tempoMap,
            ref int activeWeightSum)
        {
            while (currentWeight > weightLimit)
            {
                // Only actively held notes (not already-released tails) compete for lead/bass protection.
                var heldMelodic = activeMelodic.Where(c => c.EffectiveEndTime > currentTime).ToList();
                var leadPool = heldMelodic.Count > 0 ? heldMelodic : activeMelodic;
                var leadChannelCounts = new int[16];
                foreach (var lp in leadPool)
                {
                    if (lp.Channel >= 0 && lp.Channel < 16)
                        leadChannelCounts[lp.Channel]++;
                }
                var leadNote = leadPool
                    .OrderByDescending(c => c.Priority)
                    .ThenByDescending(c => (c.Channel >= 0 && c.Channel < 16 && leadChannelCounts[c.Channel] <= 2) ? 1 : 0)
                    .ThenByDescending(c => c.Velocity >= 48 ? 1 : 0)
                    .ThenByDescending(c => c.NoteNumber)
                    .FirstOrDefault();
                var bassNote = leadPool
                    .OrderByDescending(c => c.Priority > 0 ? 1 : 0)
                    .ThenBy(c => c.NoteNumber)
                    .FirstOrDefault();

                var channelCounts = new int[16];
                foreach (var m in activeMelodic)
                {
                    if (m.Channel >= 0 && m.Channel < 16)
                        channelCounts[m.Channel]++;
                }

                var candidates = activeMelodic.Where(c => c != leadNote && c != bassNote).ToList();
                if (candidates.Count == 0)
                {
                    break;
                }

                // Only steal the freshly triggered note itself if no already-sounding candidate exists.
                var olderCandidates = candidates.Where(c => c != currentCtx).ToList();
                var selectionPool = olderCandidates.Count > 0 ? olderCandidates : candidates;

                var ordered = prioritizeExpiredTailsFirst
                    ? selectionPool
                        .OrderBy(c => c.EffectiveEndTime <= currentTime ? 0 : 1)
                        .ThenBy(c => c.Priority)
                    : selectionPool
                        .OrderBy(c => c.Priority)
                        .ThenBy(c => c.EffectiveEndTime <= currentTime ? 0 : 1);

                var candidateToDrop = ordered
                    .ThenBy(c =>
                    {
                        int cnt = (c.Channel >= 0 && c.Channel < 16) ? channelCounts[c.Channel] : 0;
                        return cnt <= 1 ? 2 : (cnt <= 2 ? 1 : 0);
                    })
                    .ThenByDescending(c => (c.Channel >= 0 && c.Channel < 16) ? channelCounts[c.Channel] : 0)
                    .ThenBy(c => c.Time)
                    .ThenBy(c => c.Velocity)
                    .First();

                // Prefer yielding already-released envelope tails or release-stealing audible notes (>= 45ms held)
                // rather than retroactively erasing notes that already began cleanly.
                if (candidateToDrop.EffectiveEndTime <= currentTime)
                {
                    // Already received NoteOff; natural hardware release tail yields to the incoming voice without deleting the note.
                }
                else
                {
                    long heldTicks = currentTime - candidateToDrop.Time;
                    long minAudibleTicks = LengthConverter.ConvertFrom(new MetricTimeSpan(0, 0, 0, 45), candidateToDrop.Time, tempoMap);
                    if ((stealTier1HeldNotes || candidateToDrop.Priority >= 2) &&
                        heldTicks >= Math.Max(1, minAudibleTicks))
                    {
                        candidateToDrop.EffectiveLength = Math.Min(candidateToDrop.EffectiveLength, heldTicks);
                    }
                    else
                    {
                        contextsToCull.Add(candidateToDrop);
                    }
                }

                activeMelodic.Remove(candidateToDrop);
                activeContexts.Remove(candidateToDrop);
                activeWeightSum -= candidateToDrop.Weight;
                currentWeight -= candidateToDrop.Weight;
            }
        }

        private static int GetInstrumentPriority(int channel, byte gmProgram)
        {
            if (channel == 15) return 0; // Percussion budgeted separately

            // Tier 3 (Highest): Dedicated Basses, Solo Strings, Solo Brass, Solo Vocals, Accordion/Harmonica, Reeds/Winds, Synth Leads, Ethnic Solo Leads
            if ((gmProgram >= 32 && gmProgram <= 43) || // Basses + Violin, Viola, Cello, Contrabass
                gmProgram == 21 || gmProgram == 22 ||   // Accordion, Harmonica
                gmProgram == 54 || gmProgram == 58 ||   // Synth Voice, Tuba
                (gmProgram >= 56 && gmProgram <= 60) || // Solo Brass (Trumpet, Trombone, Muted Trumpet, French Horn)
                (gmProgram >= 64 && gmProgram <= 87) || // Reeds, Pipes, Synth Leads
                (gmProgram >= 109 && gmProgram <= 111)) // Bagpipe, Fiddle, Shanai
            {
                return 3;
            }

            // Tier 2 (Medium): Pianos, Chromatic Percussion, Organs, Guitars, Plucked/Timpani, Choir Pads, Ethnic Plucked, Melodic Synth Bell
            if (gmProgram <= 31 ||
                (gmProgram >= 45 && gmProgram <= 47) ||
                gmProgram == 52 || gmProgram == 53 ||
                gmProgram == 98 ||                       // FX 3 Crystal (melodic chime/bell)
                (gmProgram >= 104 && gmProgram <= 108) || // Sitar, Banjo, Shamisen, Koto, Kalimba
                (gmProgram >= 112 && gmProgram <= 118))   // Tinkle Bell .. Synth Drum
            {
                return 2;
            }

            // Tier 0: Non-pitched Sound FX & Reverse Cymbal (yield before pads & sustained harmony)
            if (gmProgram >= 119)
            {
                return 0;
            }

            // Tier 1 (Lowest musical tier): String Ensembles, Brass Sections, Synth Pads, Synth FX (96-103)
            return 1;
        }
    }
}
