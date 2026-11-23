using System;

namespace PSS790Player.Core
{
    public enum Pss680SysExFormat
    {
        Unknown = -1,
        FmVoiceData = 0x00,
        MelodyMemory = 0x01,
        ChordMemory = 0x02,
        RhythmPattern = 0x03
    }

    public class Pss680SysExPacket
    {
        public Pss680SysExFormat Format { get; set; } = Pss680SysExFormat.Unknown;
        public byte FormatByte { get; set; }
        public byte[] DecodedBytes { get; set; } = Array.Empty<byte>();
        public byte Checksum { get; set; }
        public bool IsChecksumValid { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public static class Pss680SysExParser
    {
        /// <summary>
        /// Calculates the Yamaha 7-bit two's complement checksum across transmitted 4-bit wire data nibbles.
        /// Formula: Checksum = (128 - (Sum(nibbles) &amp; 0x7F)) &amp; 0x7F
        /// </summary>
        public static byte CalculateChecksum(ReadOnlySpan<byte> nibbles)
        {
            int sum = 0;
            for (int i = 0; i < nibbles.Length; i++)
            {
                byte n = nibbles[i];
                if (n > 0x0F)
                {
                    throw new FormatException($"Invalid nibble 0x{n:X2} at index {i}: wire data nibbles must be <= 0x0F.");
                }
                sum += n;
            }
            return (byte)((128 - (sum & 0x7F)) & 0x7F);
        }

        /// <summary>
        /// Calculates the Yamaha 7-bit wire-nibble checksum directly from decoded 8-bit payload bytes without allocating.
        /// Equivalent to CalculateChecksum(Nibblize(decodedBytes)).
        /// </summary>
        public static byte CalculateChecksumFromDecoded(ReadOnlySpan<byte> decodedBytes)
        {
            int sum = 0;
            for (int i = 0; i < decodedBytes.Length; i++)
            {
                byte b = decodedBytes[i];
                sum += (b >> 4) & 0x0F;
                sum += b & 0x0F;
            }
            return (byte)((128 - (sum & 0x7F)) & 0x7F);
        }

        /// <summary>
        /// Splits a byte array into 4-bit nibbles where the high nibble is transmitted first.
        /// </summary>
        public static byte[] Nibblize(ReadOnlySpan<byte> decodedBytes)
        {
            byte[] nibbles = new byte[decodedBytes.Length * 2];
            int outIdx = 0;
            for (int i = 0; i < decodedBytes.Length; i++)
            {
                byte b = decodedBytes[i];
                nibbles[outIdx++] = (byte)((b >> 4) & 0x0F);
                nibbles[outIdx++] = (byte)(b & 0x0F);
            }
            return nibbles;
        }

        /// <summary>
        /// Combines pairs of 4-bit nibbles into bytes where the first nibble is the high 4 bits.
        /// Strictly validates that every wire nibble is &lt;= 0x0F.
        /// </summary>
        public static byte[] Denibblize(ReadOnlySpan<byte> nibbles)
        {
            if (nibbles.Length % 2 != 0)
                throw new ArgumentException("Nibble array length must be even.", nameof(nibbles));

            byte[] decoded = new byte[nibbles.Length / 2];
            int inIdx = 0;
            for (int i = 0; i < decoded.Length; i++)
            {
                byte hi = nibbles[inIdx++];
                byte lo = nibbles[inIdx++];
                if (hi > 0x0F || lo > 0x0F)
                {
                    throw new FormatException($"Corrupt or malformed wire nibble (expected <= 0x0F): hi=0x{hi:X2}, lo=0x{lo:X2} at byte index {i}.");
                }
                decoded[i] = (byte)((hi << 4) | lo);
            }
            return decoded;
        }

        /// <summary>
        /// Identifies, validates, and parses any Yamaha PSS-680 / 480 / 780 SysEx message.
        /// </summary>
        public static Pss680SysExPacket Parse(ReadOnlySpan<byte> rawPacket)
        {
            if (rawPacket.Length < 6)
                throw new ArgumentException("Packet is too short to be a valid PSS SysEx message.", nameof(rawPacket));

            if (rawPacket[0] != 0xF0 || rawPacket[^1] != 0xF7)
                throw new FormatException("Message must start with F0 and end with F7.");

            if (rawPacket[1] != Pss680Constants.ManufacturerYamaha || rawPacket[2] != Pss680Constants.InstrumentClassPss)
                throw new FormatException($"Invalid Yamaha PSS header: Expected F0 43 76, got F0 {rawPacket[1]:X2} {rawPacket[2]:X2}.");

            byte formatByte = rawPacket[3];
            if (formatByte > 0x7F)
                throw new FormatException($"Invalid format byte 0x{formatByte:X2}: must be 7-bit (<= 0x7F).");

            var format = formatByte switch
            {
                0x00 => Pss680SysExFormat.FmVoiceData,
                0x01 => Pss680SysExFormat.MelodyMemory,
                0x02 => Pss680SysExFormat.ChordMemory,
                0x03 => Pss680SysExFormat.RhythmPattern,
                _ => Pss680SysExFormat.Unknown
            };

            int nibbleCount = rawPacket.Length - 6; // Exclude F0, 43, 76, format, checksum, F7
            if (nibbleCount < 2)
                throw new FormatException($"SysEx payload too small: expected at least 2 nibbles, got {nibbleCount}.");

            if (nibbleCount % 2 != 0)
                throw new FormatException($"Odd number of nibbles in SysEx payload: {nibbleCount}.");

            var nibbleSpan = rawPacket.Slice(4, nibbleCount);
            byte[] decoded = Denibblize(nibbleSpan);

            byte receivedChecksum = rawPacket[^2];
            if (receivedChecksum > 0x7F)
                throw new FormatException($"Corrupt checksum byte 0x{receivedChecksum:X2}: must be a 7-bit MIDI data byte (<= 0x7F).");

            byte computedChecksum = CalculateChecksum(nibbleSpan);

            int? expectedDecoded = format switch
            {
                Pss680SysExFormat.FmVoiceData => Pss680Constants.Format00DecodedByteCount,
                Pss680SysExFormat.MelodyMemory => Pss680Constants.Format01DecodedByteCount,
                Pss680SysExFormat.ChordMemory => Pss680Constants.Format02DecodedByteCount,
                Pss680SysExFormat.RhythmPattern => Pss680Constants.Format03DecodedByteCount,
                _ => null
            };

            if (expectedDecoded.HasValue && decoded.Length != expectedDecoded.Value)
            {
                throw new FormatException($"Payload size mismatch for Format 0x{formatByte:X2}: expected {expectedDecoded.Value} decoded bytes, got {decoded.Length}.");
            }

            string bankLabel = decoded.Length > 0 && decoded[0] <= 4
                ? $"Bank {decoded[0] + 1}"
                : $"Bank Raw 0x{(decoded.Length > 0 ? decoded[0] : (byte)0):X2}";

            string description = format switch
            {
                Pss680SysExFormat.FmVoiceData => $"FM 2-Operator Voice Data ({bankLabel})",
                Pss680SysExFormat.MelodyMemory => "Melody Memory (5 Banks)",
                Pss680SysExFormat.ChordMemory => $"Chord Memory Data ({bankLabel})",
                Pss680SysExFormat.RhythmPattern => "Rhythm Pattern Data (Custom Drummer)",
                _ => $"Unknown PSS Format 0x{formatByte:X2}"
            };

            return new Pss680SysExPacket
            {
                Format = format,
                FormatByte = formatByte,
                DecodedBytes = decoded,
                Checksum = receivedChecksum,
                IsChecksumValid = (receivedChecksum == computedChecksum),
                Description = description
            };
        }

        /// <summary>
        /// Creates a full wire-framed SysEx message given a format ID and decoded data bytes.
        /// </summary>
        public static byte[] CreateSysEx(byte formatByte, ReadOnlySpan<byte> decodedBytes)
        {
            if (formatByte > 0x7F)
                throw new ArgumentException($"Invalid format byte 0x{formatByte:X2}: must be 7-bit (<= 0x7F).", nameof(formatByte));

            if (decodedBytes.Length == 0)
                throw new ArgumentException("Decoded bytes payload cannot be empty.", nameof(decodedBytes));

            int expectedLen = formatByte switch
            {
                Pss680Constants.FormatFmVoice => Pss680Constants.Format00DecodedByteCount,
                Pss680Constants.FormatMelodyMemory => Pss680Constants.Format01DecodedByteCount,
                Pss680Constants.FormatChordMemory => Pss680Constants.Format02DecodedByteCount,
                Pss680Constants.FormatRhythmPattern => Pss680Constants.Format03DecodedByteCount,
                _ => -1
            };

            if (expectedLen > 0 && decodedBytes.Length != expectedLen)
            {
                throw new ArgumentException($"Decoded byte count {decodedBytes.Length} does not match expected {expectedLen} for format 0x{formatByte:X2}.", nameof(decodedBytes));
            }

            byte[] nibbles = Nibblize(decodedBytes);
            byte checksum = CalculateChecksum(nibbles);

            byte[] sysex = new byte[4 + nibbles.Length + 2];
            sysex[0] = 0xF0;
            sysex[1] = Pss680Constants.ManufacturerYamaha;
            sysex[2] = Pss680Constants.InstrumentClassPss;
            sysex[3] = formatByte;

            Buffer.BlockCopy(nibbles, 0, sysex, 4, nibbles.Length);
            sysex[^2] = checksum;
            sysex[^1] = 0xF7;

            return sysex;
        }
    }
}
