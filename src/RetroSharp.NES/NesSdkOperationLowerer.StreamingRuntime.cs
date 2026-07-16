namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    private void EmitQueuePendingCameraColumn(byte direction, NesCameraConfig config)
    {
        if (usePackedCamera)
        {
            var canRequest = builder.CreateLabel("nes_packed_column_can_request");
            var done = builder.CreateLabel("nes_packed_column_request_done");
            EmitPackedAxisRequestGuard(
                NesPackedCameraRuntime.Column,
                direction,
                NesRuntimeMemoryLayout.PackedCamera.PendingColumn,
                canRequest,
                done);
            builder.Label(canRequest);
            builder.LoadAImmediate(NesPackedCameraRuntime.Column);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
            builder.LoadAImmediate(direction);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitDirection);
            builder.CallSubroutine(NesRomBuilder.WorldPackReleaseReversedEdgeLabel);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
            if (config.MapWidth > byte.MaxValue)
            {
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTarget);
            builder.LoadAImmediate(config.StreamY);
            if (config.UseFourScreenNametables)
            {
                builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow);
                if (config.MapHeight > byte.MaxValue)
                {
                    builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileRowHigh);
                }
                else
                {
                    builder.LoadAImmediate(0);
                }
            }
            else
            {
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow);
            }

            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh);
            builder.LoadAImmediate(Math.Min(NesTarget.Capabilities.ScreenTiles.Height, config.StreamHeight));
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
            builder.CallSubroutine(NesRomBuilder.WorldPackPrepareEdgeLabel);
            builder.CompareImmediate((byte)NesWorldPackResult.Success);
            builder.JumpIf(0xD0, done);
            EmitStorePackedPendingDescriptor(NesRuntimeMemoryLayout.PackedCamera.PendingColumn, NesPackedCameraRuntime.Column);
            builder.LoadAImmediate((byte)NesWorldPackResult.Success);
            builder.Label(done);
            return;
        }

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingColumnTarget);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingColumnSource);
        if (cameraConfig is { MapWidth: > byte.MaxValue })
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingColumnSourceHigh);
        }
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.OrImmediate(PendingStreamColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
    }

    private void EmitQueuePendingCameraRow(byte direction, NesCameraConfig config)
    {
        if (usePackedCamera)
        {
            var canRequest = builder.CreateLabel("nes_packed_row_can_request");
            var done = builder.CreateLabel("nes_packed_row_request_done");
            EmitPackedAxisRequestGuard(
                NesPackedCameraRuntime.Row,
                direction,
                NesRuntimeMemoryLayout.PackedCamera.PendingRow,
                canRequest,
                done);
            builder.Label(canRequest);
            builder.LoadAImmediate(NesPackedCameraRuntime.Row);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitAxis);
            builder.LoadAImmediate(direction);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitDirection);
            builder.CallSubroutine(NesRomBuilder.WorldPackReleaseReversedEdgeLabel);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
            if (config.MapHeight > byte.MaxValue)
            {
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceRowHigh);
            }
            else
            {
                builder.LoadAImmediate(0);
            }
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTarget);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow);
            if (config.MapWidth > byte.MaxValue)
            {
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh);
            builder.LoadAImmediate(32);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength);
            builder.CallSubroutine(NesRomBuilder.WorldPackPrepareEdgeLabel);
            builder.CompareImmediate((byte)NesWorldPackResult.Success);
            builder.JumpIf(0xD0, done);
            EmitStorePackedPendingDescriptor(NesRuntimeMemoryLayout.PackedCamera.PendingRow, NesPackedCameraRuntime.Row);
            builder.LoadAImmediate((byte)NesWorldPackResult.Success);
            builder.Label(done);
            return;
        }

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowTarget);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowSource);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowTargetColumn);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowSourceColumn);
        if (cameraConfig is { MapWidth: > byte.MaxValue })
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowSourceColumnHigh);
        }

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.OrImmediate(PendingStreamRow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
    }

    private void EmitPackedAxisRequestGuard(
        byte axis,
        byte direction,
        ushort pending,
        string canRequest,
        string done)
    {
        var pendingExists = builder.CreateLabel("nes_packed_axis_pending_exists");
        var checkSlot1 = builder.CreateLabel("nes_packed_axis_check_slot_1");
        var checkSlot1Axis = builder.CreateLabel("nes_packed_axis_check_slot_1_axis");
        var blocked = builder.CreateLabel("nes_packed_axis_request_blocked");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
        builder.AndImmediate(axis);
        builder.JumpIf(0xD0, pendingExists);
        builder.JumpAbsolute(canRequest);

        builder.Label(pendingExists);
        builder.LoadAAbsolute(checked((ushort)(pending + NesPackedCameraRuntime.DirectionOffset)));
        builder.CompareImmediate(direction);
        builder.JumpIf(0xF0, blocked);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xD0, checkSlot1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.AxisOffset);
        builder.CompareImmediate(axis);
        builder.JumpIf(0xF0, blocked);

        builder.Label(checkSlot1);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset);
        builder.CompareImmediate(NesPackedCameraRuntime.Committing);
        builder.JumpIf(0xF0, checkSlot1Axis);
        builder.JumpAbsolute(canRequest);
        builder.Label(checkSlot1Axis);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.AxisOffset);
        builder.CompareImmediate(axis);
        builder.JumpIf(0xD0, canRequest);

        builder.Label(blocked);
        builder.LoadAImmediate(NesPackedCameraRuntime.NoSlot);
        builder.JumpAbsolute(done);
    }

    private void EmitStorePackedPendingDescriptor(ushort destination, byte axis)
    {
        foreach (var (source, offset) in new (ushort Source, int Offset)[]
                 {
                     (NesRuntimeMemoryLayout.PackedCamera.CommitAxis, NesPackedCameraRuntime.AxisOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitDirection, NesPackedCameraRuntime.DirectionOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, NesPackedCameraRuntime.WorldEdgeLowOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, NesPackedCameraRuntime.WorldEdgeHighOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitTarget, NesPackedCameraRuntime.TargetOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow, NesPackedCameraRuntime.OrthogonalLowOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh, NesPackedCameraRuntime.OrthogonalHighOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength, NesPackedCameraRuntime.PayloadLengthOffset),
                     (NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart, NesPackedCameraRuntime.TargetStartOffset),
                 })
        {
            builder.LoadAAbsolute(source);
            builder.StoreAAbsolute(checked((ushort)(destination + offset)));
        }

        if (axis == NesPackedCameraRuntime.Column)
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
            builder.StoreAAbsolute(checked((ushort)(destination + NesPackedCameraRuntime.StateOffset)));
            if (cameraConfig is { MapWidth: <= byte.MaxValue })
            {
                builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
                for (var shift = 0; shift < 5; shift++)
                {
                    builder.ShiftRightA();
                }
            }
            else
            {
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.XHigh);
            }
            builder.StoreAAbsolute(checked((ushort)(destination + NesPackedCameraRuntime.CommitPhaseOffset)));
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
            builder.StoreAAbsolute(checked((ushort)(destination + NesPackedCameraRuntime.PayloadCursorOffset)));
        }
        else
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
            builder.StoreAAbsolute(checked((ushort)(destination + NesPackedCameraRuntime.StateOffset)));
            if (cameraConfig is { MapHeight: <= byte.MaxValue })
            {
                builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
                for (var shift = 0; shift < 5; shift++)
                {
                    builder.ShiftRightA();
                }
            }
            else
            {
                builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.YHigh);
            }
            builder.StoreAAbsolute(checked((ushort)(destination + NesPackedCameraRuntime.CommitPhaseOffset)));
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            builder.StoreAAbsolute(checked((ushort)(destination + NesPackedCameraRuntime.PayloadCursorOffset)));
        }

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
        builder.OrImmediate(axis);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
    }

    private void EmitCommitPendingCameraStream(NesCameraConfig config)
    {
        if (usePackedCamera)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
            builder.CallSubroutine(NesRomBuilder.WorldPackCommitEdgeLabel);
            EmitPublishCommittedPackedCameraAxis(NesPackedCameraRuntime.Column);
            EmitPublishCommittedPackedCameraAxis(NesPackedCameraRuntime.Row);
            return;
        }

        var canStreamColumns = ShouldStreamColumnsForCamera(config);
        var canStreamRows = ShouldStreamRowsForCamera(config);
        if (!canStreamColumns && !canStreamRows)
        {
            return;
        }

        if (canStreamColumns && canStreamRows)
        {
            EmitCommitStaggeredPendingCameraStream(config);
            return;
        }

        if (canStreamColumns)
        {
            EmitCommitSinglePendingCameraStream(PendingStreamColumn, config);
            return;
        }

        EmitCommitSinglePendingCameraStream(PendingStreamRow, config);
    }

    private void EmitCommitSinglePendingCameraStream(byte kind, NesCameraConfig config)
    {
        var commitLabel = builder.CreateLabel("nes_camera_commit_pending");
        var doneLabel = builder.CreateLabel("nes_camera_commit_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate(kind);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, commitLabel); // BNE commitLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(commitLabel);
        EmitCommitPendingCameraStreamKind(kind, config);

        builder.Label(doneLabel);
    }

    private void EmitCommitStaggeredPendingCameraStream(NesCameraConfig config)
    {
        var checkRowLabel = builder.CreateLabel("nes_camera_commit_check_row");
        var columnPendingLabel = builder.CreateLabel("nes_camera_commit_column_pending");
        var bothPendingLabel = builder.CreateLabel("nes_camera_commit_both_pending");
        var rowNextLabel = builder.CreateLabel("nes_camera_commit_row_next");
        var rowPendingLabel = builder.CreateLabel("nes_camera_commit_row_pending");
        var commitColumnLabel = builder.CreateLabel("nes_camera_commit_column");
        var commitRowLabel = builder.CreateLabel("nes_camera_commit_row");
        var doneLabel = builder.CreateLabel("nes_camera_commit_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate(PendingStreamColumn);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, columnPendingLabel); // BNE columnPendingLabel
        builder.JumpAbsolute(checkRowLabel);

        builder.Label(columnPendingLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate(PendingStreamRow);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, bothPendingLabel); // BNE bothPendingLabel
        builder.JumpAbsolute(commitColumnLabel);

        builder.Label(bothPendingLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);
        builder.CompareImmediate(PendingStreamRow);
        builder.BranchRelative(0xF0, rowNextLabel); // BEQ rowNextLabel
        builder.JumpAbsolute(commitColumnLabel);

        builder.Label(rowNextLabel);
        builder.JumpAbsolute(commitRowLabel);

        builder.Label(checkRowLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate(PendingStreamRow);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, rowPendingLabel); // BNE rowPendingLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(rowPendingLabel);
        builder.JumpAbsolute(commitRowLabel);

        builder.Label(commitColumnLabel);
        EmitCommitPendingCameraStreamKind(PendingStreamColumn, config);
        builder.LoadAImmediate(PendingStreamRow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);
        builder.JumpAbsolute(doneLabel);

        builder.Label(commitRowLabel);
        EmitCommitPendingCameraStreamKind(PendingStreamRow, config);
        builder.LoadAImmediate(PendingStreamColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);

        builder.Label(doneLabel);
    }

    private void EmitCommitPendingCameraStreamKind(byte kind, NesCameraConfig config)
    {
        if (kind == PendingStreamColumn)
        {
            EmitLoadPendingCameraColumnToStreamAddresses();
            EmitStreamColumnFromAddresses(config);
            EmitStreamColumnAttributes(config);
            EmitClearPendingCameraStream(kind);
        }
        else
        {
            EmitCommitPendingCameraRowStream(config);
        }
    }

    // Refreshes the NES attribute table for the column that was just streamed. Column tile streaming only
    // rewrites nametable tiles; without this the streamed tiles would inherit the palette slot the
    // initial upload left in that buffer position, corrupting background colors as the camera scrolls.
    private void EmitStreamColumnAttributes(NesCameraConfig config)
    {
        var attributes = program.WorldColumnAttributes;
        if (attributes is null)
        {
            return;
        }

        var highRightLabel = builder.CreateLabel("nes_col_attr_high_right");
        var highStoreLabel = builder.CreateLabel("nes_col_attr_high_store");

        builder.LoadAAbsolute(0x2002);                          // reset the PPU address latch (w = 0)

        if (config.MapWidth <= byte.MaxValue)
        {
            // X = streamed source column / 4 (attribute block index), preserved across the row loop.
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.ShiftRightA();
            builder.ShiftRightA();
            builder.TransferAToX();
        }

        // Base attribute address high byte: $23 for the left nametable, $27 for the right one.
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.CompareImmediate(32);
        builder.BranchRelative(0xB0, highRightLabel); // BCS highRightLabel
        builder.LoadAImmediate(0x23);
        builder.JumpAbsolute(highStoreLabel);
        builder.Label(highRightLabel);
        builder.LoadAImmediate(0x27);
        builder.Label(highStoreLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);

        // Base attribute address low byte: $C0 + (targetColumn % 32) / 4.
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.AndImmediate(0x1F);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.OrImmediate(0xC0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);

        for (var row = 0; row < attributes.Rows.Count; row++)
        {
            var descriptor = attributes.Rows[row];

            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
            if (descriptor.HighOffset != 0)
            {
                builder.ClearCarry();
                builder.AddImmediate(descriptor.HighOffset);
            }

            builder.StoreAAbsolute(0x2006);

            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
            if (descriptor.LowOffset != 0)
            {
                builder.ClearCarry();
                builder.AddImmediate(descriptor.LowOffset);
            }

            builder.StoreAAbsolute(0x2006);

            if (config.MapWidth <= byte.MaxValue)
            {
                builder.LdaAbsoluteX(NesRomBuilder.WorldMapColumnAttributeRowLabel(row));
            }
            else
            {
                EmitLoadWideColumnAttributeToA(row);
            }

            builder.StoreAAbsolute(0x2007);
        }
    }

    private void EmitLoadWideColumnAttributeToA(int row)
    {
        var label = NesRomBuilder.WorldMapColumnAttributeRowLabel(row);

        // Build the 16-bit attribute-block index (sourceColumn / 4) in the indirect pointer.
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.Emit(0x46, NesRuntimeMemoryLayout.Runtime.ExpressionScratch); // LSR high
        builder.Emit(0x66, NesRuntimeMemoryLayout.Runtime.IndexScratch); // ROR low
        builder.Emit(0x46, NesRuntimeMemoryLayout.Runtime.ExpressionScratch); // LSR high
        builder.Emit(0x66, NesRuntimeMemoryLayout.Runtime.IndexScratch); // ROR low

        builder.LoadAImmediateLabelLowByte(label);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.LoadAImmediateLabelHighByte(label);
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Runtime.IndexScratch);
    }

    private void EmitLoadPendingCameraColumnToStreamAddresses()
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingColumnTarget);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingColumnSource);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        if (cameraConfig is { MapWidth: > byte.MaxValue })
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingColumnSourceHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        }
    }

    private void EmitLoadPendingCameraRowToStreamAddresses()
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowTarget);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowSource);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowTargetColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowSourceColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        if (cameraConfig is { MapWidth: > byte.MaxValue })
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowSourceColumnHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        }
    }

    private void EmitClearPendingCameraStream(byte kind)
    {
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate(kind == PendingStreamColumn ? 0xFE : 0xFD);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
    }

    private void EmitCommitPendingCameraRowStream(NesCameraConfig config)
    {
        const int tilesPerPhase = 8;
        const int attributePhase = 4;
        var tilesLabel = builder.CreateLabel("nes_camera_row_tiles");
        var attributesLabel = builder.CreateLabel("nes_camera_row_attrs");
        var doneLabel = builder.CreateLabel("nes_camera_row_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.CompareImmediate(attributePhase);
        builder.BranchRelative(0xD0, tilesLabel); // BNE tilesLabel
        builder.JumpAbsolute(attributesLabel);

        builder.Label(tilesLabel);
        EmitLoadPendingCameraRowToStreamAddresses();
        EmitAdvancePendingRowColumnsForPhase(config, tilesPerPhase);
        EmitStreamRowTilesFromAddresses(config, tilesPerPhase);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.JumpAbsolute(doneLabel);

        builder.Label(attributesLabel);
        EmitLoadPendingCameraRowToStreamAddresses();
        EmitPrepareRuntimeRowAttributeColumn();
        EmitStreamRuntimeRowAttributes();
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        EmitClearPendingCameraStream(PendingStreamRow);

        builder.Label(doneLabel);
    }

    private void EmitAdvancePendingRowColumnsForPhase(NesCameraConfig config, int tilesPerPhase)
    {
        var loopLabel = builder.CreateLabel("nes_camera_row_phase_loop");
        var doneLabel = builder.CreateLabel("nes_camera_row_phase_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, doneLabel); // BEQ doneLabel
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);

        builder.Label(loopLabel);
        EmitAddImmediateModulo(NesRuntimeMemoryLayout.Camera.TargetColumn, tilesPerPhase, 64, "nes_camera_row_target_phase");
        if (config.MapWidth <= byte.MaxValue)
        {
            EmitAddImmediateModulo(NesRuntimeMemoryLayout.Camera.SourceColumn, tilesPerPhase, config.MapWidth, "nes_camera_row_source_phase");
        }
        else
        {
            EmitAddImmediateToLogicalCellModulo(
                NesRuntimeMemoryLayout.Camera.SourceColumn,
                NesRuntimeMemoryLayout.Camera.SourceColumnHigh,
                tilesPerPhase,
                config.MapWidth,
                "nes_camera_row_source_phase");
        }

        builder.DecrementZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);
        builder.BranchRelative(0xD0, loopLabel); // BNE loopLabel

        builder.Label(doneLabel);
    }

    private void EmitAddImmediateModulo(byte address, int addend, int modulo, string labelPrefix)
    {
        var loopLabel = builder.CreateLabel($"{labelPrefix}_loop");
        var doneLabel = builder.CreateLabel($"{labelPrefix}_done");

        builder.LoadAZeroPage(address);
        builder.ClearCarry();
        builder.AddImmediate(addend);
        builder.Label(loopLabel);
        builder.CompareImmediate(modulo);
        builder.BranchRelative(0x90, doneLabel); // BCC doneLabel
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.JumpAbsolute(loopLabel);
        builder.Label(doneLabel);
        builder.StoreAZeroPage(address);
    }

    private void EmitStreamColumnFromAddresses(NesCameraConfig config)
    {
        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: config.StreamY, Height: config.StreamHeight));

        builder.LoadAAbsolute(0x2002);              // reset PPU address latch
        for (var row = 0; row < config.StreamHeight; row++)
        {
            EmitStreamColumnRow(row, config.StreamY + row, config.MapWidth > byte.MaxValue);
        }
    }

    private void EmitStreamRowFromAddresses(NesCameraConfig config)
    {
        EmitStreamRowTilesFromAddresses(config, NesTarget.Capabilities.ScreenTiles.Width);
        EmitPrepareRuntimeRowAttributeColumn();
        EmitStreamRuntimeRowAttributes();
    }

    private void EmitStreamRowTilesFromAddresses(NesCameraConfig config, int width)
    {
        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(TargetRow: 0, SourceRow: 0, X: 0, Width: width));

        var segmentLabel = builder.CreateLabel("nes_stream_row_segment");
        var writeLabel = builder.CreateLabel("nes_stream_row_write");
        var sourceNoWrapLabel = builder.CreateLabel("nes_stream_row_source_no_wrap");
        var tilesDoneLabel = builder.CreateLabel("nes_stream_row_tiles_done");
        var resetAddressLabel = builder.CreateLabel("nes_stream_row_reset_address");

        builder.LoadAAbsolute(0x2002);              // reset PPU address latch
        builder.LoadXZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowPointerLowLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowPointerHighLabel);
        if (config.MapWidth > byte.MaxValue)
        {
            builder.ClearCarry();
            builder.AddAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.LoadAImmediate(width);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);

        builder.Label(segmentLabel);
        EmitSetPpuAddressFromTargetRowAndColumn();

        builder.Label(writeLabel);
        builder.LoadYZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.StoreAAbsolute(0x2007);

        if (config.MapWidth <= byte.MaxValue)
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.ClearCarry();
            builder.AddImmediate(1);
            builder.CompareImmediate(config.MapWidth);
            builder.BranchRelative(0x90, sourceNoWrapLabel); // BCC sourceNoWrapLabel
            builder.LoadAImmediate(0);
            builder.Label(sourceNoWrapLabel);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        }
        else
        {
            var noCarryLabel = builder.CreateLabel("nes_stream_row_source_no_carry");

            builder.IncrementZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.BranchRelative(0xD0, noCarryLabel); // BNE noCarryLabel
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            builder.ClearCarry();
            builder.AddImmediate(1);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            builder.IncrementZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
            builder.Label(noCarryLabel);

            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            builder.CompareImmediate((config.MapWidth >> 8) & 0xFF);
            builder.BranchRelative(0xD0, sourceNoWrapLabel); // BNE sourceNoWrapLabel
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.CompareImmediate(config.MapWidth & 0xFF);
            builder.BranchRelative(0xD0, sourceNoWrapLabel); // BNE sourceNoWrapLabel
            builder.LoadAImmediate(0);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            builder.LoadXZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
            builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowPointerLowLabel);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
            builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowPointerHighLabel);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
            builder.Label(sourceNoWrapLabel);
        }

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);

        builder.DecrementZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);
        builder.BranchRelative(0xF0, tilesDoneLabel); // BEQ tilesDoneLabel
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.AndImmediate(0x1F);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, resetAddressLabel); // BEQ resetAddressLabel
        builder.JumpAbsolute(writeLabel);

        builder.Label(resetAddressLabel);
        builder.JumpAbsolute(segmentLabel);

        builder.Label(tilesDoneLabel);
    }

    private void EmitPrepareRuntimeRowAttributeColumn()
    {
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.AndImmediate(0x3C);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
    }

    private void EmitStreamRuntimeRowAttributes()
    {
        var loopLabel = builder.CreateLabel("nes_stream_row_attr_loop");

        builder.LoadAImmediate(9);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);

        builder.Label(loopLabel);
        EmitSetPpuAttributeAddressFromTargetRowAndColumn();
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0x2007);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);

        builder.DecrementZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);
        builder.BranchRelative(0xD0, loopLabel); // BNE loopLabel
    }

    private void EmitSetPpuAttributeAddressFromTargetRowAndColumn()
    {
        var leftNameTableLabel = builder.CreateLabel("nes_stream_attr_left_nt");
        var storeColumnLabel = builder.CreateLabel("nes_stream_attr_store_col");

        builder.LoadXZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressHighLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
        builder.CompareImmediate(32);
        builder.BranchRelative(0x90, leftNameTableLabel); // BCC leftNameTableLabel
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.JumpAbsolute(storeColumnLabel);

        builder.Label(leftNameTableLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);

        builder.Label(storeColumnLabel);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        builder.StoreAAbsolute(0x2006);
        builder.LoadXZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.StoreAAbsolute(0x2006);
    }

    private void EmitSetPpuAddressFromTargetRowAndColumn()
    {
        var leftNameTableLabel = builder.CreateLabel("nes_stream_row_left_nt");
        var storeColumnLabel = builder.CreateLabel("nes_stream_row_store_col");

        builder.LoadXZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
        builder.LdaAbsoluteX(NesRomBuilder.PpuRowAddressHighLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.CompareImmediate(32);
        builder.BranchRelative(0x90, leftNameTableLabel); // BCC leftNameTableLabel
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.JumpAbsolute(storeColumnLabel);

        builder.Label(leftNameTableLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);

        builder.Label(storeColumnLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        builder.StoreAAbsolute(0x2006);
        builder.LoadXZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
        builder.LdaAbsoluteX(NesRomBuilder.PpuRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.StoreAAbsolute(0x2006);
    }

    private void EmitStreamColumnRow(int sourceRow, int targetY, bool usesWordSourceColumn)
    {
        var rightNameTableLabel = builder.CreateLabel("nes_stream_right_nt");
        var writeTileLabel = builder.CreateLabel("nes_stream_write_tile");
        var rowAddress = 0x2000 + targetY * 32;

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.CompareImmediate(32);
        builder.BranchRelative(0xB0, rightNameTableLabel); // BCS rightNameTableLabel

        builder.LoadAImmediate(rowAddress >> 8);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.ClearCarry();
        builder.AddImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);
        builder.JumpAbsolute(writeTileLabel);

        builder.Label(rightNameTableLabel);
        builder.LoadAImmediate((rowAddress >> 8) + 4);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.ClearCarry();
        builder.AddImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);

        builder.Label(writeTileLabel);
        if (usesWordSourceColumn)
        {
            builder.LoadAImmediateLabelLowByte(NesRomBuilder.WorldMapRowLabel(sourceRow));
            builder.ClearCarry();
            builder.AddZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
            builder.LoadAImmediateLabelHighByte(NesRomBuilder.WorldMapRowLabel(sourceRow));
            builder.AddAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
            builder.LoadYImmediate(0);
            builder.LoadAIndirectY(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        }
        else
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.TransferAToX();
            builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowLabel(sourceRow));
        }

        builder.StoreAAbsolute(0x2007);
    }

    private void EmitStreamMapRowSegment(int targetRow, int sourceRow, int x, int width)
    {
        var rowAddress = PpuNameTableTileAddress(x, targetRow);

        builder.LoadAImmediate(rowAddress >> 8);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);

        for (var offset = 0; offset < width; offset++)
        {
            builder.LoadXImmediate(x + offset);
            builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowLabel(sourceRow));
            builder.StoreAAbsolute(0x2007);
        }
    }

    private static int PpuNameTableTileAddress(int x, int y)
    {
        var nameTableX = x / 32;
        var nameTableY = y / 30;
        return 0x2000
               + nameTableY * 0x800
               + nameTableX * 0x400
               + y % 30 * 32
               + x % 32;
    }

    private void EmitStreamMapRowAttributes(int targetRow, int x, int width)
    {
        var firstAttributeColumn = x / 4;
        var lastAttributeColumn = (x + width - 1) / 4;
        for (var attributeColumn = firstAttributeColumn; attributeColumn <= lastAttributeColumn; attributeColumn++)
        {
            var attributeX = attributeColumn * 4;
            var attributeAddress = PpuAttributeAddress(attributeX, targetRow);
            builder.LoadAImmediate(attributeAddress >> 8);
            builder.StoreAAbsolute(0x2006);
            builder.LoadAImmediate(attributeAddress & 0xFF);
            builder.StoreAAbsolute(0x2006);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(0x2007);
        }
    }

    private static int PpuAttributeAddress(int x, int y)
    {
        var nameTableX = x / 32;
        var nameTableY = y / 30;
        return 0x23C0
               + nameTableY * 0x800
               + nameTableX * 0x400
               + (y % 30) / 4 * 8
               + (x % 32) / 4;
    }
}
