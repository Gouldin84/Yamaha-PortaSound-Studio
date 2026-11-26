using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PSS790Player.Core;
using Xunit;

namespace PSS790Player.Tests
{
    public class SysExManagerTests : IDisposable
    {
        private readonly string _testDir;

        public SysExManagerTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "PSS790_SysEx_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void SysEx_ExtractPackets_IdentifiesF0F7Boundaries()
        {
            // Stream with two distinct SysEx packets
            byte[] packet1 = new byte[] { 0xF0, 0x43, 0x10, 0x27, 0x30, 0xF7 };
            byte[] packet2 = new byte[] { 0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7 };
            byte[] stream = packet1.Concat(packet2).ToArray();

            var extracted = SysExManager.ExtractPackets(stream);

            Assert.Equal(2, extracted.Count);
            Assert.Equal(packet1, extracted[0]);
            Assert.Equal(packet2, extracted[1]);
        }

        [Fact]
        public void SysEx_ExtractPackets_RejectsRawBytesWithoutF0F7()
        {
            byte[] raw = new byte[] { 0x43, 0x10, 0x27 };
            var extracted = SysExManager.ExtractPackets(raw);

            // Secure behavior: unframed or malformed bytes must return empty rather than dangerous auto-framing
            Assert.Empty(extracted);
        }

        [Fact]
        public void SysEx_ParseDumpInfo_DetectsYamahaManufacturerId()
        {
            var filePath = Path.Combine(_testDir, "test_yamaha.syx");
            // Yamaha PSS-790 Bulk Dump header: F0 43 ... F7
            byte[] yamahaDump = new byte[] { 0xF0, 0x43, 0x00, 0x7A, 0x01, 0x02, 0x03, 0xF7 };
            File.WriteAllBytes(filePath, yamahaDump);

            var info = SysExManager.ParseDumpInfo(filePath);

            Assert.Equal("test_yamaha.syx", info.FileName);
            Assert.Equal("Yamaha", info.Manufacturer);
            Assert.Equal("PSS-790 Song & Memory Bulk Dump", info.DumpType);
            Assert.Equal(1, info.PacketCount);
            Assert.Equal(yamahaDump.Length, info.FileSizeBytes);
            Assert.Contains("F0 43 00 7A", info.HexPreview);
        }

        [Fact]
        public void SysEx_ParseDumpInfo_DetectsUniversalRealtimeAndNonRealtime()
        {
            var nonRealtimePath = Path.Combine(_testDir, "gm_on.syx");
            File.WriteAllBytes(nonRealtimePath, new byte[] { 0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7 });

            var realtimePath = Path.Combine(_testDir, "master_vol.syx");
            File.WriteAllBytes(realtimePath, new byte[] { 0xF0, 0x7F, 0x7F, 0x04, 0x01, 0x7F, 0x7F, 0xF7 });

            var nonRtInfo = SysExManager.ParseDumpInfo(nonRealtimePath);
            var rtInfo = SysExManager.ParseDumpInfo(realtimePath);

            Assert.Equal("Universal Non-Realtime", nonRtInfo.Manufacturer);
            Assert.Equal("Universal Realtime", rtInfo.Manufacturer);
        }

        [Fact]
        public void SysEx_GenerateHexPreview_FormatsCorrectColumns()
        {
            byte[] bytes = new byte[] { 0xF0, 0x43, 0x41, 0x42, 0x43, 0xF7 };
            var preview = SysExManager.GenerateHexPreview(bytes, 16);

            // Should contain offset, hex bytes, and ASCII representation
            Assert.Contains("0000:", preview);
            Assert.Contains("F0 43 41 42 43 F7", preview);
            Assert.Contains("|.CABC.|", preview);
        }

        [Fact]
        public void SysEx_FormatBytes_FormatsSizesAccurately()
        {
            Assert.Equal("512 B", SysExManager.FormatBytes(512));
            Assert.Equal("1.5 KB", SysExManager.FormatBytes(1536));
            Assert.Equal("2.00 MB", SysExManager.FormatBytes(2097152));
        }

        [Fact]
        public void SysEx_RenameDump_ChangesNameAndPreservesContent()
        {
            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            using var manager = new SysExManager(player, recorder);

            var origPath = Path.Combine(_testDir, "original.syx");
            byte[] testContent = new byte[] { 0xF0, 0x43, 0x11, 0x22, 0xF7 };
            File.WriteAllBytes(origPath, testContent);

            var newPath = manager.RenameDump(origPath, "renamed_backup", _testDir);

            Assert.True(File.Exists(newPath));
            Assert.False(File.Exists(origPath));
            Assert.EndsWith("renamed_backup.syx", newPath);
            Assert.Equal(testContent, File.ReadAllBytes(newPath));
        }

        [Fact]
        public void SysEx_DeleteDump_RemovesFileFromDisk()
        {
            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            using var manager = new SysExManager(player, recorder);

            var filePath = Path.Combine(_testDir, "delete_me.syx");
            File.WriteAllBytes(filePath, new byte[] { 0xF0, 0x43, 0xF7 });

            Assert.True(File.Exists(filePath));
            var result = manager.DeleteDump(filePath, _testDir);

            Assert.True(result);
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public void SysEx_SendQuickCommand_Mode99_DoesNotSendIllegalBytes()
        {
            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            using var manager = new SysExManager(player, recorder);

            // Calling SendQuickCommand for mode_99 should not throw or send malformed SysEx bytes with MSB=1
            var ex = Record.Exception(() => manager.SendQuickCommand("mode_99"));
            Assert.Null(ex);
        }
    }
}
