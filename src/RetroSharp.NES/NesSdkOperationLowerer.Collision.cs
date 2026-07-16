namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    internal void EmitCameraAabbTiles(Sdk2DOperation.CameraAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var config = EnsureCameraConfigured("camera_aabb_tiles");
        var worldMap = WorldMapForFlagQuery("camera_aabb_tiles");
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, "camera_aabb_tiles");

        var foundLabel = builder.CreateLabel("camera_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_aabb_tiles_end");
        var constantWorldY = TrySdkConst(operation.WorldY, out _);
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            var hitTopOffset = operation.WorldYOffset + yOffset;
            var nextRowLabel = builder.CreateLabel("camera_aabb_tiles_next_row");
            if (!constantWorldY)
            {
                EmitWorldPixelToTileCoordinate(operation.WorldY, hitTopOffset);
                builder.CompareImmediate(worldMap.Height);
                var inBoundsLabel = builder.CreateLabel("camera_aabb_tiles_row_in_bounds");
                builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
                builder.JumpAbsolute(nextRowLabel);
                builder.Label(inBoundsLabel);
                builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
            }

            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_tiles_next");
                if (constantWorldY)
                {
                    EmitCameraTileFlagsAt(operation.ScreenX, xOffset, operation.WorldY, hitTopOffset, config, "camera_aabb_tiles");
                }
                else
                {
                    EmitCameraTileFlagsAtStoredRow(operation.ScreenX, xOffset, config);
                }

                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                builder.JumpAbsolute(foundLabel);
                builder.Label(nextProbeLabel);
            }

            builder.Label(nextRowLabel);
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitCameraAabbHitTop(Sdk2DOperation.CameraAabbHitTop operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var callName = "camera_aabb_hit_top";
        var config = EnsureCameraConfigured(callName);
        var worldMap = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(255);
            builder.TransferAToX();
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, "camera_aabb_hit_top");

        var endLabel = builder.CreateLabel("camera_aabb_hit_top_end");
        var constantWorldY = TrySdkConst(operation.WorldY, out _);
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            var hitTopOffset = operation.WorldYOffset + yOffset;
            var nextRowLabel = builder.CreateLabel("camera_aabb_hit_top_next_row");
            if (!constantWorldY)
            {
                EmitWorldPixelToTileCoordinate(operation.WorldY, hitTopOffset);
                builder.CompareImmediate(worldMap.Height);
                var inBoundsLabel = builder.CreateLabel("camera_aabb_hit_top_row_in_bounds");
                builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
                builder.JumpAbsolute(nextRowLabel);
                builder.Label(inBoundsLabel);
                builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
            }

            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_hit_top_next");
                if (constantWorldY)
                {
                    EmitCameraTileFlagsAt(operation.ScreenX, xOffset, operation.WorldY, hitTopOffset, config, callName);
                }
                else
                {
                    EmitCameraTileFlagsAtStoredRow(operation.ScreenX, xOffset, config);
                }

                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                EmitWorldPixelTileTop(operation.WorldY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }

            builder.Label(nextRowLabel);
        }

        builder.LoadAImmediate(255);
        builder.TransferAToX();
        builder.Label(endLabel);
    }

    internal void EmitCameraScreenAabbTiles(Sdk2DOperation.CameraScreenAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var config = EnsureCameraConfigured("camera_screen_aabb_tiles");
        _ = WorldMapForFlagQuery("camera_screen_aabb_tiles");
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, "camera_screen_aabb_tiles");

        var foundLabel = builder.CreateLabel("camera_screen_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_screen_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_screen_aabb_tiles_next");
                EmitCameraScreenTileFlagsAt(
                    operation.ScreenX,
                    xOffset,
                    operation.ScreenY,
                    operation.ScreenYOffset + yOffset,
                    config,
                    "camera_screen_aabb_tiles");
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                builder.JumpAbsolute(foundLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitCameraScreenAabbHitTop(Sdk2DOperation.CameraScreenAabbHitTop operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var callName = "camera_screen_aabb_hit_top";
        var config = EnsureCameraConfigured(callName);
        _ = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(255);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, callName);

        var endLabel = builder.CreateLabel("camera_screen_aabb_hit_top_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_screen_aabb_hit_top_next");
                var hitTopOffset = operation.ScreenYOffset + yOffset;
                EmitCameraScreenTileFlagsAt(operation.ScreenX, xOffset, operation.ScreenY, hitTopOffset, config, callName);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                EmitScreenPixelTileTop(operation.ScreenY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(255);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(int screenPixelX, SdkWordExpression worldY, int worldYOffset, NesCameraConfig config, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("camera_tile_flags_oob");
        var endLabel = builder.CreateLabel("camera_tile_flags_end");
        if (TrySdkConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
            EmitMapFlagsAtSourceColumnInA(row);
            return;
        }

        EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        var inBoundsLabel = builder.CreateLabel("camera_tile_flags_in_bounds");
        builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
        builder.JumpAbsolute(outOfBoundsLabel);
        builder.Label(inBoundsLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
        EmitMapFlagsAtScratchColumnAndRow();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(SdkByteExpression screenPixelX, int screenPixelXOffset, SdkWordExpression worldY, int worldYOffset, NesCameraConfig config, string callName)
    {
        if (TrySdkConst(screenPixelX, out var constantScreenX))
        {
            EmitCameraTileFlagsAt(constantScreenX + screenPixelXOffset, worldY, worldYOffset, config, callName);
            return;
        }

        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("camera_tile_flags_oob");
        var endLabel = builder.CreateLabel("camera_tile_flags_end");
        if (TrySdkConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
            EmitMapFlagsAtSourceColumnInA(row);
            return;
        }

        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        var inBoundsLabel = builder.CreateLabel("camera_tile_flags_in_bounds");
        builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
        builder.JumpAbsolute(outOfBoundsLabel);
        builder.Label(inBoundsLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
        EmitMapFlagsAtScratchColumnAndRow();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAtStoredRow(SdkByteExpression screenPixelX, int screenPixelXOffset, NesCameraConfig config)
    {
        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        EmitMapFlagsAtScratchColumnAndRow();
    }

    private void EmitCameraScreenTileFlagsAt(
        SdkByteExpression screenPixelX,
        int screenPixelXOffset,
        SdkByteExpression screenPixelY,
        int screenPixelYOffset,
        NesCameraConfig config,
        string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var endLabel = builder.CreateLabel("camera_screen_tile_flags_end");

        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);

        EmitCameraPixelToSourceRow(screenPixelY, screenPixelYOffset, worldMap.Height);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
        EmitMapFlagsAtScratchColumnAndRow();
        builder.Label(endLabel);
    }

    private void EmitMapFlagsAtSourceColumnInA(int row)
    {
        if (usePackedCamera)
        {
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
            EmitClearPackedWorldXHighForByteWidth();
            builder.LoadAImmediate(row & 0xFF);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
            builder.LoadAImmediate((row >> 8) & 0xFF);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
            EmitPackedWorldCollisionLookup();
            return;
        }

        builder.TransferAToX();
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapFlagRowLabel(row));
    }

    private void EmitMapFlagsAtScratchColumnAndRow()
    {
        if (usePackedCamera)
        {
            packedCollisionAtScratchSubroutineReferenced = true;
            builder.CallSubroutine(PackedCollisionAtScratchSubroutineLabel);
            return;
        }

        builder.LoadXZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapFlagRowPointerLowLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapFlagRowPointerHighLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.LoadYZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Runtime.IndexScratch);
    }

    private void EmitPackedCollisionAtScratchSubroutine()
    {
        builder.Label(PackedCollisionAtScratchSubroutineLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        EmitClearPackedWorldXHighForByteWidth();
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYLow);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh);
        EmitPackedWorldCollisionLookup();
        builder.Return();
    }

    private void EmitClearPackedWorldXHighForByteWidth()
    {
        if (cameraConfig is not { MapWidth: <= byte.MaxValue })
        {
            return;
        }

        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
    }

    private void EmitPackedWorldCollisionLookup()
    {
        packedCollisionFlagsSubroutineReferenced = true;
        builder.CallSubroutine(PackedCollisionFlagsSubroutineLabel);
    }

    private void EmitPackedCollisionFlagsSubroutine()
    {
        var success = builder.CreateLabel("nes_packed_collision_success");
        var done = builder.CreateLabel("nes_packed_collision_done");
        builder.Label(PackedCollisionFlagsSubroutineLabel);
        builder.CallSubroutine(NesRomBuilder.WorldPackCollisionLookupLabel);
        builder.CompareImmediate((byte)NesWorldPackResult.Success);
        builder.BranchRelative(0xF0, success);
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(done);
        builder.Label(success);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.ResultCollision);
        builder.Label(done);
        builder.Return();
    }

    private void EmitCameraPixelToSourceColumn(int screenPixelX, int mapWidth)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.AndImmediate(0x07);
        if (screenPixelX != 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(screenPixelX);
        }

        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        if (mapWidth > byte.MaxValue)
        {
            EmitAddCameraTileColumnToWideOffsetInA(mapWidth);
            return;
        }

        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, endLabel); // BCC endLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitCameraPixelToSourceColumn(SdkByteExpression screenPixelX, int screenPixelXOffset, int mapWidth)
    {
        if (TrySdkConst(screenPixelX, out var constantScreenX))
        {
            EmitCameraPixelToSourceColumn(constantScreenX + screenPixelXOffset, mapWidth);
            return;
        }

        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");

        EmitSdkByteExpressionToA(screenPixelX);
        if (screenPixelXOffset != 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(screenPixelXOffset);
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.AndImmediate(0x07);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        if (mapWidth > byte.MaxValue)
        {
            EmitAddCameraTileColumnToWideOffsetInA(mapWidth);
            return;
        }

        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, endLabel); // BCC endLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitAddCameraTileColumnToWideOffsetInA(int mapWidth)
    {
        if (packedWideSourceColumnSubroutineReferenced && packedWideSourceColumnMapWidth != mapWidth)
        {
            throw new InvalidOperationException(
                $"NES packed camera cannot share source-column lowering for map widths {packedWideSourceColumnMapWidth} and {mapWidth}.");
        }

        packedWideSourceColumnSubroutineReferenced = true;
        packedWideSourceColumnMapWidth = mapWidth;
        builder.CallSubroutine(PackedWideSourceColumnSubroutineLabel);
    }

    private void EmitPackedWideSourceColumnSubroutine()
    {
        var mapWidth = packedWideSourceColumnMapWidth;
        var subtract = builder.CreateLabel("camera_pixel_column_wide_subtract");
        var done = builder.CreateLabel("camera_pixel_column_wide_end");

        builder.Label(PackedWideSourceColumnSubroutineLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileColumnHigh);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);

        builder.CompareImmediate((mapWidth >> 8) & 0xFF);
        builder.BranchRelative(0x90, done); // BCC done: high < modulo high
        builder.BranchRelative(0xD0, subtract); // BNE subtract: high > modulo high
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.CompareImmediate(mapWidth & 0xFF);
        builder.BranchRelative(0x90, done); // BCC done: low < modulo low

        builder.Label(subtract);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.SetCarry();
        builder.SubtractImmediate(mapWidth & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.SubtractImmediate((mapWidth >> 8) & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);

        builder.Label(done);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.Return();
    }

    private static void ValidateConstantCameraAabbSpan(SdkByteExpression screenX, int width, int screenWidth, string callName)
    {
        if (screenX is SdkByteExpression.Constant constant && constant.Value + width > screenWidth)
        {
            throw new InvalidOperationException($"{callName} screen span must fit within the visible NES width.");
        }
    }

    private void EmitWorldPixelToTileCoordinate(SdkWordExpression expression, int offset)
    {
        EmitSdkWordExpressionWithOffsetToAx(expression, offset);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.StoreXZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.AndImmediate(0x07);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.OrZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
    }

    private void EmitWorldPixelTileTop(SdkWordExpression expression, int offset)
    {
        EmitSdkWordExpressionWithOffsetToAx(expression, offset);
        builder.AndImmediate(0xF8);
    }

    private void EmitSdkWordExpressionWithOffsetToAx(SdkWordExpression expression, int offset)
    {
        EmitSdkWordExpressionToA(expression, highByte: false);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        EmitSdkWordExpressionToA(expression, highByte: true);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        if (offset != 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(offset & 0xFF);
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        if (offset != 0)
        {
            builder.AddImmediate((offset >> 8) & 0xFF);
        }

        builder.TransferAToX();
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
    }

    private void EmitCameraPixelToSourceRow(SdkByteExpression screenPixelY, int screenPixelYOffset, int mapHeight)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_row_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_row_end");

        EmitSdkByteExpressionToA(screenPixelY);
        EmitAddSignedImmediate(screenPixelYOffset);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
        builder.AndImmediate(0x07);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapHeight);
        builder.BranchRelative(0x90, endLabel); // BCC endLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapHeight);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitScreenPixelTileTop(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        EmitAddSignedImmediate(offset);
        builder.AndImmediate(0xF8);
    }

    internal WorldMap2D WorldMapForFlagQuery(string callName)
    {
        return program.WorldMap
               ?? throw new InvalidOperationException($"{callName} requires world_map collision flag data.");
    }

    private int CameraAabbWidth(SdkAabbExtent width)
    {
        return width switch
        {
            SdkAabbExtent.Constant constant => constant.Value,
            SdkAabbExtent.SpriteWidth spriteWidth => SpriteWidth(spriteWidth.SpriteId),
            _ => throw new InvalidOperationException($"Unsupported camera AABB width '{width.GetType().Name}'."),
        };
    }

    internal void EmitReferencedSubroutines()
    {
        if (packedWideSourceColumnSubroutineReferenced)
        {
            EmitPackedWideSourceColumnSubroutine();
        }

        if (packedCollisionAtScratchSubroutineReferenced)
        {
            EmitPackedCollisionAtScratchSubroutine();
        }

        if (packedCollisionFlagsSubroutineReferenced)
        {
            EmitPackedCollisionFlagsSubroutine();
        }
    }

}
