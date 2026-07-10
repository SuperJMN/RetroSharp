namespace RetroSharp.Core.Sdk;

using System.Collections.ObjectModel;

public enum WorldPackCodec : byte
{
    Raw = 0,
    ElementRle = 1,
}

public sealed class WorldPackValidationException(string message) : ArgumentException(message);

public sealed record WorldPackDescriptor
{
    public const string V1Magic = "RWPK";
    public const int V1HeaderBytes = 48;
    public const int V1ChunkWidth = 8;
    public const int V1ChunkHeight = 8;
    public const int V1DirectoryEntryBytes = 20;

    public string Magic { get; init; } = V1Magic;

    public int MajorVersion { get; init; } = 1;

    public int MinorVersion { get; init; }

    public int HeaderBytes { get; init; } = V1HeaderBytes;

    public int HardwareWidth { get; init; }

    public int HardwareHeight { get; init; }

    public int MetatileWidth { get; init; }

    public int MetatileHeight { get; init; }

    public int ChunkWidth { get; init; } = V1ChunkWidth;

    public int ChunkHeight { get; init; } = V1ChunkHeight;

    public int ChunkColumns { get; init; }

    public int ChunkRows { get; init; }

    public int VisualMetatileCount { get; init; }

    public int CollisionProfileCount { get; init; }

    public int VisualIdBytes { get; init; }

    public int CollisionIdBytes { get; init; }

    public int TargetCellStride { get; init; }

    public int Flags { get; init; }

    public uint CollisionProfilesOffset { get; init; }

    public uint TargetExpansionsOffset { get; init; }

    public uint DirectoryOffset { get; init; }

    public uint ChunkDataOffset { get; init; }

    public uint PackLength { get; init; }
}

public sealed record WorldPackChunkDirectoryEntry(
    uint VisualOffset,
    ushort VisualStoredBytes,
    ushort VisualDecodedBytes,
    uint CollisionOffset,
    ushort CollisionStoredBytes,
    ushort CollisionDecodedBytes,
    byte ValidWidth,
    byte ValidHeight,
    WorldPackCodec VisualCodec,
    WorldPackCodec CollisionCodec);

public sealed class WorldPackCollisionProfile
{
    private readonly WorldTileFlags[] flags;
    private readonly ReadOnlyCollection<WorldTileFlags> readOnlyFlags;

    public WorldPackCollisionProfile(IEnumerable<WorldTileFlags> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        this.flags = flags.ToArray();
        readOnlyFlags = Array.AsReadOnly(this.flags);
    }

    public IReadOnlyList<WorldTileFlags> Flags => readOnlyFlags;

    internal WorldTileFlags FlagAt(int index) => flags[index];
}

public sealed class WorldPackChunk
{
    private readonly ushort[] visualIds;
    private readonly ushort[] collisionProfileIds;
    private readonly ReadOnlyCollection<ushort> readOnlyVisualIds;
    private readonly ReadOnlyCollection<ushort> readOnlyCollisionProfileIds;

    public WorldPackChunk(
        WorldPackChunkDirectoryEntry directory,
        IEnumerable<ushort> visualIds,
        IEnumerable<ushort> collisionProfileIds)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(visualIds);
        ArgumentNullException.ThrowIfNull(collisionProfileIds);

        Directory = directory;
        this.visualIds = visualIds.ToArray();
        this.collisionProfileIds = collisionProfileIds.ToArray();
        readOnlyVisualIds = Array.AsReadOnly(this.visualIds);
        readOnlyCollisionProfileIds = Array.AsReadOnly(this.collisionProfileIds);
    }

    public WorldPackChunkDirectoryEntry Directory { get; }

    public IReadOnlyList<ushort> VisualIds => readOnlyVisualIds;

    public IReadOnlyList<ushort> CollisionProfileIds => readOnlyCollisionProfileIds;

    internal ushort VisualIdAt(int index) => visualIds[index];

    internal ushort CollisionProfileIdAt(int index) => collisionProfileIds[index];
}

public readonly record struct WorldPackCellCoordinate(
    int HardwareX,
    int HardwareY,
    int MetatileX,
    int MetatileY,
    int SubcellX,
    int SubcellY,
    int ChunkX,
    int ChunkY,
    int LocalX,
    int LocalY,
    int ChunkIndex,
    int CellIndex,
    int SubcellIndex);

public sealed class WorldPack
{
    private const WorldTileFlags AllowedCollisionFlags =
        WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform;

    private readonly WorldPackCollisionProfile[] collisionProfiles;
    private readonly WorldPackChunk[] chunks;
    private readonly byte[] targetExpansions;
    private readonly ReadOnlyCollection<WorldPackCollisionProfile> readOnlyCollisionProfiles;
    private readonly ReadOnlyCollection<WorldPackChunk> readOnlyChunks;

    public WorldPack(
        WorldPackDescriptor descriptor,
        IEnumerable<WorldPackCollisionProfile> collisionProfiles,
        ReadOnlyMemory<byte> targetExpansions,
        IEnumerable<WorldPackChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(collisionProfiles);
        ArgumentNullException.ThrowIfNull(chunks);

        Descriptor = descriptor;
        this.collisionProfiles = collisionProfiles.ToArray();
        this.chunks = chunks.ToArray();
        this.targetExpansions = targetExpansions.ToArray();
        readOnlyCollisionProfiles = Array.AsReadOnly(this.collisionProfiles);
        readOnlyChunks = Array.AsReadOnly(this.chunks);

        Validate();
        SourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
        SourceHeight = descriptor.HardwareHeight / descriptor.MetatileHeight;
    }

    public WorldPackDescriptor Descriptor { get; }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public IReadOnlyList<WorldPackCollisionProfile> CollisionProfiles => readOnlyCollisionProfiles;

    public ReadOnlyMemory<byte> TargetExpansions => targetExpansions;

    public IReadOnlyList<WorldPackChunk> Chunks => readOnlyChunks;

    public WorldPackChunk ChunkAt(int chunkX, int chunkY)
    {
        if (chunkX < 0 || chunkX >= Descriptor.ChunkColumns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkX),
                chunkX,
                $"WorldPack chunk x must be between 0 and {Descriptor.ChunkColumns - 1}.");
        }

        if (chunkY < 0 || chunkY >= Descriptor.ChunkRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkY),
                chunkY,
                $"WorldPack chunk y must be between 0 and {Descriptor.ChunkRows - 1}.");
        }

        return chunks[chunkY * Descriptor.ChunkColumns + chunkX];
    }

    public WorldPackCellCoordinate Locate(int hardwareX, int hardwareY)
    {
        if (hardwareX < 0 || hardwareX >= Descriptor.HardwareWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hardwareX),
                hardwareX,
                $"WorldPack hardware cell x must be between 0 and {Descriptor.HardwareWidth - 1}.");
        }

        if (hardwareY < 0 || hardwareY >= Descriptor.HardwareHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hardwareY),
                hardwareY,
                $"WorldPack hardware cell y must be between 0 and {Descriptor.HardwareHeight - 1}.");
        }

        var metatileX = hardwareX / Descriptor.MetatileWidth;
        var metatileY = hardwareY / Descriptor.MetatileHeight;
        var subcellX = hardwareX % Descriptor.MetatileWidth;
        var subcellY = hardwareY % Descriptor.MetatileHeight;
        var chunkX = metatileX / Descriptor.ChunkWidth;
        var chunkY = metatileY / Descriptor.ChunkHeight;
        var localX = metatileX % Descriptor.ChunkWidth;
        var localY = metatileY % Descriptor.ChunkHeight;
        var chunkIndex = chunkY * Descriptor.ChunkColumns + chunkX;
        var validWidth = chunks[chunkIndex].Directory.ValidWidth;
        var cellIndex = localY * validWidth + localX;
        var subcellIndex = subcellY * Descriptor.MetatileWidth + subcellX;

        return new WorldPackCellCoordinate(
            hardwareX,
            hardwareY,
            metatileX,
            metatileY,
            subcellX,
            subcellY,
            chunkX,
            chunkY,
            localX,
            localY,
            chunkIndex,
            cellIndex,
            subcellIndex);
    }

    public ushort VisualIdAt(int hardwareX, int hardwareY)
    {
        var coordinate = Locate(hardwareX, hardwareY);
        return chunks[coordinate.ChunkIndex].VisualIdAt(coordinate.CellIndex);
    }

    public ushort CollisionProfileIdAt(int hardwareX, int hardwareY)
    {
        var coordinate = Locate(hardwareX, hardwareY);
        return chunks[coordinate.ChunkIndex].CollisionProfileIdAt(coordinate.CellIndex);
    }

    public WorldTileFlags CollisionAt(int hardwareX, int hardwareY)
    {
        var coordinate = Locate(hardwareX, hardwareY);
        var profileId = chunks[coordinate.ChunkIndex].CollisionProfileIdAt(coordinate.CellIndex);
        return collisionProfiles[profileId].FlagAt(coordinate.SubcellIndex);
    }

    public WorldMap2D ToWorldMap2D()
    {
        var flags = new WorldTileFlags[checked(Descriptor.HardwareWidth * Descriptor.HardwareHeight)];
        for (var y = 0; y < Descriptor.HardwareHeight; y++)
        {
            for (var x = 0; x < Descriptor.HardwareWidth; x++)
            {
                flags[y * Descriptor.HardwareWidth + x] = CollisionAt(x, y);
            }
        }

        return new WorldMap2D(Descriptor.HardwareWidth, Descriptor.HardwareHeight, flags);
    }

    public WorldTileGrid ToWorldTileGrid(Func<ReadOnlyMemory<byte>, int> tileIdSelector)
    {
        ArgumentNullException.ThrowIfNull(tileIdSelector);

        var tileIds = new int[checked(Descriptor.HardwareWidth * Descriptor.HardwareHeight)];
        var metatileCells = checked(Descriptor.MetatileWidth * Descriptor.MetatileHeight);
        for (var y = 0; y < Descriptor.HardwareHeight; y++)
        {
            for (var x = 0; x < Descriptor.HardwareWidth; x++)
            {
                var coordinate = Locate(x, y);
                var visualId = chunks[coordinate.ChunkIndex].VisualIdAt(coordinate.CellIndex);
                var recordCell = checked(visualId * metatileCells + coordinate.SubcellIndex);
                var start = checked(recordCell * Descriptor.TargetCellStride);
                var cell = targetExpansions.AsMemory(start, Descriptor.TargetCellStride);
                tileIds[y * Descriptor.HardwareWidth + x] = tileIdSelector(cell);
            }
        }

        return new WorldTileGrid(Descriptor.HardwareWidth, Descriptor.HardwareHeight, tileIds);
    }

    private void Validate()
    {
        ValidateFixedHeader();
        ValidateDimensionsAndCounts();

        var metatileCells = CheckedProduct(
            "metatile cell count",
            Descriptor.MetatileWidth,
            Descriptor.MetatileHeight);
        var sourceWidth = Descriptor.HardwareWidth / Descriptor.MetatileWidth;
        var sourceHeight = Descriptor.HardwareHeight / Descriptor.MetatileHeight;
        var expectedChunkColumns = CeilingDivide(sourceWidth, Descriptor.ChunkWidth);
        var expectedChunkRows = CeilingDivide(sourceHeight, Descriptor.ChunkHeight);

        RequireEqual("chunkColumns", Descriptor.ChunkColumns, expectedChunkColumns, "derived source width");
        RequireEqual("chunkRows", Descriptor.ChunkRows, expectedChunkRows, "derived source height");

        var expectedVisualIdBytes = CanonicalIdBytes(Descriptor.VisualMetatileCount);
        var expectedCollisionIdBytes = CanonicalIdBytes(Descriptor.CollisionProfileCount);
        RequireEqual("visualIdBytes", Descriptor.VisualIdBytes, expectedVisualIdBytes, "canonical ID width");
        RequireEqual("collisionIdBytes", Descriptor.CollisionIdBytes, expectedCollisionIdBytes, "canonical ID width");

        ValidateProfiles(metatileCells);

        var collisionProfileBytes = CheckedProduct(
            "collision profile section length",
            Descriptor.CollisionProfileCount,
            metatileCells);
        var targetExpansionBytes = CheckedProduct(
            "target expansion section length",
            Descriptor.VisualMetatileCount,
            metatileCells,
            Descriptor.TargetCellStride);
        if (targetExpansionBytes != targetExpansions.LongLength)
        {
            throw Invalid(
                $"target expansion section expected {targetExpansionBytes} byte(s) from visualMetatileCount, metatile dimensions, and targetCellStride, got {targetExpansions.LongLength}.");
        }

        var expectedCollisionProfilesOffset = (long)WorldPackDescriptor.V1HeaderBytes;
        var expectedTargetExpansionsOffset = CheckedAdd(
            "targetExpansionsOffset",
            expectedCollisionProfilesOffset,
            collisionProfileBytes);
        var expectedDirectoryOffset = CheckedAdd(
            "directoryOffset",
            expectedTargetExpansionsOffset,
            targetExpansionBytes);
        var chunkCount = CheckedProduct("chunk directory entry count", expectedChunkColumns, expectedChunkRows);
        var directoryBytes = CheckedProduct(
            "chunk directory length",
            chunkCount,
            WorldPackDescriptor.V1DirectoryEntryBytes);
        var expectedChunkDataOffset = CheckedAdd("chunkDataOffset", expectedDirectoryOffset, directoryBytes);

        RequireOffset("collisionProfilesOffset", Descriptor.CollisionProfilesOffset, expectedCollisionProfilesOffset);
        RequireOffset("targetExpansionsOffset", Descriptor.TargetExpansionsOffset, expectedTargetExpansionsOffset);
        RequireOffset("directoryOffset", Descriptor.DirectoryOffset, expectedDirectoryOffset);
        RequireOffset("chunkDataOffset", Descriptor.ChunkDataOffset, expectedChunkDataOffset);

        if (chunks.LongLength != chunkCount)
        {
            throw Invalid(
                $"chunk directory expected {chunkCount} entry or entries in row-major order, got {chunks.LongLength}.");
        }

        var nextOffset = expectedChunkDataOffset;
        for (var index = 0; index < chunks.Length; index++)
        {
            var chunkX = index % expectedChunkColumns;
            var chunkY = index / expectedChunkColumns;
            var expectedWidth = Math.Min(Descriptor.ChunkWidth, sourceWidth - chunkX * Descriptor.ChunkWidth);
            var expectedHeight = Math.Min(Descriptor.ChunkHeight, sourceHeight - chunkY * Descriptor.ChunkHeight);
            nextOffset = ValidateChunk(index, chunks[index], expectedWidth, expectedHeight, nextOffset);
        }

        if (nextOffset != Descriptor.PackLength)
        {
            var detail = Descriptor.PackLength > nextOffset
                ? "trailing bytes are not canonical"
                : "packLength ends before the canonical chunk data";
            throw Invalid($"packLength is {Descriptor.PackLength}, expected {nextOffset}; {detail}.");
        }
    }

    private void ValidateFixedHeader()
    {
        if (!string.Equals(Descriptor.Magic, WorldPackDescriptor.V1Magic, StringComparison.Ordinal))
        {
            throw Invalid($"magic must be '{WorldPackDescriptor.V1Magic}', got '{Descriptor.Magic}'.");
        }

        RequireEqual("major", Descriptor.MajorVersion, 1, "v1 version");
        RequireEqual("minor", Descriptor.MinorVersion, 0, "v1 version");
        RequireEqual("headerBytes", Descriptor.HeaderBytes, WorldPackDescriptor.V1HeaderBytes, "v1 header size");
        RequireEqual("chunkWidth", Descriptor.ChunkWidth, WorldPackDescriptor.V1ChunkWidth, "v1 chunk width");
        RequireEqual("chunkHeight", Descriptor.ChunkHeight, WorldPackDescriptor.V1ChunkHeight, "v1 chunk height");
        RequireEqual("flags", Descriptor.Flags, 0, "v1 flags");
    }

    private void ValidateDimensionsAndCounts()
    {
        RequireFieldRange("hardwareWidth", Descriptor.HardwareWidth, 1, ushort.MaxValue);
        RequireFieldRange("hardwareHeight", Descriptor.HardwareHeight, 1, ushort.MaxValue);
        RequireFieldRange("metatileWidth", Descriptor.MetatileWidth, 1, byte.MaxValue);
        RequireFieldRange("metatileHeight", Descriptor.MetatileHeight, 1, byte.MaxValue);
        RequireFieldRange("chunkColumns", Descriptor.ChunkColumns, 1, ushort.MaxValue);
        RequireFieldRange("chunkRows", Descriptor.ChunkRows, 1, ushort.MaxValue);
        RequireFieldRange("visualMetatileCount", Descriptor.VisualMetatileCount, 1, ushort.MaxValue);
        RequireFieldRange("collisionProfileCount", Descriptor.CollisionProfileCount, 1, ushort.MaxValue);
        RequireFieldRange("visualIdBytes", Descriptor.VisualIdBytes, 1, 2);
        RequireFieldRange("collisionIdBytes", Descriptor.CollisionIdBytes, 1, 2);
        RequireFieldRange("targetCellStride", Descriptor.TargetCellStride, 1, byte.MaxValue);

        if (Descriptor.HardwareWidth % Descriptor.MetatileWidth != 0)
        {
            throw Invalid(
                $"hardwareWidth {Descriptor.HardwareWidth} must divide exactly by metatileWidth {Descriptor.MetatileWidth}.");
        }

        if (Descriptor.HardwareHeight % Descriptor.MetatileHeight != 0)
        {
            throw Invalid(
                $"hardwareHeight {Descriptor.HardwareHeight} must divide exactly by metatileHeight {Descriptor.MetatileHeight}.");
        }
    }

    private void ValidateProfiles(long metatileCells)
    {
        if (collisionProfiles.Length != Descriptor.CollisionProfileCount)
        {
            throw Invalid(
                $"collisionProfileCount declares {Descriptor.CollisionProfileCount} profile(s), got {collisionProfiles.Length}.");
        }

        for (var profileIndex = 0; profileIndex < collisionProfiles.Length; profileIndex++)
        {
            var profile = collisionProfiles[profileIndex];
            if (profile.Flags.Count != metatileCells)
            {
                throw Invalid(
                    $"collision profile {profileIndex} expected {metatileCells} subcell flag byte(s), got {profile.Flags.Count}.");
            }

            for (var subcell = 0; subcell < profile.Flags.Count; subcell++)
            {
                var flags = profile.Flags[subcell];
                if ((flags & ~AllowedCollisionFlags) != 0)
                {
                    throw Invalid(
                        $"collision profile {profileIndex} subcell {subcell} contains undefined WorldTileFlags bits 0x{(int)flags:X2}.");
                }

                if (profileIndex == 0 && flags != WorldTileFlags.Empty)
                {
                    throw Invalid("collision profile 0 must contain only WorldTileFlags.Empty values.");
                }
            }

            if (profileIndex > 0 && CompareProfiles(collisionProfiles[profileIndex - 1], profile) >= 0)
            {
                throw Invalid(
                    $"collision profile {profileIndex} must follow profile {profileIndex - 1} in strict lexicographic order without duplicates.");
            }
        }
    }

    private long ValidateChunk(
        int index,
        WorldPackChunk chunk,
        int expectedWidth,
        int expectedHeight,
        long nextOffset)
    {
        var directory = chunk.Directory;
        if (directory.ValidWidth != expectedWidth)
        {
            throw Invalid($"chunk {index} validWidth is {directory.ValidWidth}, expected exact clipped width {expectedWidth}.");
        }

        if (directory.ValidHeight != expectedHeight)
        {
            throw Invalid($"chunk {index} validHeight is {directory.ValidHeight}, expected exact clipped height {expectedHeight}.");
        }

        ValidateCodec(index, "visualCodec", directory.VisualCodec);
        ValidateCodec(index, "collisionCodec", directory.CollisionCodec);
        ValidateRangeFits32Bit(index, "visual range", directory.VisualOffset, directory.VisualStoredBytes);
        ValidateRangeFits32Bit(index, "collision range", directory.CollisionOffset, directory.CollisionStoredBytes);

        var cellCount = CheckedProduct($"chunk {index} cell count", expectedWidth, expectedHeight);
        var expectedVisualDecodedBytes = CheckedProduct(
            $"chunk {index} visual decoded length",
            cellCount,
            Descriptor.VisualIdBytes);
        var expectedCollisionDecodedBytes = CheckedProduct(
            $"chunk {index} collision decoded length",
            cellCount,
            Descriptor.CollisionIdBytes);
        RequireChunkLength(index, "visualDecodedBytes", directory.VisualDecodedBytes, expectedVisualDecodedBytes);
        RequireChunkLength(index, "collisionDecodedBytes", directory.CollisionDecodedBytes, expectedCollisionDecodedBytes);

        if (directory.VisualStoredBytes == 0)
        {
            throw Invalid($"chunk {index} visualStoredBytes must be positive.");
        }

        if (directory.CollisionStoredBytes == 0)
        {
            throw Invalid($"chunk {index} collisionStoredBytes must be positive.");
        }

        if (directory.VisualCodec == WorldPackCodec.Raw && directory.VisualStoredBytes != directory.VisualDecodedBytes)
        {
            throw Invalid($"chunk {index} raw visualStoredBytes must equal visualDecodedBytes.");
        }

        if (directory.CollisionCodec == WorldPackCodec.Raw && directory.CollisionStoredBytes != directory.CollisionDecodedBytes)
        {
            throw Invalid($"chunk {index} raw collisionStoredBytes must equal collisionDecodedBytes.");
        }

        if (directory.VisualCodec == WorldPackCodec.ElementRle && directory.VisualStoredBytes >= directory.VisualDecodedBytes)
        {
            throw Invalid($"chunk {index} element-RLE visualStoredBytes must be strictly shorter than visualDecodedBytes.");
        }

        if (directory.CollisionCodec == WorldPackCodec.ElementRle && directory.CollisionStoredBytes >= directory.CollisionDecodedBytes)
        {
            throw Invalid($"chunk {index} element-RLE collisionStoredBytes must be strictly shorter than collisionDecodedBytes.");
        }

        if (chunk.VisualIds.Count != cellCount)
        {
            throw Invalid($"chunk {index} expected {cellCount} decoded visual IDs, got {chunk.VisualIds.Count}.");
        }

        if (chunk.CollisionProfileIds.Count != cellCount)
        {
            throw Invalid(
                $"chunk {index} expected {cellCount} decoded collision profile IDs, got {chunk.CollisionProfileIds.Count}.");
        }

        for (var cell = 0; cell < chunk.VisualIds.Count; cell++)
        {
            var visualId = chunk.VisualIds[cell];
            if (visualId >= Descriptor.VisualMetatileCount)
            {
                throw Invalid(
                    $"chunk {index} cell {cell} has visual ID {visualId}, but visualMetatileCount is {Descriptor.VisualMetatileCount}.");
            }

            var collisionId = chunk.CollisionProfileIds[cell];
            if (collisionId >= Descriptor.CollisionProfileCount)
            {
                throw Invalid(
                    $"chunk {index} cell {cell} has collision profile ID {collisionId}, but collisionProfileCount is {Descriptor.CollisionProfileCount}.");
            }
        }

        if (directory.VisualOffset != nextOffset)
        {
            throw Invalid(
                $"chunk {index} visualOffset is {directory.VisualOffset}, expected canonical contiguous offset {nextOffset}.");
        }

        nextOffset = CheckedAdd($"chunk {index} visual range", nextOffset, directory.VisualStoredBytes);
        if (directory.CollisionOffset != nextOffset)
        {
            throw Invalid(
                $"chunk {index} collisionOffset is {directory.CollisionOffset}, expected canonical contiguous offset {nextOffset}.");
        }

        return CheckedAdd($"chunk {index} collision range", nextOffset, directory.CollisionStoredBytes);
    }

    private static int CompareProfiles(WorldPackCollisionProfile left, WorldPackCollisionProfile right)
    {
        for (var index = 0; index < left.Flags.Count; index++)
        {
            var comparison = ((byte)left.Flags[index]).CompareTo((byte)right.Flags[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int CeilingDivide(int value, int divisor)
    {
        var numerator = CheckedAdd("ceiling division", value, divisor - 1L);
        return CheckedInt("ceiling division result", numerator / divisor);
    }

    private static int CanonicalIdBytes(int count) => count <= 256 ? 1 : 2;

    private static void ValidateCodec(int chunkIndex, string field, WorldPackCodec codec)
    {
        if (codec is not WorldPackCodec.Raw and not WorldPackCodec.ElementRle)
        {
            throw Invalid($"chunk {chunkIndex} {field} has unsupported v1 value {(byte)codec}.");
        }
    }

    private static void ValidateRangeFits32Bit(int chunkIndex, string name, uint offset, ushort length)
    {
        var end = CheckedAdd($"chunk {chunkIndex} {name}", offset, length);
        if (end > uint.MaxValue)
        {
            throw Invalid(
                $"chunk {chunkIndex} {name} ending at {end} does not fit the 32-bit relative-offset field.");
        }
    }

    private static void RequireFieldRange(string field, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw Invalid($"{field} must be between {minimum} and {maximum}, got {value}.");
        }
    }

    private static void RequireEqual(string field, int actual, int expected, string rule)
    {
        if (actual != expected)
        {
            throw Invalid($"{field} is {actual}, expected {expected} from the {rule}.");
        }
    }

    private static void RequireOffset(string field, uint actual, long expected)
    {
        RequireUnsignedField(field, expected);
        if (actual != expected)
        {
            throw Invalid($"{field} is {actual}, expected canonical section offset {expected}.");
        }
    }

    private static void RequireChunkLength(int chunkIndex, string field, ushort actual, long expected)
    {
        if (expected > ushort.MaxValue)
        {
            throw Invalid($"chunk {chunkIndex} {field} value {expected} does not fit its 16-bit field.");
        }

        if (actual != expected)
        {
            throw Invalid($"chunk {chunkIndex} {field} is {actual}, expected {expected} from exact chunk coverage.");
        }
    }

    private static long CheckedProduct(string context, params long[] factors)
    {
        try
        {
            var result = 1L;
            foreach (var factor in factors)
            {
                result = checked(result * factor);
            }

            return result;
        }
        catch (OverflowException)
        {
            throw Invalid($"{context} overflows checked 64-bit arithmetic.");
        }
    }

    private static long CheckedAdd(string context, long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw Invalid($"{context} overflows checked 64-bit arithmetic.");
        }
    }

    private static int CheckedInt(string context, long value)
    {
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw Invalid($"{context} value {value} does not fit a 32-bit model integer.");
        }

        return (int)value;
    }

    private static void RequireUnsignedField(string field, long value)
    {
        if (value < 0 || value > uint.MaxValue)
        {
            throw Invalid($"{field} value {value} does not fit its 32-bit relative-offset field.");
        }
    }

    private static WorldPackValidationException Invalid(string message) =>
        new($"Invalid WorldPack descriptor: {message}");
}
