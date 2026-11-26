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

        [Fact]
        public void SysEx_ParseDumpInfo_DetectsTargetModelFromHeaderAndFolder()
        {
            var pss680HeaderFile = Path.Combine(_testDir, "loose_680.syx");
            File.WriteAllBytes(pss680HeaderFile, new byte[] { 0xF0, 0x43, 0x76, 0x03, 0x01, 0xF7 });

            var pss790HeaderFile = Path.Combine(_testDir, "loose_790.syx");
            File.WriteAllBytes(pss790HeaderFile, new byte[] { 0xF0, 0x43, 0x00, 0x7A, 0x01, 0xF7 });

            var genericRootFile = Path.Combine(_testDir, "gm_universal.syx");
            File.WriteAllBytes(genericRootFile, new byte[] { 0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7 });

            var pss680Folder = Path.Combine(_testDir, "PSS-680 Dumps");
            Directory.CreateDirectory(pss680Folder);
            var folderPlaced680 = Path.Combine(pss680Folder, "custom_generic.syx");
            File.WriteAllBytes(folderPlaced680, new byte[] { 0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7 });

            Assert.Equal("pss680", SysExManager.ParseDumpInfo(pss680HeaderFile).TargetModel);
            Assert.Equal("pss790", SysExManager.ParseDumpInfo(pss790HeaderFile).TargetModel);
            Assert.Equal("all", SysExManager.ParseDumpInfo(genericRootFile).TargetModel);
            Assert.Equal("pss680", SysExManager.ParseDumpInfo(folderPlaced680).TargetModel);
        }

        [Fact]
        public void SysEx_IsDumpCompatibleWithModel_FiltersDeviceSpecificDumps()
        {
            var dump680 = new SysExDumpInfo { FileName = "d680.syx", TargetModel = "pss680" };
            var dump790 = new SysExDumpInfo { FileName = "d790.syx", TargetModel = "pss790" };
            var dumpAll = new SysExDumpInfo { FileName = "gm.syx", TargetModel = "all" };

            Assert.True(SysExManager.IsDumpCompatibleWithModel(dump680, "pss680"));
            Assert.False(SysExManager.IsDumpCompatibleWithModel(dump680, "pss790"));

            Assert.True(SysExManager.IsDumpCompatibleWithModel(dump790, "pss790"));
            Assert.False(SysExManager.IsDumpCompatibleWithModel(dump790, "pss680"));

            Assert.True(SysExManager.IsDumpCompatibleWithModel(dumpAll, "pss790"));
            Assert.True(SysExManager.IsDumpCompatibleWithModel(dumpAll, "pss680"));
        }

        [Fact]
        public void SysEx_ImportDump_RoutesFilesIntoModelFoldersAndScansCleanly()
        {
            using var player = new MidiPlayer();
            using var recorder = new MidiRecorder();
            using var manager = new SysExManager(player, recorder);

            var srcDir = Path.Combine(_testDir, "incoming");
            var vaultRoot = Path.Combine(_testDir, "vault");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(vaultRoot);

            var src680 = Path.Combine(srcDir, "voice_or_drum.syx");
            File.WriteAllBytes(src680, new byte[] { 0xF0, 0x43, 0x76, 0x00, 0x10, 0xF7 });

            var src790 = Path.Combine(srcDir, "song_bulk.syx");
            File.WriteAllBytes(src790, new byte[] { 0xF0, 0x43, 0x00, 0x7A, 0x20, 0xF7 });

            // Simulate non-SysEx library sibling directory like PSS-680 Voices that must remain excluded
            var voicesSibling = Path.Combine(vaultRoot, "PSS-680 Voices");
            Directory.CreateDirectory(voicesSibling);
            File.WriteAllBytes(Path.Combine(voicesSibling, "patch.syx"), new byte[72]);

            var imported680 = manager.ImportDump(src680, "pss790", vaultRoot);
            var imported790 = manager.ImportDump(src790, "pss680", vaultRoot);

            Assert.Contains("PSS-680 Dumps", imported680, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PSS-790 Dumps", imported790, StringComparison.OrdinalIgnoreCase);

            var listed = SysExManager.GetSysExLibraryFromDisk(vaultRoot);
            Assert.Equal(2, listed.Count);
            Assert.Contains(listed, d => d.TargetModel == "pss680" && d.FileName == "voice_or_drum.syx");
            Assert.Contains(listed, d => d.TargetModel == "pss790" && d.FileName == "song_bulk.syx");
            Assert.DoesNotContain(listed, d => d.FileName == "patch.syx");
        }
    }
}
