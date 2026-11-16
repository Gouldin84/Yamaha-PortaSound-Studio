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

        public PolyphonyOptimizer(bool enabled = true, int threshold = Constants.PolyphonyGuardThreshold)
        {
            _enabled = enabled;
            _threshold = threshold;
        }

        public MidiFile Optimize(MidiFile midiFile)
        {
            if (!_enabled)
                return midiFile;

            var resultFile = midiFile.Clone();
            var tempoMap = resultFile.GetTempoMap();

            var allNotes = resultFile.GetNotes().ToList();
            if (allNotes.Count == 0) return resultFile;

            // 1. Clamp Percussion Note Durations & Choke Open Hi-Hats on Channel 15
            var drumNotes = allNotes.Where(n => n.Channel == (FourBitNumber)15).OrderBy(n => n.Time).ToList();
            var clampedLengths = new Dictionary<(FourBitNumber Channel, SevenBitNumber NoteNumber, long Time), long>();
            Note? activeOpenHiHat = null;

            foreach (var note in drumNotes)
            {
                var durationMs = note.LengthAs<MetricTimeSpan>(tempoMap).TotalMilliseconds;
                var noteNumber = (byte)note.NoteNumber;
                var noteKey = (note.Channel, note.NoteNumber, note.Time);

                // Open Hi-Hat Choking
                if (noteNumber is 57 or 80) // 57 = Hi-Hat Closed, 80 = Hi-Hat Foot
                {
                    if (activeOpenHiHat != null && activeOpenHiHat.EndTime > note.Time)
                    {
                        var chokedLength = Math.Max(1, note.Time - activeOpenHiHat.Time);
                        var openKey = (activeOpenHiHat.Channel, activeOpenHiHat.NoteNumber, activeOpenHiHat.Time);
                        clampedLengths[openKey] = chokedLength;
                        activeOpenHiHat = null;
                    }
                }
                else if (noteNumber == 59) // 59 = Hi-Hat Open
                {
                    activeOpenHiHat = note;
                }

                // Decay clamping
                var isCymbal = noteNumber is 60 or 61 or 62 or 63 or 59;
                var maxAllowedMs = isCymbal ? 750 : 250;

                if (durationMs > maxAllowedMs)
                {
                    var newTimeSpan = new MetricTimeSpan(0, 0, 0, (int)maxAllowedMs);
                    var newLengthTicks = TimeConverter.ConvertFrom(newTimeSpan, tempoMap);
                    if (!clampedLengths.ContainsKey(noteKey) || newLengthTicks < clampedLengths[noteKey])
                    {
                        clampedLengths[noteKey] = newLengthTicks;
                    }
                }
            }

            // 2. Resolve Overlapping Same-Pitch Notes on Same Channel
            // If a note starts while an earlier note of the exact same pitch on the same channel is still sounding,
            // clamp the earlier note's duration so it ends when the new note starts, preventing hardware voice desync.
            var notesByChannelPitch = allNotes.GroupBy(n => (n.Channel, n.NoteNumber));
            foreach (var group in notesByChannelPitch)
            {
                var ordered = group.OrderBy(n => n.Time).ToList();
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    var current = ordered[i];
                    var next = ordered[i + 1];
                    if (current.EndTime > next.Time)
                    {
                        var clampedLen = Math.Max(1, next.Time - current.Time);
                        var key = (current.Channel, current.NoteNumber, current.Time);
                        if (!clampedLengths.ContainsKey(key) || clampedLen < clampedLengths[key])
                        {
                            clampedLengths[key] = clampedLen;
                        }
                    }
                }
            }

            if (clampedLengths.Count > 0)
            {
                foreach (var trackChunk in resultFile.GetTrackChunks())
                {
                    using var notesManager = trackChunk.ManageNotes();
                    foreach (var note in notesManager.Objects)
                    {
                        var key = (note.Channel, note.NoteNumber, note.Time);
                        if (clampedLengths.TryGetValue(key, out var targetLen))
                        {
                            note.Length = Math.Min(note.Length, targetLen);
                        }
                    }
                }
            }

            // 2. Dynamic Culling of Incidental Percussion during High Polyphony Peaks
            var notesToCull = CullIncidentalDuringPeaks(allNotes);
            if (notesToCull.Count > 0)
            {
                resultFile.RemoveNotes(n => notesToCull.Contains((n.Channel, n.NoteNumber, n.Time)));
            }

            return resultFile;
        }

        private HashSet<(FourBitNumber Channel, SevenBitNumber NoteNumber, long Time)> CullIncidentalDuringPeaks(List<Note> notes)
        {
            var notesToRemove = new HashSet<(FourBitNumber, SevenBitNumber, long)>();
            if (notes.Count == 0) return notesToRemove;

            var sortedNotes = notes.OrderBy(n => n.Time).ToList();
            var activeNotes = new List<Note>();

            foreach (var currentNote in sortedNotes)
            {
                var currentTime = currentNote.Time;

                activeNotes.RemoveAll(n => n.EndTime <= currentTime);
                activeNotes.Add(currentNote);

                if (activeNotes.Count >= _threshold)
                {
                    var incidentalDrum = activeNotes
                        .Where(n => n.Channel == (FourBitNumber)15 && Constants.IncidentalDrums.Contains((byte)n.NoteNumber))
                        .OrderBy(n => n.Velocity)
                        .FirstOrDefault();

                    if (incidentalDrum != null)
                    {
                        var key = (incidentalDrum.Channel, incidentalDrum.NoteNumber, incidentalDrum.Time);
                        if (!notesToRemove.Contains(key))
                        {
                            notesToRemove.Add(key);
                            activeNotes.Remove(incidentalDrum);
                        }
                    }
                }
            }

            return notesToRemove;
        }
    }
}
