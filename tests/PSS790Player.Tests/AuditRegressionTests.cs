using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class AuditRegressionTests : IDisposable
    {
        private readonly string _tempRoot;

        public AuditRegressionTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "PSS_AuditReg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
            }
            catch { }
        }

        private sealed class FakeInputDevice : IInputDevice
        {
            public event EventHandler<MidiEventReceivedEventArgs>? EventReceived;
            public bool IsListeningForEvents { get; private set; }

            public void StartEventsListening() => IsListeningForEvents = true;
            public void StopEventsListening() => IsListeningForEvents = false;
            public void Dispose() { IsListeningForEvents = false; }

            public void SimulateNote() =>
                EventReceived?.Invoke(this, new MidiEventReceivedEventArgs(
                    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100)));
        }

        [Fact]
        public void Remapper_NativePassthrough_AppliesTransposeOctaveAndMissingPcOverrides()
        {
            // Native PSS-790 file: Channel 0 with native PC, Channel 1 with octave shift target,
            // Channel 2 with NO initial PC, and Chart 1 Drum note 45 on Ch 15.
            var nativeMidi = new MidiFile();
            var track0 = new TrackChunk();
            track0.Events.Add(new TextEvent(Remapper.Pss790MarkerText) { DeltaTime = 0 });
            track0.Events.Add(new ProgramChangeEvent((SevenBitNumber)Constants.Pss790Voices[15].MidiProgram) { DeltaTime = 0, Channel = (FourBitNumber)0 });
            track0.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { DeltaTime = 48, Channel = (FourBitNumber)0 });
            track0.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 120, Channel = (FourBitNumber)0 });
            nativeMidi.Chunks.Add(track0);

            var track1 = new TrackChunk();
            track1.Events.Add(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)85) { DeltaTime = 48, Channel = (FourBitNumber)1 });
            track1.Events.Add(new NoteOffEvent((SevenBitNumber)64, (SevenBitNumber)0) { DeltaTime = 120, Channel = (FourBitNumber)1 });
            track1.Events.Add(new NoteOnEvent((SevenBitNumber)48, (SevenBitNumber)80) { DeltaTime = 0, Channel = (FourBitNumber)2 });
            track1.Events.Add(new NoteOffEvent((SevenBitNumber)48, (SevenBitNumber)0) { DeltaTime = 120, Channel = (FourBitNumber)2 });
            track1.Events.Add(new NoteOnEvent((SevenBitNumber)45, (SevenBitNumber)100) { DeltaTime = 0, Channel = (FourBitNumber)15 });
            track1.Events.Add(new NoteOffEvent((SevenBitNumber)45, (SevenBitNumber)0) { DeltaTime = 120, Channel = (FourBitNumber)15 });
            nativeMidi.Chunks.Add(track1);

            var options = new RemapperOptions
            {
                TargetModel = "pss790",
                MasterTranspose = 5,
                ChannelOctaveShifts = new Dictionary<int, int> { [1] = -12 },
                ChannelVoiceOverrides = new Dictionary<int, int?> { [2] = 42 }
            };

            var result = Remapper.Remap(nativeMidi, options);
            var notes = result.GetNotes().ToList();

            // Ch 0: 60 + 5 = 65; Ch 1: 64 + 5 - 12 = 57; Chart 1 Drum Ch 15 remains 45
            Assert.Equal(65, (byte)notes.First(n => n.Channel == 0).NoteNumber);
            Assert.Equal(57, (byte)notes.First(n => n.Channel == 1).NoteNumber);
            Assert.Equal(45, (byte)notes.First(n => n.Channel == 15).NoteNumber);

            // Ch 0 native voice preserved
            var ch0Pc = result.GetTrackChunks().SelectMany(t => t.Events).OfType<ProgramChangeEvent>().First(p => p.Channel == 0);
            Assert.Equal(Constants.Pss790Voices[15].MidiProgram, (byte)ch0Pc.ProgramNumber);

            // Ch 2 missing PC was injected with Voice 42
            var ch2Pc = result.GetTrackChunks().SelectMany(t => t.Events).OfType<ProgramChangeEvent>().FirstOrDefault(p => p.Channel == 2);
            Assert.NotNull(ch2Pc);
            Assert.Equal(Constants.Pss790Voices[42].MidiProgram, (byte)ch2Pc!.ProgramNumber);
            Assert.True(Remapper.HasAlgorithmV2Marker(result, "pss790"));
        }

        [Fact]
        public void Remapper_CrossModelConversion_CleansOpposingMarkersAcrossAllTracks()
        {
            var src = new MidiFile();
            var track0 = new TrackChunk(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0 });
            track0.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 120, Channel = (FourBitNumber)0 });
            var track1 = new TrackChunk(new TextEvent(Remapper.Pss680RecordingMarkerText) { DeltaTime = 0 });
            src.Chunks.Add(track0);
            src.Chunks.Add(track1);

            var converted = Remapper.Remap(src, new RemapperOptions { TargetModel = "pss790" });

            Assert.False(Remapper.HasPss680Marker(converted));
            Assert.True(Remapper.HasAlgorithmV2Marker(converted, "pss790"));
        }

        [Fact]
        public async Task MidiRecorder_CountInRapidCancelAndRestart_DoesNotRevertActiveStateToIdle()
        {
            using var recorder = new MidiRecorder();
            var fakeInput = new FakeInputDevice();
            recorder.SetInputDeviceInstance(fakeInput, "FakeInput");

            // Start count-in 1 at slow BPM (beat ~1000ms), then immediately cancel & restart with immediate recording
            var firstTask = recorder.StartRecordingAsync(bpm: 60, countIn: true);
            await Task.Delay(30);
            recorder.StopRecording();

            var secondTask = recorder.StartRecordingAsync(bpm: 120, countIn: false);
            await Task.WhenAll(firstTask, secondTask);

            // Even after stale count-in unwinds, active recording stays in Recording state!
            Assert.Equal(RecorderState.Recording, recorder.State);

            recorder.StopRecording();
            Assert.Equal(RecorderState.Stopped, recorder.State);
        }

        [Fact]
        public void SecurityContainment_OmittedBaseDir_RejectsForeignPaths()
        {
            var foreignMidi = Path.Combine(_tempRoot, "foreign_rec.mid");
            var foreignSyx = Path.Combine(_tempRoot, "foreign_dump.syx");
            File.WriteAllBytes(foreignMidi, new byte[] { 0x4D, 0x54, 0x68, 0x64 });
            File.WriteAllBytes(foreignSyx, new byte[] { 0xF0, 0x43, 0xF7 });

            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            using var manager = new SysExManager(player, recorder);

            Assert.False(recorder.DeleteRecording(foreignMidi));
            Assert.True(File.Exists(foreignMidi));
            Assert.Throws<UnauthorizedAccessException>(() => recorder.RenameRecording(foreignMidi, "pwned.mid"));

            Assert.False(manager.DeleteDump(foreignSyx));
            Assert.True(File.Exists(foreignSyx));
            Assert.Throws<UnauthorizedAccessException>(() => manager.RenameDump(foreignSyx, "pwned.syx"));
        }

        [Fact]
        public async Task SysExManager_TransmitDumpCancelled_SetsIsCancelledFlag()
        {
            var syxPath = Path.Combine(_tempRoot, "cancel_test.syx");
            File.WriteAllBytes(syxPath, new byte[] { 0xF0, 0x43, 0x76, 0x00, 0xF7 });

            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            using var manager = new SysExManager(player, recorder);

            SysExTransmitEventArgs? lastArgs = null;
            manager.TransmitProgress += (_, e) => lastArgs = e;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            bool completed = await manager.TransmitDumpAsync(syxPath, packetDelayMs: 10, cancellationToken: cts.Token);

            Assert.False(completed);
            Assert.NotNull(lastArgs);
            Assert.True(lastArgs!.IsCompleted);
            Assert.True(lastArgs.IsCancelled);
        }

        [Fact]
        public async Task BatchConverter_ExecuteDualSyncAsync_ReportsMonotonicProgressAndDefersSourcePruning()
        {
            var srcDir = Path.Combine(_tempRoot, "GM_Src");
            var tgt790Dir = Path.Combine(_tempRoot, "PSS790_Out");
            var tgt680Dir = Path.Combine(_tempRoot, "PSS680_Out");
            var subDir = Path.Combine(srcDir, "Rock");
            var emptySubDir = Path.Combine(srcDir, "EmptyFolder");
            Directory.CreateDirectory(subDir);
            Directory.CreateDirectory(emptySubDir);

            // Create 2 small valid GM MIDI files
            for (int i = 1; i <= 2; i++)
            {
                var gm = new MidiFile(new TrackChunk(
                    new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0 },
                    new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0 },
                    new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 240, Channel = (FourBitNumber)0 }));
                gm.Write(Path.Combine(subDir, $"Track{i}.mid"));
            }

            var progressValues = new List<int>();
            var progress = new SyncTestProgress<(int Current, int Total, string FileName, string Action)>(p =>
            {
                int pct = p.Total > 0 ? (int)((double)p.Current / p.Total * 100) : 0;
                progressValues.Add(pct);
            });

            var res = await BatchConverter.ExecuteDualSyncAsync(srcDir, tgt790Dir, tgt680Dir, new LibrarySyncOptions
            {
                PruneEmptyDirectories = true
            }, progress: progress);

            Assert.Equal(4, res.ConvertedCount);
            Assert.True(File.Exists(Path.Combine(tgt790Dir, "Rock", "Track1.mid")));
            Assert.True(File.Exists(Path.Combine(tgt680Dir, "Rock", "Track2.mid")));
            Assert.False(Directory.Exists(emptySubDir)); // Pruned once at the end

            Assert.NotEmpty(progressValues);
            for (int i = 1; i < progressValues.Count; i++)
            {
                Assert.True(progressValues[i] >= progressValues[i - 1],
                    $"Progress regressed at index {i}: {progressValues[i - 1]} -> {progressValues[i]}");
            }
            Assert.Equal(100, progressValues[^1]);
        }

        private sealed class SyncTestProgress<T> : IProgress<T>
        {
            private readonly Action<T> _onReport;
            public SyncTestProgress(Action<T> onReport) => _onReport = onReport;
            public void Report(T value) => _onReport(value);
        }

        [Fact]
        public void FavoriteStore_PropagatesRenameMoveAndFolderRemoval_AndSupportsReload()
        {
            var storePath1 = Path.Combine(_tempRoot, "fav1.json");
            var storePath2 = Path.Combine(_tempRoot, "fav2.json");

            var oldSong = Path.Combine(AppPaths.GmMidiDir, "Rock", "Anthem.mid");
            var renamedSong = Path.Combine(AppPaths.GmMidiDir, "Rock", "Anthem_v2.mid");
            var oldFolder = Path.Combine(AppPaths.GmMidiDir, "Rock");
            var renamedFolder = Path.Combine(AppPaths.GmMidiDir, "ClassicRock");
            var movedSong = Path.Combine(AppPaths.GmMidiDir, "ClassicRock", "Anthem_v2.mid");

            var store = new FavoriteStore(storePath1);
            Assert.True(store.AddFavorite(oldSong));
            Assert.True(store.IsFavorite(oldSong));

            store.RenameFile(oldSong, renamedSong);
            Assert.False(store.IsFavorite(oldSong));
            Assert.True(store.IsFavorite(renamedSong));

            store.RenameFolder(oldFolder, renamedFolder);
            Assert.False(store.IsFavorite(renamedSong));
            Assert.True(store.IsFavorite(movedSong));

            store.RemoveFolder(renamedFolder);
            Assert.False(store.IsFavorite(movedSong));
            Assert.Empty(store.GetAllFavorites());

            File.WriteAllText(storePath2, JsonSerializer.Serialize(new[] { "Jazz/TakeFive.mid" }));
            store.Reload(storePath2);
            Assert.True(store.IsFavorite(Path.Combine(AppPaths.GmMidiDir, "Jazz", "TakeFive.mid")));
        }

        [Fact]
        public void IpcDtos_SerializeCamelCaseForRecordingsAndSysEx()
        {
            var camelOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            var rec = new RecordingTakeInfo
            {
                FileName = "Take1.mid",
                FilePath = "Recordings/Take1.mid",
                FileSizeFormatted = "4.2 KB",
                CreatedAtFormatted = "2026-11-25 09:00",
                DurationFormatted = "01:25",
                NoteCount = 42
            };
            var recJson = JsonSerializer.Serialize(rec, camelOptions);
            Assert.Contains("\"filePath\":", recJson);
            Assert.Contains("\"fileName\":", recJson);
            Assert.Contains("\"durationFormatted\":", recJson);
            Assert.Contains("\"noteCount\":42", recJson);

            var dump = new SysExDumpInfo
            {
                FileName = "Backup.syx",
                FilePath = "SysEx/Backup.syx",
                FileSizeFormatted = "8.0 KB",
                CreatedAtFormatted = "2026-11-25 09:00",
                PacketCount = 12,
                Manufacturer = "Yamaha",
                HexPreview = "0000: F0 43 F7"
            };
            var dumpJson = JsonSerializer.Serialize(dump, camelOptions);
            Assert.Contains("\"fileSizeFormatted\":", dumpJson);
            Assert.Contains("\"createdAtFormatted\":", dumpJson);
            Assert.Contains("\"hexPreview\":", dumpJson);
        }

        [Fact]
        public void MidiPlayer_ClearAllSoloAndSendActiveNotesOffFast_ExecuteSafely()
        {
            using var player = new MidiPlayer();
            player.SetChannelSolo(0, true);
            player.ClearAllSolo();
            var ex = Record.Exception(() => player.SendActiveNotesOffFast());
            Assert.Null(ex);
        }

        [Fact]
        public void BlazorHybrid_AuditHardeningSignatures_ArePresentAndConsistent()
        {
            var root = FindRepoSrcDir();
            Assert.NotNull(root);

            var interopPath = Path.Combine(root!, "wwwroot", "js", "studioInterop.js");
            Assert.True(File.Exists(interopPath), $"Expected {interopPath}");
            var js = File.ReadAllText(interopPath);
            Assert.Contains("calculateVectorWeights", js);
            Assert.Contains("initFmEnvelopeDrag", js);
            Assert.Contains("playMetronomeClick", js);
            Assert.Contains("feedVuMeter", js);
            Assert.Contains("registerGlobalShortcuts", js);

            var fmRazor = Path.Combine(root!, "Components", "SoundDesign", "FmVoiceDesigner.razor");
            Assert.True(File.Exists(fmRazor), $"Expected {fmRazor}");
            var fmContent = File.ReadAllText(fmRazor);
            Assert.Contains("ComputeEnvelopeGeometry", fmContent);
            Assert.Contains("Decay1Level", fmContent);

            var aboutRazor = Path.Combine(root!, "Components", "Modals", "AboutModal.razor");
            Assert.True(File.Exists(aboutRazor), $"Expected {aboutRazor}");
            var aboutContent = File.ReadAllText(aboutRazor);
            Assert.Contains("v3.0.0", aboutContent);
            Assert.Contains("Transport Actor", aboutContent);
            Assert.True(File.Exists(Path.Combine(root!, "Core", "TransportActor.cs")), "TransportActor.cs must exist.");
        }

        private static string? FindRepoSrcDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "PSS790Player");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
