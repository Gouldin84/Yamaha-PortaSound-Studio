using System;
using System.Collections.Generic;
using PSS790Player.Core;

namespace PSS790Player.Models
{
    public enum StudioTab
    {
        Player,
        Recorder,
        SysEx
    }

    public enum HardwareModel
    {
        Pss790,
        Pss680
    }

    public static class HardwareModelExtensions
    {
        public static string ToModelKey(this HardwareModel model) =>
            model == HardwareModel.Pss680 ? "pss680" : "pss790";

        public static HardwareModel ParseModel(string? value) =>
            string.Equals(value, "pss680", StringComparison.OrdinalIgnoreCase)
                ? HardwareModel.Pss680
                : HardwareModel.Pss790;
    }

    public enum ToastSeverity
    {
        Info,
        Success,
        Warning,
        Error
    }

    public sealed class ToastMessage
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Message { get; init; } = string.Empty;
        public ToastSeverity Severity { get; init; } = ToastSeverity.Info;
    }

    public sealed class MidiFileItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string SubFolder { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Format { get; set; } = "gm";
        public bool IsFavorite { get; set; }
    }

    public sealed class FolderNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Format { get; set; } = "gm";
        public List<MidiFileItem> Files { get; set; } = new();
        public List<FolderNode> SubFolders { get; set; } = new();
        public int DirectCount { get; set; }
        public int TotalCount { get; set; }
    }

    public sealed class FolderOption
    {
        public string SubFolder { get; set; } = string.Empty;
        public string Display { get; set; } = string.Empty;
    }

    public sealed class CategoryTreeData
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Format { get; set; } = "gm";
        public List<FolderNode> SubFolders { get; set; } = new();
        public List<FolderOption> AllFolders { get; set; } = new();
        public List<MidiFileItem> Files { get; set; } = new();
        public int DirectCount { get; set; }
        public int Count { get; set; }
    }

    public sealed class LibrarySnapshot
    {
        public CategoryTreeData Gm { get; set; } = new();
        public CategoryTreeData Pss790 { get; set; } = new();
        public CategoryTreeData Pss680 { get; set; } = new();
        public List<MidiFileItem> AllFiles { get; set; } = new();
    }

    public sealed class VoiceOptionDto
    {
        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public byte MidiProgram { get; set; }
    }

    public sealed class ChannelStateDto
    {
        public int Channel { get; set; }
        public string Role { get; set; } = string.Empty;
        public bool HasNotes { get; set; }
        public string DetectedInstrument { get; set; } = string.Empty;
        public string? InferredSource { get; set; }
        public int PssVoice { get; set; }
        public string PssVoiceName { get; set; } = string.Empty;
        public int DefaultVoice { get; set; }
        public string DefaultVoiceName { get; set; } = string.Empty;
        public bool IsOverridden { get; set; }
        public int OctaveShift { get; set; }
        public bool IsMuted { get; set; }
        public bool IsSolo { get; set; }
        public byte Volume { get; set; } = 100;
        public bool IsDrum { get; set; }
        public byte ActivityVelocity { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class SongHealthDto
    {
        public string BadgeType { get; set; } = "optimal";
        public string BadgeLabel { get; set; } = "Optimal";
        public string BadgeTooltip { get; set; } = string.Empty;
        public int PeakPolyphony { get; set; }
        public string VoiceStressRating { get; set; } = "Optimal";
        public int ActiveChannelCount { get; set; }
        public int TotalNoteCount { get; set; }
        public List<int> ExplicitProgramChannels { get; set; } = new();
        public List<string> InferredProgramChannels { get; set; } = new();
        public List<string> XgSysExChannels { get; set; } = new();
        public List<int> UnassignedChannels { get; set; } = new();
        public int StuckPedalIntervalsCount { get; set; }
        public int DanglingPitchBendsCount { get; set; }
        public int AcousticMicroBendsNeutralized { get; set; }
        public int OutOfBoundsNotesCount { get; set; }
        public List<int> NonStandardDrumChannels { get; set; } = new();
        public List<int> XgSfxChannels { get; set; } = new();
        public List<string> AppliedAutoFixes { get; set; } = new();
    }

    public sealed class LoadedSongState
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public bool IsPss790 { get; set; }
        public bool IsPss680 { get; set; }
        public bool IsGmFile { get; set; }
        public bool IsGmPassthrough { get; set; }
        public string FormatTag { get; set; } = "GM MIDI";
        public string ActiveModel { get; set; } = "pss790";
        public bool CanSaveRemapped { get; set; }
        public double DurationSeconds { get; set; }
        public string DurationFormatted { get; set; } = "00:00";
        public int MasterTranspose { get; set; }
        public string PitchBendMode { get; set; } = "Auto";
        public bool PolyphonyGuard { get; set; } = true;
        public int PolyphonyLimit790 { get; set; } = Core.Constants.PolyphonyGuardThreshold;
        public bool IsOptimizedAvailable { get; set; }
        public string? OptimizedFilePath { get; set; }
        public string? LinkedGmPath { get; set; }
        public List<ChannelStateDto> Channels { get; set; } = new();
        public SongHealthDto Health { get; set; } = new();
        public Dictionary<int, string> ChannelPrograms { get; set; } = new();
    }

    public sealed class LinkedCheckResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool HasLinked { get; set; }
        public string? LinkedPath { get; set; }
        public string? LinkedName { get; set; }
        public bool IsFolder { get; set; }
    }

    public sealed class BatchLinkedCheckResult
    {
        public int TotalCount { get; set; }
        public bool HasLinked { get; set; }
        public int LinkedCount { get; set; }
    }

    public sealed class BatchConversionRequest
    {
        public string? SourceDir { get; set; }
        public string? Target790Dir { get; set; }
        public string? Target680Dir { get; set; }
        public bool Convert790 { get; set; } = true;
        public bool Convert680 { get; set; } = true;
        public bool PolyphonyGuard790 { get; set; } = true;
        public bool ReverbBoost790 { get; set; } = true;
        public bool VelocityNorm790 { get; set; } = true;
        public bool Sustain790 { get; set; } = true;
        public bool ExpressionFold790 { get; set; } = true;
        public bool PitchTune790 { get; set; } = true;
        public bool PreRoll790 { get; set; } = true;
        public bool PolyphonyGuard680 { get; set; } = true;
        public bool CleanControllers680 { get; set; } = true;
        public bool FmVelocity680 { get; set; } = true;
        public bool Drums680 { get; set; } = true;
        public bool Sustain680 { get; set; } = true;
        public bool ExpressionFold680 { get; set; } = true;
        public bool PitchTune680 { get; set; } = true;
        public bool PreRoll680 { get; set; } = true;
    }

    public sealed class BatchProgressInfo
    {
        public bool IsRunning { get; set; }
        public int Processed { get; set; }
        public int Total { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool WasCancelled { get; set; }
        public int ConvertedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public int Converted790Count { get; set; }
        public int Converted680Count { get; set; }
    }

    public sealed class SyncFileActionItem
    {
        public string RelativePath { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
    }

    public sealed class LibrarySyncState
    {
        public bool IsAnalyzing { get; set; }
        public bool IsRunning { get; set; }
        public int TotalGm { get; set; }
        public int UpToDate { get; set; }
        public int NewOrOutdated790 { get; set; }
        public int NewOrOutdated680 { get; set; }
        public int Orphan790 { get; set; }
        public int Orphan680 { get; set; }
        public bool HasPlan => NewOrOutdated790 > 0 || NewOrOutdated680 > 0 || Orphan790 > 0 || Orphan680 > 0;
        public int Processed { get; set; }
        public int Total { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public sealed class HealthAuditItemDto
    {
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int PeakPolyphony { get; set; }
        public string VoiceStressRating { get; set; } = string.Empty;
        public string BadgeType { get; set; } = string.Empty;
        public string BadgeLabel { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    public sealed class HealthAuditState
    {
        public bool IsRunning { get; set; }
        public int ScannedCount { get; set; }
        public int TotalCount { get; set; }
        public int OptimalCount { get; set; }
        public int AutoFixedCount { get; set; }
        public int HighPolyCount { get; set; }
        public int CorruptCount { get; set; }
        public List<HealthAuditItemDto> Items { get; set; } = new();
    }

    public sealed class FmOperatorState
    {
        public int FineDetune { get; set; }
        public int Multiplier { get; set; } = 1;
        public int PanelLevel { get; set; } = 80;
        public int LevelKeyScalingHigh { get; set; }
        public int LevelKeyScalingLow { get; set; }
        public int RateKeyScaling { get; set; }
        public int AttackRate { get; set; } = 63;
        public bool AmplitudeModulationEnable { get; set; }
        public bool CoarseDetune { get; set; }
        public int Decay1Rate { get; set; } = 20;
        public int Waveform { get; set; }
        public int Decay2Rate { get; set; }
        public int Decay1Level { get; set; }
        public int ReleaseRate { get; set; } = 6;
        public int SustainReleaseRate { get; set; } = 5;

        public FmOperatorState Clone() => (FmOperatorState)MemberwiseClone();
    }

    public sealed class FmVoiceState
    {
        public string Name { get; set; } = "Init 2-Op Sine";
        public int BankNumber { get; set; }
        public int Feedback { get; set; }
        public int PitchModulationSensitivity { get; set; }
        public int AmplitudeModulationSensitivity { get; set; }
        public int VibratoDelayTime { get; set; }
        public bool Vibrato { get; set; }
        public bool Sustain { get; set; } = true;
        public FmOperatorState Modulator { get; set; } = new() { PanelLevel = 55, Decay1Rate = 25, ReleaseRate = 6, AttackRate = 63 };
        public FmOperatorState Carrier { get; set; } = new() { PanelLevel = 88, Decay1Rate = 18, ReleaseRate = 6, AttackRate = 63 };

        public PSS680Voice ToCoreVoice()
        {
            var voice = new PSS680Voice
            {
                Name = Name,
                BankNumber = Math.Clamp(BankNumber, 0, 4),
                Feedback = Math.Clamp(Feedback, 0, 7),
                PitchModulationSensitivity = Math.Clamp(PitchModulationSensitivity, 0, 7),
                AmplitudeModulationSensitivity = Math.Clamp(AmplitudeModulationSensitivity, 0, 3),
                VibratoDelayTime = Math.Clamp(VibratoDelayTime, 0, 127),
                Vibrato = Vibrato,
                Sustain = Sustain
            };
            CopyOp(Modulator, voice.Modulator);
            CopyOp(Carrier, voice.Carrier);
            return voice;
        }

        public static FmVoiceState FromCoreVoice(PSS680Voice voice) => new()
        {
            Name = voice.Name,
            BankNumber = voice.BankNumber,
            Feedback = voice.Feedback,
            PitchModulationSensitivity = voice.PitchModulationSensitivity,
            AmplitudeModulationSensitivity = voice.AmplitudeModulationSensitivity,
            VibratoDelayTime = voice.VibratoDelayTime,
            Vibrato = voice.Vibrato,
            Sustain = voice.Sustain,
            Modulator = OpFromCore(voice.Modulator),
            Carrier = OpFromCore(voice.Carrier)
        };

        private static void CopyOp(FmOperatorState src, FmOperatorParameters dst)
        {
            dst.FineDetune = Math.Clamp(src.FineDetune, -3, 3);
            dst.Multiplier = Math.Clamp(src.Multiplier, 0, 15);
            dst.PanelLevel = Math.Clamp(src.PanelLevel, 0, 99);
            dst.LevelKeyScalingHigh = Math.Clamp(src.LevelKeyScalingHigh, 0, 15);
            dst.LevelKeyScalingLow = Math.Clamp(src.LevelKeyScalingLow, 0, 15);
            dst.RateKeyScaling = Math.Clamp(src.RateKeyScaling, 0, 3);
            dst.AttackRate = Math.Clamp(src.AttackRate, 0, 63);
            dst.AmplitudeModulationEnable = src.AmplitudeModulationEnable;
            dst.CoarseDetune = src.CoarseDetune;
            dst.Decay1Rate = Math.Clamp(src.Decay1Rate, 0, 63);
            dst.Waveform = (FmWaveform)Math.Clamp(src.Waveform, 0, 3);
            dst.Decay2Rate = Math.Clamp(src.Decay2Rate, 0, 31);
            dst.Decay1Level = Math.Clamp(src.Decay1Level, 0, 15);
            dst.ReleaseRate = Math.Clamp(src.ReleaseRate, 0, 15);
            dst.SustainReleaseRate = Math.Clamp(src.SustainReleaseRate, 0, 15);
        }

        private static FmOperatorState OpFromCore(FmOperatorParameters op) => new()
        {
            FineDetune = op.FineDetune,
            Multiplier = op.Multiplier,
            PanelLevel = op.PanelLevel,
            LevelKeyScalingHigh = op.LevelKeyScalingHigh,
            LevelKeyScalingLow = op.LevelKeyScalingLow,
            RateKeyScaling = op.RateKeyScaling,
            AttackRate = op.AttackRate,
            AmplitudeModulationEnable = op.AmplitudeModulationEnable,
            CoarseDetune = op.CoarseDetune,
            Decay1Rate = op.Decay1Rate,
            Waveform = (int)op.Waveform,
            Decay2Rate = op.Decay2Rate,
            Decay1Level = op.Decay1Level,
            ReleaseRate = op.ReleaseRate,
            SustainReleaseRate = op.SustainReleaseRate
        };
    }

    public sealed class FmVoicePatchItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string VoiceName { get; set; } = string.Empty;
        public int BankNumber { get; set; }
        public int BankDisplay { get; set; }
        public string FileSize { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public sealed class VectorChannelEffectState
    {
        public int Rev { get; set; } = 1; // 0=OFF, 1=RM, 2=HL, 3=PL
        public int Vib { get; set; } = 0; // 0..7
    }

    public sealed class VectorPresetFileItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int VoiceA { get; set; }
        public int VoiceB { get; set; } = 34;
        public int VoiceC { get; set; } = 16;
        public int VoiceD { get; set; } = 56;
        public double PosX { get; set; }
        public double PosY { get; set; }
        public string OrbitMode { get; set; } = "manual";
        public double OrbitSpeed { get; set; } = 3.0;
        public Dictionary<int, VectorChannelEffectState> ChannelEffects { get; set; } = new()
        {
            [0] = new(),
            [1] = new(),
            [2] = new(),
            [3] = new()
        };
        public string FileSize { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    public sealed class DrummerTrackState
    {
        public int TrackIndex { get; set; }
        public int InstrumentId { get; set; }
        public byte MidiNote { get; set; }
        public string PadLabel { get; set; } = string.Empty;
        public string InstrumentName { get; set; } = string.Empty;
        public bool Accent1 { get; set; }
        public bool Accent2 { get; set; }
        public bool Roll { get; set; }
        public byte[] StepBytes { get; set; } = new byte[8];
    }

    public sealed class SysExInspectResultDto
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = string.Empty;
        public string CreatedAtFormatted { get; set; } = string.Empty;
        public int PacketCount { get; set; }
        public string Manufacturer { get; set; } = string.Empty;
        public string DumpType { get; set; } = string.Empty;
        public string HexPreview { get; set; } = string.Empty;
        public string? FormatCode { get; set; }
        public bool CanExtractMidi { get; set; }
        public bool CanOpenDrummerGrid { get; set; }
        public string? DecodedSummary { get; set; }
    }

    public sealed class RecorderTakeReadyDto
    {
        public int NoteCount { get; set; }
        public int EventCount { get; set; }
        public double DurationSeconds { get; set; }
        public string DurationFormatted { get; set; } = "00:00";
        public int TrimmedTicks { get; set; }
        public int? QuantizedGrid { get; set; }
        public string? AutoSavedPath { get; set; }
    }

    public sealed class DrummerBankPatternSummary
    {
        public int BankNumber { get; set; }
        public int BeatsLength { get; set; } = 4;
        public bool IsTriplet { get; set; }
        public List<DrummerTrackState> Rows { get; set; } = new();
    }

    public sealed class FileActionRequest
    {
        public string Mode { get; set; } = "new_folder"; // new_folder, rename, move, batch_move, delete, batch_delete
        public string TargetPath { get; set; } = string.Empty;
        public List<string> BatchPaths { get; set; } = new();
        public string CurrentName { get; set; } = string.Empty;
        public string Category { get; set; } = "gm";
        public string SubFolder { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public bool HasLinked { get; set; }
        public string? LinkedPath { get; set; }
        public string? LinkedName { get; set; }
        public int LinkedCount { get; set; }
    }

    public sealed class ContextMenuState
    {
        public double ClientX { get; set; }
        public double ClientY { get; set; }
        public string FullPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "gm";
        public string SubFolder { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public bool IsFavorite { get; set; }
    }
}
