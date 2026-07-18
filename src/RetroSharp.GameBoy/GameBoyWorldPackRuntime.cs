namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal enum GameBoyWorldPackResult : byte
{
    Success = 0,
    Miss = 1,
    BoundsError = 2,
    Malformed = 3,
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
    byte[]? ColumnTiles,
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
            CreateColumnTiles(pack),
            layout,
            serializedBytes[..WorldPackDescriptor.V1HeaderBytes],
            serializedBytes.AsSpan(checked((int)descriptor.CollisionProfilesOffset), profileLength).ToArray(),
            runtimeDirectory.ToArray());
    }

    private static byte[]? CreateColumnTiles(WorldPack pack)
    {
        var descriptor = pack.Descriptor;
        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        var tileCount = checked(descriptor.HardwareWidth * descriptor.HardwareHeight);
        if (descriptor.HardwareWidth <= byte.MaxValue
            || descriptor.HardwareHeight is < 19 or > byte.MaxValue
            || tileCount > 16 * 1024)
        {
            return null;
        }

        var result = new byte[tileCount];
        for (var x = 0; x < descriptor.HardwareWidth; x++)
        {
            for (var y = 0; y < descriptor.HardwareHeight; y++)
            {
                var coordinate = pack.Locate(x, y);
                var visualId = pack.VisualIdAt(x, y);
                var expansionIndex = checked((visualId * metatileCells + coordinate.SubcellIndex) * descriptor.TargetCellStride);
                result[x * descriptor.HardwareHeight + y] = pack.TargetExpansions.Span[expansionIndex];
            }
        }

        return result;
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
    private const int PackedColumnPayloadTiles = 19;

    private sealed record DecoderLabels(
        string Raw,
        string Rle,
        string ReadSourceByte,
        string Success,
        string Malformed);


    internal static void EmitInitializeState(GbBuilder builder, GameBoyWorldPackRuntimePlan plan)
    {
        builder.XorA();
        foreach (var address in new ushort[]
                 {
                     GameBoyRuntimeMemoryLayout.Collision.QueryXLow,
                     GameBoyRuntimeMemoryLayout.Collision.QueryXHigh,
                     GameBoyRuntimeMemoryLayout.Collision.QueryYLow,
                     GameBoyRuntimeMemoryLayout.Collision.QueryYHigh,
                     GameBoyRuntimeMemoryLayout.Collision.SelectedSlot,
                     GameBoyRuntimeMemoryLayout.Collision.Cache0Valid,
                     GameBoyRuntimeMemoryLayout.Collision.Cache0ChunkLow,
                     GameBoyRuntimeMemoryLayout.Collision.Cache0ChunkHigh,
                     GameBoyRuntimeMemoryLayout.Collision.Cache1Valid,
                     GameBoyRuntimeMemoryLayout.Collision.Cache1ChunkLow,
                     GameBoyRuntimeMemoryLayout.Collision.Cache1ChunkHigh,
                     GameBoyRuntimeMemoryLayout.Collision.ReplacementNext,
                     GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow,
                     GameBoyRuntimeMemoryLayout.Collision.DecodeCountHigh,
                     GameBoyRuntimeMemoryLayout.Collision.CellValid,
                     GameBoyRuntimeMemoryLayout.Collision.CellXLow,
                     GameBoyRuntimeMemoryLayout.Collision.CellXHigh,
                     GameBoyRuntimeMemoryLayout.Collision.CellYLow,
                     GameBoyRuntimeMemoryLayout.Collision.CellYHigh,
                     GameBoyRuntimeMemoryLayout.Collision.CellResult,
                     GameBoyRuntimeMemoryLayout.Collision.GameplayTickCount,
                     GameBoyRuntimeMemoryLayout.Collision.PendingChunkLow,
                     GameBoyRuntimeMemoryLayout.Collision.PendingChunkHigh,
                     GameBoyRuntimeMemoryLayout.Collision.MemoHitCount,
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
            EmitEdgePreparationRuntime(builder, plan, layout, enablePackedAudioService);
        }
        builder.Label(runtimeDirectory);
        builder.Emit(plan.RuntimeDirectoryBytes);
    }

    private static void EmitEdgePreparationRuntime(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
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
        EmitJumpIfSlotAvailable(builder, GameBoyRuntimeMemoryLayout.PackedCamera.Slot0, selectSlot0);
        EmitJumpIfSlotAvailable(builder, GameBoyRuntimeMemoryLayout.PackedCamera.Slot1, selectSlot1);
        builder.JumpAbsolute(noSlot);

        builder.Label(selectSlot0);
        EmitSelectEdgeSlot(builder, plan.Layout.EdgeSlots[0], GameBoyRuntimeMemoryLayout.PackedCamera.Slot0, 0);
        builder.JumpAbsolute(prepare);

        builder.Label(selectSlot1);
        EmitSelectEdgeSlot(builder, plan.Layout.EdgeSlots[1], GameBoyRuntimeMemoryLayout.PackedCamera.Slot1, 1);

        builder.Label(prepare);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
        builder.PushAf();
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgePreparationBankSession);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeReuseReady);
        EmitIncrementCounter(builder, GameBoyRuntimeMemoryLayout.PackedCamera.RequestCount);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Requested);
        EmitWriteSelectedSlotMetadata(builder);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Preparing);
        EmitIncrementCounter(builder, GameBoyRuntimeMemoryLayout.PackedCamera.PrepareCount);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.CompareImmediate((byte)GameBoyWorldPackResult.Success);
        builder.JumpAbsolute(0xC2, validationFailed);
        if (layout.WorldPackColumnPlanePlacement is { } columnPlanePlacement)
        {
            var genericPreparation = builder.CreateLabel("worldpack_edge_generic_preparation");
            var columnPlaneBounds = builder.CreateLabel("worldpack_column_plane_bounds");
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
            builder.CompareImmediate(GameBoyPackedCameraRuntime.Column);
            builder.JumpAbsolute(0xC2, genericPreparation);
            EmitJumpIfWordOutside(
                builder,
                GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow,
                GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh,
                0,
                plan.Pack.Descriptor.HardwareWidth,
                columnPlaneBounds);
            EmitJumpIfWordOutside(
                builder,
                GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow,
                GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh,
                0,
                plan.Pack.Descriptor.HardwareHeight,
                columnPlaneBounds);
            builder.LoadAImmediate(PackedColumnPayloadTiles);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PayloadRemaining);
            EmitStoreSelectedSlotPayloadLength(builder);
            EmitColumnPlaneCopy(
                builder,
                plan.Pack.Descriptor.HardwareHeight,
                columnPlanePlacement,
                enablePackedAudioService,
                success);
            builder.Label(columnPlaneBounds);
            builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
            builder.JumpAbsolute(failed);
            builder.Label(genericPreparation);
        }

        GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
            builder,
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.LoadAImmediate(19);
        var lengthReady = builder.CreateLabel("worldpack_edge_length_ready");
        builder.JumpAbsolute(0xC2, lengthReady);
        builder.LoadAImmediate(21);
        builder.Label(lengthReady);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PayloadRemaining);
        EmitStoreSelectedSlotPayloadLength(builder);

        builder.Label(loop);
        if (enablePackedAudioService)
        {
            var skipAudioObservation = builder.CreateLabel("worldpack_edge_skip_audio_observation");
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PayloadRemaining);
            builder.AndImmediate(0x03);
            builder.JumpAbsolute(0xC2, skipAudioObservation);
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
            builder.Label(skipAudioObservation);
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeReuseReady);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, fullLookup);
        EmitTryAdvanceEdgeLookupState(builder, plan, fullLookup);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackVisualEdgeContinueLabel);
        builder.JumpAbsolute(inspectLookup);

        builder.Label(fullLookup);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSameMetatile);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xCA, rowLookup);

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        builder.LoadDFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.LoadHFromA();
        builder.JumpAbsolute(lookup);

        builder.Label(rowLookup);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.LoadDFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        builder.LoadHFromA();

        builder.Label(lookup);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackVisualEdgeLookupLabel);
        builder.Label(inspectLookup);
        builder.Emit(0xF5); // PUSH AF; status inspection must preserve the tile byte.
        builder.LoadAFromB();
        builder.CompareImmediate((byte)GameBoyWorldPackResult.Success);
        builder.JumpAbsolute(0xC2, lookupFailed);
        builder.Emit(0xF1); // POP AF
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.TileScratch);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.LoadHFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.TileScratch);
        builder.StoreHlA();
        builder.IncrementHl();
        builder.LoadAFromL();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationHigh);

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        var incrementColumnIterator = builder.CreateLabel("worldpack_edge_increment_column_iterator");
        var iteratorIncremented = builder.CreateLabel("worldpack_edge_iterator_incremented");
        builder.JumpAbsolute(0xC2, incrementColumnIterator);
        EmitIncrementIteratorModulo(builder, plan.Pack.Descriptor.HardwareWidth);
        builder.JumpAbsolute(iteratorIncremented);
        builder.Label(incrementColumnIterator);
        EmitIncrementIteratorModulo(builder, plan.Pack.Descriptor.HardwareHeight);
        builder.Label(iteratorIncremented);

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PayloadRemaining);
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PayloadRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, loop);
        builder.JumpAbsolute(success);

        builder.Label(lookupFailed);
        builder.Emit(0xF1); // POP AF
        builder.JumpAbsolute(failed);
        builder.Label(validationFailed);
        builder.Label(failed);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Released);
        EmitIncrementCounter(builder, GameBoyRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        EmitEndEdgePreparationBankSession(builder);
        builder.LoadAImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.Emit(0xC9); // RET

        builder.Label(success);
        if (enablePackedAudioService)
        {
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
        }

        EmitIncrementCounter(builder, GameBoyRuntimeMemoryLayout.PackedCamera.ResidentCount);
        EmitSetSelectedSlotState(builder, GameBoyPackedCameraRuntime.Resident);
        EmitEndEdgePreparationBankSession(builder);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9); // RET

        builder.Label(noSlot);
        builder.LoadAImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Miss);
        builder.Emit(0xC9); // RET
    }

    private static void EmitColumnPlaneCopy(
        GbBuilder builder,
        int hardwareHeight,
        GameBoyWorldPackColumnPlanePlacement placement,
        bool enablePackedAudioService,
        string success)
    {
        var noWrap = builder.CreateLabel("worldpack_column_plane_no_wrap");
        var wraps = builder.CreateLabel("worldpack_column_plane_wraps");
        var countsReady = builder.CreateLabel("worldpack_column_plane_counts_ready");
        var noSecondBlock = builder.CreateLabel("worldpack_column_plane_no_second_block");
        var copied = builder.CreateLabel("worldpack_column_plane_copied");
        var lastNonWrappingStartRow = hardwareHeight - PackedColumnPayloadTiles;

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        builder.LoadCFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        builder.LoadBFromA();
        EmitMultiplyBcByConstantToHl(builder, hardwareHeight);
        builder.LoadDe(placement.Address);
        builder.AddHlDe();
        builder.PushHl(); // Column base for the optional wrapped second block.
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.LoadDFromA();
        builder.AddHlDe();

        if (enablePackedAudioService)
        {
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
        }

        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitIfInVBlankLabel);
        builder.LoadAImmediate(placement.Bank);
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder);

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.CompareImmediate(lastNonWrappingStartRow >> 8);
        builder.JumpAbsolute(0xDA, noWrap);
        builder.JumpAbsolute(0xC2, wraps);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.CompareImmediate(lastNonWrappingStartRow & 0xFF);
        builder.JumpAbsolute(0xDA, noWrap);
        builder.JumpAbsolute(0xCA, noWrap);

        builder.Label(wraps);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.LoadCFromA();
        builder.LoadAImmediate(hardwareHeight & 0xFF);
        builder.SubtractAFromC();
        builder.LoadBFromA();
        builder.LoadAImmediate(PackedColumnPayloadTiles);
        builder.SubtractB();
        builder.LoadCFromA();
        builder.JumpAbsolute(countsReady);

        builder.Label(noWrap);
        builder.Emit(0x06, PackedColumnPayloadTiles); // LD B,n: first contiguous block.
        builder.Emit(0x0E, 0);  // LD C,0: no wrapped second block.
        builder.Label(countsReady);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.LoadDFromA();
        EmitColumnPlaneBlockCopy(builder, "worldpack_column_plane_first_block");

        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, noSecondBlock);
        builder.PopHl();
        if (enablePackedAudioService)
        {
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
        }

        builder.LoadAFromC();
        builder.LoadBFromA();
        EmitColumnPlaneBlockCopy(builder, "worldpack_column_plane_second_block");
        builder.JumpAbsolute(copied);

        builder.Label(noSecondBlock);
        builder.PopHl();
        builder.Label(copied);
        EmitAdvanceColumnPlaneIterator(builder, hardwareHeight);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PayloadRemaining);
        builder.LoadAFromE();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadAFromD();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.JumpAbsolute(success);
    }

    private static void EmitColumnPlaneBlockCopy(GbBuilder builder, string labelPrefix)
    {
        var loop = builder.CreateLabel(labelPrefix);
        builder.Label(loop);
        builder.LoadAFromHl();
        builder.Emit(0x12); // LD (DE),A
        builder.IncrementHl();
        builder.Emit(0x13); // INC DE
        builder.Emit(0x05); // DEC B
        builder.JumpAbsolute(0xC2, loop);
    }

    private static void EmitAdvanceColumnPlaneIterator(GbBuilder builder, int hardwareHeight)
    {
        var subtractHeight = builder.CreateLabel("worldpack_column_plane_iterator_subtract_height");
        var ready = builder.CreateLabel("worldpack_column_plane_iterator_ready");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.AddAImmediate(PackedColumnPayloadTiles);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.AdcAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.CompareImmediate(hardwareHeight >> 8);
        builder.JumpAbsolute(0xDA, ready);
        builder.JumpAbsolute(0xC2, subtractHeight);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.CompareImmediate(hardwareHeight & 0xFF);
        builder.JumpAbsolute(0xDA, ready);
        builder.Label(subtractHeight);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.SubtractAImmediate(hardwareHeight & 0xFF);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.SbcAImmediate(hardwareHeight >> 8);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.Label(ready);
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

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xC2, advanceColumn);

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellX);
        builder.AddAImmediate(1);
        builder.CompareImmediate(descriptor.MetatileWidth);
        builder.JumpAbsolute(0xDA, rowWithinMetatile);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellX);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeLocalX);
        builder.AddAImmediate(1);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeValidWidth);
        builder.CompareB();
        builder.JumpAbsolute(0xCA, fullLookup);
        builder.LoadAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeLocalX);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeCellIndex);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeCellIndex);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSameMetatile);
        builder.JumpAbsolute(rowWithinChunk);

        builder.Label(rowWithinMetatile);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellX);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSameMetatile);
        EmitAdvanceEdgeExpansionAddress(builder, 1);
        builder.JumpAbsolute(ready);
        builder.Label(rowWithinChunk);
        builder.JumpAbsolute(ready);

        builder.Label(advanceColumn);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellY);
        builder.AddAImmediate(1);
        builder.CompareImmediate(descriptor.MetatileHeight);
        builder.JumpAbsolute(0xDA, columnWithinMetatile);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellY);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeLocalY);
        builder.AddAImmediate(1);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeValidHeight);
        builder.CompareB();
        builder.JumpAbsolute(0xCA, fullLookup);
        builder.LoadAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeLocalY);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeCellIndex);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeValidWidth);
        builder.AddAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeCellIndex);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSameMetatile);
        builder.JumpAbsolute(columnWithinChunk);

        builder.Label(columnWithinMetatile);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellY);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSameMetatile);
        EmitAdvanceEdgeExpansionAddress(builder, descriptor.MetatileWidth);
        builder.JumpAbsolute(ready);
        builder.Label(columnWithinChunk);
        builder.Label(ready);
    }

    private static void EmitAdvanceEdgeExpansionAddress(GbBuilder builder, int delta)
    {
        var done = builder.CreateLabel("worldpack_edge_expansion_advanced");
        var wrapBank = builder.CreateLabel("worldpack_edge_expansion_wrap_bank");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressLow);
        builder.AddAImmediate(delta);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressHigh);
        builder.AdcAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressHigh);
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xCA, wrapBank);
        builder.JumpAbsolute(done);
        builder.Label(wrapBank);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionBank);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        builder.Emit(0x3C); // INC A
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionBank);
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder, instrumentPackedCameraCritical: true);
        builder.LoadAImmediate(0x40);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressHigh);
        builder.Label(done);
    }

    private static void EmitEndEdgePreparationBankSession(GbBuilder builder)
    {
        var restored = builder.CreateLabel("worldpack_edge_bank_restored");
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgePreparationBankSession);
        builder.PopAf();
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.LoadAImmediate(payload.Start & 0xFF);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadAImmediate(payload.Start >> 8);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DestinationHigh);
    }

    private static void EmitWriteSelectedSlotMetadata(GbBuilder builder)
    {
        var slot1 = builder.CreateLabel("worldpack_edge_write_slot_1_metadata");
        var done = builder.CreateLabel("worldpack_edge_write_metadata_done");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        EmitWriteSlotMetadata(builder, GameBoyRuntimeMemoryLayout.PackedCamera.Slot0);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        EmitWriteSlotMetadata(builder, GameBoyRuntimeMemoryLayout.PackedCamera.Slot1);
        builder.Label(done);
    }

    private static void EmitWriteSlotMetadata(GbBuilder builder, ushort metadata)
    {
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis, metadata + GameBoyPackedCameraRuntime.AxisOffset);
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.CommitDirection, metadata + GameBoyPackedCameraRuntime.DirectionOffset);
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, metadata + GameBoyPackedCameraRuntime.WorldEdgeLowOffset);
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, metadata + GameBoyPackedCameraRuntime.WorldEdgeHighOffset);
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.CommitTarget, metadata + GameBoyPackedCameraRuntime.TargetOffset);
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.CommitTargetStart, metadata + GameBoyPackedCameraRuntime.TargetStartOffset);
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow, metadata + GameBoyPackedCameraRuntime.OrthogonalLowOffset);
        EmitCopyByte(builder, GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh, metadata + GameBoyPackedCameraRuntime.OrthogonalHighOffset);
    }

    private static void EmitStoreSelectedSlotPayloadLength(GbBuilder builder)
    {
        var slot1 = builder.CreateLabel("worldpack_edge_length_slot_1");
        var done = builder.CreateLabel("worldpack_edge_length_done");
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.Slot0 + GameBoyPackedCameraRuntime.PayloadLengthOffset);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        builder.LoadAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.Slot1 + GameBoyPackedCameraRuntime.PayloadLengthOffset);
        builder.Label(done);
    }

    private static void EmitSetSelectedSlotState(GbBuilder builder, byte state)
    {
        var slot1 = builder.CreateLabel("worldpack_edge_state_slot_1");
        var done = builder.CreateLabel("worldpack_edge_state_done");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadAImmediate(state);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.Slot0 + GameBoyPackedCameraRuntime.StateOffset);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        builder.LoadAImmediate(state);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.Slot1 + GameBoyPackedCameraRuntime.StateOffset);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.JumpAbsolute(0xD2, noCarry);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.Label(noCarry);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.CompareImmediate((modulo >> 8) & 0xFF);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.CompareImmediate(modulo & 0xFF);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
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
        EmitJumpIfWordOutside(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, 0, descriptor.HardwareWidth, bounds);
        EmitJumpIfWordOutside(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh, 0, descriptor.HardwareHeight, bounds);
        EmitPrepareLookupState(
            builder,
            plan,
            enableStagedCamera,
            guardLookupState: false);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkHigh);
        for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            var next = slot + 1 < cacheChecks.Length ? cacheChecks[slot + 1] : decodeChunk;
            builder.Label(cacheChecks[slot]);
            builder.LoadA(VisualCacheValidAddress(slot));
            builder.CompareImmediate(1);
            builder.JumpAbsolute(0xC2, next);
            builder.LoadA(VisualCacheChunkLowAddress(slot));
            builder.LoadBFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkLow);
            builder.CompareB();
            builder.JumpAbsolute(0xC2, next);
            builder.LoadA(VisualCacheChunkHighAddress(slot));
            builder.LoadBFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkHigh);
            builder.CompareB();
            builder.JumpAbsolute(0xCA, cacheHits[slot]);
            builder.JumpAbsolute(next);
        }

        builder.Label(decodeChunk);
        GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
            builder,
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);
        if (enableDiagonalVisualCache)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkLow);
            builder.LoadEFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkHigh);
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

                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
                builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
                builder.JumpAbsolute(0xCA, rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: false);
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupLow);
                builder.JumpAbsolute(compareCurrent);

                builder.Label(rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: true);
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupLow);

                builder.Label(compareCurrent);
                builder.LoadBFromA();
                builder.LoadAFromD();
                builder.Emit(0xB9); // CP C
                builder.JumpAbsolute(0xC2, consider);
                builder.LoadAFromE();
                builder.CompareB();
                builder.JumpAbsolute(0xCA, next);

                builder.Label(consider);
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
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
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
                builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
                builder.JumpAbsolute(0xCA, rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: false);
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupLow);
                builder.JumpAbsolute(compare);
                builder.Label(rowAxis);
                EmitLoadVisualCacheGroup(builder, slot, row: true);
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupHigh);
                builder.LoadCFromA();
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupLow);
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
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualReplacementNext);
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
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
            builder.LoadAImmediate((slot + 1) % plan.Layout.VisualSlots.Count);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualReplacementNext);
            builder.LoadDe(plan.Layout.VisualSlots[slot].Start);
            builder.JumpAbsolute(decodeReady);
        }

        builder.Label(decodeReady);
        builder.Emit(0xD5); // PUSH DE; source preparation uses DE.
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 0, enableStagedCamera);
        builder.Emit(0xD1); // POP DE
        EmitSetValidationOnly(builder, validationOnly: false);
        EmitCallSelectedDecoder(builder, rawDecoder, rleDecoder, returnStatus);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
        for (var slot = 1; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            builder.CompareImmediate(slot);
            builder.JumpAbsolute(0xCA, cacheUpdates[slot]);
        }

        builder.JumpAbsolute(cacheUpdates[0]);
        for (var slot = 0; slot < plan.Layout.VisualSlots.Count; slot++)
        {
            builder.Label(cacheUpdates[slot]);
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkLow);
            builder.StoreA(VisualCacheChunkLowAddress(slot));
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.PendingVisualChunkHigh);
            builder.StoreA(VisualCacheChunkHighAddress(slot));
            builder.LoadAImmediate(1);
            builder.StoreA(VisualCacheValidAddress(slot));
            if (enableDiagonalVisualCache)
            {
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupLow);
                builder.StoreA(VisualCacheRowGroupLowAddress(slot));
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupHigh);
                builder.StoreA(VisualCacheRowGroupHighAddress(slot));
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupLow);
                builder.StoreA(VisualCacheColumnGroupLowAddress(slot));
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupHigh);
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
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
            builder.JumpAbsolute(chunkReady);
        }

        builder.Label(chunkReady);
        if (enableDiagonalVisualCache)
        {
            builder.JumpAbsolute(0xCD, recordCacheGroup);
        }
        var skipEdgeReuse = builder.CreateLabel("worldpack_visual_skip_edge_reuse");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgePreparationBankSession);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, skipEdgeReuse);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeVisualSlot);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeReuseReady);
        builder.Label(skipEdgeReuse);
        var standardExpansion = builder.CreateLabel("worldpack_visual_standard_expansion");
        var expansionDone = builder.CreateLabel("worldpack_visual_expansion_done");
        var edgeExpansionMalformed = builder.CreateLabel("worldpack_visual_edge_expansion_malformed");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgePreparationBankSession);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, standardExpansion);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackVisualEdgeExpansionLabel);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.LoadAFromB();
        builder.CompareImmediate((byte)GameBoyWorldPackResult.Success);
        builder.JumpAbsolute(0xC2, returnStatus);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSameMetatile);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, newMetatile);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressHigh);
        builder.LoadHFromA();
        builder.LoadAFromHl();
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
        builder.Label(newMetatile);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeCellIndex);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellY);
        builder.LoadCFromA();
        builder.Emit(0x06, 0x00); // LD B,0
        EmitMultiplyBcByConstantToHl(builder, descriptor.MetatileWidth);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellX);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndex);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndexHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeVisualSlot);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndexHigh);
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
            builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            builder.CompareImmediate(first.Bank);
            builder.JumpAbsolute(0xCA, bankReady);
            builder.LoadAImmediate(first.Bank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, instrumentPackedCameraCritical: true);
            builder.Label(bankReady);
            builder.LoadAImmediate(first.Bank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
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
        EmitJumpIfWordOutside(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, 0, descriptor.HardwareWidth, bounds);
        EmitJumpIfWordOutside(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh, 0, descriptor.HardwareHeight, bounds);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow); // high byte of the 24-bit pack-relative expansion offset
        builder.LoadDe(checked((ushort)metatileCells));
        var multiplyLoop = builder.CreateLabel("worldpack_visual_expansion_multiply");
        var multiplyDone = builder.CreateLabel("worldpack_visual_expansion_multiply_done");
        builder.Label(multiplyLoop);
        builder.LoadAFromD();
        builder.Emit(0xB3); // OR E
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, multiplyDone);
        builder.AddHlBc();
        EmitIncrementScratchOnCarry(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
        builder.Emit(0x1B); // DEC DE
        builder.JumpAbsolute(multiplyLoop);
        builder.Label(multiplyDone);

        builder.LoadDe((ushort)(descriptor.TargetExpansionsOffset & 0xFFFF));
        builder.AddHlDe();
        EmitIncrementScratchOnCarry(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
        builder.AddAImmediate((int)((descriptor.TargetExpansionsOffset >> 16) & 0xFF));
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndexHigh);
        builder.LoadDFromA();
        builder.AddHlDe();
        EmitIncrementScratchOnCarry(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);

        if (layout.WorldPackPlacement is { } placement)
        {
            builder.LoadDe(checked((ushort)(placement.BaseAddress - 0x4000)));
            builder.AddHlDe();
            EmitIncrementScratchOnCarry(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
            var bankSession = builder.CreateLabel("worldpack_expansion_bank_session");
            var bankReady = builder.CreateLabel("worldpack_expansion_bank_ready");
            var valueReady = builder.CreateLabel("worldpack_expansion_value_ready");
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgePreparationBankSession);
            builder.CompareImmediate(1);
            builder.JumpAbsolute(0xCA, bankSession);
            builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            builder.PushAf();
            builder.Label(bankSession);
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
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
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
            builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            builder.LoadBFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
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
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgePreparationBankSession);
            builder.CompareImmediate(1);
            builder.JumpAbsolute(0xCA, valueReady);
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
            builder.Label(valueReady);
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
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
        0 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheValid,
        1 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1Valid,
        2 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2Valid,
        3 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3Valid,
        4 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4Valid,
        5 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5Valid,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheChunkLowAddress(int slot) => slot switch
    {
        0 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheChunkLow,
        1 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1ChunkLow,
        2 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2ChunkLow,
        3 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3ChunkLow,
        4 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4ChunkLow,
        5 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5ChunkLow,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheChunkHighAddress(int slot) => slot switch
    {
        0 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheChunkHigh,
        1 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1ChunkHigh,
        2 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2ChunkHigh,
        3 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3ChunkHigh,
        4 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4ChunkHigh,
        5 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5ChunkHigh,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheAgeAddress(int slot) => slot switch
    {
        0 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache0Age,
        1 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1Age,
        2 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2Age,
        3 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3Age,
        4 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4Age,
        5 => GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5Age,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private static ushort VisualCacheRowGroupLowAddress(int slot) =>
        checked((ushort)(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheRowGroupLowStart + slot));

    private static ushort VisualCacheRowGroupHighAddress(int slot) =>
        checked((ushort)(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheRowGroupHighStart + slot));

    private static ushort VisualCacheColumnGroupLowAddress(int slot) =>
        checked((ushort)(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheColumnGroupLowStart + slot));

    private static ushort VisualCacheColumnGroupHighAddress(int slot) =>
        checked((ushort)(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheColumnGroupHighStart + slot));

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
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
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
        EmitDivideDeByConstant(builder, chunkColumns, GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.LoadAFromE();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupLow);
        builder.LoadAFromD();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingRowGroupHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupLow);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualPendingColumnGroupHigh);
        builder.Emit(0xC9);
    }

    private static void EmitIsVisualCacheGroupProtected(GbBuilder builder)
    {
        var protectColumn = builder.CreateLabel("worldpack_visual_protect_column_group");
        var compare = builder.CreateLabel("worldpack_visual_compare_protected_group");
        var notProtected = builder.CreateLabel("worldpack_visual_group_not_protected");
        var done = builder.CreateLabel("worldpack_visual_group_protection_done");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xCA, protectColumn);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastRowGroupValid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastRowGroupHigh);
        builder.LoadBFromA();
        builder.LoadAFromD();
        builder.CompareB();
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastRowGroupLow);
        builder.JumpAbsolute(compare);

        builder.Label(protectColumn);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastColumnGroupValid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastColumnGroupHigh);
        builder.LoadBFromA();
        builder.LoadAFromD();
        builder.CompareB();
        builder.JumpAbsolute(0xC2, notProtected);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastColumnGroupLow);

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
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualSelectedSlot);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Row);
        builder.JumpAbsolute(0xCA, row);
        builder.LoadHl(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheColumnGroupLowStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastColumnGroupLow);
        builder.LoadHl(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheColumnGroupHighStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastColumnGroupHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastColumnGroupValid);
        builder.JumpAbsolute(done);
        builder.Label(row);
        builder.LoadHl(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheRowGroupLowStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastRowGroupLow);
        builder.LoadHl(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheRowGroupHighStart);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastRowGroupHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.VisualLastRowGroupValid);
        builder.Label(done);
        builder.Emit(0xC9);
    }

    private static void EmitRememberEdgeExpansionAddress(GbBuilder builder, bool banked)
    {
        var done = builder.CreateLabel("worldpack_expansion_address_not_needed");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgePreparationBankSession);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadAFromL();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionAddressHigh);
        if (banked)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
        }
        else
        {
            builder.XorA();
        }

        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeExpansionBank);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.CellValid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'E', GameBoyRuntimeMemoryLayout.Collision.CellXLow, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'D', GameBoyRuntimeMemoryLayout.Collision.CellXHigh, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'L', GameBoyRuntimeMemoryLayout.Collision.CellYLow, cellMiss);
        EmitJumpIfRegisterDoesNotMatch(builder, 'H', GameBoyRuntimeMemoryLayout.Collision.CellYHigh, cellMiss);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.CellResult);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);

        builder.Label(cellMiss);
        if (plan.Layout.CollisionMemoTable is { } collisionMemoTable)
        {
            EmitJumpIfRegisterWordAtOrAbove(builder, 'D', 'E', descriptor.HardwareWidth, lookupBounds);
            EmitJumpIfRegisterWordAtOrAbove(builder, 'H', 'L', descriptor.HardwareHeight, lookupBounds);
            builder.LoadAFromE();
            builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
            builder.LoadAFromD();
            builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.QueryXHigh);
            builder.LoadAFromL();
            builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.QueryYLow);
            builder.LoadAFromH();
            builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.QueryYHigh);
            EmitTryCollisionMemoTableLookup(builder, collisionMemoTable);
        }
        else
        {
            EmitStoreLookupCoordinates(builder);
            CopyByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
            CopyByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, GameBoyRuntimeMemoryLayout.Collision.QueryXHigh);
            CopyByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow, GameBoyRuntimeMemoryLayout.Collision.QueryYLow);
            CopyByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh, GameBoyRuntimeMemoryLayout.Collision.QueryYHigh);
            EmitJumpIfWordOutside(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, 0, descriptor.HardwareWidth, lookupBounds);
            EmitJumpIfWordOutside(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow, GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh, 0, descriptor.HardwareHeight, lookupBounds);
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
        CopyByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, GameBoyRuntimeMemoryLayout.Collision.PendingChunkLow);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, GameBoyRuntimeMemoryLayout.Collision.PendingChunkHigh);

        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, checkCache1);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyRuntimeMemoryLayout.Collision.Cache0ChunkLow,
            GameBoyRuntimeMemoryLayout.Collision.PendingChunkLow,
            checkCache1);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyRuntimeMemoryLayout.Collision.Cache0ChunkHigh,
            GameBoyRuntimeMemoryLayout.Collision.PendingChunkHigh,
            checkCache1);
        builder.JumpAbsolute(cache0);

        builder.Label(checkCache1);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, selectSlot);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyRuntimeMemoryLayout.Collision.Cache1ChunkLow,
            GameBoyRuntimeMemoryLayout.Collision.PendingChunkLow,
            selectSlot);
        EmitJumpIfBytesDiffer(
            builder,
            GameBoyRuntimeMemoryLayout.Collision.Cache1ChunkHigh,
            GameBoyRuntimeMemoryLayout.Collision.PendingChunkHigh,
            selectSlot);
        builder.JumpAbsolute(cache1);

        builder.Label(selectSlot);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, selectSlot0);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, selectSlot1);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.ReplacementNext);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, selectSlot0);
        builder.JumpAbsolute(selectSlot1);

        builder.Label(selectSlot0);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);
        builder.JumpAbsolute(rawDirect);
        builder.Label(selectSlot1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);

        builder.Label(rawDirect);
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 6, enableStagedCamera);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
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
            GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow,
            GameBoyRuntimeMemoryLayout.Collision.DecodeCountHigh);
        EmitInvalidateSelectedCollisionSlot(builder);
        EmitLoadSelectedCollisionSlotDestination(builder, plan.Layout.CollisionSlots);
        EmitSetValidationOnly(builder, validationOnly: false);
        builder.JumpAbsolute(0xCD, decoders.Rle);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, lookupReturnStatus);
        builder.JumpAbsolute(rleDecoded);

        builder.Label(rleDecoded);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, publishSlot1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.PendingChunkLow, GameBoyRuntimeMemoryLayout.Collision.Cache0ChunkLow);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.PendingChunkHigh, GameBoyRuntimeMemoryLayout.Collision.Cache0ChunkHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.ReplacementNext);
        builder.JumpAbsolute(cache0);

        builder.Label(publishSlot1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.PendingChunkLow, GameBoyRuntimeMemoryLayout.Collision.Cache1ChunkLow);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.PendingChunkHigh, GameBoyRuntimeMemoryLayout.Collision.Cache1ChunkHigh);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.ReplacementNext);
        builder.JumpAbsolute(cache1);

        builder.Label(cache0);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);
        EmitLoadSelectedCollisionId(builder, descriptor, plan.Layout.CollisionSlots);
        builder.JumpAbsolute(idReady);
        builder.Label(cache1);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);
        EmitLoadSelectedCollisionId(builder, descriptor, plan.Layout.CollisionSlots);

        builder.Label(idReady);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.LoadCFromA();
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
            builder.LoadBFromA();
        }
        else
        {
            builder.Emit(0x06, 0x00); // LD B,0
        }

        EmitJumpIfIdOutsideRange(builder, descriptor.CollisionIdBytes, descriptor.CollisionProfileCount, lookupMalformed);
        EmitCollisionProfileLookupFromScratchId(builder, plan, profileBytes);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.CellResult);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryYLow);
        builder.AndImmediate(0x01);
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
        builder.AndImmediate(0x01);
        builder.AddAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndex);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndexHigh);

        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXHigh);
        builder.LoadDFromA();
        for (var shift = 0; shift < 4; shift++)
        {
            EmitShiftRightDe(builder);
        }

        builder.LoadAFromE();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank); // Reduced-path alias: chunk X, not a ROM bank.

        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryYLow);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
        builder.AddAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);

        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryYLow);
        builder.ShiftRightLogicalA();
        builder.AndImmediate(0x07);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining); // Reduced-path alias: local metatile Y.
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
        builder.ShiftRightLogicalA();
        builder.AndImmediate(0x07);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh); // Reduced-path alias: local metatile X.

        var clippedWidth = builder.CreateLabel("worldpack_fast_collision_clipped_width");
        var cellReady = builder.CreateLabel("worldpack_fast_collision_cell_ready");
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
        builder.CompareImmediate(descriptor.ChunkColumns - 1);
        builder.JumpAbsolute(0xCA, clippedWidth);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
        builder.AddAFromA();
        builder.AddAFromA();
        builder.AddAFromA();
        builder.JumpAbsolute(cellReady);
        builder.Label(clippedWidth);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
        builder.AddAFromA();
        builder.AddAFromA();
        builder.Label(cellReady);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh);
        builder.AddAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
        builder.CompareB();
        builder.JumpAbsolute(0xC2, miss);
        builder.IncrementHl();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        EmitLoadCollisionMemoHighTag(builder);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.CompareB();
        builder.JumpAbsolute(0xC2, miss);
        builder.IncrementHl();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.CellResult);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.MemoHitCount);
        builder.Emit(0x3C); // INC A
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.MemoHitCount);
        EmitPublishExactCollisionCellAndReturnSuccess(builder);
        builder.Label(miss);
    }

    private static void EmitPublishExactCollisionCellAndReturnSuccess(GbBuilder builder)
    {
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.QueryXLow, GameBoyRuntimeMemoryLayout.Collision.CellXLow);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.QueryXHigh, GameBoyRuntimeMemoryLayout.Collision.CellXHigh);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.QueryYLow, GameBoyRuntimeMemoryLayout.Collision.CellYLow);
        CopyByte(builder, GameBoyRuntimeMemoryLayout.Collision.QueryYHigh, GameBoyRuntimeMemoryLayout.Collision.CellYHigh);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.CellValid);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.CellResult);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
    }

    private static void EmitPublishCollisionMemoTable(
        GbBuilder builder,
        GameBoyWramRange memoTable)
    {
        EmitLoadCollisionMemoTablePointer(builder, memoTable);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
        builder.StoreHlA();
        builder.IncrementHl();
        EmitLoadCollisionMemoHighTag(builder);
        builder.StoreHlA();
        builder.IncrementHl();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.CellResult);
        builder.StoreHlA();
    }

    private static void EmitLoadCollisionMemoTablePointer(
        GbBuilder builder,
        GameBoyWramRange memoTable)
    {
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXLow);
        builder.AndImmediate(0x1F);
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryYLow);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryYLow);
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.QueryXHigh);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid);
        builder.JumpAbsolute(ready);
        builder.Label(slot1);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid);
        builder.Label(ready);
    }

    private static void EmitLoadSelectedCollisionId(
        GbBuilder builder,
        WorldPackDescriptor descriptor,
        IReadOnlyList<GameBoyWramRange> slots)
    {
        var slot1 = builder.CreateLabel("worldpack_collision_id_slot_1");
        var ready = builder.CreateLabel("worldpack_collision_id_slot_ready");
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.AddAFromA();
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadHl(slots[0].Start);
        builder.JumpAbsolute(ready);
        builder.Label(slot1);
        builder.LoadHl(slots[1].Start);
        builder.Label(ready);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.IncrementHl();
            builder.LoadAFromHl();
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        }
        else
        {
            builder.XorA();
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.IncrementHl();
            if (layout.UsesBankedWorldPack)
            {
                EmitAdvanceSourceBankIfNeeded(builder, enableStagedCamera);
            }

            builder.LoadAFromHl();
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        }
        else
        {
            builder.XorA();
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.LoadCFromA();
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndexHigh);
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
            builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid);
        }

        if (enableStagedCamera)
        {
            GameBoyPackedCameraRuntimeEmitter.EmitWaitOutsideVBlank(builder);
            GameBoyPackedCameraRuntimeEmitter.EmitRecordCriticalWork(
                builder,
                GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.LoadAFromC();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.CompareImmediate(2);
        builder.JumpAbsolute(0xD2, bounds);

        EmitJumpIfWordOutside(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, 0, plan.Pack.Chunks.Count, miss);
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
                GameBoyRuntimeMemoryLayout.PackedCamera.DirectoryWorkInVBlank);
        }

        EmitRuntimeDirectoryPointer(builder, runtimeDirectory, planeEntryOffset);
        builder.Emit(0x2A); // LD A,(HL+): offset low
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.Emit(0x2A); // offset middle
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.Emit(0x2A); // offset high
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
        builder.Emit(0x2A); // stored bytes
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
        builder.Emit(0x2A); // decoded elements
        builder.LoadCFromA();
        builder.LoadAFromHl(); // codec
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);

        if (layout.WorldPackPlacement is { } placement)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
            builder.LoadLFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
            builder.LoadHFromA();
            builder.LoadDe(checked((ushort)(placement.BaseAddress - 0x4000)));
            builder.AddHlDe();
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
            builder.AdcAImmediate(0);
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
            builder.LoadAFromH();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.LoadBFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
            builder.AddAFromA();
            builder.AddAFromA();
            builder.AddAFromB();
            builder.AddAImmediate(placement.BaseBank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
            builder.LoadAFromH();
            builder.AndImmediate(0x3F);
            builder.OrImmediate(0x40);
            builder.LoadHFromA();
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.WorldPackLabel);
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
            builder.LoadEFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
            builder.LoadDFromA();
            builder.AddHlDe();
            builder.LoadAImmediate(0);
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
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
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);
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
                EmitReadSafeStagedByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, banked, malformed, trackStoredBytes: false);
                EmitReadSafeStagedByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, banked, malformed, trackStoredBytes: false);
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
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);
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
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
        builder.Emit(0x3C); // INC A
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.AndImmediate(0x80);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, runPacket);

        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        EmitRequirePacketFits(builder, malformed);
        builder.Label(literalLoop);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, packetDone);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);
        EmitWriteDecodedId(builder, idBytes, enableStagedCamera: false);

        builder.Emit(0x0D); // DEC C
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.DecrementA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.JumpAbsolute(literalLoop);

        builder.Label(runPacket);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.AndImmediate(0x7F);
        builder.AddAImmediate(3);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        EmitRequirePacketFits(builder, malformed);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);

        builder.Label(runLoop);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, packetDone);
        EmitWriteDecodedId(builder, idBytes, enableStagedCamera: false);

        builder.Emit(0x0D); // DEC C
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.DecrementA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
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
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);
        EmitBeginDecode(builder, banked, enableStagedCamera: true);
        builder.Label(packetLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        GameBoyPackedCameraRuntimeEmitter.EmitWaitForSafeRleCriticalWork(builder);
        EmitReadSafeStagedByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount, banked, malformed, trackStoredBytes: true);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.AndImmediate(0x80);
        builder.JumpAbsolute(0xC2, runPacket);

        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
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
            GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit);
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
        EmitReadSafeStagedByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow, banked, malformed, trackStoredBytes);
        if (idBytes == 2)
        {
            EmitReadSafeStagedByte(builder, GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh, banked, malformed, trackStoredBytes);
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
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
            builder.CompareImmediate(0);
            builder.JumpAbsolute(0xCA, malformed);
            builder.DecrementA();
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
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
                builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
                builder.CompareImmediate(idCount);
                builder.JumpAbsolute(0xD2, malformed);
            }

            return;
        }

        var highIsLower = builder.CreateLabel("worldpack_staged_id_high_lower");
        var highMatches = builder.CreateLabel("worldpack_staged_id_high_matches");
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.CompareImmediate((idCount >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, highIsLower);
        builder.JumpAbsolute(0xCA, highMatches);
        builder.JumpAbsolute(malformed);
        builder.Label(highMatches);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.CompareImmediate(idCount & 0xFF);
        builder.JumpAbsolute(0xD2, malformed);
        builder.Label(highIsLower);
    }

    private static void EmitWriteSafeStagedId(GbBuilder builder, int idBytes)
    {
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.Emit(0x12); // LD (DE),A
        builder.Emit(0x13); // INC DE
        if (idBytes == 2)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
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

        builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
        builder.PushAf();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.IncrementHl();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
        builder.DecrementA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
        if (banked)
        {
            builder.LoadAFromH();
            builder.CompareImmediate(0x80);
            builder.JumpAbsolute(0xC2, noCross);
            builder.LoadHImmediate(0x40);
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
            builder.Emit(0x3C); // INC A
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder, enableStagedCamera);
            builder.Label(noCross);
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, malformed);
    }

    private static void EmitRequirePacketFits(GbBuilder builder, string malformed)
    {
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
        builder.LoadBFromA();
        builder.LoadAFromC();
        builder.CompareB();
        builder.JumpAbsolute(0xDA, malformed); // JP C when remaining < packet count
    }

    private static void EmitStoreLookupCoordinates(GbBuilder builder)
    {
        builder.LoadAFromE();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.LoadAFromD();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.LoadAFromL();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh);
    }

    private static void EmitPrepareLookupState(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        bool enableStagedCamera,
        bool guardLookupState)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.LoadDFromA();
        EmitDivideDeByConstant(builder, descriptor.MetatileWidth, GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);

        builder.Emit(0xD5); // PUSH DE; preserve metatile X while dividing Y
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh);
        builder.LoadDFromA();
        EmitDivideDeByConstant(builder, descriptor.MetatileHeight, GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.Emit(0x62); // LD H,D
        builder.Emit(0x6B); // LD L,E
        builder.Emit(0xD1); // POP DE

        builder.LoadAFromE();
        builder.AndImmediate(0x07);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh); // local metatile X
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeLocalX);
        }
        builder.LoadAFromL();
        builder.AndImmediate(0x07);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining); // local metatile Y
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeLocalY);
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellX);
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeSubcellY);
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
                GameBoyRuntimeMemoryLayout.PackedCamera.DirectoryWorkInVBlank);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeValidWidth);

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
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeValidHeight);
        }

        builder.Emit(0x44); // LD B,H (chunk Y high)
        builder.Emit(0x4D); // LD C,L (chunk Y low)
        EmitMultiplyBcByConstantToHl(builder, descriptor.ChunkColumns);

        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchStoredRemaining);
        builder.LoadCFromA();
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        var cellLoop = builder.CreateLabel("worldpack_cell_index_loop");
        var cellDone = builder.CreateLabel("worldpack_cell_index_done");
        builder.Label(cellLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, cellDone);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        builder.AddAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(cellLoop);
        builder.Label(cellDone);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh);
        builder.AddAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchCellIndex);
        if (enableStagedCamera)
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.EdgeCellIndex);
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchReadByte);
        builder.LoadCFromA();
        builder.Emit(0x06, 0x00); // LD B,0
        builder.LoadHl(0);
        for (var index = 0; index < descriptor.MetatileWidth; index++)
        {
            builder.AddHlBc();
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSourceBank);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndex);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchSubcellIndexHigh);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        if (idBytes == 2)
        {
            EmitRequireStoredByte(builder, malformed);
            builder.JumpAbsolute(0xCD, readSourceByte);
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
            var highIsLower = builder.CreateLabel("worldpack_id_high_lower");
            var highMatches = builder.CreateLabel("worldpack_id_high_matches");
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
            builder.CompareImmediate((idCount >> 8) & 0xFF);
            builder.JumpAbsolute(0xDA, highIsLower);
            builder.JumpAbsolute(0xCA, highMatches);
            builder.JumpAbsolute(malformed);
            builder.Label(highMatches);
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
            builder.CompareImmediate(idCount & 0xFF);
            builder.JumpAbsolute(0xD2, malformed);
            builder.Label(highIsLower);
        }
        else if (idCount < 256)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
            builder.CompareImmediate(idCount);
            builder.JumpAbsolute(0xD2, malformed);
        }
    }

    private static void EmitWriteDecodedId(GbBuilder builder, int idBytes, bool enableStagedCamera)
    {
        var done = builder.CreateLabel("worldpack_decode_skip_validation_write");
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXLow);
        builder.Emit(0x12); // LD (DE),A
        builder.Emit(0x13); // INC DE
        if (idBytes == 2)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchXHigh);
            builder.Emit(0x12);
            builder.Emit(0x13);
        }

        builder.Label(done);
    }

    private static void EmitSetValidationOnly(GbBuilder builder, bool validationOnly)
    {
        builder.LoadAImmediate(validationOnly ? 1 : 0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchYHigh);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState);
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
            builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState);
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState);
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
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
            builder.Label(loop);
            for (var index = 0; index < bytesPerBlock; index++)
            {
                EmitAccumulateFingerprintByte(builder, phase);
                phase = (phase + 1) & 15;
            }

            builder.LoadA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
            builder.DecrementA();
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ScratchPacketCount);
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

        var cursor = GameBoyRuntimeMemoryLayout.WorldPackStaging.Start;
        var visualSlots = CreateSlots(
            "WorldPack visual slot",
            visualIdBytes * ChunkCells,
            visualSlotCount,
            ref cursor);
        var collisionSlots = CreatePair("WorldPack collision slot", collisionIdBytes * ChunkCells, ref cursor);
        var edgeSlots = CreatePair("WorldPack edge slot", EdgeBytes, ref cursor);
        GameBoyWramRange? collisionMemoTable = enableCollisionMemoTable
            ? GameBoyRuntimeMemoryLayout.CollisionMemoState
            : null;
        var totalBytes = cursor - GameBoyRuntimeMemoryLayout.WorldPackStaging.Start;
        GameBoyRuntimeMemoryLayout.ValidateWorldPackStagingBytes(totalBytes);

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
