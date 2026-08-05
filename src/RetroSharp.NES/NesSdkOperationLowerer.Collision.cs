namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    internal void EmitCameraAabbTiles(Sdk2DOperation.CameraAabbTiles operation)
    {
        if (!TryResolveCameraAabbProbe(
                SharedCameraAabbKind.Tiles,
                operation.WorldId,
                operation.ScreenX,
                operation.WorldY,
                operation.WorldYOffset,
                operation.Width,
                operation.Height,
                operation.Flags,
                "camera_aabb_tiles",
                out var probe))
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (TryEmitSharedCameraAabbCall(probe))
        {
            return;
        }

        EmitCameraAabbTilesBody(probe);
    }

    private void EmitCameraAabbTilesBody(CameraAabbProbe probe)
    {
        var foundLabel = builder.CreateLabel("camera_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_aabb_tiles_end");
        var constantWorldY = TrySdkConst(probe.WorldY, out _);
        foreach (var yOffset in AabbSampleOffsets(probe.Height))
        {
            var hitTopOffset = probe.WorldYOffset + yOffset;
            var nextRowLabel = builder.CreateLabel("camera_aabb_tiles_next_row");
            if (!constantWorldY)
            {
                EmitWorldPixelToTileCoordinate(probe.WorldY, hitTopOffset);
                builder.CompareImmediate(probe.WorldMap.Height);
                var inBoundsLabel = builder.CreateLabel("camera_aabb_tiles_row_in_bounds");
                builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
                builder.JumpAbsolute(nextRowLabel);
                builder.Label(inBoundsLabel);
                builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
            }

            foreach (var xOffset in AabbSampleOffsets(probe.Width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_tiles_next");
                if (constantWorldY)
                {
                    EmitCameraTileFlagsAt(probe.ScreenX, xOffset, probe.WorldY, hitTopOffset, probe.Config, "camera_aabb_tiles");
                }
                else
                {
                    EmitCameraTileFlagsAtStoredRow(probe.ScreenX, xOffset, probe.Config);
                }

                builder.AndImmediate(probe.Flags);
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
        if (!TryResolveCameraAabbProbe(
                SharedCameraAabbKind.HitTop,
                operation.WorldId,
                operation.ScreenX,
                operation.WorldY,
                operation.WorldYOffset,
                operation.Width,
                operation.Height,
                operation.Flags,
                "camera_aabb_hit_top",
                out var probe))
        {
            builder.LoadAImmediate(255);
            builder.TransferAToX();
            return;
        }

        if (TryEmitSharedCameraAabbCall(probe))
        {
            return;
        }

        EmitCameraAabbHitTopBody(probe);
    }

    private void EmitCameraAabbHitTopBody(CameraAabbProbe probe)
    {
        var endLabel = builder.CreateLabel("camera_aabb_hit_top_end");
        var constantWorldY = TrySdkConst(probe.WorldY, out _);
        foreach (var yOffset in AabbSampleOffsets(probe.Height))
        {
            var hitTopOffset = probe.WorldYOffset + yOffset;
            var nextRowLabel = builder.CreateLabel("camera_aabb_hit_top_next_row");
            if (!constantWorldY)
            {
                EmitWorldPixelToTileCoordinate(probe.WorldY, hitTopOffset);
                builder.CompareImmediate(probe.WorldMap.Height);
                var inBoundsLabel = builder.CreateLabel("camera_aabb_hit_top_row_in_bounds");
                builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
                builder.JumpAbsolute(nextRowLabel);
                builder.Label(inBoundsLabel);
                builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionRowScratch);
            }

            foreach (var xOffset in AabbSampleOffsets(probe.Width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_hit_top_next");
                if (constantWorldY)
                {
                    EmitCameraTileFlagsAt(probe.ScreenX, xOffset, probe.WorldY, hitTopOffset, probe.Config, "camera_aabb_hit_top");
                }
                else
                {
                    EmitCameraTileFlagsAtStoredRow(probe.ScreenX, xOffset, probe.Config);
                }

                builder.AndImmediate(probe.Flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                EmitWorldPixelTileTop(probe.WorldY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }

            builder.Label(nextRowLabel);
        }

        builder.LoadAImmediate(255);
        builder.TransferAToX();
        builder.Label(endLabel);
    }

    private bool TryResolveCameraAabbProbe(
        SharedCameraAabbKind kind,
        string worldId,
        SdkByteExpression screenX,
        SdkWordExpression worldY,
        int worldYOffset,
        SdkAabbExtent widthExtent,
        int height,
        WorldTileFlags flagSet,
        string callName,
        out CameraAabbProbe probe)
    {
        if (worldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{worldId}'.");
        }

        var config = EnsureCameraConfigured(callName);
        var worldMap = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(widthExtent);
        var flags = (int)flagSet;
        if (width == 0 || height == 0 || flags == 0)
        {
            probe = default;
            return false;
        }

        ValidateConstantCameraAabbSpan(screenX, width, NesTarget.Capabilities.ScreenPixels.Width, callName);
        probe = new CameraAabbProbe(kind, screenX, worldY, worldYOffset, width, height, flags, config, worldMap);
        return true;
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

        EmitCameraAabbScreenXToA(screenPixelX);
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
        var nonNegative = builder.CreateLabel("camera_pixel_column_wide_non_negative");
        var negativeInRange = builder.CreateLabel("camera_pixel_column_wide_negative_in_range");
        var subtract = builder.CreateLabel("camera_pixel_column_wide_subtract");
        var done = builder.CreateLabel("camera_pixel_column_wide_end");

        builder.Label(PackedWideSourceColumnSubroutineLabel);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.CompareImmediate(0x80);
        builder.JumpIf(0x90, nonNegative);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.TileColumnHigh);
        builder.AddImmediate(byte.MaxValue);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.CompareImmediate(byte.MaxValue);
        builder.JumpIf(0xD0, negativeInRange);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.ClearCarry();
        builder.AddImmediate(mapWidth & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXLow);
        builder.LoadAImmediate(byte.MaxValue);
        builder.AddImmediate((mapWidth >> 8) & 0xFF);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh);
        builder.JumpAbsolute(done);

        builder.Label(negativeInRange);
        builder.JumpAbsolute(done);

        builder.Label(nonNegative);
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
        EmitCameraAabbWorldYToA(expression, highByte: false);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        EmitCameraAabbWorldYToA(expression, highByte: true);
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

    // Shared collision probes.
    //
    // Two sites whose probe grid, span, offsets, flags, and operand kinds match compile to
    // the same instruction sequence apart from where the screen-X and world-Y operands come
    // from. Materialising those two operands into `NesRuntimeMemoryLayout.SharedSdk` at the
    // call site lets one fixed-resident body serve every matching site. Only runtime
    // world-Y shapes are shared: a constant world-Y resolves per-row `WorldMapFlagRowLabel`
    // tables and is a different body shape.

    private enum SharedCameraAabbKind
    {
        Tiles,
        HitTop,
    }

    private readonly record struct CameraAabbProbe(
        SharedCameraAabbKind Kind,
        SdkByteExpression ScreenX,
        SdkWordExpression WorldY,
        int WorldYOffset,
        int Width,
        int Height,
        int Flags,
        NesCameraConfig Config,
        WorldMap2D WorldMap);

    private readonly record struct SharedCameraAabbShape(
        SharedCameraAabbKind Kind,
        int WorldYOffset,
        int Width,
        int Height,
        int Flags,
        SharedByteOperandShape ScreenX);

    // A body is only reusable while the camera and world facts it baked in still hold.
    private readonly record struct SharedCameraAabbEnvironment(int MapWidth, int MapHeight);

    private sealed record SharedCameraAabbBody(
        string Label,
        SharedCameraAabbEnvironment Environment,
        CameraAabbProbe Probe);

    private IReadOnlyDictionary<SharedCameraAabbShape, string> SharedCameraAabbSubroutines =>
        sharedCameraAabbSubroutines ??= CreateSharedCameraAabbSubroutines();

    private IReadOnlyDictionary<SharedCameraAabbShape, string> CreateSharedCameraAabbSubroutines()
    {
        if (!shareRepeatedSdkOperations)
        {
            return new Dictionary<SharedCameraAabbShape, string>();
        }

        var shapes = NesSdkProgramOperations.Collected(program.SdkProgram)
            .Select(TrySharedCameraAabbShape)
            .Where(shape => shape is not null)
            .Select(shape => shape!.Value)
            .ToArray();
        var repeated = shapes
            .GroupBy(shape => shape)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        var result = new Dictionary<SharedCameraAabbShape, string>();
        foreach (var shape in shapes)
        {
            if (repeated.Contains(shape) && !result.ContainsKey(shape))
            {
                result.Add(shape, $"{SharedCameraAabbSubroutinePrefix}_{result.Count}");
            }
        }

        return result;
    }

    private SharedCameraAabbShape? TrySharedCameraAabbShape(Sdk2DOperation operation)
    {
        var (kind, worldId, screenX, worldY, worldYOffset, widthExtent, height, flagSet) = operation switch
        {
            Sdk2DOperation.CameraAabbTiles tiles => (
                (SharedCameraAabbKind?)SharedCameraAabbKind.Tiles,
                tiles.WorldId,
                tiles.ScreenX,
                tiles.WorldY,
                tiles.WorldYOffset,
                tiles.Width,
                tiles.Height,
                tiles.Flags),
            Sdk2DOperation.CameraAabbHitTop hitTop => (
                SharedCameraAabbKind.HitTop,
                hitTop.WorldId,
                hitTop.ScreenX,
                hitTop.WorldY,
                hitTop.WorldYOffset,
                hitTop.Width,
                hitTop.Height,
                hitTop.Flags),
            _ => (null, string.Empty, null!, null!, 0, null!, 0, default),
        };
        if (kind is not { } sharedKind || worldId != "default" || TrySdkConst(worldY, out _))
        {
            return null;
        }

        var width = CameraAabbWidth(widthExtent);
        var flags = (int)flagSet;
        if (width == 0 || height == 0 || flags == 0)
        {
            return null;
        }

        // A single-probe body is smaller than the operand stores plus JSR/RTS it would cost.
        if (AabbSampleOffsets(width).Count * AabbSampleOffsets(height).Count < 2)
        {
            return null;
        }

        return new SharedCameraAabbShape(
            sharedKind,
            worldYOffset,
            width,
            height,
            flags,
            SharedByteOperandShape.From(screenX));
    }

    private bool TryEmitSharedCameraAabbCall(CameraAabbProbe probe)
    {
        if (TrySdkConst(probe.WorldY, out _))
        {
            return false;
        }

        var shape = new SharedCameraAabbShape(
            probe.Kind,
            probe.WorldYOffset,
            probe.Width,
            probe.Height,
            probe.Flags,
            SharedByteOperandShape.From(probe.ScreenX));
        if (!SharedCameraAabbSubroutines.TryGetValue(shape, out var subroutine))
        {
            return false;
        }

        var environment = new SharedCameraAabbEnvironment(probe.Config.MapWidth, probe.WorldMap.Height);
        if (referencedSharedCameraAabbSubroutines.TryGetValue(shape, out var body))
        {
            if (body.Environment != environment)
            {
                return false;
            }
        }
        else
        {
            referencedSharedCameraAabbSubroutines.Add(shape, new SharedCameraAabbBody(subroutine, environment, probe));
        }

        EmitSharedCameraAabbOperands(probe);
        builder.CallSubroutine(subroutine);
        return true;
    }

    private void EmitSharedCameraAabbOperands(CameraAabbProbe probe)
    {
        if (probe.ScreenX is not SdkByteExpression.Constant)
        {
            EmitSdkByteExpressionToA(probe.ScreenX);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.SharedSdk.AabbScreenX);
        }

        EmitSdkWordExpressionToA(probe.WorldY, highByte: false);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.SharedSdk.AabbWorldYLow);
        EmitSdkWordExpressionToA(probe.WorldY, highByte: true);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.SharedSdk.AabbWorldYHigh);
    }

    private void EmitCameraAabbScreenXToA(SdkByteExpression screenPixelX)
    {
        if (emittingSharedCameraAabbBody)
        {
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.SharedSdk.AabbScreenX);
            return;
        }

        EmitSdkByteExpressionToA(screenPixelX);
    }

    private void EmitCameraAabbWorldYToA(SdkWordExpression expression, bool highByte)
    {
        if (emittingSharedCameraAabbBody)
        {
            builder.LoadAAbsolute(highByte
                ? NesRuntimeMemoryLayout.SharedSdk.AabbWorldYHigh
                : NesRuntimeMemoryLayout.SharedSdk.AabbWorldYLow);
            return;
        }

        EmitSdkWordExpressionToA(expression, highByte);
    }

    private void EmitReferencedSharedCameraAabbSubroutines()
    {
        foreach (var body in referencedSharedCameraAabbSubroutines.Values.OrderBy(body => body.Label, StringComparer.Ordinal))
        {
            builder.Label(body.Label);
            emittingSharedCameraAabbBody = true;
            try
            {
                switch (body.Probe.Kind)
                {
                    case SharedCameraAabbKind.Tiles:
                        EmitCameraAabbTilesBody(body.Probe);
                        break;
                    case SharedCameraAabbKind.HitTop:
                        EmitCameraAabbHitTopBody(body.Probe);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported shared camera AABB kind '{body.Probe.Kind}'.");
                }
            }
            finally
            {
                emittingSharedCameraAabbBody = false;
            }

            builder.Return();
        }
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
}
