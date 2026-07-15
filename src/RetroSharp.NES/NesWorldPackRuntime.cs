namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal sealed record NesWorldPackProbe(ushort HardwareX, ushort HardwareY);

internal enum NesWorldPackResult : byte
{
    Success = 0,
    Miss = 1,
    BoundsError = 2,
    Malformed = 3,
}

internal sealed record NesWorldPackPlaneRuntimeDescriptor(
    uint Offset,
    ushort StoredBytes,
    ushort DecodedBytes,
    byte IdBytes,
    ushort IdCount,
    WorldPackCodec Codec);

internal sealed record NesWorldPackAttributePlan(
    int Columns,
    int Rows,
    byte[] Bytes,
    int ColumnOffset,
    byte[] ColumnBytes)
{
    private readonly record struct PaletteCell(int Slot, bool IsWorldLayer);

    public static NesWorldPackAttributePlan Create(WorldPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        if (pack.Descriptor.TargetCellStride != 2)
        {
            throw new InvalidOperationException(
                $"NES WorldPack attributes require targetCellStride 2; received {pack.Descriptor.TargetCellStride}.");
        }

        var columns = (pack.Descriptor.HardwareWidth + 3) / 4;
        var rows = (pack.Descriptor.HardwareHeight + 3) / 4;
        var bytes = new byte[checked(columns * rows)];
        for (var blockY = 0; blockY < rows; blockY++)
        {
            for (var blockX = 0; blockX < columns; blockX++)
            {
                var value = 0;
                for (var quadrantY = 0; quadrantY < 2; quadrantY++)
                {
                    for (var quadrantX = 0; quadrantX < 2; quadrantX++)
                    {
                        var slot = MostCommonPaletteSlot(
                            pack,
                            blockX * 4 + quadrantX * 2,
                            blockY * 4 + quadrantY * 2);
                        value |= slot << ((quadrantY * 2 + quadrantX) * 2);
                    }
                }

                bytes[blockY * columns + blockX] = checked((byte)value);
            }
        }

        var completeNameTables = pack.Descriptor.HardwareHeight / 30;
        var remainingRows = pack.Descriptor.HardwareHeight % 30;
        var physicalRows = completeNameTables * 8 + (remainingRows + 3) / 4;
        var columnBytes = new byte[checked(columns * physicalRows)];
        for (var physicalRow = 0; physicalRow < physicalRows; physicalRow++)
        {
            var nameTableRow = physicalRow % 8;
            var sourceTop = physicalRow / 8 * 30 + nameTableRow * 4;
            for (var blockX = 0; blockX < columns; blockX++)
            {
                var value = 0;
                for (var quadrantY = 0; quadrantY < 2; quadrantY++)
                {
                    for (var quadrantX = 0; quadrantX < 2; quadrantX++)
                    {
                        // The eighth attribute row in a nametable has only two
                        // hardware tile rows. Its lower quadrants must not pull
                        // palette provenance from the next physical nametable.
                        var slot = nameTableRow == 7 && quadrantY == 1
                            ? 0
                            : MostCommonPaletteSlot(
                                pack,
                                blockX * 4 + quadrantX * 2,
                                sourceTop + quadrantY * 2);
                        value |= slot << ((quadrantY * 2 + quadrantX) * 2);
                    }
                }

                columnBytes[physicalRow * columns + blockX] = checked((byte)value);
            }
        }

        var columnOffset = (bytes.Length + byte.MaxValue) / 256 * 256;
        return new NesWorldPackAttributePlan(columns, rows, bytes, columnOffset, columnBytes);
    }

    private static int MostCommonPaletteSlot(WorldPack pack, int baseX, int baseY)
    {
        Span<int> counts = stackalloc int[4];
        Span<int> worldCounts = stackalloc int[4];
        Span<int> upperWorldCounts = stackalloc int[4];
        Span<int> lowerRowCounts = stackalloc int[4];
        for (var y = baseY; y < baseY + 2; y++)
        {
            for (var x = baseX; x < baseX + 2; x++)
            {
                var cell = PaletteCellAt(pack, x, y);
                counts[cell.Slot]++;
                if (cell.IsWorldLayer)
                {
                    worldCounts[cell.Slot]++;
                    if (y == baseY)
                    {
                        upperWorldCounts[cell.Slot]++;
                    }
                }

                if (y == baseY + 1)
                {
                    lowerRowCounts[cell.Slot]++;
                }
            }
        }

        var best = 0;
        for (var candidate = 1; candidate < 4; candidate++)
        {
            if (counts[candidate] > counts[best] ||
                counts[candidate] == counts[best] && worldCounts[candidate] > worldCounts[best] ||
                counts[candidate] == counts[best] && worldCounts[candidate] == worldCounts[best] &&
                upperWorldCounts[candidate] > upperWorldCounts[best] ||
                counts[candidate] == counts[best] && worldCounts[candidate] == worldCounts[best] &&
                upperWorldCounts[candidate] == upperWorldCounts[best] && lowerRowCounts[candidate] > lowerRowCounts[best])
            {
                best = candidate;
            }
        }

        return best;
    }

    private static PaletteCell PaletteCellAt(WorldPack pack, int x, int y)
    {
        if (x < 0 || x >= pack.Descriptor.HardwareWidth || y < 0 || y >= pack.Descriptor.HardwareHeight)
        {
            return new PaletteCell(0, false);
        }

        var coordinate = pack.Locate(x, y);
        var visualId = pack.VisualIdAt(x, y);
        var metatileCells = pack.Descriptor.MetatileWidth * pack.Descriptor.MetatileHeight;
        var offset = checked((visualId * metatileCells + coordinate.SubcellIndex) * 2 + 1);
        var metadata = pack.TargetExpansions.Span[offset];
        return new PaletteCell(metadata & 0x03, (metadata & 0x04) != 0);
    }
}

internal sealed record NesWorldPackRuntimePlan(
    WorldPack Pack,
    byte[] SerializedBytes,
    NesWorldPackRuntimeLayout Layout,
    byte[] HeaderBytes,
    byte[] CollisionProfileBytes,
    byte[] TargetExpansionBytes,
    NesWorldPackAttributePlan Attributes,
    byte[] DirectoryBytes,
    IReadOnlyList<NesWorldPackPlaneRuntimeDescriptor> Planes,
    bool UsesFastLookup)
{
    public static NesWorldPackRuntimePlan Create(byte[] serializedBytes)
    {
        ArgumentNullException.ThrowIfNull(serializedBytes);
        var pack = WorldPackSerializer.Deserialize(serializedBytes);
        var descriptor = pack.Descriptor;
        if (descriptor.TargetCellStride != 2)
        {
            throw new InvalidOperationException(
                $"NES WorldPack v1 requires targetCellStride 2; received {descriptor.TargetCellStride}.");
        }

        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        var collisionProfileLength = checked(descriptor.CollisionProfileCount * metatileCells);
        var targetExpansionLength = checked(descriptor.VisualMetatileCount * metatileCells * descriptor.TargetCellStride);
        var directoryLength = checked(pack.Chunks.Count * WorldPackDescriptor.V1DirectoryEntryBytes);
        var targetExpansionOffset = checked((int)descriptor.TargetExpansionsOffset);
        var targetExpansionBytes = serializedBytes
            .AsSpan(targetExpansionOffset, targetExpansionLength)
            .ToArray();
        for (var byteOffset = 1; byteOffset < targetExpansionBytes.Length; byteOffset += 2)
        {
            var reserved = targetExpansionBytes[byteOffset] & 0xF8;
            if (reserved != 0)
            {
                throw new InvalidOperationException(
                    $"NES WorldPack expansion metadata byte {byteOffset / 2} contains reserved bits ${reserved:X2}.");
            }
        }

        var planes = new List<NesWorldPackPlaneRuntimeDescriptor>(checked(pack.Chunks.Count * 2));
        foreach (var chunk in pack.Chunks)
        {
            planes.Add(CreatePlane(
                chunk.Directory.VisualOffset,
                chunk.Directory.VisualStoredBytes,
                chunk.Directory.VisualDecodedBytes,
                descriptor.VisualIdBytes,
                descriptor.VisualMetatileCount,
                chunk.Directory.VisualCodec));
            planes.Add(CreatePlane(
                chunk.Directory.CollisionOffset,
                chunk.Directory.CollisionStoredBytes,
                chunk.Directory.CollisionDecodedBytes,
                descriptor.CollisionIdBytes,
                descriptor.CollisionProfileCount,
                chunk.Directory.CollisionCodec));
        }

        var usesFastLookup = SupportsFastLookup(descriptor, planes);
        var layout = NesWorldPackRuntimeLayout.Create(
            descriptor.VisualIdBytes,
            descriptor.CollisionIdBytes,
            visualSlotCount: usesFastLookup ? 6 : 2);

        return new NesWorldPackRuntimePlan(
            pack,
            serializedBytes.ToArray(),
            layout,
            serializedBytes[..WorldPackDescriptor.V1HeaderBytes],
            serializedBytes.AsSpan(checked((int)descriptor.CollisionProfilesOffset), collisionProfileLength).ToArray(),
            targetExpansionBytes,
            NesWorldPackAttributePlan.Create(pack),
            serializedBytes.AsSpan(checked((int)descriptor.DirectoryOffset), directoryLength).ToArray(),
            planes,
            usesFastLookup);
    }

    private static bool SupportsFastLookup(
        WorldPackDescriptor descriptor,
        IReadOnlyList<NesWorldPackPlaneRuntimeDescriptor> planes) =>
        SupportsFastCoordinateLayout(descriptor, planes.Count / 2)
        && planes.Where((_, index) => index % 2 == 1)
            .All(plane => plane.Codec == WorldPackCodec.ElementRle);

    internal static bool SupportsFastCoordinateLayout(WorldPackDescriptor descriptor, int chunkCount) =>
        descriptor.MetatileWidth == 2
        && descriptor.MetatileHeight == 2
        && (descriptor.ChunkColumns == 20 || IsPowerOfTwo(descriptor.ChunkColumns))
        && descriptor.HardwareWidth <= 320
        && descriptor.HardwareHeight <= byte.MaxValue
        && descriptor.VisualIdBytes == 1
        && descriptor.CollisionIdBytes == 1
        && descriptor.CollisionProfileCount * 4 <= 256
        && descriptor.HardwareWidth % descriptor.MetatileWidth == 0
        && chunkCount is > 0 and <= byte.MaxValue
        && IsPowerOfTwo(
            descriptor.HardwareWidth / descriptor.MetatileWidth
            - (descriptor.ChunkColumns - 1) * 8);

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static NesWorldPackPlaneRuntimeDescriptor CreatePlane(
        uint offset,
        ushort storedBytes,
        ushort decodedBytes,
        int idBytes,
        int idCount,
        WorldPackCodec codec)
    {
        if (storedBytes == 0 || decodedBytes == 0 || decodedBytes > 64 * idBytes)
        {
            throw new InvalidOperationException(
                $"NES WorldPack plane requires stored/decoded lengths within one 8x8 ID slot; stored={storedBytes}, decoded={decodedBytes}, idBytes={idBytes}.");
        }

        return new NesWorldPackPlaneRuntimeDescriptor(
            offset,
            storedBytes,
            decodedBytes,
            checked((byte)idBytes),
            checked((ushort)idCount),
            codec);
    }
}

internal sealed record NesWorldPackRuntimeLayout(
    IReadOnlyList<NesRamRange> VisualSlots,
    IReadOnlyList<NesRamRange> CollisionSlots,
    IReadOnlyList<NesRamRange> EdgeSlots,
    int TotalBytes)
{
    private const int ChunkCells = 64;
    private const int EdgeBytes = 32 + 9;
    public static NesWorldPackRuntimeLayout Create(
        int visualIdBytes,
        int collisionIdBytes,
        int visualSlotCount = 2)
    {
        ValidateIdBytes(visualIdBytes, nameof(visualIdBytes));
        ValidateIdBytes(collisionIdBytes, nameof(collisionIdBytes));
        if (visualSlotCount is < 2 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(visualSlotCount));
        }

        var cursor = (int)NesRuntimeMemoryLayout.WorldPack.StagingStart;
        var visualSlots = CreateSlots(visualIdBytes * ChunkCells, visualSlotCount, ref cursor);
        var collisionSlots = CreatePair(collisionIdBytes * ChunkCells, ref cursor);
        var edgeSlots = CreatePair(EdgeBytes, ref cursor);
        var totalBytes = cursor - NesRuntimeMemoryLayout.WorldPack.StagingStart;
        NesRuntimeMemoryLayout.ValidateWorldPackStagingBytes(totalBytes);

        return new NesWorldPackRuntimeLayout(
            visualSlots,
            collisionSlots,
            edgeSlots,
            totalBytes);
    }

    private static IReadOnlyList<NesRamRange> CreatePair(int length, ref int cursor)
        => CreateSlots(length, 2, ref cursor);

    private static IReadOnlyList<NesRamRange> CreateSlots(int length, int count, ref int cursor)
    {
        var slots = new NesRamRange[count];
        for (var index = 0; index < count; index++)
        {
            slots[index] = new NesRamRange(checked((ushort)cursor), length);
            cursor = checked(cursor + length);
        }

        return slots;
    }

    private static void ValidateIdBytes(int value, string parameterName)
    {
        if (value is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "WorldPack v1 ID width must be one or two bytes.");
        }
    }
}

internal static class NesWorldPackRuntimeEmitter
{
    private const string ReadAdvanceLabel = "worldpack_read_advance";
    private const string TargetExpansionsLabel = "worldpack_target_expansions";
    private const string VisualPlaneOffset0Label = "worldpack_visual_plane_offset_0";
    private const string VisualPlaneOffset1Label = "worldpack_visual_plane_offset_1";
    private const string VisualPlaneOffset2Label = "worldpack_visual_plane_offset_2";
    private const string VisualPlaneOffset3Label = "worldpack_visual_plane_offset_3";
    private const string VisualPlaneCodecLabel = "worldpack_visual_plane_codec";
    private const string CollisionPlaneOffset0Label = "worldpack_collision_plane_offset_0";
    private const string CollisionPlaneOffset1Label = "worldpack_collision_plane_offset_1";
    private const string CollisionPlaneOffset2Label = "worldpack_collision_plane_offset_2";
    private const string CollisionPlaneOffset3Label = "worldpack_collision_plane_offset_3";
    private const string CollisionPlaneCodecLabel = "worldpack_collision_plane_codec";
    private const string CollisionProfilesLabel = "worldpack_collision_profiles";
    private const string FastVisualPlaneDescriptorsLabel = "worldpack_fast_visual_plane_descriptors";
    private const string FastCollisionPlaneDescriptorsLabel = "worldpack_fast_collision_plane_descriptors";
    private const string FastLoadPlaneDescriptorLabel = "worldpack_fast_load_plane_descriptor";

    public static void Emit(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesWorldPackPlacement? placement,
        NesWorldPackProbe? probe,
        bool enableStagedCamera)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);

        EmitReadByte(builder, plan, placement);
        EmitValidation(builder, plan);
        EmitPlaneDecoders(builder, plan, placement);
        EmitLookups(builder, plan);
        EmitRuntimeInitialization(builder, plan);
        if (probe is not null)
        {
            EmitProbe(builder, probe);
        }
        if (enableStagedCamera)
        {
            NesPackedCameraRuntimeEmitter.Emit(builder, plan);
        }
        if (placement is null)
        {
            EmitPinnedLookupData(builder, plan, enableStagedCamera);
        }
    }

    internal static int DefinePinnedLookupLabels(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        int startAddress,
        bool includeColumnAttributes)
    {
        var address = startAddress;
        builder.DefineExternalLabel(TargetExpansionsLabel, checked((ushort)address));
        address += plan.TargetExpansionBytes.Length;
        if (UsesFastCollisionLookup(plan))
        {
            builder.DefineExternalLabel(FastVisualPlaneDescriptorsLabel, checked((ushort)address));
            address += checked(plan.Pack.Chunks.Count * 7);
            builder.DefineExternalLabel(FastCollisionPlaneDescriptorsLabel, checked((ushort)address));
            address += checked(plan.Pack.Chunks.Count * 7);
        }
        else
        {
            foreach (var label in VisualPlaneLookupLabels)
            {
                builder.DefineExternalLabel(label, checked((ushort)address));
                address += plan.Pack.Chunks.Count;
            }
        }
        if (HasRawCollisionPlanes(plan))
        {
            foreach (var label in CollisionPlaneOffsetLabels)
            {
                builder.DefineExternalLabel(label, checked((ushort)address));
                address += plan.Pack.Chunks.Count;
            }
        }
        if (HasMixedCollisionCodecs(plan))
        {
            builder.DefineExternalLabel(CollisionPlaneCodecLabel, checked((ushort)address));
            address += plan.Pack.Chunks.Count;
        }
        if (UsesFastCollisionLookup(plan))
        {
            builder.DefineExternalLabel(CollisionProfilesLabel, checked((ushort)address));
            address += plan.CollisionProfileBytes.Length;
        }
        builder.DefineExternalLabel(NesRomBuilder.WorldPackAttributesLabel, checked((ushort)address));
        address += AttributeDataLength(plan, includeColumnAttributes);

        return address;
    }

    private static readonly string[] VisualPlaneLookupLabels =
    [
        VisualPlaneOffset0Label,
        VisualPlaneOffset1Label,
        VisualPlaneOffset2Label,
        VisualPlaneOffset3Label,
        VisualPlaneCodecLabel,
    ];

    private static readonly string[] CollisionPlaneOffsetLabels =
    [
        CollisionPlaneOffset0Label,
        CollisionPlaneOffset1Label,
        CollisionPlaneOffset2Label,
        CollisionPlaneOffset3Label,
    ];

    private static bool HasRawCollisionPlanes(NesWorldPackRuntimePlan plan) =>
        plan.Planes.Where((_, index) => index % 2 == 1).Any(plane => plane.Codec == WorldPackCodec.Raw);

    private static bool HasMixedCollisionCodecs(NesWorldPackRuntimePlan plan)
    {
        var collisionPlanes = plan.Planes.Where((_, index) => index % 2 == 1).ToArray();
        return collisionPlanes.Any(plane => plane.Codec == WorldPackCodec.Raw)
               && collisionPlanes.Any(plane => plane.Codec == WorldPackCodec.ElementRle);
    }

    private static bool UsesFastCollisionLookup(NesWorldPackRuntimePlan plan) => plan.UsesFastLookup;

    private static void EmitRuntimeInitialization(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        builder.Label(NesRomBuilder.WorldPackInitializeLabel);
        builder.LoadAImmediate(0);
        EmitClearStaging(builder, plan.Layout);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0Valid);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1Valid);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0ChunkLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0ChunkHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1ChunkLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1ChunkHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualReplacementNext);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0Valid);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1Valid);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionReplacementNext);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellYTag);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
        if (!plan.UsesFastLookup)
        {
            builder.LoadAImmediate((byte)NesWorldPackResult.Success);
            builder.Return();
            return;
        }

        // The four-screen boot image already contains hardware columns 0..31.
        // Prewarm the next two 16-cell chunk columns used by right-edge streaming.
        var loop = builder.CreateLabel("worldpack_prewarm_visual_loop");
        var next = builder.CreateLabel("worldpack_prewarm_visual_next");
        var complete = builder.CreateLabel("worldpack_prewarm_visual_complete");
        builder.LoadAImmediate(32);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.Label(loop);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualLookupLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.BranchRelative(0xF0, next);
        builder.Return();
        builder.Label(next);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.ClearCarry();
        builder.AddImmediate(16);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.CompareImmediate(48);
        builder.BranchRelative(0xD0, loop);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.ClearCarry();
        builder.AddImmediate(16);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.CompareImmediate(64);
        builder.BranchRelative(0xD0, loop);
        builder.Label(complete);

        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
    }

    private static void EmitClearStaging(PrgBuilder builder, NesWorldPackRuntimeLayout layout)
    {
        var start = layout.VisualSlots[0].Start;
        var remaining = layout.TotalBytes;
        while (remaining >= 512)
        {
            var loop = builder.CreateLabel("worldpack_staging_clear_pair");
            builder.LoadXImmediate(0);
            builder.Label(loop);
            builder.StoreAAbsoluteX(start);
            builder.StoreAAbsoluteX(checked((ushort)(start + 256)));
            builder.IncrementX();
            builder.BranchRelative(0xD0, loop); // BNE loop
            start = checked((ushort)(start + 512));
            remaining -= 512;
        }

        while (remaining >= 256)
        {
            var loop = builder.CreateLabel("worldpack_staging_clear_page");
            builder.LoadXImmediate(0);
            builder.Label(loop);
            builder.StoreAAbsoluteX(start);
            builder.IncrementX();
            builder.BranchRelative(0xD0, loop); // BNE loop
            start = checked((ushort)(start + 256));
            remaining -= 256;
        }

        if (remaining == 0)
        {
            return;
        }

        var tailLoop = builder.CreateLabel("worldpack_staging_clear_tail");
        builder.LoadXImmediate(0);
        builder.Label(tailLoop);
        builder.StoreAAbsoluteX(start);
        builder.IncrementX();
        builder.CompareXImmediate(remaining);
        builder.BranchRelative(0xD0, tailLoop); // BNE tailLoop
    }

    internal static int PinnedLookupDataLength(NesWorldPackRuntimePlan plan, bool includeColumnAttributes)
    {
        if (UsesFastCollisionLookup(plan))
        {
            return checked(
                plan.TargetExpansionBytes.Length
                + plan.Pack.Chunks.Count * 14
                + plan.CollisionProfileBytes.Length
                + AttributeDataLength(plan, includeColumnAttributes));
        }

        var collisionTableCount = HasRawCollisionPlanes(plan) ? 4 : 0;
        if (HasMixedCollisionCodecs(plan))
        {
            collisionTableCount++;
        }

        return checked(
            plan.TargetExpansionBytes.Length
            + plan.Pack.Chunks.Count * (5 + collisionTableCount)
            + (UsesFastCollisionLookup(plan) ? plan.CollisionProfileBytes.Length : 0)
            + AttributeDataLength(plan, includeColumnAttributes));
    }

    internal static void EmitPinnedLookupData(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        bool includeColumnAttributes)
    {
        builder.Label(TargetExpansionsLabel);
        builder.Emit(plan.TargetExpansionBytes);
        var visualPlanes = plan.Planes.Where((_, index) => index % 2 == 0).ToArray();
        var collisionPlanes = plan.Planes.Where((_, index) => index % 2 == 1).ToArray();
        if (UsesFastCollisionLookup(plan))
        {
            builder.Label(FastVisualPlaneDescriptorsLabel);
            builder.Emit(FastPlaneDescriptorBytes(visualPlanes));
            builder.Label(FastCollisionPlaneDescriptorsLabel);
            builder.Emit(FastPlaneDescriptorBytes(collisionPlanes));
        }
        else
        {
            EmitPlaneByteTable(builder, VisualPlaneOffset0Label, visualPlanes, plane => (byte)plane.Offset);
            EmitPlaneByteTable(builder, VisualPlaneOffset1Label, visualPlanes, plane => (byte)(plane.Offset >> 8));
            EmitPlaneByteTable(builder, VisualPlaneOffset2Label, visualPlanes, plane => (byte)(plane.Offset >> 16));
            EmitPlaneByteTable(builder, VisualPlaneOffset3Label, visualPlanes, plane => (byte)(plane.Offset >> 24));
            EmitPlaneByteTable(builder, VisualPlaneCodecLabel, visualPlanes, plane => (byte)plane.Codec);
            if (HasRawCollisionPlanes(plan))
            {
                EmitPlaneByteTable(builder, CollisionPlaneOffset0Label, collisionPlanes, plane => (byte)plane.Offset);
                EmitPlaneByteTable(builder, CollisionPlaneOffset1Label, collisionPlanes, plane => (byte)(plane.Offset >> 8));
                EmitPlaneByteTable(builder, CollisionPlaneOffset2Label, collisionPlanes, plane => (byte)(plane.Offset >> 16));
                EmitPlaneByteTable(builder, CollisionPlaneOffset3Label, collisionPlanes, plane => (byte)(plane.Offset >> 24));
            }
            if (HasMixedCollisionCodecs(plan))
            {
                EmitPlaneByteTable(builder, CollisionPlaneCodecLabel, collisionPlanes, plane => (byte)plane.Codec);
            }
        }
        if (UsesFastCollisionLookup(plan))
        {
            builder.Label(CollisionProfilesLabel);
            builder.Emit(plan.CollisionProfileBytes);
        }
        builder.Label(NesRomBuilder.WorldPackAttributesLabel);
        builder.Emit(plan.Attributes.Bytes);
        if (includeColumnAttributes)
        {
            builder.Emit(new byte[plan.Attributes.ColumnOffset - plan.Attributes.Bytes.Length]);
            builder.Emit(plan.Attributes.ColumnBytes);
        }
    }

    private static int AttributeDataLength(NesWorldPackRuntimePlan plan, bool includeColumnAttributes)
    {
        return includeColumnAttributes
            ? plan.Attributes.ColumnOffset + plan.Attributes.ColumnBytes.Length
            : plan.Attributes.Bytes.Length;
    }

    private static byte[] FastPlaneDescriptorBytes(IReadOnlyList<NesWorldPackPlaneRuntimeDescriptor> planes)
    {
        var bytes = new byte[checked(planes.Count * 7)];
        for (var index = 0; index < planes.Count; index++)
        {
            var plane = planes[index];
            var offset = index * 7;
            bytes[offset] = (byte)plane.Offset;
            bytes[offset + 1] = (byte)(plane.Offset >> 8);
            bytes[offset + 2] = (byte)(plane.Offset >> 16);
            bytes[offset + 3] = (byte)(plane.Offset >> 24);
            bytes[offset + 4] = checked((byte)plane.StoredBytes);
            bytes[offset + 5] = checked((byte)plane.DecodedBytes);
            bytes[offset + 6] = (byte)plane.Codec;
        }

        return bytes;
    }

    private static void EmitProbe(PrgBuilder builder, NesWorldPackProbe probe)
    {
        builder.Label(NesRomBuilder.WorldPackProbeLabel);
        SetProbeCoordinates(builder, probe);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualLookupLabel);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ProbeVisualStatus);
        SetProbeCoordinates(builder, probe);
        builder.CallSubroutine(NesRomBuilder.WorldPackCollisionLookupLabel);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ProbeCollisionStatus);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ProbeCompleted);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ProbeCollisionStatus);
        builder.Return();
    }

    private static void SetProbeCoordinates(PrgBuilder builder, NesWorldPackProbe probe)
    {
        builder.LoadAImmediate(probe.HardwareX & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.LoadAImmediate(probe.HardwareX >> 8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.LoadAImmediate(probe.HardwareY & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.LoadAImmediate(probe.HardwareY >> 8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
    }

    private static void EmitValidation(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        var initialize = builder.CreateLabel("worldpack_validate_initialize");
        var loop = builder.CreateLabel("worldpack_validate_loop");
        var read = builder.CreateLabel("worldpack_validate_read");
        var bitLoop = builder.CreateLabel("worldpack_validate_crc_bit");
        var shiftOnly = builder.CreateLabel("worldpack_validate_crc_shift");
        var shifted = builder.CreateLabel("worldpack_validate_crc_shifted");
        var incremented = builder.CreateLabel("worldpack_validate_incremented");
        var complete = builder.CreateLabel("worldpack_validate_complete");
        var success = builder.CreateLabel("worldpack_validate_success");
        var malformed = builder.CreateLabel("worldpack_validate_malformed");
        var expected = ComputeCrc16(plan.SerializedBytes.AsSpan(0, checked((int)plan.Pack.Descriptor.ChunkDataOffset)));

        builder.Label(NesRomBuilder.WorldPackValidateLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationState);
        builder.CompareImmediate(1);
        var checkMalformed = builder.CreateLabel("worldpack_validate_check_malformed");
        builder.BranchRelative(0xD0, checkMalformed);
        builder.JumpAbsolute(success);
        builder.Label(checkMalformed);
        builder.CompareImmediate(2);
        builder.BranchRelative(0xD0, initialize);
        builder.JumpAbsolute(malformed);

        builder.Label(initialize);
        foreach (var address in new[]
                 {
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset0,
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset1,
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset2,
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset3,
                 })
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(address);
        }
        builder.LoadAImmediate(0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcHigh);

        builder.Label(loop);
        EmitOffsetEqualityCheck(builder, plan.Pack.Descriptor.ChunkDataOffset, complete, read);
        builder.Label(read);
        builder.CallSubroutine(NesRomBuilder.WorldPackReadByteLabel);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationByte);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.XorAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationByte);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.LoadXImmediate(8);

        builder.Label(bitLoop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.AndImmediate(1);
        builder.BranchRelative(0xF0, shiftOnly);
        builder.ShiftRightAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcHigh);
        builder.RotateRightAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.XorImmediate(0x01);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcHigh);
        builder.XorImmediate(0xA0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcHigh);
        builder.JumpAbsolute(shifted);
        builder.Label(shiftOnly);
        builder.ShiftRightAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcHigh);
        builder.RotateRightAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.Label(shifted);
        builder.DecrementX();
        builder.BranchRelative(0xD0, bitLoop);

        EmitIncrementSourceOffset(builder, incremented);
        builder.Label(incremented);
        builder.JumpAbsolute(loop);

        builder.Label(complete);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcLow);
        builder.CompareImmediate((byte)expected);
        builder.JumpIf(0xD0, malformed);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationCrcHigh);
        builder.CompareImmediate((byte)(expected >> 8));
        builder.JumpIf(0xD0, malformed);

        builder.Label(success);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationState);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(malformed);
        builder.LoadAImmediate(2);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationState);
        builder.LoadAImmediate((byte)NesWorldPackResult.Malformed);
        builder.Return();
    }

    private static void EmitPlaneDecoders(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesWorldPackPlacement? placement)
    {
        var common = builder.CreateLabel("worldpack_decode_common");
        var visualSlotLabels = Enumerable.Range(1, plan.Layout.VisualSlots.Count - 1)
            .Select(index => builder.CreateLabel($"worldpack_visual_slot_{index}"))
            .ToArray();
        var visualReady = builder.CreateLabel("worldpack_visual_slot_ready");
        var collisionSlot1 = builder.CreateLabel("worldpack_collision_slot_1");
        var collisionReady = builder.CreateLabel("worldpack_collision_slot_ready");
        var bounds = builder.CreateLabel("worldpack_decode_bounds");
        var miss = builder.CreateLabel("worldpack_decode_miss");
        var malformed = builder.CreateLabel("worldpack_decode_malformed");
        var success = builder.CreateLabel("worldpack_decode_success");
        var directoryLoop = builder.CreateLabel("worldpack_directory_loop");
        var directoryReady = builder.CreateLabel("worldpack_directory_ready");
        var decrementLow = builder.CreateLabel("worldpack_directory_decrement_low");
        var descriptorVisual = builder.CreateLabel("worldpack_descriptor_visual");
        var descriptorFields = builder.CreateLabel("worldpack_descriptor_fields");
        var codecVisual = builder.CreateLabel("worldpack_codec_visual");
        var codecReady = builder.CreateLabel("worldpack_codec_ready");
        var raw = builder.CreateLabel("worldpack_decode_raw");
        var rawLoop = builder.CreateLabel("worldpack_decode_raw_loop");
        var rawComplete = builder.CreateLabel("worldpack_decode_raw_complete");
        var rle = builder.CreateLabel("worldpack_decode_rle");
        var rlePacket = builder.CreateLabel("worldpack_decode_rle_packet");
        var literal = builder.CreateLabel("worldpack_decode_rle_literal");
        var literalLoop = builder.CreateLabel("worldpack_decode_rle_literal_loop");
        var literalSecondDone = builder.CreateLabel("worldpack_decode_rle_literal_second_done");
        var run = builder.CreateLabel("worldpack_decode_rle_run");
        var runValueReady = builder.CreateLabel("worldpack_decode_rle_run_value_ready");
        var runLoop = builder.CreateLabel("worldpack_decode_rle_run_loop");
        var runSecondDone = builder.CreateLabel("worldpack_decode_rle_run_second_done");
        var decodeComplete = builder.CreateLabel("worldpack_decode_complete");
        const string readAdvance = ReadAdvanceLabel;
        var readStore = builder.CreateLabel("worldpack_read_store");
        var readStoreError = builder.CreateLabel("worldpack_read_store_error");
        var storeA = builder.CreateLabel("worldpack_store_a");

        EmitDecodeEntry(
            builder,
            NesRomBuilder.WorldPackVisualDecodeLabel,
            planeKind: 0,
            plan.Layout.VisualSlots,
            plan.Pack.Descriptor.VisualIdBytes,
            [
                NesRuntimeMemoryLayout.WorldPack.VisualSlot0State,
                NesRuntimeMemoryLayout.WorldPack.VisualSlot1State,
                .. Enumerable.Repeat(NesRuntimeMemoryLayout.WorldPack.VisualSlot0State, plan.Layout.VisualSlots.Count - 2),
            ],
            visualSlotLabels,
            visualReady,
            common,
            bounds);
        EmitDecodeEntry(
            builder,
            NesRomBuilder.WorldPackCollisionDecodeLabel,
            planeKind: 1,
            plan.Layout.CollisionSlots,
            plan.Pack.Descriptor.CollisionIdBytes,
            [NesRuntimeMemoryLayout.WorldPack.CollisionSlot0State, NesRuntimeMemoryLayout.WorldPack.CollisionSlot1State],
            [collisionSlot1],
            collisionReady,
            common,
            bounds);

        if (UsesFastCollisionLookup(plan))
        {
            EmitFastPlaneDescriptorReader(builder);
        }

        builder.Label(common);
        builder.CallSubroutine(NesRomBuilder.WorldPackValidateLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        var validated = builder.CreateLabel("worldpack_decode_validated");
        builder.BranchRelative(0xF0, validated);
        builder.Return();
        builder.Label(validated);
        EmitChunkBoundsCheck(builder, plan.Pack.Chunks.Count, miss, bounds);
        EmitBeginBulkRead(builder, placement);
        EmitRecordCriticalWork(builder, NesRuntimeMemoryLayout.PackedCamera.DirectoryWorkInCommit, "worldpack_directory");

        if (UsesFastCollisionLookup(plan))
        {
            builder.CallSubroutine(FastLoadPlaneDescriptorLabel);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.PlaneOffset0, NesRuntimeMemoryLayout.WorldPack.SourceOffset0, count: 4);
        }
        else
        {
            SetSourceOffset(builder, plan.Pack.Descriptor.DirectoryOffset);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, NesRuntimeMemoryLayout.WorldPack.WorkLow);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh, NesRuntimeMemoryLayout.WorldPack.WorkHigh);
            builder.Label(directoryLoop);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.WorkLow);
            builder.OrAbsolute(NesRuntimeMemoryLayout.WorldPack.WorkHigh);
            builder.BranchRelative(0xF0, directoryReady);
            AddToSourceOffset(builder, 20);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.WorkLow);
            builder.BranchRelative(0xD0, decrementLow);
            builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.WorkHigh);
            builder.Label(decrementLow);
            builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.WorkLow);
            builder.JumpAbsolute(directoryLoop);

            builder.Label(directoryReady);
            CopySourceOffset(builder, NesRuntimeMemoryLayout.WorldPack.DirectoryEntryOffset0);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.PlaneKind);
            builder.BranchRelative(0xF0, descriptorVisual);
            AddToSourceOffset(builder, 8);
            builder.JumpAbsolute(descriptorFields);
            builder.Label(descriptorVisual);
            builder.Label(descriptorFields);
            for (var index = 0; index < 4; index++)
            {
                builder.CallSubroutine(readAdvance);
                builder.JumpIf(0xB0, malformed);
                builder.StoreAAbsolute((ushort)(NesRuntimeMemoryLayout.WorldPack.PlaneOffset0 + index));
            }
            builder.CallSubroutine(readAdvance);
            builder.JumpIf(0xB0, malformed);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
            builder.CallSubroutine(readAdvance);
            builder.JumpIf(0xB0, malformed);
            builder.CompareImmediate(0);
            builder.JumpIf(0xD0, malformed);
            builder.CallSubroutine(readAdvance);
            builder.JumpIf(0xB0, malformed);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
            builder.CallSubroutine(readAdvance);
            builder.JumpIf(0xB0, malformed);
            builder.CompareImmediate(0);
            builder.JumpIf(0xD0, malformed);

            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.DirectoryEntryOffset0, NesRuntimeMemoryLayout.WorldPack.SourceOffset0, count: 4);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.PlaneKind);
            builder.BranchRelative(0xF0, codecVisual);
            AddToSourceOffset(builder, 19);
            builder.JumpAbsolute(codecReady);
            builder.Label(codecVisual);
            AddToSourceOffset(builder, 18);
            builder.Label(codecReady);
            builder.CallSubroutine(readAdvance);
            builder.JumpIf(0xB0, malformed);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.Codec);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.PlaneOffset0, NesRuntimeMemoryLayout.WorldPack.SourceOffset0, count: 4);
        }

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.Codec);
        builder.CompareImmediate((byte)WorldPackCodec.Raw);
        builder.BranchRelative(0xF0, raw);
        builder.CompareImmediate((byte)WorldPackCodec.ElementRle);
        builder.BranchRelative(0xF0, rle);
        builder.JumpAbsolute(malformed);

        builder.Label(raw);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.JumpIf(0xD0, malformed);
        builder.Label(rawLoop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.BranchRelative(0xF0, rawComplete);
        builder.CallSubroutine(readStore);
        builder.JumpIf(0xB0, malformed);
        builder.JumpAbsolute(rawLoop);
        builder.Label(rawComplete);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.JumpIf(0xD0, malformed);
        builder.JumpAbsolute(success);

        builder.Label(rle);
        builder.Label(rlePacket);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.JumpIf(0xF0, decodeComplete);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.PushA();
        builder.AndImmediate(0x80);
        builder.BranchRelative(0xD0, run);
        builder.PullA();
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.PacketCount);
        builder.Label(literal);
        builder.Label(literalLoop);
        builder.CallSubroutine(readStore);
        builder.JumpIf(0xB0, malformed);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdBytes);
        builder.CompareImmediate(2);
        builder.BranchRelative(0xD0, literalSecondDone);
        builder.CallSubroutine(readStore);
        builder.JumpIf(0xB0, malformed);
        builder.Label(literalSecondDone);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.PacketCount);
        builder.BranchRelative(0xD0, literalLoop);
        builder.JumpAbsolute(rlePacket);

        builder.Label(run);
        builder.PullA();
        builder.AndImmediate(0x7F);
        builder.ClearCarry();
        builder.AddImmediate(3);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.PacketCount);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.TempIdLow);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdBytes);
        builder.CompareImmediate(2);
        builder.BranchRelative(0xD0, runValueReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.TempIdHigh);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.Label(runValueReady);
        builder.Label(runLoop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.TempIdLow);
        builder.CallSubroutine(storeA);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdBytes);
        builder.CompareImmediate(2);
        builder.BranchRelative(0xD0, runSecondDone);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.TempIdHigh);
        builder.CallSubroutine(storeA);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.Label(runSecondDone);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.PacketCount);
        builder.BranchRelative(0xD0, runLoop);
        builder.JumpAbsolute(rlePacket);

        builder.Label(decodeComplete);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.JumpIf(0xD0, malformed);
        builder.JumpAbsolute(success);

        builder.Label(readAdvance);
        builder.CallSubroutine(NesRomBuilder.WorldPackReadByteLabel);
        var readAdvanceDone = builder.CreateLabel("worldpack_read_advance_done");
        builder.BranchRelative(0xB0, readAdvanceDone);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ReadValue);
        EmitIncrementSourceOffset(builder, readAdvanceDone);
        builder.Label(readAdvanceDone);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ReadValue);
        builder.Return();

        builder.Label(readStore);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.BranchRelative(0xF0, readStoreError);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.BranchRelative(0xF0, readStoreError);
        builder.CallSubroutine(readAdvance);
        builder.BranchRelative(0xB0, readStoreError);
        builder.CallSubroutine(storeA);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.StoredRemaining);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.DecodedRemaining);
        builder.ClearCarry();
        builder.Return();
        builder.Label(readStoreError);
        builder.SetCarry();
        builder.Return();

        builder.Label(storeA);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationByte);
        CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.DestinationLow, NesRuntimeMemoryLayout.WorldPack.PointerLow, count: 2);
        builder.LoadYImmediate(0);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidationByte);
        builder.StoreAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        var destinationIncremented = builder.CreateLabel("worldpack_destination_incremented");
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.DestinationLow);
        builder.BranchRelative(0xD0, destinationIncremented);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.DestinationHigh);
        builder.Label(destinationIncremented);
        builder.Return();

        builder.Label(bounds);
        EmitEndBulkRead(builder, placement);
        builder.LoadAImmediate((byte)NesWorldPackResult.BoundsError);
        builder.Return();
        builder.Label(miss);
        EmitEndBulkRead(builder, placement);
        builder.LoadAImmediate((byte)NesWorldPackResult.Miss);
        builder.Return();
        builder.Label(malformed);
        EmitEndBulkRead(builder, placement);
        builder.LoadAImmediate((byte)NesWorldPackResult.Malformed);
        builder.Return();
        builder.Label(success);
        StoreSelectedState(builder, 1);
        EmitEndBulkRead(builder, placement);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
    }

    private static void EmitLookups(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        var coordinates = builder.CreateLabel("worldpack_lookup_coordinates");
        var coordinateBounds = builder.CreateLabel("worldpack_lookup_coordinate_bounds");
        var coordinateSuccess = builder.CreateLabel("worldpack_lookup_coordinate_success");
        var visualContinue = builder.CreateLabel("worldpack_visual_lookup_continue");
        var collisionContinue = builder.CreateLabel("worldpack_collision_lookup_continue");
        var malformed = builder.CreateLabel("worldpack_lookup_malformed");
        var descriptor = plan.Pack.Descriptor;
        var collisionPlanes = plan.Planes.Where((_, index) => index % 2 == 1).ToArray();
        var hasRawCollisionPlanes = collisionPlanes.Any(plane => plane.Codec == WorldPackCodec.Raw);
        var hasRleCollisionPlanes = collisionPlanes.Any(plane => plane.Codec == WorldPackCodec.ElementRle);
        var useFastCoordinates = UsesFastCollisionLookup(plan);
        var fastCoordinates = builder.CreateLabel("worldpack_fast_lookup_coordinates");

        builder.Label(NesRomBuilder.WorldPackVisualLookupLabel);
        builder.CallSubroutine(useFastCoordinates ? fastCoordinates : coordinates);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.BranchRelative(0xF0, visualContinue);
        builder.Return();
        builder.Label(visualContinue);
        builder.Label(NesRomBuilder.WorldPackVisualLookupPreparedLabel);
        ushort[] fastVisualCacheTags =
        [
            NesRuntimeMemoryLayout.WorldPack.FastVisualCacheTag0,
            NesRuntimeMemoryLayout.WorldPack.FastVisualCacheTag1,
            NesRuntimeMemoryLayout.WorldPack.FastVisualCacheTag2,
            NesRuntimeMemoryLayout.WorldPack.FastVisualCacheTag3,
            NesRuntimeMemoryLayout.WorldPack.FastVisualCacheTag4,
            NesRuntimeMemoryLayout.WorldPack.FastVisualCacheTag5,
        ];
        var visualCaches = Enumerable.Range(0, plan.Layout.VisualSlots.Count)
            .Select(index => builder.CreateLabel($"worldpack_visual_cache_{index}"))
            .ToArray();
        var visualSelectSlots = Enumerable.Range(0, plan.Layout.VisualSlots.Count)
            .Select(index => builder.CreateLabel($"worldpack_visual_select_slot_{index}"))
            .ToArray();
        var visualPublishSlots = Enumerable.Range(0, plan.Layout.VisualSlots.Count)
            .Select(index => builder.CreateLabel($"worldpack_visual_publish_slot_{index}"))
            .ToArray();
        var visualCheckCache1 = builder.CreateLabel("worldpack_visual_check_cache_1");
        var visualSelectSlot = builder.CreateLabel("worldpack_visual_select_slot");
        var visualDecode = builder.CreateLabel("worldpack_visual_decode_selected");
        var visualRawDirect = builder.CreateLabel("worldpack_visual_raw_direct");
        var visualRawValidated = builder.CreateLabel("worldpack_visual_raw_validated");
        var visualIdReady = builder.CreateLabel("worldpack_visual_id_ready");

        if (useFastCoordinates)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
            builder.ClearCarry();
            builder.AddImmediate(1);
            for (var index = 0; index < fastVisualCacheTags.Length; index++)
            {
                builder.CompareAbsolute(fastVisualCacheTags[index]);
                builder.JumpIf(0xF0, visualCaches[index]);
            }
            builder.JumpAbsolute(visualSelectSlot);
        }
        else
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0Valid);
            builder.JumpIf(0xF0, visualCheckCache1);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0ChunkLow);
            builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
            builder.JumpIf(0xD0, visualCheckCache1);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0ChunkHigh);
            builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
            builder.JumpIf(0xF0, visualCaches[0]);

            builder.Label(visualCheckCache1);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1Valid);
            builder.JumpIf(0xF0, visualSelectSlot);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1ChunkLow);
            builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
            builder.JumpIf(0xD0, visualSelectSlot);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1ChunkHigh);
            builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
            builder.JumpIf(0xF0, visualCaches[1]);
        }

        builder.Label(visualSelectSlot);
        if (useFastCoordinates)
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.PlaneKind);
            builder.CallSubroutine(FastLoadPlaneDescriptorLabel);
        }
        else
        {
            EmitLoadChunkTableValue(builder, VisualPlaneCodecLabel, NesRuntimeMemoryLayout.WorldPack.Codec);
        }
        builder.CompareImmediate((byte)WorldPackCodec.Raw);
        builder.JumpIf(0xF0, visualRawDirect);
        if (useFastCoordinates)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualReplacementNext);
            for (var index = 0; index < visualSelectSlots.Length - 1; index++)
            {
                builder.CompareImmediate(index);
                builder.JumpIf(0xF0, visualSelectSlots[index]);
            }

            builder.JumpAbsolute(visualSelectSlots[^1]);
        }
        else
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0Valid);
            builder.JumpIf(0xF0, visualSelectSlots[0]);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1Valid);
            builder.JumpIf(0xF0, visualSelectSlots[1]);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualReplacementNext);
            builder.JumpIf(0xF0, visualSelectSlots[0]);
            builder.JumpAbsolute(visualSelectSlots[1]);
        }

        for (var index = 0; index < visualSelectSlots.Length; index++)
        {
            builder.Label(visualSelectSlots[index]);
            builder.LoadAImmediate(index);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
            builder.JumpAbsolute(visualDecode);
        }

        builder.Label(visualDecode);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualDecodeLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        var visualDecoded = builder.CreateLabel("worldpack_visual_lookup_decoded");
        builder.BranchRelative(0xF0, visualDecoded);
        builder.Return();
        builder.Label(visualDecoded);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
        if (useFastCoordinates)
        {
            for (var index = 0; index < visualPublishSlots.Length; index++)
            {
                builder.CompareImmediate(index);
                builder.JumpIf(0xF0, visualPublishSlots[index]);
            }
        }
        else
        {
            builder.CompareImmediate(1);
            builder.JumpIf(0xF0, visualPublishSlots[1]);
        }

        if (useFastCoordinates)
        {
            for (var index = 0; index < visualPublishSlots.Length; index++)
            {
                builder.Label(visualPublishSlots[index]);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
                builder.ClearCarry();
                builder.AddImmediate(1);
                builder.StoreAAbsolute(fastVisualCacheTags[index]);
                builder.LoadAImmediate((index + 1) % visualPublishSlots.Length);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualReplacementNext);
                builder.JumpAbsolute(visualCaches[index]);
            }
        }
        else
        {
            builder.LoadAImmediate(1);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache0Valid);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, NesRuntimeMemoryLayout.WorldPack.VisualCache0ChunkLow, count: 2);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualReplacementNext);
            builder.JumpAbsolute(visualCaches[0]);
            builder.Label(visualPublishSlots[1]);
            builder.LoadAImmediate(1);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualCache1Valid);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, NesRuntimeMemoryLayout.WorldPack.VisualCache1ChunkLow, count: 2);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualReplacementNext);
            builder.JumpAbsolute(visualCaches[1]);
        }

        builder.Label(visualRawDirect);
        builder.CallSubroutine(NesRomBuilder.WorldPackValidateLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.JumpIf(0xF0, visualRawValidated);
        builder.Return();
        builder.Label(visualRawValidated);
        if (useFastCoordinates)
        {
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.PlaneOffset0, NesRuntimeMemoryLayout.WorldPack.SourceOffset0, count: 4);
        }
        else
        {
            EmitLoadChunkTableValue(builder, VisualPlaneOffset0Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
            EmitLoadChunkTableValue(builder, VisualPlaneOffset1Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
            EmitLoadChunkTableValue(builder, VisualPlaneOffset2Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
            EmitLoadChunkTableValue(builder, VisualPlaneOffset3Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
        }
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
        if (descriptor.VisualIdBytes == 2)
        {
            builder.ShiftLeftA();
        }

        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
        builder.CallSubroutine(ReadAdvanceLabel);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdLow);
        if (descriptor.VisualIdBytes == 2)
        {
            builder.CallSubroutine(ReadAdvanceLabel);
            builder.JumpIf(0xB0, malformed);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdHigh);
        }
        else
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdHigh);
        }

        builder.JumpAbsolute(visualIdReady);

        if (useFastCoordinates)
        {
            for (var index = 0; index < visualCaches.Length; index++)
            {
                builder.Label(visualCaches[index]);
                builder.LoadAImmediate(index);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
                EmitLoadIdFromSlot(
                    builder,
                    plan.Layout.VisualSlots[index].Start,
                    descriptor.VisualIdBytes,
                    descriptor.VisualMetatileCount,
                    malformed);
                builder.JumpAbsolute(visualIdReady);
            }
        }
        else
        {
            for (var index = 0; index < 2; index++)
            {
                builder.Label(visualCaches[index]);
                builder.LoadAImmediate(index);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
                EmitLoadIdFromSlot(
                    builder,
                    plan.Layout.VisualSlots[index].Start,
                    descriptor.VisualIdBytes,
                    descriptor.VisualMetatileCount,
                    malformed);
                builder.JumpAbsolute(visualIdReady);
            }
        }

        builder.Label(visualIdReady);
        if (useFastCoordinates)
        {
            EmitLoadFastTargetExpansion(builder);
        }
        else
        {
            EmitLoadFixedTargetExpansion(
                builder,
                descriptor.MetatileWidth * descriptor.MetatileHeight);
        }
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(NesRomBuilder.WorldPackCollisionLookupLabel);
        if (useFastCoordinates)
        {
            EmitFastCollisionLookup(builder, plan, fastCoordinates);
        }
        else
        {
            builder.CallSubroutine(coordinates);
            builder.CompareImmediate((byte)NesWorldPackResult.Success);
            builder.BranchRelative(0xF0, collisionContinue);
            builder.Return();
            builder.Label(collisionContinue);
            var collisionCheckCache1 = builder.CreateLabel("worldpack_collision_check_cache_1");
            var collisionCache0 = builder.CreateLabel("worldpack_collision_cache_0");
            var collisionCache1 = builder.CreateLabel("worldpack_collision_cache_1");
            var collisionSelectSlot = builder.CreateLabel("worldpack_collision_select_slot");
            var collisionSelectSlot0 = builder.CreateLabel("worldpack_collision_select_slot_0");
            var collisionSelectSlot1 = builder.CreateLabel("worldpack_collision_select_slot_1");
            var collisionDecode = builder.CreateLabel("worldpack_collision_decode_selected");
            var collisionRawDirect = builder.CreateLabel("worldpack_collision_raw_direct");
            var collisionRawValidated = builder.CreateLabel("worldpack_collision_raw_validated");
            var collisionPublishSlot1 = builder.CreateLabel("worldpack_collision_publish_slot_1");
            var collisionIdReady = builder.CreateLabel("worldpack_collision_id_ready");

            if (!hasRleCollisionPlanes)
            {
                builder.JumpAbsolute(collisionRawDirect);
            }
            else
            {
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0Valid);
                builder.JumpIf(0xF0, collisionCheckCache1);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0ChunkLow);
                builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
                builder.JumpIf(0xD0, collisionCheckCache1);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0ChunkHigh);
                builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
                builder.JumpIf(0xF0, collisionCache0);

                builder.Label(collisionCheckCache1);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1Valid);
                builder.JumpIf(0xF0, collisionSelectSlot);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1ChunkLow);
                builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
                builder.JumpIf(0xD0, collisionSelectSlot);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1ChunkHigh);
                builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
                builder.JumpIf(0xF0, collisionCache1);

                builder.Label(collisionSelectSlot);
                if (hasRawCollisionPlanes)
                {
                    EmitLoadChunkTableValue(builder, CollisionPlaneCodecLabel, NesRuntimeMemoryLayout.WorldPack.Codec);
                    builder.CompareImmediate((byte)WorldPackCodec.Raw);
                    builder.JumpIf(0xF0, collisionRawDirect);
                }
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0Valid);
                builder.JumpIf(0xF0, collisionSelectSlot0);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1Valid);
                builder.JumpIf(0xF0, collisionSelectSlot1);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionReplacementNext);
                builder.JumpIf(0xF0, collisionSelectSlot0);
                builder.JumpAbsolute(collisionSelectSlot1);

                builder.Label(collisionSelectSlot0);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
                builder.JumpAbsolute(collisionDecode);
                builder.Label(collisionSelectSlot1);
                builder.LoadAImmediate(1);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
                builder.Label(collisionDecode);
                builder.CallSubroutine(NesRomBuilder.WorldPackCollisionDecodeLabel);
                builder.CompareImmediate((byte)NesWorldPackResult.Success);
                var collisionDecoded = builder.CreateLabel("worldpack_collision_lookup_decoded");
                builder.BranchRelative(0xF0, collisionDecoded);
                builder.Return();
                builder.Label(collisionDecoded);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
                builder.CompareImmediate(1);
                builder.JumpIf(0xF0, collisionPublishSlot1);
                builder.LoadAImmediate(1);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0Valid);
                CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, NesRuntimeMemoryLayout.WorldPack.CollisionCache0ChunkLow, count: 2);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionReplacementNext);
                builder.JumpAbsolute(collisionCache0);
                builder.Label(collisionPublishSlot1);
                builder.LoadAImmediate(1);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1Valid);
                CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, NesRuntimeMemoryLayout.WorldPack.CollisionCache1ChunkLow, count: 2);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionReplacementNext);
                builder.JumpAbsolute(collisionCache1);
            }

            if (hasRawCollisionPlanes)
            {
                builder.Label(collisionRawDirect);
                builder.CallSubroutine(NesRomBuilder.WorldPackValidateLabel);
                builder.CompareImmediate((byte)NesWorldPackResult.Success);
                builder.JumpIf(0xF0, collisionRawValidated);
                builder.Return();
                builder.Label(collisionRawValidated);
                EmitLoadChunkTableValue(builder, CollisionPlaneOffset0Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
                EmitLoadChunkTableValue(builder, CollisionPlaneOffset1Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
                EmitLoadChunkTableValue(builder, CollisionPlaneOffset2Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
                EmitLoadChunkTableValue(builder, CollisionPlaneOffset3Label, NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
                if (descriptor.CollisionIdBytes == 2)
                {
                    builder.ShiftLeftA();
                }

                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
                builder.ClearCarry();
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
                builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
                builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
                builder.AddImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
                builder.AddImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
                builder.CallSubroutine(ReadAdvanceLabel);
                builder.JumpIf(0xB0, malformed);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdLow);
                if (descriptor.CollisionIdBytes == 2)
                {
                    builder.CallSubroutine(ReadAdvanceLabel);
                    builder.JumpIf(0xB0, malformed);
                    builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdHigh);
                }
                else
                {
                    builder.LoadAImmediate(0);
                    builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdHigh);
                }

                builder.JumpAbsolute(collisionIdReady);
            }

            if (hasRleCollisionPlanes)
            {
                builder.Label(collisionCache0);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
                EmitLoadIdFromSlot(
                    builder,
                    plan.Layout.CollisionSlots[0].Start,
                    descriptor.CollisionIdBytes,
                    descriptor.CollisionProfileCount,
                    malformed);
                builder.JumpAbsolute(collisionIdReady);
                builder.Label(collisionCache1);
                builder.LoadAImmediate(1);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
                EmitLoadIdFromSlot(
                    builder,
                    plan.Layout.CollisionSlots[1].Start,
                    descriptor.CollisionIdBytes,
                    descriptor.CollisionProfileCount,
                    malformed);
            }
            builder.Label(collisionIdReady);
            EmitExpansionOffset(builder, descriptor.CollisionProfilesOffset, descriptor.MetatileWidth * descriptor.MetatileHeight, stride: 1);
            builder.CallSubroutine(ReadAdvanceLabel);
            builder.JumpIf(0xB0, malformed);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultCollision);
            builder.LoadAImmediate((byte)NesWorldPackResult.Success);
            builder.Return();
        }

        builder.Label(malformed);
        builder.LoadAImmediate((byte)NesWorldPackResult.Malformed);
        builder.Return();

        if (useFastCoordinates)
        {
            EmitFastLookupCoordinates(builder, plan, fastCoordinates);
        }
        else
        {
            builder.Label(coordinates);
            EmitUnsigned16BoundsCheck(
                builder,
                NesRuntimeMemoryLayout.WorldPack.HardwareXLow,
                descriptor.HardwareWidth,
                coordinateBounds);
            EmitUnsigned16BoundsCheck(
                builder,
                NesRuntimeMemoryLayout.WorldPack.HardwareYLow,
                descriptor.HardwareHeight,
                coordinateBounds);
            EmitDivide16ByConstant(
                builder,
                NesRuntimeMemoryLayout.WorldPack.HardwareXLow,
                NesRuntimeMemoryLayout.WorldPack.MetatileXLow,
                NesRuntimeMemoryLayout.WorldPack.SubcellX,
                descriptor.MetatileWidth);
            EmitDivide16ByConstant(
                builder,
                NesRuntimeMemoryLayout.WorldPack.HardwareYLow,
                NesRuntimeMemoryLayout.WorldPack.MetatileYLow,
                NesRuntimeMemoryLayout.WorldPack.SubcellY,
                descriptor.MetatileHeight);
            EmitDivide16By8(
                builder,
                NesRuntimeMemoryLayout.WorldPack.MetatileXLow,
                NesRuntimeMemoryLayout.WorldPack.ChunkXLow,
                NesRuntimeMemoryLayout.WorldPack.LocalX);
            EmitDivide16By8(
                builder,
                NesRuntimeMemoryLayout.WorldPack.MetatileYLow,
                NesRuntimeMemoryLayout.WorldPack.ChunkYLow,
                NesRuntimeMemoryLayout.WorldPack.LocalY);

            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkXLow, NesRuntimeMemoryLayout.WorldPack.MathResultLow, count: 2);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.ChunkYLow, NesRuntimeMemoryLayout.WorldPack.MathCountLow, count: 2);
            EmitAddConstantForCount(builder, descriptor.ChunkColumns);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.MathResultLow, NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, count: 2);

            var regularWidth = builder.CreateLabel("worldpack_lookup_regular_width");
            var widthReady = builder.CreateLabel("worldpack_lookup_width_ready");
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkXHigh);
            builder.CompareImmediate((descriptor.ChunkColumns - 1) >> 8);
            builder.BranchRelative(0xD0, regularWidth);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkXLow);
            builder.CompareImmediate((descriptor.ChunkColumns - 1) & 0xFF);
            builder.BranchRelative(0xD0, regularWidth);
            var sourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
            var lastWidth = sourceWidth - ((descriptor.ChunkColumns - 1) * 8);
            builder.LoadAImmediate(lastWidth);
            builder.JumpAbsolute(widthReady);
            builder.Label(regularWidth);
            builder.LoadAImmediate(8);
            builder.Label(widthReady);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidWidth);

            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.LocalX, NesRuntimeMemoryLayout.WorldPack.MathResultLow);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.LocalY, NesRuntimeMemoryLayout.WorldPack.MathCountLow);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountHigh);
            EmitAddAbsoluteForCount(builder, NesRuntimeMemoryLayout.WorldPack.ValidWidth);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.MathResultLow, NesRuntimeMemoryLayout.WorldPack.CellIndex);

            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.SubcellX, NesRuntimeMemoryLayout.WorldPack.MathResultLow);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.SubcellY, NesRuntimeMemoryLayout.WorldPack.MathCountLow);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountHigh);
            EmitAddConstantForCount(builder, descriptor.MetatileWidth);
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.MathResultLow, NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow, count: 2);

            builder.Label(coordinateSuccess);
            builder.LoadAImmediate((byte)NesWorldPackResult.Success);
            builder.Return();
            builder.Label(coordinateBounds);
            builder.LoadAImmediate((byte)NesWorldPackResult.BoundsError);
            builder.Return();
        }
    }

    private static void EmitFastLookupCoordinates(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        string entry)
    {
        var descriptor = plan.Pack.Descriptor;
        var bounds = builder.CreateLabel("worldpack_fast_coordinate_bounds");
        var regularWidth = builder.CreateLabel("worldpack_fast_coordinate_regular_width");
        var cellReady = builder.CreateLabel("worldpack_fast_coordinate_cell_ready");

        builder.Label(entry);
        EmitUnsigned16BoundsCheck(
            builder,
            NesRuntimeMemoryLayout.WorldPack.HardwareXLow,
            descriptor.HardwareWidth,
            bounds);
        EmitUnsigned16BoundsCheck(
            builder,
            NesRuntimeMemoryLayout.WorldPack.HardwareYLow,
            descriptor.HardwareHeight,
            bounds);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.AndImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.AndImmediate(1);
        builder.ShiftLeftA();
        builder.OrAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        for (var shift = 0; shift < 4; shift++)
        {
            builder.ShiftLeftA();
        }
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        for (var shift = 0; shift < 4; shift++)
        {
            builder.ShiftRightA();
        }
        builder.OrAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkXLow);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.ShiftRightA();
        builder.AndImmediate(7);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalX);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.ShiftRightA();
        builder.AndImmediate(7);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalY);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        for (var shift = 0; shift < 4; shift++)
        {
            builder.ShiftRightA();
        }
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkYLow);

        if ((descriptor.ChunkColumns & (descriptor.ChunkColumns - 1)) == 0)
        {
            for (var factor = 1; factor < descriptor.ChunkColumns; factor <<= 1)
            {
                builder.ShiftLeftA();
            }

            builder.ClearCarry();
        }
        else
        {
            // The complete stage1 layout has 20 chunk columns: y * 20 = y * 16 + y * 4.
            builder.ShiftLeftA();
            builder.ShiftLeftA();
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
            builder.ShiftLeftA();
            builder.ShiftLeftA();
            builder.ClearCarry();
            builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        }

        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkXLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexHigh);

        var sourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
        var lastWidth = sourceWidth - ((descriptor.ChunkColumns - 1) * 8);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkXLow);
        builder.CompareImmediate(descriptor.ChunkColumns - 1);
        builder.BranchRelative(0xD0, regularWidth);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalY);
        for (var factor = 1; factor < lastWidth; factor <<= 1)
        {
            builder.ShiftLeftA();
        }
        builder.JumpAbsolute(cellReady);
        builder.Label(regularWidth);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalY);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.Label(cellReady);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalX);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);

        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
        builder.Label(bounds);
        builder.LoadAImmediate((byte)NesWorldPackResult.BoundsError);
        builder.Return();
    }

    private static void EmitFastCollisionLookup(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        string coordinates)
    {
        var descriptor = plan.Pack.Descriptor;
        var coordinateReady = builder.CreateLabel("worldpack_fast_collision_coordinate_ready");
        var selectSlot0 = builder.CreateLabel("worldpack_fast_collision_select_slot_0");
        var selectSlot1 = builder.CreateLabel("worldpack_fast_collision_select_slot_1");
        var decode = builder.CreateLabel("worldpack_fast_collision_decode");
        var decoded = builder.CreateLabel("worldpack_fast_collision_decoded");
        var publishSlot1 = builder.CreateLabel("worldpack_fast_collision_publish_slot_1");
        var cache0 = builder.CreateLabel("worldpack_fast_collision_cache_0");
        var cache1 = builder.CreateLabel("worldpack_fast_collision_cache_1");
        var idReady = builder.CreateLabel("worldpack_fast_collision_id_ready");
        var cellMiss = builder.CreateLabel("worldpack_fast_collision_cell_miss");
        var malformed = builder.CreateLabel("worldpack_fast_collision_malformed");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
        builder.BranchRelative(0xD0, cellMiss);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellYTag);
        builder.BranchRelative(0xD0, cellMiss);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellXLow);
        builder.BranchRelative(0xD0, cellMiss);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellXHigh);
        builder.BranchRelative(0xD0, cellMiss);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellResult);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultCollision);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(cellMiss);
        builder.CallSubroutine(coordinates);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.JumpIf(0xF0, coordinateReady);
        builder.Return();
        builder.Label(coordinateReady);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0Valid);
        builder.JumpIf(0xF0, cache0);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1Valid);
        builder.JumpIf(0xF0, cache1);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0Valid);
        builder.JumpIf(0xF0, selectSlot0);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1Valid);
        builder.JumpIf(0xF0, selectSlot1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionReplacementNext);
        builder.JumpIf(0xF0, selectSlot0);
        builder.JumpAbsolute(selectSlot1);

        builder.Label(selectSlot0);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
        builder.JumpAbsolute(decode);
        builder.Label(selectSlot1);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
        builder.Label(decode);
        builder.CallSubroutine(NesRomBuilder.WorldPackCollisionDecodeLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.JumpIf(0xF0, decoded);
        builder.Return();
        builder.Label(decoded);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, publishSlot1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache0Valid);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionReplacementNext);
        builder.JumpAbsolute(cache0);
        builder.Label(publishSlot1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCache1Valid);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionReplacementNext);
        builder.JumpAbsolute(cache1);

        builder.Label(cache0);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
        builder.TransferAToX();
        builder.LoadAAbsoluteX(plan.Layout.CollisionSlots[0].Start);
        builder.JumpAbsolute(idReady);
        builder.Label(cache1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
        builder.TransferAToX();
        builder.LoadAAbsoluteX(plan.Layout.CollisionSlots[1].Start);
        builder.Label(idReady);
        builder.CompareImmediate(descriptor.CollisionProfileCount);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdLow);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        builder.TransferAToX();
        builder.LdaAbsoluteX(CollisionProfilesLabel);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultCollision);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellResult);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellXLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellXHigh);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionCellYTag);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(malformed);
        builder.LoadAImmediate((byte)NesWorldPackResult.Malformed);
        builder.Return();
    }

    private static void EmitUnsigned16BoundsCheck(
        PrgBuilder builder,
        ushort valueLow,
        int limit,
        string bounds)
    {
        var inBounds = builder.CreateLabel("worldpack_coordinate_in_bounds");
        builder.LoadAAbsolute((ushort)(valueLow + 1));
        builder.CompareImmediate((limit >> 8) & 0xFF);
        builder.BranchRelative(0x90, inBounds);
        builder.JumpIf(0xD0, bounds);
        builder.LoadAAbsolute(valueLow);
        builder.CompareImmediate(limit & 0xFF);
        builder.JumpIf(0xB0, bounds);
        builder.Label(inBounds);
    }

    private static void EmitDivide16ByConstant(
        PrgBuilder builder,
        ushort sourceLow,
        ushort quotientLow,
        ushort remainder,
        int divisor)
    {
        if (divisor == 1)
        {
            CopyAbsolute(builder, sourceLow, quotientLow, count: 2);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(remainder);
            return;
        }

        if ((divisor & (divisor - 1)) == 0)
        {
            builder.LoadAAbsolute(sourceLow);
            builder.AndImmediate(divisor - 1);
            builder.StoreAAbsolute(remainder);
            CopyAbsolute(builder, sourceLow, quotientLow, count: 2);
            for (var shift = 1; shift < divisor; shift <<= 1)
            {
                builder.ShiftRightAbsolute(checked((ushort)(quotientLow + 1)));
                builder.RotateRightAbsolute(quotientLow);
            }

            return;
        }

        CopyAbsolute(builder, sourceLow, NesRuntimeMemoryLayout.WorldPack.MathValueLow, count: 2);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(quotientLow);
        builder.StoreAAbsolute((ushort)(quotientLow + 1));
        var loop = builder.CreateLabel("worldpack_divide_loop");
        var subtract = builder.CreateLabel("worldpack_divide_subtract");
        var incremented = builder.CreateLabel("worldpack_divide_incremented");
        var done = builder.CreateLabel("worldpack_divide_done");
        builder.Label(loop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathValueHigh);
        builder.BranchRelative(0xD0, subtract);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathValueLow);
        builder.CompareImmediate(divisor);
        builder.BranchRelative(0x90, done);
        builder.Label(subtract);
        builder.SetCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathValueLow);
        builder.SubtractImmediate(divisor);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathValueLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathValueHigh);
        builder.SubtractImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathValueHigh);
        builder.IncrementAbsolute(quotientLow);
        builder.BranchRelative(0xD0, incremented);
        builder.IncrementAbsolute((ushort)(quotientLow + 1));
        builder.Label(incremented);
        builder.JumpAbsolute(loop);
        builder.Label(done);
        CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.MathValueLow, remainder);
    }

    private static void EmitDivide16By8(
        PrgBuilder builder,
        ushort sourceLow,
        ushort quotientLow,
        ushort remainder)
    {
        builder.LoadAAbsolute(sourceLow);
        builder.AndImmediate(7);
        builder.StoreAAbsolute(remainder);
        CopyAbsolute(builder, sourceLow, quotientLow, count: 2);
        for (var shift = 0; shift < 3; shift++)
        {
            builder.ShiftRightAbsolute((ushort)(quotientLow + 1));
            builder.RotateRightAbsolute(quotientLow);
        }
    }

    private static void EmitAddConstantForCount(PrgBuilder builder, int value)
    {
        var loop = builder.CreateLabel("worldpack_multiply_loop");
        var decrementLow = builder.CreateLabel("worldpack_multiply_decrement_low");
        var done = builder.CreateLabel("worldpack_multiply_done");
        builder.Label(loop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountLow);
        builder.OrAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountHigh);
        builder.BranchRelative(0xF0, done);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.AddImmediate(value & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.AddImmediate((value >> 8) & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountLow);
        builder.BranchRelative(0xD0, decrementLow);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountHigh);
        builder.Label(decrementLow);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountLow);
        builder.JumpAbsolute(loop);
        builder.Label(done);
    }

    private static void EmitAddAbsoluteForCount(PrgBuilder builder, ushort valueAddress)
    {
        var loop = builder.CreateLabel("worldpack_add_count_loop");
        var done = builder.CreateLabel("worldpack_add_count_done");
        builder.Label(loop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountLow);
        builder.BranchRelative(0xF0, done);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.AddAbsolute(valueAddress);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountLow);
        builder.JumpAbsolute(loop);
        builder.Label(done);
    }

    private static void EmitLoadIdFromSlot(
        PrgBuilder builder,
        ushort slotStart,
        int idBytes,
        int idCount,
        string malformed)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
        if (idBytes == 2)
        {
            builder.ShiftLeftA();
        }
        builder.TransferAToX();
        builder.LoadAAbsoluteX(slotStart);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdLow);
        if (idBytes == 2)
        {
            builder.LoadAAbsoluteX((ushort)(slotStart + 1));
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdHigh);
        }
        else
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdHigh);
        }

        var inBounds = builder.CreateLabel("worldpack_id_in_bounds");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdHigh);
        builder.CompareImmediate((idCount >> 8) & 0xFF);
        builder.BranchRelative(0x90, inBounds);
        builder.JumpIf(0xD0, malformed);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdLow);
        builder.CompareImmediate(idCount & 0xFF);
        builder.JumpIf(0xB0, malformed);
        builder.Label(inBounds);
    }

    private static void EmitExpansionOffset(
        PrgBuilder builder,
        uint sectionOffset,
        int metatileCells,
        int stride)
    {
        CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow, NesRuntimeMemoryLayout.WorldPack.MathResultLow, count: 2);
        CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.IdLow, NesRuntimeMemoryLayout.WorldPack.MathCountLow, count: 2);
        EmitAddConstantForCount(builder, metatileCells);
        if (stride == 2)
        {
            builder.ClearCarry();
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
            builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
            builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        }

        SetSourceOffset(builder, sectionOffset);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset2);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset3);
    }

    private static void EmitLoadFixedTargetExpansion(PrgBuilder builder, int metatileCells)
    {
        CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.IdLow, NesRuntimeMemoryLayout.WorldPack.MathResultLow, count: 2);
        if ((metatileCells & (metatileCells - 1)) == 0)
        {
            for (var factor = 1; factor < metatileCells; factor <<= 1)
            {
                builder.ClearCarry();
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
                builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
                builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
            }
        }
        else
        {
            CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.IdLow, NesRuntimeMemoryLayout.WorldPack.MathCountLow, count: 2);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
            EmitAddConstantForCount(builder, metatileCells);
        }

        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);

        EmitLoadPointer(builder, TargetExpansionsLabel, NesRuntimeMemoryLayout.WorldPack.MathResultLow, NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultTile);
        builder.LoadYImmediate(1);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultMetadata);
    }

    private static void EmitLoadFastTargetExpansion(PrgBuilder builder)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdLow);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdLow);
        for (var shift = 0; shift < 5; shift++)
        {
            builder.ShiftRightA();
        }
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        builder.ShiftLeftA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountLow);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.MathCountLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        EmitLoadPointer(builder, TargetExpansionsLabel, NesRuntimeMemoryLayout.WorldPack.MathResultLow, NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultTile);
        builder.LoadYImmediate(1);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultMetadata);
    }

    private static void EmitLoadChunkTableValue(PrgBuilder builder, string tableLabel, ushort destination)
    {
        EmitLoadPointer(builder, tableLabel, NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.StoreAAbsolute(destination);
    }

    private static void EmitDecodeEntry(
        PrgBuilder builder,
        string entryLabel,
        byte planeKind,
        IReadOnlyList<NesRamRange> slots,
        int idBytes,
        IReadOnlyList<ushort> states,
        IReadOnlyList<string> slotLabels,
        string ready,
        string common,
        string bounds)
    {
        builder.Label(entryLabel);
        EmitRecordCriticalWork(builder, NesRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit, "worldpack_decode");
        if (planeKind == 0)
        {
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.VisualDecodeCount);
        }
        else
        {
            var decodeCountDone = builder.CreateLabel("worldpack_collision_decode_count_done");
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionDecodeCountLow);
            builder.BranchRelative(0xD0, decodeCountDone);
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.CollisionDecodeCountHigh);
            builder.Label(decodeCountDone);
        }
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
        builder.CompareImmediate(slots.Count);
        builder.JumpIf(0xB0, bounds);
        for (var index = 1; index < slots.Count; index++)
        {
            builder.CompareImmediate(index);
            builder.JumpIf(0xF0, slotLabels[index - 1]);
        }
        SetDestination(builder, slots[0]);
        SetSelectedStateAddress(builder, states[0]);
        builder.JumpAbsolute(ready);
        for (var index = 1; index < slots.Count; index++)
        {
            builder.Label(slotLabels[index - 1]);
            SetDestination(builder, slots[index]);
            SetSelectedStateAddress(builder, states[index]);
            builder.JumpAbsolute(ready);
        }
        builder.Label(ready);
        StoreSelectedState(builder, 0);
        builder.LoadAImmediate(planeKind);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.PlaneKind);
        builder.LoadAImmediate(idBytes);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.IdBytes);
        builder.JumpAbsolute(common);
    }

    private static void EmitFastPlaneDescriptorReader(PrgBuilder builder)
    {
        var visual = builder.CreateLabel("worldpack_fast_descriptor_visual");
        var baseReady = builder.CreateLabel("worldpack_fast_descriptor_base_ready");

        builder.Label(FastLoadPlaneDescriptorLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        for (var shift = 0; shift < 5; shift++)
        {
            builder.ShiftRightA();
        }
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.SetCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.SubtractImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.MathResultHigh);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.PlaneKind);
        builder.JumpIf(0xF0, visual);
        EmitLoadPointer(builder, FastCollisionPlaneDescriptorsLabel, NesRuntimeMemoryLayout.WorldPack.MathResultLow, NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.JumpAbsolute(baseReady);
        builder.Label(visual);
        EmitLoadPointer(builder, FastVisualPlaneDescriptorsLabel, NesRuntimeMemoryLayout.WorldPack.MathResultLow, NesRuntimeMemoryLayout.WorldPack.MathResultHigh);
        builder.Label(baseReady);

        builder.LoadYImmediate(0);
        for (var index = 0; index < 7; index++)
        {
            builder.LoadAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
            builder.StoreAAbsoluteY(NesRuntimeMemoryLayout.WorldPack.PlaneOffset0);
            if (index < 6)
            {
                builder.IncrementY();
            }
        }
        builder.Return();
    }

    private static void EmitLoadPointer(PrgBuilder builder, string tableLabel, ushort offsetLow, ushort offsetHigh)
    {
        builder.LoadAImmediateLabelLowByte(tableLabel);
        builder.ClearCarry();
        builder.AddAbsolute(offsetLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.WorldPack.PointerLow);
        builder.LoadAImmediateLabelHighByte(tableLabel);
        builder.AddAbsolute(offsetHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.WorldPack.PointerHigh);
    }

    private static void SetDestination(PrgBuilder builder, NesRamRange slot)
    {
        builder.LoadAImmediate(slot.Start & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.DestinationLow);
        builder.LoadAImmediate(slot.Start >> 8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.DestinationHigh);
    }

    private static void EmitRecordCriticalWork(PrgBuilder builder, ushort counter, string labelPrefix)
    {
        var done = builder.CreateLabel($"{labelPrefix}_outside_commit");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.BranchRelative(0xF0, done);
        builder.IncrementAbsolute(counter);
        builder.Label(done);
    }

    private static void SetSelectedStateAddress(PrgBuilder builder, ushort address)
    {
        builder.LoadAImmediate(address & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SelectedStateAddressLow);
        builder.LoadAImmediate(address >> 8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SelectedStateAddressHigh);
    }

    private static void StoreSelectedState(PrgBuilder builder, byte state)
    {
        CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.SelectedStateAddressLow, NesRuntimeMemoryLayout.WorldPack.PointerLow, count: 2);
        builder.LoadYImmediate(0);
        builder.LoadAImmediate(state);
        builder.StoreAIndirectY(NesRuntimeMemoryLayout.WorldPack.PointerLow);
    }

    private static void EmitChunkBoundsCheck(
        PrgBuilder builder,
        int chunkCount,
        string miss,
        string bounds)
    {
        var inBounds = builder.CreateLabel("worldpack_chunk_in_bounds");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
        builder.CompareImmediate((chunkCount >> 8) & 0xFF);
        builder.BranchRelative(0x90, inBounds);
        builder.JumpIf(0xD0, bounds);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.CompareImmediate(chunkCount & 0xFF);
        builder.BranchRelative(0x90, inBounds);
        builder.JumpIf(0xF0, miss);
        builder.JumpAbsolute(bounds);
        builder.Label(inBounds);
    }

    private static void SetSourceOffset(PrgBuilder builder, uint offset)
    {
        for (var index = 0; index < 4; index++)
        {
            builder.LoadAImmediate((byte)(offset >> (index * 8)));
            builder.StoreAAbsolute((ushort)(NesRuntimeMemoryLayout.WorldPack.SourceOffset0 + index));
        }
    }

    private static void AddToSourceOffset(PrgBuilder builder, int value)
    {
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
        builder.AddImmediate(value);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
        for (var index = 1; index < 4; index++)
        {
            builder.LoadAAbsolute((ushort)(NesRuntimeMemoryLayout.WorldPack.SourceOffset0 + index));
            builder.AddImmediate(0);
            builder.StoreAAbsolute((ushort)(NesRuntimeMemoryLayout.WorldPack.SourceOffset0 + index));
        }
    }

    private static void CopySourceOffset(PrgBuilder builder, ushort destination) =>
        CopyAbsolute(builder, NesRuntimeMemoryLayout.WorldPack.SourceOffset0, destination, count: 4);

    private static void CopyAbsolute(PrgBuilder builder, ushort source, ushort destination, int count = 1)
    {
        for (var index = 0; index < count; index++)
        {
            builder.LoadAAbsolute((ushort)(source + index));
            builder.StoreAAbsolute((ushort)(destination + index));
        }
    }

    private static void EmitOffsetEqualityCheck(
        PrgBuilder builder,
        uint value,
        string equal,
        string notEqual)
    {
        var bytes = new[]
        {
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24),
        };
        var addresses = new[]
        {
            NesRuntimeMemoryLayout.WorldPack.SourceOffset0,
            NesRuntimeMemoryLayout.WorldPack.SourceOffset1,
            NesRuntimeMemoryLayout.WorldPack.SourceOffset2,
            NesRuntimeMemoryLayout.WorldPack.SourceOffset3,
        };
        for (var index = 3; index >= 0; index--)
        {
            builder.LoadAAbsolute(addresses[index]);
            builder.CompareImmediate(bytes[index]);
            builder.BranchRelative(0xD0, notEqual);
        }

        builder.JumpAbsolute(equal);
    }

    private static void EmitIncrementSourceOffset(PrgBuilder builder, string done)
    {
        foreach (var address in new[]
                 {
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset0,
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset1,
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset2,
                     NesRuntimeMemoryLayout.WorldPack.SourceOffset3,
                 })
        {
            builder.IncrementAbsolute(address);
            builder.BranchRelative(0xD0, done);
        }
    }

    private static ushort ComputeCrc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? (ushort)((crc >> 1) ^ 0xA001)
                    : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static void EmitPlaneByteTable(
        PrgBuilder builder,
        string label,
        IReadOnlyList<NesWorldPackPlaneRuntimeDescriptor> planes,
        Func<NesWorldPackPlaneRuntimeDescriptor, byte> selector)
    {
        builder.Label(label);
        builder.Emit(planes.Select(selector).ToArray());
    }

    private static void EmitReadByte(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesWorldPackPlacement? placement)
    {
        const byte pointerLow = NesRuntimeMemoryLayout.WorldPack.PointerLow;
        const byte pointerHigh = NesRuntimeMemoryLayout.WorldPack.PointerHigh;
        var inBounds = builder.CreateLabel("worldpack_read_in_bounds");
        var boundsError = builder.CreateLabel("worldpack_read_bounds_error");
        var bankTable = builder.CreateLabel("worldpack_r6_bank_table");
        var bulkRead = builder.CreateLabel("worldpack_bulk_read");
        var bulkInBounds = builder.CreateLabel("worldpack_bulk_read_in_bounds");
        var bulkBankReady = builder.CreateLabel("worldpack_bulk_bank_ready");
        var bulkBounds = builder.CreateLabel("worldpack_bulk_read_bounds");

        builder.Label(NesRomBuilder.WorldPackReadByteLabel);
        if (placement is not null)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadActive);
            builder.JumpIf(0xD0, bulkRead);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow);
            builder.PushA();
            for (var index = 0; index < 4; index++)
            {
                builder.LoadAAbsolute((ushort)(NesRuntimeMemoryLayout.WorldPack.SourceOffset0 + index));
                builder.PushA();
            }
        }

        EmitOffsetBoundsCheck(builder, plan.Pack.Descriptor.PackLength, inBounds, boundsError);

        builder.Label(inBounds);
        if (placement is not null)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
            for (var shift = 0; shift < 5; shift++)
            {
                builder.ShiftRightA();
            }
            builder.TransferAToX();
            builder.LdaAbsoluteX(bankTable);
            builder.CallSubroutine("mmc3_select_r6");
            RestoreSourceOffsetFromStack(builder);

            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
            builder.StoreAZeroPage(pointerLow);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
            builder.AndImmediate(0x1F);
            builder.OrImmediate(0x80);
            builder.StoreAZeroPage(pointerHigh);
        }
        else
        {
            builder.ClearCarry();
            builder.LoadAImmediateLabelLowByte(NesRomBuilder.WorldPackLabel);
            builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
            builder.StoreAZeroPage(pointerLow);
            builder.LoadAImmediateLabelHighByte(NesRomBuilder.WorldPackLabel);
            builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
            builder.StoreAZeroPage(pointerHigh);
        }

        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(pointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ReadValue);
        if (placement is not null)
        {
            builder.PullA();
            builder.CallSubroutine("mmc3_select_r6");
        }
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ReadValue);
        builder.ClearCarry();
        builder.Return();

        builder.Label(boundsError);
        if (placement is not null)
        {
            RestoreSourceOffsetFromStack(builder);
            builder.PullA();
            builder.CallSubroutine("mmc3_select_r6");
        }
        builder.LoadAImmediate(0);
        builder.SetCarry();
        builder.Return();

        if (placement is not null)
        {
            builder.Label(bulkRead);
            EmitOffsetBoundsCheck(builder, plan.Pack.Descriptor.PackLength, bulkInBounds, bulkBounds);
            builder.Label(bulkInBounds);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
            for (var shift = 0; shift < 5; shift++)
            {
                builder.ShiftRightA();
            }

            builder.TransferAToX();
            builder.LdaAbsoluteX(bankTable);
            builder.CompareAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadCurrentBank);
            builder.JumpIf(0xF0, bulkBankReady);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadCurrentBank);
            builder.CallSubroutine("mmc3_select_r6");
            builder.Label(bulkBankReady);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset0);
            builder.StoreAZeroPage(pointerLow);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SourceOffset1);
            builder.AndImmediate(0x1F);
            builder.OrImmediate(0x80);
            builder.StoreAZeroPage(pointerHigh);
            builder.LoadYImmediate(0);
            builder.LoadAIndirectY(pointerLow);
            builder.ClearCarry();
            builder.Return();
            builder.Label(bulkBounds);
            builder.LoadAImmediate(0);
            builder.SetCarry();
            builder.Return();

            builder.Label(bankTable);
            builder.Emit(placement.Segments.Select(segment => checked((byte)segment.PhysicalBank)).ToArray());
        }
    }

    private static void EmitBeginBulkRead(PrgBuilder builder, NesWorldPackPlacement? placement)
    {
        if (placement is null)
        {
            return;
        }

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadEntryBank);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadCurrentBank);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadActive);
    }

    private static void EmitEndBulkRead(PrgBuilder builder, NesWorldPackPlacement? placement)
    {
        if (placement is null)
        {
            return;
        }

        var done = builder.CreateLabel("worldpack_bulk_end_done");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadActive);
        builder.JumpIf(0xF0, done);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadEntryBank);
        builder.CallSubroutine("mmc3_select_r6");
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.BulkReadActive);
        builder.Label(done);
    }

    private static void RestoreSourceOffsetFromStack(PrgBuilder builder)
    {
        for (var index = 3; index >= 0; index--)
        {
            builder.PullA();
            builder.StoreAAbsolute((ushort)(NesRuntimeMemoryLayout.WorldPack.SourceOffset0 + index));
        }
    }

    private static void EmitOffsetBoundsCheck(
        PrgBuilder builder,
        uint length,
        string inBounds,
        string boundsError)
    {
        var bytes = new[]
        {
            (byte)length,
            (byte)(length >> 8),
            (byte)(length >> 16),
            (byte)(length >> 24),
        };
        var addresses = new[]
        {
            NesRuntimeMemoryLayout.WorldPack.SourceOffset0,
            NesRuntimeMemoryLayout.WorldPack.SourceOffset1,
            NesRuntimeMemoryLayout.WorldPack.SourceOffset2,
            NesRuntimeMemoryLayout.WorldPack.SourceOffset3,
        };
        for (var index = 3; index >= 0; index--)
        {
            builder.LoadAAbsolute(addresses[index]);
            builder.CompareImmediate(bytes[index]);
            builder.BranchRelative(0x90, inBounds);   // BCC: a lower high-order byte proves offset < length.
            builder.BranchRelative(0xD0, boundsError); // BNE with carry: offset > length.
        }

        builder.JumpAbsolute(boundsError);            // Equality is also out of bounds.
    }
}
