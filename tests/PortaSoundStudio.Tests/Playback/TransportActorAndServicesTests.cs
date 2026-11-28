using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using PSS790Player.Core;
using PSS790Player.Models;
using PSS790Player.Services;
using Xunit;

namespace PSS790Player.Tests
{
    public class ActorAndServicesTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _gmDir;
        private readonly string _pss790Dir;
        private readonly string _pss680Dir;

        public ActorAndServicesTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PSS_ActorService_Tests_" + Guid.NewGuid().ToString("N"));
            _gmDir = Path.Combine(_tempDir, "GM Midi");
            _pss790Dir = Path.Combine(_tempDir, "PSS-790 Midi");
            _pss680Dir = Path.Combine(_tempDir, "PSS-680 Midi");
            Directory.CreateDirectory(_gmDir);
            Directory.CreateDirectory(_pss790Dir);
            Directory.CreateDirectory(_pss680Dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public async Task TransportActor_SerializesConcurrentMutations_AndAllowsReentrancy()
        {
            using var actor = new TransportActor("TestActor");
            int counter = 0;
            int maxObservedConcurrency = 0;
            int activeWorkers = 0;

            var tasks = Enumerable.Range(0, 64).Select(i => actor.InvokeAsync(() =>
            {
                int now = Interlocked.Increment(ref activeWorkers);
                if (now > maxObservedConcurrency) maxObservedConcurrency = now;

                // Nested re-entrant call from inside actor thread must not deadlock
                int inner = actor.Invoke(() => ++counter);
                Assert.True(inner >= 1);

                Interlocked.Decrement(ref activeWorkers);
            })).ToArray();

            var all = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Equal(all, completed);
            await all;

            Assert.Equal(64, counter);
            Assert.Equal(1, maxObservedConcurrency);
        }

        [Fact]
        public async Task MidiPlayer_ActorAsyncOperations_RapidLoadSeekPlayStop_CompletesCleanly()
        {
            using var player = new MidiPlayer();
            var mockDev = new ThreadSafeMockDevice();
            await player.SetOutputDeviceAsync(null);
            player.SetOutputDevice(mockDev);

            var midi = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            var trk = new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)12) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)96) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 480 }
            );
            midi.Chunks.Add(trk);

            for (int i = 0; i < 8; i++)
            {
                await player.LoadMidiAsync(midi);
                await player.PlayAsync();
                await player.SeekAsync(TimeSpan.FromMilliseconds(100));
                await player.PauseAsync();
                await player.StopAsync();
            }

            Assert.False(player.IsPlaying);
            Assert.False(player.IsPaused);
            Assert.Equal(TimeSpan.Zero, player.CurrentPosition);
        }

        [Fact]
        public void ChannelStateExtractor_AnalyzesGmAndOverridesAndCrossModelCleanly()
        {
            var orig = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            orig.Chunks.Add(new TrackChunk(
                new SequenceTrackNameEvent("Electric Bass"),
                new NoteOnEvent((SevenBitNumber)40, (SevenBitNumber)90) { Channel = (FourBitNumber)1, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)40, (SevenBitNumber)0) { Channel = (FourBitNumber)1, DeltaTime = 240 }
            ));
            orig.Chunks.Add(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)9, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 120 }
            ));

            var remapped790 = Remapper.Remap(orig, new RemapperOptions { TargetModel = "pss790" });
            var overrides = new Dictionary<int, int?> { [1] = 75 };
            var octaves = new Dictionary<int, int> { [1] = 12 };

            var channels790 = ChannelStateExtractor.Analyze(orig, remapped790, MidiFormat.GeneralMidi, "pss790", isGmPassthrough: false, overrides, octaves);
            Assert.Equal(16, channels790.Count);
            Assert.True(channels790[1].HasNotes);
            Assert.True(channels790[1].IsOverridden);
            Assert.Equal(75, channels790[1].PssVoice);
            Assert.Equal(12, channels790[1].OctaveShift);
            Assert.True(channels790[15].IsDrum);
            Assert.False(channels790[9].IsDrum);

            var channels680 = ChannelStateExtractor.Analyze(orig, remapped790, MidiFormat.GeneralMidi, "pss680", isGmPassthrough: false, new Dictionary<int, int?>(), new Dictionary<int, int>());
            Assert.True(channels680[15].IsDrum);
            Assert.Equal(99, channels680[15].PssVoice);
        }

        [Fact]
        public void LibraryService_BuildSnapshot_BuildsNestedTreeAndCounts()
        {
            var gameDir = Path.Combine(_gmDir, "Arcade");
            Directory.CreateDirectory(gameDir);
            var gmSong = Path.Combine(gameDir, "OutRun.mid");
            new MidiFile(new TrackChunk(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100))).Write(gmSong);

            var pssSong = Path.Combine(_pss680Dir, "Original.mid");
            new MidiFile(new TrackChunk(new NoteOnEvent((SevenBitNumber)64, (SevenBitNumber)100))).Write(pssSong);

            var store = new AppStateStore();
            using var libService = new LibraryService(store);
            var snapshot = libService.BuildSnapshot(_gmDir, _pss790Dir, _pss680Dir);

            Assert.Equal(1, snapshot.Gm.Count);
            Assert.Single(snapshot.Gm.SubFolders);
            Assert.Equal("Arcade", snapshot.Gm.SubFolders[0].Name);
            Assert.Equal("OutRun.mid", snapshot.Gm.SubFolders[0].Files[0].FileName);

            Assert.Equal(0, snapshot.Pss790.Count);
            Assert.Equal(1, snapshot.Pss680.Count);
            Assert.Single(snapshot.Pss680.Files);
            Assert.Equal(2, snapshot.AllFiles.Count);
        }

        [Fact]
        public void SoundDesignService_CalculateVectorWeights_ClampsRadiusAndSumsToUnity()
        {
            var center = SoundDesignService.CalculateVectorWeights(0, 0);
            Assert.Equal(0.25, center.wA, 3);
            Assert.Equal(0.25, center.wB, 3);
            Assert.Equal(0.25, center.wC, 3);
            Assert.Equal(0.25, center.wD, 3);

            var extreme = SoundDesignService.CalculateVectorWeights(3.0, 4.0);
            double sum = extreme.wA + extreme.wB + extreme.wC + extreme.wD;
            Assert.Equal(1.0, sum, 6);
            Assert.True(extreme.wA > 0 && extreme.wB > 0);
            Assert.Equal(0.0, extreme.wC, 6);
            Assert.Equal(0.0, extreme.wD, 6);
        }

        [Fact]
        public void FactoryPresets_CategorizesFeaturedFmAndVectorPatches()
        {
            Assert.Equal("Acoustic Keyboards (Featured)", FactoryPresets.GetFmCategory("Concert Grand Piano"));
            Assert.Equal("Chiptune & Sega Genesis", FactoryPresets.GetFmCategory("Sky Sanctuary Crystalline Lead"));
            Assert.Equal("Custom & Imported Patches", FactoryPresets.GetFmCategory("User Custom Patch"));
            Assert.Equal("Game & Arcade Morph", FactoryPresets.GetVectorCategory("Sky Sanctuary Harp & Crystal"));
            Assert.Equal("Custom & User Presets", FactoryPresets.GetVectorCategory("Brand New Vector"));
            Assert.Equal(100, FactoryPresets.BuildVoiceCatalog("pss790").Count);
            Assert.Equal(105, FactoryPresets.BuildVoiceCatalog("pss680").Count);
        }

        [Fact]
        public void RecorderService_ComputeInboundChordDisplay_RecognizesTriadsAndInversions()
        {
            var store = new AppStateStore();
            using var devices = new HardwareDeviceService(store);
            using var lib = new LibraryService(store);
            using var playback = new PlaybackCoordinator(store, devices, lib);
            using var recorder = new RecorderService(store, devices, playback, lib);

            // Empty
            var empty = recorder.ComputeInboundChordDisplay();
            Assert.Equal("STANDBY", empty.ChordName);

            // C Major triad (C4, E4, G4)
            store.ActiveInboundNotes[60] = (0, 100);
            store.ActiveInboundNotes[64] = (0, 100);
            store.ActiveInboundNotes[67] = (0, 100);
            var cMaj = recorder.ComputeInboundChordDisplay();
            Assert.Equal("C Maj", cMaj.ChordName);

            // First inversion C/E (E3, G3, C4)
            store.ActiveInboundNotes.Clear();
            store.ActiveInboundNotes[52] = (0, 100);
            store.ActiveInboundNotes[55] = (0, 100);
            store.ActiveInboundNotes[60] = (0, 100);
            var cOverE = recorder.ComputeInboundChordDisplay();
            Assert.Equal("C Maj / E", cOverE.ChordName);
        }

        [Fact]
        public void AppStateStore_ScopedChanged_PropagatesExactScopeAndManagesToasts()
        {
            var store = new AppStateStore();
            var scopes = new List<StateScope>();
            store.ScopedChanged += scope => scopes.Add(scope);

            store.Mutate(s => s.ActiveTab = StudioTab.Recorder, StateScope.Shell);
            store.Mutate(s => s.LibraryFilter = "gm", StateScope.Library);

            Assert.Equal(2, scopes.Count);
            Assert.Equal(StateScope.Shell, scopes[0]);
            Assert.Equal(StateScope.Library, scopes[1]);
            Assert.Equal(StudioTab.Recorder, store.ActiveTab);

            store.AddToast("Model switched", ToastSeverity.Success);
            Assert.Single(store.Toasts);
            Guid toastId = store.Toasts[0].Id;

            store.RemoveToast(toastId);
            Assert.Empty(store.Toasts);
        }

        [Fact]
        public void PlaybackCoordinator_GetFilteredSongList_HidesInactiveHardwareModel()
        {
            File.WriteAllBytes(Path.Combine(_gmDir, "gm_track.mid"), new byte[] { 0x4D, 0x54, 0x68, 0x64 });
            File.WriteAllBytes(Path.Combine(_pss790Dir, "790_track.mid"), new byte[] { 0x4D, 0x54, 0x68, 0x64 });
            File.WriteAllBytes(Path.Combine(_pss680Dir, "680_track.mid"), new byte[] { 0x4D, 0x54, 0x68, 0x64 });

            var store = new AppStateStore();
            var library = new LibraryService(store);
            var snapshot = library.BuildSnapshot(_gmDir, _pss790Dir, _pss680Dir);
            store.Mutate(s =>
            {
                s.Library = snapshot;
                s.LibraryFilter = "all";
            }, StateScope.Library);

            using var devices = new HardwareDeviceService(store);
            using var playback = new PlaybackCoordinator(store, devices, library);

            playback.SetActiveModel("pss790");
            var visibleOn790 = playback.GetFilteredSongList();
            Assert.Contains(visibleOn790, f => f.Format == "gm");
            Assert.Contains(visibleOn790, f => f.Format == "pss790");
            Assert.DoesNotContain(visibleOn790, f => f.Format == "pss680");

            playback.SetActiveModel("pss680");
            var visibleOn680 = playback.GetFilteredSongList();
            Assert.Contains(visibleOn680, f => f.Format == "gm");
            Assert.Contains(visibleOn680, f => f.Format == "pss680");
            Assert.DoesNotContain(visibleOn680, f => f.Format == "pss790");
        }

        [Fact]
        public async Task PlaybackCoordinator_LoadFileWithSettleDelay_PopulatesChannelsImmediatelyAndCancelsOnStop()
        {
            var trackPath = Path.Combine(_gmDir, "settle_test.mid");
            var midi = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            midi.Chunks.Add(new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)4) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 960 }
            ));
            midi.Write(trackPath);

            var store = new AppStateStore();
            using var library = new LibraryService(store);
            using var devices = new HardwareDeviceService(store);
            using var mockDev = new ThreadSafeMockDevice();
            devices.Player.SetOutputDevice(mockDev);

            using var playback = new PlaybackCoordinator(store, devices, library);

            // Start loading with 800ms settle delay so test has ample margin before cancelling
            var loadTask = playback.LoadFileAsync(trackPath, autoPlay: true, settleDelayMs: 800);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (store.LoadedSong == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.NotNull(store.LoadedSong);
            Assert.Equal("settle_test.mid", store.LoadedSong!.FileName);
            Assert.True(store.DurationSeconds > 0);

            // Cancelling via Stop() during settle must clear countdown and keep playback stopped
            playback.Stop();
            await loadTask;

            Assert.False(store.IsPreparingPlayback);
            Assert.False(store.IsPlaying);

            // Seek bounds clamping
            playback.Seek(store.DurationSeconds * 0.5);
            Assert.True(store.CurrentSeconds > 0);
            playback.Seek(9999);
            Assert.InRange(store.CurrentSeconds, 0, store.DurationSeconds + 0.01);
        }

        [Fact]
        public async Task PlaybackCoordinator_Pss790LiveLoad_EnablesDrumTranslationCcFilterAndPolyphonyLimitDropdown()
        {
            var trackPath = Path.Combine(_gmDir, "pss790_live_opts.mid");
            var midi = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
            midi.Chunks.Add(new TrackChunk(
                new ProgramChangeEvent((SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new ControlChangeEvent((SevenBitNumber)74, (SevenBitNumber)64) { Channel = (FourBitNumber)0, DeltaTime = 0 }, // unsupported CC 74
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)90) { Channel = (FourBitNumber)0, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { Channel = (FourBitNumber)0, DeltaTime = 480 },
                // GM drum hit on Ch 10 (index 9) -> must translate to Ch 16 (index 15) for live PSS-790 playback
                new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)100) { Channel = (FourBitNumber)9, DeltaTime = 0 },
                new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { Channel = (FourBitNumber)9, DeltaTime = 240 }
            ));
            midi.Write(trackPath);

            var store = new AppStateStore();
            using var library = new LibraryService(store);
            using var devices = new HardwareDeviceService(store);
            using var mockDev = new ThreadSafeMockDevice();
            devices.Player.SetOutputDevice(mockDev);
            using var playback = new PlaybackCoordinator(store, devices, library);
            playback.SetActiveModel("pss790");

            await playback.LoadFileAsync(trackPath, autoPlay: false);

            Assert.NotNull(store.LoadedSong);
            Assert.Equal(22, store.LoadedSong!.PolyphonyLimit790);
            Assert.Equal(0, store.LoadedSong.Channels[0].PssVoice); // Ch 0 PIANO (00)
            Assert.Contains(store.LoadedSong.Channels, c => c.Channel == 15 && c.HasNotes);

            playback.SetPolyphonyLimit790(28);
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (store.LoadedSong?.PolyphonyLimit790 != 28 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
            Assert.Equal(28, store.LoadedSong!.PolyphonyLimit790);
        }

        [Fact]
        public void LibraryService_ResolvePreferredFavorites_PrefersActiveHardwareModelAndCollapsedGmDefault()
        {
            var store = new AppStateStore();
            Assert.DoesNotContain("cat:gm", store.ExpandedFolders);
            Assert.Contains("cat:fav", store.ExpandedFolders);
            Assert.Contains("cat:pss790", store.ExpandedFolders);
            Assert.Contains("cat:pss680", store.ExpandedFolders);

            var gmBoth = new MidiFileItem { FileName = "Theme.mid", RelativePath = "Anime/Theme.mid", FullPath = Path.Combine(_gmDir, "Anime", "Theme.mid"), Format = "gm", IsFavorite = true };
            var p790Both = new MidiFileItem { FileName = "Theme.mid", RelativePath = "Anime/Theme.mid", FullPath = Path.Combine(_pss790Dir, "Anime", "Theme.mid"), Format = "pss790", IsFavorite = true };
            var p680Both = new MidiFileItem { FileName = "Theme.mid", RelativePath = "Anime/Theme.mid", FullPath = Path.Combine(_pss680Dir, "Anime", "Theme.mid"), Format = "pss680", IsFavorite = true };
            var gmOnly = new MidiFileItem { FileName = "Unsynced.mid", RelativePath = "Pop/Unsynced.mid", FullPath = Path.Combine(_gmDir, "Pop", "Unsynced.mid"), Format = "gm", IsFavorite = true };

            var allFiles = new List<MidiFileItem> { gmBoth, gmOnly, p790Both, p680Both };

            var favs790 = LibraryService.ResolvePreferredFavorites(allFiles, "pss790");
            Assert.Equal(2, favs790.Count);
            Assert.Contains(favs790, f => f.FullPath == p790Both.FullPath);
            Assert.DoesNotContain(favs790, f => f.FullPath == gmBoth.FullPath);
            Assert.DoesNotContain(favs790, f => f.FullPath == p680Both.FullPath);
            Assert.Contains(favs790, f => f.FullPath == gmOnly.FullPath);

            var favs680 = LibraryService.ResolvePreferredFavorites(allFiles, "pss680");
            Assert.Equal(2, favs680.Count);
            Assert.Contains(favs680, f => f.FullPath == p680Both.FullPath);
            Assert.DoesNotContain(favs680, f => f.FullPath == gmBoth.FullPath);
            Assert.DoesNotContain(favs680, f => f.FullPath == p790Both.FullPath);
            Assert.Contains(favs680, f => f.FullPath == gmOnly.FullPath);

            store.Mutate(s =>
            {
                s.Library = new LibrarySnapshot { AllFiles = allFiles };
                s.LibraryFilter = "favorites";
            }, StateScope.Library);

            using var library = new LibraryService(store);
            using var devices = new HardwareDeviceService(store);
            using var playback = new PlaybackCoordinator(store, devices, library);

            playback.SetActiveModel("pss790");
            Assert.Equal(new[] { p790Both.FullPath, gmOnly.FullPath }, playback.GetFilteredSongList().Select(f => f.FullPath));

            playback.SetActiveModel("pss680");
            Assert.Equal(new[] { p680Both.FullPath, gmOnly.FullPath }, playback.GetFilteredSongList().Select(f => f.FullPath));
        }

        private sealed class ThreadSafeMockDevice : Melanchall.DryWetMidi.Multimedia.IOutputDevice
        {
            public event EventHandler<Melanchall.DryWetMidi.Multimedia.MidiEventSentEventArgs>? EventSent;
            public void PrepareForEventsSending() { }
            public void SendEvent(MidiEvent midiEvent) => EventSent?.Invoke(this, new Melanchall.DryWetMidi.Multimedia.MidiEventSentEventArgs(midiEvent));
            public void Dispose() { }
        }
    }
}
