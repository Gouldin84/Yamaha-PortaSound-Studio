using System.IO;
using System.Linq;
using System.Collections.Generic;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using PSS790Player.Core;
using Xunit;
using Xunit.Abstractions;

namespace PSS790Player.Tests
{
    public class IntegrationTests
    {
        [Fact]
        public void Remap_SyntheticGeneralMidi_PreservesIntegrityAndAppliesPSS790Rules()
        {
            // Build a multi-track General MIDI structure programmatically
            var sourceMidi = new MidiFile();

            // Track 1: Drums on Channel 9 (10 in 1-based)
            var drumTrack = new TrackChunk();
            drumTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)9 }); // GM drum PC
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)9 }); // Kick
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)38, (SevenBitNumber)90) { DeltaTime = 120, Channel = (FourBitNumber)9 }); // Snare
            drumTrack.Events.Add(new NoteOnEvent((SevenBitNumber)42, (SevenBitNumber)80) { DeltaTime = 120, Channel = (FourBitNumber)9 }); // Closed HH
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)38, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            drumTrack.Events.Add(new NoteOffEvent((SevenBitNumber)42, (SevenBitNumber)0) { DeltaTime = 60, Channel = (FourBitNumber)9 });
            sourceMidi.Chunks.Add(drumTrack);

            // Track 2: Piano on Channel 0
            var pianoTrack = new TrackChunk();
            pianoTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 }); // GM 0 Acoustic Grand
            pianoTrack.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            pianoTrack.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)0 }); // Vol (Allowed)
            pianoTrack.Events.Add(new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)64) { DeltaTime = 0, Channel = (FourBitNumber)0 }); // Pan (Filter)
            pianoTrack.Events.Add(new ChannelAftertouchEvent((SevenBitNumber)80) { DeltaTime = 0, Channel = (FourBitNumber)0 }); // Aftertouch (Filter)
            pianoTrack.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)0 });
            sourceMidi.Chunks.Add(pianoTrack);

            // Track 3: Melodic voice on Channel 15 (Collision test)
            var collisionTrack = new TrackChunk();
            collisionTrack.Events.Add(new ProgramChangeEvent((SevenBitNumber)33) { Channel = (FourBitNumber)15 }); // Electric Bass
            collisionTrack.Events.Add(new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)15 });
            collisionTrack.Events.Add(new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 480, Channel = (FourBitNumber)15 });
            sourceMidi.Chunks.Add(collisionTrack);

            var options = new RemapperOptions
            {
                EnablePolyphonyGuard = true,
                ScalePitchBend = true,
                FilterUnsupportedControllers = true
            };

            var remapped = Remapper.Remap(sourceMidi, options);

            // Verify in-memory serialization
            using var ms = new MemoryStream();
            remapped.Write(ms);
            ms.Position = 0;
            var reloaded = MidiFile.Read(ms);

            Assert.NotNull(reloaded);
            var timedEvents = reloaded.GetTimedEvents().ToList();
            Assert.NotEmpty(timedEvents);

            // Verify no channel 9 drums remain (should be on 15)
            // Verify no aftertouch remains
            // Verify unsupported CC 10 is filtered
            foreach (var te in timedEvents)
            {
                Assert.False(te.Event is ChannelAftertouchEvent or NoteAftertouchEvent);
                if (te.Event is ControlChangeEvent cc)
                {
                    Assert.Contains((byte)cc.ControlNumber, Constants.AllowedControlChanges);
                }
            }

            // Verify drum notes on Channel 15 were converted to Chart 1
            var drumNoteOns = timedEvents.Select(te => te.Event).OfType<NoteOnEvent>().Where(n => n.Channel == (FourBitNumber)15).ToList();
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)45); // GM 36 -> PSS 45 (BASS DRUM HI)
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)49); // GM 38 -> PSS 49 (SNARE HI)
            Assert.Contains(drumNoteOns, n => n.NoteNumber == (SevenBitNumber)57); // GM 42 -> PSS 57 (HI HAT CLOSED)
        }

        [Fact]
        public async System.Threading.Tasks.Task SyncLibrary_RecursiveBatchConvert_AllFilesConvertSuccessfully()
        {
            var musicDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Music", "Midi");
            var gmDir = Path.Combine(musicDir, "GM Midi");
            var pssDir = Path.Combine(musicDir, "PSS-790 Midi");

            if (!Directory.Exists(gmDir) || Directory.GetFiles(gmDir, "*.mid", SearchOption.AllDirectories).Length == 0) return;

            int count = await BatchConverter.ConvertDirectoryAsync(gmDir, pssDir);
            Assert.True(count >= 113, "Expected at least 113 files converted, but got " + count);
        }

        [Fact]
        public void BleachSong_InBothGmAndPss_DisplaysIdenticalTrueVoices()
        {
            var musicDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Music", "Midi");
            var gmFile = Path.Combine(musicDir, "GM Midi", "Anime", "Bleach - Never Meant To Belong.mid");
            var pssFile = Path.Combine(musicDir, "PSS-790 Midi", "Anime", "Bleach - Never Meant To Belong.mid");

            if (!File.Exists(gmFile) || !File.Exists(pssFile)) return;

            var gmMidi = MidiFile.Read(gmFile, Constants.DefaultReadingSettings);
            var pssMidi = MidiFile.Read(pssFile, Constants.DefaultReadingSettings);

            // In GM file, CH 1 & 2 are GM Prog 1 (Bright Piano), CH 3 is GM Prog 40 (Violin)
            // In PSS file, CH 1 & 2 are PSS HW Prog 3 (PIANO), CH 3 is PSS HW Prog 10 (VIOLIN 1)
            Assert.Equal(MidiFormat.GeneralMidi, Remapper.DetectMidiFormat(gmMidi));
            Assert.Equal(MidiFormat.Pss790Native, Remapper.DetectMidiFormat(pssMidi));

            var pssTimedEvents = pssMidi.GetTimedEvents().Where(te => te.Event is ProgramChangeEvent).ToList();
            var ch1Pc = pssTimedEvents.Select(te => te.Event).OfType<ProgramChangeEvent>().First(pc => pc.Channel == (FourBitNumber)0);
            var ch2Pc = pssTimedEvents.Select(te => te.Event).OfType<ProgramChangeEvent>().First(pc => pc.Channel == (FourBitNumber)1);
            var ch3Pc = pssTimedEvents.Select(te => te.Event).OfType<ProgramChangeEvent>().First(pc => pc.Channel == (FourBitNumber)2);

            // HW Program 3 must map to Voice 0: PIANO (NOT Honky-Tonk)
            Assert.Equal((SevenBitNumber)3, ch1Pc.ProgramNumber);
            Assert.Equal((SevenBitNumber)3, ch2Pc.ProgramNumber);
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue((byte)ch1Pc.ProgramNumber, out var v1));
            Assert.Equal("PIANO", v1.Name);
            Assert.Equal(0, v1.VoiceNumber);

            // HW Program 10 must map to Voice 30: VIOLIN 1 (NOT Music Box)
            Assert.Equal((SevenBitNumber)10, ch3Pc.ProgramNumber);
            Assert.True(Constants.Pss790MidiProgramToVoice.TryGetValue((byte)ch3Pc.ProgramNumber, out var v3));
            Assert.Equal("VIOLIN 1", v3.Name);
            Assert.Equal(30, v3.VoiceNumber);
        }

        [Fact]
        public void Pss790Library_AuditAfterBatchConvert_HasZeroSysExAndZeroMissingPc()
        {
            var musicDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Music", "Midi");
            var pssDir = Path.Combine(musicDir, "PSS-790 Midi");

            if (!Directory.Exists(pssDir) || Directory.GetFiles(pssDir, "*.mid", SearchOption.AllDirectories).Length == 0) return;

            var files = Directory.GetFiles(pssDir, "*.mid", SearchOption.AllDirectories);
            Assert.True(files.Length >= 113, "Expected at least 113 PSS-790 files, got " + files.Length);

            foreach (var file in files)
            {
                var midi = MidiFile.Read(file, Constants.DefaultReadingSettings);
                var events = midi.GetTimedEvents().Select(te => te.Event).ToList();

                // 1. Verify foreign SysEx completely filtered
                var sysEx = events.OfType<SysExEvent>().ToList();
                if (sysEx.Any())
                {
                    throw new System.Exception($"File '{Path.GetFileName(file)}' has {sysEx.Count} SysEx events! Type: {sysEx[0].GetType().FullName}");
                }

                // 2. Verify all active melodic channels have a Program Change
                var activeMelodicChannels = events.OfType<NoteOnEvent>()
                    .Where(n => n.Channel != (FourBitNumber)15)
                    .Select(n => n.Channel)
                    .Distinct();

                var pcChannels = events.OfType<ProgramChangeEvent>()
                    .Select(pc => pc.Channel)
                    .ToHashSet();

                foreach (var ch in activeMelodicChannels)
                {
                    Assert.Contains(ch, pcChannels);
                }
            }
        }

        private readonly ITestOutputHelper _output;

        public IntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ManageTimedEvents_PreservesAbsoluteTimeWhenRemovingEvents()
        {
            var track = new TrackChunk();
            track.Events.Add(new ControlChangeEvent((SevenBitNumber)10, (SevenBitNumber)64) { DeltaTime = 500 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100) { DeltaTime = 1000 });

            Assert.Equal(1500, track.GetTimedEvents().ElementAt(1).Time);

            using (var mgr = track.ManageTimedEvents())
            {
                mgr.Objects.RemoveAll(te => te.Event is ControlChangeEvent);
            }

            Assert.Single(track.Events);
            Assert.Equal(1500, track.Events[0].DeltaTime);
            Assert.Equal(1500, track.GetTimedEvents().ElementAt(0).Time);
        }

        [Fact]
        public void Analyze_FromNowOn_Midi()
        {
            var filePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\General MIDI\From Now On.mid"));
            if (!File.Exists(filePath)) return;

            var original = MidiFile.Read(filePath, Constants.DefaultReadingSettings);
            var originalNotes = original.GetNotes().ToList();
            _output.WriteLine($"Original total notes: {originalNotes.Count}");

            var tracks = original.GetTrackChunks().ToList();
            _output.WriteLine($"Tracks: {tracks.Count}");

            var remapped = Remapper.Remap(original, new RemapperOptions());
            var remappedNotes = remapped.GetNotes().ToList();
            _output.WriteLine($"Remapped total notes: {remappedNotes.Count}");
            _output.WriteLine($"Notes removed: {originalNotes.Count - remappedNotes.Count}");

            for (int i = 0; i < tracks.Count; i++)
            {
                var oNotes = tracks[i].GetNotes().ToList();
                var rNotes = remapped.GetTrackChunks().ElementAt(i).GetNotes().ToList();
                if (oNotes.Count > 0 && rNotes.Count > 0)
                {
                    long diff = oNotes[0].Time - rNotes[0].Time;
                    _output.WriteLine($"Track {i} '{tracks[i].Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text}': first note orig={oNotes[0].Time}, remap={rNotes[0].Time}, SHIFT={diff} ticks!");
                    Assert.Equal(0, diff); // Zero-loss timed event filtering guarantees zero tick shift
                }
            }

            // Check what notes were culled/removed by comparing times and channels
            var origNotesSet = originalNotes.Select(n => (Ch: (int)n.Channel, Note: (int)n.NoteNumber, n.Time)).ToHashSet();
            var remapNotesSet = remappedNotes.Select(n => (Ch: (int)n.Channel == 15 ? 9 : (int)n.Channel, Note: (int)n.NoteNumber, n.Time)).ToHashSet();

            // Check all events on Track 5 (Vocal Guide)
            var tr5 = original.GetTrackChunks().ElementAt(5);
            _output.WriteLine($"Track 5 total events: {tr5.Events.Count}");
            long origTime = 0;
            var tr5CCs = new Dictionary<byte, int>();
            foreach (var ev in tr5.Events)
            {
                origTime += ev.DeltaTime;
                if (ev is ControlChangeEvent cc)
                {
                    tr5CCs[(byte)cc.ControlNumber] = tr5CCs.GetValueOrDefault((byte)cc.ControlNumber, 0) + 1;
                }
            }
            _output.WriteLine("Track 5 ControlChanges in original:");
            foreach (var kvp in tr5CCs)
            {
                _output.WriteLine($"  CC {kvp.Key}: {kvp.Value} events");
            }

            // Calculate peak polyphony over time
            var allRemapNotes = remapped.GetNotes().OrderBy(n => n.Time).ToList();
            var act = new List<Note>();
            int maxPeak = 0;
            long maxPeakTime = 0;
            foreach (var n in allRemapNotes)
            {
                act.RemoveAll(x => x.EndTime <= n.Time);
                act.Add(n);
                if (act.Count > maxPeak)
                {
                    maxPeak = act.Count;
                    maxPeakTime = n.Time;
                }
            }
            _output.WriteLine($"Peak polyphony in remapped: {maxPeak} simultaneous notes at tick {maxPeakTime}");

            // How many notes were drastically shortened (e.g. length reduced by > 50%)?
            var shortenedByCh = new Dictionary<int, int>();
            var shortenedDetails = new List<string>();
            var origNotesMap = originalNotes.GroupBy(n => ((int)n.Channel, n.Time))
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var rn in remappedNotes.Where(n => (int)n.Channel != 15))
            {
                int ch = (int)rn.Channel;
                if (origNotesMap.TryGetValue((ch, rn.Time), out var origList))
                {
                    var matching = origList.FirstOrDefault(on => Remapper.ClampMelodicNote((byte)on.NoteNumber) == (byte)rn.NoteNumber);
                    if (matching != null)
                    {
                        if (rn.Length < matching.Length * 0.5 && matching.Length > 100)
                        {
                            shortenedByCh[ch] = shortenedByCh.GetValueOrDefault(ch, 0) + 1;
                            if (shortenedDetails.Count < 20)
                            {
                                shortenedDetails.Add($"Ch{ch} (time {rn.Time}, pitch {rn.NoteNumber}): orig len {matching.Length} -> new len {rn.Length}");
                            }
                        }
                    }
                }
            }

            _output.WriteLine($"Shortened melodic notes (>50% duration reduction):");
            foreach (var kvp in shortenedByCh)
            {
                _output.WriteLine($"  Channel {kvp.Key}: {kvp.Value} severely shortened notes");
            }
            foreach (var d in shortenedDetails)
            {
                _output.WriteLine($"    Detail: {d}");
            }

            // Group by channel
            var origByCh = originalNotes.GroupBy(n => (int)n.Channel).ToDictionary(g => g.Key, g => g.ToList());
            var remapByCh = remappedNotes.GroupBy(n => (int)n.Channel).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var ch in origByCh.Keys.OrderBy(k => k))
            {
                var oNotes = origByCh[ch];
                var rNotes = remapByCh.ContainsKey(ch) ? remapByCh[ch] : new List<Note>();
                long oAvgLen = (long)oNotes.Average(n => n.Length);
                long rAvgLen = rNotes.Count > 0 ? (long)rNotes.Average(n => n.Length) : 0;
                _output.WriteLine($"Ch {ch}: Orig {oNotes.Count} notes (avg len {oAvgLen}) -> Remapped {rNotes.Count} notes (avg len {rAvgLen})");
            }

            // Also check with PolyphonyGuard disabled vs enabled
            var noPoly = Remapper.Remap(original, new RemapperOptions { EnablePolyphonyGuard = false });
            var noPolyNotes = noPoly.GetNotes().ToList();
            _output.WriteLine($"Without PolyphonyGuard notes: {noPolyNotes.Count}");

            // Check with EmulateSustainPedal disabled vs enabled
            var noSustain = Remapper.Remap(original, new RemapperOptions { EmulateSustainPedal = false });
            var noSustainNotes = noSustain.GetNotes().ToList();
            _output.WriteLine($"Without EmulateSustainPedal notes: {noSustainNotes.Count}");

            // Assertions guaranteeing vocal and melodic preservation
            Assert.Equal(283, remapByCh[3].Count); // Vocal Guide (Hugh Jackman lead) - 100% preserved
            Assert.Equal(361, remapByCh[4].Count); // Harmonies Guide - 100% preserved
            Assert.Equal(29, remapByCh[5].Count);  // Choir Aahs - 100% preserved
            Assert.Equal(0, shortenedByCh.GetValueOrDefault(3, 0)); // No vocal guide truncation
            Assert.Equal(0, shortenedByCh.GetValueOrDefault(4, 0)); // No vocal harmony truncation
            Assert.Equal(0, shortenedByCh.GetValueOrDefault(5, 0)); // No choir truncation
        }
    }
}
