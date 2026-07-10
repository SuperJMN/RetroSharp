namespace RetroSharp.Core.Sdk;

using System.Buffers.Binary;
using System.Text;
using RetroSharp.Core.Sdk.Tiled;

public sealed record SerializedWorldPack(WorldPack Pack, byte[] SerializedBytes);

public static class WorldPackSerializer
{
    public static SerializedWorldPack Build(
        TiledWorldPackPlan plan,
        ReadOnlyMemory<byte> targetExpansions,
        int targetCellStride)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (targetCellStride is < 1 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetCellStride),
                targetCellStride,
                $"WorldPack target cell stride must be between 1 and {byte.MaxValue}.");
        }

        var metatileCells = checked(plan.MetatileWidth * plan.MetatileHeight);
        var expectedExpansionBytes = checked(plan.VisualMetatiles.Count * metatileCells * targetCellStride);
        if (targetExpansions.Length != expectedExpansionBytes)
        {
            throw new ArgumentException(
                $"WorldPack expected {expectedExpansionBytes} target expansion byte(s), got {targetExpansions.Length}.",
                nameof(targetExpansions));
        }

        var visualIdBytes = CanonicalIdBytes(plan.VisualMetatiles.Count);
        var collisionIdBytes = CanonicalIdBytes(plan.CollisionProfiles.Count);
        var chunkColumns = CeilingDivide(plan.SourceWidth, WorldPackDescriptor.V1ChunkWidth);
        var chunkRows = CeilingDivide(plan.SourceHeight, WorldPackDescriptor.V1ChunkHeight);
        var chunkPayloads = CreateChunkPayloads(plan, visualIdBytes, collisionIdBytes, chunkColumns, chunkRows);

        var collisionProfilesOffset = (uint)WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = CheckedUInt32(
            "target expansions offset",
            (long)collisionProfilesOffset + checked(plan.CollisionProfiles.Count * metatileCells));
        var directoryOffset = CheckedUInt32(
            "directory offset",
            (long)targetExpansionsOffset + targetExpansions.Length);
        var chunkDataOffset = CheckedUInt32(
            "chunk data offset",
            (long)directoryOffset + checked(chunkPayloads.Count * WorldPackDescriptor.V1DirectoryEntryBytes));

        var chunks = new List<WorldPackChunk>(chunkPayloads.Count);
        var nextOffset = chunkDataOffset;
        foreach (var payload in chunkPayloads)
        {
            var visualStoredBytes = CheckedUInt16("visual stored length", payload.Visual.StoredBytes.Length);
            var collisionStoredBytes = CheckedUInt16("collision stored length", payload.Collision.StoredBytes.Length);
            var visualDecodedBytes = CheckedUInt16("visual decoded length", checked(payload.VisualIds.Length * visualIdBytes));
            var collisionDecodedBytes = CheckedUInt16("collision decoded length", checked(payload.CollisionIds.Length * collisionIdBytes));
            var collisionOffset = CheckedUInt32("collision offset", (long)nextOffset + visualStoredBytes);
            var directory = new WorldPackChunkDirectoryEntry(
                nextOffset,
                visualStoredBytes,
                visualDecodedBytes,
                collisionOffset,
                collisionStoredBytes,
                collisionDecodedBytes,
                checked((byte)payload.ValidWidth),
                checked((byte)payload.ValidHeight),
                payload.Visual.Codec,
                payload.Collision.Codec);
            chunks.Add(new WorldPackChunk(directory, payload.VisualIds, payload.CollisionIds));
            nextOffset = CheckedUInt32("pack length", (long)collisionOffset + collisionStoredBytes);
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = plan.HardwareWidth,
            HardwareHeight = plan.HardwareHeight,
            MetatileWidth = plan.MetatileWidth,
            MetatileHeight = plan.MetatileHeight,
            ChunkColumns = chunkColumns,
            ChunkRows = chunkRows,
            VisualMetatileCount = plan.VisualMetatiles.Count,
            CollisionProfileCount = plan.CollisionProfiles.Count,
            VisualIdBytes = visualIdBytes,
            CollisionIdBytes = collisionIdBytes,
            TargetCellStride = targetCellStride,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = nextOffset,
        };
        var pack = new WorldPack(descriptor, plan.CollisionProfiles, targetExpansions, chunks);
        return new SerializedWorldPack(pack, Serialize(pack));
    }

    public static byte[] Serialize(WorldPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var descriptor = pack.Descriptor;
        var result = new byte[CheckedInt("pack length", descriptor.PackLength)];
        var span = result.AsSpan();

        Encoding.ASCII.GetBytes(WorldPackDescriptor.V1Magic, span[..4]);
        span[4] = checked((byte)descriptor.MajorVersion);
        span[5] = checked((byte)descriptor.MinorVersion);
        WriteUInt16(span, 6, descriptor.HeaderBytes);
        WriteUInt16(span, 8, descriptor.HardwareWidth);
        WriteUInt16(span, 10, descriptor.HardwareHeight);
        span[12] = checked((byte)descriptor.MetatileWidth);
        span[13] = checked((byte)descriptor.MetatileHeight);
        span[14] = checked((byte)descriptor.ChunkWidth);
        span[15] = checked((byte)descriptor.ChunkHeight);
        WriteUInt16(span, 16, descriptor.ChunkColumns);
        WriteUInt16(span, 18, descriptor.ChunkRows);
        WriteUInt16(span, 20, descriptor.VisualMetatileCount);
        WriteUInt16(span, 22, descriptor.CollisionProfileCount);
        span[24] = checked((byte)descriptor.VisualIdBytes);
        span[25] = checked((byte)descriptor.CollisionIdBytes);
        span[26] = checked((byte)descriptor.TargetCellStride);
        span[27] = checked((byte)descriptor.Flags);
        WriteUInt32(span, 28, descriptor.CollisionProfilesOffset);
        WriteUInt32(span, 32, descriptor.TargetExpansionsOffset);
        WriteUInt32(span, 36, descriptor.DirectoryOffset);
        WriteUInt32(span, 40, descriptor.ChunkDataOffset);
        WriteUInt32(span, 44, descriptor.PackLength);

        var profileOffset = CheckedInt("collision profiles offset", descriptor.CollisionProfilesOffset);
        foreach (var profile in pack.CollisionProfiles)
        {
            foreach (var flags in profile.Flags)
            {
                span[profileOffset++] = (byte)flags;
            }
        }

        pack.TargetExpansions.Span.CopyTo(span[CheckedInt("target expansions offset", descriptor.TargetExpansionsOffset)..]);

        var directoryOffset = CheckedInt("directory offset", descriptor.DirectoryOffset);
        var encodedChunks = new List<(EncodedPlane Visual, EncodedPlane Collision)>(pack.Chunks.Count);
        for (var index = 0; index < pack.Chunks.Count; index++)
        {
            var chunk = pack.Chunks[index];
            var visual = EncodeCanonical(chunk.VisualIds, descriptor.VisualIdBytes);
            var collision = EncodeCanonical(chunk.CollisionProfileIds, descriptor.CollisionIdBytes);
            RequireCanonicalDirectory(index, "visual", chunk.Directory.VisualCodec, chunk.Directory.VisualStoredBytes, visual);
            RequireCanonicalDirectory(index, "collision", chunk.Directory.CollisionCodec, chunk.Directory.CollisionStoredBytes, collision);
            encodedChunks.Add((visual, collision));

            var entry = span.Slice(directoryOffset + index * WorldPackDescriptor.V1DirectoryEntryBytes, WorldPackDescriptor.V1DirectoryEntryBytes);
            WriteUInt32(entry, 0, chunk.Directory.VisualOffset);
            WriteUInt16(entry, 4, chunk.Directory.VisualStoredBytes);
            WriteUInt16(entry, 6, chunk.Directory.VisualDecodedBytes);
            WriteUInt32(entry, 8, chunk.Directory.CollisionOffset);
            WriteUInt16(entry, 12, chunk.Directory.CollisionStoredBytes);
            WriteUInt16(entry, 14, chunk.Directory.CollisionDecodedBytes);
            entry[16] = chunk.Directory.ValidWidth;
            entry[17] = chunk.Directory.ValidHeight;
            entry[18] = (byte)chunk.Directory.VisualCodec;
            entry[19] = (byte)chunk.Directory.CollisionCodec;
        }

        for (var index = 0; index < pack.Chunks.Count; index++)
        {
            var directory = pack.Chunks[index].Directory;
            encodedChunks[index].Visual.StoredBytes.CopyTo(span[CheckedInt("visual offset", directory.VisualOffset)..]);
            encodedChunks[index].Collision.StoredBytes.CopyTo(span[CheckedInt("collision offset", directory.CollisionOffset)..]);
        }

        return result;
    }

    public static WorldPack Deserialize(ReadOnlyMemory<byte> serialized)
    {
        var span = serialized.Span;
        if (span.Length < WorldPackDescriptor.V1HeaderBytes)
        {
            throw Invalid($"header requires {WorldPackDescriptor.V1HeaderBytes} bytes, got {span.Length}.");
        }

        var descriptor = new WorldPackDescriptor
        {
            Magic = Encoding.ASCII.GetString(span[..4]),
            MajorVersion = span[4],
            MinorVersion = span[5],
            HeaderBytes = ReadUInt16(span, 6),
            HardwareWidth = ReadUInt16(span, 8),
            HardwareHeight = ReadUInt16(span, 10),
            MetatileWidth = span[12],
            MetatileHeight = span[13],
            ChunkWidth = span[14],
            ChunkHeight = span[15],
            ChunkColumns = ReadUInt16(span, 16),
            ChunkRows = ReadUInt16(span, 18),
            VisualMetatileCount = ReadUInt16(span, 20),
            CollisionProfileCount = ReadUInt16(span, 22),
            VisualIdBytes = span[24],
            CollisionIdBytes = span[25],
            TargetCellStride = span[26],
            Flags = span[27],
            CollisionProfilesOffset = ReadUInt32(span, 28),
            TargetExpansionsOffset = ReadUInt32(span, 32),
            DirectoryOffset = ReadUInt32(span, 36),
            ChunkDataOffset = ReadUInt32(span, 40),
            PackLength = ReadUInt32(span, 44),
        };

        if (descriptor.PackLength != span.Length)
        {
            throw Invalid($"packLength is {descriptor.PackLength}, but the serialized payload has {span.Length} bytes.");
        }

        if (descriptor.VisualIdBytes is not 1 and not 2 || descriptor.CollisionIdBytes is not 1 and not 2)
        {
            throw Invalid("ID widths must be one or two before decoding chunk payloads.");
        }

        var metatileCells = CheckedInt(
            "metatile cell count",
            checked((long)descriptor.MetatileWidth * descriptor.MetatileHeight));
        var profileBytes = CheckedInt(
            "collision profile length",
            checked((long)descriptor.CollisionProfileCount * metatileCells));
        var expansionBytes = CheckedInt(
            "target expansion length",
            checked((long)descriptor.VisualMetatileCount * metatileCells * descriptor.TargetCellStride));
        var chunkCount = CheckedInt(
            "chunk count",
            checked((long)descriptor.ChunkColumns * descriptor.ChunkRows));
        RequireRange(span.Length, descriptor.CollisionProfilesOffset, profileBytes, "collision profiles");
        RequireRange(span.Length, descriptor.TargetExpansionsOffset, expansionBytes, "target expansions");
        RequireRange(
            span.Length,
            descriptor.DirectoryOffset,
            checked(chunkCount * WorldPackDescriptor.V1DirectoryEntryBytes),
            "directory");

        var collisionProfiles = new WorldPackCollisionProfile[descriptor.CollisionProfileCount];
        var profileOffset = CheckedInt("collision profiles offset", descriptor.CollisionProfilesOffset);
        for (var profileIndex = 0; profileIndex < collisionProfiles.Length; profileIndex++)
        {
            var flags = new WorldTileFlags[metatileCells];
            for (var subcell = 0; subcell < metatileCells; subcell++)
            {
                flags[subcell] = (WorldTileFlags)span[profileOffset++];
            }

            collisionProfiles[profileIndex] = new WorldPackCollisionProfile(flags);
        }

        var targetExpansions = span
            .Slice(CheckedInt("target expansions offset", descriptor.TargetExpansionsOffset), expansionBytes)
            .ToArray();
        var chunks = new WorldPackChunk[chunkCount];
        var directoryOffset = CheckedInt("directory offset", descriptor.DirectoryOffset);
        for (var index = 0; index < chunkCount; index++)
        {
            var entry = span.Slice(directoryOffset + index * WorldPackDescriptor.V1DirectoryEntryBytes, WorldPackDescriptor.V1DirectoryEntryBytes);
            var directory = new WorldPackChunkDirectoryEntry(
                ReadUInt32(entry, 0),
                ReadUInt16(entry, 4),
                ReadUInt16(entry, 6),
                ReadUInt32(entry, 8),
                ReadUInt16(entry, 12),
                ReadUInt16(entry, 14),
                entry[16],
                entry[17],
                (WorldPackCodec)entry[18],
                (WorldPackCodec)entry[19]);
            RequireRange(span.Length, directory.VisualOffset, directory.VisualStoredBytes, $"chunk {index} visual plane");
            RequireRange(span.Length, directory.CollisionOffset, directory.CollisionStoredBytes, $"chunk {index} collision plane");
            var visualIds = DecodePlane(
                span.Slice(CheckedInt("visual offset", directory.VisualOffset), directory.VisualStoredBytes),
                directory.VisualDecodedBytes,
                descriptor.VisualIdBytes,
                directory.VisualCodec,
                $"chunk {index} visual plane");
            var collisionIds = DecodePlane(
                span.Slice(CheckedInt("collision offset", directory.CollisionOffset), directory.CollisionStoredBytes),
                directory.CollisionDecodedBytes,
                descriptor.CollisionIdBytes,
                directory.CollisionCodec,
                $"chunk {index} collision plane");
            chunks[index] = new WorldPackChunk(directory, visualIds, collisionIds);
        }

        return new WorldPack(descriptor, collisionProfiles, targetExpansions, chunks);
    }

    private static IReadOnlyList<ChunkPayload> CreateChunkPayloads(
        TiledWorldPackPlan plan,
        int visualIdBytes,
        int collisionIdBytes,
        int chunkColumns,
        int chunkRows)
    {
        var result = new List<ChunkPayload>(checked(chunkColumns * chunkRows));
        for (var chunkY = 0; chunkY < chunkRows; chunkY++)
        {
            for (var chunkX = 0; chunkX < chunkColumns; chunkX++)
            {
                var validWidth = Math.Min(WorldPackDescriptor.V1ChunkWidth, plan.SourceWidth - chunkX * WorldPackDescriptor.V1ChunkWidth);
                var validHeight = Math.Min(WorldPackDescriptor.V1ChunkHeight, plan.SourceHeight - chunkY * WorldPackDescriptor.V1ChunkHeight);
                var cellCount = checked(validWidth * validHeight);
                var visualIds = new ushort[cellCount];
                var collisionIds = new ushort[cellCount];
                for (var localY = 0; localY < validHeight; localY++)
                {
                    for (var localX = 0; localX < validWidth; localX++)
                    {
                        var cellIndex = localY * validWidth + localX;
                        var sourceX = chunkX * WorldPackDescriptor.V1ChunkWidth + localX;
                        var sourceY = chunkY * WorldPackDescriptor.V1ChunkHeight + localY;
                        var sourceIndex = sourceY * plan.SourceWidth + sourceX;
                        visualIds[cellIndex] = plan.VisualIds[sourceIndex];
                        collisionIds[cellIndex] = plan.CollisionProfileIds[sourceIndex];
                    }
                }

                result.Add(new ChunkPayload(
                    validWidth,
                    validHeight,
                    visualIds,
                    collisionIds,
                    EncodeCanonical(visualIds, visualIdBytes),
                    EncodeCanonical(collisionIds, collisionIdBytes)));
            }
        }

        return result;
    }

    private static EncodedPlane EncodeCanonical(IReadOnlyList<ushort> ids, int idBytes)
    {
        var raw = new byte[checked(ids.Count * idBytes)];
        for (var index = 0; index < ids.Count; index++)
        {
            WriteId(raw, index * idBytes, ids[index], idBytes);
        }

        var rle = EncodeRle(ids, idBytes);
        return rle.Length < raw.Length
            ? new EncodedPlane(WorldPackCodec.ElementRle, rle)
            : new EncodedPlane(WorldPackCodec.Raw, raw);
    }

    private static byte[] EncodeRle(IReadOnlyList<ushort> ids, int idBytes)
    {
        var result = new List<byte>();
        var index = 0;
        while (index < ids.Count)
        {
            var runLength = CountRun(ids, index);
            if (runLength >= 3)
            {
                var packetLength = Math.Min(130, runLength);
                result.Add(checked((byte)(0x80 | (packetLength - 3))));
                WriteId(result, ids[index], idBytes);
                index += packetLength;
                continue;
            }

            var literalStart = index;
            var literalLength = 0;
            while (index < ids.Count && literalLength < 128 && CountRun(ids, index) < 3)
            {
                index++;
                literalLength++;
            }

            result.Add(checked((byte)(literalLength - 1)));
            for (var literal = 0; literal < literalLength; literal++)
            {
                WriteId(result, ids[literalStart + literal], idBytes);
            }
        }

        return result.ToArray();
    }

    private static ushort[] DecodePlane(
        ReadOnlySpan<byte> encoded,
        int decodedBytes,
        int idBytes,
        WorldPackCodec codec,
        string context)
    {
        if (decodedBytes % idBytes != 0)
        {
            throw Invalid($"{context} decoded byte length {decodedBytes} splits a {idBytes}-byte ID.");
        }

        var idCount = decodedBytes / idBytes;
        var result = new ushort[idCount];
        if (codec == WorldPackCodec.Raw)
        {
            if (encoded.Length != decodedBytes)
            {
                throw Invalid($"{context} raw stored length {encoded.Length} differs from decoded length {decodedBytes}.");
            }

            for (var index = 0; index < idCount; index++)
            {
                result[index] = ReadId(encoded, index * idBytes, idBytes);
            }

            return result;
        }

        if (codec != WorldPackCodec.ElementRle)
        {
            throw Invalid($"{context} uses unsupported codec {(byte)codec}.");
        }

        var input = 0;
        var output = 0;
        while (input < encoded.Length && output < result.Length)
        {
            var control = encoded[input++];
            if (control <= 127)
            {
                var literalCount = control + 1;
                RequireEncodedIds(encoded.Length, input, literalCount, idBytes, context);
                if (output + literalCount > result.Length)
                {
                    throw Invalid($"{context} RLE literal overruns the declared decoded length.");
                }

                for (var literal = 0; literal < literalCount; literal++)
                {
                    result[output++] = ReadId(encoded, input, idBytes);
                    input += idBytes;
                }
            }
            else
            {
                var runCount = (control & 0x7F) + 3;
                RequireEncodedIds(encoded.Length, input, 1, idBytes, context);
                if (output + runCount > result.Length)
                {
                    throw Invalid($"{context} RLE run overruns the declared decoded length.");
                }

                var id = ReadId(encoded, input, idBytes);
                input += idBytes;
                Array.Fill(result, id, output, runCount);
                output += runCount;
            }
        }

        if (output != result.Length)
        {
            throw Invalid($"{context} RLE ended after {output} ID(s), expected {result.Length}.");
        }

        if (input != encoded.Length)
        {
            throw Invalid($"{context} RLE contains {encoded.Length - input} trailing byte(s).");
        }

        return result;
    }

    private static int CountRun(IReadOnlyList<ushort> ids, int start)
    {
        var length = 1;
        while (start + length < ids.Count && ids[start + length] == ids[start])
        {
            length++;
        }

        return length;
    }

    private static void RequireCanonicalDirectory(
        int chunkIndex,
        string plane,
        WorldPackCodec codec,
        ushort storedBytes,
        EncodedPlane encoded)
    {
        if (codec != encoded.Codec || storedBytes != encoded.StoredBytes.Length)
        {
            throw Invalid(
                $"chunk {chunkIndex} {plane} directory is not the canonical {encoded.Codec} encoding of its decoded IDs.");
        }
    }

    private static void RequireEncodedIds(int encodedLength, int offset, int count, int idBytes, string context)
    {
        var required = checked((long)count * idBytes);
        if ((long)offset + required > encodedLength)
        {
            throw Invalid($"{context} RLE ends inside an ID packet.");
        }
    }

    private static void RequireRange(int length, uint offset, int count, string context)
    {
        if ((ulong)offset + (ulong)count > (ulong)length)
        {
            throw Invalid($"{context} range [{offset}, {(ulong)offset + (ulong)count}) exceeds {length} serialized bytes.");
        }
    }

    private static void WriteId(Span<byte> destination, int offset, ushort id, int idBytes)
    {
        if (idBytes == 1)
        {
            destination[offset] = checked((byte)id);
            return;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), id);
    }

    private static void WriteId(List<byte> destination, ushort id, int idBytes)
    {
        if (idBytes == 1)
        {
            destination.Add(checked((byte)id));
            return;
        }

        destination.Add((byte)(id & 0xFF));
        destination.Add((byte)(id >> 8));
    }

    private static ushort ReadId(ReadOnlySpan<byte> source, int offset, int idBytes) =>
        idBytes == 1 ? source[offset] : BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));

    private static int CeilingDivide(int value, int divisor) => checked((value + divisor - 1) / divisor);

    private static int CanonicalIdBytes(int count) => count <= 256 ? 1 : 2;

    private static ushort CheckedUInt16(string context, int value)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException($"WorldPack {context} value {value} does not fit 16 bits.");
        }

        return (ushort)value;
    }

    private static uint CheckedUInt32(string context, long value)
    {
        if (value is < 0 or > uint.MaxValue)
        {
            throw new InvalidOperationException($"WorldPack {context} value {value} does not fit 32 bits.");
        }

        return (uint)value;
    }

    private static int CheckedInt(string context, long value)
    {
        if (value is < 0 or > int.MaxValue)
        {
            throw Invalid($"{context} value {value} does not fit a model allocation.");
        }

        return (int)value;
    }

    private static void WriteUInt16(Span<byte> span, int offset, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), checked((ushort)value));

    private static void WriteUInt16(Span<byte> span, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), value);

    private static void WriteUInt32(Span<byte> span, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), value);

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> span, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));

    private static WorldPackValidationException Invalid(string message) =>
        new($"Invalid serialized WorldPack: {message}");

    private sealed record EncodedPlane(WorldPackCodec Codec, byte[] StoredBytes);

    private sealed record ChunkPayload(
        int ValidWidth,
        int ValidHeight,
        ushort[] VisualIds,
        ushort[] CollisionIds,
        EncodedPlane Visual,
        EncodedPlane Collision);
}
