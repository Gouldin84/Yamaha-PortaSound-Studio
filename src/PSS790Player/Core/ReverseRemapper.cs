using System;
using System.Collections.Generic;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PSS790Player.Core
{
    public static class ReverseRemapper
    {
        public static MidiFile ReverseRemapToGeneralMidi(
            MidiFile pssMidi,
            string model = "pss790",
            IReadOnlyDictionary<int, byte>? pss680BankGmHints = null)
        {
            if (string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase) || Remapper.HasPss680Marker(pssMidi))
            {
                return ReverseRemapPss680ToGeneralMidi(pssMidi, pss680BankGmHints);
            }

            return ReverseRemapPss790ToGeneralMidi(pssMidi);
        }

        public static MidiFile ReverseRemapPss790ToGeneralMidi(MidiFile pss790Midi)
        {
            var gmMidi = pss790Midi.Clone();

            // 1. Remove PSS-790 markers and any stray ProgramChange on Ch 15 via ManageTimedEvents so timestamps never shift
            foreach (var track in gmMidi.GetTrackChunks())
            {
                using var mgr = track.ManageTimedEvents();
                mgr.Objects.RemoveAll(te =>
                    (te.Event is TextEvent t && (t.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                 t.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase))) ||
                    (te.Event is MarkerEvent m && (m.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                   m.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase))) ||
                    (te.Event is ProgramChangeEvent pc && pc.Channel == 15));
            }

            // 2. Analyze active channels and note densities
            var usedChannels = new HashSet<int>();
            var channelNoteCounts = new int[16];
            foreach (var te in gmMidi.GetTimedEvents())
            {
                if (te.Event is ChannelEvent ce)
                {
                    usedChannels.Add(ce.Channel);
                    if (te.Event is NoteOnEvent noteOn && noteOn.Velocity > 0)
                    {
                        channelNoteCounts[ce.Channel]++;
                    }
                }
            }

            // 3. Resolve collision if Channel 9 (GM drums) is occupied by melodic data while Channel 15 has PSS-790 drums
            int freeMelodicChannel = -1;
            if (usedChannels.Contains(9) && usedChannels.Contains(15))
            {
                for (int c = 0; c < 16; c++)
                {
                    if (c != 9 && c != 15 && !usedChannels.Contains(c))
                    {
                        freeMelodicChannel = c;
                        break;
                    }
                }
                if (freeMelodicChannel == -1)
                {
                    freeMelodicChannel = System.Linq.Enumerable.Range(0, 15)
                        .Where(c => c != 9)
                        .OrderBy(c => c == 0 ? 1 : 0)
                        .ThenBy(c => channelNoteCounts[c])
                        .ThenByDescending(c => c)
                        .First();
                }

                foreach (var track in gmMidi.GetTrackChunks())
                {
                    foreach (var evt in track.Events)
                    {
                        if (evt is ChannelEvent ce && ce.Channel == 9)
                        {
                            ce.Channel = (FourBitNumber)freeMelodicChannel;
                        }
                    }
                }
            }

            // Ensure Standard Drum Kit ProgramChange(0) is initialized at tick 0 on GM Channel 10 only when Ch 9 was previously melodic
            if (usedChannels.Contains(9) && usedChannels.Contains(15))
            {
                var firstTrack = gmMidi.GetTrackChunks().FirstOrDefault();
                firstTrack?.Events.Insert(0, new ProgramChangeEvent((SevenBitNumber)0)
                {
                    Channel = (FourBitNumber)9,
                    DeltaTime = 0
                });
            }

            // 4. Move Channel 15 (PSS-790 drums) to Channel 9 (GM drums) and translate voices/notes/pitch bends
            foreach (var track in gmMidi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ChannelEvent ce)
                    {
                        if (ce.Channel == 15)
                        {
                            ce.Channel = (FourBitNumber)9;

                            if (evt is NoteOnEvent noteOn)
                            {
                                if (Constants.Pss790ToGmDrumMap.TryGetValue((byte)noteOn.NoteNumber, out var gmDrum))
                                {
                                    noteOn.NoteNumber = (SevenBitNumber)gmDrum;
                                }
                            }
                            else if (evt is NoteOffEvent noteOff)
                            {
                                if (Constants.Pss790ToGmDrumMap.TryGetValue((byte)noteOff.NoteNumber, out var gmDrum))
                                {
                                    noteOff.NoteNumber = (SevenBitNumber)gmDrum;
                                }
                            }
                        }
                        else
                        {
                            if (evt is ProgramChangeEvent pc)
                            {
                                if (Constants.Pss790MidiProgramToGmMap.TryGetValue((byte)pc.ProgramNumber, out var gmProg))
                                {
                                    pc.ProgramNumber = (SevenBitNumber)gmProg;
                                }
                            }
                            else if (evt is PitchBendEvent pb)
                            {
                                // PSS-790 is +/- 12 semitones vs GM +/- 2 semitones (6x expansion back to GM)
                                int centered = pb.PitchValue - 8192;
                                int scaled = (int)Math.Round(centered * 6.0);
                                int finalValue = Math.Clamp(8192 + scaled, 0, 16383);
                                pb.PitchValue = (ushort)finalValue;
                            }
                        }
                    }
                }
            }

            return gmMidi;
        }

        public static MidiFile ReverseRemapPss680ToGeneralMidi(
            MidiFile pss680Midi,
            IReadOnlyDictionary<int, byte>? bankGmHints = null)
        {
            var gmMidi = pss680Midi.Clone();

            // 1. Remove PSS-680 markers and any stray ProgramChange on Ch 15 via ManageTimedEvents so timestamps never shift
            foreach (var track in gmMidi.GetTrackChunks())
            {
                using var mgr = track.ManageTimedEvents();
                mgr.Objects.RemoveAll(te =>
                    (te.Event is TextEvent t && (t.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                 t.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase))) ||
                    (te.Event is MarkerEvent m && (m.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                   m.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase))) ||
                    (te.Event is ProgramChangeEvent pc && pc.Channel == 15));
            }

            // 2. Analyze active channels and note densities
            var usedChannels = new HashSet<int>();
            var channelNoteCounts = new int[16];
            foreach (var te in gmMidi.GetTimedEvents())
            {
                if (te.Event is ChannelEvent ce)
                {
                    usedChannels.Add(ce.Channel);
                    if (te.Event is NoteOnEvent noteOn && noteOn.Velocity > 0)
                    {
                        channelNoteCounts[ce.Channel]++;
                    }
                }
            }

            // 3. Resolve collision if Channel 9 (GM drums) is occupied by melodic data while Channel 15 has PSS-680 drums
            int freeMelodicChannel = -1;
            if (usedChannels.Contains(9) && usedChannels.Contains(15))
            {
                for (int c = 0; c < 16; c++)
                {
                    if (c != 9 && c != 15 && !usedChannels.Contains(c))
                    {
                        freeMelodicChannel = c;
                        break;
                    }
                }
                if (freeMelodicChannel == -1)
                {
                    freeMelodicChannel = System.Linq.Enumerable.Range(0, 15)
                        .Where(c => c != 9)
                        .OrderBy(c => c == 0 ? 1 : 0)
                        .ThenBy(c => channelNoteCounts[c])
                        .ThenByDescending(c => c)
                        .First();
                }

                foreach (var track in gmMidi.GetTrackChunks())
                {
                    foreach (var evt in track.Events)
                    {
                        if (evt is ChannelEvent ce && ce.Channel == 9)
                        {
                            ce.Channel = (FourBitNumber)freeMelodicChannel;
                        }
                    }
                }
            }

            // Ensure Standard Drum Kit ProgramChange(0) is initialized at tick 0 on GM Channel 10 only when Ch 9 was previously melodic
            if (usedChannels.Contains(9) && usedChannels.Contains(15))
            {
                var firstTrack = gmMidi.GetTrackChunks().FirstOrDefault();
                firstTrack?.Events.Insert(0, new ProgramChangeEvent((SevenBitNumber)0)
                {
                    Channel = (FourBitNumber)9,
                    DeltaTime = 0
                });
            }

            // 4. Move Channel 15 (PSS-680 drums) to Channel 9 (GM drums) and translate voices/notes
            foreach (var track in gmMidi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ChannelEvent ce)
                    {
                        if (ce.Channel == 15)
                        {
                            ce.Channel = (FourBitNumber)9;

                            if (evt is NoteOnEvent noteOn)
                            {
                                if (Pss680Constants.Pss680ToGmDrumMap.TryGetValue((byte)noteOn.NoteNumber, out var gmDrum))
                                {
                                    noteOn.NoteNumber = (SevenBitNumber)gmDrum;
                                }
                            }
                            else if (evt is NoteOffEvent noteOff)
                            {
                                if (Pss680Constants.Pss680ToGmDrumMap.TryGetValue((byte)noteOff.NoteNumber, out var gmDrum))
                                {
                                    noteOff.NoteNumber = (SevenBitNumber)gmDrum;
                                }
                            }
                        }
                        else if (evt is ProgramChangeEvent pc)
                        {
                            int prog = pc.ProgramNumber;
                            if (prog is >= 100 and <= 104)
                            {
                                byte mappedUserGm = (bankGmHints != null && bankGmHints.TryGetValue(prog, out var hint))
                                    ? hint
                                    : (byte)80;
                                pc.ProgramNumber = (SevenBitNumber)mappedUserGm;
                            }
                            else if (Pss680Constants.Pss680ToGmVoiceMap.TryGetValue(prog, out var gmProg))
                            {
                                pc.ProgramNumber = (SevenBitNumber)gmProg;
                            }
                        }
                        else if (evt is PitchBendEvent pb)
                        {
                            // PSS-680 Mode 99 MIDI IN is +/- 12 semitones (+/- 1 octave, Manual p.35) vs GM +/- 2 semitones (6x expansion back to GM)
                            int centered = pb.PitchValue - 8192;
                            int scaled = (int)Math.Round(centered * 6.0);
                            pb.PitchValue = (ushort)Math.Clamp(8192 + scaled, 0, 16383);
                        }
                    }
                }
            }

            return gmMidi;
        }
    }
}
