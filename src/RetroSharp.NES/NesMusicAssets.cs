namespace RetroSharp.NES;

using System.Text.Json;
using RetroSharp.Core.Sdk;

internal sealed record NesCompiledMusicAsset(
    string Name,
    byte[] Data,
    int OrderStartOffset,
    int LoopOrderOffset,
    IReadOnlyList<NesApuTraceOrderEntry> OrderEntries,
    IReadOnlyList<NesDpcmSampleBlock> DpcmBlocks);

internal sealed record NesCompiledSoundEffectAsset(
    string Name,
    byte[] Data,
    int OrderStartOffset,
    IReadOnlyList<NesApuTraceOrderEntry> OrderEntries,
    IReadOnlyList<NesDpcmSampleBlock> DpcmBlocks);

internal readonly record struct NesApuTraceOrderEntry(int BodyOffset, byte Wait);

internal readonly record struct NesDpcmSampleBlock(ushort SourceAddress, byte[] Data);

internal static class NesMusicAssetCompiler
{
    private const byte ApuTraceMarker = 0x03;
    private const int ApuTraceHeaderLength = 5;
    private const ushort DpcmSampleStartAddress = 0xC000;
    private const ushort DpcmVectorStartAddress = 0xFFFA;

    public static NesCompiledMusicAsset CompileFromFile(string name, string path)
    {
        var asset = ResolveMusicAsset(path);
        return asset.Format switch
        {
            "vgm" => CompileVgmTrace(name, VgmImporter.Import(asset.Path, VgmChip.Nes2A03)),
            _ => throw new InvalidOperationException($"NES music asset '{Path.GetFileName(path)}' has unsupported format '{asset.Format}'."),
        };
    }

    private static NesCompiledMusicAsset CompileVgmTrace(string name, VgmFrameStream stream)
    {
        var frames = OptimizeFrameWrites(stream.Frames, stream.LoopFrame);
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("NES VGM trace must contain at least one supported 2A03 register write.");
        }

        var loopGroupIndex = stream.LoopFrame <= 0
            ? 0
            : frames.FindIndex(group => group.Index >= stream.LoopFrame);
        if (loopGroupIndex < 0)
        {
            loopGroupIndex = 0;
        }

        var pool = new List<byte>();
        var bodyOffsets = new Dictionary<string, int>();
        var orderEntries = new List<NesApuTraceOrderEntry>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var body = BuildApuTraceGroupBody(frames[i]);
            var key = Convert.ToHexString(body);
            if (!bodyOffsets.TryGetValue(key, out var bodyOffset))
            {
                bodyOffset = ApuTraceHeaderLength + pool.Count;
                if (bodyOffset > ushort.MaxValue)
                {
                    throw new InvalidOperationException("NES VGM trace pool exceeds the 64 KiB pointer range.");
                }

                bodyOffsets[key] = bodyOffset;
                pool.AddRange(body);
            }

            var nextFrame = i + 1 < frames.Count ? frames[i + 1].Index : stream.DurationFrames;
            orderEntries.Add(new NesApuTraceOrderEntry(bodyOffset, CheckedFrameDelta(frames[i].Index, nextFrame)));
        }

        var orderStartOffset = ApuTraceHeaderLength + pool.Count;
        var loopOrderOffset = orderStartOffset + loopGroupIndex * 3;
        if (orderStartOffset + orderEntries.Count * 3 + 3 > ushort.MaxValue)
        {
            throw new InvalidOperationException("NES VGM trace order stream exceeds the 64 KiB pointer range.");
        }

        var data = new List<byte>(ApuTraceHeaderLength + pool.Count + orderEntries.Count * 3 + 3)
        {
            ApuTraceMarker,
            (byte)(orderStartOffset & 0xFF),
            (byte)(orderStartOffset >> 8),
            (byte)(loopOrderOffset & 0xFF),
            (byte)(loopOrderOffset >> 8),
        };
        data.AddRange(pool);
        foreach (var entry in orderEntries)
        {
            data.Add((byte)(entry.BodyOffset & 0xFF));
            data.Add((byte)(entry.BodyOffset >> 8));
            data.Add(entry.Wait);
        }

        data.Add(0);
        data.Add(0);
        data.Add(0);
        return new NesCompiledMusicAsset(
            name,
            data.ToArray(),
            orderStartOffset,
            loopOrderOffset,
            orderEntries,
            CompileDpcmBlocks(stream.DpcmBlocks, frames));
    }

    private static List<VgmFrame> OptimizeFrameWrites(IReadOnlyList<VgmFrame> frames, int loopFrame)
    {
        var result = new List<VgmFrame>(frames.Count);
        var lastValues = new Dictionary<ushort, byte>();
        var loopCacheReset = loopFrame <= 0;
        foreach (var frame in frames)
        {
            if (!loopCacheReset && frame.Index >= loopFrame)
            {
                lastValues.Clear();
                loopCacheReset = true;
            }

            var writes = new List<VgmRegisterWrite>(frame.Writes.Count);
            for (var i = 0; i < frame.Writes.Count; i++)
            {
                var write = frame.Writes[i];
                if (IsNesApuTriggerRegister(write.Address) || IsLastWriteToRegisterInFrame(frame.Writes, i))
                {
                    writes.Add(write);
                }
            }

            var optimizedWrites = new List<VgmRegisterWrite>(writes.Count);
            foreach (var write in writes)
            {
                if (!IsNesApuTriggerRegister(write.Address) &&
                    lastValues.TryGetValue(write.Address, out var previousValue) &&
                    previousValue == write.Value)
                {
                    continue;
                }

                lastValues[write.Address] = write.Value;
                optimizedWrites.Add(write);
            }

            if (optimizedWrites.Count > 0)
            {
                result.Add(new VgmFrame(frame.Index, optimizedWrites));
            }
        }

        return result;
    }

    private static bool IsLastWriteToRegisterInFrame(IReadOnlyList<VgmRegisterWrite> writes, int index)
    {
        var address = writes[index].Address;
        for (var i = index + 1; i < writes.Count; i++)
        {
            if (writes[i].Address == address)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNesApuTriggerRegister(ushort address)
    {
        return address is 0x4003 or 0x4007 or 0x400B or 0x400F or 0x4015;
    }

    private static IReadOnlyList<NesDpcmSampleBlock> CompileDpcmBlocks(IReadOnlyList<VgmDpcmMemoryBlock> blocks, IReadOnlyList<VgmFrame> frames)
    {
        var ranges = DpcmSampleRanges(frames);
        if (ranges.Count == 0)
        {
            return [];
        }

        var dpcmBlocks = new List<NesDpcmSampleBlock>(ranges.Count);
        foreach (var (startAddress, endAddress) in ranges)
        {
            if (startAddress < DpcmSampleStartAddress || endAddress > DpcmVectorStartAddress)
            {
                throw new InvalidOperationException(
                    $"NES DPCM sample range ${startAddress:X4}-${endAddress - 1:X4} must fit in PRG ROM between $C000 and $FFF9.");
            }

            var data = ReadDpcmRange(blocks, startAddress, endAddress);
            dpcmBlocks.Add(new NesDpcmSampleBlock((ushort)startAddress, data));
        }

        return dpcmBlocks;
    }

    private static List<(int StartAddress, int EndAddress)> DpcmSampleRanges(IReadOnlyList<VgmFrame> frames)
    {
        var ranges = new List<(int StartAddress, int EndAddress)>();
        var addressRegister = 0;
        var lengthRegister = 0;
        foreach (var frame in frames)
        {
            foreach (var write in frame.Writes)
            {
                switch (write.Address)
                {
                    case 0x4012:
                        addressRegister = write.Value;
                        break;
                    case 0x4013:
                        lengthRegister = write.Value;
                        break;
                    case 0x4015 when (write.Value & 0x10) != 0:
                    {
                        var startAddress = DpcmSampleStartAddress + addressRegister * 64;
                        var byteCount = lengthRegister * 16 + 1;
                        ranges.Add((startAddress, startAddress + byteCount));
                        break;
                    }
                }
            }
        }

        if (ranges.Count == 0)
        {
            return ranges;
        }

        ranges.Sort((left, right) => left.StartAddress.CompareTo(right.StartAddress));
        var merged = new List<(int StartAddress, int EndAddress)>();
        foreach (var range in ranges)
        {
            if (merged.Count == 0 || range.StartAddress > merged[^1].EndAddress)
            {
                merged.Add(range);
            }
            else
            {
                var previous = merged[^1];
                merged[^1] = (previous.StartAddress, Math.Max(previous.EndAddress, range.EndAddress));
            }
        }

        return merged;
    }

    private static byte[] ReadDpcmRange(IReadOnlyList<VgmDpcmMemoryBlock> blocks, int startAddress, int endAddress)
    {
        var data = new byte[endAddress - startAddress];
        var written = new bool[data.Length];
        foreach (var block in blocks)
        {
            var blockStart = block.Address;
            var blockEnd = blockStart + block.Data.Length;
            var copyStart = Math.Max(startAddress, blockStart);
            var copyEnd = Math.Min(endAddress, blockEnd);
            if (copyStart >= copyEnd)
            {
                continue;
            }

            Array.Copy(block.Data, copyStart - blockStart, data, copyStart - startAddress, copyEnd - copyStart);
            Array.Fill(written, true, copyStart - startAddress, copyEnd - copyStart);
        }

        if (written.Any(isWritten => !isWritten))
        {
            throw new InvalidOperationException(
                $"NES DPCM sample range ${startAddress:X4}-${endAddress - 1:X4} is not covered by VGM DPCM data blocks.");
        }

        return data;
    }

    private static byte[] BuildApuTraceGroupBody(VgmFrame frame)
    {
        if (frame.Writes.Count > byte.MaxValue)
        {
            throw new InvalidOperationException("NES VGM trace contains more than 255 register writes in one frame.");
        }

        var body = new List<byte>(1 + frame.Writes.Count * 2)
        {
            (byte)frame.Writes.Count,
        };
        foreach (var write in frame.Writes)
        {
            if (write.Address is < 0x4000 or > 0x4017)
            {
                throw new InvalidOperationException($"NES VGM trace event address {write.Address:X4} is not supported.");
            }

            body.Add((byte)(write.Address - 0x4000));
            body.Add(write.Value);
        }

        return body.ToArray();
    }

    private static byte CheckedFrameDelta(int currentFrame, int nextFrame)
    {
        var frames = nextFrame - currentFrame;
        if (frames is < 0 or > byte.MaxValue)
        {
            throw new InvalidOperationException("NES VGM trace contains an inter-frame delay longer than 255 frames.");
        }

        return (byte)frames;
    }

    private static ResolvedMusicAsset ResolveMusicAsset(string path)
    {
        path = PlatformAssetPathResolver.ResolveVariant(path, "nes");
        if (Path.GetExtension(path).Equals(".vgm", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".vgz", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedMusicAsset("vgm", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var format = RequiredString(root, "format", Path.GetFileName(path));
        if (!string.Equals(format, "retrosharp.music.v1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Music asset '{Path.GetFileName(path)}' has unsupported format '{format}'.");
        }

        if (!root.TryGetProperty("platforms", out var platforms) ||
            !platforms.TryGetProperty("nes", out var nes))
        {
            throw new InvalidOperationException($"Music asset '{Path.GetFileName(path)}' does not declare a NES platform variant.");
        }

        var nesFormat = RequiredString(nes, "format", Path.GetFileName(path));
        if (!string.Equals(nesFormat, "vgm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"NES music asset '{Path.GetFileName(path)}' must use format 'vgm', got '{nesFormat}'.");
        }

        var relativePath = RequiredString(nes, "path", Path.GetFileName(path));
        var resolved = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(), relativePath);
        return new ResolvedMusicAsset("vgm", Path.GetFullPath(resolved));
    }

    private static string RequiredString(JsonElement element, string property, string assetName)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Music asset '{assetName}' must declare string property '{property}'.");
        }

        return value.GetString() ?? "";
    }

    private sealed record ResolvedMusicAsset(string Format, string Path);
}

internal static class NesSoundEffectAssetCompiler
{
    public static NesCompiledSoundEffectAsset CompileFromFile(string name, string path)
    {
        var resolvedPath = PlatformAssetPathResolver.ResolveVariant(path, "nes");
        if (!Path.GetExtension(resolvedPath).Equals(".vgm", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(resolvedPath).Equals(".vgz", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"NES SFX asset '{Path.GetFileName(path)}' must use VGM/VGZ input.");
        }

        var stream = VgmImporter.Import(resolvedPath, VgmChip.Nes2A03);
        var frame = FirstSfxFrame(stream.Frames);
        if (frame is null)
        {
            throw new InvalidOperationException("NES SFX VGM trace must contain at least one supported 2A03 channel register write.");
        }

        var body = BuildApuTraceGroupBody(frame);

        return new NesCompiledSoundEffectAsset(
            name,
            body,
            0,
            [new NesApuTraceOrderEntry(0, 0)],
            []);
    }

    private static VgmFrame? FirstSfxFrame(IReadOnlyList<VgmFrame> frames)
    {
        foreach (var frame in frames)
        {
            var writes = new List<VgmRegisterWrite>(frame.Writes.Count);
            for (var i = 0; i < frame.Writes.Count; i++)
            {
                var write = frame.Writes[i];
                if (!IsSfxChannelRegister(write.Address))
                {
                    continue;
                }

                if (IsNesApuChannelTriggerRegister(write.Address) || IsLastWriteToRegisterInFrame(frame.Writes, i))
                {
                    writes.Add(write);
                }
            }

            if (writes.Count > 0)
            {
                return new VgmFrame(frame.Index, writes);
            }
        }

        return null;
    }

    private static bool IsLastWriteToRegisterInFrame(IReadOnlyList<VgmRegisterWrite> writes, int index)
    {
        var address = writes[index].Address;
        for (var i = index + 1; i < writes.Count; i++)
        {
            if (writes[i].Address == address)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSfxChannelRegister(ushort address)
    {
        return address is >= 0x4000 and <= 0x400F;
    }

    private static bool IsNesApuChannelTriggerRegister(ushort address)
    {
        return address is 0x4003 or 0x4007 or 0x400B or 0x400F;
    }

    private static byte[] BuildApuTraceGroupBody(VgmFrame frame)
    {
        if (frame.Writes.Count > byte.MaxValue)
        {
            throw new InvalidOperationException("NES SFX VGM trace contains more than 255 register writes in one frame.");
        }

        var body = new List<byte>(1 + frame.Writes.Count * 2)
        {
            (byte)frame.Writes.Count,
        };
        foreach (var write in frame.Writes)
        {
            body.Add((byte)(write.Address - 0x4000));
            body.Add(write.Value);
        }

        return body.ToArray();
    }

}
