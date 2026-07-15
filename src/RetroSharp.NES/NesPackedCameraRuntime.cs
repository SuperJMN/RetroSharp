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

    internal static ushort SlotMetadata(int slot) => slot switch
    {
        0 => NesRuntimeMemoryLayout.PackedCamera.Slot0,
        1 => NesRuntimeMemoryLayout.PackedCamera.Slot1,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}

internal static class NesPackedCameraRuntimeEmitter
{
    internal static void Emit(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        EmitPrepareEdge(builder, plan);
        EmitCommitEdge(builder, plan);
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

    private static void EmitCommitEdge(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        var invalid = builder.CreateLabel("nes_packed_commit_invalid");
        var column = builder.CreateLabel("nes_packed_commit_column");
        var row = builder.CreateLabel("nes_packed_commit_row");
        var slot1Payload = builder.CreateLabel("nes_packed_commit_slot_1_payload");
        var payloadReady = builder.CreateLabel("nes_packed_commit_payload_ready");
        var columnSegment = builder.CreateLabel("nes_packed_commit_column_segment");
        var columnTopSegment = builder.CreateLabel("nes_packed_commit_column_top_segment");
        var columnSegmentReady = builder.CreateLabel("nes_packed_commit_column_segment_ready");
        var loop = builder.CreateLabel("nes_packed_commit_column_loop");
        var columnSetTopTarget = builder.CreateLabel("nes_packed_commit_column_set_top_target");
        var columnStoreTarget = builder.CreateLabel("nes_packed_commit_column_store_target");
        var columnTilesDone = builder.CreateLabel("nes_packed_commit_column_tiles_done");
        var columnAttributes = builder.CreateLabel("nes_packed_commit_column_attributes");
        var columnAttributeLoop = builder.CreateLabel("nes_packed_commit_column_attribute_loop");
        var columnRelease = builder.CreateLabel("nes_packed_commit_column_release");
        var rowSlot1Payload = builder.CreateLabel("nes_packed_row_slot_1_payload");
        var rowPayloadReady = builder.CreateLabel("nes_packed_row_payload_ready");
        var rowSlot1Phase = builder.CreateLabel("nes_packed_row_slot_1_phase");
        var rowPhaseReady = builder.CreateLabel("nes_packed_row_phase_ready");
        var rowAttributes = builder.CreateLabel("nes_packed_row_attributes");
        var rowTileLoop = builder.CreateLabel("nes_packed_row_tile_loop");
        var rowPhaseDone = builder.CreateLabel("nes_packed_row_phase_done");
        var rowAttributeLoop = builder.CreateLabel("nes_packed_row_attribute_loop");
        var rowRelease = builder.CreateLabel("nes_packed_row_release");

        builder.Label(NesRomBuilder.WorldPackCommitEdgeLabel);
        EmitSelectSlotForCommit(builder, invalid);
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
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Committing);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[0]);
        builder.JumpAbsolute(payloadReady);
        builder.Label(slot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[1]);

        builder.Label(payloadReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.LoadAAbsolute(0x2002); // Reset the shared PPUSCROLL/PPUADDR write latch.
        builder.LoadAImmediate(0x84);  // NMI enabled, PPUDATA increments vertically by 32.
        builder.StoreAAbsolute(0x2000);
        builder.LoadYImmediate(0);

        builder.Label(columnSegment);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.CompareImmediate(30);
        builder.JumpIf(0x90, columnTopSegment);
        builder.SetCarry();
        builder.SubtractImmediate(30);
        builder.Label(columnTopSegment);
        builder.XorImmediate(0xFF);
        builder.ClearCarry();
        builder.AddImmediate(31);
        builder.Label(columnSegmentReady);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        EmitSetPpuTileAddress(
            builder,
            NesRuntimeMemoryLayout.PackedCamera.TargetCursor,
            NesRuntimeMemoryLayout.PackedCamera.CommitTarget,
            "nes_packed_column_tile");
        // Keep the hot PPUDATA loop to one indirect load, one store, and the
        // two register increments. The old per-tile absolute DEC made a full
        // column too slow for the real NMI-to-pre-render window. Select the
        // shorter of the remaining payload and this physical nametable segment
        // once, then account for the payload at the segment boundary.
        builder.StoreYZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.SetCarry();
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.PackedCamera.PayloadIndexScratch);
        var columnSegmentCountReady = builder.CreateLabel("nes_packed_commit_column_segment_count_ready");
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0x90, columnSegmentCountReady);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.Label(columnSegmentCountReady);
        builder.TransferAToX();

        builder.Label(loop);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.DecrementX();
        builder.JumpIf(0xD0, loop);
        builder.TransferYToA();
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.JumpIf(0xF0, columnTilesDone);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.CompareImmediate(30);
        builder.JumpIf(0x90, columnSetTopTarget);
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(columnStoreTarget);
        builder.Label(columnSetTopTarget);
        builder.LoadAImmediate(30);
        builder.Label(columnStoreTarget);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.JumpAbsolute(columnSegment);

        builder.Label(columnTilesDone);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength, NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);

        builder.Label(columnAttributes);
        // Each tile PPUADDR pair leaves the shared PPUSCROLL/PPUADDR latch in its
        // first-write phase, so the attribute stream can continue without a
        // second PPUSTATUS read. Keeping that four-cycle VBlank margin is what
        // prevents the final attribute byte from reaching the pre-render line.
        builder.LoadAImmediate(0x80);  // Restore horizontal PPUDATA increment before sparse attributes.
        builder.StoreAAbsolute(0x2000);
        // Attributes are staged at a fixed +32 offset even when a short world
        // column streams fewer than 32 tile rows.
        builder.LoadYImmediate(32);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.AddImmediate(3);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0xFC);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
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

        builder.Label(columnAttributeLoop);
        EmitSetPpuAttributeAddressForColumn(builder);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.CompareImmediate(60);
        builder.JumpIf(0x90, columnRelease);
        builder.SetCarry();
        builder.SubtractImmediate(60);
        builder.Label(columnRelease);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        // DEC already publishes the zero flag used by the loop branch.
        builder.JumpIf(0xD0, columnAttributeLoop);

        EmitCopyFrameToSelectedMetadata(builder, resident: false, commit: true);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Released);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.CommitCount);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        builder.LoadAImmediate(NesPackedCameraRuntime.Row);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        EmitClearPendingAxis(builder, NesPackedCameraRuntime.Column);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(row);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Committing);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, rowSlot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[0]);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.JumpAbsolute(rowPayloadReady);
        builder.Label(rowSlot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[1]);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.Label(rowPayloadReady);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.RowPhase);
        builder.CompareImmediate(4);
        builder.JumpIf(0xF0, rowAttributes);

        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0x3F);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.LoadAImmediate(8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);

        builder.Label(rowTileLoop);
        EmitSetPpuTileAddress(
            builder,
            NesRuntimeMemoryLayout.PackedCamera.CommitTarget,
            NesRuntimeMemoryLayout.PackedCamera.TargetCursor,
            "nes_packed_row_tile");
        EmitLoadPayloadByte(builder);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastTileWrites);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.AndImmediate(0x3F);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0xD0, rowTileLoop);

        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.RowPhase);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, rowSlot1Phase);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.RowPhase, NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.JumpAbsolute(rowPhaseDone);
        builder.Label(rowSlot1Phase);
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.RowPhase, NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.Label(rowPhaseDone);
        builder.LoadAImmediate(NesPackedCameraRuntime.Column);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(rowAttributes);
        builder.LoadAImmediate(32);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
        builder.AddImmediate(3);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);

        builder.Label(rowAttributeLoop);
        EmitSetPpuAttributeAddress(builder);
        EmitLoadPayloadByte(builder);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.Iterator);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.AndImmediate(0x0F);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.AttributeBlock);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PhaseRemaining);
        builder.JumpIf(0xD0, rowAttributeLoop);
        builder.JumpAbsolute(rowRelease);

        builder.Label(rowRelease);
        EmitCopyFrameToSelectedMetadata(builder, resident: false, commit: true);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Released);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.CommitCount);
        EmitIncrement(builder, NesRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        builder.LoadAImmediate(NesPackedCameraRuntime.Column);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        EmitClearPendingAxis(builder, NesPackedCameraRuntime.Row);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(invalid);
        builder.LoadAImmediate((byte)NesWorldPackResult.Miss);
        builder.Return();
    }

    private static void EmitSelectSlotForCommit(PrgBuilder builder, string invalid)
    {
        var slot0Committing = builder.CreateLabel("nes_packed_select_slot_0_committing");
        var slot1Committing = builder.CreateLabel("nes_packed_select_slot_1_committing");
        var slot0CheckPeerAxis = builder.CreateLabel("nes_packed_select_slot_0_check_peer_axis");
        var slot1CheckPeerAxis = builder.CreateLabel("nes_packed_select_slot_1_check_peer_axis");
        var checkResidents = builder.CreateLabel("nes_packed_select_check_residents");
        var slot0Resident = builder.CreateLabel("nes_packed_select_slot_0_resident");
        var bothResident = builder.CreateLabel("nes_packed_select_both_resident");
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
        builder.JumpIf(0xF0, slot0CheckPeerAxis);
        builder.JumpAbsolute(selectSlot0);
        builder.Label(slot0CheckPeerAxis);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpIf(0xF0, selectSlot1);
        builder.JumpAbsolute(selectSlot0);

        builder.Label(slot1Committing);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, slot1CheckPeerAxis);
        builder.JumpAbsolute(selectSlot1);
        builder.Label(slot1CheckPeerAxis);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.NextAxis);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpIf(0xF0, selectSlot0);
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
        builder.JumpAbsolute(done);
        builder.Label(selectSlot1);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.Label(done);
    }

    private static void EmitLoadPendingExpectedTags(PrgBuilder builder)
    {
        var done = builder.CreateLabel("nes_packed_pending_tags_done");
        var selectedSlot1 = builder.CreateLabel("nes_packed_pending_selected_slot_1");
        var axisReady = builder.CreateLabel("nes_packed_pending_axis_ready");
        var row = builder.CreateLabel("nes_packed_pending_row");
        var sourceReady = builder.CreateLabel("nes_packed_pending_source_ready");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
        builder.JumpIf(0xF0, done);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, selectedSlot1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpAbsolute(axisReady);
        builder.Label(selectedSlot1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.AxisOffset);
        builder.Label(axisReady);
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
        var slot1 = builder.CreateLabel("nes_packed_validate_slot_1");
        var validate = builder.CreateLabel("nes_packed_validate_tags");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.Slot0 & 0xFF);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.Slot0 >> 8);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.JumpAbsolute(validate);
        builder.Label(slot1);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.Slot1 & 0xFF);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.LoadAImmediate(NesRuntimeMemoryLayout.PackedCamera.Slot1 >> 8);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.PackedCamera.PointerHigh);
        builder.Label(validate);
        var stateValid = builder.CreateLabel("nes_packed_validate_state_valid");
        builder.LoadYImmediate(NesPackedCameraRuntime.StateOffset);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, stateValid);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xD0, invalid);
        builder.Label(stateValid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.AxisOffset, NesRuntimeMemoryLayout.PackedCamera.CommitAxis, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.DirectionOffset, NesRuntimeMemoryLayout.PackedCamera.CommitDirection, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.WorldEdgeLowOffset, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.WorldEdgeHighOffset, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.TargetOffset, NesRuntimeMemoryLayout.PackedCamera.CommitTarget, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.OrthogonalLowOffset, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.OrthogonalHighOffset, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.PayloadLengthOffset, NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.TargetStartOffset, NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart, invalid);
    }

    private static void EmitCompareIndirectMetadata(PrgBuilder builder, int offset, byte expected, string invalid)
    {
        builder.LoadYImmediate(offset);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
        builder.CompareImmediate(expected);
        builder.JumpIf(0xD0, invalid);
    }

    private static void EmitCompareIndirectMetadata(PrgBuilder builder, int offset, ushort expectedAddress, string invalid)
    {
        builder.LoadYImmediate(offset);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.PackedCamera.PointerLow);
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
        EmitInitializeSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.Slot0);
        builder.JumpAbsolute(prepare);

        builder.Label(selectSlot1);
        EmitSelectSlot(builder, plan.Layout.EdgeSlots[1], slot: 1);
        EmitInitializeSelectedMetadata(builder, NesRuntimeMemoryLayout.PackedCamera.Slot1);

        builder.Label(prepare);
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
        builder.LoadAImmediate(edge.Start & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationLow);
        builder.LoadAImmediate(edge.Start >> 8);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.DestinationHigh);
    }

    private static void EmitInitializeSelectedMetadata(PrgBuilder builder, ushort metadata)
    {
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitAxis, checked((ushort)(metadata + NesPackedCameraRuntime.AxisOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitDirection, checked((ushort)(metadata + NesPackedCameraRuntime.DirectionOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, checked((ushort)(metadata + NesPackedCameraRuntime.WorldEdgeLowOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, checked((ushort)(metadata + NesPackedCameraRuntime.WorldEdgeHighOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitTarget, checked((ushort)(metadata + NesPackedCameraRuntime.TargetOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow, checked((ushort)(metadata + NesPackedCameraRuntime.OrthogonalLowOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh, checked((ushort)(metadata + NesPackedCameraRuntime.OrthogonalHighOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength, checked((ushort)(metadata + NesPackedCameraRuntime.PayloadLengthOffset)));
        EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart, checked((ushort)(metadata + NesPackedCameraRuntime.TargetStartOffset)));
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(checked((ushort)(metadata + NesPackedCameraRuntime.CommitPhaseOffset)));
        builder.StoreAAbsolute(checked((ushort)(metadata + NesPackedCameraRuntime.PayloadCursorOffset)));
        EmitCopyFrame(builder, metadata, resident: false);
    }

    private static void EmitCopyFrameToSelectedMetadata(PrgBuilder builder, bool resident, bool commit = false)
    {
        if (commit)
        {
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow, NesRuntimeMemoryLayout.PackedCamera.CommitFrameLow);
            EmitCopy(builder, NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh, NesRuntimeMemoryLayout.PackedCamera.CommitFrameHigh);
            return;
        }

        var slot1 = builder.CreateLabel("nes_packed_frame_slot_1");
        var done = builder.CreateLabel("nes_packed_frame_done");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1);
        EmitCopyFrame(builder, NesRuntimeMemoryLayout.PackedCamera.Slot0, resident);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        EmitCopyFrame(builder, NesRuntimeMemoryLayout.PackedCamera.Slot1, resident);
        builder.Label(done);
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
        var slot1 = builder.CreateLabel("nes_packed_state_slot_1");
        var done = builder.CreateLabel("nes_packed_state_done");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1);
        builder.LoadAImmediate(state);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        builder.LoadAImmediate(state);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.Label(done);
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

    private static void EmitSetPpuAttributeAddressForColumn(PrgBuilder builder)
    {
        builder.LoadXAbsolute(NesRuntimeMemoryLayout.PackedCamera.TargetCursor);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressHighLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.Status);
        builder.StoreAAbsolute(0x2006);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesRuntimeMemoryLayout.PackedCamera.AddressColumn);
        builder.StoreAAbsolute(0x2006);
    }
}
