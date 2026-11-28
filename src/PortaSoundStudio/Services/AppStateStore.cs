using System;
using System.Collections.Generic;
using PSS790Player.Core;
using PSS790Player.Models;

namespace PSS790Player.Services
{
    [Flags]
    public enum StateScope
    {
        None = 0,
        Shell = 1 << 0,
        Library = 1 << 1,
        Transport = 1 << 2,
        Channels = 1 << 3,
        SoundDesign = 1 << 4,
        Recorder = 1 << 5,
        Modals = 1 << 6,
        All = Shell | Library | Transport | Channels | SoundDesign | Recorder | Modals
    }

    /// <summary>
    /// Thread-safe reactive application state store for all Blazor UI components and background services.
    /// Raises <see cref="ScopedChanged"/> and <see cref="Changed"/> whenever UI-bound properties update.
    /// </summary>
    public sealed class AppStateStore
    {
        private readonly object _gate = new();

        public event Action? Changed;
        public event Action<StateScope>? ScopedChanged;

        // UI Shell & Navigation
        public StudioTab ActiveTab { get; set; } = StudioTab.Player;
        public string? ActiveHeaderMenu { get; set; } // "model", "more", or null
        public string SysExViewMode { get; set; } = "fm"; // fm, vector, drums, vault
        public string ActiveModel { get; set; } = "pss790";
        public bool IsDarkTheme { get; set; } = true;
        public bool HandheldMode { get; set; }
        public bool IsSidebarCollapsed { get; set; }
        public int SidebarWidth { get; set; } = 256;
        public bool ShowStudioHardwareGuide { get; set; }
        public string StudioGuideModelTab { get; set; } = "pss790";
        public string SysExGuideModelTab { get; set; } = "pss790";
        public string VisualizerMode { get; set; } = "spectrum"; // spectrum, waterfall, scope

        // Devices & Settings
        public List<string> OutputDevices { get; set; } = new();
        public List<string> InputDevices { get; set; } = new();
        public string? SelectedOutputDevice { get; set; }
        public string? SelectedInputDevice { get; set; }
        public string LibraryRoot { get; set; } = AppPaths.BaseMidiDir;
        public string DefaultLibraryRoot { get; set; } = AppPaths.DefaultBaseMidiDir;
        public bool IsCustomLibraryRoot => !string.IsNullOrWhiteSpace(AppSettings.CustomLibraryRoot);

        // Voice Catalogs
        public List<VoiceOptionDto> Pss790Voices { get; } = FactoryPresets.BuildVoiceCatalog("pss790");
        public List<VoiceOptionDto> Pss680Voices { get; } = FactoryPresets.BuildVoiceCatalog("pss680");
        public List<VoiceOptionDto> ActiveVoices => ActiveModel == "pss680" ? Pss680Voices : Pss790Voices;

        // Library & Navigation
        public LibrarySnapshot Library { get; set; } = new();
        public HashSet<string> Favorites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string SearchQuery { get; set; } = string.Empty;
        public string LibraryFilter { get; set; } = "all"; // all, gm, pss790, pss680, favorites
        public string? SelectedSongPath { get; set; }
        public string? PlayingSongPath { get; set; }
        public bool IsLoadingSong { get; set; }
        public bool IsPreparingPlayback { get; set; }
        public double PlaybackCountdownSeconds { get; set; }

        // Playback Transport
        public LoadedSongState? LoadedSong { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public double CurrentSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public double Speed { get; set; } = 1.0;
        public bool LoopEnabled { get; set; }
        public bool GmPassthrough { get; set; }
        public bool JukeboxContinuous { get; set; }
        public bool JukeboxShuffle { get; set; }
        public bool VectorQuadPlaybackEnabled { get; set; }
        public string SelectedMixerVectorPreset { get; set; } = "current";

        // Live Channel Meter Snapshot (0..15)
        public bool[] ChannelActive { get; set; } = new bool[16];
        public byte[] ChannelVelocities { get; set; } = new byte[16];

        // Batch / Sync / Health Audit States
        public BatchProgressInfo BatchProgress { get; set; } = new();
        public LibrarySyncState SyncState { get; set; } = new();
        public HealthAuditState HealthAudit { get; set; } = new();

        // Sound Design: PSS-680 FM Voice Designer
        public FmVoiceState FmVoice { get; set; } = FactoryPresets.CreateInitFmVoice(0);
        public int SelectedFmBank { get; set; }
        public bool FmLiveSyncEnabled { get; set; } = true;
        public string FmLiveStatus { get; set; } = "Offline";
        public string FmSearchQuery { get; set; } = string.Empty;
        public List<FmVoicePatchItem> FmVoiceLibrary { get; set; } = new();

        // Sound Design: PSS-790 Vector Synth
        public int VectorVoiceA { get; set; } = 0;
        public int VectorVoiceB { get; set; } = 34;
        public int VectorVoiceC { get; set; } = 16;
        public int VectorVoiceD { get; set; } = 56;
        public double VectorX { get; set; }
        public double VectorY { get; set; }
        public bool VectorIsOrbiting { get; set; }
        public string VectorOrbitMode { get; set; } = "manual";
        public double VectorOrbitSpeed { get; set; } = 3.0;
        public string VectorPresetName { get; set; } = "Custom Blend";
        public string VectorSearchQuery { get; set; } = string.Empty;
        public Dictionary<int, VectorChannelEffectState> VectorChannelEffects { get; set; } = new()
        {
            [0] = new(),
            [1] = new(),
            [2] = new(),
            [3] = new()
        };
        public List<VectorPresetFileItem> VectorPresetLibrary { get; set; } = new();

        // Sound Design: Drum Pads & Custom Drummer (03H)
        public int SelectedDrumPadBank { get; set; } // 0=A, 1=B, 2=C, 3=D
        public string DrummerPatternName { get; set; } = "Custom_Beat";
        public int DrummerBankNumber { get; set; }
        public int DrummerBeatsLength { get; set; } = 4;
        public bool DrummerTripletQuantize { get; set; }
        public List<DrummerTrackState> DrummerRows { get; set; } = BuildDefaultDrummerRows();

        // SysEx Vault
        public List<SysExDumpInfo> SysExDumps { get; set; } = new();
        public bool IsSysExCapturing { get; set; }
        public int SysExCapturePackets { get; set; }
        public long SysExCaptureBytes { get; set; }
        public string SysExCaptureStatus { get; set; } = "Ready to listen for bulk dumps.";
        public bool IsSysExTransmitting { get; set; }
        public int SysExTransmitProgress { get; set; }
        public int SysExTransmitPacketIndex { get; set; }
        public int SysExTransmitPacketTotal { get; set; }
        public string SysExTransmitStatus { get; set; } = string.Empty;
        public int SysExTransmitDelayMs { get; set; } = 30;
        public SysExInspectResultDto? InspectedSysEx { get; set; }

        // Recorder Studio
        public RecorderState RecorderState { get; set; } = RecorderState.Idle;
        public int RecorderCountInBeat { get; set; }
        public int RecorderBpm { get; set; } = 120;
        public bool RecorderCountIn { get; set; } = true;
        public bool RecorderMetronomeEnabled { get; set; } = true;
        public bool RecorderMidiThru { get; set; }
        public bool RecorderAutoTrim { get; set; } = true;
        public double RecorderElapsedSeconds { get; set; }
        public int RecorderEventCount { get; set; }
        public int RecorderNoteCount { get; set; }
        public int RecorderBar { get; set; } = 1;
        public int RecorderBeat { get; set; } = 1;
        public RecorderTakeReadyDto? LastTakeStats { get; set; }
        public List<RecordingTakeInfo> RecordingsLibrary { get; set; } = new();
        public string? PreviewingRecordingPath { get; set; }
        public bool[] RecorderChannelActive { get; set; } = new bool[16];
        public byte[] RecorderChannelVelocity { get; set; } = new byte[16];
        public byte?[] RecorderChannelNote { get; set; } = new byte?[16];
        public SortedDictionary<int, (int Channel, byte Velocity)> ActiveInboundNotes { get; } = new();

        // Modals & Toasts
        public List<ToastMessage> Toasts { get; } = new();
        public bool ShowBatchModal { get; set; }
        public bool ShowSyncModal { get; set; }
        public bool ShowHealthModal { get; set; }
        public bool ShowHealthAuditModal { get; set; }
        public bool ShowSettingsModal { get; set; }
        public bool ShowShortcutsModal { get; set; }
        public bool ShowHardwareGuideModal { get; set; }
        public string ModalGuideModel { get; set; } = "pss790";
        public string ModalGuideMethod { get; set; } = "midi";
        public bool ShowAboutModal { get; set; }
        public FileActionRequest? PendingFileAction { get; set; }
        public ContextMenuState? LibraryContextMenu { get; set; }
        public HashSet<string> ExpandedFolders { get; } = new(StringComparer.OrdinalIgnoreCase) { "cat:fav", "cat:pss790", "cat:pss680" };
        public HashSet<string> SelectedBatchPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsSelectMode { get; set; }

        public void Mutate(Action<AppStateStore> updater, StateScope scope = StateScope.All)
        {
            lock (_gate)
            {
                updater(this);
            }
            ScopedChanged?.Invoke(scope);
            Changed?.Invoke();
        }

        public void AddToast(string message, ToastSeverity severity = ToastSeverity.Info)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var toast = new ToastMessage { Message = message, Severity = severity };
            lock (_gate)
            {
                Toasts.Add(toast);
                if (Toasts.Count > 5)
                {
                    Toasts.RemoveAt(0);
                }
            }
            ScopedChanged?.Invoke(StateScope.Modals);
            Changed?.Invoke();

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(4500).ConfigureAwait(false);
                RemoveToast(toast.Id);
            });
        }

        public void RemoveToast(Guid id)
        {
            bool removed;
            lock (_gate)
            {
                removed = Toasts.RemoveAll(t => t.Id == id) > 0;
            }
            if (removed)
            {
                ScopedChanged?.Invoke(StateScope.Modals);
                Changed?.Invoke();
            }
        }

        private static List<DrummerTrackState> BuildDefaultDrummerRows()
        {
            int[] defaultIds = { 8, 9, 13, 14, 15, 0, 2, 4, 30, 28 };
            var rows = new List<DrummerTrackState>(defaultIds.Length);
            foreach (int idx in defaultIds)
            {
                var cat = Pss680CustomDrummerPattern.PadCatalog[Math.Clamp(idx, 0, 31)];
                rows.Add(new DrummerTrackState
                {
                    TrackIndex = idx,
                    InstrumentId = cat.Id,
                    MidiNote = cat.MidiNote,
                    PadLabel = $"Pad {cat.Pad}",
                    InstrumentName = cat.Name
                });
            }
            return rows;
        }
    }
}
