using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PSS790Player.Core
{
    public static class ReverseRemapper
    {
        public static MidiFile ReverseRemapToGeneralMidi(MidiFile pssMidi, string model = "pss790")
        {
            if (string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase) || Remapper.HasPss680Marker(pssMidi))
            {
                return ReverseRemapPss680ToGeneralMidi(pssMidi);
            }

            return ReverseRemapPss790ToGeneralMidi(pssMidi);
        }

        public static MidiFile ReverseRemapPss790ToGeneralMidi(MidiFile pss790Midi)
        {
            var gmMidi = pss790Midi.Clone();

            // 1. Remove PSS-790 native marker events so standard engines recognize it as GM
            foreach (var track in gmMidi.GetTrackChunks())
            {
                track.Events.RemoveAll(e =>
                    (e is TextEvent te && (te.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                           te.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase))) ||
                    (e is MarkerEvent me && (me.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                             me.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase))));
            }

            // 2. Analyze which channels are active
            var usedChannels = new HashSet<int>();
            foreach (var te in gmMidi.GetTimedEvents())
            {
                if (te.Event is ChannelEvent ce)
                {
                    usedChannels.Add(ce.Channel);
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
                if (freeMelodicChannel == -1) freeMelodicChannel = 0;

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

            // 4. Move Channel 15 (PSS-790 drums) to Channel 9 (GM drums) and translate drum notes
            foreach (var track in gmMidi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ChannelEvent ce)
                    {
                        if (ce.Channel == 15)
                        {
                            // Move to GM Drum Channel 10 (0-indexed 9)
                            ce.Channel = (FourBitNumber)9;

                            // Translate Chart 1 Drum Note -> Standard General MIDI Drum Note
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
                            // Melodic Channels: Translate Chart 2 Program Changes to GM Instruments (0-127)
                            if (evt is ProgramChangeEvent pc)
                            {
                                if (Constants.Pss790MidiProgramToGmMap.TryGetValue((byte)pc.ProgramNumber, out var gmProg))
                                {
                                    pc.ProgramNumber = (SevenBitNumber)gmProg;
                                }
                            }
                            else if (evt is PitchBendEvent pb)
                            {
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

        public static MidiFile ReverseRemapPss680ToGeneralMidi(MidiFile pss680Midi)
        {
            var gmMidi = pss680Midi.Clone();

            // 1. Remove PSS-680 native marker events so standard engines recognize it as GM
            foreach (var track in gmMidi.GetTrackChunks())
            {
                track.Events.RemoveAll(e =>
                    (e is TextEvent te && (te.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                           te.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase))) ||
                    (e is MarkerEvent me && (me.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                             me.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase))));
            }

            // 2. Analyze which channels are active
            var usedChannels = new HashSet<int>();
            foreach (var te in gmMidi.GetTimedEvents())
            {
                if (te.Event is ChannelEvent ce)
                {
                    usedChannels.Add(ce.Channel);
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
                if (freeMelodicChannel == -1) freeMelodicChannel = 0;

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

            // 4. Move Channel 15 (PSS-680 drums) to Channel 9 (GM drums) and translate drum notes
            foreach (var track in gmMidi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ChannelEvent ce)
                    {
                        if (ce.Channel == 15)
                        {
                            // Move to GM Drum Channel 10 (0-indexed 9)
                            ce.Channel = (FourBitNumber)9;

                            // Translate PSS-680 32-Drum Note -> Standard General MIDI Drum Note
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
                        else
                        {
                            // Melodic Channels: Translate PSS-680 FM Program Changes (0-99) to GM Instruments (0-127)
                            if (evt is ProgramChangeEvent pc)
                            {
                                if (Pss680Constants.Pss680ToGmVoiceMap.TryGetValue((int)pc.ProgramNumber, out var gmProg))
                                {
                                    pc.ProgramNumber = (SevenBitNumber)gmProg;
                                }
                            }
                            else if (evt is PitchBendEvent pb)
                            {
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
    }
}
