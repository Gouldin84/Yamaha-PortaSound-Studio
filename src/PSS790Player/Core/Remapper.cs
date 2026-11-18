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
        public bool NeutralizeAcousticPitchBends { get; set; } = true; // Neutralizes micro pitch bends on acoustic instruments (Pianos, Organs, Strings, Bells, Choirs, Brass sections) so they don't detune on PSS-790 hardware
        public bool AutoCenterDanglingPitchBends { get; set; } = true; // Guarantees all channels return to pitch 8192 (center) at track end and during silences
        public bool EmulateSustainPedal { get; set; } = true; // Extends staccato note durations while CC 64 (Damper Pedal) is held, preventing dry/empty sound on piano/keyboard tracks
        public bool FoldExpressionPedal { get; set; } = true; // Folds CC 11 (Expression) into CC 7 (Volume) so swells, crescendos, and track balance play on PSS-790
        public bool NormalizeAwmVelocities { get; set; } = true; // Maps velocities into PSS-790 sweet spot (36-118), preventing inaudible notes and harsh sample clipping
        public bool EnsureSetupPreRoll { get; set; } = true; // Guarantees a clean setup window before Note 1 so microcomputer configures voices without dropping opening notes
        public bool DefaultReverbBoost { get; set; } = true; // Injects default CC 91 (Reverb Send = 48-64) for melodic channels lacking reverb events
        public bool FilterUnsupportedControllers { get; set; } = true;
        public bool ForceRemap { get; set; } = false;
        public double TempoMultiplier { get; set; } = 1.0; // Optional tempo multiplier (e.g. 1.25x, 1.35x, 1.5x)
        public bool AutoNormalizeSluggishTempo { get; set; } = false; // When true, doubles tempo if detected average BPM < 68 BPM
        public Dictionary<int, int?> ChannelVoiceOverrides { get; set; } = new(); // Channel (0-15) -> VoiceNumber (0-99) or null (auto)
    }

    public class Remapper
    {
        public const string Pss790MarkerText = "PSS790_REMAPPED:v1";
        public const string Pss790RecordingMarkerText = "PSS790_RECORDING:v1";

        public static bool HasPss790Marker(MidiFile midiFile)
        {
            foreach (var track in midiFile.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is TextEvent textEvent && (textEvent.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                        textEvent.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase)))
                        return true;
                    if (evt is MarkerEvent markerEvent && (markerEvent.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                          markerEvent.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }
            return false;
        }

        public static bool IsRecording(MidiFile midiFile)
        {
            foreach (var track in midiFile.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is TextEvent textEvent && textEvent.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (evt is MarkerEvent markerEvent && markerEvent.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public static MidiFormat DetectMidiFormat(MidiFile midiFile)
        {
            // 1. Primary Check: Embedded Non-Destructive Marker in Text or Marker Events (Remapped or Recording)
            if (HasPss790Marker(midiFile))
            {
                return MidiFormat.Pss790Native;
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

            // 0. Intelligent Tempo Normalization & Adjustment
            ApplyTempoAdjustments(remapped, options);

            // Normalize zero-velocity NoteOn events to standard NoteOff events to guarantee note pairing across all parsers
            foreach (var track in remapped.GetTrackChunks())
            {
                for (int i = 0; i < track.Events.Count; i++)
                {
                    if (track.Events[i] is NoteOnEvent noteOn && (byte)noteOn.Velocity == 0)
                    {
                        track.Events[i] = new NoteOffEvent(noteOn.NoteNumber, (SevenBitNumber)0)
                        {
                            Channel = noteOn.Channel,
                            DeltaTime = noteOn.DeltaTime
                        };
                    }
                }
            }

            // Pre-scan for GM Programs per channel to identify acoustic vs synth/solo channels
            var channelGmPrograms = new Dictionary<int, byte>();

            // 1. Scan standard ProgramChangeEvents
            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is ProgramChangeEvent pc && !channelGmPrograms.ContainsKey(pc.Channel))
                    {
                        channelGmPrograms[pc.Channel] = (byte)pc.ProgramNumber;
                    }
                }
            }

            // 2. Scan for Yamaha XG Multi-Part SysEx Parameter Voicing dumps before SysEx is stripped
            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is NormalSysExEvent sysex && sysex.Data != null && sysex.Data.Length >= 8)
                    {
                        var d = sysex.Data;
                        // Yamaha XG SysEx: 43 [dev] 4C ...
                        if (d[0] == 0x43 && d[2] == 0x4C)
                        {
                            // Bulk Dump: 43 00 4C [sizeH] [sizeL] [addrH] [addrM] [addrL]
                            // Address 08 [part] 00 is Multi-Part Base Parameter Block, offset 10 is Program Number
                            if (d[1] == 0x00 && d[5] == 0x08 && d[7] == 0x00 && d.Length > 10)
                            {
                                int part = d[6];
                                byte prog = d[10];
                                if (part >= 0 && part < 16 && !channelGmPrograms.ContainsKey(part))
                                {
                                    channelGmPrograms[part] = prog;
                                }
                            }
                            // Parameter Change: 43 10 4C [addrH] [addrM] [addrL] [data...]
                            // Address 08 [part] 03 is Part Program Number
                            else if (d[1] == 0x10 && d[3] == 0x08 && d[5] == 0x03 && d.Length > 6)
                            {
                                int part = d[4];
                                byte prog = d[6];
                                if (part >= 0 && part < 16 && !channelGmPrograms.ContainsKey(part))
                                {
                                    channelGmPrograms[part] = prog;
                                }
                            }
                        }
                    }
                }
            }

            // 3. Scan track metadata / text events for active channels lacking ProgramChange
            var tracksWithNotes = new Dictionary<int, TrackChunk>();
            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is NoteOnEvent n && n.Velocity > 0 && n.Channel != (FourBitNumber)9)
                    {
                        tracksWithNotes.TryAdd((int)n.Channel, trackChunk);
                    }
                }
            }

            foreach (var (ch, tr) in tracksWithNotes)
            {
                if (!channelGmPrograms.ContainsKey(ch))
                {
                    // Try inferring from track name or text events
                    byte? inferred = null;
                    foreach (var textEvt in tr.Events.OfType<BaseTextEvent>())
                    {
                        inferred = InferGmProgramFromText(textEvt.Text);
                        if (inferred.HasValue) break;
                    }

                    // Default to GM 0 (Acoustic Grand Piano) if text not found
                    channelGmPrograms[ch] = inferred ?? 0;
                }
            }

            // 0. Virtual Sustain Pedal (CC 64) Emulation: Extend staccato note durations for keyboards (GM 0-23)
            if (options.EmulateSustainPedal)
            {
                ApplySustainPedalEmulation(remapped, channelGmPrograms);
            }

            // 0b. Dynamic Expression (CC 11) Folding into Volume (CC 7) for PSS-790 hardware swells & balance
            if (options.FoldExpressionPedal)
            {
                ApplyExpressionPedalFolding(remapped);
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
                    if (evt is NormalSysExEvent sysex && sysex.Data.Length > 6)
                    {
                        var d = sysex.Data;
                        // Yamaha XG SysEx Part Mode Drum (08 [part] 07 > 0)
                        if (d[0] == 0x43 && d[2] == 0x4C && d[1] == 0x10 && d[3] == 0x08 && d[5] == 0x07 && d[6] > 0)
                        {
                            drumChannels.Add(d[4]);
                        }
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
                var bentChannels = new HashSet<int>();

                // Filter unsupported events, foreign GS/XG SysEx, and any Program Change on drum channels
                // CRITICAL: Must use ManageTimedEvents to preserve absolute event timing and prevent track desync!
                using (var timedEventsMgr = trackChunk.ManageTimedEvents())
                {
                    timedEventsMgr.Objects.RemoveAll(te =>
                        te.Event is ChannelAftertouchEvent or NoteAftertouchEvent ||
                        te.Event is NormalSysExEvent or EscapeSysExEvent ||
                        (te.Event is ControlChangeEvent cc && options.FilterUnsupportedControllers && !Constants.AllowedControlChanges.Contains((byte)cc.ControlNumber)) ||
                        (te.Event is ProgramChangeEvent pc && drumChannels.Contains(pc.Channel))
                    );
                }

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
                            int gmProg = channelGmPrograms.TryGetValue(originalChannel, out var gp) ? gp : -1;

                            // Melodic octave folding for PSS-790 hardware multisample range (36 to 96, 88 for acoustic)
                            if (chEvt is NoteOnEvent melodicNoteOn)
                            {
                                melodicNoteOn.NoteNumber = (SevenBitNumber)ClampMelodicNote((byte)melodicNoteOn.NoteNumber, gmProg);
                                if (options.NormalizeAwmVelocities && melodicNoteOn.Velocity > 0)
                                {
                                    byte v = (byte)melodicNoteOn.Velocity;
                                    int norm = Constants.AwmVelocityMin + (int)Math.Round((v - 1) * (double)(Constants.AwmVelocityMax - Constants.AwmVelocityMin) / 126.0);
                                    melodicNoteOn.Velocity = (SevenBitNumber)Math.Clamp(norm, 1, 127);
                                }
                            }
                            else if (chEvt is NoteOffEvent melodicNoteOff)
                            {
                                melodicNoteOff.NoteNumber = (SevenBitNumber)ClampMelodicNote((byte)melodicNoteOff.NoteNumber, gmProg);
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
                                    byte progNum = (byte)pc.ProgramNumber;
                                    int pssVoiceNum = Constants.GmToPss790VoiceMap.GetValueOrDefault(progNum, 0);
                                    byte pssProg = Constants.Pss790Voices[pssVoiceNum].MidiProgram;
                                    pc.ProgramNumber = (SevenBitNumber)pssProg;
                                }
                            }
                            else if (chEvt is PitchBendEvent pb)
                            {
                                bool isAcoustic = gmProg >= 0 && IsAcousticOrKeyboardInstrument((byte)gmProg);

                                if (options.NeutralizeAcousticPitchBends && isAcoustic)
                                {
                                    // Acoustic instruments (piano, strings, organ) must not have micro-bends detuning hardware voices
                                    pb.PitchValue = 8192;
                                }
                                else if (options.ScalePitchBend)
                                {
                                    double scaleFactor = channelBendRange.TryGetValue(originalChannel, out var range) && range > 0
                                        ? Math.Clamp((double)range / 12.0, 0.05, 1.0)
                                        : Constants.PitchBendScaleFactor;

                                    int centered = pb.PitchValue - 8192;
                                    int scaled = (int)Math.Round(centered * scaleFactor);
                                    int finalValue = Math.Clamp(8192 + scaled, 0, 16383);
                                    pb.PitchValue = (ushort)finalValue;
                                    bentChannels.Add(currentChannel);
                                }
                            }
                        }
                    }
                }

                // Guarantee that all channels that experienced pitch bends cleanly return to center (8192)
                if (options.AutoCenterDanglingPitchBends && bentChannels.Count > 0)
                {
                    foreach (var ch in bentChannels)
                    {
                        var lastPb = trackChunk.Events.OfType<PitchBendEvent>().LastOrDefault(p => (int)p.Channel == ch);
                        if (lastPb != null && lastPb.PitchValue != 8192)
                        {
                            // Only append center-bend if the track contains active notes on this channel
                            bool hasNotesOnChannel = trackChunk.Events.OfType<NoteOnEvent>().Any(n => (int)n.Channel == ch && n.Velocity > 0);
                            if (hasNotesOnChannel)
                            {
                                int endIdx = -1;
                                for (int idx = trackChunk.Events.Count - 1; idx >= 0; idx--)
                                {
                                    if (trackChunk.Events[idx] is EndOfTrackEvent)
                                    {
                                        endIdx = idx;
                                        break;
                                    }
                                }
                                var centerPb = new PitchBendEvent((ushort)8192)
                                {
                                    Channel = (FourBitNumber)ch,
                                    DeltaTime = 0
                                };

                                if (endIdx >= 0)
                                {
                                    trackChunk.Events.Insert(endIdx, centerPb);
                                }
                                else
                                {
                                    trackChunk.Events.Add(centerPb);
                                }
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
                        byte pssProg = 3; // Default PSS-790 Voice 00: PIANO (MidiProgram 3)
                        if (channelGmPrograms.TryGetValue(ch, out var gmProg))
                        {
                            int pssVoiceNum = Constants.GmToPss790VoiceMap.GetValueOrDefault(gmProg, 0);
                            if (Constants.Pss790Voices.TryGetValue(pssVoiceNum, out var vInfo))
                            {
                                pssProg = vInfo.MidiProgram;
                            }
                        }

                        firstTrack.Events.Insert(0, new ProgramChangeEvent((SevenBitNumber)pssProg)
                        {
                            Channel = (FourBitNumber)ch,
                            DeltaTime = 0
                        });
                    }
                }
            }

            // Injects default Yamaha DSP Reverb (CC 91) on channels without reverb events
            if (options.DefaultReverbBoost)
            {
                ApplyReverbBoost(remapped);
            }

            // Run Smart Polyphony Optimizer
            if (options.EnablePolyphonyGuard)
            {
                var optimizer = new PolyphonyOptimizer(enabled: true);
                remapped = optimizer.Optimize(remapped);
            }

            // Enforce clean setup pre-roll separation so tone generators configure before Note 1 strikes
            if (options.EnsureSetupPreRoll)
            {
                ApplySetupPreRoll(remapped);
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

            if (!HasPss790Marker(midi))
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

        public static bool IsPianoOrKeyboardInstrument(byte gmProgram)
        {
            // GM 0-7: Pianos (Acoustic Grand, Bright, Electric Grand, Honky-tonk, Rhodes, Clavinet)
            // GM 8-15: Chromatic Percussion / Bells (Celesta, Glockenspiel, Music Box, Vibraphone, Marimba)
            // GM 16-23: Organs & Accordions
            return gmProgram <= 23;
        }

        public static bool IsAcousticOrKeyboardInstrument(byte gmProgram)
        {
            // GM 0-7: Piano
            // GM 8-15: Chromatic Percussion / Bells
            // GM 16-23: Organ & Accordion
            // GM 40-55: Strings & Orchestral Ensembles, Harp, Timpani, Choirs
            // GM 88-103: Synth Pads & FX
            return (gmProgram >= 0 && gmProgram <= 23) ||
                   (gmProgram >= 40 && gmProgram <= 55) ||
                   (gmProgram >= 88 && gmProgram <= 103);
        }

        public static bool IsAliasingSensitiveAcousticVoice(byte gmProgram)
        {
            // Acoustic piano (GM 0-7), Organs (GM 16-23), and Strings/Choir/Ensemble (GM 40-55)
            // exhibit harsh FM/AWM multisample aliasing above Note 88 (E6).
            // Chromatic percussion/bells (GM 8-15), Woodwinds/Piccolos (GM 72-79), and Synth Leads (GM 80-87)
            // naturally play up to Note 108 (C8) and must not be restricted to 88.
            return (gmProgram >= 0 && gmProgram <= 7) ||
                   (gmProgram >= 16 && gmProgram <= 23) ||
                   (gmProgram >= 40 && gmProgram <= 55);
        }

        public static bool IsBassInstrument(int gmProg)
        {
            // GM 32-39: Basses, 43: Contrabass, 58: Tuba
            return (gmProg >= 32 && gmProg <= 39) || gmProg == 43 || gmProg == 58;
        }

        public static byte ClampMelodicNote(byte note, int gmProg)
        {
            // PSS-790 tone generator natively receives MIDI notes 12 to 108 (C0 to C8).
            // Acoustic piano, organ, and strings voices above Note 88 (E6) exhibit harsh multisample aliasing.
            // All other voices (synth leads, brass, bells, piccolos, flutes, guitars, bass) play cleanly up to Note 108 (C8).
            bool isSensitive = gmProg >= 0 && IsAliasingSensitiveAcousticVoice((byte)gmProg);
            byte maxNote = isSensitive ? (byte)88 : (byte)108;

            // Bass instruments and keyboards play naturally in Octave 1 (C1=24 to B1=35).
            // Minimum note threshold is Note 24 (C1), preserving all 4-string (E1=28) and 5-string (B0/C1) bass notes.
            byte minNote = 24;

            while (note < minNote) note += 12;
            while (note > maxNote) note -= 12;
            return Math.Clamp(note, minNote, maxNote);
        }

        public static byte ClampMelodicNote(byte note) => ClampMelodicNote(note, -1);

        public static byte? InferGmProgramFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var lower = text.ToLowerInvariant();

            if (lower.Contains("bass") || lower.Contains("bss"))
            {
                if (lower.Contains("ac") || lower.Contains("acoustic") || lower.Contains("upright")) return 32; // Acoustic Bass
                if (lower.Contains("slap")) return 36; // Slap Bass 1
                if (lower.Contains("synth") || lower.Contains("syn")) return 38; // Synth Bass 1
                if (lower.Contains("pick")) return 34; // Electric Bass (pick)
                if (lower.Contains("fretless")) return 35; // Fretless Bass
                return 33; // Electric Bass (finger)
            }
            if (lower.Contains("guitar") || lower.Contains("gtr"))
            {
                if (lower.Contains("dist") || lower.Contains("overdrive") || lower.Contains("od")) return 29; // Overdriven Guitar
                if (lower.Contains("mute")) return 28; // Muted Guitar
                if (lower.Contains("e ") || lower.Contains("electric") || lower.Contains("clean")) return 27; // Clean Guitar
                if (lower.Contains("nylon")) return 24; // Nylon Guitar
                if (lower.Contains("jazz")) return 26; // Electric Guitar (jazz)
                return 25; // Steel Acoustic Guitar
            }
            if (lower.Contains("piano") || lower.Contains("pno") || lower.Contains("grand") || lower.Contains("intro"))
            {
                if (lower.Contains("honky") || lower.Contains("tack")) return 3; // Honky-tonk Piano
                return 0; // Acoustic Grand Piano
            }
            if (lower.Contains("rhodes") || lower.Contains("electric piano") || lower.Contains("e.piano") || lower.Contains("e-piano") || lower.Contains("wurlitzer") || lower.Contains("wurli") || lower.StartsWith("ep ") || lower.EndsWith(" ep") || lower.Contains(" ep ") || lower == "ep")
            {
                return 4; // Electric Piano 1 (Rhodes)
            }
            if (lower.Contains("clav") || lower.Contains("clavi")) return 7; // Clavinet
            if (lower.Contains("harpsi") || lower.Contains("harpsichord")) return 6; // Harpsichord
            if (lower.Contains("organ") || lower.Contains("hammond") || lower.Contains("b3")) return 16; // Drawbar Organ
            if (lower.Contains("accord") || lower.Contains("harmonica") || lower.Contains("bandoneon")) return 21; // Accordion
            if (lower.Contains("vibes") || lower.Contains("vibraphone")) return 11; // Vibraphone
            if (lower.Contains("marimba")) return 12; // Marimba
            if (lower.Contains("xylophone") || lower.Contains("xylo")) return 13; // Xylophone
            if (lower.Contains("timpani")) return 47; // Timpani
            if (lower.Contains("pizz") || lower.Contains("pizzicato")) return 45; // Pizzicato Strings
            if (lower.Contains("string") || lower.Contains("str ") || lower.Contains("str.") || lower.Contains("violin") || lower.Contains("viola") || lower.Contains("cello") || lower.Contains("contrabass") || lower.Contains("orch")) return 48; // String Ensemble 1
            if (lower.Contains("choir") || lower.Contains("aah")) return 52; // Choir Aahs
            if (lower.Contains("vocal") || lower.Contains("voice") || lower.Contains("ooh")) return 54; // Voice Oohs
            if (lower.Contains("trumpet") || lower.Contains("brass") || lower.Contains("horn") || lower.Contains("trombone") || lower.Contains("tuba")) return 61; // Brass Section
            if (lower.Contains("sax")) return 66; // Tenor Sax
            if (lower.Contains("flute") || lower.Contains("pan") || lower.Contains("pipe") || lower.Contains("piccolo")) return 73; // Flute
            if (lower.Contains("oboe") || lower.Contains("clarinet") || lower.Contains("bassoon")) return 68; // Oboe
            if (lower.Contains("bell") || lower.Contains("chime") || lower.Contains("glock") || lower.Contains("celesta")) return 9; // Glockenspiel / Bells
            if (lower.Contains("harp")) return 46; // Orchestral Harp
            if (lower.Contains("pad") || lower.Contains("warm") || lower.Contains("sweep")) return 89; // Warm Pad
            if (lower.Contains("lead") || lower.Contains("synth") || lower.Contains("syn ") || lower.Contains("syn.") || lower.Contains("saw") || lower.Contains("square") || lower.Contains("arp")) return 80; // Lead 1 (square)

            return null;
        }

        public static string GetGmInstrumentName(byte program)
        {
            if (Constants.GmToPss790VoiceMap.TryGetValue(program, out var pssVoice) &&
                Constants.Pss790Voices.TryGetValue(pssVoice, out var voiceInfo))
            {
                return voiceInfo.Name;
            }
            return $"GM Instrument {program}";
        }

        public static MidiHealthReport AnalyzeHealth(MidiFile originalMidi, MidiFile? remappedMidi = null, string? fileName = null)
        {
            return MidiHealthInspector.Analyze(originalMidi, remappedMidi, fileName);
        }

        public static void ApplySustainPedalEmulation(MidiFile midi, Dictionary<int, byte>? channelGmPrograms = null)
        {
            var timeDivision = midi.TimeDivision as TicksPerQuarterNoteTimeDivision;
            short tpqn = timeDivision?.TicksPerQuarterNote ?? 480;
            long maxPedalExtensionTicks = tpqn * 8; // Max 2 measures (8 beats) of sustain extension

            foreach (var track in midi.GetTrackChunks())
            {
                var pedalDownTicks = new Dictionary<int, long>();
                var pedalIntervals = new Dictionary<int, List<(long Start, long End)>>();
                for (int i = 0; i < 16; i++) pedalIntervals[i] = new List<(long, long)>();

                foreach (var te in track.GetTimedEvents())
                {
                    if (te.Event is ControlChangeEvent cc && cc.ControlNumber == 64)
                    {
                        int ch = cc.Channel;
                        if (cc.ControlValue >= 64)
                        {
                            if (!pedalDownTicks.ContainsKey(ch))
                            {
                                pedalDownTicks[ch] = te.Time;
                            }
                        }
                        else
                        {
                            if (pedalDownTicks.TryGetValue(ch, out var downTick))
                            {
                                pedalIntervals[ch].Add((downTick, te.Time));
                                pedalDownTicks.Remove(ch);
                            }
                        }
                    }
                }

                long trackEndTime = track.GetTimedEvents().LastOrDefault()?.Time ?? 0;
                foreach (var (ch, downTick) in pedalDownTicks)
                {
                    // A pedal that is never released was left on as a sequencer artifact.
                    // Cap unreleased sustain intervals to at most 2 measures (8 beats) instead of whole track.
                    long cappedEnd = Math.Min(trackEndTime, downTick + maxPedalExtensionTicks);
                    pedalIntervals[ch].Add((downTick, cappedEnd));
                }

                // Window overly long intervals (> 4 measures / 16 beats) to prevent runaway polyphony accumulation
                for (int ch = 0; ch < 16; ch++)
                {
                    for (int i = 0; i < pedalIntervals[ch].Count; i++)
                    {
                        var (start, end) = pedalIntervals[ch][i];
                        if (end - start > tpqn * 16)
                        {
                            pedalIntervals[ch][i] = (start, start + maxPedalExtensionTicks);
                        }
                    }
                }

                bool hasPedal = pedalIntervals.Values.Any(l => l.Count > 0);
                if (!hasPedal) continue;

                using var notesManager = track.ManageNotes();
                foreach (var group in notesManager.Objects.GroupBy(n => (int)n.Channel))
                {
                    int ch = group.Key;
                    if (ch == 9) continue; // Skip GM drum channel
                    if (!pedalIntervals.TryGetValue(ch, out var intervals) || intervals.Count == 0) continue;

                    // Restrict sustain pedal emulation strictly to Piano, Organ, and Chromatic Percussion (GM 0-23)!
                    // Guitars, basses, brass, woodwinds, strings, and vocals must never have artificial sustain pedal extension.
                    if (channelGmPrograms != null && channelGmPrograms.TryGetValue(ch, out var gmProg) && !IsPianoOrKeyboardInstrument(gmProg))
                    {
                        continue;
                    }

                    var notes = group.OrderBy(n => n.Time).ToList();
                    for (int i = 0; i < notes.Count; i++)
                    {
                        var note = notes[i];
                        long noteEnd = note.EndTime;

                        var matchingInterval = intervals.FirstOrDefault(inv => note.Time <= inv.End && noteEnd >= inv.Start && noteEnd < inv.End);
                        if (matchingInterval != default)
                        {
                            long newEnd = matchingInterval.End;

                            for (int j = i + 1; j < notes.Count; j++)
                            {
                                if (notes[j].NoteNumber == note.NoteNumber)
                                {
                                    if (notes[j].Time < newEnd)
                                    {
                                        newEnd = Math.Max(note.Time + 1, notes[j].Time - 1);
                                    }
                                    break;
                                }
                            }

                            if (newEnd - note.Time > maxPedalExtensionTicks)
                            {
                                newEnd = note.Time + maxPedalExtensionTicks;
                            }

                            if (newEnd > note.EndTime)
                            {
                                note.Length = newEnd - note.Time;
                            }
                        }
                    }
                }
            }
        }

        private static void ApplyReverbBoost(MidiFile midi)
        {
            var activeNoteChannels = midi.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<NoteOnEvent>()
                .Where(n => n.Velocity > 0)
                .Select(n => (int)n.Channel)
                .Distinct()
                .ToHashSet();

            var channelsWithReverb = midi.GetTrackChunks()
                .SelectMany(t => t.Events)
                .OfType<ControlChangeEvent>()
                .Where(cc => cc.ControlNumber == 91 && cc.ControlValue > 0)
                .Select(cc => (int)cc.Channel)
                .Distinct()
                .ToHashSet();

            var firstTrack = midi.GetTrackChunks().FirstOrDefault();
            if (firstTrack != null)
            {
                foreach (var ch in activeNoteChannels)
                {
                    if (!channelsWithReverb.Contains(ch))
                    {
                        byte reverbVal = ch == 15 ? (byte)32 : (byte)54;
                        firstTrack.Events.Insert(0, new ControlChangeEvent((SevenBitNumber)91, (SevenBitNumber)reverbVal)
                        {
                            Channel = (FourBitNumber)ch,
                            DeltaTime = 0
                        });
                    }
                }
            }
        }

        private static void ApplyTempoAdjustments(MidiFile midi, RemapperOptions options)
        {
            double mult = options.TempoMultiplier;
            if (options.AutoNormalizeSluggishTempo)
            {
                var tempoEvents = midi.GetTrackChunks()
                    .SelectMany(t => t.Events)
                    .OfType<SetTempoEvent>()
                    .ToList();

                if (tempoEvents.Count > 0)
                {
                    double avgUs = tempoEvents.Average(e => (double)e.MicrosecondsPerQuarterNote);
                    double avgBpm = 60_000_000.0 / avgUs;
                    if (avgBpm < 68.0)
                    {
                        mult = 2.0; // Double sluggish half-time tempo (e.g. 63 BPM -> 126 BPM)
                    }
                }
            }

            if (Math.Abs(mult - 1.0) > 0.001 && mult > 0)
            {
                foreach (var track in midi.GetTrackChunks())
                {
                    foreach (var evt in track.Events)
                    {
                        if (evt is SetTempoEvent st)
                        {
                            long newUs = (long)Math.Round(st.MicrosecondsPerQuarterNote / mult);
                            st.MicrosecondsPerQuarterNote = Math.Clamp(newUs, 100_000, 2_000_000);
                        }
                    }
                }
            }
        }

        private static void ApplyExpressionPedalFolding(MidiFile midi)
        {
            foreach (var track in midi.GetTrackChunks())
            {
                var currentCc7 = new Dictionary<int, byte>();
                var currentCc11 = new Dictionary<int, byte>();

                for (int ch = 0; ch < 16; ch++)
                {
                    currentCc7[ch] = 100;
                    currentCc11[ch] = 127;
                }

                using var mgr = track.ManageTimedEvents();
                foreach (var te in mgr.Objects)
                {
                    if (te.Event is ControlChangeEvent cc)
                    {
                        int ch = cc.Channel;
                        if (cc.ControlNumber == 7)
                        {
                            currentCc7[ch] = (byte)cc.ControlValue;
                            if (currentCc11[ch] != 127)
                            {
                                int folded = (int)Math.Round((double)currentCc7[ch] * currentCc11[ch] / 127.0);
                                cc.ControlValue = (SevenBitNumber)Math.Clamp(folded, 0, 127);
                            }
                        }
                        else if (cc.ControlNumber == 11)
                        {
                            currentCc11[ch] = (byte)cc.ControlValue;
                            int folded = (int)Math.Round((double)currentCc7[ch] * currentCc11[ch] / 127.0);
                            cc.ControlNumber = (SevenBitNumber)7;
                            cc.ControlValue = (SevenBitNumber)Math.Clamp(folded, 0, 127);
                        }
                    }
                }
            }
        }

        private static void ApplySetupPreRoll(MidiFile midi, int minPreRollTicks = 48)
        {
            long minNoteTime = long.MaxValue;

            foreach (var track in midi.GetTrackChunks())
            {
                using var mgr = track.ManageTimedEvents();
                foreach (var te in mgr.Objects)
                {
                    if (te.Event is NoteOnEvent noteOn && noteOn.Velocity > 0)
                    {
                        if (te.Time < minNoteTime)
                        {
                            minNoteTime = te.Time;
                        }
                    }
                }
            }

            if (minNoteTime < minPreRollTicks)
            {
                long shiftTicks = minPreRollTicks - minNoteTime;
                foreach (var track in midi.GetTrackChunks())
                {
                    using var mgr = track.ManageTimedEvents();
                    foreach (var te in mgr.Objects)
                    {
                        if (te.Event is NoteOnEvent or NoteOffEvent)
                        {
                            te.Time += shiftTicks;
                        }
                    }
                }
            }
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
