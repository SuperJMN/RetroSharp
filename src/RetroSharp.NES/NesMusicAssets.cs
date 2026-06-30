namespace RetroSharp.NES;

using System.Text.Json;
using RetroSharp.Core.Sdk;

internal sealed record NesCompiledMusicAsset(
    string Name,
    byte[] Data,
    int OrderStartOffset,
    int LoopOrderOffset,
    IReadOnlyList<NesApuTraceOrderEntry> OrderEntries);

internal readonly record struct NesApuTraceOrderEntry(int BodyOffset, byte Wait);

internal static class NesMusicAssetCompiler
{
    private const byte ApuTraceMarker = 0x03;
    private const int ApuTraceHeaderLength = 5;

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
        if (stream.Frames.Count == 0)
        {
            throw new InvalidOperationException("NES VGM trace must contain at least one supported 2A03 register write.");
        }

        var loopGroupIndex = stream.LoopFrame <= 0
            ? 0
            : stream.Frames.ToList().FindIndex(group => group.Index >= stream.LoopFrame);
        if (loopGroupIndex < 0)
        {
            loopGroupIndex = 0;
        }

        var pool = new List<byte>();
        var bodyOffsets = new Dictionary<string, int>();
        var orderEntries = new List<NesApuTraceOrderEntry>(stream.Frames.Count);
        for (var i = 0; i < stream.Frames.Count; i++)
        {
            var body = BuildApuTraceGroupBody(stream.Frames[i]);
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

            var nextFrame = i + 1 < stream.Frames.Count ? stream.Frames[i + 1].Index : stream.DurationFrames;
            orderEntries.Add(new NesApuTraceOrderEntry(bodyOffset, CheckedFrameDelta(stream.Frames[i].Index, nextFrame)));
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
        return new NesCompiledMusicAsset(name, data.ToArray(), orderStartOffset, loopOrderOffset, orderEntries);
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
