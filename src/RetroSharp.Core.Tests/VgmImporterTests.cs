namespace RetroSharp.Core.Tests;

using System.IO.Compression;
using RetroSharp.Core.Sdk;
using Xunit;

public sealed class VgmImporterTests
{
    [Fact]
    public void Imports_game_boy_dmg_writes_from_vgm()
    {
        var path = WriteVgm("stage.vgm", chipClockOffset: 0x80, chipClockHz: 4_194_304, 0xB3, [0x14, 0x87]);

        var stream = VgmImporter.Import(path, VgmChip.GameBoyDmg);

        Assert.Equal(4_194_304, stream.ChipClockHz);
        Assert.Collection(
            stream.Frames,
            frame =>
            {
                Assert.Equal(0, frame.Index);
                var write = Assert.Single(frame.Writes);
                Assert.Equal(0xFF24, write.Address);
                Assert.Equal(0x87, write.Value);
            });
    }

    [Fact]
    public void Imports_nes_2a03_writes_from_vgz_and_quantizes_waits_to_frames()
    {
        var vgm = BuildVgm(chipClockOffset: 0x84, chipClockHz: 1_789_773, 0xB4, [0x00, 0x30], waitSamples: 735, [0x04, 0x7F]);
        var path = WriteGzip("stage.vgz", vgm);

        var stream = VgmImporter.Import(path, VgmChip.Nes2A03);

        Assert.Equal(1_789_773, stream.ChipClockHz);
        Assert.Collection(
            stream.Frames,
            frame =>
            {
                Assert.Equal(0, frame.Index);
                var write = Assert.Single(frame.Writes);
                Assert.Equal(0x4000, write.Address);
                Assert.Equal(0x30, write.Value);
            },
            frame =>
            {
                Assert.Equal(1, frame.Index);
                var write = Assert.Single(frame.Writes);
                Assert.Equal(0x4004, write.Address);
                Assert.Equal(0x7F, write.Value);
            });
    }

    [Fact]
    public void Skips_nes_dpcm_data_blocks_and_dpcm_register_writes_for_v1_four_channel_bgm()
    {
        var vgm = BuildVgm(
            chipClockOffset: 0x84,
            chipClockHz: 1_789_773,
            0xB4,
            [0x10, 0x0F],
            waitSamples: 0,
            dataBlockType: 0xC2,
            dataBlock: [0xAA, 0xBB],
            [0x00, 0x30]);
        var path = WriteGzip("dpcm.vgz", vgm);

        var stream = VgmImporter.Import(path, VgmChip.Nes2A03);

        var frame = Assert.Single(stream.Frames);
        var write = Assert.Single(frame.Writes);
        Assert.Equal(0x4000, write.Address);
        Assert.Equal(0x30, write.Value);
    }

    [Fact]
    public void Unsupported_chip_write_fails_with_clear_message()
    {
        var path = WriteVgm("ym.vgm", chipClockOffset: 0x80, chipClockHz: 4_194_304, 0x52, [0x22, 0x33]);

        var exception = Assert.Throws<InvalidOperationException>(() => VgmImporter.Import(path, VgmChip.GameBoyDmg));

        Assert.Contains("unsupported VGM command 0x52", exception.Message);
    }

    private static string WriteVgm(string fileName, int chipClockOffset, int chipClockHz, byte command, params byte[][] commandPayloads)
    {
        var bytes = BuildVgm(chipClockOffset, chipClockHz, command, commandPayloads);
        var path = Path.Combine(CreateTempDirectory(), fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string WriteGzip(string fileName, byte[] bytes)
    {
        var path = Path.Combine(CreateTempDirectory(), fileName);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        gzip.Write(bytes);
        return path;
    }

    private static byte[] BuildVgm(int chipClockOffset, int chipClockHz, byte command, params byte[][] commandPayloads)
    {
        return BuildVgm(chipClockOffset, chipClockHz, command, commandPayloads[0], waitSamples: 0, commandPayloads.Skip(1).ToArray());
    }

    private static byte[] BuildVgm(
        int chipClockOffset,
        int chipClockHz,
        byte command,
        byte[] firstPayload,
        int waitSamples = 0,
        params byte[][] remainingPayloads)
    {
        return BuildVgm(chipClockOffset, chipClockHz, command, firstPayload, waitSamples, dataBlockType: null, dataBlock: null, remainingPayloads);
    }

    private static byte[] BuildVgm(
        int chipClockOffset,
        int chipClockHz,
        byte command,
        byte[] firstPayload,
        int waitSamples,
        byte? dataBlockType,
        byte[]? dataBlock,
        params byte[][] remainingPayloads)
    {
        var commands = new List<byte> { command };
        commands.AddRange(firstPayload);
        if (dataBlockType.HasValue)
        {
            commands.Add(0x67);
            commands.Add(0x66);
            commands.Add(dataBlockType.Value);
            var block = dataBlock ?? [];
            commands.AddRange(BitConverter.GetBytes(block.Length));
            commands.AddRange(block);
        }

        if (waitSamples > 0)
        {
            commands.Add(0x61);
            commands.Add((byte)(waitSamples & 0xFF));
            commands.Add((byte)(waitSamples >> 8));
        }

        foreach (var payload in remainingPayloads)
        {
            commands.Add(command);
            commands.AddRange(payload);
        }

        commands.Add(0x66);

        var bytes = new byte[0xC0 + commands.Count];
        bytes[0] = (byte)'V';
        bytes[1] = (byte)'g';
        bytes[2] = (byte)'m';
        bytes[3] = (byte)' ';
        WriteUInt32(bytes, 0x08, 0x00000161);
        WriteUInt32(bytes, 0x18, (uint)Math.Max(waitSamples, 735));
        WriteUInt32(bytes, 0x34, 0x8C);
        WriteUInt32(bytes, chipClockOffset, (uint)chipClockHz);
        commands.CopyTo(bytes, 0xC0);
        WriteUInt32(bytes, 0x04, (uint)(bytes.Length - 4));
        return bytes;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-vgm-importer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
