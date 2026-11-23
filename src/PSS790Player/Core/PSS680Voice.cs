using System;

namespace PSS790Player.Core
{
    public enum FmWaveform
    {
        Sine = 0,
        SquaredSine = 1,
        SineHalfWave = 2,
        SquaredSineHalfWave = 3
    }

    public class FmOperatorParameters
    {
        /// <summary>
        /// Fine Detune (-3 to +3). Stored in DT1 (Bit 6 is sign bit, Bits 5..4 are 2-bit magnitude).
        /// </summary>
        public int FineDetune { get; set; } = 0;

        /// <summary>
        /// Frequency Multiplier (0 to 15). 0 = 0.5x, 1 = 1x, 2 = 2x, etc.
        /// </summary>
        public int Multiplier { get; set; } = 1;

        /// <summary>
        /// Total Level as displayed on front panel (0 to 99, where 99 is maximum volume / modulation).
        /// Hardware SysEx TL is inverted (SysEx TL = 99 - PanelLevel).
        /// </summary>
        public int PanelLevel { get; set; } = 80;

        /// <summary>
        /// Raw SysEx Total Level (0 to 99, where 0 is maximum level and 99 is minimum/attenuated).
        /// </summary>
        public int RawSysExTotalLevel
        {
            get => Math.Clamp(99 - PanelLevel, 0, 99);
            set => PanelLevel = Math.Clamp(99 - value, 0, 99);
        }

        /// <summary>
        /// Level Key Scaling High (0 to 15). Scales notes above C3.
        /// </summary>
        public int LevelKeyScalingHigh { get; set; } = 0;

        /// <summary>
        /// Level Key Scaling Low (0 to 15). Scales notes below C3.
        /// </summary>
        public int LevelKeyScalingLow { get; set; } = 0;

        /// <summary>
        /// Rate Key Scaling (0 to 3). Shortens envelopes on higher pitches.
        /// </summary>
        public int RateKeyScaling { get; set; } = 0;

        /// <summary>
        /// Attack Rate (0 to 63). 0 = slowest, 63 = fastest.
        /// </summary>
        public int AttackRate { get; set; } = 63;

        /// <summary>
        /// Amplitude Modulation Enable (Tremolo LFO toggle for this operator).
        /// </summary>
        public bool AmplitudeModulationEnable { get; set; } = false;

        /// <summary>
        /// Coarse Detune (+600 cents / tritone).
        /// </summary>
        public bool CoarseDetune { get; set; } = false;

        /// <summary>
        /// Decay 1 Rate (0 to 63). Initial decay speed from attack peak to D1L.
        /// </summary>
        public int Decay1Rate { get; set; } = 20;

        /// <summary>
        /// Waveform Table (0 = Sine, 1 = Squared Sine, 2 = Sine Half-Wave, 3 = Squared Sine Half-Wave).
        /// </summary>
        public FmWaveform Waveform { get; set; } = FmWaveform.Sine;

        /// <summary>
        /// Decay 2 Rate (0 to 31). Secondary decay slope toward sustain.
        /// </summary>
        public int Decay2Rate { get; set; } = 0;

        /// <summary>
        /// Decay 1 Level (0 to 15). Breakpoint level between Decay 1 and Decay 2.
        /// </summary>
        public int Decay1Level { get; set; } = 0;

        /// <summary>
        /// Release Rate (0 to 15). Release time after Note-Off when Sustain is OFF.
        /// </summary>
        public int ReleaseRate { get; set; } = 6;

        /// <summary>
        /// Sustain Release Rate (0 to 15). Release time after Note-Off when Sustain is ON.
        /// </summary>
        public int SustainReleaseRate { get; set; } = 5;
    }

    public class PSS680Voice
    {
        public string Name { get; set; } = "Custom Voice";

        /// <summary>
        /// Bank Number (0 to 4 for Banks 1 to 5).
        /// </summary>
        public int BankNumber { get; set; } = 0;

        /// <summary>
        /// Operator 1 (Modulator).
        /// </summary>
        public FmOperatorParameters Modulator { get; set; } = new()
        {
            Multiplier = 1,
            PanelLevel = 50,
            AttackRate = 63,
            Decay1Rate = 25,
            ReleaseRate = 6
        };

        /// <summary>
        /// Operator 2 (Carrier).
        /// </summary>
        public FmOperatorParameters Carrier { get; set; } = new()
        {
            Multiplier = 1,
            PanelLevel = 80,
            AttackRate = 63,
            Decay1Rate = 20,
            ReleaseRate = 6
        };

        /// <summary>
        /// Modulator Self-Feedback Level (0 to 7).
        /// </summary>
        public int Feedback { get; set; } = 0;

        /// <summary>
        /// Pitch Modulation Sensitivity / Vibrato Depth (0 to 7).
        /// </summary>
        public int PitchModulationSensitivity { get; set; } = 0;

        /// <summary>
        /// Amplitude Modulation Sensitivity / Tremolo Depth (0 to 3).
        /// </summary>
        public int AmplitudeModulationSensitivity { get; set; } = 0;

        /// <summary>
        /// Vibrato Delay Time (0 to 127).
        /// </summary>
        public int VibratoDelayTime { get; set; } = 0;

        /// <summary>
        /// Vibrato Enable switch.
        /// </summary>
        public bool Vibrato { get; set; } = false;

        /// <summary>
        /// Sustain Enable switch.
        /// </summary>
        public bool Sustain { get; set; } = false;

        /// <summary>
        /// Encodes this voice into 33 decoded bytes according to Yamaha PSS-680 Format 00H specification.
        /// </summary>
        public byte[] ToDecodedBytes()
        {
            byte[] bytes = new byte[Pss680Constants.Format00DecodedByteCount];

            // Byte 0: Bank Number (0-4)
            bytes[0] = (byte)Math.Clamp(BankNumber, 0, 4);

            // Byte 1 (M) & Byte 2 (C): DT1 (b6..b4) + MUL (b3..b0)
            bytes[1] = EncodeDt1Mul(Modulator);
            bytes[2] = EncodeDt1Mul(Carrier);

            // Byte 3 (M) & Byte 4 (C): TL (b6..b0, 0-99 inverted)
            bytes[3] = (byte)Math.Clamp(Modulator.RawSysExTotalLevel, 0, 99);
            bytes[4] = (byte)Math.Clamp(Carrier.RawSysExTotalLevel, 0, 99);

            // Byte 5 (M) & Byte 6 (C): LKS(HI) (b7..b4) + LKS(LO) (b3..b0)
            bytes[5] = (byte)((Math.Clamp(Modulator.LevelKeyScalingHigh, 0, 15) << 4) | Math.Clamp(Modulator.LevelKeyScalingLow, 0, 15));
            bytes[6] = (byte)((Math.Clamp(Carrier.LevelKeyScalingHigh, 0, 15) << 4) | Math.Clamp(Carrier.LevelKeyScalingLow, 0, 15));

            // Byte 7 (M) & Byte 8 (C): RKS (b7..b6) + AR (b5..b0)
            bytes[7] = (byte)((Math.Clamp(Modulator.RateKeyScaling, 0, 3) << 6) | Math.Clamp(Modulator.AttackRate, 0, 63));
            bytes[8] = (byte)((Math.Clamp(Carrier.RateKeyScaling, 0, 3) << 6) | Math.Clamp(Carrier.AttackRate, 0, 63));

            // Byte 9 (M) & Byte 10 (C): AM EN (b7) + DT2 (b6) + D1R (b5..b0)
            bytes[9] = (byte)((Modulator.AmplitudeModulationEnable ? 0x80 : 0x00) |
                              (Modulator.CoarseDetune ? 0x40 : 0x00) |
                              Math.Clamp(Modulator.Decay1Rate, 0, 63));
            bytes[10] = (byte)((Carrier.AmplitudeModulationEnable ? 0x80 : 0x00) |
                               (Carrier.CoarseDetune ? 0x40 : 0x00) |
                               Math.Clamp(Carrier.Decay1Rate, 0, 63));

            // Byte 11 (M) & Byte 12 (C): SIN TBL (b7..b5) + D2R (b4..b0)
            bytes[11] = (byte)((Math.Clamp((int)Modulator.Waveform, 0, 3) << 5) | Math.Clamp(Modulator.Decay2Rate, 0, 31));
            bytes[12] = (byte)((Math.Clamp((int)Carrier.Waveform, 0, 3) << 5) | Math.Clamp(Carrier.Decay2Rate, 0, 31));

            // Byte 13 (M) & Byte 14 (C): D1L (b7..b4) + RR (b3..b0)
            bytes[13] = (byte)((Math.Clamp(Modulator.Decay1Level, 0, 15) << 4) | Math.Clamp(Modulator.ReleaseRate, 0, 15));
            bytes[14] = (byte)((Math.Clamp(Carrier.Decay1Level, 0, 15) << 4) | Math.Clamp(Carrier.ReleaseRate, 0, 15));

            // Byte 15: FB (b5..b3)
            bytes[15] = (byte)(Math.Clamp(Feedback, 0, 7) << 3);

            // Byte 16: PMS (b6..b4) + AMS (b1..b0)
            bytes[16] = (byte)((Math.Clamp(PitchModulationSensitivity, 0, 7) << 4) | Math.Clamp(AmplitudeModulationSensitivity, 0, 3));

            // Bytes 17-19: Unused (0)

            // Byte 20 (M) & Byte 21 (C): SRR (b3..b0)
            bytes[20] = (byte)Math.Clamp(Modulator.SustainReleaseRate, 0, 15);
            bytes[21] = (byte)Math.Clamp(Carrier.SustainReleaseRate, 0, 15);

            // Byte 22: VDT (b6..b0)
            bytes[22] = (byte)Math.Clamp(VibratoDelayTime, 0, 127);

            // Byte 23: Unused (0)

            // Byte 24: V (b7) + S (b6)
            bytes[24] = (byte)((Vibrato ? 0x80 : 0x00) | (Sustain ? 0x40 : 0x00));

            // Bytes 25-32: Unused (0)

            return bytes;
        }

        /// <summary>
        /// Populates voice parameters from 33 decoded bytes according to Yamaha PSS-680 Format 00H.
        /// </summary>
        public static PSS680Voice FromDecodedBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Pss680Constants.Format00DecodedByteCount)
                throw new ArgumentException($"Decoded voice data must be at least {Pss680Constants.Format00DecodedByteCount} bytes.", nameof(bytes));

            var voice = new PSS680Voice
            {
                BankNumber = bytes[0] > 4 ? 0 : bytes[0]
            };

            // Byte 1 & 2: DT1 & MUL
            DecodeDt1Mul(bytes[1], voice.Modulator);
            DecodeDt1Mul(bytes[2], voice.Carrier);

            // Byte 3 & 4: TL (inverted)
            voice.Modulator.RawSysExTotalLevel = bytes[3] & 0x7F;
            voice.Carrier.RawSysExTotalLevel = bytes[4] & 0x7F;

            // Byte 5 & 6: LKS
            voice.Modulator.LevelKeyScalingHigh = (bytes[5] >> 4) & 0x0F;
            voice.Modulator.LevelKeyScalingLow = bytes[5] & 0x0F;
            voice.Carrier.LevelKeyScalingHigh = (bytes[6] >> 4) & 0x0F;
            voice.Carrier.LevelKeyScalingLow = bytes[6] & 0x0F;

            // Byte 7 & 8: RKS & AR
            voice.Modulator.RateKeyScaling = (bytes[7] >> 6) & 0x03;
            voice.Modulator.AttackRate = bytes[7] & 0x3F;
            voice.Carrier.RateKeyScaling = (bytes[8] >> 6) & 0x03;
            voice.Carrier.AttackRate = bytes[8] & 0x3F;

            // Byte 9 & 10: AM EN, DT2, D1R
            voice.Modulator.AmplitudeModulationEnable = (bytes[9] & 0x80) != 0;
            voice.Modulator.CoarseDetune = (bytes[9] & 0x40) != 0;
            voice.Modulator.Decay1Rate = bytes[9] & 0x3F;

            voice.Carrier.AmplitudeModulationEnable = (bytes[10] & 0x80) != 0;
            voice.Carrier.CoarseDetune = (bytes[10] & 0x40) != 0;
            voice.Carrier.Decay1Rate = bytes[10] & 0x3F;

            // Byte 11 & 12: SIN TBL, D2R
            voice.Modulator.Waveform = (FmWaveform)((bytes[11] >> 5) & 0x03);
            voice.Modulator.Decay2Rate = bytes[11] & 0x1F;

            voice.Carrier.Waveform = (FmWaveform)((bytes[12] >> 5) & 0x03);
            voice.Carrier.Decay2Rate = bytes[12] & 0x1F;

            // Byte 13 & 14: D1L, RR
            voice.Modulator.Decay1Level = (bytes[13] >> 4) & 0x0F;
            voice.Modulator.ReleaseRate = bytes[13] & 0x0F;

            voice.Carrier.Decay1Level = (bytes[14] >> 4) & 0x0F;
            voice.Carrier.ReleaseRate = bytes[14] & 0x0F;

            // Byte 15: FB
            voice.Feedback = (bytes[15] >> 3) & 0x07;

            // Byte 16: PMS, AMS
            voice.PitchModulationSensitivity = (bytes[16] >> 4) & 0x07;
            voice.AmplitudeModulationSensitivity = bytes[16] & 0x03;

            // Byte 20 & 21: SRR
            voice.Modulator.SustainReleaseRate = bytes[20] & 0x0F;
            voice.Carrier.SustainReleaseRate = bytes[21] & 0x0F;

            // Byte 22: VDT
            voice.VibratoDelayTime = bytes[22] & 0x7F;

            // Byte 24: V, S
            voice.Vibrato = (bytes[24] & 0x80) != 0;
            voice.Sustain = (bytes[24] & 0x40) != 0;

            return voice;
        }

        /// <summary>
        /// Converts the voice to a full 72-byte Yamaha SysEx message ready for transmission over MIDI.
        /// High nibble first, low nibble second, followed by 7-bit two's complement checksum and F7.
        /// </summary>
        public byte[] ToSysExMessage()
        {
            byte[] decoded = ToDecodedBytes();
            return Pss680SysExParser.CreateSysEx(Pss680Constants.FormatFmVoice, decoded);
        }

        /// <summary>
        /// Parses and strictly validates a PSS-680 Format 00H SysEx message into a PSS680Voice instance.
        /// Accepts canonical 72-byte wire packets (F0..F7), 71-byte packets (with leading F0 or trailing F7),
        /// as well as 70-byte raw payload without F0 or F7.
        /// </summary>
        public static PSS680Voice FromSysExMessage(byte[] sysex)
        {
            if (sysex == null)
                throw new ArgumentNullException(nameof(sysex));

            bool hasF0 = sysex.Length > 0 && sysex[0] == 0xF0;
            bool hasF7 = sysex.Length > 0 && sysex[^1] == 0xF7;
            int payloadLen = sysex.Length - (hasF0 ? 1 : 0) - (hasF7 ? 1 : 0);

            if (payloadLen != 70)
            {
                throw new ArgumentException($"SysEx FM Voice message must contain 70 payload bytes (got {sysex.Length} total).", nameof(sysex));
            }

            byte[] normalizedBuffer = new byte[72];
            normalizedBuffer[0] = 0xF0;
            normalizedBuffer[71] = 0xF7;
            int srcOffset = hasF0 ? 1 : 0;
            Buffer.BlockCopy(sysex, srcOffset, normalizedBuffer, 1, 70);

            var packet = Pss680SysExParser.Parse(normalizedBuffer);
            if (packet.Format != Pss680SysExFormat.FmVoiceData)
            {
                throw new FormatException($"Expected Format 00H (FM Voice), got 0x{packet.FormatByte:X2}.");
            }

            if (!packet.IsChecksumValid)
            {
                throw new InvalidOperationException($"SysEx checksum verification failed! Received 0x{packet.Checksum:X2}.");
            }

            return FromDecodedBytes(packet.DecodedBytes);
        }

        private static byte EncodeDt1Mul(FmOperatorParameters op)
        {
            int abs = Math.Clamp(Math.Abs(op.FineDetune), 0, 3);
            int sign = op.FineDetune < 0 ? 0x40 : 0x00;
            int dt1 = sign | ((abs & 0x03) << 4);
            int mul = Math.Clamp(op.Multiplier, 0, 15) & 0x0F;
            return (byte)(dt1 | mul);
        }

        private static void DecodeDt1Mul(byte b, FmOperatorParameters op)
        {
            bool isNegative = (b & 0x40) != 0;
            int abs = (b >> 4) & 0x03;
            op.FineDetune = isNegative ? -abs : abs;
            op.Multiplier = b & 0x0F;
        }
    }
}
