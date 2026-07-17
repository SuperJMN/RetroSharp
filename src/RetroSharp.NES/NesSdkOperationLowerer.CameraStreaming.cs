namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    internal void EmitStreamMapColumn(Sdk2DOperation.StreamMapColumn operation)
    {
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("map_stream_column requires world_map(...) data for the NES target.");
        var y = CheckedRange(operation.Y, 0, 29, "map_stream_column argument 3");
        var height = CheckedRange(operation.Height, 1, worldMap.Height, "map_stream_column argument 4");
        if (y + height > 30)
        {
            throw new InvalidOperationException("map_stream_column stream area must fit within the NES visible nametable height.");
        }

        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: y, Height: height));

        EmitSdkByteExpressionToA(operation.TargetColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        EmitSdkByteExpressionToA(operation.SourceColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        EmitWaitFrame();
        EmitStreamColumnFromAddresses(new NesCameraConfig(worldMap.Width, worldMap.Height, y, height, UseFourScreenNametables: false));
    }

    internal void EmitStreamMapRow(Sdk2DOperation.StreamMapRow operation)
    {
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("map_stream_row requires world_map(...) data for the NES target.");
        var targetRow = CheckedRange(operation.TargetRow, 0, 59, "map_stream_row argument 1");
        var sourceRow = CheckedRange(operation.SourceRow, 0, worldMap.Height - 1, "map_stream_row argument 2");
        var x = CheckedRange(operation.X, 0, 63, "map_stream_row argument 3");
        var width = CheckedRange(operation.Width, 1, worldMap.Width, "map_stream_row argument 4");
        if (x + width > 64)
        {
            throw new InvalidOperationException("map_stream_row stream area must fit within the NES four-screen row width.");
        }

        if (x + width > worldMap.Width)
        {
            throw new InvalidOperationException("map_stream_row source span must fit within the declared world map width.");
        }

        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(targetRow, sourceRow, x, width));

        EmitWaitFrame();
        builder.LoadAAbsolute(0x2002);              // reset PPU address latch
        var remaining = width;
        var segmentX = x;
        while (remaining > 0)
        {
            var segmentWidth = Math.Min(remaining, 32 - segmentX % 32);
            EmitStreamMapRowSegment(targetRow, sourceRow, segmentX, segmentWidth);
            segmentX += segmentWidth;
            remaining -= segmentWidth;
        }

        EmitStreamMapRowAttributes(targetRow, x, width);
    }

    private static bool ShouldStreamColumnsForCamera(NesCameraConfig config)
    {
        return config.UseFourScreenNametables ? config.MapWidth > 64 : config.MapWidth > 32;
    }

    private static bool ShouldStreamRowsForCamera(NesCameraConfig config)
    {
        return config.UseFourScreenNametables && config.MapHeight > 60;
    }

    private void EmitTrackCameraXPosition(NesCameraConfig config)
    {
        EmitStreamColumnForCameraPosition(config, streamColumns: false);
    }

    private void EmitStreamColumnForCameraPosition(
        NesCameraConfig config,
        bool streamColumns = true)
    {
        var usesWordCameraX = Math.Max(0, (config.MapWidth - NesTarget.Capabilities.ScreenTiles.Width) * 8) > byte.MaxValue;
        var moveRightLabel = builder.CreateLabel("nes_camera_move_right");
        var moveLeftLabel = builder.CreateLabel("nes_camera_move_left");
        var fallbackLabel = builder.CreateLabel("nes_camera_move_fallback");
        var changedLabel = builder.CreateLabel("nes_camera_changed");
        var rightCrossedTileLabel = builder.CreateLabel("nes_camera_right_crossed_tile");
        var leftCrossedTileLabel = builder.CreateLabel("nes_camera_left_crossed_tile");
        var storeOnlyLabel = builder.CreateLabel("nes_camera_store_only");
        var storeAndStreamRightLabel = builder.CreateLabel("nes_camera_store_stream_right");
        var storeAndStreamLeftLabel = builder.CreateLabel("nes_camera_store_stream_left");
        var publishPackedLabel = builder.CreateLabel("nes_packed_camera_publish_x");
        var endLabel = builder.CreateLabel("nes_camera_stream_end");

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.BranchRelative(0xD0, changedLabel); // BNE changedLabel
        builder.JumpAbsolute(endLabel);

        builder.Label(changedLabel);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.BranchRelative(0xF0, moveRightLabel); // BEQ moveRightLabel

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.BranchRelative(0xF0, moveLeftLabel); // BEQ moveLeftLabel

        builder.JumpAbsolute(fallbackLabel);

        builder.Label(moveRightLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0x07);
        builder.BranchRelative(0xF0, rightCrossedTileLabel); // BEQ rightCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(rightCrossedTileLabel);
        EmitIncrementCameraTile(config.MapWidth);
        builder.JumpAbsolute(storeAndStreamRightLabel);

        builder.Label(moveLeftLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, leftCrossedTileLabel); // BEQ leftCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(leftCrossedTileLabel);
        EmitDecrementCameraTile(config.MapWidth);
        builder.JumpAbsolute(storeAndStreamLeftLabel);

        builder.Label(fallbackLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.JumpAbsolute(storeOnlyLabel);

        builder.Label(storeAndStreamRightLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        if (streamColumns)
        {
            EmitPrepareRightStreamColumn(config);
            EmitQueuePendingCameraColumn(NesPackedCameraRuntime.Positive, config);
            if (usePackedCamera)
            {
                var queued = builder.CreateLabel("nes_packed_camera_right_queued");
                builder.CompareImmediate((byte)NesWorldPackResult.Success);
                builder.JumpIf(0xF0, queued);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);
                EmitDecrementCameraTile(config.MapWidth);
                EmitDecrementCameraPixel(NesRuntimeMemoryLayout.Camera.X, NesRuntimeMemoryLayout.Camera.XHigh, usesWordCameraX);
                builder.JumpAbsolute(publishPackedLabel);
                builder.Label(queued);
                builder.JumpAbsolute(endLabel);
            }
        }
        else if (usePackedCamera)
        {
            builder.JumpAbsolute(publishPackedLabel);
        }
        if (!usePackedCamera)
        {
            builder.JumpAbsolute(endLabel);
        }

        builder.Label(storeAndStreamLeftLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        if (streamColumns)
        {
            EmitPrepareLeftStreamColumn();
            EmitQueuePendingCameraColumn(NesPackedCameraRuntime.Negative, config);
            if (usePackedCamera)
            {
                var queued = builder.CreateLabel("nes_packed_camera_left_queued");
                builder.CompareImmediate((byte)NesWorldPackResult.Success);
                builder.JumpIf(0xF0, queued);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);
                EmitIncrementCameraTile(config.MapWidth);
                EmitIncrementCameraPixel(NesRuntimeMemoryLayout.Camera.X, NesRuntimeMemoryLayout.Camera.XHigh, usesWordCameraX);
                builder.JumpAbsolute(publishPackedLabel);
                builder.Label(queued);
                builder.JumpAbsolute(endLabel);
            }
        }
        else if (usePackedCamera)
        {
            builder.JumpAbsolute(publishPackedLabel);
        }
        if (!usePackedCamera)
        {
            builder.JumpAbsolute(endLabel);
        }

        builder.Label(storeOnlyLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        if (usePackedCamera)
        {
            builder.Label(publishPackedLabel);
            EmitPublishVisibleCameraX();
        }
        builder.Label(endLabel);
    }

    private void EmitTrackCameraYPosition(NesCameraConfig config)
    {
        var streamRows = ShouldStreamRowsForCamera(config);
        var usesWordCameraY = Math.Max(0, (config.MapHeight - NesTarget.Capabilities.ScreenTiles.Height) * 8) > byte.MaxValue;
        var moveDownLabel = builder.CreateLabel("nes_camera_move_down");
        var moveUpLabel = builder.CreateLabel("nes_camera_move_up");
        var fallbackMoveDownLabel = builder.CreateLabel("nes_camera_y_fallback_down");
        var fallbackLabel = builder.CreateLabel("nes_camera_y_fallback");
        var changedLabel = builder.CreateLabel("nes_camera_y_changed");
        var downCrossedTileLabel = builder.CreateLabel("nes_camera_down_crossed_tile");
        var upCrossedTileLabel = builder.CreateLabel("nes_camera_up_crossed_tile");
        var storeOnlyLabel = builder.CreateLabel("nes_camera_y_store_only");
        var storeAfterTileChangeLabel = builder.CreateLabel("nes_camera_y_store_after_tile");
        var storeAndStreamDownLabel = builder.CreateLabel("nes_camera_y_store_stream_down");
        var storeAndStreamUpLabel = builder.CreateLabel("nes_camera_y_store_stream_up");
        var endLabel = builder.CreateLabel("nes_camera_y_end");

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.BranchRelative(0xD0, changedLabel); // BNE changedLabel
        builder.JumpAbsolute(endLabel);

        builder.Label(changedLabel);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
        builder.BranchRelative(0xF0, moveDownLabel); // BEQ moveDownLabel

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.BranchRelative(0xF0, moveUpLabel); // BEQ moveUpLabel

        builder.JumpAbsolute(fallbackLabel);

        builder.Label(moveDownLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0x07);
        builder.BranchRelative(0xF0, downCrossedTileLabel); // BEQ downCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(downCrossedTileLabel);
        EmitIncrementCameraRow(config.MapHeight);
        if (streamRows)
        {
            EmitPrepareDownStreamRow(config);
            builder.JumpAbsolute(storeAndStreamDownLabel);
        }

        builder.JumpAbsolute(storeAfterTileChangeLabel);

        builder.Label(moveUpLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, upCrossedTileLabel); // BEQ upCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(upCrossedTileLabel);
        EmitDecrementCameraRow(config.MapHeight);
        if (streamRows)
        {
            EmitPrepareUpStreamRow();
            builder.JumpAbsolute(storeAndStreamUpLabel);
        }

        builder.JumpAbsolute(storeAfterTileChangeLabel);

        builder.Label(fallbackLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
        builder.SetCarry();
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.CompareImmediate(0x80);
        builder.BranchRelative(0x90, fallbackMoveDownLabel); // BCC fallbackMoveDownLabel
        builder.JumpAbsolute(moveUpLabel);
        builder.Label(fallbackMoveDownLabel);
        builder.JumpAbsolute(moveDownLabel);

        builder.Label(storeAfterTileChangeLabel);
        builder.JumpAbsolute(storeOnlyLabel);

        if (streamRows)
        {
            builder.Label(storeAndStreamDownLabel);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
            EmitQueuePendingCameraRow(NesPackedCameraRuntime.Positive, config);
            if (usePackedCamera)
            {
                var queued = builder.CreateLabel("nes_packed_camera_down_queued");
                builder.CompareImmediate((byte)NesWorldPackResult.Success);
                builder.JumpIf(0xF0, queued);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);
                EmitDecrementCameraRow(config.MapHeight);
                EmitDecrementCameraPixel(NesRuntimeMemoryLayout.Camera.Y, NesRuntimeMemoryLayout.Camera.YHigh, usesWordCameraY);
                builder.JumpAbsolute(endLabel);
                builder.Label(queued);
            }
            builder.JumpAbsolute(endLabel);

            builder.Label(storeAndStreamUpLabel);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
            EmitQueuePendingCameraRow(NesPackedCameraRuntime.Negative, config);
            if (usePackedCamera)
            {
                var queued = builder.CreateLabel("nes_packed_camera_up_queued");
                builder.CompareImmediate((byte)NesWorldPackResult.Success);
                builder.JumpIf(0xF0, queued);
                builder.LoadAImmediate(0);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);
                EmitIncrementCameraRow(config.MapHeight);
                EmitIncrementCameraPixel(NesRuntimeMemoryLayout.Camera.Y, NesRuntimeMemoryLayout.Camera.YHigh, usesWordCameraY);
                builder.JumpAbsolute(endLabel);
                builder.Label(queued);
            }
            builder.JumpAbsolute(endLabel);
        }

        builder.Label(storeOnlyLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        if (usePackedCamera)
        {
            EmitPublishVisibleCameraY();
        }
        builder.Label(endLabel);
    }

    private void EmitTrackShortCameraYPosition()
    {
        var changedLabel = builder.CreateLabel("nes_camera_y_short_changed");
        var moveDownLabel = builder.CreateLabel("nes_camera_y_short_down");
        var moveUpLabel = builder.CreateLabel("nes_camera_y_short_up");
        var storeLabel = builder.CreateLabel("nes_camera_y_short_store");
        var endLabel = builder.CreateLabel("nes_camera_y_short_end");

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.BranchRelative(0xD0, changedLabel); // BNE changedLabel
        builder.JumpAbsolute(endLabel);

        builder.Label(changedLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
        builder.SetCarry();
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.CompareImmediate(0x80);
        builder.BranchRelative(0x90, moveDownLabel); // BCC moveDownLabel
        builder.JumpAbsolute(moveUpLabel);

        builder.Label(moveDownLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.JumpAbsolute(storeLabel);

        builder.Label(moveUpLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.SetCarry();
        builder.SubtractImmediate(1);

        builder.Label(storeLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
        if (usePackedCamera)
        {
            EmitPublishVisibleCameraY();
        }

        builder.Label(endLabel);
    }

    private void EmitIncrementCameraTile(int mapWidth)
    {
        if (mapWidth <= byte.MaxValue)
        {
            var noWrapLabel = builder.CreateLabel("nes_camera_tile_inc_no_wrap");
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
            builder.ClearCarry();
            builder.AddImmediate(1);
            builder.CompareImmediate(mapWidth);
            builder.BranchRelative(0x90, noWrapLabel);  // BCC noWrapLabel
            builder.LoadAImmediate(0);
            builder.Label(noWrapLabel);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
            return;
        }

        EmitIncrementLogicalCell(NesRuntimeMemoryLayout.Camera.TileColumn, NesRuntimeMemoryLayout.Camera.TileColumnHigh, mapWidth, "nes_camera_tile");
    }

    private void EmitDecrementCameraTile(int mapWidth)
    {
        if (mapWidth <= byte.MaxValue)
        {
            var decrementLabel = builder.CreateLabel("nes_camera_tile_dec");
            var storeLabel = builder.CreateLabel("nes_camera_tile_dec_store");

            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
            builder.CompareImmediate(0);
            builder.BranchRelative(0xD0, decrementLabel); // BNE decrementLabel
            builder.LoadAImmediate(mapWidth - 1);
            builder.JumpAbsolute(storeLabel);
            builder.Label(decrementLabel);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
            builder.SetCarry();
            builder.SubtractImmediate(1);
            builder.Label(storeLabel);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
            return;
        }

        EmitDecrementLogicalCell(NesRuntimeMemoryLayout.Camera.TileColumn, NesRuntimeMemoryLayout.Camera.TileColumnHigh, mapWidth, "nes_camera_tile");
    }

    private void EmitIncrementCameraRow(int mapHeight)
    {
        if (mapHeight <= byte.MaxValue)
        {
            var noWrapLabel = builder.CreateLabel("nes_camera_row_inc_no_wrap");
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            builder.ClearCarry();
            builder.AddImmediate(1);
            builder.CompareImmediate(mapHeight);
            builder.BranchRelative(0x90, noWrapLabel);  // BCC noWrapLabel
            builder.LoadAImmediate(0);
            builder.Label(noWrapLabel);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            return;
        }

        EmitIncrementLogicalCell(NesRuntimeMemoryLayout.Camera.TileRow, NesRuntimeMemoryLayout.Camera.TileRowHigh, mapHeight, "nes_camera_row");
    }

    private void EmitDecrementCameraRow(int mapHeight)
    {
        if (mapHeight <= byte.MaxValue)
        {
            var decrementLabel = builder.CreateLabel("nes_camera_row_dec");
            var storeLabel = builder.CreateLabel("nes_camera_row_dec_store");

            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            builder.CompareImmediate(0);
            builder.BranchRelative(0xD0, decrementLabel); // BNE decrementLabel
            builder.LoadAImmediate(mapHeight - 1);
            builder.JumpAbsolute(storeLabel);
            builder.Label(decrementLabel);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            builder.SetCarry();
            builder.SubtractImmediate(1);
            builder.Label(storeLabel);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            return;
        }

        EmitDecrementLogicalCell(NesRuntimeMemoryLayout.Camera.TileRow, NesRuntimeMemoryLayout.Camera.TileRowHigh, mapHeight, "nes_camera_row");
    }

    private void EmitIncrementCameraPixel(byte lowAddress, ushort highAddress, bool adjustHigh)
    {
        var done = builder.CreateLabel("nes_camera_pixel_inc_done");
        builder.IncrementZeroPage(lowAddress);
        if (!adjustHigh)
        {
            return;
        }

        builder.BranchRelative(0xD0, done);
        builder.IncrementAbsolute(highAddress);
        builder.Label(done);
    }

    private void EmitDecrementCameraPixel(byte lowAddress, ushort highAddress, bool adjustHigh)
    {
        if (!adjustHigh)
        {
            builder.DecrementZeroPage(lowAddress);
            return;
        }

        var decrementLow = builder.CreateLabel("nes_camera_pixel_dec_low");
        builder.LoadAZeroPage(lowAddress);
        builder.BranchRelative(0xD0, decrementLow);
        builder.DecrementAbsolute(highAddress);
        builder.Label(decrementLow);
        builder.DecrementZeroPage(lowAddress);
    }

    private void EmitIncrementLogicalCell(byte lowAddress, ushort highAddress, int modulo, string labelPrefix)
    {
        var noCarryLabel = builder.CreateLabel(labelPrefix + "_inc_no_carry");
        var noWrapLabel = builder.CreateLabel(labelPrefix + "_inc_no_wrap");

        builder.IncrementZeroPage(lowAddress);
        builder.BranchRelative(0xD0, noCarryLabel); // BNE noCarryLabel
        builder.LoadAAbsolute(highAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(highAddress);
        builder.Label(noCarryLabel);

        builder.LoadAAbsolute(highAddress);
        builder.CompareImmediate((modulo >> 8) & 0xFF);
        builder.BranchRelative(0xD0, noWrapLabel); // BNE noWrapLabel
        builder.LoadAZeroPage(lowAddress);
        builder.CompareImmediate(modulo & 0xFF);
        builder.BranchRelative(0xD0, noWrapLabel); // BNE noWrapLabel
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(lowAddress);
        builder.StoreAAbsolute(highAddress);
        builder.Label(noWrapLabel);
    }

    private void EmitDecrementLogicalCell(byte lowAddress, ushort highAddress, int modulo, string labelPrefix)
    {
        var decrementLabel = builder.CreateLabel(labelPrefix + "_dec_value");
        var noBorrowLabel = builder.CreateLabel(labelPrefix + "_dec_no_borrow");
        var doneLabel = builder.CreateLabel(labelPrefix + "_dec_done");

        builder.LoadAAbsolute(highAddress);
        builder.BranchRelative(0xD0, decrementLabel); // BNE decrementLabel
        builder.LoadAZeroPage(lowAddress);
        builder.BranchRelative(0xD0, decrementLabel); // BNE decrementLabel
        builder.LoadAImmediate((modulo - 1) & 0xFF);
        builder.StoreAZeroPage(lowAddress);
        builder.LoadAImmediate(((modulo - 1) >> 8) & 0xFF);
        builder.StoreAAbsolute(highAddress);
        builder.JumpAbsolute(doneLabel);

        builder.Label(decrementLabel);
        builder.LoadAZeroPage(lowAddress);
        builder.BranchRelative(0xD0, noBorrowLabel); // BNE noBorrowLabel
        builder.LoadAAbsolute(highAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);
        builder.StoreAAbsolute(highAddress);
        builder.Label(noBorrowLabel);
        builder.DecrementZeroPage(lowAddress);
        builder.Label(doneLabel);
    }

    private void EmitPrepareRightStreamColumn(NesCameraConfig config)
    {
        const int lookaheadColumns = 32;
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.ClearCarry();
        builder.AddImmediate(lookaheadColumns);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        if (config.MapWidth <= byte.MaxValue)
        {
            builder.ClearCarry();
            builder.AddImmediate(lookaheadColumns);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            var sourceNoWrapLabel = builder.CreateLabel("nes_camera_source_no_wrap");
            builder.CompareImmediate(config.MapWidth);
            builder.BranchRelative(0x90, sourceNoWrapLabel); // BCC sourceNoWrapLabel
            builder.SetCarry();
            builder.SubtractImmediate(config.MapWidth);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
            builder.Label(sourceNoWrapLabel);
            return;
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileColumnHigh);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        EmitAddImmediateToLogicalCellModulo(
            NesRuntimeMemoryLayout.Camera.SourceColumn,
            NesRuntimeMemoryLayout.Camera.SourceColumnHigh,
            lookaheadColumns,
            config.MapWidth,
            "nes_camera_source");
    }

    private void EmitPrepareLeftStreamColumn()
    {
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        if (cameraConfig is { MapWidth: > byte.MaxValue })
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileColumnHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        }
    }

    private void EmitAddImmediateToLogicalCellModulo(
        byte lowAddress,
        ushort highAddress,
        int addend,
        int modulo,
        string labelPrefix)
    {
        var subtractLabel = builder.CreateLabel(labelPrefix + "_subtract_modulo");
        var doneLabel = builder.CreateLabel(labelPrefix + "_add_done");

        builder.LoadAZeroPage(lowAddress);
        builder.ClearCarry();
        builder.AddImmediate(addend & 0xFF);
        builder.StoreAZeroPage(lowAddress);
        builder.LoadAAbsolute(highAddress);
        builder.AddImmediate((addend >> 8) & 0xFF);
        builder.StoreAAbsolute(highAddress);

        builder.CompareImmediate((modulo >> 8) & 0xFF);
        builder.BranchRelative(0x90, doneLabel); // BCC doneLabel: high < modulo high
        builder.BranchRelative(0xD0, subtractLabel); // BNE subtractLabel: high > modulo high
        builder.LoadAZeroPage(lowAddress);
        builder.CompareImmediate(modulo & 0xFF);
        builder.BranchRelative(0x90, doneLabel); // BCC doneLabel: low < modulo low

        builder.Label(subtractLabel);
        builder.LoadAZeroPage(lowAddress);
        builder.SetCarry();
        builder.SubtractImmediate(modulo & 0xFF);
        builder.StoreAZeroPage(lowAddress);
        builder.LoadAAbsolute(highAddress);
        builder.SubtractImmediate((modulo >> 8) & 0xFF);
        builder.StoreAAbsolute(highAddress);

        builder.Label(doneLabel);
    }

    private void EmitPrepareDownStreamRow(NesCameraConfig config)
    {
        EmitAddCameraRowWithWrap(30, 60, NesRuntimeMemoryLayout.Camera.TargetRow);
        if (usePackedCamera && config.MapHeight > byte.MaxValue)
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileRowHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceRowHigh);
            EmitAddImmediateToLogicalCellModulo(
                NesRuntimeMemoryLayout.Camera.SourceRow,
                NesRuntimeMemoryLayout.Camera.SourceRowHigh,
                30,
                config.MapHeight,
                "nes_camera_source_row");
        }
        else
        {
            EmitAddCameraRowWithWrap(30, config.MapHeight, NesRuntimeMemoryLayout.Camera.SourceRow);
        }
        EmitPrepareRowColumns();
    }

    private void EmitPrepareUpStreamRow()
    {
        EmitCameraRowModulo(60, NesRuntimeMemoryLayout.Camera.TargetRow);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
        if (usePackedCamera && cameraConfig is { MapHeight: > byte.MaxValue })
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileRowHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceRowHigh);
        }
        EmitPrepareRowColumns();
    }

    private void EmitPrepareRowColumns()
    {
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        if (cameraConfig is { MapWidth: > byte.MaxValue })
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileColumnHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        }
    }

    private void EmitAddCameraRowWithWrap(int addend, int modulo, byte targetAddress)
    {
        var noWrapLabel = builder.CreateLabel("nes_camera_row_add_no_wrap");
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
        builder.ClearCarry();
        builder.AddImmediate(addend);
        builder.CompareImmediate(modulo);
        builder.BranchRelative(0x90, noWrapLabel); // BCC noWrapLabel
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.Label(noWrapLabel);
        builder.StoreAZeroPage(targetAddress);
    }

    private void EmitCameraRowModulo(int modulo, byte targetAddress)
    {
        var loopLabel = builder.CreateLabel("nes_camera_row_mod_loop");
        var doneLabel = builder.CreateLabel("nes_camera_row_mod_done");

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
        builder.Label(loopLabel);
        builder.CompareImmediate(modulo);
        builder.BranchRelative(0x90, doneLabel); // BCC doneLabel
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.JumpAbsolute(loopLabel);
        builder.Label(doneLabel);
        builder.StoreAZeroPage(targetAddress);
    }

    private void EmitAbsoluteCameraRowModulo(ushort sourceAddress, int modulo, byte targetAddress)
    {
        var loopLabel = builder.CreateLabel("nes_camera_absolute_row_mod_loop");
        var doneLabel = builder.CreateLabel("nes_camera_absolute_row_mod_done");

        builder.LoadAAbsolute(sourceAddress);
        builder.Label(loopLabel);
        builder.CompareImmediate(modulo);
        builder.BranchRelative(0x90, doneLabel); // BCC doneLabel
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.JumpAbsolute(loopLabel);
        builder.Label(doneLabel);
        builder.StoreAZeroPage(targetAddress);
    }

    private void EmitPublishVisibleCameraX()
    {
        var doneLabel = builder.CreateLabel("nes_packed_camera_defer_publish_x");
        builder.Emit(
            0x2C,
            (byte)(NesRuntimeMemoryLayout.Camera.ScrollApplied & 0xFF),
            (byte)(NesRuntimeMemoryLayout.Camera.ScrollApplied >> 8));
        builder.BranchRelative(0x30, doneLabel); // BMI: stale NMI, retain until a fresh VBlank
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow);
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
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraTileColumn);
        builder.Label(doneLabel);
    }

    private void EmitPublishVisibleCameraY()
    {
        var doneLabel = builder.CreateLabel("nes_packed_camera_defer_publish_y");
        builder.Emit(
            0x2C,
            (byte)(NesRuntimeMemoryLayout.Camera.ScrollApplied & 0xFF),
            (byte)(NesRuntimeMemoryLayout.Camera.ScrollApplied >> 8));
        builder.BranchRelative(0x30, doneLabel); // BMI: stale NMI, retain until a fresh VBlank
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow);
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
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraTileRow);
        builder.Label(doneLabel);
    }

    private void EmitPublishCommittedPackedCameraAxis(byte axis)
    {
        var doneLabel = builder.CreateLabel("nes_packed_camera_publish_done");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate(axis);
        builder.BranchRelative(0xF0, doneLabel); // BEQ doneLabel: axis was not pending
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
        builder.AndImmediate(axis);
        builder.BranchRelative(0xD0, doneLabel); // BNE doneLabel: commit is incomplete
        // Preparation can yield across frames and the bounded walk can advance farther inside the
        // newly resident tile before the pending camera commit drains it. Publish that latest safe fine-scroll
        // position; restoring the older edge snapshot leaves the visible camera stranded up to seven
        // pixels behind once the requested position has already been reached.
        if (axis == NesPackedCameraRuntime.Column)
        {
            EmitPublishVisibleCameraX();
        }
        else
        {
            EmitPublishVisibleCameraY();
        }
        builder.Label(doneLabel);
    }

    private void EmitRestoreCameraScroll(NesCameraConfig config)
    {
        if (config.UseFourScreenNametables)
        {
            EmitRestoreFourScreenCameraScroll();
        }
        else
        {
            EmitRestoreCameraScroll();
        }
    }

    private void EmitRestoreCameraScroll()
    {
        var rightNameTableLabel = builder.CreateLabel("nes_camera_apply_right_nt");
        var storeControlLabel = builder.CreateLabel("nes_camera_apply_store_ctrl");

        builder.LoadAAbsolute(0x2002);              // reset PPU scroll latch
        if (usePackedCamera)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraTileColumn);
        }
        else
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        }
        builder.AndImmediate(0x20);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, rightNameTableLabel); // BNE rightNameTableLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(storeControlLabel);

        builder.Label(rightNameTableLabel);
        builder.LoadAImmediate(1);

        builder.Label(storeControlLabel);
        if (usePackedCamera)
        {
            builder.OrImmediate(0x80);
        }
        builder.StoreAAbsolute(0x2000);
        if (usePackedCamera)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow);
        }
        else
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        }
        builder.StoreAAbsolute(0x2005);
        builder.LoadAImmediate(BottomOverscanInset());
        builder.StoreAAbsolute(0x2005);
    }

    // One tile row of vertical scroll when a screen-tall world's streamed background reaches the bottom
    // visible row, so its bottom tile row would otherwise be lost to bottom overscan; zero otherwise.
    // Scoped to camera windows that do not scroll vertically (the configured stream height fits the
    // screen) so scrolling windows keep their framing even when both use the same taller backing world.
    // Sprites apply the same offset so they stay aligned with the shifted background.
    private int BottomOverscanInset()
        => cameraConfig is { } config
           && config.StreamHeight <= NesTarget.Capabilities.ScreenTiles.Height
           && config.StreamY + config.StreamHeight >= NesTarget.Capabilities.ScreenTiles.Height
            ? BottomOverscanInsetPixels
            : 0;

    private void EmitRestoreFourScreenCameraScroll()
    {
        var noRightNameTableLabel = builder.CreateLabel("nes_camera_apply_no_right_nt");
        var noBottomNameTableLabel = builder.CreateLabel("nes_camera_apply_no_bottom_nt");
        var topRowLabel = builder.CreateLabel("nes_camera_apply_top_row");
        var storeYScrollLabel = builder.CreateLabel("nes_camera_apply_store_y_scroll");

        builder.LoadAAbsolute(0x2002);              // reset PPU scroll latch
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        if (usePackedCamera)
        {
            EmitAbsoluteCameraRowModulo(
                NesRuntimeMemoryLayout.PackedCamera.VisibleCameraTileRow,
                60,
                NesRuntimeMemoryLayout.Runtime.IndexScratch);
        }
        else
        {
            EmitCameraRowModulo(60, NesRuntimeMemoryLayout.Runtime.IndexScratch);
        }

        if (usePackedCamera)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraTileColumn);
        }
        else
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        }
        builder.AndImmediate(0x20);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, noRightNameTableLabel); // BEQ noRightNameTableLabel
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.OrImmediate(0x01);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.Label(noRightNameTableLabel);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.CompareImmediate(30);
        builder.BranchRelative(0x90, noBottomNameTableLabel); // BCC noBottomNameTableLabel
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.OrImmediate(0x02);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.Label(noBottomNameTableLabel);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        if (usePackedCamera)
        {
            builder.OrImmediate(0x80);
        }
        builder.StoreAAbsolute(0x2000);
        if (usePackedCamera)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow);
        }
        else
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        }
        builder.StoreAAbsolute(0x2005);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.CompareImmediate(30);
        builder.BranchRelative(0x90, topRowLabel); // BCC topRowLabel
        builder.SetCarry();
        builder.SubtractImmediate(30);
        builder.JumpAbsolute(storeYScrollLabel);
        builder.Label(topRowLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.Label(storeYScrollLabel);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        if (usePackedCamera)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow);
        }
        else
        {
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        }
        builder.AndImmediate(0x07);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        var fourScreenInset = BottomOverscanInset();
        if (fourScreenInset > 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(fourScreenInset);
        }

        builder.StoreAAbsolute(0x2005);
    }

}
