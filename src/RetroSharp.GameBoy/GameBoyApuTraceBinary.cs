namespace RetroSharp.GameBoy;

using System.Buffers.Binary;
using System.Text;

// Compact binary container for an APU register trace (`retrosharp.gbapu` v2).
//
// The JSON form is ~50x larger than this binary for the same capture: register data is
// machine-captured, not hand-authored, so the only human use is inspect/diff, which the
// `gbapu-dump` tool handles. Binary is the preferred source artifact; JSON survives as an
// optional debug view. Event deltas use unsigned LEB128 and addresses store only the low
// high-RAM byte (the high byte is always 0xFF), so a typical event costs 3-4 bytes.
public static class GameBoyApuTraceBinary
{
    private static readonly byte[] Magic = "GBAP"u8.ToArray();
    private const byte Version = 0x02;
    private const byte FlagHasMetadata = 0x01;

    public static bool LooksLikeBinary(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[4];
        return stream.Read(head) == 4 && head.SequenceEqual(Magic);
    }

    public static GameBoyApuTrace Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var assetName = Path.GetFileName(path);
        if (bytes.Length < 4 || !bytes.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' is not a binary gbapu file.");
        }

        var offset = 4;
        var version = ReadByte(bytes, ref offset, assetName);
        if (version != Version)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' has unsupported binary version {version}.");
        }

        var flags = ReadByte(bytes, ref offset, assetName);
        var clockHz = (int)ReadUInt32(bytes, ref offset, assetName);
        var replayMilliHz = ReadUInt32(bytes, ref offset, assetName);
        var durationCycles = ReadUInt32(bytes, ref offset, assetName);
        var loopCycle = ReadUInt32(bytes, ref offset, assetName);
        var eventCount = ReadUInt32(bytes, ref offset, assetName);

        var events = new List<GameBoyApuTraceEvent>((int)eventCount);
        for (var i = 0; i < eventCount; i++)
        {
            var delta = (long)ReadVarint(bytes, ref offset, assetName);
            var addressLow = ReadByte(bytes, ref offset, assetName);
            var value = ReadByte(bytes, ref offset, assetName);
            var address = (ushort)(0xFF00 | addressLow);
            if (!GameBoyApuTraceFile.IsSupportedApuAddress(address))
            {
                throw new InvalidOperationException(
                    $"Game Boy APU trace '{assetName}' contains unsupported APU address {address:X4}.");
            }

            events.Add(new GameBoyApuTraceEvent(delta, address, value));
        }

        if (events.Count == 0)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' must contain at least one event.");
        }

        var metadata = (flags & FlagHasMetadata) != 0
            ? ReadMetadata(bytes, ref offset, assetName, replayMilliHz)
            : new GameBoyApuTraceMetadata(ReplayHz: ReplayHzOrNull(replayMilliHz));

        return new GameBoyApuTrace(clockHz, DeriveFramesPerSecond(clockHz), durationCycles, loopCycle, metadata, events);
    }

    public static void Write(string path, GameBoyApuTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var buffer = new List<byte>(16 + trace.Events.Count * 4);
        buffer.AddRange(Magic);
        buffer.Add(Version);

        var hasMetadata = MetadataIsPresent(trace.Metadata);
        buffer.Add((byte)(hasMetadata ? FlagHasMetadata : 0));

        WriteUInt32(buffer, (uint)trace.ClockHz);
        WriteUInt32(buffer, (uint)Math.Clamp(Math.Round((trace.Metadata.ReplayHz ?? 0) * 1000.0), 0, uint.MaxValue));
        WriteUInt32(buffer, (uint)trace.DurationCycles);
        WriteUInt32(buffer, (uint)trace.LoopCycle);
        WriteUInt32(buffer, (uint)trace.Events.Count);

        foreach (var traceEvent in trace.Events)
        {
            WriteVarint(buffer, (ulong)traceEvent.DeltaCycles);
            buffer.Add((byte)(traceEvent.Address & 0xFF));
            buffer.Add(traceEvent.Value);
        }

        if (hasMetadata)
        {
            WriteString(buffer, trace.Metadata.Title);
            WriteString(buffer, trace.Metadata.Author);
            WriteString(buffer, trace.Metadata.Copyright);
            WriteString(buffer, trace.Metadata.Source);
            buffer.Add((byte)Math.Clamp(trace.Metadata.Subsong ?? 0, 0, 255));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, buffer.ToArray());
    }

    private static GameBoyApuTraceMetadata ReadMetadata(byte[] bytes, ref int offset, string assetName, uint replayMilliHz)
    {
        var title = ReadString(bytes, ref offset, assetName);
        var author = ReadString(bytes, ref offset, assetName);
        var copyright = ReadString(bytes, ref offset, assetName);
        var source = ReadString(bytes, ref offset, assetName);
        var subsong = ReadByte(bytes, ref offset, assetName);
        return new GameBoyApuTraceMetadata(
            string.IsNullOrEmpty(title) ? null : title,
            string.IsNullOrEmpty(author) ? null : author,
            string.IsNullOrEmpty(copyright) ? null : copyright,
            subsong == 0 ? null : subsong,
            string.IsNullOrEmpty(source) ? null : source,
            ReplayHzOrNull(replayMilliHz));
    }

    private static bool MetadataIsPresent(GameBoyApuTraceMetadata metadata)
    {
        return !string.IsNullOrEmpty(metadata.Title)
            || !string.IsNullOrEmpty(metadata.Author)
            || !string.IsNullOrEmpty(metadata.Copyright)
            || !string.IsNullOrEmpty(metadata.Source)
            || metadata.Subsong is > 0;
    }

    private static double? ReplayHzOrNull(uint replayMilliHz) => replayMilliHz == 0 ? null : replayMilliHz / 1000.0;

    private static int DeriveFramesPerSecond(int clockHz)
    {
        // Retained only for the legacy GameBoyApuTrace.FramesPerSecond field; the compiler maps
        // cycles to frames using the true DMG frame period, not this value.
        return clockHz <= 0 ? 60 : Math.Clamp((int)Math.Round(clockHz / 70_224.0), 1, 255);
    }

    private static void WriteVarint(List<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            buffer.Add((byte)(value | 0x80));
            value >>= 7;
        }

        buffer.Add((byte)value);
    }

    private static ulong ReadVarint(byte[] bytes, ref int offset, string assetName)
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = ReadByte(bytes, ref offset, assetName);
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift >= 64)
            {
                throw new InvalidOperationException($"Game Boy APU trace '{assetName}' has a malformed delta varint.");
            }
        }
    }

    private static void WriteString(List<byte> buffer, string? value)
    {
        var encoded = string.IsNullOrEmpty(value) ? [] : Encoding.UTF8.GetBytes(value);
        WriteVarint(buffer, (ulong)encoded.Length);
        buffer.AddRange(encoded);
    }

    private static string ReadString(byte[] bytes, ref int offset, string assetName)
    {
        var length = (int)ReadVarint(bytes, ref offset, assetName);
        if (offset + length > bytes.Length)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' is truncated.");
        }

        var value = Encoding.UTF8.GetString(bytes, offset, length);
        offset += length;
        return value;
    }

    private static void WriteUInt32(List<byte> buffer, uint value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        buffer.AddRange(span);
    }

    private static uint ReadUInt32(byte[] bytes, ref int offset, string assetName)
    {
        if (offset + 4 > bytes.Length)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' is truncated.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static byte ReadByte(byte[] bytes, ref int offset, string assetName)
    {
        if (offset >= bytes.Length)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' is truncated.");
        }

        return bytes[offset++];
    }
}
