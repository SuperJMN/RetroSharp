namespace RetroSharp.NES;

internal static class NesPackedCameraRuntime
{
    internal const int SlotMetadataBytes = 16;

    internal const int StateOffset = 0;
    internal const int AxisOffset = 1;
    internal const int DirectionOffset = 2;
    internal const int WorldEdgeLowOffset = 3;
    internal const int WorldEdgeHighOffset = 4;
    internal const int TargetOffset = 5;
    internal const int OrthogonalLowOffset = 6;
    internal const int OrthogonalHighOffset = 7;
    internal const int PayloadLengthOffset = 8;
    internal const int RequestFrameLowOffset = 9;
    internal const int RequestFrameHighOffset = 10;
    internal const int ResidentFrameLowOffset = 11;
    internal const int ResidentFrameHighOffset = 12;
    internal const int CommitPhaseOffset = 13;
    internal const int TargetStartOffset = 14;
    internal const int PayloadCursorOffset = 15;

    internal const byte Empty = 0;
    internal const byte Requested = 1;
    internal const byte Preparing = 2;
    internal const byte Resident = 3;
    internal const byte Committing = 4;
    internal const byte Released = 5;

    internal const byte Column = 1;
    internal const byte Row = 2;
    internal const byte Negative = 1;
    internal const byte Positive = 2;
    internal const byte NoSlot = 0xFF;
    internal const byte CommitPpuPhase = 1;
    internal const byte CommitReadyToFinalize = 2;

    internal static ushort SlotMetadata(int slot) => slot switch
    {
        0 => NesRuntimeMemoryLayout.PackedCamera.Slot0,
        1 => NesRuntimeMemoryLayout.PackedCamera.Slot1,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}

internal static class NesPackedCameraRuntimeEmitter
{
    private const string BeginSelectedPhaseLabel = "nes_packed_begin_selected_phase";

    internal static void Emit(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesPackedCameraPhaseSchedule schedule)
    {
        EmitPrepareEdge(builder, plan);
        EmitCommitEdge(builder, plan, schedule);
        EmitFinalizeEdge(builder);
        EmitBeginSelectedPhaseRoutine(builder, plan);
        EmitReleaseReversedEdges(builder);
    }

    private static void EmitReleaseReversedEdges(PrgBuilder builder)
    {
        builder.Label(NesRomBuilder.WorldPackReleaseReversedEdgeLabel);
        EmitReleaseReversedSlot(builder, NesRuntimeMemoryLayout.PackedCamera.Slot0, "nes_packed_reverse_slot_0");
        EmitReleaseReversedSlot(builder, NesRuntimeMemoryLayout.PackedCamera.Slot1, "nes_packed_reverse_slot_1");
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
    }

    private static void EmitReleaseReversedSlot(PrgBuilder builder, ushort slot, string prefix)
    {
        var done = builder.CreateLabel($"{prefix}_done");
        builder.LoadAAbsolute(checked((ushort)(slot + NesPackedCameraRuntime.StateOffset)));
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xD0, done);
        builder.LoadAAbsolute(checked((ushort)(slot + NesPackedCameraRuntime.AxisOffset)));
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.JumpIf(0xD0, done);
        builder.LoadAAbsolute(checked((ushort)(slot + NesPackedCameraRuntime.DirectionOffset)));
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitDirection);
        builder.JumpIf(0xF0, done);
        builder.LoadAImmediate(NesPackedCameraRuntime.Released);
        builder.StoreAAbsolute(checked((ushort)(slot + NesPackedCameraRuntime.StateOffset)));
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        builder.Label(done);
    }

    private static void EmitCommitEdge(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesPackedCameraPhaseSchedule schedule)
    {
        var invalid = builder.CreateLabel("nes_packed_commit_invalid");
        var column = builder.CreateLabel("nes_packed_commit_column");
        var row = builder.CreateLabel("nes_packed_commit_row");

        builder.Label(NesRomBuilder.WorldPackCommitEdgeLabel);
        EmitSelectSlotForCommit(builder, invalid, schedule);
        EmitLoadPendingExpectedTags(builder);
        EmitValidateSelectedSlot(builder, invalid);
        EmitValidatePayloadLength(builder, invalid);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, column);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xF0, row);
        builder.JumpAbsolute(invalid);

        builder.Label(column);
        EmitCommitColumnPhase(builder, plan, schedule);

        builder.Label(row);
        EmitCommitRowPhase(builder, plan, schedule);

        builder.Label(invalid);
        builder.LoadAImmediate((byte)NesWorldPackResult.Miss);
        builder.Return();
    }

    private static void EmitCommitColumnPhase(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesPackedCameraPhaseSchedule schedule)
    {
        var attributes = builder.CreateLabel("nes_packed_column_phase_attributes");
        var attributesFit = builder.CreateLabel("nes_packed_column_phase_attributes_fit");
        var writeAttributes = builder.CreateLabel("nes_packed_column_phase_write_attributes");
        var tileCountWithinBudget = builder.CreateLabel("nes_packed_column_tile_count_budget");
        var topBoundary = builder.CreateLabel("nes_packed_column_top_boundary");
        var boundaryReady = builder.CreateLabel("nes_packed_column_boundary_ready");
        var countReady = builder.CreateLabel("nes_packed_column_tile_count_ready");
        var tileSegment = builder.CreateLabel("nes_packed_column_tile_segment");
        var tileLoop = builder.CreateLabel("nes_packed_column_tile_phase_loop");
        var tilesRemain = builder.CreateLabel("nes_packed_column_tiles_remain");
        var incomplete = builder.CreateLabel("nes_packed_column_phase_incomplete");
        var ready = builder.CreateLabel("nes_packed_column_phase_ready");
        var finish = builder.CreateLabel("nes_packed_column_phase_finish");

        builder.CallSubroutine(BeginSelectedPhaseLabel);
        builder.LoadAImmediate(schedule.MaximumWritesPerPhase);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        EmitLoadSelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.JumpIf(0xB0, attributes);

        builder.LoadAImmediate(0x84);
        builder.StoreAAbsolute(0x2000);
        builder.Label(tileSegment);
        EmitAddModulo(
            builder,
            NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart,
            NesRuntimeMemoryLayout.PackedCamera.Iterator,
            modulo: 60,
            NesRuntimeMemoryLayout.PackedCamera.TargetCursor,
            "nes_packed_column_target");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0x90, tileCountWithinBudget);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(tileCountWithinBudget);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.CompareImmediate(30);
        builder.JumpIf(0x90, topBoundary);
        builder.LoadAImmediate(60);
        builder.JumpAbsolute(boundaryReady);
        builder.Label(topBoundary);
        builder.LoadAImmediate(30);
        builder.Label(boundaryReady);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0xB0, countReady);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(countReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);

        EmitSetPpuTileAddress(
            builder,
            NesRuntimeMemoryLayout.PackedCamera.TargetCursor,
            NesRuntimeMemoryLayout.PackedCamera.CommitTarget,
            "nes_packed_column_phase_tile");
        builder.LoadYAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.TransferAToX();
        builder.Label(tileLoop);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.DecrementX();
        builder.JumpIf(0xD0, tileLoop);
        builder.TransferYToA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        EmitStoreASelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.JumpIf(0xD0, tilesRemain);
        builder.LoadAImmediate(32);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        EmitStoreASelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0xF0, incomplete);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        EmitComputeAttributeCount(builder);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.JumpIf(0x90, attributesFit);
        builder.JumpIf(0xF0, attributesFit);
        builder.JumpAbsolute(incomplete);
        builder.Label(attributesFit);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.JumpAbsolute(writeAttributes);

        builder.Label(tilesRemain);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0xD0, tileSegment);
        builder.JumpAbsolute(incomplete);

        builder.Label(attributes);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.Label(writeAttributes);
        EmitColumnAttributeWrites(builder, ready, incomplete);

        builder.Label(incomplete);
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(finish);
        builder.Label(ready);
        builder.LoadAImmediate(NesPackedCameraRuntime.CommitReadyToFinalize);
        builder.Label(finish);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        EmitIncrementSelectedMetadata(builder, NesPackedCameraRuntime.CommitPhaseOffset);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
    }

    private static void EmitCommitRowPhase(
        PrgBuilder builder,
        NesWorldPackRuntimePlan plan,
        NesPackedCameraPhaseSchedule schedule)
    {
        var attributes = builder.CreateLabel("nes_packed_row_phase_attributes");
        var attributesFit = builder.CreateLabel("nes_packed_row_phase_attributes_fit");
        var countWithinBudget = builder.CreateLabel("nes_packed_row_tile_count_budget");
        var leftBoundary = builder.CreateLabel("nes_packed_row_left_boundary");
        var boundaryReady = builder.CreateLabel("nes_packed_row_boundary_ready");
        var countReady = builder.CreateLabel("nes_packed_row_tile_count_ready");
        var tileSegment = builder.CreateLabel("nes_packed_row_tile_segment");
        var tileLoop = builder.CreateLabel("nes_packed_row_tile_phase_loop");
        var tilesRemain = builder.CreateLabel("nes_packed_row_tiles_remain");
        var writeAttributes = builder.CreateLabel("nes_packed_row_write_attributes");
        var incomplete = builder.CreateLabel("nes_packed_row_phase_incomplete");
        var ready = builder.CreateLabel("nes_packed_row_phase_ready");
        var finish = builder.CreateLabel("nes_packed_row_phase_finish");

        builder.CallSubroutine(BeginSelectedPhaseLabel);
        builder.LoadAImmediate(schedule.MaximumWritesPerPhase);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        EmitLoadSelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.JumpIf(0xB0, attributes);

        builder.LoadAImmediate(0x80);
        builder.StoreAAbsolute(0x2000);
        builder.Label(tileSegment);
        EmitAddModulo(
            builder,
            NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart,
            NesRuntimeMemoryLayout.PackedCamera.Iterator,
            modulo: 64,
            NesRuntimeMemoryLayout.PackedCamera.TargetCursor,
            "nes_packed_row_target");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0x90, countWithinBudget);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(countWithinBudget);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.CompareImmediate(32);
        builder.JumpIf(0x90, leftBoundary);
        builder.LoadAImmediate(64);
        builder.JumpAbsolute(boundaryReady);
        builder.Label(leftBoundary);
        builder.LoadAImmediate(32);
        builder.Label(boundaryReady);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0xB0, countReady);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(countReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);

        builder.LoadYAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        EmitSetPpuTileAddress(
            builder,
            NesRuntimeMemoryLayout.PackedCamera.CommitTarget,
            NesRuntimeMemoryLayout.PackedCamera.TargetCursor,
            "nes_packed_row_phase_tile");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.TransferAToX();
        builder.Label(tileLoop);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.DecrementX();
        builder.JumpIf(0xD0, tileLoop);
        builder.TransferYToA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        EmitStoreASelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.JumpIf(0xD0, tilesRemain);
        builder.LoadAImmediate(32);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        EmitStoreASelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0xF0, incomplete);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        EmitComputeAttributeCount(builder);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.JumpIf(0x90, attributesFit);
        builder.JumpIf(0xF0, attributesFit);
        builder.JumpAbsolute(incomplete);
        builder.Label(attributesFit);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.JumpAbsolute(writeAttributes);

        builder.Label(tilesRemain);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0xD0, tileSegment);
        builder.JumpAbsolute(incomplete);

        builder.Label(attributes);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.Label(writeAttributes);
        EmitRowAttributeWrites(builder, ready, incomplete);

        builder.Label(incomplete);
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(finish);
        builder.Label(ready);
        builder.LoadAImmediate(NesPackedCameraRuntime.CommitReadyToFinalize);
        builder.Label(finish);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        EmitIncrementSelectedMetadata(builder, NesPackedCameraRuntime.CommitPhaseOffset);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
    }

    private static void EmitBeginSelectedPhaseRoutine(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        builder.Label(BeginSelectedPhaseLabel);
        builder.LoadAImmediate(NesPackedCameraRuntime.Committing);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Committing);
        builder.LoadAImmediate(NesPackedCameraRuntime.CommitPpuPhase);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        var slot1 = builder.CreateLabel("nes_packed_phase_slot_1_payload");
        var ready = builder.CreateLabel("nes_packed_phase_payload_ready");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[0]);
        builder.JumpAbsolute(ready);
        builder.Label(slot1);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[1]);
        builder.Label(ready);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.Return();
    }

    private static void EmitColumnAttributeWrites(
        PrgBuilder builder,
        string ready,
        string incomplete)
    {
        var countReady = builder.CreateLabel("nes_packed_column_attr_count_ready");
        var topTarget = builder.CreateLabel("nes_packed_column_attr_top_target");
        var addressReady = builder.CreateLabel("nes_packed_column_attr_address_ready");
        var loop = builder.CreateLabel("nes_packed_column_attr_phase_loop");

        EmitComputeAttributeCount(builder);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.SetCarry();
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.JumpIf(0x90, countReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(countReady);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining, NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);

        builder.LoadAImmediate(0x80);
        builder.StoreAAbsolute(0x2000);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0xFC);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.CompareImmediate(60);
        builder.JumpIf(0x90, topTarget);
        builder.SetCarry();
        builder.SubtractImmediate(60);
        builder.Label(topTarget);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        EmitPrepareColumnAttributeAddress(builder);
        EmitInitializeColumnAttributePpuAddress(builder);

        builder.LoadYAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.Label(loop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.StoreAAbsolute(0x2006);
        builder.StoreXAbsolute(0x2006);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.Emit(0x8A); // TXA
        builder.ClearCarry();
        builder.AddImmediate(8);
        builder.TransferAToX();
        builder.JumpIf(0x90, addressReady);
        builder.Emit(0x8A); // TXA
        builder.OrImmediate(0xC0);
        builder.TransferAToX();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.Emit(0x49, 0x08); // EOR #$08: toggle the vertical nametable attribute page.
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.Label(addressReady);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0xD0, loop);
        builder.TransferYToA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        EmitStoreASelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0xF0, ready);
        builder.JumpAbsolute(incomplete);
    }

    private static void EmitRowAttributeWrites(
        PrgBuilder builder,
        string ready,
        string incomplete)
    {
        var countReady = builder.CreateLabel("nes_packed_row_attr_count_ready");
        var topBlock = builder.CreateLabel("nes_packed_row_attr_top_block");
        var boundaryReady = builder.CreateLabel("nes_packed_row_attr_boundary_ready");
        var segmentCountReady = builder.CreateLabel("nes_packed_row_attr_segment_count_ready");
        var segment = builder.CreateLabel("nes_packed_row_attr_segment");
        var loop = builder.CreateLabel("nes_packed_row_attr_phase_loop");
        var phaseDone = builder.CreateLabel("nes_packed_row_attr_phase_done");

        EmitComputeAttributeCount(builder);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.SetCarry();
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.JumpIf(0x90, countReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(countReady);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining, NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        builder.AndImmediate(0x0F);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.LoadYAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);

        builder.Label(segment);
        EmitSetPpuAttributeAddress(builder);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.CompareImmediate(8);
        builder.JumpIf(0x90, topBlock);
        builder.LoadAImmediate(16);
        builder.JumpAbsolute(boundaryReady);
        builder.Label(topBlock);
        builder.LoadAImmediate(8);
        builder.Label(boundaryReady);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0x90, segmentCountReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(segmentCountReady);
        builder.TransferAToX();

        builder.Label(loop);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.AndImmediate(0x0F);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.DecrementX();
        builder.JumpIf(0xD0, loop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0xD0, segment);
        builder.Label(phaseDone);
        builder.TransferYToA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        EmitStoreASelectedMetadata(builder, NesPackedCameraRuntime.PayloadCursorOffset);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.JumpIf(0xF0, ready);
        builder.JumpAbsolute(incomplete);
    }

    private static void EmitComputeAttributeCount(PrgBuilder builder)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.AddImmediate(3);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
    }

    private static void EmitPrepareColumnAttributeAddress(PrgBuilder builder)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTarget);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.AndImmediate(0x0F);
        builder.PushA();
        builder.AndImmediate(0x07);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AddressColumn);
        builder.PullA();
        builder.AndImmediate(0x08);
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
    }

    private static void EmitInitializeColumnAttributePpuAddress(PrgBuilder builder)
    {
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressHighLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.AddressColumn);
        builder.TransferAToX();
    }

    private static void EmitAddModulo(
        PrgBuilder builder,
        ushort left,
        ushort right,
        int modulo,
        ushort destination,
        string prefix)
    {
        var ready = builder.CreateLabel(prefix + "_ready");
        builder.LoadAAbsolute(left);
        builder.ClearCarry();
        builder.AddAbsolute(right);
        builder.CompareImmediate(modulo);
        builder.JumpIf(0x90, ready);
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.Label(ready);
        builder.StoreAAbsolute(destination);
    }

    private static void EmitLoadSelectedMetadata(PrgBuilder builder, int offset)
    {
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.LoadAAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)));
    }

    private static void EmitStoreASelectedMetadata(PrgBuilder builder, int offset)
    {
        builder.PushA();
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.PullA();
        builder.StoreAAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)));
    }

    private static void EmitIncrementSelectedMetadata(PrgBuilder builder, int offset)
    {
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.IncrementAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)));
    }

    private static void EmitFinalizeEdge(PrgBuilder builder)
    {
        var done = builder.CreateLabel("nes_packed_finalize_done");
        var row = builder.CreateLabel("nes_packed_finalize_row");
        var clear = builder.CreateLabel("nes_packed_finalize_clear");
        builder.Label(NesRomBuilder.WorldPackFinalizeEdgeLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.CompareImmediate(NesPackedCameraRuntime.CommitReadyToFinalize);
        builder.JumpIf(0xD0, done);
        EmitCopyFrameToSelectedMetadata(builder, resident: false, commit: true);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Released);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.CommitCount);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xF0, row);
        builder.LoadAImmediate(NesPackedCameraRuntime.Row);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        EmitClearPendingAxis(builder, NesPackedCameraRuntime.Column);
        builder.JumpAbsolute(clear);
        builder.Label(row);
        builder.LoadAImmediate(NesPackedCameraRuntime.Column);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        EmitClearPendingAxis(builder, NesPackedCameraRuntime.Row);
        builder.Label(clear);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.Label(done);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();
    }

    private static void EmitSelectSlotForCommit(
        PrgBuilder builder,
        string invalid,
        NesPackedCameraPhaseSchedule schedule)
    {
        if (schedule.MaximumColumnFrames != schedule.MaximumRowFrames)
        {
            throw new InvalidOperationException(
                "NES packed deadline selection requires the schedule to expose one comparable axis deadline.");
        }

        var slot0Committing = builder.CreateLabel("nes_packed_select_slot_0_committing");
        var slot1Committing = builder.CreateLabel("nes_packed_select_slot_1_committing");
        var checkResidents = builder.CreateLabel("nes_packed_select_check_residents");
        var slot0Resident = builder.CreateLabel("nes_packed_select_slot_0_resident");
        var bothResident = builder.CreateLabel("nes_packed_select_both_resident");
        var compareDeadlineLow = builder.CreateLabel("nes_packed_select_compare_deadline_low");
        var deadlinesEqual = builder.CreateLabel("nes_packed_select_deadlines_equal");
        var desiredReady = builder.CreateLabel("nes_packed_select_desired_ready");
        var selectSlot0 = builder.CreateLabel("nes_packed_select_slot_0");
        var selectSlot1 = builder.CreateLabel("nes_packed_select_slot_1");
        var done = builder.CreateLabel("nes_packed_select_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xF0, slot0Committing);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xF0, slot1Committing);
        builder.JumpAbsolute(checkResidents);

        builder.Label(slot0Committing);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, bothResident);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xF0, bothResident);
        builder.JumpAbsolute(selectSlot0);

        builder.Label(slot1Committing);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, bothResident);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xF0, bothResident);
        builder.JumpAbsolute(selectSlot1);

        builder.Label(checkResidents);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, slot0Resident);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, selectSlot1);
        builder.JumpAbsolute(invalid);

        builder.Label(slot0Resident);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, bothResident);
        builder.JumpAbsolute(selectSlot0);

        builder.Label(bothResident);
        // Every current schedule gives columns and rows the same visibility
        // deadline. Their nearest deadline is therefore the oldest request,
        // compared as a wrap-safe 16-bit age from the current physical frame.
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.RequestFrameLowOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh);
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.RequestFrameHighOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow);
        builder.SetCarry();
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.RequestFrameLowOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh);
        builder.SubtractAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.RequestFrameHighOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh);
        builder.JumpIf(0x90, selectSlot1);
        builder.JumpIf(0xF0, compareDeadlineLow);
        builder.JumpAbsolute(selectSlot0);
        builder.Label(compareDeadlineLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow);
        builder.JumpIf(0x90, selectSlot1);
        builder.JumpIf(0xF0, deadlinesEqual);
        builder.JumpAbsolute(selectSlot0);
        builder.Label(deadlinesEqual);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        builder.JumpIf(0xD0, desiredReady);
        builder.LoadAImmediate(NesPackedCameraRuntime.Column);
        builder.Label(desiredReady);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpIf(0xF0, selectSlot0);
        builder.JumpAbsolute(selectSlot1);

        builder.Label(selectSlot0);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.JumpAbsolute(done);
        builder.Label(selectSlot1);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.LoadAImmediate(NesPackedCameraRuntime.SlotMetadataBytes);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.Label(done);
    }

    private static void EmitLoadPendingExpectedTags(PrgBuilder builder)
    {
        var done = builder.CreateLabel("nes_packed_pending_tags_done");
        var row = builder.CreateLabel("nes_packed_pending_row");
        var sourceReady = builder.CreateLabel("nes_packed_pending_source_ready");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
        builder.JumpIf(0xF0, done);
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.LoadAAbsoluteX(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xF0, row);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.PendingColumn & 0xFF);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.PendingColumn >> 8);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.JumpAbsolute(sourceReady);
        builder.Label(row);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.PendingRow & 0xFF);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.PendingRow >> 8);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.Label(sourceReady);
        foreach (var (offset, destination) in new (int Offset, ushort Destination)[]
                 {
                     (NesPackedCameraRuntime.AxisOffset, NesRuntimeMemoryLayout.PackedCamera.CommitAxis),
                     (NesPackedCameraRuntime.DirectionOffset, NesRuntimeMemoryLayout.PackedCamera.CommitDirection),
                     (NesPackedCameraRuntime.WorldEdgeLowOffset, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow),
                     (NesPackedCameraRuntime.WorldEdgeHighOffset, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh),
                     (NesPackedCameraRuntime.TargetOffset, NesRuntimeMemoryLayout.PackedCamera.CommitTarget),
                     (NesPackedCameraRuntime.OrthogonalLowOffset, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow),
                     (NesPackedCameraRuntime.OrthogonalHighOffset, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh),
                     (NesPackedCameraRuntime.PayloadLengthOffset, NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength),
                     (NesPackedCameraRuntime.TargetStartOffset, NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart),
                 })
        {
            builder.LoadYImmediate(offset);
            builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
            builder.StoreAAbsolute(destination);
        }

        builder.Label(done);
    }

    private static void EmitClearPendingAxis(PrgBuilder builder, byte axis)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
        builder.AndImmediate(axis == NesPackedCameraRuntime.Column ? 0xFE : 0xFD);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
    }

    private static void EmitValidateSelectedSlot(PrgBuilder builder, string invalid)
    {
        var stateValid = builder.CreateLabel("nes_packed_validate_state_valid");
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.LoadAAbsoluteX(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, stateValid);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xD0, invalid);
        builder.Label(stateValid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.AxisOffset, NesRuntimeMemoryLayout.PackedCamera.CommitAxis, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.DirectionOffset, NesRuntimeMemoryLayout.PackedCamera.CommitDirection, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.WorldEdgeLowOffset, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.WorldEdgeHighOffset, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.TargetOffset, NesRuntimeMemoryLayout.PackedCamera.CommitTarget, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.OrthogonalLowOffset, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.OrthogonalHighOffset, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.PayloadLengthOffset, NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength, invalid);
        EmitCompareSelectedMetadata(builder, NesPackedCameraRuntime.TargetStartOffset, NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart, invalid);
    }

    private static void EmitCompareSelectedMetadata(PrgBuilder builder, int offset, byte expected, string invalid)
    {
        builder.LoadAAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)));
        builder.CompareImmediate(expected);
        builder.JumpIf(0xD0, invalid);
    }

    private static void EmitCompareSelectedMetadata(PrgBuilder builder, int offset, ushort expectedAddress, string invalid)
    {
        builder.LoadAAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)));
        builder.CompareAbsolute(expectedAddress);
        builder.JumpIf(0xD0, invalid);
    }

    private static void EmitPrepareEdge(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        var selectSlot0 = builder.CreateLabel("nes_packed_edge_select_slot_0");
        var selectSlot1 = builder.CreateLabel("nes_packed_edge_select_slot_1");
        var prepare = builder.CreateLabel("nes_packed_edge_prepare");
        var rowLoop = builder.CreateLabel("nes_packed_edge_prepare_row_loop");
        var column = builder.CreateLabel("nes_packed_edge_prepare_column");
        var row = builder.CreateLabel("nes_packed_edge_prepare_row");
        var store = builder.CreateLabel("nes_packed_edge_prepare_store");
        var columnAdvance = builder.CreateLabel("nes_packed_edge_prepare_column_advance");
        var columnLookup = builder.CreateLabel("nes_packed_edge_prepare_column_lookup");
        var failed = builder.CreateLabel("nes_packed_edge_prepare_failed");
        var attributes = builder.CreateLabel("nes_packed_edge_prepare_attributes");
        var success = builder.CreateLabel("nes_packed_edge_prepare_success");
        var noSlot = builder.CreateLabel("nes_packed_edge_no_slot");
        var entryValid = builder.CreateLabel("nes_packed_edge_entry_valid");
        var invalidRequest = builder.CreateLabel("nes_packed_edge_invalid_request");

        builder.Label(NesRomBuilder.WorldPackPrepareEdgeLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, entryValid);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xD0, invalidRequest);
        builder.Label(entryValid);
        EmitValidatePayloadLength(builder, invalidRequest);
        EmitJumpIfSlotAvailable(builder, NesRuntimeMemoryLayout.PackedCamera.Slot0, selectSlot0);
        EmitJumpIfSlotAvailable(builder, NesRuntimeMemoryLayout.PackedCamera.Slot1, selectSlot1);
        builder.JumpAbsolute(noSlot);

        builder.Label(selectSlot0);
        EmitSelectSlot(builder, plan.Layout.EdgeSlots[0], slot: 0);
        builder.JumpAbsolute(prepare);

        builder.Label(selectSlot1);
        EmitSelectSlot(builder, plan.Layout.EdgeSlots[1], slot: 1);

        builder.Label(prepare);
        EmitInitializeSelectedMetadata(builder);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.RequestCount);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Requested);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.PrepareCount);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Preparing);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SlotIndex);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, column);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xF0, row);
        builder.JumpAbsolute(failed);

        builder.Label(column);
        EmitColumnCoordinate(builder);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualLookupLabel);
        builder.JumpAbsolute(store);

        builder.Label(row);
        builder.Label(rowLoop);
        EmitRowCoordinate(builder);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualLookupLabel);

        builder.Label(store);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.JumpIf(0xD0, failed);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.LoadYAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultTile);
        builder.StoreAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.JumpIf(0xF0, attributes);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, columnAdvance);
        builder.JumpAbsolute(rowLoop);

        builder.Label(columnAdvance);
        EmitAdvancePreparedColumnCoordinate(builder, plan.Pack.Descriptor, plan.UsesFastLookup);
        builder.Label(columnLookup);
        builder.CallSubroutine(NesRomBuilder.WorldPackVisualLookupPreparedLabel);
        builder.JumpAbsolute(store);

        builder.Label(attributes);
        EmitPrepareAttributes(builder, plan);
        builder.JumpAbsolute(success);

        builder.Label(failed);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Released);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.Return();

        builder.Label(success);
        EmitCopyFrameToSelectedMetadata(builder, resident: true);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Resident);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.ResidentCount);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(noSlot);
        builder.LoadAImmediate(NesPackedCameraRuntime.NoSlot);
        builder.Return();

        builder.Label(invalidRequest);
        builder.LoadAImmediate((byte)NesWorldPackResult.Miss);
        builder.Return();
    }

    private static void EmitJumpIfSlotAvailable(PrgBuilder builder, ushort slot, string target)
    {
        builder.LoadAAbsolute(checked((ushort)(slot + NesPackedCameraRuntime.StateOffset)));
        builder.CompareImmediate(NesPackedCameraRuntime.Empty);
        builder.JumpIf(0xF0, target);
        builder.CompareImmediate(NesPackedCameraRuntime.Released);
        builder.JumpIf(0xF0, target);
    }

    private static void EmitValidatePayloadLength(PrgBuilder builder, string invalid)
    {
        var column = builder.CreateLabel("nes_packed_payload_column");
        var done = builder.CreateLabel("nes_packed_payload_valid");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, column);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.CompareImmediate(32);
        builder.JumpIf(0xD0, invalid);
        builder.JumpAbsolute(done);

        builder.Label(column);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.JumpIf(0xF0, invalid);
        builder.CompareImmediate(33);
        builder.JumpIf(0xB0, invalid);
        builder.Label(done);
    }

    private static void EmitSelectSlot(PrgBuilder builder, NesRamRange edge, int slot)
    {
        builder.LoadAImmediate(slot);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.LoadAImmediate(slot * NesPackedCameraRuntime.SlotMetadataBytes);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.LoadAImmediate(edge.Start & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadAImmediate(edge.Start >> 8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
    }

    private static void EmitInitializeSelectedMetadata(PrgBuilder builder)
    {
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitAxis, NesPackedCameraRuntime.AxisOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitDirection, NesPackedCameraRuntime.DirectionOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, NesPackedCameraRuntime.WorldEdgeLowOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, NesPackedCameraRuntime.WorldEdgeHighOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitTarget, NesPackedCameraRuntime.TargetOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow, NesPackedCameraRuntime.OrthogonalLowOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh, NesPackedCameraRuntime.OrthogonalHighOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength, NesPackedCameraRuntime.PayloadLengthOffset);
        EmitCopyToSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart, NesPackedCameraRuntime.TargetStartOffset);
        builder.LoadAImmediate(0);
        builder.StoreAAbsoluteX(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.StoreAAbsoluteX(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.PayloadCursorOffset);
        EmitCopyFrameToSelectedMetadata(builder, resident: false);
    }

    private static void EmitCopyToSelectedMetadata(PrgBuilder builder, ushort source, int offset)
    {
        builder.LoadAAbsolute(source);
        builder.StoreAAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)));
    }

    private static void EmitCopyFrameToSelectedMetadata(PrgBuilder builder, bool resident, bool commit = false)
    {
        if (commit)
        {
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow, NesRuntimeMemoryLayout.PackedCamera.CommitFrameLow);
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh, NesRuntimeMemoryLayout.PackedCamera.CommitFrameHigh);
            return;
        }

        var lowOffset = resident
            ? NesPackedCameraRuntime.ResidentFrameLowOffset
            : NesPackedCameraRuntime.RequestFrameLowOffset;
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow);
        builder.StoreAAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + lowOffset)));
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh);
        builder.StoreAAbsoluteX(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + lowOffset + 1)));
        if (resident)
        {
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow, NesRuntimeMemoryLayout.PackedCamera.ResidentFrameLow);
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh, NesRuntimeMemoryLayout.PackedCamera.ResidentFrameHigh);
        }
        else
        {
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow, NesRuntimeMemoryLayout.PackedCamera.RequestFrameLow);
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh, NesRuntimeMemoryLayout.PackedCamera.RequestFrameHigh);
        }
    }

    private static void EmitCopyFrame(PrgBuilder builder, ushort metadata, bool resident)
    {
        var lowOffset = resident
            ? NesPackedCameraRuntime.ResidentFrameLowOffset
            : NesPackedCameraRuntime.RequestFrameLowOffset;
        var highOffset = lowOffset + 1;
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow, checked((ushort)(metadata + lowOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh, checked((ushort)(metadata + highOffset)));
        if (resident)
        {
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow, NesRuntimeMemoryLayout.PackedCamera.ResidentFrameLow);
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh, NesRuntimeMemoryLayout.PackedCamera.ResidentFrameHigh);
        }
        else
        {
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow, NesRuntimeMemoryLayout.PackedCamera.RequestFrameLow);
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh, NesRuntimeMemoryLayout.PackedCamera.RequestFrameHigh);
        }
    }

    private static void EmitColumnCoordinate(PrgBuilder builder)
    {
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
    }

    private static void EmitAdvancePreparedColumnCoordinate(
        PrgBuilder builder,
        RetroSharp.Core.Sdk.WorldPackDescriptor descriptor,
        bool usesFastLookup)
    {
        var hardwareYReady = builder.CreateLabel("nes_packed_column_hardware_y_ready");
        var sameMetatile = builder.CreateLabel("nes_packed_column_same_metatile");
        var sameChunk = builder.CreateLabel("nes_packed_column_same_chunk");
        var done = builder.CreateLabel("nes_packed_column_coordinate_advanced");

        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.JumpIf(0xD0, hardwareYReady);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
        builder.Label(hardwareYReady);

        if (usesFastLookup)
        {
            // Fast coordinates are always 2x2. An odd destination row remains in the
            // current metatile; an even row starts the next one. Preserve the fixed
            // X subcell directly from the current packed subcell index so the common
            // lookup path does not have to materialize SubcellX/SubcellY.
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
            builder.AndImmediate(1);
            builder.JumpIf(0xD0, sameMetatile);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
            builder.AndImmediate(1);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        }
        else
        {
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellY);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellY);
            builder.CompareImmediate(descriptor.MetatileHeight);
            builder.JumpIf(0x90, sameMetatile);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellY);
            EmitCopy(builder, NesRuntimeMemoryLayout.WorldPack.SubcellX, NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexHigh);
        }
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.MetatileYLow);
        var metatileReady = builder.CreateLabel("nes_packed_column_metatile_y_ready");
        builder.JumpIf(0xD0, metatileReady);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.MetatileYHigh);
        builder.Label(metatileReady);

        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalY);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalY);
        builder.CompareImmediate(8);
        builder.JumpIf(0x90, sameChunk);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.LocalY);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkYLow);
        var chunkYReady = builder.CreateLabel("nes_packed_column_chunk_y_ready");
        builder.JumpIf(0xD0, chunkYReady);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkYHigh);
        builder.Label(chunkYReady);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.AddImmediate(descriptor.ChunkColumns & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
        builder.AddImmediate((descriptor.ChunkColumns >> 8) & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh);
        EmitCopy(builder, NesRuntimeMemoryLayout.WorldPack.LocalX, NesRuntimeMemoryLayout.WorldPack.CellIndex);
        builder.JumpAbsolute(done);

        builder.Label(sameChunk);
        if (usesFastLookup)
        {
            var regularWidth = builder.CreateLabel("nes_packed_column_regular_chunk_width");
            var sourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
            var lastWidth = sourceWidth - ((descriptor.ChunkColumns - 1) * 8);

            builder.LoadYImmediate(8);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ChunkXLow);
            builder.CompareImmediate(descriptor.ChunkColumns - 1);
            builder.BranchRelative(0xD0, regularWidth);
            builder.LoadYImmediate(lastWidth);
            builder.Label(regularWidth);
            builder.TransferYToA();
            builder.ClearCarry();
            builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
        }
        else
        {
            builder.ClearCarry();
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
            builder.AddAbsolute(NesRuntimeMemoryLayout.WorldPack.ValidWidth);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.CellIndex);
        }

        builder.JumpAbsolute(done);

        builder.Label(sameMetatile);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        builder.AddImmediate(descriptor.MetatileWidth);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.SubcellIndexHigh);
        builder.Label(done);
    }

    private static void EmitPrepareAttributes(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        var coordinatesReady = builder.CreateLabel("nes_packed_attrs_coordinates_ready");
        var multiplyLoop = builder.CreateLabel("nes_packed_attrs_multiply_loop");
        var multiplyDone = builder.CreateLabel("nes_packed_attrs_multiply_done");
        var decrementLow = builder.CreateLabel("nes_packed_attrs_decrement_low");
        var copyLoop = builder.CreateLabel("nes_packed_attrs_copy_loop");
        var incrementColumn = builder.CreateLabel("nes_packed_attrs_increment_column");
        var incrementDone = builder.CreateLabel("nes_packed_attrs_increment_done");
        var indexReady = builder.CreateLabel("nes_packed_attrs_index_ready");

        EmitAlignedOrthogonalBlock(builder, NesRuntimeMemoryLayout.PackedCamera.AttributeBlockXLow);
        EmitShiftedWord(
            builder,
            NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow,
            NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYLow,
            shifts: 2);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.BranchRelative(0xD0, coordinatesReady);
        for (var index = 0; index < 2; index++)
        {
            builder.LoadXAbsolute(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockXLow + index)));
            builder.LoadAAbsolute(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYLow + index)));
            if (index == 1)
            {
                // CMP established carry on the column path. SBC by the two's
                // complement adds the page-aligned column-table displacement
                // without spending scarce fixed-bank bytes on a second add.
                builder.SubtractImmediate(0x100 - (plan.Attributes.ColumnOffset >> 8));
            }
            builder.StoreAAbsolute(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockXLow + index)));
            builder.StoreXAbsolute(checked((ushort)(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYLow + index)));
        }

        builder.Label(coordinatesReady);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.AttributeBlockXLow, NesRuntimeMemoryLayout.PackedCamera.AttributeIndexLow);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.AttributeBlockXHigh, NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);

        builder.Label(multiplyLoop);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYLow);
        builder.OrAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYHigh);
        builder.BranchRelative(0xF0, multiplyDone);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexLow);
        builder.AddImmediate(plan.Attributes.Columns & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);
        builder.AddImmediate((plan.Attributes.Columns >> 8) & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYLow);
        builder.BranchRelative(0xD0, decrementLow);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYHigh);
        builder.Label(decrementLow);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlockYLow);
        builder.JumpAbsolute(multiplyLoop);

        builder.Label(multiplyDone);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.AddImmediate(3);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.LoadAImmediate(32);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);

        builder.Label(copyLoop);
        builder.LoadAImmediateLabelLowByte(NesRomBuilder.WorldPackAttributesLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAImmediateLabelHighByte(NesRomBuilder.WorldPackAttributesLabel);
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.LoadYAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.StoreAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.BranchRelative(0xF0, incrementColumn);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexLow);
        builder.BranchRelative(0xD0, indexReady);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);
        builder.JumpAbsolute(incrementDone);
        builder.Label(incrementColumn);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexLow);
        builder.AddImmediate(plan.Attributes.Columns & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexLow);
        if (plan.Attributes.Columns <= byte.MaxValue)
        {
            builder.BranchRelative(0x90, incrementDone);
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);
        }
        else
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);
            builder.AddImmediate((plan.Attributes.Columns >> 8) & 0xFF);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeIndexHigh);
        }
        builder.Label(incrementDone);
        builder.Label(indexReady);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeCount);
        builder.BranchRelative(0xD0, copyLoop);
    }

    private static void EmitAlignedOrthogonalBlock(PrgBuilder builder, ushort destinationLow)
    {
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow, destinationLow);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh, checked((ushort)(destinationLow + 1)));
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.SetCarry();
        builder.LoadAAbsolute(destinationLow);
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(destinationLow);
        builder.LoadAAbsolute(checked((ushort)(destinationLow + 1)));
        builder.SubtractImmediate(0);
        builder.StoreAAbsolute(checked((ushort)(destinationLow + 1)));
        for (var shift = 0; shift < 2; shift++)
        {
            builder.ShiftRightAbsolute(checked((ushort)(destinationLow + 1)));
            builder.RotateRightAbsolute(destinationLow);
        }
    }

    private static void EmitShiftedWord(
        PrgBuilder builder,
        ushort sourceLow,
        ushort destinationLow,
        int shifts)
    {
        EmitCopy(builder, sourceLow, destinationLow);
        EmitCopy(builder, checked((ushort)(sourceLow + 1)), checked((ushort)(destinationLow + 1)));
        for (var shift = 0; shift < shifts; shift++)
        {
            builder.ShiftRightAbsolute(checked((ushort)(destinationLow + 1)));
            builder.RotateRightAbsolute(destinationLow);
        }
    }

    private static void EmitRowCoordinate(PrgBuilder builder)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
    }

    private static void EmitSetSelectedState(PrgBuilder builder, byte state)
    {
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedMetadataOffset);
        builder.LoadAImmediate(state);
        builder.StoreAAbsoluteX(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
    }

    private static void EmitCopy(PrgBuilder builder, ushort source, ushort destination)
    {
        builder.LoadAAbsolute(source);
        builder.StoreAAbsolute(destination);
    }

    private static void EmitIncrement(PrgBuilder builder, ushort address) => builder.IncrementAbsolute(address);

    private static void EmitStoreDestination(PrgBuilder builder, NesRamRange edge)
    {
        builder.LoadAImmediate(edge.Start & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadAImmediate(edge.Start >> 8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
    }

    private static void EmitLoadPayloadByte(PrgBuilder builder)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.LoadYAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
    }

    private static void EmitSetPpuTileAddress(
        PrgBuilder builder,
        ushort rowAddress,
        ushort columnAddress,
        string labelPrefix)
    {
        var leftNameTable = builder.CreateLabel($"{labelPrefix}_left_nt");
        var highReady = builder.CreateLabel($"{labelPrefix}_high_ready");
        builder.LoadXAbsolute(rowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.PpuRowAddressHighLabel);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.LoadAAbsolute(columnAddress);
        builder.CompareImmediate(32);
        builder.JumpIf(0x90, leftNameTable);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.JumpAbsolute(highReady);
        builder.Label(leftNameTable);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.Label(highReady);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAAbsolute(columnAddress);
        builder.AndImmediate(0x1F);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AddressColumn);
        builder.LoadXAbsolute(rowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.PpuRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.AddressColumn);
        builder.StoreAAbsolute(0x2006);
    }

    private static void EmitSetPpuAttributeAddress(PrgBuilder builder)
    {
        var leftNameTable = builder.CreateLabel("nes_packed_row_attr_left_nt");
        var highReady = builder.CreateLabel("nes_packed_row_attr_high_ready");
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTarget);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressHighLabel);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.CompareImmediate(8);
        builder.JumpIf(0x90, leftNameTable);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.JumpAbsolute(highReady);
        builder.Label(leftNameTable);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.Label(highReady);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.AndImmediate(0x07);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AddressColumn);
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTarget);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.AddressColumn);
        builder.StoreAAbsolute(0x2006);
    }

}
