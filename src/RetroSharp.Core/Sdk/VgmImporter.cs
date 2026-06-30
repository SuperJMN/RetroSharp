namespace RetroSharp.Core.Sdk;

using System.Globalization;
using System.IO.Compression;

public enum VgmChip
{
    GameBoyDmg,
    Nes2A03,
}

public sealed record VgmRegisterWrite(ushort Address, byte Value);

public sealed record VgmFrame(int Index, IReadOnlyList<VgmRegisterWrite> Writes);

public sealed record VgmFrameStream(
    VgmChip Chip,
    int ChipClockHz,
    int FrameRate,
    int DurationFrames,
    int LoopFrame,
    IReadOnlyList<VgmFrame> Frames);

public static class VgmImporter
{
    private const int SampleRate = 44_100;
    private const int FrameRate = 60;
    private const int HeaderSize = 0x40;

    public static VgmFrameStream Import(string path, VgmChip chip)
    {
        var bytes = ReadBytes(path);
        var assetName = Path.GetFileName(path);
        ValidateHeader(bytes, assetName);

        var commandOffset = CommandOffset(bytes, assetName);
        var chipClockHz = ReadChipClock(bytes, chip, assetName);
        var totalSamples = ReadUInt32(bytes, 0x18);
        var loopSamples = ReadUInt32(bytes, 0x20);
        var loopFrame = loopSamples == 0 || loopSamples > totalSamples
            ? 0
            : SampleToFrame(totalSamples - loopSamples);

        var frames = new SortedDictionary<int, List<VgmRegisterWrite>>();
        var position = commandOffset;
        var samples = 0L;
        while (position < bytes.Length)
        {
            var commandOffsetForError = position;
            var command = bytes[position++];
            switch (command)
            {
                case 0x66:
                    return BuildStream(chip, chipClockHz, totalSamples, loopFrame, frames);
                case 0x61:
                    samples = checked(samples + ReadUInt16(bytes, ref position, commandOffsetForError, assetName));
                    break;
                case 0x62:
                    samples += 735;
                    break;
                case 0x63:
                    samples += 882;
                    break;
                case >= 0x70 and <= 0x7F:
                    samples += (command & 0x0F) + 1;
                    break;
                case 0x67:
                    SkipDataBlock(bytes, ref position, commandOffsetForError, assetName);
                    break;
                case 0xB3:
                    ReadTargetWrite(bytes, ref position, commandOffsetForError, assetName, chip, VgmChip.GameBoyDmg, samples, frames);
                    break;
                case 0xB4:
                    ReadTargetWrite(bytes, ref position, commandOffsetForError, assetName, chip, VgmChip.Nes2A03, samples, frames);
                    break;
                default:
                    throw UnsupportedCommand(command, commandOffsetForError, assetName, chip);
            }
        }

        throw new InvalidOperationException($"VGM file '{assetName}' ended before the end-of-data command 0x66.");
    }

    private static byte[] ReadBytes(string path)
    {
        using var stream = File.OpenRead(path);
        if (Path.GetExtension(path).Equals(".vgz", StringComparison.OrdinalIgnoreCase) || LooksLikeGzip(stream))
        {
            stream.Position = 0;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        stream.Position = 0;
        using var plain = new MemoryStream();
        stream.CopyTo(plain);
        return plain.ToArray();
    }

    private static bool LooksLikeGzip(Stream stream)
    {
        if (stream.Length < 2)
        {
            return false;
        }

        var first = stream.ReadByte();
        var second = stream.ReadByte();
        return first == 0x1F && second == 0x8B;
    }

    private static void ValidateHeader(byte[] bytes, string assetName)
    {
        if (bytes.Length < HeaderSize ||
            bytes[0] != (byte)'V' ||
            bytes[1] != (byte)'g' ||
            bytes[2] != (byte)'m' ||
            bytes[3] != (byte)' ')
        {
            throw new InvalidOperationException($"VGM file '{assetName}' does not start with the 'Vgm ' signature.");
        }
    }

    private static int CommandOffset(byte[] bytes, string assetName)
    {
        var relative = ReadUInt32(bytes, 0x34);
        var offset = relative == 0 ? HeaderSize : checked(0x34 + (int)relative);
        if (offset < HeaderSize || offset >= bytes.Length)
        {
            throw new InvalidOperationException($"VGM file '{assetName}' has invalid command data offset 0x{offset:X}.");
        }

        return offset;
    }

    private static int ReadChipClock(byte[] bytes, VgmChip chip, string assetName)
    {
        var offset = chip switch
        {
            VgmChip.GameBoyDmg => 0x80,
            VgmChip.Nes2A03 => 0x84,
            _ => throw new ArgumentOutOfRangeException(nameof(chip), chip, null),
        };

        var clock = ReadUInt32(bytes, offset);
        if (clock == 0)
        {
            throw new InvalidOperationException($"VGM file '{assetName}' does not declare a {ChipName(chip)} clock.");
        }

        if (clock > int.MaxValue)
        {
            throw new InvalidOperationException($"VGM file '{assetName}' declares an unsupported {ChipName(chip)} clock value {clock.ToString(CultureInfo.InvariantCulture)}.");
        }

        return (int)clock;
    }

    private static void ReadTargetWrite(
        byte[] bytes,
        ref int position,
        int commandOffset,
        string assetName,
        VgmChip requestedChip,
        VgmChip commandChip,
        long samples,
        SortedDictionary<int, List<VgmRegisterWrite>> frames)
    {
        RequireBytes(bytes, position, 2, commandOffset, assetName);
        var register = bytes[position++];
        var value = bytes[position++];

        if (requestedChip != commandChip)
        {
            throw UnsupportedCommand((byte)(commandChip == VgmChip.GameBoyDmg ? 0xB3 : 0xB4), commandOffset, assetName, requestedChip);
        }

        var address = MapRegister(commandChip, register, assetName);
        if (address is null)
        {
            return;
        }

        var frame = SampleToFrame(samples);
        if (!frames.TryGetValue(frame, out var writes))
        {
            writes = [];
            frames.Add(frame, writes);
        }

        writes.Add(new VgmRegisterWrite(address.Value, value));
    }

    private static ushort? MapRegister(VgmChip chip, byte register, string assetName)
    {
        return chip switch
        {
            VgmChip.GameBoyDmg => register switch
            {
                <= 0x16 => (ushort)(0xFF10 + register),
                >= 0x20 and <= 0x2F => (ushort)(0xFF10 + register),
                _ => throw new InvalidOperationException($"VGM file '{assetName}' contains unsupported Game Boy DMG register 0x{register:X2}."),
            },
            VgmChip.Nes2A03 => register switch
            {
                <= 0x0F => (ushort)(0x4000 + register),
                >= 0x10 and <= 0x13 => null,
                0x15 or 0x17 => (ushort)(0x4000 + register),
                _ => throw new InvalidOperationException($"VGM file '{assetName}' contains unsupported NES 2A03 register 0x{register:X2}."),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(chip), chip, null),
        };
    }

    private static void SkipDataBlock(byte[] bytes, ref int position, int commandOffset, string assetName)
    {
        RequireBytes(bytes, position, 6, commandOffset, assetName);
        if (bytes[position++] != 0x66)
        {
            throw new InvalidOperationException($"VGM file '{assetName}' has malformed data block at 0x{commandOffset:X}: missing 0x66 marker.");
        }

        _ = bytes[position++];
        var size = ReadUInt32(bytes, position);
        position += 4;
        if (size > int.MaxValue || position + (int)size > bytes.Length)
        {
            throw new InvalidOperationException($"VGM file '{assetName}' has data block at 0x{commandOffset:X} that extends past the end of the file.");
        }

        position += (int)size;
    }

    private static VgmFrameStream BuildStream(
        VgmChip chip,
        int chipClockHz,
        uint totalSamples,
        int loopFrame,
        SortedDictionary<int, List<VgmRegisterWrite>> frames)
    {
        var frameList = frames
            .Select(pair => new VgmFrame(pair.Key, pair.Value))
            .ToArray();
        var durationFrames = Math.Max(SampleToFrame(totalSamples), frameList.Length == 0 ? 1 : frameList[^1].Index + 1);
        return new VgmFrameStream(chip, chipClockHz, FrameRate, durationFrames, loopFrame, frameList);
    }

    private static int SampleToFrame(long samples)
    {
        var frame = checked((samples * FrameRate + SampleRate / 2) / SampleRate);
        if (frame > int.MaxValue)
        {
            throw new InvalidOperationException("VGM stream duration exceeds the frame stream limit.");
        }

        return (int)frame;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            return 0;
        }

        return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
    }

    private static ushort ReadUInt16(byte[] bytes, ref int position, int commandOffset, string assetName)
    {
        RequireBytes(bytes, position, 2, commandOffset, assetName);
        var result = (ushort)(bytes[position] | (bytes[position + 1] << 8));
        position += 2;
        return result;
    }

    private static void RequireBytes(byte[] bytes, int position, int count, int commandOffset, string assetName)
    {
        if (position + count > bytes.Length)
        {
            throw new InvalidOperationException($"VGM file '{assetName}' command at 0x{commandOffset:X} extends past the end of the file.");
        }
    }

    private static InvalidOperationException UnsupportedCommand(byte command, int offset, string assetName, VgmChip chip)
    {
        return new InvalidOperationException($"VGM file '{assetName}' contains unsupported VGM command 0x{command:X2} at 0x{offset:X} for {ChipName(chip)} import.");
    }

    private static string ChipName(VgmChip chip)
    {
        return chip switch
        {
            VgmChip.GameBoyDmg => "Game Boy DMG",
            VgmChip.Nes2A03 => "NES 2A03",
            _ => chip.ToString(),
        };
    }
}
