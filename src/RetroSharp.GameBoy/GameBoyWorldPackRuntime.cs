namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal enum GameBoyWorldPackResult : byte
{
    Success = 0,
    Miss = 1,
    BoundsError = 2,
    Malformed = 3,
}

internal static class GameBoyWorldPackRuntimeAbi
{
    internal const ushort CollisionQueryXLow = 0xC1EB;
    internal const ushort CollisionQueryXHigh = 0xC1EC;
    internal const ushort CollisionQueryYLow = 0xC1ED;
    internal const ushort CollisionQueryYHigh = 0xC1EE;
    internal const ushort CollisionSelectedSlot = 0xC1EF;
    internal const ushort CollisionCache0Valid = 0xC1FD;
    internal const ushort CollisionCache0ChunkLow = 0xC1FE;
    internal const ushort CollisionCache0ChunkHigh = 0xC1FF;
    internal const ushort CollisionCache1Valid = 0xC200;
    internal const ushort CollisionCache1ChunkLow = 0xC201;
    internal const ushort CollisionCache1ChunkHigh = 0xC202;
    internal const ushort CollisionReplacementNext = 0xC203;
    internal const ushort CollisionDecodeCountLow = 0xC204;
    internal const ushort CollisionDecodeCountHigh = 0xC205;
    internal const ushort CollisionCellValid = 0xC206;
    internal const ushort CollisionCellXLow = 0xC207;
    internal const ushort CollisionCellXHigh = 0xC208;
    internal const ushort CollisionCellYLow = 0xC209;
    internal const ushort CollisionCellYHigh = 0xC20A;
    internal const ushort CollisionCellResult = 0xC20B;
    internal const ushort GameplayTickCount = 0xC20C;
    internal const ushort CollisionPendingChunkLow = 0xC20D;
    internal const ushort CollisionPendingChunkHigh = 0xC20E;
    internal const ushort CollisionMemoHitCount = 0xC20F;
}

internal sealed record GameBoyWorldPackPlaneRuntimeDescriptor(
    uint Offset,
    byte StoredBytes,
    byte DecodedElements,
    byte IdBytes,
    ushort IdCount,
    WorldPackCodec Codec);

internal sealed record GameBoyWorldPackRuntimePlan(
    WorldPack Pack,
    byte[] SerializedBytes,
    GameBoyWorldPackRuntimeLayout Layout,
    byte[] HeaderBytes,
    byte[] CollisionProfileBytes,
    byte[] RuntimeDirectoryBytes)
{
    public static GameBoyWorldPackRuntimePlan Create(
        byte[] serializedBytes,
        bool enablePackedCameraCache = false,
        bool enableDiagonalVisualCache = false)
    {
        ArgumentNullException.ThrowIfNull(serializedBytes);
        var pack = WorldPackSerializer.Deserialize(serializedBytes);
        var descriptor = pack.Descriptor;
        if (descriptor.TargetCellStride != 1)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack v1 requires targetCellStride 1; received {descriptor.TargetCellStride}.");
        }

        if (descriptor.PackLength > 32u * 16u * 1024u)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack length {descriptor.PackLength} exceeds the current 32-bank MBC1 address envelope.");
        }

        var visualSlotCount = descriptor.VisualIdBytes == 1
            ? enableDiagonalVisualCache ? 6 : enablePackedCameraCache ? 3 : 2
            : 2;
        var supportsCollisionMemoTable = SupportsCollisionMemoTable(descriptor);
        var layout = GameBoyWorldPackRuntimeLayout.Create(
            descriptor.VisualIdBytes,
            descriptor.CollisionIdBytes,
            visualSlotCount,
            enableCollisionMemoTable: enablePackedCameraCache && supportsCollisionMemoTable);
        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        var profileLength = checked(descriptor.CollisionProfileCount * metatileCells);
        var runtimeDirectory = new List<byte>(checked(pack.Chunks.Count * 16));
        foreach (var chunk in pack.Chunks)
        {
            var visual = CreatePlane(
                chunk.Directory.VisualOffset,
                chunk.Directory.VisualStoredBytes,
                chunk.Directory.VisualDecodedBytes,
                descriptor.VisualIdBytes,
                descriptor.VisualMetatileCount,
                chunk.Directory.VisualCodec);
            var collision = CreatePlane(
                chunk.Directory.CollisionOffset,
                chunk.Directory.CollisionStoredBytes,
                chunk.Directory.CollisionDecodedBytes,
                descriptor.CollisionIdBytes,
                descriptor.CollisionProfileCount,
                chunk.Directory.CollisionCodec);
            AddRuntimePlane(runtimeDirectory, visual);
            AddRuntimePlane(runtimeDirectory, collision);
            runtimeDirectory.Add(chunk.Directory.ValidWidth);
            runtimeDirectory.Add(chunk.Directory.ValidHeight);
            runtimeDirectory.Add(0); // Reserved: keep entries at 16 bytes for shift-only addressing.
            runtimeDirectory.Add(0);
        }

        return new GameBoyWorldPackRuntimePlan(
            pack,
            serializedBytes.ToArray(),
            layout,
            serializedBytes[..WorldPackDescriptor.V1HeaderBytes],
            serializedBytes.AsSpan(checked((int)descriptor.CollisionProfilesOffset), profileLength).ToArray(),
            runtimeDirectory.ToArray());
    }

    private static bool SupportsCollisionMemoTable(WorldPackDescriptor descriptor)
    {
        // The reduced lookup below is deliberately tied to complete-stage geometry.
        // Every other v1 shape retains the general word-safe coordinate lowering.
        var sourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
        var lastChunkWidth = sourceWidth - ((descriptor.ChunkColumns - 1) * descriptor.ChunkWidth);
        return descriptor.MetatileWidth == 2
               && descriptor.MetatileHeight == 2
               && descriptor.CollisionIdBytes == 1
               && descriptor.ChunkColumns == 20
               && descriptor.HardwareHeight <= 64
               && descriptor.ChunkColumns * descriptor.ChunkRows <= byte.MaxValue
               && lastChunkWidth == 4;
    }

    private static void AddRuntimePlane(List<byte> target, GameBoyWorldPackPlaneRuntimeDescriptor plane)
    {
        target.Add((byte)plane.Offset);
        target.Add((byte)(plane.Offset >> 8));
        target.Add((byte)(plane.Offset >> 16));
        target.Add(plane.StoredBytes);
        target.Add(plane.DecodedElements);
        target.Add((byte)plane.Codec);
    }

    private static GameBoyWorldPackPlaneRuntimeDescriptor CreatePlane(
        uint offset,
        ushort storedBytes,
        ushort decodedBytes,
        int idBytes,
        int idCount,
        WorldPackCodec codec)
    {
        if (offset > 0x07FFFF)
        {
            throw new InvalidOperationException($"Game Boy WorldPack plane offset {offset} exceeds the current 19-bit MBC1 reader envelope.");
        }

        if (storedBytes > 128 || decodedBytes > 128 || decodedBytes % idBytes != 0)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack plane requires stored/decoded lengths within one 8x8 ID slot; stored={storedBytes}, decoded={decodedBytes}, idBytes={idBytes}.");
        }

        return new GameBoyWorldPackPlaneRuntimeDescriptor(
            offset,
            checked((byte)storedBytes),
            checked((byte)(decodedBytes / idBytes)),
            checked((byte)idBytes),
            checked((ushort)idCount),
            codec);
    }
}

internal static class GameBoyWorldPackRuntimeEmitter
{
    private sealed record DecoderLabels(
        string Raw,
        string Rle,
        string ReadSourceByte,
        string Success,
        string Malformed);

    private const ushort ScratchPacketCount = 0xC1F0;
    private const ushort ScratchXLow = 0xC1F1;
    private const ushort ScratchXHigh = 0xC1F2;
    private const ushort ScratchYLow = 0xC1F3;
    private const ushort ScratchYHigh = 0xC1F4;
    private const ushort ScratchCellIndex = 0xC1F5;
    private const ushort ScratchStoredRemaining = 0xC1F6;
    private const ushort ScratchSourceBank = 0xC1F7;
    private const ushort ScratchReadByte = 0xC1F8;
    private const ushort ScratchSubcellIndex = 0xC1F9;
    private const ushort ScratchSubcellIndexHigh = 0xC1FC;

    internal static void EmitInitializeState(GbBuilder builder, GameBoyWorldPackRuntimePlan plan)
    {
        builder.XorA();
        foreach (var address in new ushort[]
                 {
                     GameBoyWorldPackRuntimeAbi.CollisionQueryXLow,
                     GameBoyWorldPackRuntimeAbi.CollisionQueryXHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionQueryYLow,
                     GameBoyWorldPackRuntimeAbi.CollisionQueryYHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot,
                     GameBoyWorldPackRuntimeAbi.CollisionCache0Valid,
                     GameBoyWorldPackRuntimeAbi.CollisionCache0ChunkLow,
                     GameBoyWorldPackRuntimeAbi.CollisionCache0ChunkHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionCache1Valid,
                     GameBoyWorldPackRuntimeAbi.CollisionCache1ChunkLow,
                     GameBoyWorldPackRuntimeAbi.CollisionCache1ChunkHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionReplacementNext,
                     GameBoyWorldPackRuntimeAbi.CollisionDecodeCountLow,
                     GameBoyWorldPackRuntimeAbi.CollisionDecodeCountHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionCellValid,
                     GameBoyWorldPackRuntimeAbi.CollisionCellXLow,
                     GameBoyWorldPackRuntimeAbi.CollisionCellXHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionCellYLow,
                     GameBoyWorldPackRuntimeAbi.CollisionCellYHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionCellResult,
                     GameBoyWorldPackRuntimeAbi.GameplayTickCount,
                     GameBoyWorldPackRuntimeAbi.CollisionPendingChunkLow,
                     GameBoyWorldPackRuntimeAbi.CollisionPendingChunkHigh,
                     GameBoyWorldPackRuntimeAbi.CollisionMemoHitCount,
                 })
        {
            builder.StoreA(address);
        }

        if (plan.Layout.CollisionMemoTable is { } memoTable)
        {
            for (var entry = 0; entry < GameBoyWorldPackRuntimeLayout.CollisionMemoEntryCount; entry++)
            {
                builder.StoreA(checked((ushort)(memoTable.Start
                    + entry * GameBoyWorldPackRuntimeLayout.CollisionMemoEntryBytes
                    + 1)));
            }
        }
    }

    public static void Emit(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        bool enableStagedCamera,
        bool enableDiagonalVisualCache,
        bool enablePackedAudioService)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(layout);

        var descriptor = plan.Pack.Descriptor;
        var runtimeDirectory = builder.CreateLabel("worldpack_runtime_directory");
        var visualDecoders = CreateDecoderLabels(builder, "visual");
        var collisionDecoders = CreateDecoderLabels(builder, "collision");
        var collisionProfileBytes = EmitMetadataValidation(
            builder,
            plan,
            layout,
            enableStagedCamera);
        EmitVisualRuntime(
            builder,
            plan,
            layout,
            runtimeDirectory,
            visualDecoders,
            enableStagedCamera,
            enableDiagonalVisualCache);
        EmitCollisionRuntime(builder, plan, layout, collisionProfileBytes, runtimeDirectory, collisionDecoders, enableStagedCamera);
        if (enableStagedCamera)
        {
            GameBoyPackedCameraRuntimeEmitter.EmitWaitOutsideVBlankRoutine(builder);
            if (enablePackedAudioService)
            {
                GameBoyPackedCameraRuntimeEmitter.EmitObserveFrameWrapRoutine(builder);
            }

            GameBoyPackedCameraRuntimeEmitter.EmitWaitIfInVBlankRoutine(builder, enablePackedAudioService);
            EmitEdgePreparationRuntime(builder, plan, enablePackedAudioService);
        }
        builder.Label(runtimeDirectory);
        builder.Emit(plan.RuntimeDirectoryBytes);
    }

    private static void EmitEdgePreparationRuntime(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        bool enablePackedAudioService)
    {
        var selectSlot0 = builder.CreateLabel("worldpack_edge_select_slot_0");
        var selectSlot1 = builder.CreateLabel("worldpack_edge_select_slot_1");
        var prepare = builder.CreateLabel("worldpack_edge_prepare");
        var fullLookup = builder.CreateLabel("worldpack_edge_full_lookup");
        var rowLookup = builder.CreateLabel("worldpack_edge_row_lookup");
        var lookup = builder.CreateLabel("worldpack_edge_lookup");
        var inspectLookup = builder.CreateLabel("worldpack_edge_inspect_lookup");
        var validationFailed = builder.CreateLabel("worldpack_edge_validation_failed");
        var lookupFailed = builder.CreateLabel("worldpack_edge_lookup_failed");
        var failed = builder.CreateLabel("worldpack_edge_failed");
        var loop = builder.CreateLabel("worldpack_edge_prepare_loop");
        var success = builder.CreateLabel("worldpack_edge_prepare_success");
        var noSlot = builder.CreateLabel("worldpack_edge_no_slot");

        builder.Label(GameBoyRomBuilder.WorldPackPrepareEdgeLabel);
        EmitJumpIfSlotAvailable(builder, GameBoyPackedCameraRuntime.Slot0, selectSlot0);
        EmitJumpIfSlotAvailable(builder, GameBoyPackedCameraRuntime.Slot1, selectSlot1);
        builder.JumpAbsolute(noSlot);

        builder.Label(selectSlot0);
        EmitSelectEdgeSlot(builder, plan.Layout.EdgeSlots[0], GameBoyPackedCameraRuntime.Slot0, 0);
        builder.JumpAbsolute(prepare);

        builder.Label(selectSlot1);
        EmitSelectEdgeSlot(builder, plan.Layout.EdgeSlots[1], GameBoyPackedCameraRuntime.Slot1, 1);

        builder.Label(prepare);
        builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
        builder.PushAf();
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgePreparationBankSession);
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeReuseReady);
        EmitIncrementCounter(builder, GameBoyPackedCameraRuntime.RequestCount);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Requested);
        EmitWriteSelectedSlotMetadata(builder);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Preparing);
        EmitIncrementCounter(builder, GameBoyPackedCameraRuntime.PrepareCount);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.CompareImmediate((byte)GameBoyWorldPackResult.Success);
        builder.JumpAbsolute(0xC2, validationFailed);
        GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
            builder,
            GameBoyPackedCameraRuntime.DecodeWorkInCommit);
        builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.LoadAImmediate(19);
        var lengthReady = builder.CreateLabel("worldpack_edge_length_ready");
        builder.JumpAbsolute(0xC2, lengthReady);
        builder.LoadAImmediate(21);
        builder.Label(lengthReady);
        builder.StoreA(GameBoyPackedCameraRuntime.PayloadRemaining);
        EmitStoreSelectedSlotPayloadLength(builder);

        builder.Label(loop);
        if (enablePackedAudioService)
        {
            var skipAudioObservation = builder.CreateLabel("worldpack_edge_skip_audio_observation");
            builder.LoadA(GameBoyPackedCameraRuntime.PayloadRemaining);
            builder.AndImmediate(0x03);
            builder.JumpAbsolute(0xC2, skipAudioObservation);
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
            builder.Label(skipAudioObservation);
        }

        builder.LoadA(GameBoyPackedCameraRuntime.EdgeReuseReady);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, fullLookup);
        EmitTryAdvanceEdgeLookupState(builder, plan, fullLookup);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackVisualEdgeContinueLabel);
        builder.JumpAbsolute(inspectLookup);

        builder.Label(fullLookup);
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSameMetatile);
        builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xCA, rowLookup);

        builder.LoadA(GameBoyPackedCameraRuntime.CommitWorldEdgeLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.CommitWorldEdgeHigh);
        builder.LoadDFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorHigh);
        builder.LoadHFromA();
        builder.JumpAbsolute(lookup);

        builder.Label(rowLookup);
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorHigh);
        builder.LoadDFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.CommitWorldEdgeLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.CommitWorldEdgeHigh);
        builder.LoadHFromA();

        builder.Label(lookup);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackVisualEdgeLookupLabel);
        builder.Label(inspectLookup);
        builder.Emit(0xF5); // PUSH AF; status inspection must preserve the tile byte.
        builder.LoadAFromB();
        builder.CompareImmediate((byte)GameBoyWorldPackResult.Success);
        builder.JumpAbsolute(0xC2, lookupFailed);
        builder.Emit(0xF1); // POP AF
        builder.StoreA(GameBoyPackedCameraRuntime.TileScratch);
        builder.LoadA(GameBoyPackedCameraRuntime.DestinationLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.DestinationHigh);
        builder.LoadHFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.TileScratch);
        builder.StoreHlA();
        builder.IncrementHl();
        builder.LoadAFromL();
        builder.StoreA(GameBoyPackedCameraRuntime.DestinationLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyPackedCameraRuntime.DestinationHigh);

        builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        var incrementColumnIterator = builder.CreateLabel("worldpack_edge_increment_column_iterator");
        var iteratorIncremented = builder.CreateLabel("worldpack_edge_iterator_incremented");
        builder.JumpAbsolute(0xC2, incrementColumnIterator);
        EmitIncrementIteratorModulo(builder, plan.Pack.Descriptor.HardwareWidth);
        builder.JumpAbsolute(iteratorIncremented);
        builder.Label(incrementColumnIterator);
        EmitIncrementIteratorModulo(builder, plan.Pack.Descriptor.HardwareHeight);
        builder.Label(iteratorIncremented);

        builder.LoadA(GameBoyPackedCameraRuntime.PayloadRemaining);
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.PayloadRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, loop);
        builder.JumpAbsolute(success);

        builder.Label(lookupFailed);
        builder.Emit(0xF1); // POP AF
        builder.JumpAbsolute(failed);
        builder.Label(validationFailed);
        builder.Label(failed);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Released);
        EmitIncrementCounter(builder, GameBoyPackedCameraRuntime.ReleaseCount);
        EmitEndEdgePreparationBankSession(builder);
        builder.LoadAImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.Emit(0xC9); // RET

        builder.Label(success);
        if (enablePackedAudioService)
        {
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
        }

        EmitIncrementCounter(builder, GameBoyPackedCameraRuntime.ResidentCount);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Resident);
        EmitEndEdgePreparationBankSession(builder);
        builder.LoadA(GameBoyPackedCameraRuntime.SelectedSlot);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9); // RET

        builder.Label(noSlot);
        builder.LoadAImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Miss);
        builder.Emit(0xC9); // RET
    }

    private static void EmitTryAdvanceEdgeLookupState(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        string fullLookup)
    {
        var descriptor = plan.Pack.Descriptor;
        var advanceColumn = builder.CreateLabel("worldpack_edge_advance_column");
        var rowWithinMetatile = builder.CreateLabel("worldpack_edge_row_within_metatile");
        var rowWithinChunk = builder.CreateLabel("worldpack_edge_row_within_chunk");
        var columnWithinMetatile = builder.CreateLabel("worldpack_edge_column_within_metatile");
        var columnWithinChunk = builder.CreateLabel("worldpack_edge_column_within_chunk");
        var ready = builder.CreateLabel("worldpack_edge_advance_ready");

        builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xC2, advanceColumn);

        builder.LoadA(GameBoyPackedCameraRuntime.EdgeSubcellX);
        builder.AddAImmediate(1);
        builder.CompareImmediate(descriptor.MetatileWidth);
        builder.JumpAbsolute(0xDA, rowWithinMetatile);
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSubcellX);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeLocalX);
        builder.AddAImmediate(1);
        builder.LoadBFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeValidWidth);
        builder.CompareB();
        builder.JumpAbsolute(0xCA, fullLookup);
        builder.LoadAFromB();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeLocalX);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeCellIndex);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeCellIndex);
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSameMetatile);
        builder.JumpAbsolute(rowWithinChunk);

        builder.Label(rowWithinMetatile);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSubcellX);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSameMetatile);
        EmitAdvanceEdgeExpansionAddress(builder, 1);
        builder.JumpAbsolute(ready);
        builder.Label(rowWithinChunk);
        builder.JumpAbsolute(ready);

        builder.Label(advanceColumn);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeSubcellY);
        builder.AddAImmediate(1);
        builder.CompareImmediate(descriptor.MetatileHeight);
        builder.JumpAbsolute(0xDA, columnWithinMetatile);
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSubcellY);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeLocalY);
        builder.AddAImmediate(1);
        builder.LoadBFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeValidHeight);
        builder.CompareB();
        builder.JumpAbsolute(0xCA, fullLookup);
        builder.LoadAFromB();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeLocalY);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeCellIndex);
        builder.LoadBFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeValidWidth);
        builder.AddAFromB();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeCellIndex);
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSameMetatile);
        builder.JumpAbsolute(columnWithinChunk);

        builder.Label(columnWithinMetatile);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSubcellY);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeSameMetatile);
        EmitAdvanceEdgeExpansionAddress(builder, descriptor.MetatileWidth);
        builder.JumpAbsolute(ready);
        builder.Label(columnWithinChunk);
        builder.Label(ready);
    }

    private static void EmitAdvanceEdgeExpansionAddress(GbBuilder builder, int delta)
    {
        var done = builder.CreateLabel("worldpack_edge_expansion_advanced");
        var wrapBank = builder.CreateLabel("worldpack_edge_expansion_wrap_bank");
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeExpansionAddressLow);
        builder.AddAImmediate(delta);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeExpansionAddressLow);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeExpansionAddressHigh);
        builder.AdcAImmediate(0);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeExpansionAddressHigh);
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xCA, wrapBank);
        builder.JumpAbsolute(done);
        builder.Label(wrapBank);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeExpansionBank);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        builder.Emit(0x3C); // INC A
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeExpansionBank);
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder, instrumentPackedCameraCritical: true);
        builder.LoadAImmediate(0x40);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeExpansionAddressHigh);
        builder.Label(done);
    }

    private static void EmitEndEdgePreparationBankSession(GbBuilder builder)
    {
        var restored = builder.CreateLabel("worldpack_edge_bank_restored");
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgePreparationBankSession);
        builder.PopAf();
        builder.LoadBFromA();
        builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
        builder.CompareB();
        builder.JumpAbsolute(0xCA, restored);
        builder.LoadAFromB();
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder, instrumentPackedCameraCritical: true);
        builder.Label(restored);
    }

    private static void EmitJumpIfSlotAvailable(GbBuilder builder, ushort metadata, string available)
    {
        builder.LoadA(checked((ushort)(metadata + GameBoyPackedCameraRuntime.StateOffset)));
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Empty);
        builder.JumpAbsolute(0xCA, available);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Released);
        builder.JumpAbsolute(0xCA, available);
    }

    private static void EmitSelectEdgeSlot(
        GbBuilder builder,
        GameBoyWramRange payload,
        ushort metadata,
        byte slot)
    {
        builder.LoadAImmediate(slot);
        builder.StoreA(GameBoyPackedCameraRuntime.SelectedSlot);
        builder.LoadAImmediate(payload.Start & 0xFF);
        builder.StoreA(GameBoyPackedCameraRuntime.DestinationLow);
        builder.LoadAImmediate(payload.Start >> 8);
        builder.StoreA(GameBoyPackedCameraRuntime.DestinationHigh);
    }

    private static void EmitWriteSelectedSlotMetadata(GbBuilder builder)
    {
        var slot1 = builder.CreateLabel("worldpack_edge_write_slot_1_metadata");
        var done = builder.CreateLabel("worldpack_edge_write_metadata_done");
        builder.LoadA(GameBoyPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        EmitWriteSlotMetadata(builder, GameBoyPackedCameraRuntime.Slot0);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        EmitWriteSlotMetadata(builder, GameBoyPackedCameraRuntime.Slot1);
        builder.Label(done);
    }

    private static void EmitWriteSlotMetadata(GbBuilder builder, ushort metadata)
    {
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.CommitAxis, metadata + GameBoyPackedCameraRuntime.AxisOffset);
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.CommitDirection, metadata + GameBoyPackedCameraRuntime.DirectionOffset);
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.CommitWorldEdgeLow, metadata + GameBoyPackedCameraRuntime.WorldEdgeLowOffset);
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.CommitWorldEdgeHigh, metadata + GameBoyPackedCameraRuntime.WorldEdgeHighOffset);
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.CommitTarget, metadata + GameBoyPackedCameraRuntime.TargetOffset);
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.CommitTargetStart, metadata + GameBoyPackedCameraRuntime.TargetStartOffset);
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.IteratorLow, metadata + GameBoyPackedCameraRuntime.OrthogonalLowOffset);
        EmitCopyByte(builder, GameBoyPackedCameraRuntime.IteratorHigh, metadata + GameBoyPackedCameraRuntime.OrthogonalHighOffset);
    }

    private static void EmitStoreSelectedSlotPayloadLength(GbBuilder builder)
    {
        var slot1 = builder.CreateLabel("worldpack_edge_length_slot_1");
        var done = builder.CreateLabel("worldpack_edge_length_done");
        builder.LoadBFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadAFromB();
        builder.StoreA(GameBoyPackedCameraRuntime.Slot0 + GameBoyPackedCameraRuntime.PayloadLengthOffset);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        builder.LoadAFromB();
        builder.StoreA(GameBoyPackedCameraRuntime.Slot1 + GameBoyPackedCameraRuntime.PayloadLengthOffset);
        builder.Label(done);
    }

    private static void EmitSetSelectedSlotState(GbBuilder builder, byte state)
    {
        var slot1 = builder.CreateLabel("worldpack_edge_state_slot_1");
        var done = builder.CreateLabel("worldpack_edge_state_done");
        builder.LoadA(GameBoyPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadAImmediate(state);
        builder.StoreA(GameBoyPackedCameraRuntime.Slot0 + GameBoyPackedCameraRuntime.StateOffset);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        builder.LoadAImmediate(state);
        builder.StoreA(GameBoyPackedCameraRuntime.Slot1 + GameBoyPackedCameraRuntime.StateOffset);
        builder.Label(done);
    }

    private static void EmitIncrementCounter(GbBuilder builder, ushort address)
    {
        builder.LoadA(address);
        builder.AddAImmediate(1);
        builder.StoreA(address);
    }

    private static void EmitIncrementIteratorModulo(GbBuilder builder, int modulo)
    {
        var noCarry = builder.CreateLabel("worldpack_edge_iterator_no_carry");
        var done = builder.CreateLabel("worldpack_edge_iterator_done");
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorLow);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.IteratorLow);
        builder.JumpAbsolute(0xD2, noCarry);
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorHigh);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.IteratorHigh);
        builder.Label(noCarry);
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorHigh);
        builder.CompareImmediate((modulo >> 8) & 0xFF);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadA(GameBoyPackedCameraRuntime.IteratorLow);
        builder.CompareImmediate(modulo & 0xFF);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyPackedCameraRuntime.IteratorLow);
        builder.StoreA(GameBoyPackedCameraRuntime.IteratorHigh);
        builder.Label(done);
    }

    private static void EmitCopyByte(GbBuilder builder, ushort source, int target)
    {
        builder.LoadA(source);
        builder.StoreA(checked((ushort)target));
    }

    private static DecoderLabels CreateDecoderLabels(GbBuilder builder, string planeName) =>
        new(
            builder.CreateLabel($"worldpack_{planeName}_decode_raw"),
            builder.CreateLabel($"worldpack_{planeName}_decode_rle"),
            builder.CreateLabel($"worldpack_{planeName}_read_source_byte"),
            builder.CreateLabel($"worldpack_{planeName}_decode_success"),
            builder.CreateLabel($"worldpack_{planeName}_decode_malformed"));

    private static void EmitVisualRuntime(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        DecoderLabels decoders,
        bool enableStagedCamera,
        bool enableDiagonalVisualCache)
    {
        var descriptor = plan.Pack.Descriptor;
        EmitPlaneDecodeEntry(
            builder,
            GameBoyRomBuilder.WorldPackVisualDecodeLabel,
            plan,
            layout,
            planeOffset: 0,
            plan.Layout.VisualSlots,
            runtimeDirectory,
            decoders.Raw,
            decoders.Rle,
            enableStagedCamera);
        EmitVisualLookup(
            builder,
            plan,
            layout,
            runtimeDirectory,
            decoders.Raw,
            decoders.Rle,
            enableStagedCamera,
            enableDiagonalVisualCache);
        EmitRawDecoder(
            builder,
            descriptor.VisualIdBytes,
            descriptor.VisualMetatileCount,
            decoders.Raw,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack,
            enableStagedCamera);
        EmitRleDecoder(
            builder,
            descriptor.VisualIdBytes,
            descriptor.VisualMetatileCount,
            decoders.Rle,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack,
            enableStagedCamera);
        EmitReadSourceByte(builder, decoders.ReadSourceByte, layout.UsesBankedWorldPack, enableStagedCamera);
        EmitDecodeReturns(builder, decoders.Success, decoders.Malformed, layout.UsesBankedWorldPack, enableStagedCamera);
    }

    private static void EmitVisualLookup(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        string rawDecoder,
        string rleDecoder,
        bool enableStagedCamera,
        bool enableDiagonalVisualCache)
    {
        if (!enableStagedCamera)
        {
            EmitUnstagedVisualLookup(builder, plan, layout, runtimeDirectory, rawDecoder, rleDecoder);
            return;
        }

        var bounds = builder.CreateLabel("worldpack_visual_lookup_bounds");
        var returnStatus = builder.CreateLabel("worldpack_visual_lookup_return_status");
        var decodeChunk = builder.CreateLabel("worldpack_visual_lookup_decode_chunk");
        var decodeReady = builder.CreateLabel("worldpack_visual_lookup_decode_ready");
        var cacheUpdated = builder.CreateLabel("worldpack_visual_lookup_cache_updated");
        var chunkReady = builder.CreateLabel("worldpack_visual_lookup_chunk_ready");
        var touchCache = builder.CreateLabel("worldpack_visual_lookup_touch_cache");
        var cacheGroup = builder.CreateLabel("worldpack_visual_lookup_cache_group");
        var cacheGroupProtected = builder.CreateLabel("worldpack_visual_lookup_cache_group_protected");
        var recordCacheGroup = builder.CreateLabel("worldpack_visual_lookup_record_cache_group");
        var cacheChecks = Enumerable.Range(0, plan.Layout.VisualSlots.Count)
            .Select(index => builder.CreateLabel($"worldpack_visual_lookup_check_slot_{index}"))
            .ToArray();
        var cacheHits = Enumerable.Range(0, plan.Layout.VisualSlots.Count)
            .Select(index => builder.CreateLabel($"worldpack_visual_lookup_slot_{index}_hit"))
            .ToArray();
        var cacheChoices = Enumerable.Range(0, plan.Layout.VisualSlots.Count)
            .Select(index => builder.CreateLabel($"worldpack_visual_lookup_choose_slot_{index}"))
            .ToArray();
        var cacheUpdates = Enumerable.Range(0, plan.Layout.VisualSlots.Count)
            .Select(index => builder.CreateLabel($"worldpack_visual_lookup_update_slot_{index}"))
            .ToArray();
        var descriptor = plan.Pack.Descriptor;
        builder.Label(GameBoyRomBuilder.WorldPackVisualLookupLabel);
        builder.Emit(0xD5); // PUSH DE
        builder.PushHl();
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.PopHl();
        builder.Emit(0xD1); // POP DE
        builder.LoadBFromA();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        builder.Label(GameBoyRomBuilder.WorldPackVisualEdgeLookupLabel);
        EmitStoreLookupCoordinates(builder);
        EmitJumpIfWordOutside(builder, ScratchXLow, ScratchXHigh, 0, descriptor.HardwareWidth, bounds);
        EmitJumpIfWordOutside(builder, ScratchYLow, ScratchYHigh, 0, descriptor.HardwareHeight, bounds);
        EmitPrepareLookupState(
            builder,
            plan,
            enableStagedCamera,
            guardLookupState: false);
        builder.LoadA(ScratchXLow);
        builder.StoreA(GameBoyPackedCameraRuntime.PendingVisualChunkLow);
        builder.LoadA(ScratchXHigh);
        builder.StoreA(GameBoyPackedCameraRuntime.PendingVisualChunkHigh);
        for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            var next = slot + 1 < cacheChecks.Length ? cacheChecks[slot + 1] : decodeChunk;
            builder.Label(cacheChecks[slot]);
            builder.LoadA(VisualCacheValidAddress(slot));
            builder.CompareImmediate(1);
            builder.JumpAbsolute(0xC2, next);
            builder.LoadA(VisualCacheChunkLowAddress(slot));
            builder.LoadBFromA();
            builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkLow);
            builder.CompareB();
            builder.JumpAbsolute(0xC2, next);
            builder.LoadA(VisualCacheChunkHighAddress(slot));
            builder.LoadBFromA();
            builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkHigh);
            builder.CompareB();
            builder.JumpAbsolute(0xCA, cacheHits[slot]);
            builder.JumpAbsolute(next);
        }

        builder.Label(decodeChunk);
        GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
            builder,
            GameBoyPackedCameraRuntime.DecodeWorkInCommit);
        if (enableDiagonalVisualCache)
        {
            builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkLow);
            builder.LoadEFromA();
            builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkHigh);
            builder.LoadDFromA();
            builder.JumpAbsolute(0xCD, cacheGroup);
        }
        for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            builder.LoadA(VisualCacheValidAddress(slot));
            builder.CompareImmediate(1);
            builder.JumpAbsolute(0xC2, cacheChoices[slot]);
        }

        if (enableDiagonalVisualCache)
        {
            // Preserve chunks on the row/column still being prepared and the last
            // opposite-axis group. The stale same-axis group is the preferred victim;
            // replacing either protected group creates a decode cascade on traversal.
            for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
            {
                var rowAxis = builder.CreateLabel($"worldpack_visual_lookup_row_axis_slot_{slot}");
                var compareCurrent = builder.CreateLabel($"worldpack_visual_lookup_compare_current_slot_{slot}");
                var consider = builder.CreateLabel($"worldpack_visual_lookup_consider_slot_{slot}");
                var protectColumn = builder.CreateLabel($"worldpack_visual_lookup_protect_column_slot_{slot}");
                var protect = builder.CreateLabel($"worldpack_visual_lookup_protect_slot_{slot}");
                var next = builder.CreateLabel($"worldpack_visual_lookup_skip_protected_slot_{slot}");

                builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
                builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
                builder.JumpAbsolute(0xCA, rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: false);
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupLow);
                builder.JumpAbsolute(compareCurrent);

                builder.Label(rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: true);
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingRowGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingRowGroupLow);

                builder.Label(compareCurrent);
                builder.LoadBFromA();
                builder.LoadAFromD();
                builder.Emit(0xB9); // CP C
                builder.JumpAbsolute(0xC2, consider);
                builder.LoadAFromE();
                builder.CompareB();
                builder.JumpAbsolute(0xCA, next);

                builder.Label(consider);
                builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
                builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
                builder.JumpAbsolute(0xCA, protectColumn);
                EmitLoadVisualCacheGroup(builder, slot, row: true);
                builder.JumpAbsolute(protect);
                builder.Label(protectColumn);
                EmitLoadVisualCacheGroup(builder, slot, row: false);
                builder.Label(protect);
                builder.JumpAbsolute(0xCD, cacheGroupProtected);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xCA, cacheChoices[slot]);
                builder.Label(next);
            }

            // When the current row/column plus the last opposite-axis group occupy
            // all six slots, keep the current traversal intact and evict one member
            // of the opposite group. That produces one future miss instead of a
            // same-edge decode cascade.
            for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
            {
                var rowAxis = builder.CreateLabel($"worldpack_visual_lookup_fallback_row_axis_slot_{slot}");
                var compare = builder.CreateLabel($"worldpack_visual_lookup_fallback_compare_slot_{slot}");
                builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
                builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
                builder.JumpAbsolute(0xCA, rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: false);
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupLow);
                builder.JumpAbsolute(compare);
                builder.Label(rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: true);
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingRowGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingRowGroupLow);
                builder.Label(compare);
                builder.LoadBFromA();
                builder.LoadAFromD();
                builder.Emit(0xB9); // CP C
                builder.JumpAbsolute(0xC2, cacheChoices[slot]);
                builder.LoadAFromE();
                builder.CompareB();
                builder.JumpAbsolute(0xC2, cacheChoices[slot]);
            }
        }

        if (enableDiagonalVisualCache)
        {
            for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
            {
                builder.LoadA(VisualCacheAgeAddress(slot));
                builder.CompareImmediate(plan.Layout.VisualSlots.Count - 1);
                builder.JumpAbsolute(0xCA, cacheChoices[slot]);
            }
        }
        else
        {
            builder.LoadA(GameBoyPackedCameraRuntime.VisualReplacementNext);
            for (var slot = 1; slot < plan.Layout.VisualSlots.Count; slot++)
            {
                builder.CompareImmediate(slot);
                builder.JumpAbsolute(0xCA, cacheChoices[slot]);
            }
        }

        builder.JumpAbsolute(cacheChoices[0]);
        for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            builder.Label(cacheChoices[slot]);
            builder.LoadAImmediate(slot);
            builder.StoreA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
            builder.LoadAImmediate((slot + 1) % plan.Layout.VisualSlots.Count);
            builder.StoreA(GameBoyPackedCameraRuntime.VisualReplacementNext);
            builder.LoadDe(plan.Layout.VisualSlots[slot].Start);
            builder.JumpAbsolute(decodeReady);
        }

        builder.Label(decodeReady);
        builder.Emit(0xD5); // PUSH DE; source preparation uses DE.
        builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkLow);
        builder.StoreA(ScratchXLow);
        builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkHigh);
        builder.StoreA(ScratchXHigh);
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 0, enableStagedCamera);
        builder.Emit(0xD1); // POP DE
        EmitSetValidationOnly(builder, validationOnly: false);
        EmitCallSelectedDecoder(builder, rawDecoder, rleDecoder, returnStatus);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
        for (var slot = 1; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            builder.CompareImmediate(slot);
            builder.JumpAbsolute(0xCA, cacheUpdates[slot]);
        }

        builder.JumpAbsolute(cacheUpdates[0]);
        for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            builder.Label(cacheUpdates[slot]);
            builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkLow);
            builder.StoreA(VisualCacheChunkLowAddress(slot));
            builder.LoadA(GameBoyPackedCameraRuntime.PendingVisualChunkHigh);
            builder.StoreA(VisualCacheChunkHighAddress(slot));
            builder.LoadAImmediate(1);
            builder.StoreA(VisualCacheValidAddress(slot));
            if (enableDiagonalVisualCache)
            {
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingRowGroupLow);
                builder.StoreA(VisualCacheRowGroupLowAddress(slot));
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingRowGroupHigh);
                builder.StoreA(VisualCacheRowGroupHighAddress(slot));
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupLow);
                builder.StoreA(VisualCacheColumnGroupLowAddress(slot));
                builder.LoadA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupHigh);
                builder.StoreA(VisualCacheColumnGroupHighAddress(slot));
            }
            if (enableDiagonalVisualCache)
            {
                builder.JumpAbsolute(0xCD, touchCache);
            }
            builder.JumpAbsolute(cacheUpdated);
        }

        builder.Label(cacheUpdated);
        builder.JumpAbsolute(chunkReady);

        for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            builder.Label(cacheHits[slot]);
            builder.LoadAImmediate(slot);
            builder.StoreA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
            builder.JumpAbsolute(chunkReady);
        }

        builder.Label(chunkReady);
        if (enableDiagonalVisualCache)
        {
            builder.JumpAbsolute(0xCD, recordCacheGroup);
        }
        var skipEdgeReuse = builder.CreateLabel("worldpack_visual_skip_edge_reuse");
        builder.LoadA(GameBoyPackedCameraRuntime.EdgePreparationBankSession);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, skipEdgeReuse);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeVisualSlot);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeReuseReady);
        builder.Label(skipEdgeReuse);
        var standardExpansion = builder.CreateLabel("worldpack_visual_standard_expansion");
        var expansionDone = builder.CreateLabel("worldpack_visual_expansion_done");
        var edgeExpansionMalformed = builder.CreateLabel("worldpack_visual_edge_expansion_malformed");
        builder.LoadA(GameBoyPackedCameraRuntime.EdgePreparationBankSession);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, standardExpansion);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackVisualEdgeExpansionLabel);
        builder.StoreA(ScratchReadByte);
        builder.LoadAFromB();
        builder.CompareImmediate((byte)GameBoyWorldPackResult.Success);
        builder.JumpAbsolute(0xC2, returnStatus);
        builder.LoadA(ScratchReadByte);
        builder.JumpAbsolute(expansionDone);
        builder.Label(standardExpansion);
        EmitVisualExpansionLookup(builder, plan, layout, enableStagedCamera, edgeExpansionMalformed);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Label(expansionDone);
        builder.Emit(0xC9);
        builder.Label(bounds);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
        builder.Emit(0xC9);
        builder.Label(returnStatus);
        builder.LoadAImmediate(0);
        builder.Emit(0xC9);

        builder.Label(GameBoyRomBuilder.WorldPackVisualEdgeExpansionLabel);
        EmitEdgeVisualExpansionLookup(builder, plan, layout, edgeExpansionMalformed);
        builder.Emit(0xC9);
        builder.Label(edgeExpansionMalformed);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);

        builder.Label(GameBoyRomBuilder.WorldPackVisualEdgeContinueLabel);
        var newMetatile = builder.CreateLabel("worldpack_visual_edge_new_metatile");
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeSameMetatile);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, newMetatile);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeExpansionAddressLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeExpansionAddressHigh);
        builder.LoadHFromA();
        builder.LoadAFromHl();
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
        builder.Label(newMetatile);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeCellIndex);
        builder.StoreA(ScratchCellIndex);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeSubcellY);
        builder.LoadCFromA();
        builder.Emit(0x06, 0x00); // LD B,0
        EmitMultiplyBcByConstantToHl(builder, descriptor.MetatileWidth);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeSubcellX);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(ScratchSubcellIndex);
        builder.LoadAFromH();
        builder.StoreA(ScratchSubcellIndexHigh);
        builder.LoadA(GameBoyPackedCameraRuntime.EdgeVisualSlot);
        builder.StoreA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackVisualEdgeExpansionLabel);
        builder.Emit(0xC9);

        if (enableDiagonalVisualCache)
        {
            builder.Label(touchCache);
            EmitTouchSelectedVisualCacheSlot(builder, plan.Layout.VisualSlots.Count);

            builder.Label(cacheGroup);
            EmitVisualCacheEdgeGroup(builder, descriptor.ChunkColumns);

            builder.Label(cacheGroupProtected);
            EmitIsVisualCacheGroupProtected(builder);

            builder.Label(recordCacheGroup);
            EmitRecordSelectedVisualCacheGroup(builder);
        }
    }

    private static void EmitEdgeVisualExpansionLookup(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string malformed)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(ScratchCellIndex);
        if (descriptor.VisualIdBytes == 2)
        {
            builder.AddAFromA();
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        EmitLoadSelectedVisualSlotAddress(builder, plan.Layout.VisualSlots, "worldpack_edge_expansion");
        builder.AddHlDe();
        builder.Emit(0x4E); // LD C,(HL)
        if (descriptor.VisualIdBytes == 2)
        {
            builder.IncrementHl();
            builder.Emit(0x46); // LD B,(HL)
        }
        else
        {
            builder.Emit(0x06, 0x00); // LD B,0
        }

        EmitJumpIfIdOutsideRange(builder, descriptor.VisualIdBytes, descriptor.VisualMetatileCount, malformed);

        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        EmitMultiplyBcByConstantToHl(builder, metatileCells);
        builder.LoadA(ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(ScratchSubcellIndexHigh);
        builder.LoadDFromA();
        builder.AddHlDe();

        if (layout.WorldPackPlacement is { } placement)
        {
            var expansionLength = checked(descriptor.VisualMetatileCount * metatileCells);
            var first = placement.TranslateOffset(checked((int)descriptor.TargetExpansionsOffset));
            var last = placement.TranslateOffset(checked((int)descriptor.TargetExpansionsOffset + expansionLength - 1));
            if (first.Bank != last.Bank)
            {
                EmitVisualExpansionLookup(builder, plan, layout, enableStagedCamera: true, malformed);
                return;
            }

            builder.LoadDe(first.Address);
            builder.AddHlDe();
            var bankReady = builder.CreateLabel("worldpack_edge_expansion_bank_ready");
            builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
            builder.CompareImmediate(first.Bank);
            builder.JumpAbsolute(0xCA, bankReady);
            builder.LoadAImmediate(first.Bank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, instrumentPackedCameraCritical: true);
            builder.Label(bankReady);
            builder.LoadAImmediate(first.Bank);
            builder.StoreA(ScratchSourceBank);
            EmitRememberEdgeExpansionAddress(builder, banked: true);
            builder.LoadAFromHl();
            builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
            return;
        }

        builder.LoadDe(checked((ushort)descriptor.TargetExpansionsOffset));
        builder.AddHlDe();
        builder.LoadDe(GameBoyRomBuilder.WorldPackLabel);
        builder.AddHlDe();
        EmitRememberEdgeExpansionAddress(builder, banked: false);
        builder.LoadAFromHl();
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
    }

    private static void EmitJumpIfIdOutsideRange(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string malformed)
    {
        if (idBytes == 1)
        {
            if (idCount < 256)
            {
                builder.LoadAFromC();
                builder.CompareImmediate(idCount);
                builder.JumpAbsolute(0xD2, malformed);
            }

            return;
        }

        var highIsLower = builder.CreateLabel("worldpack_edge_id_high_lower");
        var highMatches = builder.CreateLabel("worldpack_edge_id_high_matches");
        builder.LoadAFromB();
        builder.CompareImmediate((idCount >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, highIsLower);
        builder.JumpAbsolute(0xCA, highMatches);
        builder.JumpAbsolute(malformed);
        builder.Label(highMatches);
        builder.LoadAFromC();
        builder.CompareImmediate(idCount & 0xFF);
        builder.JumpAbsolute(0xD2, malformed);
        builder.Label(highIsLower);
    }

    private static void EmitUnstagedVisualLookup(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        string rawDecoder,
        string rleDecoder)
    {
        var bounds = builder.CreateLabel("worldpack_visual_lookup_bounds");
        var returnStatus = builder.CreateLabel("worldpack_visual_lookup_return_status");
        var malformedExpansion = builder.CreateLabel("worldpack_visual_lookup_malformed_expansion");
        var descriptor = plan.Pack.Descriptor;
        builder.Label(GameBoyRomBuilder.WorldPackVisualLookupLabel);
        builder.Emit(0xD5); // PUSH DE
        builder.PushHl();
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.PopHl();
        builder.Emit(0xD1); // POP DE
        builder.LoadBFromA();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        EmitStoreLookupCoordinates(builder);
        EmitJumpIfWordOutside(builder, ScratchXLow, ScratchXHigh, 0, descriptor.HardwareWidth, bounds);
        EmitJumpIfWordOutside(builder, ScratchYLow, ScratchYHigh, 0, descriptor.HardwareHeight, bounds);
        EmitPrepareLookupState(builder, plan, enableStagedCamera: false, guardLookupState: false);
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 0, enableStagedCamera: false);
        builder.LoadDe(plan.Layout.VisualSlots[0].Start);
        EmitSetValidationOnly(builder, validationOnly: false);
        EmitCallSelectedDecoder(builder, rawDecoder, rleDecoder, returnStatus);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        EmitVisualExpansionLookup(builder, plan, layout, enableStagedCamera: false, malformedExpansion);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
        builder.Label(bounds);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
        builder.Emit(0xC9);
        builder.Label(returnStatus);
        builder.LoadAImmediate(0);
        builder.Emit(0xC9);
        builder.Label(malformedExpansion);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);
    }

    private static void EmitVisualExpansionLookup(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        bool enableStagedCamera,
        string malformed)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(ScratchCellIndex);
        if (descriptor.VisualIdBytes == 2)
        {
            builder.AddAFromA();
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        if (enableStagedCamera)
        {
            EmitLoadSelectedVisualSlotAddress(builder, plan.Layout.VisualSlots, "worldpack_visual_expansion");
        }
        else
        {
            builder.LoadHl(plan.Layout.VisualSlots[0].Start);
        }
        builder.AddHlDe();
        builder.Emit(0x4E); // LD C,(HL)
        if (descriptor.VisualIdBytes == 2)
        {
            builder.IncrementHl();
            builder.Emit(0x46); // LD B,(HL)
        }
        else
        {
            builder.Emit(0x06, 0x00); // LD B,0
        }

        EmitJumpIfIdOutsideRange(builder, descriptor.VisualIdBytes, descriptor.VisualMetatileCount, malformed);

        builder.LoadHl(0);
        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        builder.XorA();
        builder.StoreA(ScratchYLow); // high byte of the 24-bit pack-relative expansion offset
        builder.LoadDe(checked((ushort)metatileCells));
        var multiplyLoop = builder.CreateLabel("worldpack_visual_expansion_multiply");
        var multiplyDone = builder.CreateLabel("worldpack_visual_expansion_multiply_done");
        builder.Label(multiplyLoop);
        builder.LoadAFromD();
        builder.Emit(0xB3); // OR E
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, multiplyDone);
        builder.AddHlBc();
        EmitIncrementScratchOnCarry(builder, ScratchYLow);
        builder.Emit(0x1B); // DEC DE
        builder.JumpAbsolute(multiplyLoop);
        builder.Label(multiplyDone);

        builder.LoadDe((ushort)(descriptor.TargetExpansionsOffset & 0xFFFF));
        builder.AddHlDe();
        EmitIncrementScratchOnCarry(builder, ScratchYLow);
        builder.LoadA(ScratchYLow);
        builder.AddAImmediate((int)((descriptor.TargetExpansionsOffset >> 16) & 0xFF));
        builder.StoreA(ScratchYLow);
        builder.LoadA(ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(ScratchSubcellIndexHigh);
        builder.LoadDFromA();
        builder.AddHlDe();
        EmitIncrementScratchOnCarry(builder, ScratchYLow);

        if (layout.WorldPackPlacement is { } placement)
        {
            builder.LoadDe(checked((ushort)(placement.BaseAddress - 0x4000)));
            builder.AddHlDe();
            EmitIncrementScratchOnCarry(builder, ScratchYLow);
            var bankSession = builder.CreateLabel("worldpack_expansion_bank_session");
            var bankReady = builder.CreateLabel("worldpack_expansion_bank_ready");
            var valueReady = builder.CreateLabel("worldpack_expansion_value_ready");
            builder.LoadA(GameBoyPackedCameraRuntime.EdgePreparationBankSession);
            builder.CompareImmediate(1);
            builder.JumpAbsolute(0xCA, bankSession);
            builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
            builder.PushAf();
            builder.Label(bankSession);
            builder.LoadA(ScratchYLow);
            builder.AddAFromA();
            builder.AddAFromA();
            builder.LoadBFromA();
            builder.LoadAFromH();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.AddAFromB();
            builder.AddAImmediate(placement.BaseBank);
            builder.StoreA(ScratchSourceBank);
            builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
            builder.LoadBFromA();
            builder.LoadA(ScratchSourceBank);
            builder.CompareB();
            builder.JumpAbsolute(0xCA, bankReady);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
            builder.Label(bankReady);
            builder.LoadAFromH();
            builder.AndImmediate(0x3F);
            builder.OrImmediate(0x40);
            builder.LoadHFromA();
            EmitRememberEdgeExpansionAddress(builder, banked: true);
            builder.LoadAFromHl();
            builder.StoreA(ScratchReadByte);
            builder.LoadA(GameBoyPackedCameraRuntime.EdgePreparationBankSession);
            builder.CompareImmediate(1);
            builder.JumpAbsolute(0xCA, valueReady);
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
            builder.Label(valueReady);
            builder.LoadA(ScratchReadByte);
        }
        else
        {
            builder.LoadDe(GameBoyRomBuilder.WorldPackLabel);
            builder.AddHlDe();
            EmitRememberEdgeExpansionAddress(builder, banked: false);
            builder.LoadAFromHl();
        }
    }

    private static void EmitLoadSelectedVisualSlotAddress(
        GbBuilder builder,
        IReadOnlyList<GameBoyWramRange> slots,
        string labelPrefix)
    {
        var selected = Enumerable.Range(1, slots.Count - 1)
            .Select(index => builder.CreateLabel($"{labelPrefix}_slot_{index}"))
            .ToArray();
        var ready = builder.CreateLabel($"{labelPrefix}_slot_ready");
        builder.LoadA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
        for (var slot = 1; slot < slots.Count; slot++)
        {
            builder.CompareImmediate(slot);
            builder.JumpAbsolute(0xCA, selected[slot - 1]);
        }

        builder.LoadHl(slots[0].Start);
        builder.JumpAbsolute(ready);
        for (var slot = 1; slot < slots.Count; slot++)
        {
            builder.Label(selected[slot - 1]);
            builder.LoadHl(slots[slot].Start);
            builder.JumpAbsolute(ready);
        }

        builder.Label(ready);
    }

    private static ushort VisualCacheValidAddress(int slot) => slot switch
    {
        0 => GameBoyPackedCameraRuntime.VisualCacheValid,
        1 => GameBoyPackedCameraRuntime.VisualCache1Valid,
        2 => GameBoyPackedCameraRuntime.VisualCache2Valid,
        3 => GameBoyPackedCameraRuntime.VisualCache3Valid,
        4 => GameBoyPackedCameraRuntime.VisualCache4Valid,
        5 => GameBoyPackedCameraRuntime.VisualCache5Valid,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheChunkLowAddress(int slot) => slot switch
    {
        0 => GameBoyPackedCameraRuntime.VisualCacheChunkLow,
        1 => GameBoyPackedCameraRuntime.VisualCache1ChunkLow,
        2 => GameBoyPackedCameraRuntime.VisualCache2ChunkLow,
        3 => GameBoyPackedCameraRuntime.VisualCache3ChunkLow,
        4 => GameBoyPackedCameraRuntime.VisualCache4ChunkLow,
        5 => GameBoyPackedCameraRuntime.VisualCache5ChunkLow,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheChunkHighAddress(int slot) => slot switch
    {
        0 => GameBoyPackedCameraRuntime.VisualCacheChunkHigh,
        1 => GameBoyPackedCameraRuntime.VisualCache1ChunkHigh,
        2 => GameBoyPackedCameraRuntime.VisualCache2ChunkHigh,
        3 => GameBoyPackedCameraRuntime.VisualCache3ChunkHigh,
        4 => GameBoyPackedCameraRuntime.VisualCache4ChunkHigh,
        5 => GameBoyPackedCameraRuntime.VisualCache5ChunkHigh,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheAgeAddress(int slot) => slot switch
    {
        0 => GameBoyPackedCameraRuntime.VisualCache0Age,
        1 => GameBoyPackedCameraRuntime.VisualCache1Age,
        2 => GameBoyPackedCameraRuntime.VisualCache2Age,
        3 => GameBoyPackedCameraRuntime.VisualCache3Age,
        4 => GameBoyPackedCameraRuntime.VisualCache4Age,
        5 => GameBoyPackedCameraRuntime.VisualCache5Age,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheRowGroupLowAddress(int slot) =>
        checked((ushort)(GameBoyPackedCameraRuntime.VisualCacheRowGroupLowStart + slot));

    private static ushort VisualCacheRowGroupHighAddress(int slot) =>
        checked((ushort)(GameBoyPackedCameraRuntime.VisualCacheRowGroupHighStart + slot));

    private static ushort VisualCacheColumnGroupLowAddress(int slot) =>
        checked((ushort)(GameBoyPackedCameraRuntime.VisualCacheColumnGroupLowStart + slot));

    private static ushort VisualCacheColumnGroupHighAddress(int slot) =>
        checked((ushort)(GameBoyPackedCameraRuntime.VisualCacheColumnGroupHighStart + slot));

    private static void EmitLoadVisualCacheGroup(GbBuilder builder, int slot, bool row)
    {
        builder.LoadA(row ? VisualCacheRowGroupLowAddress(slot) : VisualCacheColumnGroupLowAddress(slot));
        builder.LoadEFromA();
        builder.LoadA(row ? VisualCacheRowGroupHighAddress(slot) : VisualCacheColumnGroupHighAddress(slot));
        builder.LoadDFromA();
    }

    private static void EmitTouchSelectedVisualCacheSlot(GbBuilder builder, int slotCount)
    {
        var maximumAge = checked((byte)(slotCount - 1));
        for (var slot = 0; slot < slotCount; slot++)
        {
            var skip = builder.CreateLabel($"worldpack_visual_touch_skip_slot_{slot}");
            builder.LoadA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
            builder.CompareImmediate(slot);
            builder.JumpAbsolute(0xCA, skip);
            builder.LoadA(VisualCacheAgeAddress(slot));
            builder.CompareImmediate(maximumAge);
            builder.JumpAbsolute(0xCA, skip);
            builder.Emit(0x3C); // INC A
            builder.StoreA(VisualCacheAgeAddress(slot));
            builder.Label(skip);
        }

        var selectedSlots = Enumerable.Range(1, slotCount - 1)
            .Select(index => builder.CreateLabel($"worldpack_visual_touch_selected_slot_{index}"))
            .ToArray();
        builder.LoadA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
        for (var slot = 1; slot < slotCount; slot++)
        {
            builder.CompareImmediate(slot);
            builder.JumpAbsolute(0xCA, selectedSlots[slot - 1]);
        }

        builder.XorA();
        builder.StoreA(VisualCacheAgeAddress(0));
        builder.Emit(0xC9);
        for (var slot = 1; slot < slotCount; slot++)
        {
            builder.Label(selectedSlots[slot - 1]);
            builder.XorA();
            builder.StoreA(VisualCacheAgeAddress(slot));
            builder.Emit(0xC9);
        }
    }

    private static void EmitVisualCacheEdgeGroup(GbBuilder builder, int chunkColumns)
    {
        EmitDivideDeByConstant(builder, chunkColumns, ScratchPacketCount);
        builder.LoadAFromE();
        builder.StoreA(GameBoyPackedCameraRuntime.VisualPendingRowGroupLow);
        builder.LoadAFromD();
        builder.StoreA(GameBoyPackedCameraRuntime.VisualPendingRowGroupHigh);
        builder.LoadA(ScratchPacketCount);
        builder.StoreA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupLow);
        builder.XorA();
        builder.StoreA(GameBoyPackedCameraRuntime.VisualPendingColumnGroupHigh);
        builder.Emit(0xC9);
    }

    private static void EmitIsVisualCacheGroupProtected(GbBuilder builder)
    {
        var protectColumn = builder.CreateLabel("worldpack_visual_protect_column_group");
        var compare = builder.CreateLabel("worldpack_visual_compare_protected_group");
        var notProtected = builder.CreateLabel("worldpack_visual_group_not_protected");
        var done = builder.CreateLabel("worldpack_visual_group_protection_done");
        builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xCA, protectColumn);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualLastRowGroupValid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualLastRowGroupHigh);
        builder.LoadBFromA();
        builder.LoadAFromD();
        builder.CompareB();
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualLastRowGroupLow);
        builder.JumpAbsolute(compare);

        builder.Label(protectColumn);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualLastColumnGroupValid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualLastColumnGroupHigh);
        builder.LoadBFromA();
        builder.LoadAFromD();
        builder.CompareB();
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyPackedCameraRuntime.VisualLastColumnGroupLow);

        builder.Label(compare);
        builder.LoadBFromA();
        builder.LoadAFromE();
        builder.CompareB();
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(done);
        builder.Label(notProtected);
        builder.XorA();
        builder.Label(done);
        builder.Emit(0xC9);
    }

    private static void EmitRecordSelectedVisualCacheGroup(GbBuilder builder)
    {
        var row = builder.CreateLabel("worldpack_visual_record_row_group");
        var done = builder.CreateLabel("worldpack_visual_record_group_done");
        builder.LoadA(GameBoyPackedCameraRuntime.VisualSelectedSlot);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadA(GameBoyPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xCA, row);
        builder.LoadHl(GameBoyPackedCameraRuntime.VisualCacheColumnGroupLowStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyPackedCameraRuntime.VisualLastColumnGroupLow);
        builder.LoadHl(GameBoyPackedCameraRuntime.VisualCacheColumnGroupHighStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyPackedCameraRuntime.VisualLastColumnGroupHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.VisualLastColumnGroupValid);
        builder.JumpAbsolute(done);
        builder.Label(row);
        builder.LoadHl(GameBoyPackedCameraRuntime.VisualCacheRowGroupLowStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyPackedCameraRuntime.VisualLastRowGroupLow);
        builder.LoadHl(GameBoyPackedCameraRuntime.VisualCacheRowGroupHighStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyPackedCameraRuntime.VisualLastRowGroupHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.VisualLastRowGroupValid);
        builder.Label(done);
        builder.Emit(0xC9);
    }

    private static void EmitRememberEdgeExpansionAddress(GbBuilder builder, bool banked)
    {
        var done = builder.CreateLabel("worldpack_expansion_address_not_needed");
        builder.LoadA(GameBoyPackedCameraRuntime.EdgePreparationBankSession);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadAFromL();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeExpansionAddressLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyPackedCameraRuntime.EdgeExpansionAddressHigh);
        if (banked)
        {
            builder.LoadA(ScratchSourceBank);
        }
        else
        {
            builder.XorA();
        }

        builder.StoreA(GameBoyPackedCameraRuntime.EdgeExpansionBank);
        builder.Label(done);
    }

    private static void EmitIncrementScratchOnCarry(GbBuilder builder, ushort address)
    {
        var noCarry = builder.CreateLabel("worldpack_offset_no_carry");
        builder.JumpAbsolute(0xD2, noCarry);
        builder.LoadA(address);
        builder.Emit(0x3C); // INC A
        builder.StoreA(address);
        builder.Label(noCarry);
    }

    private static void EmitCollisionRuntime(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string profileBytes,
        string runtimeDirectory,
        DecoderLabels decoders,
        bool enableStagedCamera)
    {
        var lookupBounds = builder.CreateLabel("worldpack_collision_lookup_bounds");
        var lookupReturnStatus = builder.CreateLabel("worldpack_collision_lookup_return_status");
        var lookupMalformed = builder.CreateLabel("worldpack_collision_lookup_malformed");
        var cellMiss = builder.CreateLabel("worldpack_collision_cell_miss");
        var checkCache1 = builder.CreateLabel("worldpack_collision_check_cache_1");
        var cache0 = builder.CreateLabel("worldpack_collision_cache_0");
        var cache1 = builder.CreateLabel("worldpack_collision_cache_1");
        var selectSlot = builder.CreateLabel("worldpack_collision_select_slot");
        var selectSlot0 = builder.CreateLabel("worldpack_collision_select_slot_0");
        var selectSlot1 = builder.CreateLabel("worldpack_collision_select_slot_1");
        var rawDirect = builder.CreateLabel("worldpack_collision_raw_direct");
        var rleDecode = builder.CreateLabel("worldpack_collision_rle_decode");
        var rleDecoded = builder.CreateLabel("worldpack_collision_rle_decoded");
        var publishSlot1 = builder.CreateLabel("worldpack_collision_publish_slot_1");
        var idReady = builder.CreateLabel("worldpack_collision_id_ready");
        var descriptor = plan.Pack.Descriptor;

        EmitPlaneDecodeEntry(
            builder,
            GameBoyRomBuilder.WorldPackCollisionDecodeLabel,
            plan,
            layout,
            planeOffset: 1,
            plan.Layout.CollisionSlots,
            runtimeDirectory,
            decoders.Raw,
            decoders.Rle,
            enableStagedCamera);

        builder.Label(GameBoyRomBuilder.WorldPackCollisionLookupLabel);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCellValid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'E', GameBoyWorldPackRuntimeAbi.CollisionCellXLow, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'D', GameBoyWorldPackRuntimeAbi.CollisionCellXHigh, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'L', GameBoyWorldPackRuntimeAbi.CollisionCellYLow, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'H', GameBoyWorldPackRuntimeAbi.CollisionCellYHigh, cellMiss);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCellResult);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);

        builder.Label(cellMiss);
        if (plan.Layout.CollisionMemoTable is { } collisionMemoTable)
        {
            EmitJumpIfRegisterWordAtOrAbove(builder, 'D', 'E', descriptor.HardwareWidth, lookupBounds);
            EmitJumpIfRegisterWordAtOrAbove(builder, 'H', 'L', descriptor.HardwareHeight, lookupBounds);
            builder.LoadAFromE();
            builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
            builder.LoadAFromD();
            builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionQueryXHigh);
            builder.LoadAFromL();
            builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionQueryYLow);
            builder.LoadAFromH();
            builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionQueryYHigh);
            EmitTryCollisionMemoTableLookup(builder, collisionMemoTable);
        }
        else
        {
            EmitStoreLookupCoordinates(builder);
            CopyByte(builder, ScratchXLow, GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
            CopyByte(builder, ScratchXHigh, GameBoyWorldPackRuntimeAbi.CollisionQueryXHigh);
            CopyByte(builder, ScratchYLow, GameBoyWorldPackRuntimeAbi.CollisionQueryYLow);
            CopyByte(builder, ScratchYHigh, GameBoyWorldPackRuntimeAbi.CollisionQueryYHigh);
            EmitJumpIfWordOutside(builder, ScratchXLow, ScratchXHigh, 0, descriptor.HardwareWidth, lookupBounds);
            EmitJumpIfWordOutside(builder, ScratchYLow, ScratchYHigh, 0, descriptor.HardwareHeight, lookupBounds);
        }

        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, lookupReturnStatus);

        if (plan.Layout.CollisionMemoTable is not null)
        {
            EmitPrepareFastCollisionLookupState(builder, descriptor);
        }
        else
        {
            EmitPrepareLookupState(
                builder,
                plan,
                enableStagedCamera,
                guardLookupState: false);
        }
        CopyByte(builder, ScratchXLow, GameBoyWorldPackRuntimeAbi.CollisionPendingChunkLow);
        CopyByte(builder, ScratchXHigh, GameBoyWorldPackRuntimeAbi.CollisionPendingChunkHigh);

        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCache0Valid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, checkCache1);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyWorldPackRuntimeAbi.CollisionCache0ChunkLow,
            GameBoyWorldPackRuntimeAbi.CollisionPendingChunkLow,
            checkCache1);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyWorldPackRuntimeAbi.CollisionCache0ChunkHigh,
            GameBoyWorldPackRuntimeAbi.CollisionPendingChunkHigh,
            checkCache1);
        builder.JumpAbsolute(cache0);

        builder.Label(checkCache1);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCache1Valid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, selectSlot);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyWorldPackRuntimeAbi.CollisionCache1ChunkLow,
            GameBoyWorldPackRuntimeAbi.CollisionPendingChunkLow,
            selectSlot);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyWorldPackRuntimeAbi.CollisionCache1ChunkHigh,
            GameBoyWorldPackRuntimeAbi.CollisionPendingChunkHigh,
            selectSlot);
        builder.JumpAbsolute(cache1);

        builder.Label(selectSlot);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCache0Valid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, selectSlot0);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCache1Valid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, selectSlot1);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionReplacementNext);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, selectSlot0);
        builder.JumpAbsolute(selectSlot1);

        builder.Label(selectSlot0);
        builder.XorA();
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);
        builder.JumpAbsolute(rawDirect);
        builder.Label(selectSlot1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);

        builder.Label(rawDirect);
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 6, enableStagedCamera);
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate((byte)WorldPackCodec.Raw);
        var loadRaw = builder.CreateLabel("worldpack_collision_load_raw");
        builder.JumpAbsolute(0xCA, loadRaw);
        builder.CompareImmediate((byte)WorldPackCodec.ElementRle);
        builder.JumpAbsolute(0xCA, rleDecode);
        builder.JumpAbsolute(lookupMalformed);

        builder.Label(loadRaw);
        EmitLoadRawCollisionId(builder, descriptor, layout, decoders.Malformed, enableStagedCamera);
        builder.JumpAbsolute(idReady);

        builder.Label(rleDecode);
        EmitIncrementWordCounter(
            builder,
            GameBoyWorldPackRuntimeAbi.CollisionDecodeCountLow,
            GameBoyWorldPackRuntimeAbi.CollisionDecodeCountHigh);
        EmitInvalidateSelectedCollisionSlot(builder);
        EmitLoadSelectedCollisionSlotDestination(builder, plan.Layout.CollisionSlots);
        EmitSetValidationOnly(builder, validationOnly: false);
        builder.JumpAbsolute(0xCD, decoders.Rle);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, lookupReturnStatus);
        builder.JumpAbsolute(rleDecoded);

        builder.Label(rleDecoded);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, publishSlot1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCache0Valid);
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionPendingChunkLow, GameBoyWorldPackRuntimeAbi.CollisionCache0ChunkLow);
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionPendingChunkHigh, GameBoyWorldPackRuntimeAbi.CollisionCache0ChunkHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionReplacementNext);
        builder.JumpAbsolute(cache0);

        builder.Label(publishSlot1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCache1Valid);
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionPendingChunkLow, GameBoyWorldPackRuntimeAbi.CollisionCache1ChunkLow);
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionPendingChunkHigh, GameBoyWorldPackRuntimeAbi.CollisionCache1ChunkHigh);
        builder.XorA();
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionReplacementNext);
        builder.JumpAbsolute(cache1);

        builder.Label(cache0);
        builder.XorA();
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);
        EmitLoadSelectedCollisionId(builder, descriptor, plan.Layout.CollisionSlots);
        builder.JumpAbsolute(idReady);
        builder.Label(cache1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);
        EmitLoadSelectedCollisionId(builder, descriptor, plan.Layout.CollisionSlots);

        builder.Label(idReady);
        builder.LoadA(ScratchXLow);
        builder.LoadCFromA();
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.LoadA(ScratchXHigh);
            builder.LoadBFromA();
        }
        else
        {
            builder.Emit(0x06, 0x00); // LD B,0
        }

        EmitJumpIfIdOutsideRange(builder, descriptor.CollisionIdBytes, descriptor.CollisionProfileCount, lookupMalformed);
        EmitCollisionProfileLookupFromScratchId(builder, plan, profileBytes);
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCellResult);
        if (plan.Layout.CollisionMemoTable is { } collisionMemoTableForPublish)
        {
            EmitPublishCollisionMemoTable(builder, collisionMemoTableForPublish);
        }

        EmitPublishExactCollisionCellAndReturnSuccess(builder);
        builder.Label(lookupBounds);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
        builder.Emit(0xC9);
        builder.Label(lookupReturnStatus);
        builder.LoadAImmediate(0);
        builder.Emit(0xC9);
        builder.Label(lookupMalformed);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);

        EmitRawDecoder(
            builder,
            descriptor.CollisionIdBytes,
            descriptor.CollisionProfileCount,
            decoders.Raw,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack,
            enableStagedCamera);
        EmitRleDecoder(
            builder,
            descriptor.CollisionIdBytes,
            descriptor.CollisionProfileCount,
            decoders.Rle,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack,
            enableStagedCamera);
        EmitReadSourceByte(builder, decoders.ReadSourceByte, layout.UsesBankedWorldPack, enableStagedCamera);
        EmitDecodeReturns(builder, decoders.Success, decoders.Malformed, layout.UsesBankedWorldPack, enableStagedCamera);
    }

    private static void EmitJumpIfRegisterDoesNotMatch(
        GbBuilder builder,
        char register,
        ushort address,
        string mismatch)
    {
        builder.LoadA(address);
        builder.LoadBFromA();
        LoadAFromRegister(builder, register);
        builder.CompareB();
        builder.JumpAbsolute(0xC2, mismatch);
    }

    private static void EmitPrepareFastCollisionLookupState(
        GbBuilder builder,
        WorldPackDescriptor descriptor)
    {
        // SupportsCollisionMemoTable guarantees 2x2 metatiles, 8x8 chunks,
        // twenty chunk columns, one-byte IDs, and the four-cell clipped tail.
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryYLow);
        builder.AndImmediate(0x01);
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
        builder.AndImmediate(0x01);
        builder.AddAFromB();
        builder.StoreA(ScratchSubcellIndex);
        builder.XorA();
        builder.StoreA(ScratchSubcellIndexHigh);

        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXHigh);
        builder.LoadDFromA();
        for (var shift = 0; shift < 4; shift++)
        {
            EmitShiftRightDe(builder);
        }

        builder.LoadAFromE();
        builder.StoreA(ScratchSourceBank); // Reduced-path alias: chunk X, not a ROM bank.

        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryYLow);
        for (var shift = 0; shift < 4; shift++)
        {
            builder.ShiftRightLogicalA();
        }

        builder.AddAFromA();
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.AddAFromA();
        builder.AddAFromA();
        builder.AddAFromB();
        builder.LoadBFromA();
        builder.LoadA(ScratchSourceBank);
        builder.AddAFromB();
        builder.StoreA(ScratchXLow);
        builder.XorA();
        builder.StoreA(ScratchXHigh);

        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryYLow);
        builder.ShiftRightLogicalA();
        builder.AndImmediate(0x07);
        builder.StoreA(ScratchStoredRemaining); // Reduced-path alias: local metatile Y.
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
        builder.ShiftRightLogicalA();
        builder.AndImmediate(0x07);
        builder.StoreA(ScratchYHigh); // Reduced-path alias: local metatile X.

        var clippedWidth = builder.CreateLabel("worldpack_fast_collision_clipped_width");
        var cellReady = builder.CreateLabel("worldpack_fast_collision_cell_ready");
        builder.LoadA(ScratchSourceBank);
        builder.CompareImmediate(descriptor.ChunkColumns - 1);
        builder.JumpAbsolute(0xCA, clippedWidth);
        builder.LoadA(ScratchStoredRemaining);
        builder.AddAFromA();
        builder.AddAFromA();
        builder.AddAFromA();
        builder.JumpAbsolute(cellReady);
        builder.Label(clippedWidth);
        builder.LoadA(ScratchStoredRemaining);
        builder.AddAFromA();
        builder.AddAFromA();
        builder.Label(cellReady);
        builder.LoadBFromA();
        builder.LoadA(ScratchYHigh);
        builder.AddAFromB();
        builder.StoreA(ScratchCellIndex);
    }

    private static void EmitJumpIfBytesDiffer(
        GbBuilder builder,
        ushort left,
        ushort right,
        string mismatch)
    {
        builder.LoadA(left);
        builder.LoadBFromA();
        builder.LoadA(right);
        builder.CompareB();
        builder.JumpAbsolute(0xC2, mismatch);
    }

    private static void EmitJumpIfRegisterWordAtOrAbove(
        GbBuilder builder,
        char highRegister,
        char lowRegister,
        int maximumExclusive,
        string outside)
    {
        var below = builder.CreateLabel("worldpack_fast_bounds_below");
        LoadAFromRegister(builder, highRegister);
        builder.CompareImmediate((maximumExclusive >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, below);
        builder.JumpAbsolute(0xC2, outside);
        if ((maximumExclusive & 0xFF) == 0)
        {
            builder.JumpAbsolute(outside);
        }
        else
        {
            LoadAFromRegister(builder, lowRegister);
            builder.CompareImmediate(maximumExclusive & 0xFF);
            builder.JumpAbsolute(0xD2, outside);
        }

        builder.Label(below);
    }

    private static void CopyByte(GbBuilder builder, ushort source, ushort target)
    {
        builder.LoadA(source);
        builder.StoreA(target);
    }

    private static void EmitTryCollisionMemoTableLookup(
        GbBuilder builder,
        GameBoyWramRange memoTable)
    {
        var miss = builder.CreateLabel("worldpack_collision_memo_table_miss");
        EmitLoadCollisionMemoTablePointer(builder, memoTable);
        builder.LoadAFromHl();
        builder.LoadBFromA();
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
        builder.CompareB();
        builder.JumpAbsolute(0xC2, miss);
        builder.IncrementHl();
        builder.LoadAFromHl();
        builder.StoreA(ScratchReadByte);
        EmitLoadCollisionMemoHighTag(builder);
        builder.LoadBFromA();
        builder.LoadA(ScratchReadByte);
        builder.CompareB();
        builder.JumpAbsolute(0xC2, miss);
        builder.IncrementHl();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCellResult);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionMemoHitCount);
        builder.Emit(0x3C); // INC A
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionMemoHitCount);
        EmitPublishExactCollisionCellAndReturnSuccess(builder);
        builder.Label(miss);
    }

    private static void EmitPublishExactCollisionCellAndReturnSuccess(GbBuilder builder)
    {
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionQueryXLow, GameBoyWorldPackRuntimeAbi.CollisionCellXLow);
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionQueryXHigh, GameBoyWorldPackRuntimeAbi.CollisionCellXHigh);
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionQueryYLow, GameBoyWorldPackRuntimeAbi.CollisionCellYLow);
        CopyByte(builder, GameBoyWorldPackRuntimeAbi.CollisionQueryYHigh, GameBoyWorldPackRuntimeAbi.CollisionCellYHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCellValid);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCellResult);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
    }

    private static void EmitPublishCollisionMemoTable(
        GbBuilder builder,
        GameBoyWramRange memoTable)
    {
        EmitLoadCollisionMemoTablePointer(builder, memoTable);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
        builder.StoreHlA();
        builder.IncrementHl();
        EmitLoadCollisionMemoHighTag(builder);
        builder.StoreHlA();
        builder.IncrementHl();
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionCellResult);
        builder.StoreHlA();
    }

    private static void EmitLoadCollisionMemoTablePointer(
        GbBuilder builder,
        GameBoyWramRange memoTable)
    {
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXLow);
        builder.AndImmediate(0x1F);
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryYLow);
        builder.AndImmediate(0x01);
        builder.AddAFromB();
        builder.LoadBFromA();
        builder.AddAFromA();
        builder.AddAFromB();
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(memoTable.Start);
        builder.AddHlDe();
    }

    private static void EmitLoadCollisionMemoHighTag(GbBuilder builder)
    {
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryYLow);
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionQueryXHigh);
        builder.AddAFromB();
        builder.OrImmediate(0x80);
    }

    private static void EmitIncrementWordCounter(GbBuilder builder, ushort lowAddress, ushort highAddress)
    {
        var done = builder.CreateLabel("worldpack_increment_word_done");
        builder.LoadA(lowAddress);
        builder.Emit(0x3C); // INC A
        builder.StoreA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadA(highAddress);
        builder.Emit(0x3C); // INC A
        builder.StoreA(highAddress);
        builder.Label(done);
    }

    private static void EmitLoadSelectedCollisionSlotDestination(
        GbBuilder builder,
        IReadOnlyList<GameBoyWramRange> slots)
    {
        var slot1 = builder.CreateLabel("worldpack_collision_destination_slot_1");
        var ready = builder.CreateLabel("worldpack_collision_destination_ready");
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadDe(slots[0].Start);
        builder.JumpAbsolute(ready);
        builder.Label(slot1);
        builder.LoadDe(slots[1].Start);
        builder.Label(ready);
    }

    private static void EmitInvalidateSelectedCollisionSlot(GbBuilder builder)
    {
        var slot1 = builder.CreateLabel("worldpack_collision_invalidate_slot_1");
        var ready = builder.CreateLabel("worldpack_collision_invalidate_ready");
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.XorA();
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCache0Valid);
        builder.JumpAbsolute(ready);
        builder.Label(slot1);
        builder.XorA();
        builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCache1Valid);
        builder.Label(ready);
    }

    private static void EmitLoadSelectedCollisionId(
        GbBuilder builder,
        WorldPackDescriptor descriptor,
        IReadOnlyList<GameBoyWramRange> slots)
    {
        var slot1 = builder.CreateLabel("worldpack_collision_id_slot_1");
        var ready = builder.CreateLabel("worldpack_collision_id_slot_ready");
        builder.LoadA(ScratchCellIndex);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.AddAFromA();
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadA(GameBoyWorldPackRuntimeAbi.CollisionSelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadHl(slots[0].Start);
        builder.JumpAbsolute(ready);
        builder.Label(slot1);
        builder.LoadHl(slots[1].Start);
        builder.Label(ready);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(ScratchXLow);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.IncrementHl();
            builder.LoadAFromHl();
            builder.StoreA(ScratchXHigh);
        }
        else
        {
            builder.XorA();
            builder.StoreA(ScratchXHigh);
        }
    }

    private static void EmitLoadRawCollisionId(
        GbBuilder builder,
        WorldPackDescriptor descriptor,
        GameBoyRomLayout layout,
        string malformed,
        bool enableStagedCamera)
    {
        EmitBeginDecode(builder, layout.UsesBankedWorldPack, enableStagedCamera);
        builder.LoadA(ScratchCellIndex);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.AddAFromA();
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        if (layout.UsesBankedWorldPack)
        {
            EmitAdvanceSourceBankIfNeeded(builder, enableStagedCamera);
        }

        builder.LoadAFromHl();
        builder.StoreA(ScratchXLow);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.IncrementHl();
            if (layout.UsesBankedWorldPack)
            {
                EmitAdvanceSourceBankIfNeeded(builder, enableStagedCamera);
            }

            builder.LoadAFromHl();
            builder.StoreA(ScratchXHigh);
        }
        else
        {
            builder.XorA();
            builder.StoreA(ScratchXHigh);
        }

        EmitValidateSafeStagedId(
            builder,
            descriptor.CollisionIdBytes,
            descriptor.CollisionProfileCount,
            malformed);
        if (layout.UsesBankedWorldPack)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
        }
    }

    private static void EmitCollisionProfileLookupFromScratchId(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        string profileBytes)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(ScratchXLow);
        builder.LoadCFromA();
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.LoadA(ScratchXHigh);
            builder.LoadBFromA();
        }
        else
        {
            builder.Emit(0x06, 0x00); // LD B,0
        }

        builder.LoadHl(0);
        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        for (var index = 0; index < metatileCells; index++)
        {
            builder.AddHlBc();
        }

        builder.LoadDe(profileBytes);
        builder.AddHlDe();
        builder.LoadA(ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(ScratchSubcellIndexHigh);
        builder.LoadDFromA();
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private static void EmitPlaneDecodeEntry(
        GbBuilder builder,
        string entryLabel,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        int planeOffset,
        IReadOnlyList<GameBoyWramRange> slots,
        string runtimeDirectory,
        string rawDecoder,
        string rleDecoder,
        bool enableStagedCamera)
    {
        var bounds = builder.CreateLabel("worldpack_decode_bounds");
        var miss = builder.CreateLabel("worldpack_decode_miss");
        var returnStatus = builder.CreateLabel("worldpack_decode_return_status");
        builder.Label(entryLabel);
        if (planeOffset == 1)
        {
            builder.XorA();
            builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCache0Valid);
            builder.StoreA(GameBoyWorldPackRuntimeAbi.CollisionCache1Valid);
        }

        if (enableStagedCamera)
        {
            GameBoyPackedCameraRuntimeEmitter.EmitWaitOutsideVBlank(builder);
            GameBoyPackedCameraRuntimeEmitter.EmitRecordCriticalWork(
                builder,
                GameBoyPackedCameraRuntime.DecodeWorkInCommit);
            if (planeOffset == 0)
            {
                builder.LoadAImmediate(0);
                for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
                {
                    builder.StoreA(VisualCacheValidAddress(slot));
                }
            }
        }
        builder.Emit(0xC5); // PUSH BC
        builder.PushHl();
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.PopHl();
        builder.Emit(0xC1); // POP BC
        builder.LoadBFromA();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        builder.LoadAFromL();
        builder.StoreA(ScratchXLow);
        builder.LoadAFromH();
        builder.StoreA(ScratchXHigh);
        builder.LoadAFromC();
        builder.StoreA(ScratchPacketCount);
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(2);
        builder.JumpAbsolute(0xD2, bounds);

        EmitJumpIfWordOutside(builder, ScratchXLow, ScratchXHigh, 0, plan.Pack.Chunks.Count, miss);
        EmitLoadSlotDestination(builder, slots);
        builder.Emit(0xD5); // PUSH DE; source preparation uses DE as arithmetic scratch
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeOffset * 6, enableStagedCamera);
        builder.Emit(0xD1); // POP DE
        EmitSetValidationOnly(builder, validationOnly: false);
        EmitCallSelectedDecoder(builder, rawDecoder, rleDecoder, returnStatus);
        builder.Emit(0xC9);
        builder.Label(bounds);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
        builder.Emit(0xC9);
        builder.Label(miss);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Miss);
        builder.Emit(0xC9);
        builder.Label(returnStatus);
        builder.LoadAImmediate(0);
        builder.Emit(0xC9);
    }

    private static void EmitLoadPlaneAndPrepareSource(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        int planeEntryOffset,
        bool enableStagedCamera)
    {
        if (enableStagedCamera)
        {
            GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
                builder,
                GameBoyPackedCameraRuntime.DirectoryWorkInVBlank);
        }

        EmitRuntimeDirectoryPointer(builder, runtimeDirectory, planeEntryOffset);
        builder.Emit(0x2A); // LD A,(HL+): offset low
        builder.StoreA(ScratchXLow);
        builder.Emit(0x2A); // offset middle
        builder.StoreA(ScratchXHigh);
        builder.Emit(0x2A); // offset high
        builder.StoreA(ScratchYLow);
        builder.Emit(0x2A); // stored bytes
        builder.StoreA(ScratchStoredRemaining);
        builder.Emit(0x2A); // decoded elements
        builder.LoadCFromA();
        builder.LoadAFromHl(); // codec
        builder.StoreA(ScratchPacketCount);

        if (layout.WorldPackPlacement is { } placement)
        {
            builder.LoadA(ScratchXLow);
            builder.LoadLFromA();
            builder.LoadA(ScratchXHigh);
            builder.LoadHFromA();
            builder.LoadDe(checked((ushort)(placement.BaseAddress - 0x4000)));
            builder.AddHlDe();
            builder.LoadA(ScratchYLow);
            builder.AdcAImmediate(0);
            builder.StoreA(ScratchYLow);
            builder.LoadAFromH();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.LoadBFromA();
            builder.LoadA(ScratchYLow);
            builder.AddAFromA();
            builder.AddAFromA();
            builder.AddAFromB();
            builder.AddAImmediate(placement.BaseBank);
            builder.StoreA(ScratchSourceBank);
            builder.LoadAFromH();
            builder.AndImmediate(0x3F);
            builder.OrImmediate(0x40);
            builder.LoadHFromA();
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.WorldPackLabel);
            builder.LoadA(ScratchXLow);
            builder.LoadEFromA();
            builder.LoadA(ScratchXHigh);
            builder.LoadDFromA();
            builder.AddHlDe();
            builder.LoadAImmediate(0);
            builder.StoreA(ScratchSourceBank);
        }
    }

    private static void EmitCallSelectedDecoder(
        GbBuilder builder,
        string rawDecoder,
        string rleDecoder,
        string malformedReturn)
    {
        var rle = builder.CreateLabel("worldpack_decode_select_rle");
        var done = builder.CreateLabel("worldpack_decode_select_done");
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate((byte)WorldPackCodec.Raw);
        builder.JumpAbsolute(0xC2, rle);
        builder.JumpAbsolute(0xCD, rawDecoder);
        builder.JumpAbsolute(done);
        builder.Label(rle);
        builder.CompareImmediate((byte)WorldPackCodec.ElementRle);
        builder.JumpAbsolute(0xC2, malformedReturn);
        builder.JumpAbsolute(0xCD, rleDecoder);
        builder.Label(done);
    }

    private static void EmitRuntimeDirectoryPointer(
        GbBuilder builder,
        string runtimeDirectory,
        int entryOffset)
    {
        builder.LoadA(ScratchXHigh);
        builder.LoadBFromA();
        builder.LoadA(ScratchXLow);
        builder.LoadCFromA();
        builder.Emit(0x60); // LD H,B
        builder.Emit(0x69); // LD L,C
        for (var shift = 0; shift < 4; shift++)
        {
            builder.Emit(0x29); // ADD HL,HL; 16-byte runtime-directory entries.
        }

        builder.LoadDe(runtimeDirectory);
        builder.AddHlDe();
        if (entryOffset != 0)
        {
            builder.LoadDe(checked((ushort)entryOffset));
            builder.AddHlDe();
        }
    }

    private static void EmitLoadSlotDestination(GbBuilder builder, IReadOnlyList<GameBoyWramRange> slots)
    {
        var second = builder.CreateLabel("worldpack_decode_second_slot");
        var ready = builder.CreateLabel("worldpack_decode_slot_ready");
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, second);
        builder.LoadDe(slots[0].Start);
        builder.JumpAbsolute(ready);
        builder.Label(second);
        builder.LoadDe(slots[1].Start);
        builder.Label(ready);
    }

    private static (byte Bank, ushort Address, bool InlineLabel) ResolveSource(
        GameBoyRomLayout layout,
        uint relativeOffset)
    {
        if (layout.WorldPackPlacement is { } placement)
        {
            var far = placement.TranslateOffset(checked((int)relativeOffset));
            return (far.Bank, far.Address, false);
        }

        if (relativeOffset > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Inline Game Boy WorldPack relative offset {relativeOffset} exceeds the fixed 16-bit address space.");
        }

        return (0, 0, true);
    }

    private static void EmitRawDecoder(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string label,
        string readSourceByte,
        string success,
        string malformed,
        bool banked,
        bool enableStagedCamera)
    {
        if (enableStagedCamera)
        {
            EmitValidatedStagedRawDecoder(builder, idBytes, idCount, label, success, malformed, banked);
            return;
        }

        var loop = builder.CreateLabel("worldpack_raw_loop");
        builder.Label(label);
        EmitBeginDecode(builder, banked, enableStagedCamera: false);
        builder.Label(loop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, success);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);
        EmitWriteDecodedId(builder, idBytes, enableStagedCamera: false);

        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(loop);
    }

    private static void EmitValidatedStagedRawDecoder(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string label,
        string success,
        string malformed,
        bool banked)
    {
        var loop = builder.CreateLabel("worldpack_staged_raw_loop");
        var secondHalf = builder.CreateLabel("worldpack_staged_raw_second_half");
        var secondLoop = builder.CreateLabel("worldpack_staged_raw_second_loop");
        var done = builder.CreateLabel("worldpack_staged_raw_done");
        builder.Label(label);
        GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
            builder,
            GameBoyPackedCameraRuntime.DecodeWorkInCommit);
        EmitBeginDecode(builder, banked, enableStagedCamera: true);
        builder.LoadAFromC();
        builder.Emit(0xCB, 0x3F); // SRL A
        builder.LoadBFromA();

        void EmitElement()
        {
            if (idBytes == 1)
            {
                builder.LoadAFromHl();
                builder.IncrementHl();
                if (idCount < 256)
                {
                    builder.CompareImmediate(idCount);
                    builder.JumpAbsolute(0xD2, malformed);
                }

                builder.Emit(0x12); // LD (DE),A
                builder.Emit(0x13); // INC DE
                if (banked)
                {
                    EmitAdvanceSourceBankIfNeeded(builder);
                }
            }
            else
            {
                EmitReadSafeStagedByte(builder, ScratchXLow, banked, malformed, trackStoredBytes: false);
                EmitReadSafeStagedByte(builder, ScratchXHigh, banked, malformed, trackStoredBytes: false);
                EmitValidateSafeStagedId(builder, idBytes, idCount, malformed);
                EmitWriteSafeStagedId(builder, idBytes);
            }
        }

        builder.Label(loop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        EmitElement();
        builder.Emit(0x0D); // DEC C
        builder.Emit(0x05); // DEC B
        builder.JumpAbsolute(0xC2, loop);

        builder.Label(secondHalf);
        GameBoyPackedCameraRuntimeEmitter.EmitRecordCriticalWork(
            builder,
            GameBoyPackedCameraRuntime.DecodeWorkInCommit);
        GameBoyPackedCameraRuntimeEmitter.EmitWaitIfInVBlank(builder);
        builder.Label(secondLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        EmitElement();
        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(secondLoop);

        builder.Label(done);
        GameBoyPackedCameraRuntimeEmitter.EmitRecordCriticalWork(
            builder,
            GameBoyPackedCameraRuntime.DecodeWorkInCommit);
        builder.XorA();
        builder.StoreA(ScratchStoredRemaining);
        builder.JumpAbsolute(success);
    }

    private static void EmitAdvanceSourceBankIfNeeded(
        GbBuilder builder,
        bool instrumentPackedCameraCritical = true)
    {
        var noCross = builder.CreateLabel("worldpack_raw_no_cross");
        builder.LoadAFromH();
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xC2, noCross);
        builder.LoadHImmediate(0x40);
        builder.LoadA(ScratchSourceBank);
        builder.Emit(0x3C); // INC A
        builder.StoreA(ScratchSourceBank);
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder, instrumentPackedCameraCritical);
        builder.Label(noCross);
    }

    private static void EmitRleDecoder(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string label,
        string readSourceByte,
        string success,
        string malformed,
        bool banked,
        bool enableStagedCamera)
    {
        if (enableStagedCamera)
        {
            EmitSafeStagedRleDecoder(builder, idBytes, idCount, label, success, malformed, banked);
            return;
        }

        var packetLoop = builder.CreateLabel("worldpack_rle_packet");
        var runPacket = builder.CreateLabel("worldpack_rle_run");
        var literalLoop = builder.CreateLabel("worldpack_rle_literal_loop");
        var runLoop = builder.CreateLabel("worldpack_rle_run_loop");
        var packetDone = builder.CreateLabel("worldpack_rle_packet_done");

        builder.Label(label);
        EmitBeginDecode(builder, banked, enableStagedCamera: false);
        builder.Label(packetLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, success);
        EmitRequireStoredByte(builder, malformed);
        builder.JumpAbsolute(0xCD, readSourceByte);
        builder.StoreA(ScratchReadByte);
        builder.AndImmediate(0x80);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, runPacket);

        builder.LoadA(ScratchReadByte);
        builder.AddAImmediate(1);
        builder.StoreA(ScratchPacketCount);
        EmitRequirePacketFits(builder, malformed);
        builder.Label(literalLoop);
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, packetDone);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);
        EmitWriteDecodedId(builder, idBytes, enableStagedCamera: false);

        builder.Emit(0x0D); // DEC C
        builder.LoadA(ScratchPacketCount);
        builder.DecrementA();
        builder.StoreA(ScratchPacketCount);
        builder.JumpAbsolute(literalLoop);

        builder.Label(runPacket);
        builder.LoadA(ScratchReadByte);
        builder.AndImmediate(0x7F);
        builder.AddAImmediate(3);
        builder.StoreA(ScratchPacketCount);
        EmitRequirePacketFits(builder, malformed);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);

        builder.Label(runLoop);
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, packetDone);
        EmitWriteDecodedId(builder, idBytes, enableStagedCamera: false);

        builder.Emit(0x0D); // DEC C
        builder.LoadA(ScratchPacketCount);
        builder.DecrementA();
        builder.StoreA(ScratchPacketCount);
        builder.JumpAbsolute(runLoop);

        builder.Label(packetDone);
        builder.JumpAbsolute(packetLoop);
    }

    private static void EmitSafeStagedRleDecoder(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string label,
        string success,
        string malformed,
        bool banked)
    {
        var packetLoop = builder.CreateLabel("worldpack_staged_rle_packet");
        var runPacket = builder.CreateLabel("worldpack_staged_rle_run");
        var literalLoop = builder.CreateLabel("worldpack_staged_rle_literal_loop");
        var runLoop = builder.CreateLabel("worldpack_staged_rle_run_loop");
        var packetDone = builder.CreateLabel("worldpack_staged_rle_packet_done");
        var done = builder.CreateLabel("worldpack_staged_rle_done");

        builder.Label(label);
        GameBoyPackedCameraRuntimeEmitter.EmitGuardRleCriticalWork(
            builder,
            GameBoyPackedCameraRuntime.DecodeWorkInCommit);
        EmitBeginDecode(builder, banked, enableStagedCamera: true);
        builder.Label(packetLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        GameBoyPackedCameraRuntimeEmitter.EmitWaitForSafeRleCriticalWork(builder);
        EmitReadSafeStagedByte(builder, ScratchPacketCount, banked, malformed, trackStoredBytes: true);
        builder.LoadA(ScratchPacketCount);
        builder.AndImmediate(0x80);
        builder.JumpAbsolute(0xC2, runPacket);

        builder.LoadA(ScratchPacketCount);
        builder.AddAImmediate(1);
        builder.LoadBFromA();
        EmitRequireRegisterPacketFits(builder, malformed);
        builder.Label(literalLoop);
        EmitReadSafeStagedId(builder, idBytes, idCount, banked, malformed, trackStoredBytes: true);
        EmitWriteSafeStagedId(builder, idBytes);
        builder.Emit(0x0D); // DEC C
        builder.Emit(0x05); // DEC B
        builder.JumpAbsolute(0xC2, literalLoop);
        builder.JumpAbsolute(packetDone);

        builder.Label(runPacket);
        builder.LoadA(ScratchPacketCount);
        builder.AndImmediate(0x7F);
        builder.AddAImmediate(3);
        builder.LoadBFromA();
        EmitRequireRegisterPacketFits(builder, malformed);
        EmitReadSafeStagedId(builder, idBytes, idCount, banked, malformed, trackStoredBytes: true);
        builder.Label(runLoop);
        EmitWriteSafeStagedId(builder, idBytes);
        builder.Emit(0x0D); // DEC C
        builder.Emit(0x05); // DEC B
        builder.JumpAbsolute(0xC2, runLoop);

        builder.Label(packetDone);
        GameBoyPackedCameraRuntimeEmitter.EmitRecordCriticalWork(
            builder,
            GameBoyPackedCameraRuntime.DecodeWorkInCommit);
        builder.JumpAbsolute(packetLoop);

        builder.Label(done);
        builder.JumpAbsolute(success);
    }

    private static void EmitRequireRegisterPacketFits(GbBuilder builder, string malformed)
    {
        builder.LoadAFromC();
        builder.CompareB();
        builder.JumpAbsolute(0xDA, malformed); // JP C when remaining decoded IDs < packet count
    }

    private static void EmitReadSafeStagedId(
        GbBuilder builder,
        int idBytes,
        int idCount,
        bool banked,
        string malformed,
        bool trackStoredBytes)
    {
        EmitReadSafeStagedByte(builder, ScratchXLow, banked, malformed, trackStoredBytes);
        if (idBytes == 2)
        {
            EmitReadSafeStagedByte(builder, ScratchXHigh, banked, malformed, trackStoredBytes);
        }

        EmitValidateSafeStagedId(builder, idBytes, idCount, malformed);
    }

    private static void EmitReadSafeStagedByte(
        GbBuilder builder,
        ushort destination,
        bool banked,
        string malformed,
        bool trackStoredBytes)
    {
        if (trackStoredBytes)
        {
            builder.LoadA(ScratchStoredRemaining);
            builder.CompareImmediate(0);
            builder.JumpAbsolute(0xCA, malformed);
            builder.DecrementA();
            builder.StoreA(ScratchStoredRemaining);
        }

        builder.LoadAFromHl();
        builder.IncrementHl();
        builder.StoreA(destination);

        if (banked)
        {
            EmitAdvanceSourceBankIfNeeded(builder);
        }
    }

    private static void EmitValidateSafeStagedId(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string malformed)
    {
        if (idBytes == 1)
        {
            if (idCount < 256)
            {
                builder.LoadA(ScratchXLow);
                builder.CompareImmediate(idCount);
                builder.JumpAbsolute(0xD2, malformed);
            }

            return;
        }

        var highIsLower = builder.CreateLabel("worldpack_staged_id_high_lower");
        var highMatches = builder.CreateLabel("worldpack_staged_id_high_matches");
        builder.LoadA(ScratchXHigh);
        builder.CompareImmediate((idCount >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, highIsLower);
        builder.JumpAbsolute(0xCA, highMatches);
        builder.JumpAbsolute(malformed);
        builder.Label(highMatches);
        builder.LoadA(ScratchXLow);
        builder.CompareImmediate(idCount & 0xFF);
        builder.JumpAbsolute(0xD2, malformed);
        builder.Label(highIsLower);
    }

    private static void EmitWriteSafeStagedId(GbBuilder builder, int idBytes)
    {
        builder.LoadA(ScratchXLow);
        builder.Emit(0x12); // LD (DE),A
        builder.Emit(0x13); // INC DE
        if (idBytes == 2)
        {
            builder.LoadA(ScratchXHigh);
            builder.Emit(0x12);
            builder.Emit(0x13);
        }
    }

    private static void EmitBeginDecode(GbBuilder builder, bool banked, bool enableStagedCamera)
    {
        if (!banked)
        {
            return;
        }

        builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
        builder.PushAf();
        builder.LoadA(ScratchSourceBank);
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
    }

    private static void EmitReadSourceByte(
        GbBuilder builder,
        string label,
        bool banked,
        bool enableStagedCamera)
    {
        var noCross = builder.CreateLabel("worldpack_read_no_cross");
        builder.Label(label);
        builder.LoadAFromHl();
        builder.StoreA(ScratchReadByte);
        builder.IncrementHl();
        builder.LoadA(ScratchStoredRemaining);
        builder.DecrementA();
        builder.StoreA(ScratchStoredRemaining);
        if (banked)
        {
            builder.LoadAFromH();
            builder.CompareImmediate(0x80);
            builder.JumpAbsolute(0xC2, noCross);
            builder.LoadHImmediate(0x40);
            builder.LoadA(ScratchSourceBank);
            builder.Emit(0x3C); // INC A
            builder.StoreA(ScratchSourceBank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
            builder.Label(noCross);
        }

        builder.LoadA(ScratchReadByte);
        builder.Emit(0xC9);
    }

    private static void EmitDecodeReturns(
        GbBuilder builder,
        string success,
        string malformed,
        bool banked,
        bool enableStagedCamera)
    {
        var successReturn = builder.CreateLabel("worldpack_decode_success_return");
        var malformedReturn = builder.CreateLabel("worldpack_decode_malformed_return");
        builder.Label(success);
        builder.LoadA(ScratchStoredRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, malformed);
        builder.JumpAbsolute(successReturn);
        builder.Label(malformed);
        builder.JumpAbsolute(malformedReturn);

        builder.Label(successReturn);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
        }

        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);

        builder.Label(malformedReturn);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
        }

        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);
    }

    private static void EmitRequireStoredByte(GbBuilder builder, string malformed)
    {
        builder.LoadA(ScratchStoredRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, malformed);
    }

    private static void EmitRequirePacketFits(GbBuilder builder, string malformed)
    {
        builder.LoadA(ScratchPacketCount);
        builder.LoadBFromA();
        builder.LoadAFromC();
        builder.CompareB();
        builder.JumpAbsolute(0xDA, malformed); // JP C when remaining < packet count
    }

    private static void EmitStoreLookupCoordinates(GbBuilder builder)
    {
        builder.LoadAFromE();
        builder.StoreA(ScratchXLow);
        builder.LoadAFromD();
        builder.StoreA(ScratchXHigh);
        builder.LoadAFromL();
        builder.StoreA(ScratchYLow);
        builder.LoadAFromH();
        builder.StoreA(ScratchYHigh);
    }

    private static void EmitPrepareLookupState(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        bool enableStagedCamera,
        bool guardLookupState)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(ScratchXLow);
        builder.LoadEFromA();
        builder.LoadA(ScratchXHigh);
        builder.LoadDFromA();
        EmitDivideDeByConstant(builder, descriptor.MetatileWidth, ScratchSourceBank);

        builder.Emit(0xD5); // PUSH DE; preserve metatile X while dividing Y
        builder.LoadA(ScratchYLow);
        builder.LoadEFromA();
        builder.LoadA(ScratchYHigh);
        builder.LoadDFromA();
        EmitDivideDeByConstant(builder, descriptor.MetatileHeight, ScratchReadByte);
        builder.Emit(0x62); // LD H,D
        builder.Emit(0x6B); // LD L,E
        builder.Emit(0xD1); // POP DE

        builder.LoadAFromE();
        builder.AndImmediate(0x07);
        builder.StoreA(ScratchYHigh); // local metatile X
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyPackedCameraRuntime.EdgeLocalX);
        }
        builder.LoadAFromL();
        builder.AndImmediate(0x07);
        builder.StoreA(ScratchStoredRemaining); // local metatile Y
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyPackedCameraRuntime.EdgeLocalY);
            builder.LoadA(ScratchSourceBank);
            builder.StoreA(GameBoyPackedCameraRuntime.EdgeSubcellX);
            builder.LoadA(ScratchReadByte);
            builder.StoreA(GameBoyPackedCameraRuntime.EdgeSubcellY);
        }
        for (var shift = 0; shift < 3; shift++)
        {
            EmitShiftRightDe(builder);
            EmitShiftRightHl(builder);
        }

        if (guardLookupState)
        {
            GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
                builder,
                GameBoyPackedCameraRuntime.DirectoryWorkInVBlank);
        }

        var sourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
        var lastChunkWidth = sourceWidth - ((descriptor.ChunkColumns - 1) * descriptor.ChunkWidth);
        EmitLoadClippedChunkDimensionToA(
            builder,
            highRegister: 'D',
            lowRegister: 'E',
            descriptor.ChunkColumns - 1,
            descriptor.ChunkWidth,
            lastChunkWidth,
            "worldpack_lookup_width");
        builder.StoreA(ScratchCellIndex);
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyPackedCameraRuntime.EdgeValidWidth);

            var sourceHeight = descriptor.HardwareHeight / descriptor.MetatileHeight;
            var lastChunkHeight = sourceHeight - ((descriptor.ChunkRows - 1) * descriptor.ChunkHeight);
            EmitLoadClippedChunkDimensionToA(
                builder,
                highRegister: 'H',
                lowRegister: 'L',
                descriptor.ChunkRows - 1,
                descriptor.ChunkHeight,
                lastChunkHeight,
                "worldpack_lookup_height");
            builder.StoreA(GameBoyPackedCameraRuntime.EdgeValidHeight);
        }

        builder.Emit(0x44); // LD B,H (chunk Y high)
        builder.Emit(0x4D); // LD C,L (chunk Y low)
        EmitMultiplyBcByConstantToHl(builder, descriptor.ChunkColumns);

        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(ScratchXLow);
        builder.LoadAFromH();
        builder.StoreA(ScratchXHigh);
        builder.LoadA(ScratchCellIndex);
        builder.LoadBFromA();
        builder.LoadA(ScratchStoredRemaining);
        builder.LoadCFromA();
        builder.XorA();
        builder.StoreA(ScratchCellIndex);
        var cellLoop = builder.CreateLabel("worldpack_cell_index_loop");
        var cellDone = builder.CreateLabel("worldpack_cell_index_done");
        builder.Label(cellLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, cellDone);
        builder.LoadA(ScratchCellIndex);
        builder.AddAFromB();
        builder.StoreA(ScratchCellIndex);
        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(cellLoop);
        builder.Label(cellDone);
        builder.LoadA(ScratchCellIndex);
        builder.LoadBFromA();
        builder.LoadA(ScratchYHigh);
        builder.AddAFromB();
        builder.StoreA(ScratchCellIndex);
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyPackedCameraRuntime.EdgeCellIndex);
        }

        builder.LoadA(ScratchReadByte);
        builder.LoadCFromA();
        builder.Emit(0x06, 0x00); // LD B,0
        builder.LoadHl(0);
        for (var index = 0; index < descriptor.MetatileWidth; index++)
        {
            builder.AddHlBc();
        }

        builder.LoadA(ScratchSourceBank);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(ScratchSubcellIndex);
        builder.LoadAFromH();
        builder.StoreA(ScratchSubcellIndexHigh);
    }

    private static void EmitLoadClippedChunkDimensionToA(
        GbBuilder builder,
        char highRegister,
        char lowRegister,
        int lastChunk,
        int fullDimension,
        int lastDimension,
        string labelPrefix)
    {
        if (lastDimension == fullDimension)
        {
            builder.LoadAImmediate(fullDimension);
            return;
        }

        var full = builder.CreateLabel($"{labelPrefix}_full");
        var ready = builder.CreateLabel($"{labelPrefix}_ready");
        LoadAFromRegister(builder, highRegister);
        builder.CompareImmediate((lastChunk >> 8) & 0xFF);
        builder.JumpAbsolute(0xC2, full);
        LoadAFromRegister(builder, lowRegister);
        builder.CompareImmediate(lastChunk & 0xFF);
        builder.JumpAbsolute(0xC2, full);
        builder.LoadAImmediate(lastDimension);
        builder.JumpAbsolute(ready);
        builder.Label(full);
        builder.LoadAImmediate(fullDimension);
        builder.Label(ready);
    }

    private static void LoadAFromRegister(GbBuilder builder, char register)
    {
        switch (register)
        {
            case 'D':
                builder.LoadAFromD();
                break;
            case 'E':
                builder.LoadAFromE();
                break;
            case 'H':
                builder.LoadAFromH();
                break;
            case 'L':
                builder.LoadAFromL();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(register));
        }
    }

    private static void EmitMultiplyBcByConstantToHl(GbBuilder builder, int multiplier)
    {
        builder.LoadHl(0);
        if (multiplier == 0)
        {
            return;
        }

        var highestBit = 31 - int.LeadingZeroCount(multiplier);
        for (var bit = highestBit; bit >= 0; bit--)
        {
            if (bit != highestBit)
            {
                builder.Emit(0x29); // ADD HL,HL
            }

            if ((multiplier & (1 << bit)) != 0)
            {
                builder.AddHlBc();
            }
        }
    }

    private static void EmitShiftRightDe(GbBuilder builder)
    {
        builder.Emit(0xCB, 0x3A); // SRL D
        builder.Emit(0xCB, 0x1B); // RR E through D carry
    }

    private static void EmitDivideDeByConstant(GbBuilder builder, int divisor, ushort remainderAddress)
    {
        if (divisor == 1)
        {
            builder.XorA();
            builder.StoreA(remainderAddress);
            return;
        }

        if ((divisor & (divisor - 1)) == 0)
        {
            builder.LoadAFromE();
            builder.AndImmediate(divisor - 1);
            builder.StoreA(remainderAddress);
            for (var value = divisor; value > 1; value >>= 1)
            {
                EmitShiftRightDe(builder);
            }

            return;
        }

        var loop = builder.CreateLabel("worldpack_divide_loop");
        var subtract = builder.CreateLabel("worldpack_divide_subtract");
        var done = builder.CreateLabel("worldpack_divide_done");
        builder.LoadBc(0);
        builder.Label(loop);
        builder.LoadAFromD();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, subtract);
        builder.LoadAFromE();
        builder.CompareImmediate(divisor);
        builder.JumpAbsolute(0xDA, done);
        builder.Label(subtract);
        builder.LoadAFromE();
        builder.SubtractAImmediate(divisor);
        builder.LoadEFromA();
        builder.LoadAFromD();
        builder.SbcAImmediate(0);
        builder.LoadDFromA();
        builder.Emit(0x03); // INC BC
        builder.JumpAbsolute(loop);
        builder.Label(done);
        builder.LoadAFromE();
        builder.StoreA(remainderAddress);
        builder.Emit(0x50); // LD D,B
        builder.Emit(0x59); // LD E,C
    }

    private static void EmitShiftRightHl(GbBuilder builder)
    {
        builder.Emit(0xCB, 0x3C); // SRL H
        builder.Emit(0xCB, 0x1D); // RR L through H carry
    }

    private static void EmitReadAndValidateId(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string readSourceByte,
        string malformed)
    {
        EmitRequireStoredByte(builder, malformed);
        builder.JumpAbsolute(0xCD, readSourceByte);
        builder.StoreA(ScratchXLow);
        if (idBytes == 2)
        {
            EmitRequireStoredByte(builder, malformed);
            builder.JumpAbsolute(0xCD, readSourceByte);
            builder.StoreA(ScratchXHigh);
            var highIsLower = builder.CreateLabel("worldpack_id_high_lower");
            var highMatches = builder.CreateLabel("worldpack_id_high_matches");
            builder.LoadA(ScratchXHigh);
            builder.CompareImmediate((idCount >> 8) & 0xFF);
            builder.JumpAbsolute(0xDA, highIsLower);
            builder.JumpAbsolute(0xCA, highMatches);
            builder.JumpAbsolute(malformed);
            builder.Label(highMatches);
            builder.LoadA(ScratchXLow);
            builder.CompareImmediate(idCount & 0xFF);
            builder.JumpAbsolute(0xD2, malformed);
            builder.Label(highIsLower);
        }
        else if (idCount < 256)
        {
            builder.LoadA(ScratchXLow);
            builder.CompareImmediate(idCount);
            builder.JumpAbsolute(0xD2, malformed);
        }
    }

    private static void EmitWriteDecodedId(GbBuilder builder, int idBytes, bool enableStagedCamera)
    {
        var done = builder.CreateLabel("worldpack_decode_skip_validation_write");
        builder.LoadA(ScratchYHigh);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadA(ScratchXLow);
        builder.Emit(0x12); // LD (DE),A
        builder.Emit(0x13); // INC DE
        if (idBytes == 2)
        {
            builder.LoadA(ScratchXHigh);
            builder.Emit(0x12);
            builder.Emit(0x13);
        }

        builder.Label(done);
    }

    private static void EmitSetValidationOnly(GbBuilder builder, bool validationOnly)
    {
        builder.LoadAImmediate(validationOnly ? 1 : 0);
        builder.StoreA(ScratchYHigh);
    }

    private static void EmitJumpIfWordOutside(
        GbBuilder builder,
        ushort lowAddress,
        ushort highAddress,
        int minimum,
        int maximumExclusive,
        string outside)
    {
        var aboveMinimum = builder.CreateLabel("worldpack_range_above_minimum");
        var belowMaximum = builder.CreateLabel("worldpack_range_below_maximum");
        builder.LoadA(highAddress);
        builder.CompareImmediate((minimum >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, outside); // high < min high
        builder.JumpAbsolute(0xC2, aboveMinimum);
        builder.LoadA(lowAddress);
        builder.CompareImmediate(minimum & 0xFF);
        builder.JumpAbsolute(0xDA, outside);
        builder.Label(aboveMinimum);

        builder.LoadA(highAddress);
        builder.CompareImmediate((maximumExclusive >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, belowMaximum);
        builder.JumpAbsolute(0xC2, outside);
        builder.LoadA(lowAddress);
        builder.CompareImmediate(maximumExclusive & 0xFF);
        builder.JumpAbsolute(0xD2, outside); // low >= max low
        builder.Label(belowMaximum);
    }

    private static string EmitMetadataValidation(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        bool enableStagedCamera)
    {
        var malformed = builder.CreateLabel("worldpack_validate_malformed");
        var success = builder.CreateLabel("worldpack_validate_success");
        var cachedMalformed = builder.CreateLabel("worldpack_validate_cached_malformed");
        var validate = builder.CreateLabel("worldpack_validate_uncached");
        var expectedProfiles = builder.CreateLabel("worldpack_expected_profiles");
        var banked = layout.WorldPackPlacement is not null;
        builder.Label(GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadA(GameBoyWramLayout.WorldPackValidationStateAddress);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, cachedMalformed);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
        builder.Label(cachedMalformed);
        builder.CompareImmediate(2);
        builder.JumpAbsolute(0xC2, validate);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);
        builder.Label(validate);
        if (banked)
        {
            builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
            builder.PushAf();
        }

        EmitValidateExactHeader(builder, plan, layout, malformed, enableStagedCamera);
        EmitValidateWholePackFingerprint(builder, plan, layout, malformed, enableStagedCamera);

        builder.JumpAbsolute(success);
        builder.Label(malformed);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
        }

        builder.LoadAImmediate(2);
        builder.StoreA(GameBoyWramLayout.WorldPackValidationStateAddress);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);

        builder.Label(success);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
        }

        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWramLayout.WorldPackValidationStateAddress);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);

        builder.Label(expectedProfiles);
        builder.Emit(plan.CollisionProfileBytes);
        return expectedProfiles;
    }

    private static void EmitValidateExactHeader(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string malformed,
        bool enableStagedCamera)
    {
        var source = ResolveSource(layout, 0);
        if (source.InlineLabel)
        {
            builder.LoadHl(GameBoyRomBuilder.WorldPackLabel);
        }
        else
        {
            builder.LoadAImmediate(source.Bank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
            builder.LoadHl(source.Address);
        }

        foreach (var expected in plan.HeaderBytes)
        {
            builder.Emit(0x2A); // LD A,(HL+)
            builder.CompareImmediate(expected);
            builder.JumpAbsolute(0xC2, malformed);
        }
    }

    private static void EmitValidateWholePackFingerprint(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string malformed,
        bool enableStagedCamera)
    {
        // Four position-sensitive rolling lanes keep the full-pack preflight below one DMG frame.
        // Exact header comparison and semantic decoder checks enforce the malformed-data contract.
        var expected = ComputeFingerprint(plan.SerializedBytes);
        builder.Emit(0x06, 0x3D); // LD B,0x3D
        builder.Emit(0x0E, 0xA7); // LD C,0xA7
        builder.LoadDImmediate(0x5B);
        builder.Emit(0x1E, 0xC1); // LD E,0xC1

        var phase = 0;
        if (layout.WorldPackPlacement is { } placement)
        {
            foreach (var segment in placement.Segments)
            {
                builder.LoadAImmediate(segment.Bank);
                GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
                builder.LoadHl(segment.Address);
                EmitAccumulateFingerprintRange(builder, segment.Length, phase);
                phase = (phase + segment.Length) & 15;
            }
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.WorldPackLabel);
            EmitAccumulateFingerprintRange(builder, plan.SerializedBytes.Length, phase);
        }

        builder.LoadAFromB();
        builder.CompareImmediate(expected[0]);
        builder.JumpAbsolute(0xC2, malformed);
        builder.LoadAFromC();
        builder.CompareImmediate(expected[1]);
        builder.JumpAbsolute(0xC2, malformed);
        builder.LoadAFromD();
        builder.CompareImmediate(expected[2]);
        builder.JumpAbsolute(0xC2, malformed);
        builder.LoadAFromE();
        builder.CompareImmediate(expected[3]);
        builder.JumpAbsolute(0xC2, malformed);
    }

    private static byte[] ComputeFingerprint(ReadOnlySpan<byte> bytes)
    {
        byte[] lanes = [0x3D, 0xA7, 0x5B, 0xC1];
        for (var index = 0; index < bytes.Length; index++)
        {
            var lane = index & 3;
            var sum = unchecked((byte)(lanes[lane] + bytes[index]));
            lanes[lane] = (index >> 2 & 3) == lane
                ? (byte)((sum << 1) | (sum >> 7))
                : sum;
        }

        return lanes;
    }

    private static void EmitAccumulateFingerprintRange(GbBuilder builder, int length, int startingPhase)
    {
        const int bytesPerBlock = 64;
        const int maximumBlocksPerLoop = 128;
        var phase = startingPhase;
        while (length >= bytesPerBlock)
        {
            var blocks = Math.Min(length / bytesPerBlock, maximumBlocksPerLoop);
            var loop = builder.CreateLabel("worldpack_fingerprint_range_loop");
            builder.LoadAImmediate(blocks);
            builder.StoreA(ScratchPacketCount);
            builder.Label(loop);
            for (var index = 0; index < bytesPerBlock; index++)
            {
                EmitAccumulateFingerprintByte(builder, phase);
                phase = (phase + 1) & 15;
            }

            builder.LoadA(ScratchPacketCount);
            builder.DecrementA();
            builder.StoreA(ScratchPacketCount);
            builder.JumpAbsolute(0xC2, loop);
            length -= blocks * bytesPerBlock;
        }

        while (length-- > 0)
        {
            EmitAccumulateFingerprintByte(builder, phase);
            phase = (phase + 1) & 15;
        }
    }

    private static void EmitAccumulateFingerprintByte(GbBuilder builder, int phase)
    {
        var lane = phase & 3;
        builder.Emit(0x2A); // LD A,(HL+)
        builder.Emit(lane switch
        {
            0 => (byte)0x80, // ADD A,B
            1 => (byte)0x81, // ADD A,C
            2 => (byte)0x82, // ADD A,D
            _ => (byte)0x83, // ADD A,E
        });
        if ((phase >> 2 & 3) == lane)
        {
            builder.Emit(0x07); // RLCA; rotate each lane once per 16 source bytes.
        }
        builder.Emit(lane switch
        {
            0 => (byte)0x47, // LD B,A
            1 => (byte)0x4F, // LD C,A
            2 => (byte)0x57, // LD D,A
            _ => (byte)0x5F, // LD E,A
        });
    }

}

internal sealed record GameBoyWorldPackRuntimeLayout(
    int VisualIdBytes,
    int CollisionIdBytes,
    IReadOnlyList<GameBoyWramRange> VisualSlots,
    IReadOnlyList<GameBoyWramRange> CollisionSlots,
    IReadOnlyList<GameBoyWramRange> EdgeSlots,
    GameBoyWramRange? CollisionMemoTable,
    int TotalBytes)
{
    private const int ChunkCells = 64;
    private const int EdgeBytes = 21;
    internal const int CollisionMemoEntryBytes = 3;
    internal const int CollisionMemoEntryCount = 64;

    public static GameBoyWorldPackRuntimeLayout Create(
        int visualIdBytes,
        int collisionIdBytes,
        int visualSlotCount = 2,
        bool enableCollisionMemoTable = false)
    {
        ValidateIdBytes(visualIdBytes, nameof(visualIdBytes));
        ValidateIdBytes(collisionIdBytes, nameof(collisionIdBytes));
        if (visualSlotCount is < 2 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(visualSlotCount));
        }

        var cursor = GameBoyWramLayout.WorldPackStaging.Start;
        var visualSlots = CreateSlots(
            "WorldPack visual slot",
            visualIdBytes * ChunkCells,
            visualSlotCount,
            ref cursor);
        var collisionSlots = CreatePair("WorldPack collision slot", collisionIdBytes * ChunkCells, ref cursor);
        var edgeSlots = CreatePair("WorldPack edge slot", EdgeBytes, ref cursor);
        GameBoyWramRange? collisionMemoTable = enableCollisionMemoTable
            ? GameBoyWramLayout.CollisionMemoState
            : null;
        var totalBytes = cursor - GameBoyWramLayout.WorldPackStaging.Start;
        GameBoyWramLayout.ValidateStagingBytes(totalBytes);

        return new GameBoyWorldPackRuntimeLayout(
            visualIdBytes,
            collisionIdBytes,
            visualSlots,
            collisionSlots,
            edgeSlots,
            collisionMemoTable,
            totalBytes);
    }

    private static IReadOnlyList<GameBoyWramRange> CreatePair(string name, int length, ref ushort cursor)
        => CreateSlots(name, length, 2, ref cursor);

    private static IReadOnlyList<GameBoyWramRange> CreateSlots(string name, int length, int count, ref ushort cursor)
    {
        var slots = new GameBoyWramRange[count];
        for (var index = 0; index < count; index++)
        {
            slots[index] = new GameBoyWramRange($"{name} {index}", cursor, length);
            cursor = checked((ushort)slots[index].EndExclusive);
        }

        return slots;
    }

    private static void ValidateIdBytes(int value, string parameterName)
    {
        if (value is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "WorldPack v1 ID width must be one or two bytes.");
        }
    }
}
