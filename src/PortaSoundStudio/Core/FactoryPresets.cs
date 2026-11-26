using System;
using System.Collections.Generic;
using PSS790Player.Models;

namespace PSS790Player.Core
{
    /// <summary>
    /// Authoritative catalog metadata, category groupings, and initial templates for
    /// PSS-680 FM Voice Designer patches and PSS-790 Vector Synthesizer blends.
    /// </summary>
    public static class FactoryPresets
    {
        public static readonly IReadOnlyList<string> FmCategoryOrder = new[]
        {
            "Acoustic Keyboards (Featured)",
            "Custom & Imported Patches",
            "80s Synth Classics",
            "Basses & Leads",
            "Pads & Keys",
            "Chiptune & Sega Genesis",
            "Percussion Hits"
        };

        public static readonly IReadOnlyDictionary<string, string> FmPresetCategories =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Concert Grand Piano"] = "Acoustic Keyboards (Featured)",
                ["Honky-Tonk Upright Piano"] = "Acoustic Keyboards (Featured)",
                ["Take On Me Lead"] = "80s Synth Classics",
                ["Jump OB Brass"] = "80s Synth Classics",
                ["Axel F Brass Lead"] = "80s Synth Classics",
                ["Stranger Things Arp Bass"] = "80s Synth Classics",
                ["Harder Better Lead"] = "80s Synth Classics",
                ["LatelyBass / Solid Bass"] = "Basses & Leads",
                ["Sweet Dreams Pulse"] = "Basses & Leads",
                ["Blue Monday Octaver"] = "Basses & Leads",
                ["Danger Zone FM Slap"] = "Basses & Leads",
                ["Chemical Plant Slap Bass"] = "Basses & Leads",
                ["Go Straight Gritty House Bass"] = "Basses & Leads",
                ["DX7 Ballad Tine EP"] = "Pads & Keys",
                ["Blade Runner CS-80 Pad"] = "Pads & Keys",
                ["Africa Polysynth Pad"] = "Pads & Keys",
                ["Orinoco Shimmer Pad"] = "Pads & Keys",
                ["Sky Sanctuary Crystalline Lead"] = "Chiptune & Sega Genesis",
                ["Green Hill Steel Drum"] = "Chiptune & Sega Genesis",
                ["Last Christmas FM Bell"] = "Chiptune & Sega Genesis",
                ["In the Air Glass Chime"] = "Chiptune & Sega Genesis",
                ["80s DX7 Wood Marimba"] = "Chiptune & Sega Genesis",
                ["80s Gated Noise Snare"] = "Percussion Hits",
                ["Planet Rock Sub Kick"] = "Percussion Hits",
                ["Blue Monday Metallic Clank"] = "Percussion Hits"
            };

        public static readonly IReadOnlyList<string> VectorCategoryOrder = new[]
        {
            "Custom & User Presets",
            "Iconic 80s Soundscapes",
            "Ambient & Ethereal Pads",
            "Bass & Riff Morph",
            "Game & Arcade Morph",
            "Percussive & Mallet Morph"
        };

        public static readonly IReadOnlyDictionary<string, string> VectorPresetCategories =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Take On Me Vector Lead"] = "Iconic 80s Soundscapes",
                ["Jump OB Vector Brass"] = "Iconic 80s Soundscapes",
                ["Ballad Tine & Crystal EP"] = "Iconic 80s Soundscapes",
                ["Axel F Marimba-Brass Lead"] = "Iconic 80s Soundscapes",
                ["Stranger Things Analog Vector"] = "Iconic 80s Soundscapes",
                ["Harder Better Talk-Lead"] = "Iconic 80s Soundscapes",
                ["Blade Runner CS-80 Vector Pad"] = "Ambient & Ethereal Pads",
                ["Africa Kalimba & Polysynth"] = "Ambient & Ethereal Pads",
                ["Orinoco Pizzicato & Choir"] = "Ambient & Ethereal Pads",
                ["Last Christmas Sleigh & Bell"] = "Ambient & Ethereal Pads",
                ["In the Air Glass & Atmosphere"] = "Ambient & Ethereal Pads",
                ["What Is Love Club Bass"] = "Bass & Riff Morph",
                ["Sweet Dreams Vector Pulse"] = "Bass & Riff Morph",
                ["Blue Monday Choir & Bass"] = "Bass & Riff Morph",
                ["Danger Zone Slap & Brass"] = "Bass & Riff Morph",
                ["Chemical Plant Slap & Synth"] = "Bass & Riff Morph",
                ["Go Straight Rave Stab & Bass"] = "Bass & Riff Morph",
                ["Big Earl Slap & Clavinet"] = "Bass & Riff Morph",
                ["Sky Sanctuary Harp & Crystal"] = "Game & Arcade Morph",
                ["Green Hill Steel Drum Ensemble"] = "Game & Arcade Morph",
                ["Magical Sound Steel & Marimba"] = "Game & Arcade Morph",
                ["Metal Squad Vector Shred"] = "Game & Arcade Morph",
                ["Reincarnated Gothic Harpsichord"] = "Game & Arcade Morph",
                ["Algol Battle Vector Brass"] = "Game & Arcade Morph",
                ["Final Take Off Arcade Lead"] = "Game & Arcade Morph",
                ["Whirlwind Shakuhachi & Koto"] = "Game & Arcade Morph",
                ["80s Wood Marimba & Vibe"] = "Percussive & Mallet Morph",
                ["80s Gated Timpani & Metal Hit"] = "Percussive & Mallet Morph",
                ["Planet Rock Fairlight Hit"] = "Percussive & Mallet Morph",
                ["Blue Monday Metallic Clank"] = "Percussive & Mallet Morph"
            };

        public static string GetFmCategory(string? voiceName) =>
            !string.IsNullOrWhiteSpace(voiceName) && FmPresetCategories.TryGetValue(voiceName.Trim(), out var cat)
                ? cat
                : "Custom & Imported Patches";

        public static string GetVectorCategory(string? presetName) =>
            !string.IsNullOrWhiteSpace(presetName) && VectorPresetCategories.TryGetValue(presetName.Trim(), out var cat)
                ? cat
                : "Custom & User Presets";

        public static FmVoiceState CreateInitFmVoice(int bankNumber = 0) => new()
        {
            Name = "Init (2-Op Sine)",
            BankNumber = Math.Clamp(bankNumber, 0, 4),
            Feedback = 0,
            PitchModulationSensitivity = 0,
            AmplitudeModulationSensitivity = 0,
            VibratoDelayTime = 0,
            Vibrato = false,
            Sustain = false,
            Modulator = new FmOperatorState
            {
                Multiplier = 1,
                PanelLevel = 50,
                Waveform = 0,
                AttackRate = 63,
                Decay1Rate = 25,
                Decay1Level = 0,
                Decay2Rate = 0,
                ReleaseRate = 6,
                SustainReleaseRate = 5
            },
            Carrier = new FmOperatorState
            {
                Multiplier = 1,
                PanelLevel = 80,
                Waveform = 0,
                AttackRate = 63,
                Decay1Rate = 20,
                Decay1Level = 0,
                Decay2Rate = 0,
                ReleaseRate = 6,
                SustainReleaseRate = 5
            }
        };

        public static FmVoiceState CreateRandomFmVoice(int bankNumber = 0)
        {
            var rng = Random.Shared;
            int[] fbChoices = { 0, 1, 2, 3, 4, 5 };
            int[] pmsChoices = { 0, 1, 2, 3 };
            int[] amsChoices = { 0, 0, 1 };
            int[] modMulChoices = { 1, 1, 2, 2, 3, 4, 5, 7, 11 };
            int[] carMulChoices = { 1, 1, 1, 2, 3, 4 };
            int[] waveChoices = { 0, 0, 1, 2, 3 };
            int[] fineChoices = { -1, 0, 0, 0, 1 };
            int[] lksHiChoices = { 0, 0, 2, 4 };

            return new FmVoiceState
            {
                Name = $"FM Rand {rng.Next(10, 100)}",
                BankNumber = Math.Clamp(bankNumber, 0, 4),
                Feedback = fbChoices[rng.Next(fbChoices.Length)],
                PitchModulationSensitivity = pmsChoices[rng.Next(pmsChoices.Length)],
                AmplitudeModulationSensitivity = amsChoices[rng.Next(amsChoices.Length)],
                VibratoDelayTime = rng.Next(0, 51),
                Vibrato = rng.NextDouble() > 0.6,
                Sustain = rng.NextDouble() > 0.5,
                Modulator = new FmOperatorState
                {
                    Multiplier = modMulChoices[rng.Next(modMulChoices.Length)],
                    PanelLevel = rng.Next(25, 86),
                    Waveform = waveChoices[rng.Next(waveChoices.Length)],
                    AttackRate = rng.Next(35, 64),
                    Decay1Rate = rng.Next(10, 46),
                    ReleaseRate = rng.Next(4, 10),
                    FineDetune = fineChoices[rng.Next(fineChoices.Length)],
                    CoarseDetune = rng.NextDouble() > 0.85,
                    Decay1Level = rng.Next(0, 7),
                    Decay2Rate = rng.Next(0, 16),
                    SustainReleaseRate = rng.Next(3, 8),
                    LevelKeyScalingHigh = lksHiChoices[rng.Next(lksHiChoices.Length)],
                    LevelKeyScalingLow = 0,
                    RateKeyScaling = rng.Next(2),
                    AmplitudeModulationEnable = rng.NextDouble() > 0.8
                },
                Carrier = new FmOperatorState
                {
                    Multiplier = carMulChoices[rng.Next(carMulChoices.Length)],
                    PanelLevel = rng.Next(70, 96),
                    Waveform = rng.NextDouble() > 0.7 ? rng.Next(1, 3) : 0,
                    AttackRate = rng.Next(40, 64),
                    Decay1Rate = rng.Next(12, 36),
                    ReleaseRate = rng.Next(4, 9),
                    FineDetune = 0,
                    CoarseDetune = false,
                    Decay1Level = rng.Next(0, 5),
                    Decay2Rate = rng.Next(0, 11),
                    SustainReleaseRate = rng.Next(4, 8),
                    LevelKeyScalingHigh = rng.NextDouble() > 0.7 ? 2 : 0,
                    LevelKeyScalingLow = 0,
                    RateKeyScaling = rng.Next(2),
                    AmplitudeModulationEnable = false
                }
            };
        }

        public static List<VoiceOptionDto> BuildVoiceCatalog(string model)
        {
            bool is680 = string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase);
            var list = new List<VoiceOptionDto>();
            if (is680)
            {
                foreach (var kv in Pss680Constants.Voices)
                {
                    list.Add(new VoiceOptionDto
                    {
                        Number = kv.Key,
                        Name = kv.Value.Name,
                        Category = kv.Value.Category,
                        MidiProgram = (byte)Math.Clamp(kv.Key, 0, 127)
                    });
                }
            }
            else
            {
                foreach (var kv in Constants.Pss790Voices)
                {
                    list.Add(new VoiceOptionDto
                    {
                        Number = kv.Key,
                        Name = kv.Value.Name,
                        Category = kv.Value.Category,
                        MidiProgram = kv.Value.MidiProgram
                    });
                }
            }
            list.Sort((a, b) => a.Number.CompareTo(b.Number));
            return list;
        }
    }
}
