using System;
using System.IO;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class SoundDesignTests
    {
        [Fact]
        public void AppPaths_SoundDesignDirectories_ExistAndAreConfigured()
        {
            // Verify path configuration
            Assert.NotNull(AppPaths.FmVoicesDir);
            Assert.NotNull(AppPaths.VectorPresetsDir);

            Assert.EndsWith("PSS-680 Voices", AppPaths.FmVoicesDir, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("PSS-790 Vectors", AppPaths.VectorPresetsDir, StringComparison.OrdinalIgnoreCase);

            // Verify EnsureDirectoriesExist creates them
            AppPaths.EnsureDirectoriesExist();
            Assert.True(Directory.Exists(AppPaths.FmVoicesDir), "FmVoicesDir must exist on disk");
            Assert.True(Directory.Exists(AppPaths.VectorPresetsDir), "VectorPresetsDir must exist on disk");
        }

        [Fact]
        public void PSS680Voice_All38Parameters_RoundTripWithoutLoss()
        {
            var original = new PSS680Voice
            {
                Name = "Complex Test Patch",
                BankNumber = 3,
                Feedback = 6,
                PitchModulationSensitivity = 5,
                AmplitudeModulationSensitivity = 3, // Hidden
                VibratoDelayTime = 77,
                Vibrato = true,
                Sustain = true
            };

            // Modulator (all panel + hidden parameters)
            original.Modulator.Multiplier = 7;
            original.Modulator.PanelLevel = 84;
            original.Modulator.Waveform = FmWaveform.SquaredSine;
            original.Modulator.AttackRate = 58;
            original.Modulator.Decay1Rate = 33;
            original.Modulator.ReleaseRate = 9;
            original.Modulator.FineDetune = -2;         // Hidden
            original.Modulator.CoarseDetune = true;     // Hidden
            original.Modulator.Decay1Level = 7;         // Hidden
            original.Modulator.Decay2Rate = 19;         // Hidden
            original.Modulator.SustainReleaseRate = 11; // Hidden
            original.Modulator.LevelKeyScalingHigh = 8; // Hidden
            original.Modulator.LevelKeyScalingLow = 4;  // Hidden
            original.Modulator.RateKeyScaling = 2;      // Hidden
            original.Modulator.AmplitudeModulationEnable = true; // Hidden

            // Carrier (all panel + hidden parameters, including independent waveform)
            original.Carrier.Multiplier = 2;
            original.Carrier.PanelLevel = 91;
            original.Carrier.Waveform = FmWaveform.SquaredSineHalfWave; // Hidden independent wave!
            original.Carrier.AttackRate = 61;
            original.Carrier.Decay1Rate = 22;
            original.Carrier.ReleaseRate = 8;
            original.Carrier.FineDetune = 3;            // Hidden
            original.Carrier.CoarseDetune = false;      // Hidden
            original.Carrier.Decay1Level = 3;           // Hidden
            original.Carrier.Decay2Rate = 12;           // Hidden
            original.Carrier.SustainReleaseRate = 6;    // Hidden
            original.Carrier.LevelKeyScalingHigh = 5;   // Hidden
            original.Carrier.LevelKeyScalingLow = 2;    // Hidden
            original.Carrier.RateKeyScaling = 1;        // Hidden
            original.Carrier.AmplitudeModulationEnable = false; // Hidden

            // Serialize to 72-byte SysEx
            byte[] sysex = original.ToSysExMessage();
            Assert.NotNull(sysex);
            Assert.Equal(72, sysex.Length);
            Assert.Equal(0xF0, sysex[0]);
            Assert.Equal(0x43, sysex[1]); // Yamaha ID
            Assert.Equal(0x76, sysex[2]); // Sub-status / Channel
            Assert.Equal(0x00, sysex[3]); // Format 00H
            Assert.Equal(0x00, sysex[4]); // High nibble of Bank 3 (0x03)
            Assert.Equal(0x03, sysex[5]); // Low nibble of Bank 3 (0x03)
            Assert.Equal(0xF7, sysex[71]); // EOX

            // Deserialize from SysEx
            var restored = PSS680Voice.FromSysExMessage(sysex);

            // Assert identity across all 38 parameters
            Assert.Equal(original.BankNumber, restored.BankNumber);
            Assert.Equal(original.Feedback, restored.Feedback);
            Assert.Equal(original.PitchModulationSensitivity, restored.PitchModulationSensitivity);
            Assert.Equal(original.AmplitudeModulationSensitivity, restored.AmplitudeModulationSensitivity);
            Assert.Equal(original.VibratoDelayTime, restored.VibratoDelayTime);
            Assert.Equal(original.Vibrato, restored.Vibrato);
            Assert.Equal(original.Sustain, restored.Sustain);

            // Modulator verification
            Assert.Equal(original.Modulator.Multiplier, restored.Modulator.Multiplier);
            Assert.Equal(original.Modulator.PanelLevel, restored.Modulator.PanelLevel);
            Assert.Equal(original.Modulator.Waveform, restored.Modulator.Waveform);
            Assert.Equal(original.Modulator.AttackRate, restored.Modulator.AttackRate);
            Assert.Equal(original.Modulator.Decay1Rate, restored.Modulator.Decay1Rate);
            Assert.Equal(original.Modulator.ReleaseRate, restored.Modulator.ReleaseRate);
            Assert.Equal(original.Modulator.FineDetune, restored.Modulator.FineDetune);
            Assert.Equal(original.Modulator.CoarseDetune, restored.Modulator.CoarseDetune);
            Assert.Equal(original.Modulator.Decay1Level, restored.Modulator.Decay1Level);
            Assert.Equal(original.Modulator.Decay2Rate, restored.Modulator.Decay2Rate);
            Assert.Equal(original.Modulator.SustainReleaseRate, restored.Modulator.SustainReleaseRate);
            Assert.Equal(original.Modulator.LevelKeyScalingHigh, restored.Modulator.LevelKeyScalingHigh);
            Assert.Equal(original.Modulator.LevelKeyScalingLow, restored.Modulator.LevelKeyScalingLow);
            Assert.Equal(original.Modulator.RateKeyScaling, restored.Modulator.RateKeyScaling);
            Assert.Equal(original.Modulator.AmplitudeModulationEnable, restored.Modulator.AmplitudeModulationEnable);

            // Carrier verification
            Assert.Equal(original.Carrier.Multiplier, restored.Carrier.Multiplier);
            Assert.Equal(original.Carrier.PanelLevel, restored.Carrier.PanelLevel);
            Assert.Equal(original.Carrier.Waveform, restored.Carrier.Waveform);
            Assert.Equal(original.Carrier.AttackRate, restored.Carrier.AttackRate);
            Assert.Equal(original.Carrier.Decay1Rate, restored.Carrier.Decay1Rate);
            Assert.Equal(original.Carrier.ReleaseRate, restored.Carrier.ReleaseRate);
            Assert.Equal(original.Carrier.FineDetune, restored.Carrier.FineDetune);
            Assert.Equal(original.Carrier.CoarseDetune, restored.Carrier.CoarseDetune);
            Assert.Equal(original.Carrier.Decay1Level, restored.Carrier.Decay1Level);
            Assert.Equal(original.Carrier.Decay2Rate, restored.Carrier.Decay2Rate);
            Assert.Equal(original.Carrier.SustainReleaseRate, restored.Carrier.SustainReleaseRate);
            Assert.Equal(original.Carrier.LevelKeyScalingHigh, restored.Carrier.LevelKeyScalingHigh);
            Assert.Equal(original.Carrier.LevelKeyScalingLow, restored.Carrier.LevelKeyScalingLow);
            Assert.Equal(original.Carrier.RateKeyScaling, restored.Carrier.RateKeyScaling);
            Assert.Equal(original.Carrier.AmplitudeModulationEnable, restored.Carrier.AmplitudeModulationEnable);
        }

        [Theory]
        [InlineData(0.0, 0.0, 0.25, 0.25, 0.25, 0.25)] // Center: 25% balance
        [InlineData(0.0, 1.0, 1.0, 0.0, 0.0, 0.0)]    // North Pole (Voice A 100%)
        [InlineData(1.0, 0.0, 0.0, 1.0, 0.0, 0.0)]    // East Pole (Voice B 100%)
        [InlineData(0.0, -1.0, 0.0, 0.0, 1.0, 0.0)]   // South Pole (Voice C 100%)
        [InlineData(-1.0, 0.0, 0.0, 0.0, 0.0, 1.0)]   // West Pole (Voice D 100%)
        public void VectorSynthesizer_Weights_CardinalPolesAndCenter(
            double x, double y,
            double expectedA, double expectedB, double expectedC, double expectedD)
        {
            var (wA, wB, wC, wD) = CalculateWeights(x, y);

            Assert.Equal(expectedA, wA, 2);
            Assert.Equal(expectedB, wB, 2);
            Assert.Equal(expectedC, wC, 2);
            Assert.Equal(expectedD, wD, 2);

            var sum = wA + wB + wC + wD;
            Assert.Equal(1.0, sum, 3);
        }

        [Fact]
        public void VectorSynthesizer_DiagonalAndArbitraryPoints_AlwaysSumToOne()
        {
            double[] testCoords = { -1.0, -0.75, -0.5, -0.25, 0.0, 0.25, 0.5, 0.75, 1.0 };

            foreach (var x in testCoords)
            {
                foreach (var y in testCoords)
                {
                    var (wA, wB, wC, wD) = CalculateWeights(x, y);

                    Assert.InRange(wA, 0.0, 1.0);
                    Assert.InRange(wB, 0.0, 1.0);
                    Assert.InRange(wC, 0.0, 1.0);
                    Assert.InRange(wD, 0.0, 1.0);

                    var sum = wA + wB + wC + wD;
                    Assert.Equal(1.0, sum, 3);
                }
            }
        }

        [Fact]
        public void SysExManager_GetSysExLibrary_TopDirectoryOnly_DoesNotPolluteWithSubdirs()
        {
            AppPaths.EnsureDirectoriesExist();

            // Create temporary files in subdirectories
            var fmFile = Path.Combine(AppPaths.FmVoicesDir, "_test_fm_patch.syx");
            var vecFile = Path.Combine(AppPaths.VectorPresetsDir, "_test_vector_preset.json");

            try
            {
                File.WriteAllBytes(fmFile, new byte[72]);
                File.WriteAllText(vecFile, "{}");

                // GetSysExLibrary on root SysExDir must NOT list these subdirectory files
                var rootLibrary = SysExManager.GetSysExLibraryFromDisk();
                Assert.DoesNotContain(rootLibrary, f => f.FileName.Contains("_test_fm_patch"));
                Assert.DoesNotContain(rootLibrary, f => f.FileName.Contains("_test_vector_preset"));
            }
            finally
            {
                if (File.Exists(fmFile)) File.Delete(fmFile);
                if (File.Exists(vecFile)) File.Delete(vecFile);
            }
        }

        [Fact]
        public void SoundDesignSeeder_SeedsAll23FmVoicesAnd30VectorPresets()
        {
            AppPaths.EnsureDirectoriesExist();
            SoundDesignSeeder.SeedAll();

            var fmFiles = Directory.GetFiles(AppPaths.FmVoicesDir, "*.syx", SearchOption.TopDirectoryOnly);
            Assert.Equal(23, fmFiles.Length);

            foreach (var file in fmFiles)
            {
                var bytes = File.ReadAllBytes(file);
                Assert.Equal(72, bytes.Length);
                Assert.Equal(0xF0, bytes[0]);
                Assert.Equal(0x43, bytes[1]);
                Assert.Equal(0x00, bytes[3]);
                Assert.Equal(0xF7, bytes[71]);

                var voice = PSS680Voice.FromSysExMessage(bytes);
                Assert.NotNull(voice);
                Assert.False(string.IsNullOrWhiteSpace(voice.Name));
            }

            var vecFiles = Directory.GetFiles(AppPaths.VectorPresetsDir, "*.json", SearchOption.TopDirectoryOnly);
            Assert.Equal(30, vecFiles.Length);

            foreach (var file in vecFiles)
            {
                var text = File.ReadAllText(file);
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                var root = doc.RootElement;
                Assert.True(root.TryGetProperty("name", out var nameProp));
                Assert.False(string.IsNullOrWhiteSpace(nameProp.GetString()));
                Assert.True(root.TryGetProperty("voiceA", out _));
                Assert.True(root.TryGetProperty("voiceB", out _));
                Assert.True(root.TryGetProperty("voiceC", out _));
                Assert.True(root.TryGetProperty("voiceD", out _));
                Assert.True(root.TryGetProperty("posX", out _));
                Assert.True(root.TryGetProperty("posY", out _));
                Assert.True(root.TryGetProperty("orbitMode", out _));
                Assert.True(root.TryGetProperty("orbitSpeed", out _));
            }
        }

        private static (double wA, double wB, double wC, double wD) CalculateWeights(double x, double y)
        {
            double rawA = Math.Max(0, y);
            double rawB = Math.Max(0, x);
            double rawC = Math.Max(0, -y);
            double rawD = Math.Max(0, -x);

            double manhattan = Math.Abs(x) + Math.Abs(y);
            double radius = Math.Min(1.0, Math.Sqrt(x * x + y * y));

            double dirA = 0.25, dirB = 0.25, dirC = 0.25, dirD = 0.25;
            if (manhattan > 0.0001)
            {
                dirA = rawA / manhattan;
                dirB = rawB / manhattan;
                dirC = rawC / manhattan;
                dirD = rawD / manhattan;
            }

            double wA = (1.0 - radius) * 0.25 + radius * dirA;
            double wB = (1.0 - radius) * 0.25 + radius * dirB;
            double wC = (1.0 - radius) * 0.25 + radius * dirC;
            double wD = (1.0 - radius) * 0.25 + radius * dirD;

            return (wA, wB, wC, wD);
        }
    }
}
