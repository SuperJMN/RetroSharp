namespace RetroSharp.GameBoy;

using System.Globalization;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    private const WorldTileFlags SupportedCollisionFlags =
        WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform;
    private const int SmallMapRowShortcutMaxHeight = 16;

    internal void EmitMapTileAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("map_tile_at requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var row = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "map_tile_at argument 2"), 0, program.MapColumnHeight - 1, "map_tile_at argument 2");

        context.EmitSourceExpressionToA(args[0]);
        EmitReadOnlyMapByteAtSourceColumnInA(GameBoyRomBuilder.MapRowLabel(row));
    }

    internal void EmitMapFlagsAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        if (program.MapFlagColumnHeight == 0)
        {
            throw new InvalidOperationException("map_flags_at requires world_map collision flag data.");
        }

        var args = call.Parameters.ToList();
        var row = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "map_flags_at argument 2"), 0, program.MapFlagColumnHeight - 1, "map_flags_at argument 2");

        context.EmitSourceExpressionToA(args[0]);
        EmitMapFlagsAtSourceColumnInA(row);
    }

    private void EmitWorldTileFlagsAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var args = call.Parameters.ToList();
        EmitWorldTileFlagsAt(args[0], 0, args[1], 0, call.Name);
    }

    private void EmitReadWorldTileFlags(Sdk2DOperation.ReadWorldTileFlags operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
        }

        EmitWorldTileFlagsAt(operation.WorldX, 0, operation.WorldY, 0, "world_tile_flags_at");
    }

    private void EmitWorldTileFlagsAt(ExpressionSyntax worldX, int worldXOffset, ExpressionSyntax worldY, int worldYOffset, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        if (!worldMap.ContainsAnyFlags(SupportedCollisionFlags))
        {
            builder.LoadAImmediate(0);
            return;
        }

        var outOfBoundsLabel = builder.CreateLabel("world_tile_flags_oob");
        var endLabel = builder.CreateLabel("world_tile_flags_end");
        if (context.TrySourceConstant(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
            builder.CompareImmediate(worldMap.Width);
            builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
            EmitMapFlagsAtSourceColumnInA(row);
            builder.JumpAbsolute(endLabel);
            builder.Label(outOfBoundsLabel);
            builder.LoadAImmediate(0);
            builder.Label(endLabel);
            return;
        }

        EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
        builder.CompareImmediate(worldMap.Width);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadBFromA();

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitWorldTileFlagsAt(SdkByteExpression worldX, int worldXOffset, SdkByteExpression worldY, int worldYOffset, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        if (!worldMap.ContainsAnyFlags(SupportedCollisionFlags))
        {
            builder.LoadAImmediate(0);
            return;
        }

        var outOfBoundsLabel = builder.CreateLabel("world_tile_flags_oob");
        var endLabel = builder.CreateLabel("world_tile_flags_end");
        if (TrySdkConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
            builder.CompareImmediate(worldMap.Width);
            builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
            EmitMapFlagsAtSourceColumnInA(row);
            builder.JumpAbsolute(endLabel);
            builder.Label(outOfBoundsLabel);
            builder.LoadAImmediate(0);
            builder.Label(endLabel);
            return;
        }

        EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
        builder.CompareImmediate(worldMap.Width);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadBFromA();

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private WorldMap2D WorldMapForFlagQuery(string callName)
    {
        return program.WorldMap
               ?? throw new InvalidOperationException($"{callName} requires world_map collision flag data.");
    }

    private void EmitWorldPixelToTileCoordinate(ExpressionSyntax expression, int offset)
    {
        context.EmitSourceExpressionToA(expression);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
    }

    private void EmitWorldPixelToTileCoordinate(SdkByteExpression expression, int offset)
    {
        context.EmitByteExpressionToA(expression);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
    }

    private void EmitWorldPixelToTileCoordinate(SdkWordExpression expression, int offset)
    {
        EmitSdkWordExpressionWithOffsetToHl(expression, offset);
        builder.LoadAFromH();
        builder.AndImmediate(0x07);
        builder.SwapA();
        builder.AddAFromA();
        builder.LoadBFromA();
        builder.LoadAFromL();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.OrAFromB();
    }

    private void EmitWorldPixelTileTop(SdkWordExpression expression, int offset)
    {
        EmitSdkWordExpressionWithOffsetToHl(expression, offset);
        builder.LoadAFromL();
        builder.AndImmediate(0xF8);
        builder.LoadLFromA();
    }

    private void EmitSdkWordExpressionWithOffsetToHl(SdkWordExpression expression, int offset)
    {
        context.EmitWordExpressionToA(expression, highByte: false);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow);
        context.EmitWordExpressionToA(expression, highByte: true);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);

        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow);
        if (offset != 0)
        {
            builder.AddAImmediate(offset & 0xFF);
        }

        builder.StoreA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
        if (offset != 0)
        {
            builder.AdcAImmediate((offset >> 8) & 0xFF);
        }

        builder.LoadHFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow);
        builder.LoadLFromA();
    }

    internal void EmitCollisionAabbTiles(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        var worldMap = WorldMapForFlagQuery(call.Name);
        var args = call.Parameters.ToList();
        var width = CheckedRange(context.ConstRuntimeValue(args[2], "collision_aabb_tiles argument 3"), 0, 255, "collision_aabb_tiles argument 3");
        var height = CheckedRange(context.ConstRuntimeValue(args[3], "collision_aabb_tiles argument 4"), 0, 255, "collision_aabb_tiles argument 4");
        var allowedFlags = (int)SupportedCollisionFlags;
        var flags = CheckedRange(GameBoyVideoProgram.ConstValue(args[4], "collision_aabb_tiles argument 5"), 0, allowedFlags, "collision_aabb_tiles argument 5");
        if (width == 0 || height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (!worldMap.ContainsAnyFlags((WorldTileFlags)flags))
        {
            builder.LoadAImmediate(0);
            return;
        }

        var foundLabel = builder.CreateLabel("collision_aabb_tiles_found");
        var endLabel = builder.CreateLabel("collision_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitWorldTileFlagsAt(args[0], xOffset, args[1], yOffset, call.Name);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitCameraAabbTiles(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        var config = EnsureCameraConfigured(call.Name);
        var worldMap = WorldMapForFlagQuery(call.Name);
        var args = call.Parameters.ToList();
        var screenX = CheckedRange(context.ConstRuntimeValue(args[0], "camera_aabb_tiles argument 1"), 0, 159, "camera_aabb_tiles argument 1");
        var width = CheckedRange(context.ConstRuntimeValue(args[2], "camera_aabb_tiles argument 3"), 0, 160, "camera_aabb_tiles argument 3");
        var height = CheckedRange(context.ConstRuntimeValue(args[3], "camera_aabb_tiles argument 4"), 0, 255, "camera_aabb_tiles argument 4");
        var allowedFlags = (int)SupportedCollisionFlags;
        var flags = CheckedRange(GameBoyVideoProgram.ConstValue(args[4], "camera_aabb_tiles argument 5"), 0, allowedFlags, "camera_aabb_tiles argument 5");
        if (width == 0 || height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (!worldMap.ContainsAnyFlags((WorldTileFlags)flags))
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (screenX + width > 160)
        {
            throw new InvalidOperationException("camera_aabb_tiles screen span must fit within the visible Game Boy width.");
        }

        var foundLabel = builder.CreateLabel("camera_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitCameraTileFlagsAt(screenX + xOffset, args[1], yOffset, config, call.Name);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitCameraAabbTiles(Sdk2DOperation.CameraAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
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

        if (!worldMap.ContainsAnyFlags(operation.Flags))
        {
            builder.LoadAImmediate(0);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, "camera_aabb_tiles");

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
                builder.JumpAbsolute(0xD2, nextRowLabel); // JP NC,nextRowLabel
                builder.StoreA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
            }

            foreach (var xOffset in AabbSampleOffsets(width))
            {
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
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
            }

            builder.Label(nextRowLabel);
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitCameraAabbHitTop(Sdk2DOperation.CameraAabbHitTop operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
        }

        var callName = "camera_aabb_hit_top";
        var config = EnsureCameraConfigured(callName);
        var worldMap = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadHl(0xFFFF);
            return;
        }

        if (!worldMap.ContainsAnyFlags(operation.Flags))
        {
            builder.LoadHl(0xFFFF);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, "camera_aabb_hit_top");

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
                builder.JumpAbsolute(0xD2, nextRowLabel); // JP NC,nextRowLabel
                builder.StoreA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
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
                builder.JumpAbsolute(0xCA, nextProbeLabel); // JP Z,nextProbeLabel
                EmitWorldPixelTileTop(operation.WorldY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }

            builder.Label(nextRowLabel);
        }

        builder.LoadHl(0xFFFF);
        builder.Label(endLabel);
    }

    private void EmitCameraScreenAabbTiles(Sdk2DOperation.CameraScreenAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
        }

        var config = EnsureCameraConfigured("camera_screen_aabb_tiles");
        var worldMap = WorldMapForFlagQuery("camera_screen_aabb_tiles");
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (!worldMap.ContainsAnyFlags(operation.Flags))
        {
            builder.LoadAImmediate(0);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, "camera_screen_aabb_tiles");

        var foundLabel = builder.CreateLabel("camera_screen_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_screen_aabb_tiles_end");
        var rowShortcuts = SmallMapRowShortcuts(worldMap, operation.Flags);
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            var nextRowLabel = builder.CreateLabel("camera_screen_aabb_tiles_next_row");
            EmitCameraPixelToSourceRow(operation.ScreenY, operation.ScreenYOffset + yOffset, worldMap.Height);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
            EmitSmallMapRowShortcutJumps(rowShortcuts.FullRows, rowShortcuts.EmptyRows, foundLabel, nextRowLabel);
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitCameraScreenTileFlagsAtStoredRow(operation.ScreenX, xOffset, config);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
            }

            builder.Label(nextRowLabel);
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitCameraScreenAabbHitTop(Sdk2DOperation.CameraScreenAabbHitTop operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
        }

        var callName = "camera_screen_aabb_hit_top";
        var config = EnsureCameraConfigured(callName);
        var worldMap = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(255);
            return;
        }

        if (!worldMap.ContainsAnyFlags(operation.Flags))
        {
            builder.LoadAImmediate(255);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, callName);

        var endLabel = builder.CreateLabel("camera_screen_aabb_hit_top_end");
        var rowShortcuts = SmallMapRowShortcuts(worldMap, operation.Flags);
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            var hitTopOffset = operation.ScreenYOffset + yOffset;
            var nextRowLabel = builder.CreateLabel("camera_screen_aabb_hit_top_next_row");
            var fullRowLabel = rowShortcuts.FullRows.Count == 0
                ? null
                : builder.CreateLabel("camera_screen_aabb_hit_top_full_row");
            EmitCameraPixelToSourceRow(operation.ScreenY, hitTopOffset, worldMap.Height);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
            EmitSmallMapRowShortcutJumps(rowShortcuts.FullRows, rowShortcuts.EmptyRows, fullRowLabel ?? nextRowLabel, nextRowLabel);
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_screen_aabb_hit_top_next");
                EmitCameraScreenTileFlagsAtStoredRow(operation.ScreenX, xOffset, config);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xCA, nextProbeLabel); // JP Z,nextProbeLabel
                EmitScreenPixelTileTop(operation.ScreenY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }

            if (fullRowLabel is not null)
            {
                builder.JumpAbsolute(nextRowLabel);
                builder.Label(fullRowLabel);
                EmitScreenPixelTileTop(operation.ScreenY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
            }

            builder.Label(nextRowLabel);
        }

        builder.LoadAImmediate(255);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(int screenPixelX, ExpressionSyntax worldY, int worldYOffset, GameBoyCameraConfig config, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("camera_tile_flags_oob");
        var endLabel = builder.CreateLabel("camera_tile_flags_end");
        if (context.TrySourceConstant(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
            EmitCameraMapFlagsAtSourceColumnInA(row, config.MapWidth);
            return;
        }

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();

        EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
        builder.LoadBFromA();
        EmitCameraMapFlagsAtSourceColumnInBAndRowInC(config.MapWidth);
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(int screenPixelX, SdkWordExpression worldY, int worldYOffset, GameBoyCameraConfig config, string callName)
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
            EmitCameraMapFlagsAtSourceColumnInA(row, config.MapWidth);
            return;
        }

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
        builder.LoadBFromA();
        EmitCameraMapFlagsAtSourceColumnInBAndRowInC(config.MapWidth);
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(SdkByteExpression screenPixelX, int screenPixelXOffset, SdkWordExpression worldY, int worldYOffset, GameBoyCameraConfig config, string callName)
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
            EmitCameraMapFlagsAtSourceColumnInA(row, config.MapWidth);
            return;
        }

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.LoadBFromA();
        EmitCameraMapFlagsAtSourceColumnInBAndRowInC(config.MapWidth);
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAtStoredRow(SdkByteExpression screenPixelX, int screenPixelXOffset, GameBoyCameraConfig config)
    {
        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
        builder.LoadCFromA();
        EmitCameraMapFlagsAtSourceColumnInBAndRowInC(config.MapWidth);
    }

    private void EmitCameraScreenTileFlagsAt(
        SdkByteExpression screenPixelX,
        int screenPixelXOffset,
        SdkByteExpression screenPixelY,
        int screenPixelYOffset,
        GameBoyCameraConfig config,
        string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var endLabel = builder.CreateLabel("camera_screen_tile_flags_end");

        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumn);

        EmitCameraPixelToSourceRow(screenPixelY, screenPixelYOffset, worldMap.Height);
        builder.LoadCFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumn);
        builder.LoadBFromA();
        EmitCameraMapFlagsAtSourceColumnInBAndRowInC(config.MapWidth);
        builder.Label(endLabel);
    }

    private void EmitCameraScreenTileFlagsAtStoredRow(
        SdkByteExpression screenPixelX,
        int screenPixelXOffset,
        GameBoyCameraConfig config)
    {
        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
        builder.LoadCFromA();
        EmitCameraMapFlagsAtSourceColumnInBAndRowInC(config.MapWidth);
    }

    private static (IReadOnlyList<int> FullRows, IReadOnlyList<int> EmptyRows) SmallMapRowShortcuts(
        WorldMap2D worldMap,
        WorldTileFlags flags)
    {
        var fullRows = new List<int>();
        var emptyRows = new List<int>();
        if (flags == WorldTileFlags.Empty || worldMap.Height > SmallMapRowShortcutMaxHeight)
        {
            return (fullRows, emptyRows);
        }

        for (var row = 0; row < worldMap.Height; row++)
        {
            if (worldMap.RowAllColumnsContainAnyFlags(row, flags))
            {
                fullRows.Add(row);
            }
            else if (!worldMap.RowContainsAnyFlags(row, flags))
            {
                emptyRows.Add(row);
            }
        }

        return (fullRows, emptyRows);
    }

    private void EmitSmallMapRowShortcutJumps(
        IReadOnlyList<int> fullRows,
        IReadOnlyList<int> emptyRows,
        string fullRowLabel,
        string emptyRowLabel)
    {
        if (fullRows.Count == 0 && emptyRows.Count == 0)
        {
            return;
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
        foreach (var row in fullRows)
        {
            builder.CompareImmediate(row);
            builder.JumpAbsolute(0xCA, fullRowLabel); // JP Z,fullRowLabel
        }

        foreach (var row in emptyRows)
        {
            builder.CompareImmediate(row);
            builder.JumpAbsolute(0xCA, emptyRowLabel); // JP Z,emptyRowLabel
        }
    }

    private void EmitCameraPixelToSourceColumn(int screenPixelX, int mapWidth)
    {
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        if (screenPixelX != 0)
        {
            builder.AddAImmediate(screenPixelX);
        }

        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        if (mapWidth > byte.MaxValue)
        {
            EmitAddCameraTileOffsetToWideSourceColumn(mapWidth);
            return;
        }

        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapWidth);
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

        context.EmitByteExpressionToA(screenPixelX);
        if (screenPixelXOffset != 0)
        {
            builder.AddAImmediate(screenPixelXOffset);
        }

        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.AddAFromB();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        if (mapWidth > byte.MaxValue)
        {
            EmitAddCameraTileOffsetToWideSourceColumn(mapWidth);
            return;
        }

        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitAddCameraTileOffsetToWideSourceColumn(int mapWidth)
    {
        var subtractWidthLabel = builder.CreateLabel("camera_pixel_column_subtract_width");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");
        var widthLow = (byte)mapWidth;
        var widthHigh = checked((byte)(mapWidth >> 8));

        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
        builder.AddAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumn);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh);
        builder.Emit(0xCE, 0x00); // ADC A,0: include carry from the low-byte addition
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumnHigh);

        builder.CompareImmediate(widthHigh);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.JumpAbsolute(0xC2, subtractWidthLabel); // JP NZ,subtractWidthLabel
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumn);
        builder.CompareImmediate(widthLow);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel

        builder.Label(subtractWidthLabel);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumn);
        builder.SubtractAImmediate(widthLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumn);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumnHigh);
        builder.Emit(0xDE, widthHigh); // SBC A,widthHigh: include borrow from the low byte
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumnHigh);

        builder.Label(endLabel);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumn);
    }

    private void EmitCameraPixelToSourceRow(SdkByteExpression screenPixelY, int screenPixelYOffset, int mapHeight)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_row_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_row_end");

        context.EmitByteExpressionToA(screenPixelY);
        EmitAddSignedImmediateToA(screenPixelYOffset);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.AddAFromB();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapHeight);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapHeight);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitScreenPixelTileTop(SdkByteExpression expression, int offset)
    {
        context.EmitByteExpressionToA(expression);
        EmitAddSignedImmediateToA(offset);
        builder.AndImmediate(0xF8);
    }

    private void EmitAddSignedImmediateToA(int offset)
    {
        if (offset > 0)
        {
            builder.AddAImmediate(offset);
        }
        else if (offset < 0)
        {
            builder.SubtractAImmediate(-offset);
        }
    }

    private static void ValidateConstantCameraAabbSpan(SdkByteExpression screenX, int width, int screenWidth, string callName)
    {
        if (screenX is SdkByteExpression.Constant constant && constant.Value + width > screenWidth)
        {
            throw new InvalidOperationException($"{callName} screen span must fit within the visible Game Boy width.");
        }
    }

    private static IReadOnlyList<int> AabbSampleOffsets(int size)
    {
        var offsets = new List<int>();
        for (var offset = 0; offset < size; offset += 8)
        {
            offsets.Add(offset);
        }

        var lastOffset = size - 1;
        if (!offsets.Contains(lastOffset))
        {
            offsets.Add(lastOffset);
        }

        return offsets;
    }
}
