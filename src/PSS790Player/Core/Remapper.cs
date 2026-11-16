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
            // 1. Pre-scan for RPN 0,0 (Pitch Bend Sensitivity) across tracks before filtering controllers
            var channelBendRange = new Dictionary<int, int>();
            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                var rpnMsb = new Dictionary<int, int>();
                var rpnLsb = new Dictionary<int, int>();

                foreach (var evt in trackChunk.Events)
                {
                    if (evt is ControlChangeEvent cc)
                    {
                        int ch = cc.Channel;
                        if (cc.ControlNumber == 101) rpnMsb[ch] = cc.ControlValue;
                        else if (cc.ControlNumber == 100) rpnLsb[ch] = cc.ControlValue;
                        else if (cc.ControlNumber == 6)
                        {
                            if (rpnMsb.TryGetValue(ch, out var msb) && msb == 0 &&
                                rpnLsb.TryGetValue(ch, out var lsb) && lsb == 0)
                            {
                                channelBendRange[ch] = (int)cc.ControlValue;
                            }
                        }
                    }
                }
            }

            // Pre-scan for Drum Channels (GM Channel 9 is standard percussion; GS/XG channels use Bank Select CC 0 == 127 or 128)
            var drumChannels = new HashSet<int> { 9 };
            var usedChannels = new HashSet<int>();
            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is ChannelEvent channelEvent)
                    {
                        usedChannels.Add(channelEvent.Channel);
                    }
                    if (evt is ControlChangeEvent cc && cc.ControlNumber == 0 && (cc.ControlValue == 127 || cc.ControlValue == 128))
                    {
                        drumChannels.Add(cc.Channel);
                    }
                }
            }

            int? relocatedMelodicChannel = null;
            // Only relocate Channel 15 if Channel 15 is MELODIC (not a drum channel) and another channel is used for drums
            if (usedChannels.Contains(15) && !drumChannels.Contains(15) && drumChannels.Any(d => usedChannels.Contains(d)))
            {
                for (int ch = 0; ch < 15; ch++)
                {
                    if (!drumChannels.Contains(ch) && !usedChannels.Contains(ch))
                    {
                        relocatedMelodicChannel = ch;
                        break;
                    }
                }
                relocatedMelodicChannel ??= drumChannels.FirstOrDefault(d => d != 15);
            }

            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                // Filter unsupported events, foreign GS/XG SysEx, and any Program Change on drum channels
                trackChunk.Events.RemoveAll(e =>
                    e is ChannelAftertouchEvent or NoteAftertouchEvent ||
                    e is NormalSysExEvent or EscapeSysExEvent ||
                    (e is ControlChangeEvent cc && options.FilterUnsupportedControllers && !Constants.AllowedControlChanges.Contains((byte)cc.ControlNumber)) ||
                    (e is ProgramChangeEvent pc && drumChannels.Contains(pc.Channel))
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
                        else if (drumChannels.Contains(originalChannel))
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
                            // Melodic octave folding for PSS-790 hardware multisample range (36 to 96)
                            if (chEvt is NoteOnEvent melodicNoteOn)
                            {
                                melodicNoteOn.NoteNumber = (SevenBitNumber)ClampMelodicNote((byte)melodicNoteOn.NoteNumber);
                            }
                            else if (chEvt is NoteOffEvent melodicNoteOff)
                            {
                                melodicNoteOff.NoteNumber = (SevenBitNumber)ClampMelodicNote((byte)melodicNoteOff.NoteNumber);
                            }

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
                                double scaleFactor = channelBendRange.TryGetValue(originalChannel, out var range) && range > 0
                                    ? Math.Clamp((double)range / 12.0, 0.05, 1.0)
                                    : Constants.PitchBendScaleFactor;

                                int centered = pb.PitchValue - 8192;
                                int scaled = (int)Math.Round(centered * scaleFactor);
                                int finalValue = Math.Clamp(8192 + scaled, 0, 16383);
                                pb.PitchValue = (ushort)finalValue;
                            }
                        }
                    }
                }
            }

            // Ensure all active melodic channels with notes have an initial Program Change
            var allActiveNoteChannels = remapped.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<NoteOnEvent>()
                .Where(n => n.Velocity > 0)
                .Select(n => (int)n.Channel)
                .Distinct()
                .ToHashSet();

            var allPcChannels = remapped.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<ProgramChangeEvent>()
                .Select(p => (int)p.Channel)
                .Distinct()
                .ToHashSet();

            var firstTrack = remapped.GetTrackChunks().FirstOrDefault();
            if (firstTrack != null)
            {
                foreach (var ch in allActiveNoteChannels)
                {
                    if (ch != 15 && !allPcChannels.Contains(ch))
                    {
                        // Default to PSS-790 Voice 00: PIANO (MidiProgram 3) at Tick 0
                        firstTrack.Events.Insert(0, new ProgramChangeEvent((SevenBitNumber)3)
                        {
                            Channel = (FourBitNumber)ch,
                            DeltaTime = 0
                        });
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

            // Musical fallbacks for extended GM/GS drum notes
            if (gmNote is 27 or 28) return 64; // High Q / Slap -> Rimshot
            if (gmNote is 31 or 33) return 75; // Sticks / Click -> Woodblock/Cowbell
            if (gmNote is 82 or 83) return 58; // Shaker / Jingle Bell -> Maracas
            if (gmNote is 84 or 85) return 76; // Bell Tree / Castanets -> Triangle Open
            if (gmNote is 86 or 87) return 45; // Surdo -> Bass Drum Hi

            if (gmNote < 35) return 44; // Bass drum lo
            if (gmNote > 81) return 58; // Default high percussion -> Maracas
            return 49;                  // Snare hi
        }

        public static byte ClampMelodicNote(byte note)
        {
            // PSS-790 keyboard and tone generator physical multisample range: C2 (36) to C7 (96)
            while (note < 36) note += 12;
            while (note > 96) note -= 12;
            return Math.Clamp(note, (byte)36, (byte)96);
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
