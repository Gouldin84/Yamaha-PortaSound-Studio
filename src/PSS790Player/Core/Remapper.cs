using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace PSS790Player.Core
{
    public class RemapperOptions
    {
        public bool EnablePolyphonyGuard { get; set; } = true;
        public bool ScalePitchBend { get; set; } = true;
        public bool FilterUnsupportedControllers { get; set; } = true;
        public Dictionary<int, int?> ChannelVoiceOverrides { get; set; } = new(); // Channel (0-15) -> VoiceNumber (0-99) or null (auto)
    }

    public class Remapper
    {
        public static MidiFile Remap(MidiFile sourceMidi, RemapperOptions? options = null)
        {
            options ??= new RemapperOptions();
            var remapped = sourceMidi.Clone();

            // Detect channels used in source
            var usedChannels = new HashSet<int>();

            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is ChannelEvent channelEvent)
                    {
                        usedChannels.Add(channelEvent.Channel);
                    }
                }
            }

            // Determine Channel Relocation
            // GM Drums: Channel 9 (10 in 1-based display) -> PSS-790 Channel 15 (16)
            // If Channel 15 already has melodic events, shift to lowest unused channel 0-14
            int? relocatedMelodicChannel = null;
            if (usedChannels.Contains(15) && usedChannels.Contains(9))
            {
                for (int ch = 0; ch < 15; ch++)
                {
                    if (ch != 9 && !usedChannels.Contains(ch))
                    {
                        relocatedMelodicChannel = ch;
                        break;
                    }
                }
                relocatedMelodicChannel ??= 9;
            }

            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                // 1. Filter out unsupported SysEx, Aftertouch, and non-whitelisted CCs
                trackChunk.Events.RemoveAll(e =>
                    e is ChannelAftertouchEvent or NoteAftertouchEvent ||
                    (e is ControlChangeEvent cc && options.FilterUnsupportedControllers && !Constants.AllowedControlChanges.Contains((byte)cc.ControlNumber)) ||
                    (e is ProgramChangeEvent pc && pc.Channel == (FourBitNumber)9) // Will be drum channel 15
                );

                // 2. Process Channel Events in-place
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is ChannelEvent chEvt)
                    {
                        int originalChannel = chEvt.Channel;

                        if (originalChannel == 15 && relocatedMelodicChannel.HasValue)
                        {
                            chEvt.Channel = (FourBitNumber)relocatedMelodicChannel.Value;
                        }
                        else if (originalChannel == 9)
                        {
                            chEvt.Channel = (FourBitNumber)15; // Relocate drums to Channel 16
                        }

                        int currentChannel = chEvt.Channel;

                        if (currentChannel == 15)
                        {
                            // Drum Channel Note Translation
                            if (chEvt is NoteOnEvent noteOn)
                            {
                                noteOn.NoteNumber = (SevenBitNumber)TranslateDrumNote((byte)noteOn.NoteNumber);
                            }
                            else if (chEvt is NoteOffEvent noteOff)
                            {
                                noteOff.NoteNumber = (SevenBitNumber)TranslateDrumNote((byte)noteOff.NoteNumber);
                            }
                        }
                        else
                        {
                            // Melodic Channel Processing
                            if (chEvt is ProgramChangeEvent pc)
                            {
                                if (options.ChannelVoiceOverrides.TryGetValue(currentChannel, out var overrideVoice) && overrideVoice.HasValue)
                                {
                                    if (Constants.Pss790Voices.TryGetValue(overrideVoice.Value, out var voiceInfo))
                                    {
                                        pc.ProgramNumber = (SevenBitNumber)voiceInfo.MidiProgram;
                                    }
                                }
                                else
                                {
                                    byte gmProg = (byte)pc.ProgramNumber;
                                    int pssVoiceNum = Constants.GmToPss790VoiceMap.GetValueOrDefault(gmProg, 0);
                                    byte pssProg = Constants.Pss790Voices[pssVoiceNum].MidiProgram;
                                    pc.ProgramNumber = (SevenBitNumber)pssProg;
                                }
                            }
                            else if (chEvt is PitchBendEvent pb && options.ScalePitchBend)
                            {
                                int centered = pb.PitchValue - 8192;
                                int scaled = (int)Math.Round(centered * Constants.PitchBendScaleFactor);
                                int finalValue = Math.Clamp(8192 + scaled, 0, 16383);
                                pb.PitchValue = (ushort)finalValue;
                            }
                        }
                    }
                }
            }

            // 3. Run Smart Polyphony Optimizer
            if (options.EnablePolyphonyGuard)
            {
                var optimizer = new PolyphonyOptimizer(enabled: true);
                remapped = optimizer.Optimize(remapped);
            }

            return remapped;
        }

        public static byte TranslateDrumNote(byte gmNote)
        {
            if (Constants.GmToPss790DrumMap.TryGetValue(gmNote, out var pssNote))
            {
                return pssNote;
            }

            if (gmNote < 35) return 44; // Bass drum lo
            if (gmNote > 81) return 60; // Crash cymbal
            return 49;                  // Snare hi
        }

        public static void SaveRemapped(MidiFile remapped, string destinationPath)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            remapped.Write(destinationPath, overwriteFile: true);
        }
    }
}
