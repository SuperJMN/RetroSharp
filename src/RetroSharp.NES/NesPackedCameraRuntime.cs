namespace RetroSharp.NES;

internal static class NesPackedCameraRuntime
{
    internal const ushort FrameCounterLow = 0x036E;
    internal const ushort FrameCounterHigh = 0x036F;
    internal const ushort RequestCount = 0x0370;
    internal const ushort PrepareCount = 0x0371;
    internal const ushort ResidentCount = 0x0372;
    internal const ushort CommitCount = 0x0373;
    internal const ushort ReleaseCount = 0x0374;
    internal const ushort BankWorkInCommit = 0x0375;
    internal const ushort DirectoryWorkInCommit = 0x0376;
    internal const ushort DecodeWorkInCommit = 0x0377;
    internal const ushort LastTileWrites = 0x0378;
    internal const ushort LastAttributeWrites = 0x0379;
    internal const ushort RequestFrameLow = 0x037A;
    internal const ushort RequestFrameHigh = 0x037B;
    internal const ushort ResidentFrameLow = 0x037C;
    internal const ushort ResidentFrameHigh = 0x037D;
    internal const ushort CommitFrameLow = 0x037E;
    internal const ushort CommitFrameHigh = 0x037F;
    internal const ushort CriticalSection = 0x0380;
    internal const ushort SelectedSlot = 0x0381;
    internal const ushort CommitAxis = 0x0382;
    internal const ushort CommitDirection = 0x0383;
    internal const ushort CommitWorldEdgeLow = 0x0384;
    internal const ushort CommitWorldEdgeHigh = 0x0385;
    internal const ushort CommitTarget = 0x0386;
    internal const ushort CommitOrthogonalLow = 0x0387;
    internal const ushort CommitOrthogonalHigh = 0x0388;
    internal const ushort CommitPayloadLength = 0x0389;
    internal const ushort RowPhase = 0x038A;
    internal const ushort CommitTargetStart = 0x038B;
    internal const ushort NextAxis = 0x038C;
    internal const ushort FramePending = 0x038F;

    internal const ushort Slot0 = 0x0390;
    internal const ushort Slot1 = 0x03A0;
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

    internal const ushort Iterator = 0x03B0;
    internal const ushort DestinationLow = 0x03B1;
    internal const ushort DestinationHigh = 0x03B2;
    internal const ushort Status = 0x03B3;
    internal const ushort TargetCursor = 0x03B4;
    internal const ushort AddressColumn = 0x03B5;
    internal const ushort PhaseRemaining = 0x03B6;
    internal const ushort AttributeBlock = 0x03B7;
    internal const ushort AttributeIndexLow = 0x03C3;
    internal const ushort AttributeIndexHigh = 0x03C4;
    internal const ushort AttributeCount = 0x03C5;
    internal const ushort AttributeBlockXLow = 0x03C6;
    internal const ushort AttributeBlockXHigh = 0x03C7;
    internal const ushort AttributeBlockYLow = 0x03C8;
    internal const ushort AttributeBlockYHigh = 0x03C9;
    internal const ushort PendingAxes = 0x03CA;
    internal const ushort VisibleCameraXLow = 0x03CB;
    internal const ushort VisibleCameraXHigh = 0x03CC;
    internal const ushort VisibleCameraYLow = 0x03CD;
    internal const ushort VisibleCameraYHigh = 0x03CE;
    internal const ushort VisibleCameraTileColumn = 0x03CF;
    internal const ushort PendingColumn = 0x03D0;
    internal const ushort PendingRow = 0x03E0;
    internal const ushort VisibleCameraTileRow = 0x03F0;

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
        0 => Slot0,
        1 => Slot1,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}

internal static class NesPackedCameraRuntimeEmitter
{
    private const byte PayloadIndexScratch = 0xE4;
    private const byte PointerLow = 0xE8;
    private const byte PointerHigh = 0xE9;

    internal static void Emit(PrgBuilder builder, NesWorldPackRuntimePlan plan)
    {
        EmitPrepareEdge(builder, plan);
        EmitCommitEdge(builder, plan);
        EmitReleaseReversedEdges(builder);
    }

    private static void EmitReleaseReversedEdges(PrgBuilder builder)
    {
        builder.Label(NesRomBuilder.WorldPackReleaseReversedEdgeLabel);
        EmitReleaseReversedSlot(builder, NesPackedCameraRuntime.Slot0, "nes_packed_reverse_slot_0");
        EmitReleaseReversedSlot(builder, NesPackedCameraRuntime.Slot1, "nes_packed_reverse_slot_1");
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
        builder.CompareAbsolute(NesPackedCameraRuntime.CommitAxis);
        builder.JumpIf(0xD0, done);
        builder.LoadAAbsolute(checked((ushort)(slot + NesPackedCameraRuntime.DirectionOffset)));
        builder.CompareAbsolute(NesPackedCameraRuntime.CommitDirection);
        builder.JumpIf(0xF0, done);
        builder.LoadAImmediate(NesPackedCameraRuntime.Released);
        builder.StoreAAbsolute(checked((ushort)(slot + NesPackedCameraRuntime.StateOffset)));
        EmitIncrement(builder, NesPackedCameraRuntime.ReleaseCount);
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
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, column);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xF0, row);
        builder.JumpAbsolute(invalid);

        builder.Label(column);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Committing);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesPackedCameraRuntime.CriticalSection);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.StoreAAbsolute(NesPackedCameraRuntime.TargetCursor);

        builder.LoadAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[0]);
        builder.JumpAbsolute(payloadReady);
        builder.Label(slot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[1]);

        builder.Label(payloadReady);
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationLow);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationHigh);
        builder.StoreAZeroPage(PointerHigh);
        builder.LoadAAbsolute(0x2002); // Reset the shared PPUSCROLL/PPUADDR write latch.
        builder.LoadAImmediate(0x84);  // NMI enabled, PPUDATA increments vertically by 32.
        builder.StoreAAbsolute(0x2000);
        builder.LoadYImmediate(0);

        builder.Label(columnSegment);
        builder.LoadAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.CompareImmediate(30);
        builder.JumpIf(0x90, columnTopSegment);
        builder.SetCarry();
        builder.SubtractImmediate(30);
        builder.Label(columnTopSegment);
        builder.XorImmediate(0xFF);
        builder.ClearCarry();
        builder.AddImmediate(31);
        builder.Label(columnSegmentReady);
        builder.StoreAAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        EmitSetPpuTileAddress(
            builder,
            NesPackedCameraRuntime.TargetCursor,
            NesPackedCameraRuntime.CommitTarget,
            "nes_packed_column_tile");
        // Keep the hot PPUDATA loop to one indirect load, one store, and the
        // two register increments. The old per-tile absolute DEC made a full
        // column too slow for the real NMI-to-pre-render window. Select the
        // shorter of the remaining payload and this physical nametable segment
        // once, then account for the payload at the segment boundary.
        builder.StoreYZeroPage(PayloadIndexScratch);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.SetCarry();
        builder.SubtractZeroPage(PayloadIndexScratch);
        var columnSegmentCountReady = builder.CreateLabel("nes_packed_commit_column_segment_count_ready");
        builder.CompareAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.JumpIf(0x90, columnSegmentCountReady);
        builder.LoadAAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.Label(columnSegmentCountReady);
        builder.TransferAToX();

        builder.Label(loop);
        builder.LoadAIndirectY(PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.DecrementX();
        builder.JumpIf(0xD0, loop);
        builder.TransferYToA();
        builder.CompareAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.JumpIf(0xF0, columnTilesDone);
        builder.LoadAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.CompareImmediate(30);
        builder.JumpIf(0x90, columnSetTopTarget);
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(columnStoreTarget);
        builder.Label(columnSetTopTarget);
        builder.LoadAImmediate(30);
        builder.Label(columnStoreTarget);
        builder.StoreAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.JumpAbsolute(columnSegment);

        builder.Label(columnTilesDone);
        EmitCopy(builder, NesPackedCameraRuntime.CommitPayloadLength, NesPackedCameraRuntime.LastTileWrites);

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
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.AddImmediate(3);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.StoreAAbsolute(NesPackedCameraRuntime.LastAttributeWrites);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.AndImmediate(0xFC);
        builder.StoreAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTarget);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.AndImmediate(0x0F);
        builder.PushA();
        builder.AndImmediate(0x07);
        builder.StoreAAbsolute(NesPackedCameraRuntime.AddressColumn);
        builder.PullA();
        builder.AndImmediate(0x08);
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesPackedCameraRuntime.Status);

        builder.Label(columnAttributeLoop);
        EmitSetPpuAttributeAddressForColumn(builder);
        builder.LoadAIndirectY(PointerLow);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementY();
        builder.LoadAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.CompareImmediate(60);
        builder.JumpIf(0x90, columnRelease);
        builder.SetCarry();
        builder.SubtractImmediate(60);
        builder.Label(columnRelease);
        builder.StoreAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.DecrementAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        // DEC already publishes the zero flag used by the loop branch.
        builder.JumpIf(0xD0, columnAttributeLoop);

        EmitCopyFrameToSelectedMetadata(builder, resident: false, commit: true);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Released);
        EmitIncrement(builder, NesPackedCameraRuntime.CommitCount);
        EmitIncrement(builder, NesPackedCameraRuntime.ReleaseCount);
        builder.LoadAImmediate(NesPackedCameraRuntime.Row);
        builder.StoreAAbsolute(NesPackedCameraRuntime.NextAxis);
        EmitClearPendingAxis(builder, NesPackedCameraRuntime.Column);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesPackedCameraRuntime.CriticalSection);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(row);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Committing);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesPackedCameraRuntime.CriticalSection);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesPackedCameraRuntime.LastTileWrites);
        builder.StoreAAbsolute(NesPackedCameraRuntime.LastAttributeWrites);

        builder.LoadAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, rowSlot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[0]);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.JumpAbsolute(rowPayloadReady);
        builder.Label(rowSlot1Payload);
        EmitStoreDestination(builder, plan.Layout.EdgeSlots[1]);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.Label(rowPayloadReady);
        builder.StoreAAbsolute(NesPackedCameraRuntime.RowPhase);
        builder.CompareImmediate(4);
        builder.JumpIf(0xF0, rowAttributes);

        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.StoreAAbsolute(NesPackedCameraRuntime.Iterator);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.AndImmediate(0x3F);
        builder.StoreAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.LoadAImmediate(8);
        builder.StoreAAbsolute(NesPackedCameraRuntime.PhaseRemaining);

        builder.Label(rowTileLoop);
        EmitSetPpuTileAddress(
            builder,
            NesPackedCameraRuntime.CommitTarget,
            NesPackedCameraRuntime.TargetCursor,
            "nes_packed_row_tile");
        EmitLoadPayloadByte(builder);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementAbsolute(NesPackedCameraRuntime.LastTileWrites);
        builder.IncrementAbsolute(NesPackedCameraRuntime.Iterator);
        builder.IncrementAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.LoadAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.AndImmediate(0x3F);
        builder.StoreAAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.DecrementAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.LoadAAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.JumpIf(0xD0, rowTileLoop);

        builder.IncrementAbsolute(NesPackedCameraRuntime.RowPhase);
        builder.LoadAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, rowSlot1Phase);
        EmitCopy(builder, NesPackedCameraRuntime.RowPhase, NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.JumpAbsolute(rowPhaseDone);
        builder.Label(rowSlot1Phase);
        EmitCopy(builder, NesPackedCameraRuntime.RowPhase, NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset);
        builder.Label(rowPhaseDone);
        builder.LoadAImmediate(NesPackedCameraRuntime.Column);
        builder.StoreAAbsolute(NesPackedCameraRuntime.NextAxis);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesPackedCameraRuntime.CriticalSection);
        builder.LoadAImmediate((byte)NesWorldPackResult.Success);
        builder.Return();

        builder.Label(rowAttributes);
        builder.LoadAImmediate(32);
        builder.StoreAAbsolute(NesPackedCameraRuntime.Iterator);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.AddImmediate(3);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesPackedCameraRuntime.AttributeBlock);

        builder.Label(rowAttributeLoop);
        EmitSetPpuAttributeAddress(builder);
        EmitLoadPayloadByte(builder);
        builder.StoreAAbsolute(0x2007);
        builder.IncrementAbsolute(NesPackedCameraRuntime.LastAttributeWrites);
        builder.IncrementAbsolute(NesPackedCameraRuntime.Iterator);
        builder.IncrementAbsolute(NesPackedCameraRuntime.AttributeBlock);
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeBlock);
        builder.AndImmediate(0x0F);
        builder.StoreAAbsolute(NesPackedCameraRuntime.AttributeBlock);
        builder.DecrementAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.LoadAAbsolute(NesPackedCameraRuntime.PhaseRemaining);
        builder.JumpIf(0xD0, rowAttributeLoop);
        builder.JumpAbsolute(rowRelease);

        builder.Label(rowRelease);
        EmitCopyFrameToSelectedMetadata(builder, resident: false, commit: true);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Released);
        EmitIncrement(builder, NesPackedCameraRuntime.CommitCount);
        EmitIncrement(builder, NesPackedCameraRuntime.ReleaseCount);
        builder.LoadAImmediate(NesPackedCameraRuntime.Column);
        builder.StoreAAbsolute(NesPackedCameraRuntime.NextAxis);
        EmitClearPendingAxis(builder, NesPackedCameraRuntime.Row);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesPackedCameraRuntime.CriticalSection);
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

        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xF0, slot0Committing);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xF0, slot1Committing);
        builder.JumpAbsolute(checkResidents);

        builder.Label(slot0Committing);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, slot0CheckPeerAxis);
        builder.JumpAbsolute(selectSlot0);
        builder.Label(slot0CheckPeerAxis);
        builder.LoadAAbsolute(NesPackedCameraRuntime.NextAxis);
        builder.CompareAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpIf(0xF0, selectSlot1);
        builder.JumpAbsolute(selectSlot0);

        builder.Label(slot1Committing);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, slot1CheckPeerAxis);
        builder.JumpAbsolute(selectSlot1);
        builder.Label(slot1CheckPeerAxis);
        builder.LoadAAbsolute(NesPackedCameraRuntime.NextAxis);
        builder.CompareAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpIf(0xF0, selectSlot0);
        builder.JumpAbsolute(selectSlot1);

        builder.Label(checkResidents);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, slot0Resident);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, selectSlot1);
        builder.JumpAbsolute(invalid);

        builder.Label(slot0Resident);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, bothResident);
        builder.JumpAbsolute(selectSlot0);

        builder.Label(bothResident);
        builder.LoadAAbsolute(NesPackedCameraRuntime.NextAxis);
        builder.JumpIf(0xD0, desiredReady);
        builder.LoadAImmediate(NesPackedCameraRuntime.Column);
        builder.Label(desiredReady);
        builder.CompareAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpIf(0xF0, selectSlot0);
        builder.JumpAbsolute(selectSlot1);

        builder.Label(selectSlot0);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.JumpAbsolute(done);
        builder.Label(selectSlot1);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.Label(done);
    }

    private static void EmitLoadPendingExpectedTags(PrgBuilder builder)
    {
        var done = builder.CreateLabel("nes_packed_pending_tags_done");
        var selectedSlot1 = builder.CreateLabel("nes_packed_pending_selected_slot_1");
        var axisReady = builder.CreateLabel("nes_packed_pending_axis_ready");
        var row = builder.CreateLabel("nes_packed_pending_row");
        var sourceReady = builder.CreateLabel("nes_packed_pending_source_ready");
        builder.LoadAAbsolute(NesPackedCameraRuntime.PendingAxes);
        builder.JumpIf(0xF0, done);
        builder.LoadAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, selectedSlot1);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.JumpAbsolute(axisReady);
        builder.Label(selectedSlot1);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.AxisOffset);
        builder.Label(axisReady);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xF0, row);
        builder.LoadAImmediate(NesPackedCameraRuntime.PendingColumn & 0xFF);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAImmediate(NesPackedCameraRuntime.PendingColumn >> 8);
        builder.StoreAZeroPage(PointerHigh);
        builder.JumpAbsolute(sourceReady);
        builder.Label(row);
        builder.LoadAImmediate(NesPackedCameraRuntime.PendingRow & 0xFF);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAImmediate(NesPackedCameraRuntime.PendingRow >> 8);
        builder.StoreAZeroPage(PointerHigh);
        builder.Label(sourceReady);
        foreach (var (offset, destination) in new (int Offset, ushort Destination)[]
                 {
                     (NesPackedCameraRuntime.AxisOffset, NesPackedCameraRuntime.CommitAxis),
                     (NesPackedCameraRuntime.DirectionOffset, NesPackedCameraRuntime.CommitDirection),
                     (NesPackedCameraRuntime.WorldEdgeLowOffset, NesPackedCameraRuntime.CommitWorldEdgeLow),
                     (NesPackedCameraRuntime.WorldEdgeHighOffset, NesPackedCameraRuntime.CommitWorldEdgeHigh),
                     (NesPackedCameraRuntime.TargetOffset, NesPackedCameraRuntime.CommitTarget),
                     (NesPackedCameraRuntime.OrthogonalLowOffset, NesPackedCameraRuntime.CommitOrthogonalLow),
                     (NesPackedCameraRuntime.OrthogonalHighOffset, NesPackedCameraRuntime.CommitOrthogonalHigh),
                     (NesPackedCameraRuntime.PayloadLengthOffset, NesPackedCameraRuntime.CommitPayloadLength),
                     (NesPackedCameraRuntime.TargetStartOffset, NesPackedCameraRuntime.CommitTargetStart),
                 })
        {
            builder.LoadYImmediate(offset);
            builder.LoadAIndirectY(PointerLow);
            builder.StoreAAbsolute(destination);
        }

        builder.Label(done);
    }

    private static void EmitClearPendingAxis(PrgBuilder builder, byte axis)
    {
        builder.LoadAAbsolute(NesPackedCameraRuntime.PendingAxes);
        builder.AndImmediate(axis == NesPackedCameraRuntime.Column ? 0xFE : 0xFD);
        builder.StoreAAbsolute(NesPackedCameraRuntime.PendingAxes);
    }

    private static void EmitValidateSelectedSlot(PrgBuilder builder, string invalid)
    {
        var slot1 = builder.CreateLabel("nes_packed_validate_slot_1");
        var validate = builder.CreateLabel("nes_packed_validate_tags");
        builder.LoadAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1);
        builder.LoadAImmediate(NesPackedCameraRuntime.Slot0 & 0xFF);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAImmediate(NesPackedCameraRuntime.Slot0 >> 8);
        builder.StoreAZeroPage(PointerHigh);
        builder.JumpAbsolute(validate);
        builder.Label(slot1);
        builder.LoadAImmediate(NesPackedCameraRuntime.Slot1 & 0xFF);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAImmediate(NesPackedCameraRuntime.Slot1 >> 8);
        builder.StoreAZeroPage(PointerHigh);
        builder.Label(validate);
        var stateValid = builder.CreateLabel("nes_packed_validate_state_valid");
        builder.LoadYImmediate(NesPackedCameraRuntime.StateOffset);
        builder.LoadAIndirectY(PointerLow);
        builder.CompareImmediate(NesPackedCameraRuntime.Resident);
        builder.JumpIf(0xF0, stateValid);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xD0, invalid);
        builder.Label(stateValid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.AxisOffset, NesPackedCameraRuntime.CommitAxis, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.DirectionOffset, NesPackedCameraRuntime.CommitDirection, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.WorldEdgeLowOffset, NesPackedCameraRuntime.CommitWorldEdgeLow, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.WorldEdgeHighOffset, NesPackedCameraRuntime.CommitWorldEdgeHigh, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.TargetOffset, NesPackedCameraRuntime.CommitTarget, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.OrthogonalLowOffset, NesPackedCameraRuntime.CommitOrthogonalLow, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.OrthogonalHighOffset, NesPackedCameraRuntime.CommitOrthogonalHigh, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.PayloadLengthOffset, NesPackedCameraRuntime.CommitPayloadLength, invalid);
        EmitCompareIndirectMetadata(builder, NesPackedCameraRuntime.TargetStartOffset, NesPackedCameraRuntime.CommitTargetStart, invalid);
    }

    private static void EmitCompareIndirectMetadata(PrgBuilder builder, int offset, byte expected, string invalid)
    {
        builder.LoadYImmediate(offset);
        builder.LoadAIndirectY(PointerLow);
        builder.CompareImmediate(expected);
        builder.JumpIf(0xD0, invalid);
    }

    private static void EmitCompareIndirectMetadata(PrgBuilder builder, int offset, ushort expectedAddress, string invalid)
    {
        builder.LoadYImmediate(offset);
        builder.LoadAIndirectY(PointerLow);
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
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, entryValid);
        builder.CompareImmediate(NesPackedCameraRuntime.Row);
        builder.JumpIf(0xD0, invalidRequest);
        builder.Label(entryValid);
        EmitValidatePayloadLength(builder, invalidRequest);
        EmitJumpIfSlotAvailable(builder, NesPackedCameraRuntime.Slot0, selectSlot0);
        EmitJumpIfSlotAvailable(builder, NesPackedCameraRuntime.Slot1, selectSlot1);
        builder.JumpAbsolute(noSlot);

        builder.Label(selectSlot0);
        EmitSelectSlot(builder, plan.Layout.EdgeSlots[0], slot: 0);
        EmitInitializeSelectedMetadata(builder, NesPackedCameraRuntime.Slot0);
        builder.JumpAbsolute(prepare);

        builder.Label(selectSlot1);
        EmitSelectSlot(builder, plan.Layout.EdgeSlots[1], slot: 1);
        EmitInitializeSelectedMetadata(builder, NesPackedCameraRuntime.Slot1);

        builder.Label(prepare);
        EmitIncrement(builder, NesPackedCameraRuntime.RequestCount);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Requested);
        EmitIncrement(builder, NesPackedCameraRuntime.PrepareCount);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Preparing);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesPackedCameraRuntime.Iterator);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SlotIndex);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitAxis);
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
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationLow);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationHigh);
        builder.StoreAZeroPage(PointerHigh);
        builder.LoadYAbsolute(NesPackedCameraRuntime.Iterator);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ResultTile);
        builder.StoreAIndirectY(PointerLow);
        builder.IncrementAbsolute(NesPackedCameraRuntime.Iterator);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Iterator);
        builder.CompareAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.JumpIf(0xF0, attributes);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitAxis);
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
        builder.StoreAAbsolute(NesPackedCameraRuntime.Status);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Released);
        EmitIncrement(builder, NesPackedCameraRuntime.ReleaseCount);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Status);
        builder.Return();

        builder.Label(success);
        EmitCopyFrameToSelectedMetadata(builder, resident: true);
        EmitSetSelectedState(builder, NesPackedCameraRuntime.Resident);
        EmitIncrement(builder, NesPackedCameraRuntime.ResidentCount);
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
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.JumpIf(0xF0, column);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.CompareImmediate(32);
        builder.JumpIf(0xD0, invalid);
        builder.JumpAbsolute(done);

        builder.Label(column);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.JumpIf(0xF0, invalid);
        builder.CompareImmediate(33);
        builder.JumpIf(0xB0, invalid);
        builder.Label(done);
    }

    private static void EmitSelectSlot(PrgBuilder builder, NesRamRange edge, int slot)
    {
        builder.LoadAImmediate(slot);
        builder.StoreAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.LoadAImmediate(edge.Start & 0xFF);
        builder.StoreAAbsolute(NesPackedCameraRuntime.DestinationLow);
        builder.LoadAImmediate(edge.Start >> 8);
        builder.StoreAAbsolute(NesPackedCameraRuntime.DestinationHigh);
    }

    private static void EmitInitializeSelectedMetadata(PrgBuilder builder, ushort metadata)
    {
        EmitCopy(builder, NesPackedCameraRuntime.CommitAxis, checked((ushort)(metadata + NesPackedCameraRuntime.AxisOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitDirection, checked((ushort)(metadata + NesPackedCameraRuntime.DirectionOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitWorldEdgeLow, checked((ushort)(metadata + NesPackedCameraRuntime.WorldEdgeLowOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitWorldEdgeHigh, checked((ushort)(metadata + NesPackedCameraRuntime.WorldEdgeHighOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitTarget, checked((ushort)(metadata + NesPackedCameraRuntime.TargetOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitOrthogonalLow, checked((ushort)(metadata + NesPackedCameraRuntime.OrthogonalLowOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitOrthogonalHigh, checked((ushort)(metadata + NesPackedCameraRuntime.OrthogonalHighOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitPayloadLength, checked((ushort)(metadata + NesPackedCameraRuntime.PayloadLengthOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.CommitTargetStart, checked((ushort)(metadata + NesPackedCameraRuntime.TargetStartOffset)));
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(checked((ushort)(metadata + NesPackedCameraRuntime.CommitPhaseOffset)));
        builder.StoreAAbsolute(checked((ushort)(metadata + NesPackedCameraRuntime.PayloadCursorOffset)));
        EmitCopyFrame(builder, metadata, resident: false);
    }

    private static void EmitCopyFrameToSelectedMetadata(PrgBuilder builder, bool resident, bool commit = false)
    {
        if (commit)
        {
            EmitCopy(builder, NesPackedCameraRuntime.FrameCounterLow, NesPackedCameraRuntime.CommitFrameLow);
            EmitCopy(builder, NesPackedCameraRuntime.FrameCounterHigh, NesPackedCameraRuntime.CommitFrameHigh);
            return;
        }

        var slot1 = builder.CreateLabel("nes_packed_frame_slot_1");
        var done = builder.CreateLabel("nes_packed_frame_done");
        builder.LoadAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1);
        EmitCopyFrame(builder, NesPackedCameraRuntime.Slot0, resident);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        EmitCopyFrame(builder, NesPackedCameraRuntime.Slot1, resident);
        builder.Label(done);
    }

    private static void EmitCopyFrame(PrgBuilder builder, ushort metadata, bool resident)
    {
        var lowOffset = resident
            ? NesPackedCameraRuntime.ResidentFrameLowOffset
            : NesPackedCameraRuntime.RequestFrameLowOffset;
        var highOffset = lowOffset + 1;
        EmitCopy(builder, NesPackedCameraRuntime.FrameCounterLow, checked((ushort)(metadata + lowOffset)));
        EmitCopy(builder, NesPackedCameraRuntime.FrameCounterHigh, checked((ushort)(metadata + highOffset)));
        if (resident)
        {
            EmitCopy(builder, NesPackedCameraRuntime.FrameCounterLow, NesPackedCameraRuntime.ResidentFrameLow);
            EmitCopy(builder, NesPackedCameraRuntime.FrameCounterHigh, NesPackedCameraRuntime.ResidentFrameHigh);
        }
        else
        {
            EmitCopy(builder, NesPackedCameraRuntime.FrameCounterLow, NesPackedCameraRuntime.RequestFrameLow);
            EmitCopy(builder, NesPackedCameraRuntime.FrameCounterHigh, NesPackedCameraRuntime.RequestFrameHigh);
        }
    }

    private static void EmitColumnCoordinate(PrgBuilder builder)
    {
        EmitCopy(builder, NesPackedCameraRuntime.CommitWorldEdgeLow, NesWorldPackRuntimeAbi.HardwareXLow);
        EmitCopy(builder, NesPackedCameraRuntime.CommitWorldEdgeHigh, NesWorldPackRuntimeAbi.HardwareXHigh);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitOrthogonalLow);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.Iterator);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareYLow);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitOrthogonalHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareYHigh);
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

        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.HardwareYLow);
        builder.JumpIf(0xD0, hardwareYReady);
        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.HardwareYHigh);
        builder.Label(hardwareYReady);

        if (usesFastLookup)
        {
            // Fast coordinates are always 2x2. An odd destination row remains in the
            // current metatile; an even row starts the next one. Preserve the fixed
            // X subcell directly from the current packed subcell index so the common
            // lookup path does not have to materialize SubcellX/SubcellY.
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.HardwareYLow);
            builder.AndImmediate(1);
            builder.JumpIf(0xD0, sameMetatile);
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SubcellIndexLow);
            builder.AndImmediate(1);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SubcellIndexLow);
        }
        else
        {
            builder.IncrementAbsolute(NesWorldPackRuntimeAbi.SubcellY);
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SubcellY);
            builder.CompareImmediate(descriptor.MetatileHeight);
            builder.JumpIf(0x90, sameMetatile);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SubcellY);
            EmitCopy(builder, NesWorldPackRuntimeAbi.SubcellX, NesWorldPackRuntimeAbi.SubcellIndexLow);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SubcellIndexHigh);
        }
        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.MetatileYLow);
        var metatileReady = builder.CreateLabel("nes_packed_column_metatile_y_ready");
        builder.JumpIf(0xD0, metatileReady);
        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.MetatileYHigh);
        builder.Label(metatileReady);

        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.LocalY);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.LocalY);
        builder.CompareImmediate(8);
        builder.JumpIf(0x90, sameChunk);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.LocalY);
        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.ChunkYLow);
        var chunkYReady = builder.CreateLabel("nes_packed_column_chunk_y_ready");
        builder.JumpIf(0xD0, chunkYReady);
        builder.IncrementAbsolute(NesWorldPackRuntimeAbi.ChunkYHigh);
        builder.Label(chunkYReady);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ChunkIndexLow);
        builder.AddImmediate(descriptor.ChunkColumns & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ChunkIndexLow);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ChunkIndexHigh);
        builder.AddImmediate((descriptor.ChunkColumns >> 8) & 0xFF);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.ChunkIndexHigh);
        EmitCopy(builder, NesWorldPackRuntimeAbi.LocalX, NesWorldPackRuntimeAbi.CellIndex);
        builder.JumpAbsolute(done);

        builder.Label(sameChunk);
        if (usesFastLookup)
        {
            var regularWidth = builder.CreateLabel("nes_packed_column_regular_chunk_width");
            var sourceWidth = descriptor.HardwareWidth / descriptor.MetatileWidth;
            var lastWidth = sourceWidth - ((descriptor.ChunkColumns - 1) * 8);

            builder.LoadYImmediate(8);
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.ChunkXLow);
            builder.CompareImmediate(descriptor.ChunkColumns - 1);
            builder.BranchRelative(0xD0, regularWidth);
            builder.LoadYImmediate(lastWidth);
            builder.Label(regularWidth);
            builder.TransferYToA();
            builder.ClearCarry();
            builder.AddAbsolute(NesWorldPackRuntimeAbi.CellIndex);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.CellIndex);
        }
        else
        {
            builder.ClearCarry();
            builder.LoadAAbsolute(NesWorldPackRuntimeAbi.CellIndex);
            builder.AddAbsolute(NesWorldPackRuntimeAbi.ValidWidth);
            builder.StoreAAbsolute(NesWorldPackRuntimeAbi.CellIndex);
        }

        builder.JumpAbsolute(done);

        builder.Label(sameMetatile);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SubcellIndexLow);
        builder.AddImmediate(descriptor.MetatileWidth);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SubcellIndexLow);
        builder.LoadAAbsolute(NesWorldPackRuntimeAbi.SubcellIndexHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.SubcellIndexHigh);
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

        EmitAlignedOrthogonalBlock(builder, NesPackedCameraRuntime.AttributeBlockXLow);
        EmitShiftedWord(
            builder,
            NesPackedCameraRuntime.CommitWorldEdgeLow,
            NesPackedCameraRuntime.AttributeBlockYLow,
            shifts: 2);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.BranchRelative(0xD0, coordinatesReady);
        for (var index = 0; index < 2; index++)
        {
            builder.LoadXAbsolute(checked((ushort)(NesPackedCameraRuntime.AttributeBlockXLow + index)));
            builder.LoadAAbsolute(checked((ushort)(NesPackedCameraRuntime.AttributeBlockYLow + index)));
            if (index == 1)
            {
                // CMP established carry on the column path. SBC by the two's
                // complement adds the page-aligned column-table displacement
                // without spending scarce fixed-bank bytes on a second add.
                builder.SubtractImmediate(0x100 - (plan.Attributes.ColumnOffset >> 8));
            }
            builder.StoreAAbsolute(checked((ushort)(NesPackedCameraRuntime.AttributeBlockXLow + index)));
            builder.StoreXAbsolute(checked((ushort)(NesPackedCameraRuntime.AttributeBlockYLow + index)));
        }

        builder.Label(coordinatesReady);
        EmitCopy(builder, NesPackedCameraRuntime.AttributeBlockXLow, NesPackedCameraRuntime.AttributeIndexLow);
        EmitCopy(builder, NesPackedCameraRuntime.AttributeBlockXHigh, NesPackedCameraRuntime.AttributeIndexHigh);

        builder.Label(multiplyLoop);
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeBlockYLow);
        builder.OrAbsolute(NesPackedCameraRuntime.AttributeBlockYHigh);
        builder.BranchRelative(0xF0, multiplyDone);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeIndexLow);
        builder.AddImmediate(plan.Attributes.Columns & 0xFF);
        builder.StoreAAbsolute(NesPackedCameraRuntime.AttributeIndexLow);
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeIndexHigh);
        builder.AddImmediate((plan.Attributes.Columns >> 8) & 0xFF);
        builder.StoreAAbsolute(NesPackedCameraRuntime.AttributeIndexHigh);
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeBlockYLow);
        builder.BranchRelative(0xD0, decrementLow);
        builder.DecrementAbsolute(NesPackedCameraRuntime.AttributeBlockYHigh);
        builder.Label(decrementLow);
        builder.DecrementAbsolute(NesPackedCameraRuntime.AttributeBlockYLow);
        builder.JumpAbsolute(multiplyLoop);

        builder.Label(multiplyDone);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.CommitPayloadLength);
        builder.AddImmediate(3);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAAbsolute(NesPackedCameraRuntime.AttributeCount);
        builder.LoadAImmediate(32);
        builder.StoreAAbsolute(NesPackedCameraRuntime.Iterator);

        builder.Label(copyLoop);
        builder.LoadAImmediateLabelLowByte(NesRomBuilder.WorldPackAttributesLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.AttributeIndexLow);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAImmediateLabelHighByte(NesRomBuilder.WorldPackAttributesLabel);
        builder.AddAbsolute(NesPackedCameraRuntime.AttributeIndexHigh);
        builder.StoreAZeroPage(PointerHigh);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(PointerLow);
        builder.StoreAAbsolute(NesPackedCameraRuntime.Status);
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationLow);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationHigh);
        builder.StoreAZeroPage(PointerHigh);
        builder.LoadYAbsolute(NesPackedCameraRuntime.Iterator);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Status);
        builder.StoreAIndirectY(PointerLow);
        builder.IncrementAbsolute(NesPackedCameraRuntime.Iterator);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitAxis);
        builder.CompareImmediate(NesPackedCameraRuntime.Column);
        builder.BranchRelative(0xF0, incrementColumn);
        builder.IncrementAbsolute(NesPackedCameraRuntime.AttributeIndexLow);
        builder.BranchRelative(0xD0, indexReady);
        builder.IncrementAbsolute(NesPackedCameraRuntime.AttributeIndexHigh);
        builder.JumpAbsolute(incrementDone);
        builder.Label(incrementColumn);
        builder.ClearCarry();
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeIndexLow);
        builder.AddImmediate(plan.Attributes.Columns & 0xFF);
        builder.StoreAAbsolute(NesPackedCameraRuntime.AttributeIndexLow);
        if (plan.Attributes.Columns <= byte.MaxValue)
        {
            builder.BranchRelative(0x90, incrementDone);
            builder.IncrementAbsolute(NesPackedCameraRuntime.AttributeIndexHigh);
        }
        else
        {
            builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeIndexHigh);
            builder.AddImmediate((plan.Attributes.Columns >> 8) & 0xFF);
            builder.StoreAAbsolute(NesPackedCameraRuntime.AttributeIndexHigh);
        }
        builder.Label(incrementDone);
        builder.Label(indexReady);
        builder.DecrementAbsolute(NesPackedCameraRuntime.AttributeCount);
        builder.BranchRelative(0xD0, copyLoop);
    }

    private static void EmitAlignedOrthogonalBlock(PrgBuilder builder, ushort destinationLow)
    {
        EmitCopy(builder, NesPackedCameraRuntime.CommitOrthogonalLow, destinationLow);
        EmitCopy(builder, NesPackedCameraRuntime.CommitOrthogonalHigh, checked((ushort)(destinationLow + 1)));
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitTargetStart);
        builder.AndImmediate(0x03);
        builder.StoreAZeroPage(PointerLow);
        builder.SetCarry();
        builder.LoadAAbsolute(destinationLow);
        builder.SubtractZeroPage(PointerLow);
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
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitOrthogonalLow);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.Iterator);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareXLow);
        builder.LoadAAbsolute(NesPackedCameraRuntime.CommitOrthogonalHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesWorldPackRuntimeAbi.HardwareXHigh);
        EmitCopy(builder, NesPackedCameraRuntime.CommitWorldEdgeLow, NesWorldPackRuntimeAbi.HardwareYLow);
        EmitCopy(builder, NesPackedCameraRuntime.CommitWorldEdgeHigh, NesWorldPackRuntimeAbi.HardwareYHigh);
    }

    private static void EmitSetSelectedState(PrgBuilder builder, byte state)
    {
        var slot1 = builder.CreateLabel("nes_packed_state_slot_1");
        var done = builder.CreateLabel("nes_packed_state_done");
        builder.LoadAAbsolute(NesPackedCameraRuntime.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpIf(0xF0, slot1);
        builder.LoadAImmediate(state);
        builder.StoreAAbsolute(NesPackedCameraRuntime.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        builder.LoadAImmediate(state);
        builder.StoreAAbsolute(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.StateOffset);
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
        builder.StoreAAbsolute(NesPackedCameraRuntime.DestinationLow);
        builder.LoadAImmediate(edge.Start >> 8);
        builder.StoreAAbsolute(NesPackedCameraRuntime.DestinationHigh);
    }

    private static void EmitLoadPayloadByte(PrgBuilder builder)
    {
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationLow);
        builder.StoreAZeroPage(PointerLow);
        builder.LoadAAbsolute(NesPackedCameraRuntime.DestinationHigh);
        builder.StoreAZeroPage(PointerHigh);
        builder.LoadYAbsolute(NesPackedCameraRuntime.Iterator);
        builder.LoadAIndirectY(PointerLow);
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
        builder.StoreAAbsolute(NesPackedCameraRuntime.Status);
        builder.LoadAAbsolute(columnAddress);
        builder.CompareImmediate(32);
        builder.JumpIf(0x90, leftNameTable);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Status);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.JumpAbsolute(highReady);
        builder.Label(leftNameTable);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Status);
        builder.Label(highReady);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAAbsolute(columnAddress);
        builder.AndImmediate(0x1F);
        builder.StoreAAbsolute(NesPackedCameraRuntime.AddressColumn);
        builder.LoadXAbsolute(rowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.PpuRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.AddressColumn);
        builder.StoreAAbsolute(0x2006);
    }

    private static void EmitSetPpuAttributeAddress(PrgBuilder builder)
    {
        var leftNameTable = builder.CreateLabel("nes_packed_row_attr_left_nt");
        var highReady = builder.CreateLabel("nes_packed_row_attr_high_ready");
        builder.LoadXAbsolute(NesPackedCameraRuntime.CommitTarget);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressHighLabel);
        builder.StoreAAbsolute(NesPackedCameraRuntime.Status);
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeBlock);
        builder.CompareImmediate(8);
        builder.JumpIf(0x90, leftNameTable);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Status);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.JumpAbsolute(highReady);
        builder.Label(leftNameTable);
        builder.LoadAAbsolute(NesPackedCameraRuntime.Status);
        builder.Label(highReady);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAAbsolute(NesPackedCameraRuntime.AttributeBlock);
        builder.AndImmediate(0x07);
        builder.StoreAAbsolute(NesPackedCameraRuntime.AddressColumn);
        builder.LoadXAbsolute(NesPackedCameraRuntime.CommitTarget);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.AddressColumn);
        builder.StoreAAbsolute(0x2006);
    }

    private static void EmitSetPpuAttributeAddressForColumn(PrgBuilder builder)
    {
        builder.LoadXAbsolute(NesPackedCameraRuntime.TargetCursor);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressHighLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.Status);
        builder.StoreAAbsolute(0x2006);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddAbsolute(NesPackedCameraRuntime.AddressColumn);
        builder.StoreAAbsolute(0x2006);
    }
}
