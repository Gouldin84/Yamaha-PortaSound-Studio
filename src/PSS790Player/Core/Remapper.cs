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
        Pss790Native,
        Pss680Native
    }

    public enum PitchBendScaleMode
    {
        Auto,
        Preserve1to1,
        DivideBy6,
        Disable
    }

    public class RemapperOptions
    {
        public string TargetModel { get; set; } = "pss790"; // "pss790" or "pss680"
        public bool EnablePolyphonyGuard { get; set; } = true;
        public bool ScalePitchBend { get; set; } = true;
        public PitchBendScaleMode PitchBendMode { get; set; } = PitchBendScaleMode.Auto;
        public bool NeutralizeAcousticPitchBends { get; set; } = true; // Neutralizes micro pitch bends on acoustic instruments on PSS-790 hardware PCM
        public bool AutoCenterDanglingPitchBends { get; set; } = true; // Guarantees all channels return to pitch 8192 (center) at track end
        public bool EmulateSustainPedal { get; set; } = true; // Extends staccato note durations while CC 64 (Damper Pedal) is held on keyboards
        public bool FoldExpressionPedal { get; set; } = true; // Folds CC 11 (Expression) into CC 7 (Volume) for PSS-790 swells & balance
        public bool NormalizeAwmVelocities { get; set; } = true; // Maps velocities into PSS-790 sweet spot (36-118)
        public bool EnsureSetupPreRoll { get; set; } = true; // Guarantees a clean setup window before Note 1 so hardware configures voices first
        public bool DefaultReverbBoost { get; set; } = true; // Injects default CC 91 (Reverb Send) for melodic channels lacking reverb on PSS-790
        public bool FilterUnsupportedControllers { get; set; } = true;
        public bool ForceRemap { get; set; } = false;
        public double TempoMultiplier { get; set; } = 1.0;
        public bool AutoNormalizeSluggishTempo { get; set; } = false; // Doubles tempo if average BPM < 68 BPM
        public int MasterTranspose { get; set; } = 0; // Semitones (-12 to +12)
        public Dictionary<int, int> ChannelOctaveShifts { get; set; } = new(); // Channel (0-15) -> Semitone shift (-24 to +24)
        public Dictionary<int, int?> ChannelVoiceOverrides { get; set; } = new(); // Channel (0-15) -> VoiceNumber (0-104) or null (auto)
    }

    public class Remapper
    {
        public const string Pss790MarkerText = "PSS790_REMAPPED:v2";
        public const string Pss790RecordingMarkerText = "PSS790_RECORDING:v1";
        public const string Pss680MarkerText = "PSS680_REMAPPED:v2";
        public const string Pss680RecordingMarkerText = "PSS680_RECORDING:v1";

        private readonly record struct TimedValueEntry(long Time, int TrackIndex, int EventOrder, byte Value);
        private readonly record struct TimedBendRangeEntry(long Time, int TrackIndex, int EventOrder, int Semitones);

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

        public static bool HasPss680Marker(MidiFile midiFile)
        {
            foreach (var track in midiFile.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is TextEvent textEvent && (textEvent.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                        textEvent.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase)))
                        return true;
                    if (evt is MarkerEvent markerEvent && (markerEvent.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                          markerEvent.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }
            return false;
        }

        public static bool HasAlgorithmV2Marker(MidiFile midiFile, string targetModel = "pss790")
        {
            bool is680 = string.Equals(targetModel, "pss680", StringComparison.OrdinalIgnoreCase);
            string expected = is680 ? Pss680MarkerText : Pss790MarkerText;
            foreach (var track in midiFile.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is TextEvent t && t.Text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (evt is MarkerEvent m && m.Text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public static bool HasAlgorithmV2MarkerFile(string filePath, string targetModel = "pss790")
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                bool is680 = string.Equals(targetModel, "pss680", StringComparison.OrdinalIgnoreCase);
                byte[] markerBytes = System.Text.Encoding.ASCII.GetBytes(is680 ? Pss680MarkerText : Pss790MarkerText);
                byte[] fileBytes = File.ReadAllBytes(filePath);

                int searchLen = Math.Min(fileBytes.Length, 4096);
                for (int i = 0; i <= searchLen - markerBytes.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < markerBytes.Length; j++)
                    {
                        if (fileBytes[i + j] != markerBytes[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsXgMidiFile(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                return IsXgMidiBytes(bytes);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsXgMidiBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 14) return false;

            // Fast scan for:
            // 1. Yamaha XG SysEx: 0xF0 followed within 8 bytes by 0x43 [dev] 0x4C
            // 2. Bank Select CC 0 == 64 (XG SFX) or CC 0 == 126/127 (XG/GS Drums)
            for (int i = 0; i < bytes.Length - 4; i++)
            {
                if (bytes[i] == 0xF0)
                {
                    int maxLook = Math.Min(bytes.Length - 1, i + 8);
                    for (int j = i + 1; j < maxLook - 1; j++)
                    {
                        if (bytes[j] == 0x43 && bytes[j + 2] == 0x4C)
                            return true;
                    }
                }

                if ((bytes[i] & 0xF0) == 0xB0 && bytes[i + 1] == 0x00)
                {
                    byte ccVal = bytes[i + 2];
                    if (ccVal is 64 or 126 or 127)
                        return true;
                }
            }

            return false;
        }

        public static bool IsXgMidi(MidiFile midiFile)
        {
            foreach (var track in midiFile.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is NormalSysExEvent sysex && sysex.Data != null && sysex.Data.Length >= 3)
                    {
                        var d = sysex.Data;
                        if (d[0] == 0x43 && d[2] == 0x4C)
                            return true;
                    }
                    if (evt is ControlChangeEvent cc && (byte)cc.ControlNumber == 0)
                    {
                        byte val = (byte)cc.ControlValue;
                        if (val is 64 or 126 or 127)
                            return true;
                    }
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
                    if (evt is TextEvent textEvent && (textEvent.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase) ||
                                                       textEvent.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase)))
                        return true;
                    if (evt is MarkerEvent markerEvent && (markerEvent.Text.Contains("PSS790_RECORDING", StringComparison.OrdinalIgnoreCase) ||
                                                          markerEvent.Text.Contains("PSS680_RECORDING", StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }
            return false;
        }

        public static MidiFormat DetectMidiFormat(MidiFile midiFile)
        {
            // 1. Primary Check: Embedded Non-Destructive Marker in Text or Marker Events
            if (HasPss680Marker(midiFile))
            {
                return MidiFormat.Pss680Native;
            }

            if (HasPss790Marker(midiFile))
            {
                return MidiFormat.Pss790Native;
            }

            // 2. Conservative Heuristic Check for unmarked PSS native files:
            // Must have NO Ch 9 events, active Ch 15 notes, NO ProgramChange/PitchBend on Ch 15,
            // NO melodic instrument name on Ch 15's track, and ALL Ch 15 notes within the PSS percussion map.
            var usedChannels = new HashSet<int>();
            bool ch15HasProgramChange = false;
            bool ch15HasPitchBend = false;
            bool ch15HasMelodicText = false;
            int ch15NoteCount = 0;
            bool allCh15In790Map = true;
            bool allCh15In680Map = true;

            foreach (var track in midiFile.GetTrackChunks())
            {
                bool trackUsesCh15 = false;
                foreach (var evt in track.Events)
                {
                    if (evt is ChannelEvent ce)
                    {
                        usedChannels.Add(ce.Channel);
                        if (ce.Channel == 15)
                        {
                            trackUsesCh15 = true;
                            if (ce is ProgramChangeEvent)
                            {
                                ch15HasProgramChange = true;
                            }
                            else if (ce is PitchBendEvent)
                            {
                                ch15HasPitchBend = true;
                            }
                            else if (ce is NoteOnEvent noteOn && (byte)noteOn.Velocity > 0)
                            {
                                ch15NoteCount++;
                                byte note = (byte)noteOn.NoteNumber;
                                if (!Constants.Pss790Drums.ContainsKey(note))
                                {
                                    allCh15In790Map = false;
                                }
                                if (!Pss680Constants.DrumMap.ContainsKey(note))
                                {
                                    allCh15In680Map = false;
                                }
                            }
                        }
                    }
                }

                if (trackUsesCh15)
                {
                    foreach (var textEvt in track.Events.OfType<BaseTextEvent>())
                    {
                        if (InferGmProgramFromText(textEvt.Text).HasValue)
                        {
                            ch15HasMelodicText = true;
                            break;
                        }
                    }
                }
            }

            if (!usedChannels.Contains(9) &&
                ch15NoteCount > 0 &&
                !ch15HasProgramChange &&
                !ch15HasPitchBend &&
                !ch15HasMelodicText)
            {
                if (allCh15In680Map && !allCh15In790Map)
                    return MidiFormat.Pss680Native;
                if (allCh15In790Map)
                    return MidiFormat.Pss790Native;
            }

            return MidiFormat.GeneralMidi;
        }

        public static MidiFile Remap(MidiFile sourceMidi, RemapperOptions? options = null)
        {
            options ??= new RemapperOptions();
            var remapped = sourceMidi.Clone();
            var format = DetectMidiFormat(remapped);
            bool is680 = string.Equals(options.TargetModel, "pss680", StringComparison.OrdinalIgnoreCase);

            // If already converted for target model and not forced, run in Passthrough Mode
            if (((format == MidiFormat.Pss680Native && is680) || (format == MidiFormat.Pss790Native && !is680)) && !options.ForceRemap)
            {
                ApplyVoiceOverridesOnly(remapped, options.ChannelVoiceOverrides, options.TargetModel);
                EnsureMarkerEmbedded(remapped, options.TargetModel);
                return remapped;
            }

            // 0. Intelligent Tempo Normalization & Adjustment
            ApplyTempoAdjustments(remapped, options);

            // Normalize zero-velocity NoteOn events to standard NoteOff events to guarantee note pairing across all passes
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

            // 1. Pre-scan Drum Channels (Ch 9 + Bank 126/127 + XG SysEx), XG SFX Channels (Bank 64), and Used Channels
            var drumChannels = new HashSet<int> { 9 };
            var sfxChannels = new HashSet<int>();
            var usedChannels = new HashSet<int>();
            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is NoteOnEvent noteOn && noteOn.Velocity > 0)
                    {
                        usedChannels.Add(noteOn.Channel);
                    }
                    else if (evt is ControlChangeEvent cc && cc.ControlNumber == 0)
                    {
                        if (cc.ControlValue == 126 || cc.ControlValue == 127)
                        {
                            drumChannels.Add(cc.Channel);
                        }
                        else if (cc.ControlValue == 64)
                        {
                            sfxChannels.Add(cc.Channel);
                        }
                    }
                    else if (evt is NormalSysExEvent sysex && sysex.Data != null && sysex.Data.Length > 6)
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

            // 2. Pre-scan GM Programs per original channel (ProgramChange -> XG SysEx -> Track text inference)
            var channelGmPrograms = new Dictionary<int, byte>();
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

            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is NormalSysExEvent sysex && sysex.Data != null && sysex.Data.Length >= 7)
                    {
                        var d = sysex.Data;
                        if (d[0] == 0x43 && d[2] == 0x4C)
                        {
                            // Bulk Dump: 43 00 4C [sizeH] [sizeL] 08 [part] 00 ... [prog at offset 10]
                            if (d[1] == 0x00 && d[5] == 0x08 && d[7] == 0x00 && d.Length > 10)
                            {
                                int part = d[6];
                                byte prog = d[10];
                                if (part >= 0 && part < 16 && !channelGmPrograms.ContainsKey(part))
                                {
                                    channelGmPrograms[part] = prog;
                                }
                            }
                            // Parameter Change: 43 10 4C 08 [part] 03 [prog]
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

            var tracksWithNotes = new Dictionary<int, TrackChunk>();
            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                foreach (var evt in trackChunk.Events)
                {
                    if (evt is NoteOnEvent n && n.Velocity > 0 && !drumChannels.Contains(n.Channel))
                    {
                        tracksWithNotes.TryAdd((int)n.Channel, trackChunk);
                    }
                }
            }

            foreach (var (ch, tr) in tracksWithNotes)
            {
                if (!channelGmPrograms.ContainsKey(ch))
                {
                    byte? inferred = null;
                    foreach (var textEvt in tr.Events.OfType<BaseTextEvent>())
                    {
                        inferred = InferGmProgramFromText(textEvt.Text);
                        if (inferred.HasValue) break;
                    }
                    channelGmPrograms[ch] = inferred ?? 0;
                }
            }

            // 3. Virtual Sustain Pedal (CC 64) Emulation & Runaway Pedal Protection
            // Runs after drumChannels detection so secondary GS/XG drum channels are never extended.
            if (options.EmulateSustainPedal)
            {
                ApplySustainPedalEmulation(remapped, channelGmPrograms, drumChannels);
            }

            // 4. Volume & Expression Dynamics (runs BEFORE drum merging and BEFORE CC 11 is filtered)
            if (is680)
            {
                // PSS-680 ignores CC 7 in Mode 99: bake global Type-1 CC 7 + CC 11 timelines perceptually into [24..118],
                // cull zero-volume notes cleanly (NoteOn + NoteOff), and strip CC 7 / CC 11 from output.
                ApplyPss680VelocityBaking(remapped);
            }
            else if (options.FoldExpressionPedal)
            {
                // PSS-790 supports CC 7 natively: fold global Type-1 CC 11 swells into CC 7.
                ApplyExpressionPedalFolding(remapped);
            }

            // 5. Chronological RPN 0,0 (Pitch Bend Sensitivity) Timeline Scan (with RPN Null & NRPN isolation)
            var channelBendTimelines = ScanChronologicalPitchBendRanges(remapped);

            // 6. Unconditional Melodic Channel 15 Relocation
            int? relocatedMelodicChannel = null;
            if (usedChannels.Contains(15) && !drumChannels.Contains(15))
            {
                for (int ch = 0; ch < 15; ch++)
                {
                    if (!drumChannels.Contains(ch) && !usedChannels.Contains(ch))
                    {
                        relocatedMelodicChannel = ch;
                        break;
                    }
                }
                relocatedMelodicChannel ??= drumChannels.Cast<int?>().FirstOrDefault(d => d != 15);
                relocatedMelodicChannel ??= Enumerable.Range(0, 15).FirstOrDefault(c => !drumChannels.Contains(c));
            }

            // Build post-relocation GM program map for initial ProgramChange injection and PolyphonyOptimizer priority tiers
            var postRelocationGmPrograms = new Dictionary<int, byte>();
            foreach (var (origCh, gmProg) in channelGmPrograms)
            {
                if (drumChannels.Contains(origCh)) continue;
                int destCh = (origCh == 15 && relocatedMelodicChannel.HasValue) ? relocatedMelodicChannel.Value : origCh;
                postRelocationGmPrograms[destCh] = gmProg;
            }

            // Pre-scan low bass notes on PSS-680 so bassline contour isn't inverted by per-note folding
            var autoChannelOctaveShifts = new Dictionary<int, int>();
            if (is680)
            {
                var channelMinNote = new Dictionary<int, int>();
                foreach (var trackChunk in remapped.GetTrackChunks())
                {
                    foreach (var evt in trackChunk.Events)
                    {
                        if (evt is NoteOnEvent noteOn && noteOn.Velocity > 0 && !drumChannels.Contains(noteOn.Channel))
                        {
                            int ch = noteOn.Channel;
                            if (noteOn.NoteNumber < 24)
                            {
                                if (!channelMinNote.TryGetValue(ch, out var curMin) || noteOn.NoteNumber < curMin)
                                {
                                    channelMinNote[ch] = noteOn.NoteNumber;
                                }
                            }
                        }
                    }
                }

                foreach (var (ch, minN) in channelMinNote)
                {
                    int gmProg = channelGmPrograms.TryGetValue(ch, out var gp) ? gp : -1;
                    if (IsBassInstrument(gmProg))
                    {
                        int neededOctaves = Math.Min(1, (int)Math.Ceiling((24 - minN) / 12.0));
                        autoChannelOctaveShifts[ch] = neededOctaves * 12;
                    }
                }
            }

            // 7. Filter Unsupported Events & Remap Channels, Voices, Notes, and Pitch Bends
            double hardwareBendRangeSemitones = is680 ? 2.0 : 12.0;

            foreach (var trackChunk in remapped.GetTrackChunks())
            {
                var bentChannels = new HashSet<int>();

                using (var timedEventsMgr = trackChunk.ManageTimedEvents())
                {
                    timedEventsMgr.Objects.RemoveAll(te =>
                        te.Event is ChannelAftertouchEvent or NoteAftertouchEvent ||
                        te.Event is NormalSysExEvent or EscapeSysExEvent ||
                        (te.Event is ControlChangeEvent cc && options.FilterUnsupportedControllers &&
                            (is680
                                ? (cc.ControlNumber != 1 && cc.ControlNumber != 64)
                                : !Constants.AllowedControlChanges.Contains((byte)cc.ControlNumber))) ||
                        (te.Event is ProgramChangeEvent pc &&
                            (drumChannels.Contains(pc.Channel) || (pc.Channel == 15 && !relocatedMelodicChannel.HasValue)))
                    );
                }

                long absTime = 0;
                foreach (var evt in trackChunk.Events)
                {
                    absTime += evt.DeltaTime;

                    if (evt is ChannelEvent chEvt)
                    {
                        int originalChannel = chEvt.Channel;

                        if (originalChannel == 15 && relocatedMelodicChannel.HasValue)
                        {
                            chEvt.Channel = (FourBitNumber)relocatedMelodicChannel.Value;
                        }
                        else if (drumChannels.Contains(originalChannel))
                        {
                            chEvt.Channel = (FourBitNumber)15; // Relocate all drums to Channel 16
                        }

                        int currentChannel = chEvt.Channel;

                        if (currentChannel == 15)
                        {
                            if (chEvt is NoteOnEvent noteOn)
                            {
                                noteOn.NoteNumber = (SevenBitNumber)(is680
                                    ? Pss680Constants.TranslateDrumNote((byte)noteOn.NoteNumber)
                                    : TranslateDrumNote((byte)noteOn.NoteNumber));
                            }
                            else if (chEvt is NoteOffEvent noteOff)
                            {
                                noteOff.NoteNumber = (SevenBitNumber)(is680
                                    ? Pss680Constants.TranslateDrumNote((byte)noteOff.NoteNumber)
                                    : TranslateDrumNote((byte)noteOff.NoteNumber));
                            }
                        }
                        else
                        {
                            int gmProg = channelGmPrograms.TryGetValue(originalChannel, out var gp) ? gp : -1;

                            int transposeShift = options.MasterTranspose;
                            int octaveLookupCh = options.ChannelOctaveShifts.ContainsKey(originalChannel) ? originalChannel : currentChannel;
                            if (options.ChannelOctaveShifts.TryGetValue(octaveLookupCh, out var chShift))
                            {
                                transposeShift += chShift;
                            }
                            else if (autoChannelOctaveShifts.TryGetValue(originalChannel, out var autoShift))
                            {
                                transposeShift += autoShift;
                            }

                            if (chEvt is NoteOnEvent melodicNoteOn)
                            {
                                int shifted = melodicNoteOn.NoteNumber + transposeShift;
                                melodicNoteOn.NoteNumber = (SevenBitNumber)ClampMelodicNote((byte)Math.Clamp(shifted, 0, 127), gmProg, is680);
                                if (!is680 && options.NormalizeAwmVelocities && melodicNoteOn.Velocity > 0)
                                {
                                    byte v = (byte)melodicNoteOn.Velocity;
                                    int norm = Constants.AwmVelocityMin + (int)Math.Round((v - 1) * (double)(Constants.AwmVelocityMax - Constants.AwmVelocityMin) / 126.0);
                                    melodicNoteOn.Velocity = (SevenBitNumber)Math.Clamp(norm, 1, 127);
                                }
                            }
                            else if (chEvt is NoteOffEvent melodicNoteOff)
                            {
                                int shifted = melodicNoteOff.NoteNumber + transposeShift;
                                melodicNoteOff.NoteNumber = (SevenBitNumber)ClampMelodicNote((byte)Math.Clamp(shifted, 0, 127), gmProg, is680);
                            }

                            if (chEvt is ProgramChangeEvent pc)
                            {
                                int overrideLookupCh = options.ChannelVoiceOverrides.ContainsKey(originalChannel) ? originalChannel : currentChannel;
                                if (options.ChannelVoiceOverrides.TryGetValue(overrideLookupCh, out var overrideVoice) && overrideVoice.HasValue)
                                {
                                    if (is680)
                                    {
                                        pc.ProgramNumber = (SevenBitNumber)Math.Clamp(overrideVoice.Value, 0, 104);
                                    }
                                    else if (Constants.Pss790Voices.TryGetValue(overrideVoice.Value, out var voiceInfo))
                                    {
                                        pc.ProgramNumber = (SevenBitNumber)voiceInfo.MidiProgram;
                                    }
                                }
                                else
                                {
                                    byte progNum = (byte)pc.ProgramNumber;
                                    if (sfxChannels.Contains(originalChannel))
                                    {
                                        pc.ProgramNumber = (SevenBitNumber)TranslateXgSfxVoice(progNum, is680);
                                    }
                                    else if (is680)
                                    {
                                        int pssVoiceNum = Pss680Constants.GmToPss680VoiceMap.GetValueOrDefault(progNum, 3);
                                        pc.ProgramNumber = (SevenBitNumber)Math.Clamp(pssVoiceNum, 0, 99);
                                    }
                                    else
                                    {
                                        int pssVoiceNum = Constants.GmToPss790VoiceMap.GetValueOrDefault(progNum, 0);
                                        byte pssProg = Constants.Pss790Voices[pssVoiceNum].MidiProgram;
                                        pc.ProgramNumber = (SevenBitNumber)pssProg;
                                    }
                                }
                            }
                            else if (chEvt is PitchBendEvent pb)
                            {
                                bool isAcoustic = gmProg >= 0 && IsAcousticOrKeyboardInstrument((byte)gmProg);

                                if (options.NeutralizeAcousticPitchBends && isAcoustic && !is680)
                                {
                                    pb.PitchValue = 8192;
                                }
                                else if (options.PitchBendMode == PitchBendScaleMode.Disable)
                                {
                                    pb.PitchValue = 8192;
                                }
                                else if (options.ScalePitchBend)
                                {
                                    double scaleFactor;
                                    if (options.PitchBendMode == PitchBendScaleMode.Preserve1to1)
                                    {
                                        scaleFactor = 1.0;
                                    }
                                    else if (options.PitchBendMode == PitchBendScaleMode.DivideBy6)
                                    {
                                        scaleFactor = 1.0 / 6.0;
                                    }
                                    else // Auto
                                    {
                                        int? activeRange = LookupBendRangeAtTime(channelBendTimelines, originalChannel, absTime);
                                        if (activeRange.HasValue && activeRange.Value > 0)
                                        {
                                            scaleFactor = Math.Clamp(activeRange.Value / hardwareBendRangeSemitones, 0.05, 1.0);
                                        }
                                        else
                                        {
                                            scaleFactor = is680 ? 1.0 : Constants.PitchBendScaleFactor;
                                        }
                                    }

                                    int centered = pb.PitchValue - 8192;
                                    int scaled = (int)Math.Round(centered * scaleFactor);
                                    pb.PitchValue = (ushort)Math.Clamp(8192 + scaled, 0, 16383);
                                    bentChannels.Add(currentChannel);
                                }
                            }
                        }
                    }
                }

                // Auto-center dangling pitch bends before track end
                if (options.AutoCenterDanglingPitchBends && bentChannels.Count > 0)
                {
                    foreach (var ch in bentChannels)
                    {
                        var lastPb = trackChunk.Events.OfType<PitchBendEvent>().LastOrDefault(p => (int)p.Channel == ch);
                        if (lastPb != null && lastPb.PitchValue != 8192)
                        {
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

                                if (endIdx >= 0) trackChunk.Events.Insert(endIdx, centerPb);
                                else trackChunk.Events.Add(centerPb);
                            }
                        }
                    }
                }
            }

            // 8. Ensure all active melodic channels have an initial Program Change
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
                        byte pssProg = is680 ? (byte)3 : (byte)0;
                        int origLookupCh = (relocatedMelodicChannel.HasValue && ch == relocatedMelodicChannel.Value) ? 15 : ch;
                        int overrideKey = options.ChannelVoiceOverrides.ContainsKey(origLookupCh) ? origLookupCh : ch;

                        if (options.ChannelVoiceOverrides.TryGetValue(overrideKey, out var overrideVoice) && overrideVoice.HasValue)
                        {
                            if (is680)
                            {
                                pssProg = (byte)Math.Clamp(overrideVoice.Value, 0, 104);
                            }
                            else if (Constants.Pss790Voices.TryGetValue(overrideVoice.Value, out var ovInfo))
                            {
                                pssProg = ovInfo.MidiProgram;
                            }
                        }
                        else if (postRelocationGmPrograms.TryGetValue(ch, out var gmProg))
                        {
                            if (is680)
                            {
                                pssProg = (byte)Pss680Constants.GmToPss680VoiceMap.GetValueOrDefault(gmProg, 3);
                            }
                            else
                            {
                                int pssVoiceNum = Constants.GmToPss790VoiceMap.GetValueOrDefault(gmProg, 0);
                                if (Constants.Pss790Voices.TryGetValue(pssVoiceNum, out var vInfo))
                                {
                                    pssProg = vInfo.MidiProgram;
                                }
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

            // 9. Inject default Yamaha DSP Reverb (CC 91) on PSS-790
            if (!is680 && options.DefaultReverbBoost)
            {
                ApplyReverbBoost(remapped);
            }

            // 10. Run Smart Polyphony Optimizer with accurate pre-remap GM program priorities
            if (options.EnablePolyphonyGuard)
            {
                int threshold = is680 ? Pss680Constants.PolyphonyMode99 : Constants.PolyphonyGuardThreshold;
                var optimizer = new PolyphonyOptimizer(enabled: true, threshold: threshold, model: options.TargetModel);
                remapped = optimizer.Optimize(remapped, postRelocationGmPrograms);
            }

            // 11. Tempo-accurate 31.25 kbps serial MIDI controller thinning (preserving extremes and sweep endpoints)
            ApplyCcThinning(remapped);

            // 12. Setup pre-roll separation keeping tick-0 setup events at 0 while shifting notes and automation together
            if (options.EnsureSetupPreRoll)
            {
                ApplySetupPreRoll(remapped);
            }

            // 13. Embed v2 metadata marker safely via ManageTimedEvents
            EnsureMarkerEmbedded(remapped, options.TargetModel);

            return remapped;
        }

        private static Dictionary<int, List<TimedBendRangeEntry>> ScanChronologicalPitchBendRanges(MidiFile midi)
        {
            var rawEvents = new List<(long Time, int TrackIndex, int EventOrder, int Channel, byte ControlNumber, byte ControlValue)>();
            var tracks = midi.GetTrackChunks().ToList();

            for (int t = 0; t < tracks.Count; t++)
            {
                int order = 0;
                foreach (var te in tracks[t].GetTimedEvents())
                {
                    if (te.Event is ControlChangeEvent cc &&
                        (int)cc.ControlNumber is 6 or 98 or 99 or 100 or 101)
                    {
                        rawEvents.Add((te.Time, t, order, (int)cc.Channel, (byte)cc.ControlNumber, (byte)cc.ControlValue));
                    }
                    order++;
                }
            }

            rawEvents.Sort((a, b) =>
            {
                int cmp = a.Time.CompareTo(b.Time);
                if (cmp != 0) return cmp;
                cmp = a.TrackIndex.CompareTo(b.TrackIndex);
                if (cmp != 0) return cmp;
                return a.EventOrder.CompareTo(b.EventOrder);
            });

            var timelines = new Dictionary<int, List<TimedBendRangeEntry>>();
            var rpnMsb = new int[16];
            var rpnLsb = new int[16];
            var rpnActive = new bool[16];
            Array.Fill(rpnMsb, 127);
            Array.Fill(rpnLsb, 127);

            foreach (var e in rawEvents)
            {
                int ch = e.Channel;
                if (e.ControlNumber == 101)
                {
                    rpnMsb[ch] = e.ControlValue;
                    rpnActive[ch] = !(rpnMsb[ch] == 127 && rpnLsb[ch] == 127);
                }
                else if (e.ControlNumber == 100)
                {
                    rpnLsb[ch] = e.ControlValue;
                    rpnActive[ch] = !(rpnMsb[ch] == 127 && rpnLsb[ch] == 127);
                }
                else if (e.ControlNumber is 99 or 98)
                {
                    // Entering NRPN mode disables active RPN so NRPN Data Entry (CC 6) cannot corrupt pitch bend sensitivity
                    rpnActive[ch] = false;
                }
                else if (e.ControlNumber == 6)
                {
                    if (rpnActive[ch] && rpnMsb[ch] == 0 && rpnLsb[ch] == 0 && e.ControlValue > 0)
                    {
                        if (!timelines.TryGetValue(ch, out var list))
                        {
                            list = new List<TimedBendRangeEntry>();
                            timelines[ch] = list;
                        }
                        list.Add(new TimedBendRangeEntry(e.Time, e.TrackIndex, e.EventOrder, e.ControlValue));
                    }
                }
            }

            return timelines;
        }

        private static int? LookupBendRangeAtTime(Dictionary<int, List<TimedBendRangeEntry>> timelines, int channel, long time)
        {
            if (!timelines.TryGetValue(channel, out var list) || list.Count == 0)
            {
                return null;
            }

            int? result = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Time <= time) result = list[i].Semitones;
                else break;
            }
            return result;
        }

        private static void ApplyVoiceOverridesOnly(MidiFile midi, Dictionary<int, int?> overrides, string targetModel = "pss790")
        {
            if (overrides == null || overrides.Count == 0) return;
            bool is680 = string.Equals(targetModel, "pss680", StringComparison.OrdinalIgnoreCase);

            foreach (var track in midi.GetTrackChunks())
            {
                foreach (var evt in track.Events)
                {
                    if (evt is ProgramChangeEvent pc && pc.Channel != (FourBitNumber)15)
                    {
                        int ch = pc.Channel;
                        if (overrides.TryGetValue(ch, out var overrideVoice) && overrideVoice.HasValue)
                        {
                            if (is680)
                            {
                                pc.ProgramNumber = (SevenBitNumber)Math.Clamp(overrideVoice.Value, 0, 104);
                            }
                            else if (Constants.Pss790Voices.TryGetValue(overrideVoice.Value, out var voiceInfo))
                            {
                                pc.ProgramNumber = (SevenBitNumber)voiceInfo.MidiProgram;
                            }
                        }
                    }
                }
            }
        }

        private static void EnsureMarkerEmbedded(MidiFile midi, string targetModel = "pss790")
        {
            var firstTrack = midi.GetTrackChunks().FirstOrDefault();
            if (firstTrack == null)
            {
                firstTrack = new TrackChunk();
                midi.Chunks.Add(firstTrack);
            }

            bool is680 = string.Equals(targetModel, "pss680", StringComparison.OrdinalIgnoreCase);
            string markerText = is680 ? Pss680MarkerText : Pss790MarkerText;

            if (HasAlgorithmV2Marker(midi, targetModel))
            {
                return;
            }

            // Remove legacy markers using ManageTimedEvents so Track 0 timestamps never shift
            using (var mgr = firstTrack.ManageTimedEvents())
            {
                mgr.Objects.RemoveAll(te =>
                    (te.Event is TextEvent t && (t.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                 t.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase))) ||
                    (te.Event is MarkerEvent m && (m.Text.Contains("PSS790_REMAPPED", StringComparison.OrdinalIgnoreCase) ||
                                                   m.Text.Contains("PSS680_REMAPPED", StringComparison.OrdinalIgnoreCase))));
            }

            // Inserting a DeltaTime == 0 event at index 0 preserves all subsequent absolute timestamps
            firstTrack.Events.Insert(0, new TextEvent(markerText) { DeltaTime = 0 });
        }

        public static byte TranslateDrumNote(byte gmNote)
        {
            if (Constants.GmToPss790DrumMap.TryGetValue(gmNote, out var pssNote))
            {
                return pssNote;
            }

            if (gmNote is 24 or 25) return 49; // Snare Roll -> Snare Hi
            if (gmNote is 26) return 64;       // Finger Snap -> High Q / Rimshot
            if (gmNote is 27 or 28) return 64; // High Q / Slap -> Rimshot
            if (gmNote is 29 or 30) return 79; // Scratch Push/Pull -> Brash Squeeze
            if (gmNote is 31 or 32 or 33) return 75; // Sticks / Clicks -> Woodblock/Cowbell
            if (gmNote is 34) return 39;       // Metronome Bell -> Triangle Open
            if (gmNote is 82 or 83) return 58; // Shaker / Jingle Bell -> Maracas
            if (gmNote is 84 or 85) return 39; // Bell Tree / Castanets -> Triangle Open
            if (gmNote is 86 or 87) return 45; // Surdo -> Bass Drum Hi
            if (gmNote is 88 or 89) return 77; // Cuica Hi/Lo -> Cuica Hi
            if (gmNote is 90) return 37;       // Triangle Mute -> Triangle Mute
            if (gmNote is 91) return 39;       // Triangle Open -> Triangle Open

            if (gmNote < 35) return 44; // Bass drum lo
            if (gmNote > 81) return 58; // Default high percussion -> Maracas
            return 49;                  // Snare hi
        }

        public static byte TranslateXgSfxVoice(byte xgProgram, bool is680)
        {
            if (is680)
            {
                // Valid PSS-680 Manual p.40 FM voices suitable for XG SFX:
                // 97: LASER, 95: SIREN, 99: UFO
                return xgProgram switch
                {
                    >= 48 and <= 51 => 97,   // Machine Gun, Laser Gun, Explosion, Firework -> LASER (97)
                    >= 80 and <= 88 => 95,   // Phone, Door, Car, Alarm -> SIREN (95)
                    >= 90 and <= 92 => 99,   // Helicopter, Starship, Jet Plane -> UFO (99)
                    >= 93 and <= 127 => 97,  // Gunshot / impacts -> LASER (97)
                    _ => 99                  // Ambient / Cosmic / Wind FX -> UFO (99)
                };
            }
            else
            {
                int pssVoice = xgProgram switch
                {
                    >= 48 and <= 51 => 99,   // Laser Gun, Machine Gun, Explosion -> ORCHESTRA HIT (Voice 99)
                    >= 80 and <= 88 => 43,   // Phone, Door, Car, Siren -> GLOCKENSPIEL / SYNTH LEAD (Voice 43)
                    >= 90 and <= 92 => 15,   // Helicopter, Starship, Jet Plane -> DISTORTION GUITAR (Voice 15)
                    >= 93 and <= 127 => 99,  // Gunshot / etc -> ORCHESTRA HIT (Voice 99)
                    _ => 82                  // Wind, Stream, Bubble, Cosmic -> FANTASY (Voice 82)
                };
                return Constants.Pss790Voices[pssVoice].MidiProgram;
            }
        }

        public static bool IsPianoOrKeyboardInstrument(byte gmProgram)
        {
            return gmProgram <= 23;
        }

        public static bool IsAcousticOrKeyboardInstrument(byte gmProgram)
        {
            return (gmProgram >= 0 && gmProgram <= 23) ||
                   (gmProgram >= 40 && gmProgram <= 55) ||
                   (gmProgram >= 88 && gmProgram <= 103);
        }

        public static bool IsAliasingSensitiveAcousticVoice(byte gmProgram)
        {
            return (gmProgram >= 0 && gmProgram <= 7) ||
                   (gmProgram >= 16 && gmProgram <= 23) ||
                   (gmProgram >= 40 && gmProgram <= 55);
        }

        public static bool IsBassInstrument(int gmProg)
        {
            return (gmProg >= 32 && gmProg <= 39) || gmProg == 43 || gmProg == 58;
        }

        public static byte ClampMelodicNote(byte note, int gmProg, bool is680 = false)
        {
            bool isSensitive = !is680 && gmProg >= 0 && IsAliasingSensitiveAcousticVoice((byte)gmProg);
            byte maxNote = isSensitive ? (byte)88 : (byte)108;
            byte minNote = 24;

            while (note < minNote) note += 12;
            while (note > maxNote) note -= 12;
            return Math.Clamp(note, minNote, maxNote);
        }

        public static byte ClampMelodicNote(byte note) => ClampMelodicNote(note, -1, false);

        public static byte? InferGmProgramFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var lower = text.ToLowerInvariant();

            if (lower.Contains("bass") || lower.Contains("bss"))
            {
                if (lower.Contains("ac") || lower.Contains("acoustic") || lower.Contains("upright")) return 32;
                if (lower.Contains("slap")) return 36;
                if (lower.Contains("synth") || lower.Contains("syn")) return 38;
                if (lower.Contains("pick")) return 34;
                if (lower.Contains("fretless")) return 35;
                return 33;
            }
            if (lower.Contains("guitar") || lower.Contains("gtr"))
            {
                if (lower.Contains("dist") || lower.Contains("overdrive") || lower.Contains("od")) return 29;
                if (lower.Contains("mute")) return 28;
                if (lower.Contains("e ") || lower.Contains("electric") || lower.Contains("clean")) return 27;
                if (lower.Contains("nylon")) return 24;
                if (lower.Contains("jazz")) return 26;
                return 25;
            }
            if (lower.Contains("piano") || lower.Contains("pno") || lower.Contains("grand") || lower.Contains("intro"))
            {
                if (lower.Contains("honky") || lower.Contains("tack")) return 3;
                return 0;
            }
            if (lower.Contains("rhodes") || lower.Contains("electric piano") || lower.Contains("e.piano") || lower.Contains("e-piano") || lower.Contains("wurlitzer") || lower.Contains("wurli") || lower.StartsWith("ep ") || lower.EndsWith(" ep") || lower.Contains(" ep ") || lower == "ep")
            {
                return 4;
            }
            if (lower.Contains("clav") || lower.Contains("clavi")) return 7;
            if (lower.Contains("harpsi") || lower.Contains("harpsichord")) return 6;
            if (lower.Contains("organ") || lower.Contains("hammond") || lower.Contains("b3")) return 16;
            if (lower.Contains("accord") || lower.Contains("harmonica") || lower.Contains("bandoneon")) return 21;
            if (lower.Contains("vibes") || lower.Contains("vibraphone")) return 11;
            if (lower.Contains("marimba")) return 12;
            if (lower.Contains("xylophone") || lower.Contains("xylo")) return 13;
            if (lower.Contains("timpani")) return 47;
            if (lower.Contains("pizz") || lower.Contains("pizzicato")) return 45;
            if (lower.Contains("string") || lower.Contains("str ") || lower.Contains("str.") || lower.Contains("violin") || lower.Contains("viola") || lower.Contains("cello") || lower.Contains("contrabass") || lower.Contains("orch")) return 48;
            if (lower.Contains("choir") || lower.Contains("aah")) return 52;
            if (lower.Contains("vocal") || lower.Contains("voice") || lower.Contains("ooh")) return 54;
            if (lower.Contains("trumpet") || lower.Contains("brass") || lower.Contains("horn") || lower.Contains("trombone") || lower.Contains("tuba")) return 61;
            if (lower.Contains("sax")) return 66;
            if (lower.Contains("flute") || lower.Contains("pan") || lower.Contains("pipe") || lower.Contains("piccolo")) return 73;
            if (lower.Contains("oboe") || lower.Contains("clarinet") || lower.Contains("bassoon")) return 68;
            if (lower.Contains("bell") || lower.Contains("chime") || lower.Contains("glock") || lower.Contains("celesta")) return 9;
            if (lower.Contains("harp")) return 46;
            if (lower.Contains("pad") || lower.Contains("warm") || lower.Contains("sweep")) return 89;
            if (lower.Contains("lead") || lower.Contains("synth") || lower.Contains("syn ") || lower.Contains("syn.") || lower.Contains("saw") || lower.Contains("square") || lower.Contains("arp")) return 80;

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

        public static MidiHealthReport AnalyzeHealth(MidiFile originalMidi, MidiFile? remappedMidi = null, string? fileName = null, string model = "pss790")
        {
            return MidiHealthInspector.Analyze(originalMidi, remappedMidi, fileName, model);
        }

        public static void ApplySustainPedalEmulation(MidiFile midi, Dictionary<int, byte>? channelGmPrograms = null, HashSet<int>? drumChannels = null)
        {
            var effectiveDrums = drumChannels ?? new HashSet<int> { 9 };
            var timeDivision = midi.TimeDivision as TicksPerQuarterNoteTimeDivision;
            short tpqn = timeDivision?.TicksPerQuarterNote ?? 480;
            long maxPedalExtensionTicks = tpqn * 8; // Max 2 measures (8 beats)
            long runawayThresholdTicks = tpqn * 16; // > 4 measures considered runaway

            // Build global chronological CC 64 intervals per channel across all tracks (handles Type-1 split tracks)
            var tracks = midi.GetTrackChunks().ToList();
            var cc64Events = new List<(long Time, int TrackIndex, int Order, int Channel, byte Value)>();
            long globalEndTime = 0;

            for (int t = 0; t < tracks.Count; t++)
            {
                int order = 0;
                foreach (var te in tracks[t].GetTimedEvents())
                {
                    if (te.Time > globalEndTime) globalEndTime = te.Time;
                    if (te.Event is ControlChangeEvent cc && cc.ControlNumber == 64)
                    {
                        cc64Events.Add((te.Time, t, order, (int)cc.Channel, (byte)cc.ControlValue));
                    }
                    order++;
                }
            }

            if (cc64Events.Count == 0) return;

            cc64Events.Sort((a, b) =>
            {
                int cmp = a.Time.CompareTo(b.Time);
                if (cmp != 0) return cmp;
                cmp = a.TrackIndex.CompareTo(b.TrackIndex);
                if (cmp != 0) return cmp;
                return a.Order.CompareTo(b.Order);
            });

            var pedalDownTicks = new Dictionary<int, long>();
            var pedalIntervals = new Dictionary<int, List<(long Start, long End)>>();
            var runawayCapReleaseTicks = new Dictionary<int, List<long>>();
            for (int i = 0; i < 16; i++)
            {
                pedalIntervals[i] = new List<(long, long)>();
                runawayCapReleaseTicks[i] = new List<long>();
            }

            foreach (var e in cc64Events)
            {
                int ch = e.Channel;
                if (e.Value >= 64)
                {
                    if (!pedalDownTicks.ContainsKey(ch))
                    {
                        pedalDownTicks[ch] = e.Time;
                    }
                }
                else
                {
                    if (pedalDownTicks.TryGetValue(ch, out long downTick))
                    {
                        long endTick = e.Time;
                        if (endTick - downTick > runawayThresholdTicks)
                        {
                            long cappedEnd = downTick + maxPedalExtensionTicks;
                            pedalIntervals[ch].Add((downTick, cappedEnd));
                            runawayCapReleaseTicks[ch].Add(cappedEnd);
                        }
                        else
                        {
                            pedalIntervals[ch].Add((downTick, endTick));
                        }
                        pedalDownTicks.Remove(ch);
                    }
                }
            }

            foreach (var (ch, downTick) in pedalDownTicks)
            {
                long cappedEnd = Math.Min(globalEndTime, downTick + maxPedalExtensionTicks);
                pedalIntervals[ch].Add((downTick, cappedEnd));
                runawayCapReleaseTicks[ch].Add(cappedEnd);
            }

            // Track channels whose sustain was baked into note durations so their raw CC 64 can be stripped cleanly
            var bakedKeyboardChannels = new HashSet<int>();
            for (int ch = 0; ch < 16; ch++)
            {
                if (effectiveDrums.Contains(ch)) continue;
                if (pedalIntervals[ch].Count == 0) continue;

                bool isKeyboard = channelGmPrograms == null ||
                                  !channelGmPrograms.TryGetValue(ch, out byte gmProg) ||
                                  IsPianoOrKeyboardInstrument(gmProg);
                if (isKeyboard)
                {
                    bakedKeyboardChannels.Add(ch);
                }
            }

            // 1. Extend staccato note durations on keyboard channels
            foreach (var track in tracks)
            {
                using var notesManager = track.ManageNotes();
                foreach (var group in notesManager.Objects.GroupBy(n => (int)n.Channel))
                {
                    int ch = group.Key;
                    if (!bakedKeyboardChannels.Contains(ch)) continue;

                    var intervals = pedalIntervals[ch];
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

            // 2. Strip CC 64 from baked keyboard channels & drums (prevents double-sustain / stuck pedals),
            // and inject explicit CC 64 = 0 release caps for any runaway pedals on non-keyboard channels.
            foreach (var track in tracks)
            {
                using var mgr = track.ManageTimedEvents();
                mgr.Objects.RemoveAll(te =>
                    te.Event is ControlChangeEvent cc &&
                    cc.ControlNumber == 64 &&
                    (bakedKeyboardChannels.Contains(cc.Channel) || effectiveDrums.Contains(cc.Channel)));
            }

            var firstTrack = tracks.FirstOrDefault();
            if (firstTrack != null)
            {
                var capsToInsert = new List<TimedEvent>();
                for (int ch = 0; ch < 16; ch++)
                {
                    if (bakedKeyboardChannels.Contains(ch) || effectiveDrums.Contains(ch)) continue;
                    foreach (long releaseTick in runawayCapReleaseTicks[ch])
                    {
                        capsToInsert.Add(new TimedEvent(
                            new ControlChangeEvent((SevenBitNumber)64, (SevenBitNumber)0) { Channel = (FourBitNumber)ch },
                            releaseTick));
                    }
                }

                if (capsToInsert.Count > 0)
                {
                    using var mgr = firstTrack.ManageTimedEvents();
                    foreach (var cap in capsToInsert)
                    {
                        mgr.Objects.Add(cap);
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
                        mult = 2.0;
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

        private static (List<TimedValueEntry>[] Cc7, List<TimedValueEntry>[] Cc11) CollectGlobalVolumeTimelines(IReadOnlyList<TrackChunk> tracks)
        {
            var cc7 = new List<TimedValueEntry>[16];
            var cc11 = new List<TimedValueEntry>[16];
            for (int ch = 0; ch < 16; ch++)
            {
                cc7[ch] = new List<TimedValueEntry>();
                cc11[ch] = new List<TimedValueEntry>();
            }

            for (int t = 0; t < tracks.Count; t++)
            {
                int order = 0;
                foreach (var te in tracks[t].GetTimedEvents())
                {
                    if (te.Event is ControlChangeEvent cc)
                    {
                        if (cc.ControlNumber == 7)
                        {
                            cc7[cc.Channel].Add(new TimedValueEntry(te.Time, t, order, (byte)cc.ControlValue));
                        }
                        else if (cc.ControlNumber == 11)
                        {
                            cc11[cc.Channel].Add(new TimedValueEntry(te.Time, t, order, (byte)cc.ControlValue));
                        }
                    }
                    order++;
                }
            }

            static int CompareEntry(TimedValueEntry a, TimedValueEntry b)
            {
                int cmp = a.Time.CompareTo(b.Time);
                if (cmp != 0) return cmp;
                cmp = a.TrackIndex.CompareTo(b.TrackIndex);
                if (cmp != 0) return cmp;
                return a.EventOrder.CompareTo(b.EventOrder);
            }

            for (int ch = 0; ch < 16; ch++)
            {
                cc7[ch].Sort(CompareEntry);
                cc11[ch].Sort(CompareEntry);
            }

            return (cc7, cc11);
        }

        private static byte LookupControllerAtTime(List<TimedValueEntry> timeline, long time, byte defaultValue)
        {
            if (timeline.Count == 0) return defaultValue;

            // Binary search for last entry with entry.Time <= time
            int lo = 0;
            int hi = timeline.Count - 1;
            int idx = -1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (timeline[mid].Time <= time)
                {
                    idx = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return idx >= 0 ? timeline[idx].Value : defaultValue;
        }

        private static void ApplyExpressionPedalFolding(MidiFile midi)
        {
            var tracks = midi.GetTrackChunks().ToList();
            var (globalCc7, globalCc11) = CollectGlobalVolumeTimelines(tracks);

            foreach (var track in tracks)
            {
                using var mgr = track.ManageTimedEvents();
                foreach (var te in mgr.Objects)
                {
                    if (te.Event is ControlChangeEvent cc)
                    {
                        int ch = cc.Channel;
                        if (cc.ControlNumber == 7)
                        {
                            byte activeCc11 = LookupControllerAtTime(globalCc11[ch], te.Time, 127);
                            if (activeCc11 != 127)
                            {
                                int folded = (int)Math.Round((double)(byte)cc.ControlValue * activeCc11 / 127.0);
                                cc.ControlValue = (SevenBitNumber)Math.Clamp(folded, 0, 127);
                            }
                        }
                        else if (cc.ControlNumber == 11)
                        {
                            byte activeCc7 = LookupControllerAtTime(globalCc7[ch], te.Time, 100);
                            int folded = (int)Math.Round((double)activeCc7 * (byte)cc.ControlValue / 127.0);
                            cc.ControlNumber = (SevenBitNumber)7;
                            cc.ControlValue = (SevenBitNumber)Math.Clamp(folded, 0, 127);
                        }
                    }
                }
            }
        }

        private static void ApplyPss680VelocityBaking(MidiFile midi)
        {
            var tracks = midi.GetTrackChunks().ToList();
            var (globalCc7, globalCc11) = CollectGlobalVolumeTimelines(tracks);

            // Step 1: Bake global Type-1 CC 7 & CC 11 into paired Notes (removing both NoteOn & NoteOff when culled)
            foreach (var track in tracks)
            {
                using var notesMgr = track.ManageNotes();
                var notesToCull = new HashSet<Note>();

                foreach (var note in notesMgr.Objects)
                {
                    int ch = note.Channel;
                    byte rawV = (byte)note.Velocity;
                    byte cc7 = LookupControllerAtTime(globalCc7[ch], note.Time, 100);
                    byte cc11 = LookupControllerAtTime(globalCc11[ch], note.Time, 127);

                    if (rawV == 0 || cc7 == 0 || cc11 == 0)
                    {
                        notesToCull.Add(note);
                        continue;
                    }

                    // Calibrated sweet spot mapping for 2-op FM YM3420
                    double eff = rawV * (cc7 / 127.0) * (cc11 / 127.0);

                    if (eff < 0.5)
                    {
                        notesToCull.Add(note);
                        continue;
                    }

                    // Exact calibrated 2-op FM velocity range: [24..118]
                    double clampedEff = Math.Clamp(eff, 1.0, 127.0);
                    int scaled = 24 + (int)Math.Round((clampedEff / 127.0) * (118 - 24));
                    note.Velocity = (SevenBitNumber)Math.Clamp(scaled, 24, 118);
                }

                if (notesToCull.Count > 0)
                {
                    notesMgr.Objects.RemoveAll(n => notesToCull.Contains(n));
                }
            }

            // Step 2: Strip ignored CC 7 and CC 11 events from PSS-680 output to conserve 31.25 kbps serial MIDI bandwidth
            foreach (var track in tracks)
            {
                using var mgr = track.ManageTimedEvents();
                mgr.Objects.RemoveAll(te => te.Event is ControlChangeEvent cc && (cc.ControlNumber == 7 || cc.ControlNumber == 11));
            }
        }

        private static void ApplyCcThinning(MidiFile midi)
        {
            const double MinIntervalMs = 18.0;
            var tempoMap = midi.GetTempoMap();

            foreach (var track in midi.GetTrackChunks())
            {
                using var mgr = track.ManageTimedEvents();
                var allEvents = mgr.Objects.ToList();
                var eventsToRemove = new HashSet<TimedEvent>();

                // Group continuous controller streams by (Channel, ControllerKey) where PitchBend uses key -1
                var streams = new Dictionary<(int Channel, int Key), List<(TimedEvent Event, double TimeMs, int Value, bool IsExtreme)>>();

                foreach (var te in allEvents)
                {
                    if (te.Event is ControlChangeEvent cc && (cc.ControlNumber == 1 || cc.ControlNumber == 7 || cc.ControlNumber == 11))
                    {
                        var streamKey = ((int)cc.Channel, (int)cc.ControlNumber);
                        if (!streams.TryGetValue(streamKey, out var list))
                        {
                            list = new List<(TimedEvent, double, int, bool)>();
                            streams[streamKey] = list;
                        }
                        int val = (byte)cc.ControlValue;
                        double ms = TimeConverter.ConvertTo<MetricTimeSpan>(te.Time, tempoMap).TotalMilliseconds;
                        list.Add((te, ms, val, val == 0 || val == 127));
                    }
                    else if (te.Event is PitchBendEvent pb)
                    {
                        var streamKey = ((int)pb.Channel, -1);
                        if (!streams.TryGetValue(streamKey, out var list))
                        {
                            list = new List<(TimedEvent, double, int, bool)>();
                            streams[streamKey] = list;
                        }
                        int val = pb.PitchValue;
                        double ms = TimeConverter.ConvertTo<MetricTimeSpan>(te.Time, tempoMap).TotalMilliseconds;
                        list.Add((te, ms, val, val == 0 || val == 8192 || val == 16383));
                    }
                }

                foreach (var stream in streams.Values)
                {
                    if (stream.Count <= 1) continue;

                    double lastKeptMs = stream[0].TimeMs;
                    int lastKeptVal = stream[0].Value;

                    for (int i = 1; i < stream.Count; i++)
                    {
                        var cur = stream[i];

                        // Always drop consecutive duplicate values
                        if (cur.Value == lastKeptVal)
                        {
                            eventsToRemove.Add(cur.Event);
                            continue;
                        }

                        // Always preserve extremes (0, 127, 8192, 16383)
                        if (cur.IsExtreme)
                        {
                            lastKeptMs = cur.TimeMs;
                            lastKeptVal = cur.Value;
                            continue;
                        }

                        // Preserve if elapsed time meets minimum serial MIDI spacing
                        if ((cur.TimeMs - lastKeptMs) >= MinIntervalMs)
                        {
                            lastKeptMs = cur.TimeMs;
                            lastKeptVal = cur.Value;
                            continue;
                        }

                        // Preserve the final settling value of a ramp (last event in stream or followed by >= 18ms gap)
                        bool isRampEnd = (i == stream.Count - 1) || ((stream[i + 1].TimeMs - cur.TimeMs) >= MinIntervalMs);
                        if (isRampEnd)
                        {
                            lastKeptMs = cur.TimeMs;
                            lastKeptVal = cur.Value;
                            continue;
                        }

                        eventsToRemove.Add(cur.Event);
                    }
                }

                if (eventsToRemove.Count > 0)
                {
                    mgr.Objects.RemoveAll(te => eventsToRemove.Contains(te));
                }
            }
        }

        private static void ApplySetupPreRoll(MidiFile midi, int minPreRollTicks = 48)
        {
            long minNoteTime = long.MaxValue;

            foreach (var track in midi.GetTrackChunks())
            {
                foreach (var te in track.GetTimedEvents())
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
                        if (minNoteTime == 0)
                        {
                            if (te.Time > 0 || te.Event is NoteOnEvent or NoteOffEvent)
                            {
                                te.Time += shiftTicks;
                            }
                        }
                        else if (te.Time >= minNoteTime)
                        {
                            te.Time += shiftTicks;
                        }
                    }
                }
            }
        }

        public static void SaveRemapped(MidiFile remapped, string destinationPath, string? targetModel = null)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var resolvedModel = targetModel ?? (destinationPath.Contains("PSS-680", StringComparison.OrdinalIgnoreCase) || destinationPath.Contains("PSS680", StringComparison.OrdinalIgnoreCase) ? "pss680" : "pss790");
            EnsureMarkerEmbedded(remapped, resolvedModel);
            remapped.Write(destinationPath, overwriteFile: true);
        }
    }
}
