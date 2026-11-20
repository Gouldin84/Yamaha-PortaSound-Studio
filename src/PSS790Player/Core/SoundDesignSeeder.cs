using System;
using System.IO;
using System.Text.Json;

namespace PSS790Player.Core
{
    /// <summary>
    /// Automatically seeds curated factory FM voices and Vector presets into the user's SysEx library on initial launch.
    /// </summary>
    public static class SoundDesignSeeder
    {
        private static readonly object _seedLock = new();
        private static bool _isSeeding = false;

        public static void SeedAll()
        {
            lock (_seedLock)
            {
                if (_isSeeding) return;
                try
                {
                    _isSeeding = true;
                    SeedFmVoices();
                    SeedVectorPresets();
                }
                finally
                {
                    _isSeeding = false;
                }
            }
        }

        public static void SeedFmVoices()
        {
            try
            {
                if (!Directory.Exists(AppPaths.FmVoicesDir))
                {
                    Directory.CreateDirectory(AppPaths.FmVoicesDir);
                }
                if (Directory.GetFiles(AppPaths.FmVoicesDir, "*.syx", SearchOption.TopDirectoryOnly).Length > 0) return;

                {
                    var v = new PSS680Voice { Name = "Take On Me Lead", BankNumber = 0, Feedback = 6, PitchModulationSensitivity = 3, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 35, Vibrato = true, Sustain = true };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 76; v.Modulator.Waveform = FmWaveform.SineHalfWave; v.Modulator.AttackRate = 62; v.Modulator.Decay1Rate = 18; v.Modulator.ReleaseRate = 7; v.Modulator.FineDetune = 2; v.Modulator.Decay1Level = 4; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 7; v.Modulator.LevelKeyScalingHigh = 3; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.SquaredSine; v.Carrier.AttackRate = 61; v.Carrier.Decay1Rate = 14; v.Carrier.ReleaseRate = 7; v.Carrier.FineDetune = -1; v.Carrier.Decay1Level = 2; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 7; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Take On Me Lead.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Jump OB Brass", BankNumber = 0, Feedback = 6, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 74; v.Modulator.Waveform = FmWaveform.SineHalfWave; v.Modulator.AttackRate = 57; v.Modulator.Decay1Rate = 22; v.Modulator.ReleaseRate = 6; v.Modulator.FineDetune = 3; v.Modulator.Decay1Level = 3; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 6; v.Modulator.LevelKeyScalingHigh = 3; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.SineHalfWave; v.Carrier.AttackRate = 55; v.Carrier.Decay1Rate = 16; v.Carrier.ReleaseRate = 6; v.Carrier.FineDetune = -2; v.Carrier.Decay1Level = 1; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 6; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Jump OB Brass.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "DX7 Ballad Tine EP", BankNumber = 0, Feedback = 1, PitchModulationSensitivity = 3, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 35, Vibrato = true, Sustain = true };
                    v.Modulator.Multiplier = 14; v.Modulator.PanelLevel = 68; v.Modulator.Waveform = FmWaveform.Sine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 52; v.Modulator.ReleaseRate = 7; v.Modulator.FineDetune = 1; v.Modulator.Decay1Level = 14; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 7; v.Modulator.LevelKeyScalingHigh = 9; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 16; v.Carrier.ReleaseRate = 6; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 10; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 6; v.Carrier.LevelKeyScalingHigh = 3; v.Carrier.RateKeyScaling = 1; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "DX7 Ballad Tine EP.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "LatelyBass / Solid Bass", BankNumber = 0, Feedback = 6, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 75; v.Modulator.Waveform = FmWaveform.SquaredSine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 48; v.Modulator.ReleaseRate = 9; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 12; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 9; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 24; v.Carrier.ReleaseRate = 8; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 2; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 8; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "LatelyBass  Solid Bass.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Sweet Dreams Pulse", BankNumber = 0, Feedback = 5, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 2; v.Modulator.PanelLevel = 71; v.Modulator.Waveform = FmWaveform.SquaredSineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 36; v.Modulator.ReleaseRate = 9; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 5; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 9; v.Modulator.LevelKeyScalingHigh = 3; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 28; v.Carrier.ReleaseRate = 9; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 3; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 9; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Sweet Dreams Pulse.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Blue Monday Octaver", BankNumber = 0, Feedback = 3, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 77; v.Modulator.Waveform = FmWaveform.SquaredSine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 46; v.Modulator.ReleaseRate = 8; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 15; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 8; v.Modulator.LevelKeyScalingHigh = 3; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 0; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 34; v.Carrier.ReleaseRate = 7; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 15; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 7; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Blue Monday Octaver.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Danger Zone FM Slap", BankNumber = 0, Feedback = 4, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 3; v.Modulator.PanelLevel = 81; v.Modulator.Waveform = FmWaveform.SineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 50; v.Modulator.ReleaseRate = 9; v.Modulator.FineDetune = 1; v.Modulator.Decay1Level = 13; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 9; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 30; v.Carrier.ReleaseRate = 8; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 14; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 8; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Danger Zone FM Slap.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Axel F Brass Lead", BankNumber = 0, Feedback = 4, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 2; v.Modulator.PanelLevel = 70; v.Modulator.Waveform = FmWaveform.SquaredSine; v.Modulator.AttackRate = 62; v.Modulator.Decay1Rate = 38; v.Modulator.ReleaseRate = 8; v.Modulator.FineDetune = 1; v.Modulator.Decay1Level = 6; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 8; v.Modulator.LevelKeyScalingHigh = 3; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 62; v.Carrier.Decay1Rate = 26; v.Carrier.ReleaseRate = 7; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 4; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 7; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Axel F Brass Lead.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Harder Better Lead", BankNumber = 0, Feedback = 5, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 3; v.Modulator.PanelLevel = 72; v.Modulator.Waveform = FmWaveform.SquaredSineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 32; v.Modulator.ReleaseRate = 9; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 4; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 9; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.SquaredSine; v.Carrier.AttackRate = 62; v.Carrier.Decay1Rate = 22; v.Carrier.ReleaseRate = 9; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 2; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 9; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Harder Better Lead.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Stranger Things Arp Bass", BankNumber = 0, Feedback = 6, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 76; v.Modulator.Waveform = FmWaveform.SineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 44; v.Modulator.ReleaseRate = 8; v.Modulator.FineDetune = 2; v.Modulator.Decay1Level = 7; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 8; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.SquaredSine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 36; v.Carrier.ReleaseRate = 7; v.Carrier.FineDetune = -1; v.Carrier.Decay1Level = 5; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 7; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Stranger Things Arp Bass.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Blade Runner CS-80 Pad", BankNumber = 0, Feedback = 5, PitchModulationSensitivity = 3, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 35, Vibrato = true, Sustain = true };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 69; v.Modulator.Waveform = FmWaveform.SquaredSine; v.Modulator.AttackRate = 18; v.Modulator.Decay1Rate = 12; v.Modulator.ReleaseRate = 3; v.Modulator.FineDetune = 2; v.Modulator.Decay1Level = 3; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 3; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 16; v.Carrier.Decay1Rate = 10; v.Carrier.ReleaseRate = 3; v.Carrier.FineDetune = -2; v.Carrier.Decay1Level = 1; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 3; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Blade Runner CS-80 Pad.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Africa Polysynth Pad", BankNumber = 0, Feedback = 4, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 71; v.Modulator.Waveform = FmWaveform.SineHalfWave; v.Modulator.AttackRate = 28; v.Modulator.Decay1Rate = 16; v.Modulator.ReleaseRate = 4; v.Modulator.FineDetune = 2; v.Modulator.Decay1Level = 5; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 4; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 25; v.Carrier.Decay1Rate = 14; v.Carrier.ReleaseRate = 4; v.Carrier.FineDetune = -1; v.Carrier.Decay1Level = 2; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 4; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Africa Polysynth Pad.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Orinoco Shimmer Pad", BankNumber = 0, Feedback = 2, PitchModulationSensitivity = 3, AmplitudeModulationSensitivity = 1, VibratoDelayTime = 35, Vibrato = true, Sustain = true };
                    v.Modulator.Multiplier = 4; v.Modulator.PanelLevel = 59; v.Modulator.Waveform = FmWaveform.Sine; v.Modulator.AttackRate = 24; v.Modulator.Decay1Rate = 14; v.Modulator.ReleaseRate = 3; v.Modulator.FineDetune = 1; v.Modulator.Decay1Level = 4; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 3; v.Modulator.LevelKeyScalingHigh = 9; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = true;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 22; v.Carrier.Decay1Rate = 12; v.Carrier.ReleaseRate = 3; v.Carrier.FineDetune = -1; v.Carrier.Decay1Level = 1; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 3; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Orinoco Shimmer Pad.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Last Christmas FM Bell", BankNumber = 0, Feedback = 1, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 7; v.Modulator.PanelLevel = 72; v.Modulator.Waveform = FmWaveform.Sine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 24; v.Modulator.ReleaseRate = 5; v.Modulator.FineDetune = 1; v.Modulator.Decay1Level = 15; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 5; v.Modulator.LevelKeyScalingHigh = 9; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 12; v.Carrier.ReleaseRate = 4; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 15; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 4; v.Carrier.LevelKeyScalingHigh = 3; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Last Christmas FM Bell.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "In the Air Glass Chime", BankNumber = 0, Feedback = 2, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 5; v.Modulator.PanelLevel = 65; v.Modulator.Waveform = FmWaveform.Sine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 28; v.Modulator.ReleaseRate = 4; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 14; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 4; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 16; v.Carrier.ReleaseRate = 4; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 12; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 4; v.Carrier.LevelKeyScalingHigh = 3; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "In the Air Glass Chime.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "80s DX7 Wood Marimba", BankNumber = 0, Feedback = 0, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 4; v.Modulator.PanelLevel = 70; v.Modulator.Waveform = FmWaveform.Sine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 56; v.Modulator.ReleaseRate = 8; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 15; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 8; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 34; v.Carrier.ReleaseRate = 8; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 15; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 8; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 1; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "80s DX7 Wood Marimba.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Green Hill Steel Drum", BankNumber = 0, Feedback = 4, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 3; v.Modulator.PanelLevel = 71; v.Modulator.Waveform = FmWaveform.SquaredSine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 38; v.Modulator.ReleaseRate = 7; v.Modulator.FineDetune = 2; v.Modulator.Decay1Level = 15; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 7; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 26; v.Carrier.ReleaseRate = 7; v.Carrier.FineDetune = -1; v.Carrier.Decay1Level = 15; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 7; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Green Hill Steel Drum.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "80s Gated Noise Snare", BankNumber = 0, Feedback = 7, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 3; v.Modulator.PanelLevel = 77; v.Modulator.Waveform = FmWaveform.SineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 46; v.Modulator.ReleaseRate = 13; v.Modulator.FineDetune = 3; v.Modulator.Decay1Level = 15; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 13; v.Modulator.LevelKeyScalingHigh = 0; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.SquaredSineHalfWave; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 44; v.Carrier.ReleaseRate = 13; v.Carrier.FineDetune = -3; v.Carrier.Decay1Level = 15; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 13; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "80s Gated Noise Snare.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Planet Rock Sub Kick", BankNumber = 0, Feedback = 3, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 79; v.Modulator.Waveform = FmWaveform.Sine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 60; v.Modulator.ReleaseRate = 10; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 15; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 10; v.Modulator.LevelKeyScalingHigh = 0; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 0; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 34; v.Carrier.ReleaseRate = 9; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 15; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 9; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Planet Rock Sub Kick.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Blue Monday Metallic Clank", BankNumber = 0, Feedback = 5, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = false };
                    v.Modulator.Multiplier = 7; v.Modulator.PanelLevel = 74; v.Modulator.Waveform = FmWaveform.SquaredSineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 48; v.Modulator.ReleaseRate = 12; v.Modulator.FineDetune = 2; v.Modulator.Decay1Level = 15; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 12; v.Modulator.LevelKeyScalingHigh = 3; v.Modulator.RateKeyScaling = 0; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 5; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.SquaredSineHalfWave; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 42; v.Carrier.ReleaseRate = 12; v.Carrier.FineDetune = -2; v.Carrier.Decay1Level = 15; v.Carrier.Decay2Rate = 10; v.Carrier.SustainReleaseRate = 12; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Blue Monday Metallic Clank.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Sky Sanctuary Crystalline Lead", BankNumber = 0, Feedback = 3, PitchModulationSensitivity = 3, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 35, Vibrato = true, Sustain = true };
                    v.Modulator.Multiplier = 3; v.Modulator.PanelLevel = 70; v.Modulator.Waveform = FmWaveform.SquaredSine; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 42; v.Modulator.ReleaseRate = 7; v.Modulator.FineDetune = 1; v.Modulator.Decay1Level = 11; v.Modulator.Decay2Rate = 12; v.Modulator.SustainReleaseRate = 7; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 18; v.Carrier.ReleaseRate = 6; v.Carrier.FineDetune = -1; v.Carrier.Decay1Level = 4; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 6; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Sky Sanctuary Crystalline Lead.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Chemical Plant Slap Bass", BankNumber = 0, Feedback = 6, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 2; v.Modulator.PanelLevel = 74; v.Modulator.Waveform = FmWaveform.SineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 52; v.Modulator.ReleaseRate = 9; v.Modulator.FineDetune = 0; v.Modulator.Decay1Level = 11; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 9; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 1; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.Sine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 26; v.Carrier.ReleaseRate = 9; v.Carrier.FineDetune = 0; v.Carrier.Decay1Level = 2; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 9; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Chemical Plant Slap Bass.syx"), v.ToSysExMessage());
                }
                {
                    var v = new PSS680Voice { Name = "Go Straight Gritty House Bass", BankNumber = 0, Feedback = 7, PitchModulationSensitivity = 0, AmplitudeModulationSensitivity = 0, VibratoDelayTime = 0, Vibrato = false, Sustain = true };
                    v.Modulator.Multiplier = 1; v.Modulator.PanelLevel = 85; v.Modulator.Waveform = FmWaveform.SquaredSineHalfWave; v.Modulator.AttackRate = 63; v.Modulator.Decay1Rate = 44; v.Modulator.ReleaseRate = 9; v.Modulator.FineDetune = 2; v.Modulator.Decay1Level = 8; v.Modulator.Decay2Rate = 4; v.Modulator.SustainReleaseRate = 9; v.Modulator.LevelKeyScalingHigh = 6; v.Modulator.RateKeyScaling = 1; v.Modulator.AmplitudeModulationEnable = false;
                    v.Carrier.Multiplier = 0; v.Carrier.PanelLevel = 92; v.Carrier.Waveform = FmWaveform.SquaredSine; v.Carrier.AttackRate = 63; v.Carrier.Decay1Rate = 28; v.Carrier.ReleaseRate = 8; v.Carrier.FineDetune = -2; v.Carrier.Decay1Level = 3; v.Carrier.Decay2Rate = 3; v.Carrier.SustainReleaseRate = 8; v.Carrier.LevelKeyScalingHigh = 0; v.Carrier.RateKeyScaling = 0; v.Carrier.AmplitudeModulationEnable = false;
                    File.WriteAllBytes(Path.Combine(AppPaths.FmVoicesDir, "Go Straight Gritty House Bass.syx"), v.ToSysExMessage());
                }
            }
            catch { }
        }

        public static void SeedVectorPresets()
        {
            try
            {
                if (!Directory.Exists(AppPaths.VectorPresetsDir))
                {
                    Directory.CreateDirectory(AppPaths.VectorPresetsDir);
                }
                if (Directory.GetFiles(AppPaths.VectorPresetsDir, "*.json", SearchOption.TopDirectoryOnly).Length > 0) return;

                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Take On Me Vector Lead.json"), "{\"name\": \"Take On Me Vector Lead\", \"voiceA\": 65, \"voiceB\": 65, \"voiceC\": 0, \"voiceD\": 7, \"posX\": 0.38, \"posY\": 0.88, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.4, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 3}, \"1\": {\"rev\": 2, \"vib\": 3}, \"2\": {\"rev\": 1, \"vib\": 0}, \"3\": {\"rev\": 1, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Jump OB Vector Brass.json"), "{\"name\": \"Jump OB Vector Brass\", \"voiceA\": 65, \"voiceB\": 66, \"voiceC\": 65, \"voiceD\": 74, \"posX\": 0.12, \"posY\": 0.69, \"orbitMode\": \"pingpong\", \"orbitSpeed\": 4.3, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 4}, \"1\": {\"rev\": 2, \"vib\": 2}, \"2\": {\"rev\": 2, \"vib\": 4}, \"3\": {\"rev\": 3, \"vib\": 5}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Ballad Tine & Crystal EP.json"), "{\"name\": \"Ballad Tine & Crystal EP\", \"voiceA\": 1, \"voiceB\": 7, \"voiceC\": 2, \"voiceD\": 74, \"posX\": -0.19, \"posY\": 0.44, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.7, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 1}, \"1\": {\"rev\": 1, \"vib\": 0}, \"2\": {\"rev\": 2, \"vib\": 1}, \"3\": {\"rev\": 3, \"vib\": 4}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "What Is Love Club Bass.json"), "{\"name\": \"What Is Love Club Bass\", \"voiceA\": 91, \"voiceB\": 84, \"voiceC\": 6, \"voiceD\": 92, \"posX\": -0.12, \"posY\": 0.56, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.2, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 0, \"vib\": 0}, \"2\": {\"rev\": 1, \"vib\": 0}, \"3\": {\"rev\": 1, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Sweet Dreams Vector Pulse.json"), "{\"name\": \"Sweet Dreams Vector Pulse\", \"voiceA\": 71, \"voiceB\": 91, \"voiceC\": 11, \"voiceD\": 34, \"posX\": 0.25, \"posY\": 0.5, \"orbitMode\": \"manual\", \"orbitSpeed\": 3.0, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 4}, \"1\": {\"rev\": 0, \"vib\": 0}, \"2\": {\"rev\": 2, \"vib\": 2}, \"3\": {\"rev\": 2, \"vib\": 3}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Blue Monday Choir & Bass.json"), "{\"name\": \"Blue Monday Choir & Bass\", \"voiceA\": 92, \"voiceB\": 64, \"voiceC\": 84, \"voiceD\": 81, \"posX\": 0.19, \"posY\": 0.44, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.6, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 2, \"vib\": 3}, \"2\": {\"rev\": 0, \"vib\": 0}, \"3\": {\"rev\": 1, \"vib\": 2}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Danger Zone Slap & Brass.json"), "{\"name\": \"Danger Zone Slap & Brass\", \"voiceA\": 84, \"voiceB\": 65, \"voiceC\": 86, \"voiceD\": 15, \"posX\": 0.12, \"posY\": 0.62, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.2, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 2, \"vib\": 3}, \"2\": {\"rev\": 0, \"vib\": 0}, \"3\": {\"rev\": 3, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Axel F Marimba-Brass Lead.json"), "{\"name\": \"Axel F Marimba-Brass Lead\", \"voiceA\": 66, \"voiceB\": 43, \"voiceC\": 71, \"voiceD\": 98, \"posX\": 0.31, \"posY\": 0.56, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.2, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 2}, \"1\": {\"rev\": 1, \"vib\": 0}, \"2\": {\"rev\": 2, \"vib\": 4}, \"3\": {\"rev\": 3, \"vib\": 1}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Harder Better Talk-Lead.json"), "{\"name\": \"Harder Better Talk-Lead\", \"voiceA\": 72, \"voiceB\": 81, \"voiceC\": 6, \"voiceD\": 71, \"posX\": 0.44, \"posY\": 0.5, \"orbitMode\": \"circle\", \"orbitSpeed\": 1.9, \"channelEffects\": {\"0\": {\"rev\": 1, \"vib\": 2}, \"1\": {\"rev\": 0, \"vib\": 0}, \"2\": {\"rev\": 0, \"vib\": 0}, \"3\": {\"rev\": 1, \"vib\": 2}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Stranger Things Analog Vector.json"), "{\"name\": \"Stranger Things Analog Vector\", \"voiceA\": 91, \"voiceB\": 75, \"voiceC\": 65, \"voiceD\": 74, \"posX\": 0.25, \"posY\": 0.44, \"orbitMode\": \"pingpong\", \"orbitSpeed\": 3.8, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 1}, \"1\": {\"rev\": 3, \"vib\": 5}, \"2\": {\"rev\": 2, \"vib\": 3}, \"3\": {\"rev\": 3, \"vib\": 5}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Blade Runner CS-80 Vector Pad.json"), "{\"name\": \"Blade Runner CS-80 Vector Pad\", \"voiceA\": 65, \"voiceB\": 74, \"voiceC\": 35, \"voiceD\": 76, \"posX\": 0.19, \"posY\": 0.31, \"orbitMode\": \"pingpong\", \"orbitSpeed\": 7.5, \"channelEffects\": {\"0\": {\"rev\": 3, \"vib\": 5}, \"1\": {\"rev\": 3, \"vib\": 6}, \"2\": {\"rev\": 2, \"vib\": 3}, \"3\": {\"rev\": 3, \"vib\": 4}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Africa Kalimba & Polysynth.json"), "{\"name\": \"Africa Kalimba & Polysynth\", \"voiceA\": 75, \"voiceB\": 43, \"voiceC\": 66, \"voiceD\": 74, \"posX\": -0.12, \"posY\": 0.38, \"orbitMode\": \"wander\", \"orbitSpeed\": 2.0, \"channelEffects\": {\"0\": {\"rev\": 3, \"vib\": 5}, \"1\": {\"rev\": 2, \"vib\": 0}, \"2\": {\"rev\": 2, \"vib\": 2}, \"3\": {\"rev\": 3, \"vib\": 5}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Orinoco Pizzicato & Choir.json"), "{\"name\": \"Orinoco Pizzicato & Choir\", \"voiceA\": 36, \"voiceB\": 64, \"voiceC\": 37, \"voiceD\": 82, \"posX\": 0.12, \"posY\": 0.25, \"orbitMode\": \"pingpong\", \"orbitSpeed\": 5.0, \"channelEffects\": {\"0\": {\"rev\": 3, \"vib\": 2}, \"1\": {\"rev\": 3, \"vib\": 5}, \"2\": {\"rev\": 2, \"vib\": 1}, \"3\": {\"rev\": 3, \"vib\": 6}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Last Christmas Sleigh & Bell.json"), "{\"name\": \"Last Christmas Sleigh & Bell\", \"voiceA\": 40, \"voiceB\": 43, \"voiceC\": 42, \"voiceD\": 7, \"posX\": 0.19, \"posY\": 0.5, \"orbitMode\": \"wander\", \"orbitSpeed\": 2.1, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 4}, \"1\": {\"rev\": 2, \"vib\": 0}, \"2\": {\"rev\": 2, \"vib\": 4}, \"3\": {\"rev\": 1, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "In the Air Glass & Atmosphere.json"), "{\"name\": \"In the Air Glass & Atmosphere\", \"voiceA\": 93, \"voiceB\": 82, \"voiceC\": 7, \"voiceD\": 64, \"posX\": 0.31, \"posY\": 0.38, \"orbitMode\": \"pingpong\", \"orbitSpeed\": 6.0, \"channelEffects\": {\"0\": {\"rev\": 3, \"vib\": 2}, \"1\": {\"rev\": 2, \"vib\": 4}, \"2\": {\"rev\": 2, \"vib\": 0}, \"3\": {\"rev\": 3, \"vib\": 4}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "80s Wood Marimba & Vibe.json"), "{\"name\": \"80s Wood Marimba & Vibe\", \"voiceA\": 43, \"voiceB\": 7, \"voiceC\": 98, \"voiceD\": 91, \"posX\": 0.12, \"posY\": 0.56, \"orbitMode\": \"manual\", \"orbitSpeed\": 2.5, \"channelEffects\": {\"0\": {\"rev\": 1, \"vib\": 0}, \"1\": {\"rev\": 1, \"vib\": 0}, \"2\": {\"rev\": 2, \"vib\": 1}, \"3\": {\"rev\": 0, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Green Hill Steel Drum Ensemble.json"), "{\"name\": \"Green Hill Steel Drum Ensemble\", \"voiceA\": 41, \"voiceB\": 43, \"voiceC\": 7, \"voiceD\": 52, \"posX\": 0.19, \"posY\": 0.75, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.5, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 1}, \"1\": {\"rev\": 2, \"vib\": 0}, \"2\": {\"rev\": 1, \"vib\": 0}, \"3\": {\"rev\": 3, \"vib\": 2}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "80s Gated Timpani & Metal Hit.json"), "{\"name\": \"80s Gated Timpani & Metal Hit\", \"voiceA\": 95, \"voiceB\": 99, \"voiceC\": 76, \"voiceD\": 92, \"posX\": 0.12, \"posY\": 0.31, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.0, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 0, \"vib\": 0}, \"2\": {\"rev\": 1, \"vib\": 0}, \"3\": {\"rev\": 0, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Planet Rock Fairlight Hit.json"), "{\"name\": \"Planet Rock Fairlight Hit\", \"voiceA\": 99, \"voiceB\": 52, \"voiceC\": 95, \"voiceD\": 64, \"posX\": 0.19, \"posY\": 0.69, \"orbitMode\": \"manual\", \"orbitSpeed\": 1.9, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 3, \"vib\": 2}, \"2\": {\"rev\": 0, \"vib\": 0}, \"3\": {\"rev\": 2, \"vib\": 3}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Blue Monday Metallic Clank.json"), "{\"name\": \"Blue Monday Metallic Clank\", \"voiceA\": 40, \"voiceB\": 76, \"voiceC\": 98, \"voiceD\": 41, \"posX\": 0.38, \"posY\": 0.25, \"orbitMode\": \"manual\", \"orbitSpeed\": 1.2, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 1, \"vib\": 0}, \"2\": {\"rev\": 1, \"vib\": 0}, \"3\": {\"rev\": 0, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Sky Sanctuary Harp & Crystal.json"), "{\"name\": \"Sky Sanctuary Harp & Crystal\", \"voiceA\": 37, \"voiceB\": 7, \"voiceC\": 5, \"voiceD\": 34, \"posX\": 0.25, \"posY\": 0.56, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.7, \"channelEffects\": {\"0\": {\"rev\": 3, \"vib\": 2}, \"1\": {\"rev\": 2, \"vib\": 0}, \"2\": {\"rev\": 2, \"vib\": 0}, \"3\": {\"rev\": 2, \"vib\": 3}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Chemical Plant Slap & Synth.json"), "{\"name\": \"Chemical Plant Slap & Synth\", \"voiceA\": 84, \"voiceB\": 92, \"voiceC\": 71, \"voiceD\": 73, \"posX\": 0.31, \"posY\": 0.62, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.1, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 0, \"vib\": 0}, \"2\": {\"rev\": 1, \"vib\": 2}, \"3\": {\"rev\": 1, \"vib\": 1}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Go Straight Rave Stab & Bass.json"), "{\"name\": \"Go Straight Rave Stab & Bass\", \"voiceA\": 92, \"voiceB\": 65, \"voiceC\": 9, \"voiceD\": 99, \"posX\": 0.19, \"posY\": 0.56, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.3, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 1, \"vib\": 2}, \"2\": {\"rev\": 1, \"vib\": 0}, \"3\": {\"rev\": 0, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Magical Sound Steel & Marimba.json"), "{\"name\": \"Magical Sound Steel & Marimba\", \"voiceA\": 43, \"voiceB\": 41, \"voiceC\": 29, \"voiceD\": 65, \"posX\": 0.38, \"posY\": 0.5, \"orbitMode\": \"manual\", \"orbitSpeed\": 2.1, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 0}, \"1\": {\"rev\": 2, \"vib\": 1}, \"2\": {\"rev\": 1, \"vib\": 0}, \"3\": {\"rev\": 2, \"vib\": 3}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Metal Squad Vector Shred.json"), "{\"name\": \"Metal Squad Vector Shred\", \"voiceA\": 15, \"voiceB\": 15, \"voiceC\": 65, \"voiceD\": 73, \"posX\": 0.25, \"posY\": 0.75, \"orbitMode\": \"pingpong\", \"orbitSpeed\": 2.7, \"channelEffects\": {\"0\": {\"rev\": 1, \"vib\": 2}, \"1\": {\"rev\": 1, \"vib\": 2}, \"2\": {\"rev\": 2, \"vib\": 4}, \"3\": {\"rev\": 1, \"vib\": 1}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Big Earl Slap & Clavinet.json"), "{\"name\": \"Big Earl Slap & Clavinet\", \"voiceA\": 84, \"voiceB\": 6, \"voiceC\": 85, \"voiceD\": 45, \"posX\": 0.19, \"posY\": 0.44, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.4, \"channelEffects\": {\"0\": {\"rev\": 0, \"vib\": 0}, \"1\": {\"rev\": 0, \"vib\": 0}, \"2\": {\"rev\": 0, \"vib\": 0}, \"3\": {\"rev\": 1, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Reincarnated Gothic Harpsichord.json"), "{\"name\": \"Reincarnated Gothic Harpsichord\", \"voiceA\": 5, \"voiceB\": 37, \"voiceC\": 34, \"voiceD\": 64, \"posX\": 0.12, \"posY\": 0.69, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.8, \"channelEffects\": {\"0\": {\"rev\": 1, \"vib\": 0}, \"1\": {\"rev\": 2, \"vib\": 1}, \"2\": {\"rev\": 2, \"vib\": 3}, \"3\": {\"rev\": 3, \"vib\": 4}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Algol Battle Vector Brass.json"), "{\"name\": \"Algol Battle Vector Brass\", \"voiceA\": 52, \"voiceB\": 65, \"voiceC\": 50, \"voiceD\": 34, \"posX\": 0.25, \"posY\": 0.62, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.6, \"channelEffects\": {\"0\": {\"rev\": 3, \"vib\": 2}, \"1\": {\"rev\": 2, \"vib\": 4}, \"2\": {\"rev\": 2, \"vib\": 2}, \"3\": {\"rev\": 2, \"vib\": 3}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Final Take Off Arcade Lead.json"), "{\"name\": \"Final Take Off Arcade Lead\", \"voiceA\": 65, \"voiceB\": 15, \"voiceC\": 65, \"voiceD\": 44, \"posX\": 0.31, \"posY\": 0.56, \"orbitMode\": \"pingpong\", \"orbitSpeed\": 2.5, \"channelEffects\": {\"0\": {\"rev\": 2, \"vib\": 4}, \"1\": {\"rev\": 1, \"vib\": 2}, \"2\": {\"rev\": 2, \"vib\": 4}, \"3\": {\"rev\": 3, \"vib\": 0}}}");
                File.WriteAllText(Path.Combine(AppPaths.VectorPresetsDir, "Whirlwind Shakuhachi & Koto.json"), "{\"name\": \"Whirlwind Shakuhachi & Koto\", \"voiceA\": 81, \"voiceB\": 81, \"voiceC\": 37, \"voiceD\": 70, \"posX\": -0.19, \"posY\": 0.69, \"orbitMode\": \"wander\", \"orbitSpeed\": 1.4, \"channelEffects\": {\"0\": {\"rev\": 3, \"vib\": 6}, \"1\": {\"rev\": 3, \"vib\": 6}, \"2\": {\"rev\": 2, \"vib\": 2}, \"3\": {\"rev\": 3, \"vib\": 5}}}");
            }
            catch { }
        }
    }
}