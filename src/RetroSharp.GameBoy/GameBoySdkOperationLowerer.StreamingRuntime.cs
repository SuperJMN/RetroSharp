namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    private void EmitRequestPackedColumn(
        byte direction,
        ushort sourceLow,
        ushort? sourceHigh,
        ushort targetColumn)
    {
        builder.LoadAImmediate(GameBoyPackedCameraRuntime.Column);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.LoadAImmediate(direction);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitDirection);
        EmitCopyWordOrZeroHigh(sourceLow, sourceHigh, GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        EmitCopyByte(targetColumn, GameBoyRuntimeMemoryLayout.PackedCamera.CommitTarget);
        EmitCopyByte(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, GameBoyRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        EmitCopyWordOrZeroHigh(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow, null, GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow, GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackPrepareEdgeLabel);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PreparedSlot);
    }

    private void EmitRequestPackedRow(
        byte direction,
        ushort sourceRow,
        ushort targetRow,
        GameBoyCameraConfig config,
        int cursorDelta)
    {
        builder.LoadAImmediate(GameBoyPackedCameraRuntime.Row);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.LoadAImmediate(direction);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitDirection);
        EmitStoreAddressWithDeltaModulo(sourceRow, config.SourceHeight, cursorDelta, GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        EmitStoreAddressWithDeltaModulo(targetRow, 32, cursorDelta, GameBoyRuntimeMemoryLayout.PackedCamera.CommitTarget);
        var targetReady = builder.CreateLabel("packed_camera_row_target_start_ready");
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn);
        builder.AddAImmediate(1);
        builder.CompareImmediate(32);
        builder.JumpAbsolute(0xDA, targetReady);
        builder.SubtractAImmediate(32);
        builder.Label(targetReady);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        EmitCopyWordOrZeroHigh(
            GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn,
            config.MapWidth > byte.MaxValue ? GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh : null,
            GameBoyRuntimeMemoryLayout.PackedCamera.IteratorLow,
            GameBoyRuntimeMemoryLayout.PackedCamera.IteratorHigh);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackPrepareEdgeLabel);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.PreparedSlot);
    }

    private void EmitStoreAddressWithDeltaModulo(ushort source, int modulo, int delta, ushort target)
    {
        var adjusted = builder.CreateLabel("packed_camera_cursor_delta_adjusted");
        builder.LoadA(source);
        if (delta > 0)
        {
            builder.AddAImmediate(1);
            builder.CompareImmediate(modulo);
            builder.JumpAbsolute(0xC2, adjusted);
            builder.LoadAImmediate(0);
        }
        else
        {
            builder.CompareImmediate(0);
            var decrement = builder.CreateLabel("packed_camera_cursor_delta_decrement");
            builder.JumpAbsolute(0xC2, decrement);
            builder.LoadAImmediate(modulo - 1);
            builder.JumpAbsolute(adjusted);
            builder.Label(decrement);
            builder.SubtractAImmediate(1);
        }

        builder.Label(adjusted);
        builder.StoreA(target);
    }

    private void EmitQueuePreparedPackedColumn(
        byte direction,
        ushort target,
        ushort source,
        ushort? sourceHigh)
    {
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitQueuePreparedPackedStaggeredStream(
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnCount,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSourceHigh,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSourceHigh,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSecondDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSlot,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSecondSlot,
                direction,
                target,
                source,
                sourceHigh);
            return;
        }

        EmitQueuePreparedPackedStream(
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount,
            PendingStreamColumn,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSourceHigh,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSourceHigh,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingSlot,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondSlot,
            direction,
            target,
            source,
            sourceHigh);
    }

    private void EmitQueuePreparedPackedRow(byte direction, ushort target, ushort source)
    {
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitQueuePreparedPackedStaggeredStream(
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowCount,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondSource,
                null,
                null,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSecondDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSlot,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSecondSlot,
                direction,
                target,
                source,
                null);
            return;
        }

        EmitQueuePreparedPackedStream(
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount,
            PendingStreamRow,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSource,
            null,
            null,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingSlot,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondSlot,
            direction,
            target,
            source,
            null);
    }

    private void EmitQueuePreparedPackedStream(
        ushort kindAddress,
        ushort countAddress,
        byte kind,
        ushort targetAddress,
        ushort sourceAddress,
        ushort secondTargetAddress,
        ushort secondSourceAddress,
        ushort? sourceHighTarget,
        ushort? secondSourceHighTarget,
        ushort directionAddress,
        ushort secondDirectionAddress,
        ushort slotAddress,
        ushort secondSlotAddress,
        byte direction,
        ushort target,
        ushort source,
        ushort? sourceHigh)
    {
        var second = builder.CreateLabel("packed_camera_queue_second");
        var done = builder.CreateLabel("packed_camera_queue_done");
        builder.LoadA(kindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xC2, second);
        EmitStorePackedQueueEntry(targetAddress, sourceAddress, sourceHighTarget, directionAddress, slotAddress, direction, target, source, sourceHigh);
        builder.LoadAImmediate(kind);
        builder.StoreA(kindAddress);
        builder.LoadAImmediate(PendingStreamSingle);
        builder.StoreA(countAddress);
        builder.JumpAbsolute(done);
        builder.Label(second);
        EmitStorePackedQueueEntry(secondTargetAddress, secondSourceAddress, secondSourceHighTarget, secondDirectionAddress, secondSlotAddress, direction, target, source, sourceHigh);
        builder.LoadAImmediate(PendingStreamDouble);
        builder.StoreA(countAddress);
        builder.Label(done);
    }

    private void EmitQueuePreparedPackedStaggeredStream(
        ushort kindAddress,
        ushort countAddress,
        ushort targetAddress,
        ushort sourceAddress,
        ushort secondTargetAddress,
        ushort secondSourceAddress,
        ushort? sourceHighTarget,
        ushort? secondSourceHighTarget,
        ushort directionAddress,
        ushort secondDirectionAddress,
        ushort slotAddress,
        ushort secondSlotAddress,
        byte direction,
        ushort target,
        ushort source,
        ushort? sourceHigh)
    {
        EmitQueuePreparedPackedStream(
            kindAddress,
            countAddress,
            PendingStreamQueued,
            targetAddress,
            sourceAddress,
            secondTargetAddress,
            secondSourceAddress,
            sourceHighTarget,
            secondSourceHighTarget,
            directionAddress,
            secondDirectionAddress,
            slotAddress,
            secondSlotAddress,
            direction,
            target,
            source,
            sourceHigh);
    }

    private void EmitStorePackedQueueEntry(
        ushort targetAddress,
        ushort sourceAddress,
        ushort? sourceHighTarget,
        ushort directionAddress,
        ushort slotAddress,
        byte direction,
        ushort target,
        ushort source,
        ushort? sourceHigh)
    {
        EmitCopyByte(target, targetAddress);
        EmitCopyByte(source, sourceAddress);
        if (sourceHighTarget is { } highTarget)
        {
            if (sourceHigh is { } highSource)
            {
                EmitCopyByte(highSource, highTarget);
            }
            else
            {
                builder.LoadAImmediate(0);
                builder.StoreA(highTarget);
            }
        }

        builder.LoadAImmediate(direction);
        builder.StoreA(directionAddress);
        EmitCopyByte(GameBoyRuntimeMemoryLayout.PackedCamera.PreparedSlot, slotAddress);
    }

    private void EmitCopyWordOrZeroHigh(ushort sourceLow, ushort? sourceHigh, ushort targetLow, ushort targetHigh)
    {
        EmitCopyByte(sourceLow, targetLow);
        if (sourceHigh is { } high)
        {
            EmitCopyByte(high, targetHigh);
        }
        else
        {
            builder.LoadAImmediate(0);
            builder.StoreA(targetHigh);
        }
    }

    private void EmitCopyByte(ushort source, ushort target)
    {
        builder.LoadA(source);
        builder.StoreA(target);
    }

    private void EmitMapStreamColumnFromAddresses(ushort targetColumnAddress, ushort sourceColumnAddress, int y, int height)
    {
        EmitMapStreamColumnFromAddresses(targetColumnAddress, sourceColumnAddress, y, height, GameBoyRomBuilder.MapRowLabel);
    }

    private void EmitMapStreamColumnFromAddresses(ushort targetColumnAddress, ushort sourceColumnAddress, int y, int height, Func<int, string> rowLabel)
    {
        for (var row = 0; row < height; row++)
        {
            builder.LoadA(sourceColumnAddress);
            EmitReadOnlyMapByteAtSourceColumnInA(rowLabel(row));
            builder.LoadBFromA();

            var rowAddress = 0x9800 + (y + row) * 32;
            builder.LoadA(targetColumnAddress);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitBackgroundStreamColumnFromAddresses(
        ushort targetColumnAddress,
        ushort sourceColumnAddress,
        ushort sourceColumnHighAddress,
        GameBoyCameraConfig config)
    {
        var visibleBackgroundRows = Math.Min(program.BackgroundStreamHeight, VisibleScreenTileHeight);
        if (visibleBackgroundRows <= 0)
        {
            return;
        }

        var loopLabel = builder.CreateLabel("background_stream_column_loop");
        var sourceNoCarryLabel = builder.CreateLabel("background_stream_column_source_no_carry");
        var targetNoCarryLabel = builder.CreateLabel("background_stream_column_target_no_carry");

        builder.LoadHl(GameBoyRomBuilder.BackgroundRowLabel(0));
        builder.LoadA(sourceColumnAddress);
        builder.LoadEFromA();
        if (config.MapWidth <= byte.MaxValue)
        {
            builder.LoadDImmediate(0);
        }
        else
        {
            builder.LoadA(sourceColumnHighAddress);
            builder.LoadDFromA();
        }
        builder.AddHlDe();
        EmitLoadDeFromHl();

        builder.LoadA(targetColumnAddress);
        builder.LoadLFromA();
        builder.LoadHImmediate(0x98);
        builder.Emit(0x0E, (byte)visibleBackgroundRows); // LD C,visibleBackgroundRows

        builder.Label(loopLabel);
        builder.Emit(0x1A); // LD A,(DE)
        builder.StoreHlA();

        if (config.MapWidth <= byte.MaxValue)
        {
            builder.LoadAFromE();
            builder.AddAImmediate(config.MapWidth);
            builder.LoadEFromA();
            builder.JumpAbsolute(0xD2, sourceNoCarryLabel); // JP NC,sourceNoCarryLabel
            builder.Emit(0x14); // INC D
            builder.Label(sourceNoCarryLabel);
        }
        else
        {
            EmitAddConstantToDe(config.MapWidth);
        }

        builder.LoadAFromL();
        builder.AddAImmediate(BackgroundTileMapWidth);
        builder.LoadLFromA();
        builder.JumpAbsolute(0xD2, targetNoCarryLabel); // JP NC,targetNoCarryLabel
        builder.Emit(0x24); // INC H
        builder.Label(targetNoCarryLabel);

        builder.Emit(0x0D); // DEC C
        builder.JumpRelative(0x20, loopLabel); // JR NZ,loopLabel
    }

    private void EmitVisibleWorldStreamColumnFromAddresses(
        ushort targetColumnAddress,
        ushort sourceColumnAddress,
        ushort sourceColumnHighAddress,
        GameBoyCameraConfig config)
    {
        var streamRows = Math.Max(0, Math.Min(config.StreamHeight, VisibleScreenTileHeight + 1));
        if (streamRows == 0)
        {
            return;
        }

        Sdk2DOperationValidator.Validate(
            GameBoyTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: config.StreamY, Height: streamRows));

        var loopLabel = builder.CreateLabel("map_stream_column_loop");
        var sourceNoCarryLabel = builder.CreateLabel("map_stream_column_source_no_carry");
        var targetNoCarryLabel = builder.CreateLabel("map_stream_column_target_no_carry");
        var targetNoWrapLabel = builder.CreateLabel("map_stream_column_target_no_wrap");
        var endLabel = builder.CreateLabel("map_stream_column_end");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamSourceRowScratch);
        EmitLoadMapRowPointerToHl(GameBoyRuntimeMemoryLayout.Camera.StreamSourceRowScratch);
        builder.LoadA(sourceColumnAddress);
        builder.LoadEFromA();
        if (config.MapWidth <= byte.MaxValue)
        {
            builder.LoadDImmediate(0);
        }
        else
        {
            builder.LoadA(sourceColumnHighAddress);
            builder.LoadDFromA();
        }
        builder.AddHlDe();
        EmitLoadDeFromHl();
        builder.LoadA(targetColumnAddress);
        builder.LoadCFromA();
        EmitBackgroundTileAddressToHl(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow);
        builder.Emit(0x0E, (byte)streamRows); // LD C,streamRows

        builder.Label(loopLabel);
        builder.Emit(0x1A); // LD A,(DE)
        builder.StoreHlA();

        if (config.MapWidth <= byte.MaxValue)
        {
            builder.LoadAFromE();
            builder.AddAImmediate(config.MapWidth);
            builder.LoadEFromA();
            builder.JumpAbsolute(0xD2, sourceNoCarryLabel); // JP NC,sourceNoCarryLabel
            builder.Emit(0x14); // INC D
            builder.Label(sourceNoCarryLabel);
        }
        else
        {
            EmitAddConstantToDe(config.MapWidth);
        }

        builder.LoadAFromL();
        builder.AddAImmediate(BackgroundTileMapWidth);
        builder.LoadLFromA();
        builder.JumpAbsolute(0xD2, targetNoCarryLabel); // JP NC,targetNoCarryLabel
        builder.Emit(0x24); // INC H
        builder.LoadAFromH();
        builder.CompareImmediate(0x9C);
        builder.JumpAbsolute(0xC2, targetNoWrapLabel); // JP NZ,targetNoWrapLabel
        builder.LoadHImmediate(0x98);
        builder.Label(targetNoWrapLabel);
        builder.Label(targetNoCarryLabel);

        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(0xC2, loopLabel); // JP NZ,loopLabel
        builder.Label(endLabel);
    }

    private void EmitMapStreamRowFromSourceRowAddress(ushort targetRowAddress, ushort sourceRowAddress, GameBoyCameraConfig config)
    {
        Sdk2DOperationValidator.Validate(
            GameBoyTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(TargetRow: 0, SourceRow: 0, X: 0, Width: VisibleScreenTileWidth + 1));

        EmitLoadMapRowPointerToHl(sourceRowAddress);
        builder.LoadAFromL();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamRowDataLow);
        builder.LoadAFromH();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamRowDataHigh);
        EmitMapStreamRowFromSelectedSourceRow(targetRowAddress, config.MapWidth);
    }

    private void EmitMapStreamRowFromSelectedSourceRow(ushort targetRowAddress, int mapWidth)
    {
        var targetInitialReadyLabel = builder.CreateLabel("map_stream_row_initial_target_ready");
        var firstCountReadyLabel = builder.CreateLabel("map_stream_row_first_count_ready");
        var secondSegmentLabel = builder.CreateLabel("map_stream_row_second_segment");
        var endLabel = builder.CreateLabel("map_stream_row_copy_end");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn);
        builder.AddAImmediate(1);
        builder.CompareImmediate(BackgroundTileMapWidth);
        builder.JumpAbsolute(0xDA, targetInitialReadyLabel); // JP C,targetInitialReadyLabel
        builder.SubtractAImmediate(BackgroundTileMapWidth);
        builder.Label(targetInitialReadyLabel);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamTargetColumnScratch);
        builder.LoadBFromA();
        builder.LoadAImmediate(BackgroundTileMapWidth);
        builder.SubtractB();
        builder.CompareImmediate(VisibleScreenTileWidth + 2);
        builder.JumpAbsolute(0xDA, firstCountReadyLabel); // JP C,firstCountReadyLabel
        builder.LoadAImmediate(VisibleScreenTileWidth + 1);
        builder.Label(firstCountReadyLabel);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamColumnsRemaining);
        builder.LoadBFromA();
        builder.LoadAImmediate(VisibleScreenTileWidth + 1);
        builder.SubtractB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamSourceColumnScratch);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
        builder.LoadBFromA();

        EmitLoadSourceRowPointerToDe();
        EmitLoadHlFromDe();
        if (mapWidth <= byte.MaxValue)
        {
            builder.LoadAFromB();
            builder.LoadEFromA();
            builder.LoadDImmediate(0);
        }
        else
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnLow);
            builder.LoadEFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnHigh);
            builder.LoadDFromA();
        }

        builder.AddHlDe();
        EmitLoadDeFromHl();
        if (mapWidth <= byte.MaxValue)
        {
            // B is no longer needed as the logical source column once DE points at the first
            // tile. Keep the number of tiles until the source-row wrap instead. This removes
            // the per-tile LD/CP pair from the VBlank-critical row streamer.
            builder.LoadAImmediate(mapWidth);
            builder.SubtractB();
            builder.LoadBFromA();
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamTargetColumnScratch);
        builder.LoadCFromA();
        EmitBackgroundTileAddressToHl(targetRowAddress);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamColumnsRemaining);
        builder.LoadCFromA();
        EmitMapStreamRowCopyVisibleSegment(mapWidth);

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamSourceColumnScratch);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel); // JP Z,endLabel
        builder.Label(secondSegmentLabel);
        builder.Emit(0x0E, 0x00); // LD C,0
        EmitBackgroundTileAddressToHl(targetRowAddress);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamSourceColumnScratch);
        builder.LoadCFromA();
        EmitMapStreamRowCopyVisibleSegment(mapWidth);
        builder.Label(endLabel);
    }

    private void EmitMapStreamRowCopyVisibleSegment(int mapWidth)
    {
        var loopLabel = builder.CreateLabel("map_stream_row_segment_loop");
        var sourceReadyLabel = builder.CreateLabel("map_stream_row_segment_source_ready");

        builder.Label(loopLabel);
        builder.Emit(0x1A); // LD A,(DE)
        builder.Emit(0x22); // LD (HL+),A
        builder.Emit(0x13); // INC DE
        if (mapWidth <= byte.MaxValue)
        {
            builder.Emit(0x05); // DEC B
            builder.JumpRelative(0x20, sourceReadyLabel); // JR NZ,sourceReadyLabel
            builder.Emit(0x06, checked((byte)mapWidth)); // LD B,mapWidth
            EmitLoadSourceRowPointerToDe();
        }
        else
        {
            var noCarryLabel = builder.CreateLabel("map_stream_row_source_no_carry");

            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnLow);
            builder.AddAImmediate(1);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnLow);
            builder.JumpAbsolute(0xD2, noCarryLabel); // JP NC,noCarryLabel
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnHigh);
            builder.AddAImmediate(1);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnHigh);
            builder.Label(noCarryLabel);

            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnHigh);
            builder.CompareImmediate((mapWidth >> 8) & 0xFF);
            builder.JumpAbsolute(0xC2, sourceReadyLabel); // JP NZ,sourceReadyLabel
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnLow);
            builder.CompareImmediate(mapWidth & 0xFF);
            builder.JumpAbsolute(0xC2, sourceReadyLabel); // JP NZ,sourceReadyLabel
            builder.LoadAImmediate(0);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnLow);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.StreamLogicalSourceColumnHigh);
            EmitLoadSourceRowPointerToDe();
        }

        builder.Label(sourceReadyLabel);
        builder.Emit(0x0D); // DEC C
        builder.JumpRelative(0x20, loopLabel); // JR NZ,loopLabel
    }

    private void EmitLoadSourceRowPointerToDe()
    {
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamRowDataLow);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamRowDataHigh);
        builder.LoadDFromA();
    }

    private void EmitLoadMapRowPointerToHl(ushort sourceRowAddress)
    {
        builder.LoadA(sourceRowAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(GameBoyRomBuilder.MapRowPointerLowLabel);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.LoadCFromA();

        builder.LoadA(sourceRowAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(GameBoyRomBuilder.MapRowPointerHighLabel);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.LoadHFromA();
        builder.LoadAFromC();
        builder.LoadLFromA();
    }

    private void EmitLoadDeFromHl()
    {
        builder.Emit(0x54); // LD D,H
        builder.Emit(0x5D); // LD E,L
    }

    private void EmitLoadHlFromDe()
    {
        builder.Emit(0x62); // LD H,D
        builder.LoadLFromE();
    }

    private void EmitAddConstantToDe(int value)
    {
        builder.LoadAFromE();
        builder.AddAImmediate(value & 0xFF);
        builder.LoadEFromA();
        builder.LoadAFromD();
        builder.Emit(0xCE, (byte)((value >> 8) & 0xFF)); // ADC A,high(value)
        builder.LoadDFromA();
    }

    private void EmitVisibleBackgroundColumnToC(int screenColumn)
    {
        var endLabel = builder.CreateLabel("camera_row_target_column_end");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn);
        builder.AddAImmediate(1 + screenColumn);
        builder.CompareImmediate(32);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(32);
        builder.Label(endLabel);
        builder.LoadCFromA();
    }

    private void EmitBackgroundTileAddressToHl(ushort rowAddress)
    {
        EmitBackgroundTileAddressHighToH(rowAddress);

        builder.LoadA(rowAddress);
        for (var i = 0; i < 5; i++)
        {
            builder.AddAFromA();
        }

        builder.AddAFromC();
        builder.LoadLFromA();
    }

    private void EmitBackgroundTileAddressHighToH(ushort rowAddress)
    {
        var high98Label = builder.CreateLabel("camera_row_high_98");
        var high99Label = builder.CreateLabel("camera_row_high_99");
        var high9ALabel = builder.CreateLabel("camera_row_high_9a");
        var endLabel = builder.CreateLabel("camera_row_high_end");

        builder.LoadA(rowAddress);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xDA, high98Label); // JP C,high98Label
        builder.CompareImmediate(16);
        builder.JumpAbsolute(0xDA, high99Label); // JP C,high99Label
        builder.CompareImmediate(24);
        builder.JumpAbsolute(0xDA, high9ALabel); // JP C,high9ALabel

        builder.LoadHImmediate(0x9B);
        builder.JumpAbsolute(endLabel);
        builder.Label(high98Label);
        builder.LoadHImmediate(0x98);
        builder.JumpAbsolute(endLabel);
        builder.Label(high99Label);
        builder.LoadHImmediate(0x99);
        builder.JumpAbsolute(endLabel);
        builder.Label(high9ALabel);
        builder.LoadHImmediate(0x9A);
        builder.Label(endLabel);
    }

    private void EmitIncrement16(ushort lowAddress, ushort highAddress)
    {
        var endLabel = builder.CreateLabel("increment16_end");

        builder.LoadA(lowAddress);
        builder.AddAImmediate(1);
        builder.StoreA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadA(highAddress);
        builder.AddAImmediate(1);
        builder.StoreA(highAddress);
        builder.Label(endLabel);
    }

    private void EmitDecrement16(ushort lowAddress, ushort highAddress)
    {
        var noBorrowLabel = builder.CreateLabel("decrement16_no_borrow");

        builder.LoadA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, noBorrowLabel); // JP NZ,noBorrowLabel

        builder.LoadA(highAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(highAddress);

        builder.Label(noBorrowLabel);
        builder.LoadA(lowAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(lowAddress);
    }

    private void EmitIncrementWordAddressModulo(ushort lowAddress, ushort highAddress, int modulo)
    {
        var endLabel = builder.CreateLabel("increment_word_modulo_end");

        EmitIncrement16(lowAddress, highAddress);
        builder.LoadA(highAddress);
        builder.CompareImmediate((modulo >> 8) & 0xFF);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadA(lowAddress);
        builder.CompareImmediate(modulo & 0xFF);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadAImmediate(0);
        builder.StoreA(lowAddress);
        builder.StoreA(highAddress);
        builder.Label(endLabel);
    }

    private void EmitDecrementWordAddressModulo(ushort lowAddress, ushort highAddress, int modulo)
    {
        var decrementLabel = builder.CreateLabel("decrement_word_modulo_value");
        var endLabel = builder.CreateLabel("decrement_word_modulo_end");

        builder.LoadA(highAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, decrementLabel); // JP NZ,decrementLabel
        builder.LoadA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, decrementLabel); // JP NZ,decrementLabel
        context.EmitStoreSplitWordImmediate(lowAddress, highAddress, modulo - 1);
        builder.JumpAbsolute(endLabel);

        builder.Label(decrementLabel);
        EmitDecrement16(lowAddress, highAddress);
        builder.Label(endLabel);
    }

    private void EmitIncrementAddressModulo(ushort address, int modulo)
    {
        var endLabel = builder.CreateLabel("increment_modulo_end");

        builder.LoadA(address);
        builder.AddAImmediate(1);
        builder.StoreA(address);
        builder.CompareImmediate(modulo);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadAImmediate(0);
        builder.StoreA(address);
        builder.Label(endLabel);
    }

    private void EmitDecrementAddressModulo(ushort address, int modulo)
    {
        var endLabel = builder.CreateLabel("decrement_modulo_end");

        builder.LoadA(address);
        builder.SubtractAImmediate(1);
        builder.StoreA(address);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadAImmediate(modulo - 1);
        builder.StoreA(address);
        builder.Label(endLabel);
    }
}
