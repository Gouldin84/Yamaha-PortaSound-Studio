using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace PSS790Player.Core
{
    public enum MidiFormat
    {
        GeneralMidi,
        Pss790Native
    }

    public class RemapperOptions
    {
        public bool EnablePolyphonyGuard { get; set; } = true;
        public bool ScalePitchBend { get; set; } = true;
        public bool FilterUnsupportedControllers { get; set; } = true;
        public bool ForceRemap { get; set; } = false;
        public Dictionary<int, int?> ChannelVoiceOverrides { get; set; } = new(); // Channel (0-15) -> VoiceNumber (0-99) or null (auto)
    }

    public class Remapper
    {
        public const string Pss790MarkerText = "PSS790_REMAPPED:v1";

        public static MidiFormat DetectMidiFormat(MidiFile midiFile)
        {
            // 1. Primary Check: Embedded Non-Destructive Marker in Text or Marker Events
            foreach (var track in midiFile.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is TextEvent textEvent && textEvent.Text.Contains("PSS790_REMAPPED"))
                        return MidiFormat.Pss790Native;
                    if (evt is MarkerEvent markerEvent && markerEvent.Text.Contains("PSS790_REMAPPED"))
                        return MidiFormat.Pss790Native;
                }
            }

            // 2. Heuristic Check: Channel 15 has percussion notes and Channel 9 has no events
            var usedChannels = new HashSet<int>();
            bool hasChannel15DrumNotes = false;

            foreach (var track in midiFile.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ChannelEvent ce)
                    {
                        usedChannels.Add(ce.Channel);
                        if (ce.Channel == 15 && ce is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0)
                        {
                            byte note = (byte)noteOn.NoteNumber;
                            if (Constants.Pss790Drums.ContainsKey(note))
                            {
                                hasChannel15DrumNotes = true;
                            }
                        }
                    }
                }
            }

            if (!usedChannels.Contains(9) && usedChannels.Contains(15) && hasChannel15DrumNotes)
            {
                return MidiFormat.Pss790Native;
            }

            return MidiFormat.GeneralMidi;
        }

        public static MidiFile Remap(MidiFile sourceMidi, RemapperOptions? options = null)
        {
            options ??= new RemapperOptions();
            var remapped = sourceMidi.Clone();
            var format = DetectMidiFormat(remapped);

            // If already PSS-790 converted and not forced, run in Passthrough Mode
            if (format == MidiFormat.Pss790Native && !options.ForceRemap)
            {
                ApplyVoiceOverridesOnly(remapped, options.ChannelVoiceOverrides);
                EnsureMarkerEmbedded(remapped);
                return remapped;
            }

            // Full GM to PSS-790 Remapping Pipeline
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
                // Filter unsupported events
                trackChunk.Events.RemoveAll(e =>
                    e is ChannelAftertouchEvent or NoteAftertouchEvent ||
                    (e is ControlChangeEvent cc && options.FilterUnsupportedControllers && !Constants.AllowedControlChanges.Contains((byte)cc.ControlNumber)) ||
                    (e is ProgramChangeEvent pc && pc.Channel == (FourBitNumber)9)
                );

                // Process channel events in-place
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

            // Run Smart Polyphony Optimizer
            if (options.EnablePolyphonyGuard)
            {
                var optimizer = new PolyphonyOptimizer(enabled: true);
                remapped = optimizer.Optimize(remapped);
            }

            // Embed metadata marker
            EnsureMarkerEmbedded(remapped);

            return remapped;
        }

        private static void ApplyVoiceOverridesOnly(MidiFile midi, Dictionary<int, int?> overrides)
        {
            if (overrides == null || overrides.Count == 0) return;

            foreach (var track in midi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ProgramChangeEvent pc && pc.Channel != (FourBitNumber)15)
                    {
                        int ch = pc.Channel;
                        if (overrides.TryGetValue(ch, out var overrideVoice) && overrideVoice.HasValue)
                        {
                            if (Constants.Pss790Voices.TryGetValue(overrideVoice.Value, out var voiceInfo))
                            {
                                pc.ProgramNumber = (SevenBitNumber)voiceInfo.MidiProgram;
                            }
                        }
                    }
                }
            }
        }

        private static void EnsureMarkerEmbedded(MidiFile midi)
        {
            var firstTrack = midi.GetTrackChunks().FirstOrDefault();
            if (firstTrack == null)
            {
                firstTrack = new TrackChunk();
                midi.Chunks.Add(firstTrack);
            }

            bool markerExists = firstTrack.Events.Any(e => e is TextEvent t && t.Text.Contains("PSS790_REMAPPED"));
            if (!markerExists)
            {
                firstTrack.Events.Insert(0, new TextEvent(Pss790MarkerText) { DeltaTime = 0 });
            }
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

            EnsureMarkerEmbedded(remapped);
            remapped.Write(destinationPath, overwriteFile: true);
        }
    }
}
