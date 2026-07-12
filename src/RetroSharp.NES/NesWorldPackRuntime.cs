namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal sealed record NesWorldPackProbe(ushort HardwareX, ushort HardwareY);

internal static class NesWorldPackRuntimeAbi
{
    internal const ushort ValidationState = 0x0326;
    internal const ushort SourceOffset0 = 0x0327;
    internal const ushort SourceOffset1 = 0x0328;
    internal const ushort SourceOffset2 = 0x0329;
    internal const ushort SourceOffset3 = 0x032A;
    internal const ushort ReadValue = 0x032B;
    internal const ushort ValidationCrcLow = 0x032C;
    internal const ushort ValidationCrcHigh = 0x032D;
    internal const ushort ValidationByte = 0x032E;
    internal const ushort ChunkIndexLow = 0x032F;
    internal const ushort ChunkIndexHigh = 0x0330;
    internal const ushort SlotIndex = 0x0331;
    internal const ushort PlaneKind = 0x0332;
    internal const ushort DirectoryEntryOffset0 = 0x0333;
    internal const ushort DirectoryEntryOffset1 = 0x0334;
    internal const ushort DirectoryEntryOffset2 = 0x0335;
    internal const ushort DirectoryEntryOffset3 = 0x0336;
    internal const ushort PlaneOffset0 = 0x0337;
    internal const ushort PlaneOffset1 = 0x0338;
    internal const ushort PlaneOffset2 = 0x0339;
    internal const ushort PlaneOffset3 = 0x033A;
    internal const ushort StoredRemaining = 0x033B;
    internal const ushort DecodedRemaining = 0x033C;
    internal const ushort Codec = 0x033D;
    internal const ushort DestinationLow = 0x033E;
    internal const ushort DestinationHigh = 0x033F;
    internal const ushort WorkLow = 0x0340;
    internal const ushort WorkHigh = 0x0341;
    internal const ushort PacketCount = 0x0342;
    internal const ushort IdBytes = 0x0343;
    internal const ushort TempIdLow = 0x0344;
    internal const ushort TempIdHigh = 0x0345;
    internal const ushort HardwareXLow = 0x0346;
    internal const ushort HardwareXHigh = 0x0347;
    internal const ushort HardwareYLow = 0x0348;
    internal const ushort HardwareYHigh = 0x0349;
    internal const ushort ResultTile = 0x034A;
    internal const ushort ResultMetadata = 0x034B;
    internal const ushort ResultCollision = 0x034C;
    internal const ushort MetatileXLow = 0x034D;
    internal const ushort MetatileXHigh = 0x034E;
    internal const ushort MetatileYLow = 0x034F;
    internal const ushort MetatileYHigh = 0x0350;
    internal const ushort SubcellX = 0x0351;
    internal const ushort SubcellY = 0x0352;
    internal const ushort ChunkXLow = 0x0353;
    internal const ushort ChunkXHigh = 0x0354;
    internal const ushort ChunkYLow = 0x0355;
    internal const ushort ChunkYHigh = 0x0356;
    internal const ushort LocalX = 0x0357;
    internal const ushort LocalY = 0x0358;
    internal const ushort CellIndex = 0x0359;
    internal const ushort SubcellIndexLow = 0x035A;
    internal const ushort SubcellIndexHigh = 0x035B;
    internal const ushort IdLow = 0x035C;
    internal const ushort IdHigh = 0x035D;
    internal const ushort MathValueLow = 0x035E;
    internal const ushort MathValueHigh = 0x035F;
    internal const ushort MathCountLow = 0x0360;
    internal const ushort MathCountHigh = 0x0361;
    internal const ushort MathResultLow = 0x0362;
    internal const ushort MathResultHigh = 0x0363;
    internal const ushort ValidWidth = 0x0364;
    internal const ushort VisualSlot0State = 0x0365;
    internal const ushort VisualSlot1State = 0x0366;
    internal const ushort CollisionSlot0State = 0x0367;
    internal const ushort CollisionSlot1State = 0x0368;
    internal const ushort SelectedStateAddressLow = 0x0369;
    internal const ushort SelectedStateAddressHigh = 0x036A;
    internal const ushort ProbeVisualStatus = 0x036B;
    internal const ushort ProbeCollisionStatus = 0x036C;
    internal const ushort ProbeCompleted = 0x036D;
}

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

internal sealed record NesWorldPackRuntimePlan(
    WorldPack Pack,
    byte[] SerializedBytes,
    NesWorldPackRuntimeLayout Layout,
    byte[] HeaderBytes,
    byte[] CollisionProfileBytes,
    byte[] TargetExpansionBytes,
    byte[] DirectoryBytes,
    IReadOnlyList<NesWorldPackPlaneRuntimeDescriptor> Planes)
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

        var layout = NesWorldPackRuntimeLayout.Create(descriptor.VisualIdBytes, descriptor.CollisionIdBytes);
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

        return new NesWorldPackRuntimePlan(
            pack,
            serializedBytes.ToArray(),
            layout,
            serializedBytes[..WorldPackDescriptor.V1HeaderBytes],
            serializedBytes.AsSpan(checked((int)descriptor.CollisionProfilesOffset), collisionProfileLength).ToArray(),
            targetExpansionBytes,
            serializedBytes.AsSpan(checked((int)descriptor.DirectoryOffset), directoryLength).ToArray(),
            planes);
    }

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

internal readonly record struct NesRamRange(ushort Start, int Length)
{
    public int EndExclusive => Start + Length;
}

internal sealed record NesWorldPackRuntimeLayout(
    IReadOnlyList<NesRamRange> VisualSlots,
    IReadOnlyList<NesRamRange> CollisionSlots,
    IReadOnlyList<NesRamRange> EdgeSlots,
    int TotalBytes)
{
    private const int ChunkCells = 64;
    private const int EdgeBytes = 32 + 9;
    private const ushort StagingStart = 0x0400;
    private const int InternalRamEndExclusive = 0x0800;

    public static NesWorldPackRuntimeLayout Create(int visualIdBytes, int collisionIdBytes)
    {
        ValidateIdBytes(visualIdBytes, nameof(visualIdBytes));
        ValidateIdBytes(collisionIdBytes, nameof(collisionIdBytes));

        var cursor = (int)StagingStart;
        var visualSlots = CreatePair(visualIdBytes * ChunkCells, ref cursor);
        var collisionSlots = CreatePair(collisionIdBytes * ChunkCells, ref cursor);
        var edgeSlots = CreatePair(EdgeBytes, ref cursor);
        if (cursor > InternalRamEndExclusive)
        {
            throw new InvalidOperationException(
                $"NES WorldPack staging ends at ${cursor - 1:X4}, beyond internal RAM $07FF.");
        }

        return new NesWorldPackRuntimeLayout(
            visualSlots,
            collisionSlots,
            edgeSlots,
            cursor - StagingStart);
    }

    private static IReadOnlyList<NesRamRange> CreatePair(int length, ref int cursor)
    {
        var first = new NesRamRange(checked((ushort)cursor), length);
        cursor = checked(cursor + length);
        var second = new NesRamRange(checked((ushort)cursor), length);
        cursor = checked(cursor + length);
        return [first, second];
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

    public static void Emit(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesWorldPackPlacement? placement,
        NesWorldPackProbe? probe)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);

        EmitReadByte(builder, plan, placement);
        EmitValidation(builder, plan);
        EmitPlaneDecoders(builder, plan);
        EmitLookups(builder, plan);
        if (probe is not null)
        {
            EmitProbe(builder, probe);
        }
    }

    private static void EmitProbe(PrgBuilder builder, NesWorldPackProbe probe)
    {
        builder.Label(NesRomBuilder.WorldPackProbeLabel);
        SetProbeCoordinates(builder, probe);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualLookupLabel);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ProbeVisualStatus);
        SetProbeCoordinates(builder, probe);
        builder.CallSubroutine(NesRomBuilder.WorldPackCollisionLookupLabel);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ProbeCollisionStatus);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ProbeCompleted);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ProbeCollisionStatus);
        builder.Return();
    }

    private static void SetProbeCoordinates(PrgBuilder builder, NesWorldPackProbe probe)
    {
        builder.LoadAImmediate(probe.HardwareX & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareXLow);
        builder.LoadAImmediate(probe.HardwareX >> 8);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareXHigh);
        builder.LoadAImmediate(probe.HardwareY & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareYLow);
        builder.LoadAImmediate(probe.HardwareY >> 8);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareYHigh);
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
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationState);
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
                     NesWorldPackRuntimeAbi.SourceOffset0,
                     NesWorldPackRuntimeAbi.SourceOffset1,
                     NesWorldPackRuntimeAbi.SourceOffset2,
                     NesWorldPackRuntimeAbi.SourceOffset3,
                 })
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(address);
        }
        builder.LoadAImmediate(0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcHigh);

        builder.Label(loop);
        EmitOffsetEqualityCheck(builder, plan.Pack.Descriptor.ChunkDataOffset, complete, read);
        builder.Label(read);
        builder.CallSubroutine(NesRomBuilder.WorldPackReadByteLabel);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationByte);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.XorAbsolute(NesWorldPackRuntimeAbi.ValidationByte);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.LoadXImmediate(8);

        builder.Label(bitLoop);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.AndImmediate(1);
        builder.BranchRelative(0xF0, shiftOnly);
        builder.ShiftRightAbsolute(NesWorldPackRuntimeAbi.ValidationCrcHigh);
        builder.RotateRightAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.XorImmediate(0x01);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcHigh);
        builder.XorImmediate(0xA0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcHigh);
        builder.JumpAbsolute(shifted);
        builder.Label(shiftOnly);
        builder.ShiftRightAbsolute(NesWorldPackRuntimeAbi.ValidationCrcHigh);
        builder.RotateRightAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.Label(shifted);
        builder.DecrementX();
        builder.BranchRelative(0xD0, bitLoop);

        EmitIncrementSourceOffset(builder, incremented);
        builder.Label(incremented);
        builder.JumpAbsolute(loop);

        builder.Label(complete);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcLow);
        builder.CompareImmediate((byte)expected);
        builder.JumpIf(0xD0, malformed);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationCrcHigh);
        builder.CompareImmediate((byte)(expected >> 8));
        builder.JumpIf(0xD0, malformed);

        builder.Label(success);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationState);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(malformed);
        builder.LoadAImmediate(2);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationState);
        builder.LoadAImmediate((byte)NesWorldPackResult.Malformed);
        builder.Return();
    }

    private static void EmitPlaneDecoders(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        var common = builder.CreateLabel("worldpack_decode_common");
        var visualSlot1 = builder.CreateLabel("worldpack_visual_slot_1");
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
            NesWorldPackRuntimeAbi.VisualSlot0State,
            NesWorldPackRuntimeAbi.VisualSlot1State,
            visualSlot1,
            visualReady,
            common,
            bounds);
        EmitDecodeEntry(
            builder,
            NesRomBuilder.WorldPackCollisionDecodeLabel,
            planeKind: 1,
            plan.Layout.CollisionSlots,
            plan.Pack.Descriptor.CollisionIdBytes,
            NesWorldPackRuntimeAbi.CollisionSlot0State,
            NesWorldPackRuntimeAbi.CollisionSlot1State,
            collisionSlot1,
            collisionReady,
            common,
            bounds);

        builder.Label(common);
        builder.CallSubroutine(NesRomBuilder.WorldPackValidateLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        var validated = builder.CreateLabel("worldpack_decode_validated");
        builder.BranchRelative(0xF0, validated);
        builder.Return();
        builder.Label(validated);
        EmitChunkBoundsCheck(builder, plan.Pack.Chunks.Count, miss, bounds);

        SetSourceOffset(builder, plan.Pack.Descriptor.DirectoryOffset);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.ChunkIndexLow, NesWorldPackRuntimeAbi.WorkLow);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.ChunkIndexHigh, NesWorldPackRuntimeAbi.WorkHigh);
        builder.Label(directoryLoop);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.WorkLow);
        builder.OrAbsolute(NesWorldPackRuntimeAbi.WorkHigh);
        builder.BranchRelative(0xF0, directoryReady);
        AddToSourceOffset(builder, 20);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.WorkLow);
        builder.BranchRelative(0xD0, decrementLow);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.WorkHigh);
        builder.Label(decrementLow);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.WorkLow);
        builder.JumpAbsolute(directoryLoop);

        builder.Label(directoryReady);
        CopySourceOffset(builder, NesWorldPackRuntimeAbi.DirectoryEntryOffset0);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.PlaneKind);
        builder.BranchRelative(0xF0, descriptorVisual);
        AddToSourceOffset(builder, 8);
        builder.JumpAbsolute(descriptorFields);
        builder.Label(descriptorVisual);
        builder.Label(descriptorFields);
        for (var index = 0; index < 4; index++)
        {
            builder.CallSubroutine(readAdvance);
            builder.JumpIf(0xB0, malformed);
            builder.StoreAAbsolute((ushort)(NesWorldPackRuntimeAbi.PlaneOffset0 + index));
        }
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.CompareImmediate(0);
        builder.JumpIf(0xD0, malformed);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.CompareImmediate(0);
        builder.JumpIf(0xD0, malformed);

        CopyAbsolute(builder, NesWorldPackRuntimeAbi.DirectoryEntryOffset0, NesWorldPackRuntimeAbi.SourceOffset0, count: 4);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.PlaneKind);
        builder.BranchRelative(0xF0, codecVisual);
        AddToSourceOffset(builder, 19);
        builder.JumpAbsolute(codecReady);
        builder.Label(codecVisual);
        AddToSourceOffset(builder, 18);
        builder.Label(codecReady);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.Codec);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.PlaneOffset0, NesWorldPackRuntimeAbi.SourceOffset0, count: 4);

        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.Codec);
        builder.CompareImmediate((byte)WorldPackCodec.Raw);
        builder.BranchRelative(0xF0, raw);
        builder.CompareImmediate((byte)WorldPackCodec.ElementRle);
        builder.BranchRelative(0xF0, rle);
        builder.JumpAbsolute(malformed);

        builder.Label(raw);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.CompareAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.JumpIf(0xD0, malformed);
        builder.Label(rawLoop);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.BranchRelative(0xF0, rawComplete);
        builder.CallSubroutine(readStore);
        builder.JumpIf(0xB0, malformed);
        builder.JumpAbsolute(rawLoop);
        builder.Label(rawComplete);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.JumpIf(0xD0, malformed);
        builder.JumpAbsolute(success);

        builder.Label(rle);
        builder.Label(rlePacket);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.JumpIf(0xF0, decodeComplete);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.PushA();
        builder.AndImmediate(0x80);
        builder.BranchRelative(0xD0, run);
        builder.PullA();
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.PacketCount);
        builder.Label(literal);
        builder.Label(literalLoop);
        builder.CallSubroutine(readStore);
        builder.JumpIf(0xB0, malformed);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.IdBytes);
        builder.CompareImmediate(2);
        builder.BranchRelative(0xD0, literalSecondDone);
        builder.CallSubroutine(readStore);
        builder.JumpIf(0xB0, malformed);
        builder.Label(literalSecondDone);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.PacketCount);
        builder.BranchRelative(0xD0, literalLoop);
        builder.JumpAbsolute(rlePacket);

        builder.Label(run);
        builder.PullA();
        builder.AndImmediate(0x7F);
        builder.ClearCarry();
        builder.AddImmediate(3);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.PacketCount);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.TempIdLow);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.IdBytes);
        builder.CompareImmediate(2);
        builder.BranchRelative(0xD0, runValueReady);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.CallSubroutine(readAdvance);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.TempIdHigh);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.Label(runValueReady);
        builder.Label(runLoop);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.TempIdLow);
        builder.CallSubroutine(storeA);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.IdBytes);
        builder.CompareImmediate(2);
        builder.BranchRelative(0xD0, runSecondDone);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.JumpIf(0xF0, malformed);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.TempIdHigh);
        builder.CallSubroutine(storeA);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.Label(runSecondDone);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.PacketCount);
        builder.BranchRelative(0xD0, runLoop);
        builder.JumpAbsolute(rlePacket);

        builder.Label(decodeComplete);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.JumpIf(0xD0, malformed);
        builder.JumpAbsolute(success);

        builder.Label(readAdvance);
        builder.CallSubroutine(NesRomBuilder.WorldPackReadByteLabel);
        var readAdvanceDone = builder.CreateLabel("worldpack_read_advance_done");
        builder.BranchRelative(0xB0, readAdvanceDone);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ReadValue);
        EmitIncrementSourceOffset(builder, readAdvanceDone);
        builder.Label(readAdvanceDone);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ReadValue);
        builder.Return();

        builder.Label(readStore);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.BranchRelative(0xF0, readStoreError);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.BranchRelative(0xF0, readStoreError);
        builder.CallSubroutine(readAdvance);
        builder.BranchRelative(0xB0, readStoreError);
        builder.CallSubroutine(storeA);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.StoredRemaining);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.DecodedRemaining);
        builder.ClearCarry();
        builder.Return();
        builder.Label(readStoreError);
        builder.SetCarry();
        builder.Return();

        builder.Label(storeA);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidationByte);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.DestinationLow, 0x00E8, count: 2);
        builder.LoadYImmediate(0);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ValidationByte);
        builder.StoreAIndirectY(0xE8);
        var destinationIncremented = builder.CreateLabel("worldpack_destination_incremented");
        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.DestinationLow);
        builder.BranchRelative(0xD0, destinationIncremented);
        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.DestinationHigh);
        builder.Label(destinationIncremented);
        builder.Return();

        builder.Label(bounds);
        builder.LoadAImmediate((byte)NesWorldPackResult.BoundsError);
        builder.Return();
        builder.Label(miss);
        builder.LoadAImmediate((byte)NesWorldPackResult.Miss);
        builder.Return();
        builder.Label(malformed);
        builder.LoadAImmediate((byte)NesWorldPackResult.Malformed);
        builder.Return();
        builder.Label(success);
        StoreSelectedState(builder, 1);
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

        builder.Label(NesRomBuilder.WorldPackVisualLookupLabel);
        builder.CallSubroutine(coordinates);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.BranchRelative(0xF0, visualContinue);
        builder.Return();
        builder.Label(visualContinue);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SlotIndex);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualDecodeLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        var visualDecoded = builder.CreateLabel("worldpack_visual_lookup_decoded");
        builder.BranchRelative(0xF0, visualDecoded);
        builder.Return();
        builder.Label(visualDecoded);
        EmitLoadIdFromSlot(
            builder,
            plan.Layout.VisualSlots[0].Start,
            descriptor.VisualIdBytes,
            descriptor.VisualMetatileCount,
            malformed);
        EmitExpansionOffset(builder, descriptor.TargetExpansionsOffset, descriptor.MetatileWidth * descriptor.MetatileHeight, stride: 2);
        builder.CallSubroutine(ReadAdvanceLabel);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ResultTile);
        builder.CallSubroutine(ReadAdvanceLabel);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ResultMetadata);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(NesRomBuilder.WorldPackCollisionLookupLabel);
        builder.CallSubroutine(coordinates);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.BranchRelative(0xF0, collisionContinue);
        builder.Return();
        builder.Label(collisionContinue);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SlotIndex);
        builder.CallSubroutine(NesRomBuilder.WorldPackCollisionDecodeLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        var collisionDecoded = builder.CreateLabel("worldpack_collision_lookup_decoded");
        builder.BranchRelative(0xF0, collisionDecoded);
        builder.Return();
        builder.Label(collisionDecoded);
        EmitLoadIdFromSlot(
            builder,
            plan.Layout.CollisionSlots[0].Start,
            descriptor.CollisionIdBytes,
            descriptor.CollisionProfileCount,
            malformed);
        EmitExpansionOffset(builder, descriptor.CollisionProfilesOffset, descriptor.MetatileWidth * descriptor.MetatileHeight, stride: 1);
        builder.CallSubroutine(ReadAdvanceLabel);
        builder.JumpIf(0xB0, malformed);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ResultCollision);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(malformed);
        builder.LoadAImmediate((byte)NesWorldPackResult.Malformed);
        builder.Return();

        builder.Label(coordinates);
        EmitUnsigned16BoundsCheck(
            builder,
            NesWorldPackRuntimeAbi.HardwareXLow,
            descriptor.HardwareWidth,
            coordinateBounds);
        EmitUnsigned16BoundsCheck(
            builder,
            NesWorldPackRuntimeAbi.HardwareYLow,
            descriptor.HardwareHeight,
            coordinateBounds);
        EmitDivide16ByConstant(
            builder,
            NesWorldPackRuntimeAbi.HardwareXLow,
            NesWorldPackRuntimeAbi.MetatileXLow,
            NesWorldPackRuntimeAbi.SubcellX,
            descriptor.MetatileWidth);
        EmitDivide16ByConstant(
            builder,
            NesWorldPackRuntimeAbi.HardwareYLow,
            NesWorldPackRuntimeAbi.MetatileYLow,
            NesWorldPackRuntimeAbi.SubcellY,
            descriptor.MetatileHeight);
        EmitDivide16By8(
            builder,
            NesWorldPackRuntimeAbi.MetatileXLow,
            NesWorldPackRuntimeAbi.ChunkXLow,
            NesWorldPackRuntimeAbi.LocalX);
        EmitDivide16By8(
            builder,
            NesWorldPackRuntimeAbi.MetatileYLow,
            NesWorldPackRuntimeAbi.ChunkYLow,
            NesWorldPackRuntimeAbi.LocalY);

        CopyAbsolute(builder, NesWorldPackRuntimeAbi.ChunkXLow, NesWorldPackRuntimeAbi.MathResultLow, count: 2);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.ChunkYLow, NesWorldPackRuntimeAbi.MathCountLow, count: 2);
        EmitAddConstantForCount(builder, descriptor.ChunkColumns);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.MathResultLow, NesWorldPackRuntimeAbi.ChunkIndexLow, count: 2);

        var regularWidth = builder.CreateLabel("worldpack_lookup_regular_width");
        var widthReady = builder.CreateLabel("worldpack_lookup_width_ready");
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ChunkXHigh);
        builder.CompareImmediate((descriptor.ChunkColumns - 1) >> 8);
        builder.BranchRelative(0xD0, regularWidth);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ChunkXLow);
        builder.CompareImmediate((descriptor.ChunkColumns - 1) & 0xFF);
        builder.BranchRelative(0xD0, regularWidth);
        var sourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
        var lastWidth = sourceWidth - ((descriptor.ChunkColumns - 1) * 8);
        builder.LoadAImmediate(lastWidth);
        builder.JumpAbsolute(widthReady);
        builder.Label(regularWidth);
        builder.LoadAImmediate(8);
        builder.Label(widthReady);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ValidWidth);

        CopyAbsolute(builder, NesWorldPackRuntimeAbi.LocalX, NesWorldPackRuntimeAbi.MathResultLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.LocalY, NesWorldPackRuntimeAbi.MathCountLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathCountHigh);
        EmitAddAbsoluteForCount(builder, NesWorldPackRuntimeAbi.ValidWidth);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.MathResultLow, NesWorldPackRuntimeAbi.CellIndex);

        CopyAbsolute(builder, NesWorldPackRuntimeAbi.SubcellX, NesWorldPackRuntimeAbi.MathResultLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.SubcellY, NesWorldPackRuntimeAbi.MathCountLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathCountHigh);
        EmitAddConstantForCount(builder, descriptor.MetatileWidth);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.MathResultLow, NesWorldPackRuntimeAbi.SubcellIndexLow, count: 2);

        builder.Label(coordinateSuccess);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
        builder.Label(coordinateBounds);
        builder.LoadAImmediate((byte)NesWorldPackResult.BoundsError);
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

        CopyAbsolute(builder, sourceLow, NesWorldPackRuntimeAbi.MathValueLow, count: 2);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(quotientLow);
        builder.StoreAAbsolute((ushort)(quotientLow + 1));
        var loop = builder.CreateLabel("worldpack_divide_loop");
        var subtract = builder.CreateLabel("worldpack_divide_subtract");
        var incremented = builder.CreateLabel("worldpack_divide_incremented");
        var done = builder.CreateLabel("worldpack_divide_done");
        builder.Label(loop);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathValueHigh);
        builder.BranchRelative(0xD0, subtract);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathValueLow);
        builder.CompareImmediate(divisor);
        builder.BranchRelative(0x90, done);
        builder.Label(subtract);
        builder.SetCarry();
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathValueLow);
        builder.SubtractImmediate(divisor);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathValueLow);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathValueHigh);
        builder.SubtractImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathValueHigh);
        builder.IncrementAbsolute(quotientLow);
        builder.BranchRelative(0xD0, incremented);
        builder.IncrementAbsolute((ushort)(quotientLow + 1));
        builder.Label(incremented);
        builder.JumpAbsolute(loop);
        builder.Label(done);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.MathValueLow, remainder);
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
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathCountLow);
        builder.OrAbsolute(NesWorldPackRuntimeAbi.MathCountHigh);
        builder.BranchRelative(0xF0, done);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
        builder.AddImmediate(value & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        builder.AddImmediate((value >> 8) & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathCountLow);
        builder.BranchRelative(0xD0, decrementLow);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.MathCountHigh);
        builder.Label(decrementLow);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.MathCountLow);
        builder.JumpAbsolute(loop);
        builder.Label(done);
    }

    private static void EmitAddAbsoluteForCount(PrgBuilder builder, ushort valueAddress)
    {
        var loop = builder.CreateLabel("worldpack_add_count_loop");
        var done = builder.CreateLabel("worldpack_add_count_done");
        builder.Label(loop);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathCountLow);
        builder.BranchRelative(0xF0, done);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
        builder.AddAbsolute(valueAddress);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        builder.DecrementAbsolute(NesWorldPackRuntimeAbi.MathCountLow);
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
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.CellIndex);
        if (idBytes == 2)
        {
            builder.ShiftLeftA();
        }
        builder.TransferAToX();
        builder.LoadAAbsoluteX(slotStart);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.IdLow);
        if (idBytes == 2)
        {
            builder.LoadAAbsoluteX((ushort)(slotStart + 1));
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.IdHigh);
        }
        else
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.IdHigh);
        }

        var inBounds = builder.CreateLabel("worldpack_id_in_bounds");
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.IdHigh);
        builder.CompareImmediate((idCount >> 8) & 0xFF);
        builder.BranchRelative(0x90, inBounds);
        builder.JumpIf(0xD0, malformed);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.IdLow);
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
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.SubcellIndexLow, NesWorldPackRuntimeAbi.MathResultLow, count: 2);
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.IdLow, NesWorldPackRuntimeAbi.MathCountLow, count: 2);
        EmitAddConstantForCount(builder, metatileCells);
        if (stride == 2)
        {
            builder.ClearCarry();
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
            builder.AddAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
            builder.AddAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        }

        SetSourceOffset(builder, sectionOffset);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset0);
        builder.AddAbsolute(NesWorldPackRuntimeAbi.MathResultLow);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SourceOffset0);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset1);
        builder.AddAbsolute(NesWorldPackRuntimeAbi.MathResultHigh);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SourceOffset1);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset2);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SourceOffset2);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset3);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SourceOffset3);
    }

    private static void EmitDecodeEntry(
        PrgBuilder builder,
        string entryLabel,
        byte planeKind,
        IReadOnlyList<NesRamRange> slots,
        int idBytes,
        ushort state0,
        ushort state1,
        string slot1,
        string ready,
        string common,
        string bounds)
    {
        builder.Label(entryLabel);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SlotIndex);
        builder.CompareImmediate(2);
        builder.JumpIf(0xB0, bounds);
        builder.CompareImmediate(1);
        builder.BranchRelative(0xF0, slot1);
        SetDestination(builder, slots[0]);
        SetSelectedStateAddress(builder, state0);
        builder.JumpAbsolute(ready);
        builder.Label(slot1);
        SetDestination(builder, slots[1]);
        SetSelectedStateAddress(builder, state1);
        builder.Label(ready);
        StoreSelectedState(builder, 0);
        builder.LoadAImmediate(planeKind);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.PlaneKind);
        builder.LoadAImmediate(idBytes);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.IdBytes);
        builder.JumpAbsolute(common);
    }

    private static void SetDestination(PrgBuilder builder, NesRamRange slot)
    {
        builder.LoadAImmediate(slot.Start & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.DestinationLow);
        builder.LoadAImmediate(slot.Start >> 8);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.DestinationHigh);
    }

    private static void SetSelectedStateAddress(PrgBuilder builder, ushort address)
    {
        builder.LoadAImmediate(address & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SelectedStateAddressLow);
        builder.LoadAImmediate(address >> 8);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SelectedStateAddressHigh);
    }

    private static void StoreSelectedState(PrgBuilder builder, byte state)
    {
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.SelectedStateAddressLow, 0x00E8, count: 2);
        builder.LoadYImmediate(0);
        builder.LoadAImmediate(state);
        builder.StoreAIndirectY(0xE8);
    }

    private static void EmitChunkBoundsCheck(
        PrgBuilder builder,
        int chunkCount,
        string miss,
        string bounds)
    {
        var inBounds = builder.CreateLabel("worldpack_chunk_in_bounds");
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ChunkIndexHigh);
        builder.CompareImmediate((chunkCount >> 8) & 0xFF);
        builder.BranchRelative(0x90, inBounds);
        builder.JumpIf(0xD0, bounds);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ChunkIndexLow);
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
            builder.StoreAAbsolute((ushort)(NesWorldPackRuntimeAbi.SourceOffset0 + index));
        }
    }

    private static void AddToSourceOffset(PrgBuilder builder, int value)
    {
        builder.ClearCarry();
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset0);
        builder.AddImmediate(value);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SourceOffset0);
        for (var index = 1; index < 4; index++)
        {
            builder.LoadAAbsolute((ushort)(NesWorldPackRuntimeAbi.SourceOffset0 + index));
            builder.AddImmediate(0);
            builder.StoreAAbsolute((ushort)(NesWorldPackRuntimeAbi.SourceOffset0 + index));
        }
    }

    private static void CopySourceOffset(PrgBuilder builder, ushort destination) =>
        CopyAbsolute(builder, NesWorldPackRuntimeAbi.SourceOffset0, destination, count: 4);

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
            NesWorldPackRuntimeAbi.SourceOffset0,
            NesWorldPackRuntimeAbi.SourceOffset1,
            NesWorldPackRuntimeAbi.SourceOffset2,
            NesWorldPackRuntimeAbi.SourceOffset3,
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
                     NesWorldPackRuntimeAbi.SourceOffset0,
                     NesWorldPackRuntimeAbi.SourceOffset1,
                     NesWorldPackRuntimeAbi.SourceOffset2,
                     NesWorldPackRuntimeAbi.SourceOffset3,
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

    private static void EmitReadByte(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesWorldPackPlacement? placement)
    {
        const byte pointerLow = 0xE8;
        const byte pointerHigh = 0xE9;
        var inBounds = builder.CreateLabel("worldpack_read_in_bounds");
        var boundsError = builder.CreateLabel("worldpack_read_bounds_error");
        var bankTable = builder.CreateLabel("worldpack_r6_bank_table");

        builder.Label(NesRomBuilder.WorldPackReadByteLabel);
        if (placement is not null)
        {
            builder.LoadAAbsolute(NesRomBuilder.Mmc3R6BankShadowAddress);
            builder.PushA();
            for (var index = 0; index < 4; index++)
            {
                builder.LoadAAbsolute((ushort)(NesWorldPackRuntimeAbi.SourceOffset0 + index));
                builder.PushA();
            }
        }

        EmitOffsetBoundsCheck(builder, plan.Pack.Descriptor.PackLength, inBounds, boundsError);

        builder.Label(inBounds);
        if (placement is not null)
        {
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset1);
            for (var shift = 0; shift < 5; shift++)
            {
                builder.ShiftRightA();
            }
            builder.TransferAToX();
            builder.LdaAbsoluteX(bankTable);
            builder.CallSubroutine("mmc3_select_r6");
            RestoreSourceOffsetFromStack(builder);

            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset0);
            builder.StoreAZeroPage(pointerLow);
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SourceOffset1);
            builder.AndImmediate(0x1F);
            builder.OrImmediate(0x80);
            builder.StoreAZeroPage(pointerHigh);
        }
        else
        {
            builder.ClearCarry();
            builder.LoadAImmediateLabelLowByte(NesRomBuilder.WorldPackLabel);
            builder.AddAbsolute(NesWorldPackRuntimeAbi.SourceOffset0);
            builder.StoreAZeroPage(pointerLow);
            builder.LoadAImmediateLabelHighByte(NesRomBuilder.WorldPackLabel);
            builder.AddAbsolute(NesWorldPackRuntimeAbi.SourceOffset1);
            builder.StoreAZeroPage(pointerHigh);
        }

        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(pointerLow);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ReadValue);
        if (placement is not null)
        {
            builder.PullA();
            builder.CallSubroutine("mmc3_select_r6");
        }
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ReadValue);
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
            builder.Label(bankTable);
            builder.Emit(placement.Segments.Select(segment => checked((byte)segment.PhysicalBank)).ToArray());
        }
    }

    private static void RestoreSourceOffsetFromStack(PrgBuilder builder)
    {
        for (var index = 3; index >= 0; index--)
        {
            builder.PullA();
            builder.StoreAAbsolute((ushort)(NesWorldPackRuntimeAbi.SourceOffset0 + index));
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
            NesWorldPackRuntimeAbi.SourceOffset0,
            NesWorldPackRuntimeAbi.SourceOffset1,
            NesWorldPackRuntimeAbi.SourceOffset2,
            NesWorldPackRuntimeAbi.SourceOffset3,
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
