namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public static class Sdk2DOperationValidator
{
    public static void ValidateFrame(Target2DCapabilities capabilities, IEnumerable<Sdk2DOperation> operations)
    {
        var backgroundTileWrites = 0;
        foreach (var operation in operations)
        {
            Validate(capabilities, operation);
            backgroundTileWrites += BackgroundTileWrites(operation);
        }

        ValidateFrameBudget(capabilities, new Sdk2DFrameBudget(backgroundTileWrites));
    }

    public static void ValidateFrameBudget(Target2DCapabilities capabilities, Sdk2DFrameBudget budget)
    {
        RequireBackgroundTileWriteBudget(
            capabilities,
            budget.BackgroundTileWrites,
            "streaming background tiles in one frame");

        if (budget.HardwareSprites > capabilities.SpriteCount)
        {
            throw new InvalidOperationException(
                $"Target '{capabilities.Name}' supports {capabilities.SpriteCount} hardware sprites per frame, but {budget.HardwareSprites} are required for drawing logical sprites in one frame.");
        }

        RequireSpriteSizeModes(capabilities, budget.SpriteSizeModes);

        var maxScanline = budget.HardwareSpritesByScanline
            .OrderBy(pair => pair.Key)
            .FirstOrDefault(pair => pair.Value > capabilities.MaxSpritesPerScanline);
        if (maxScanline.Value > capabilities.MaxSpritesPerScanline)
        {
            throw new InvalidOperationException(
                $"Target '{capabilities.Name}' supports {capabilities.MaxSpritesPerScanline} hardware sprites per scanline, but {maxScanline.Value} are required on scanline {maxScanline.Key} for drawing logical sprites in one frame.");
        }
    }

    private static void RequireSpriteSizeModes(Target2DCapabilities capabilities, SpriteSizeMode modes)
    {
        foreach (var mode in SpriteSizeModes(modes))
        {
            if (capabilities.SupportsSpriteSize(mode))
            {
                continue;
            }

            throw new InvalidOperationException($"Target '{capabilities.Name}' does not support {FormatSpriteSizeMode(mode)} sprite size mode.");
        }
    }

    private static IEnumerable<SpriteSizeMode> SpriteSizeModes(SpriteSizeMode modes)
    {
        if (modes.HasFlag(SpriteSizeMode.Sprite8x8))
        {
            yield return SpriteSizeMode.Sprite8x8;
        }

        if (modes.HasFlag(SpriteSizeMode.Sprite8x16))
        {
            yield return SpriteSizeMode.Sprite8x16;
        }

        if (modes.HasFlag(SpriteSizeMode.Sprite16x16))
        {
            yield return SpriteSizeMode.Sprite16x16;
        }
    }

    private static string FormatSpriteSizeMode(SpriteSizeMode mode)
    {
        return mode switch
        {
            SpriteSizeMode.Sprite8x8 => "8x8",
            SpriteSizeMode.Sprite8x16 => "8x16",
            SpriteSizeMode.Sprite16x16 => "16x16",
            _ => mode.ToString(),
        };
    }

    public static void Validate(Target2DCapabilities capabilities, Sdk2DOperation operation)
    {
        switch (operation)
        {
            case Sdk2DOperation.WaitFrame:
            case Sdk2DOperation.PollInput:
                return;
            case Sdk2DOperation.ReadWorldTile tile:
                ValidateByteExpression(tile.WorldX, "world tile X");
                ValidateByteExpression(tile.WorldY, "world tile Y");
                return;
            case Sdk2DOperation.ReadWorldTileFlags flags:
                RequireCollisionQuery(capabilities, CollisionQueryMode.WorldTileFlags, "world tile flag");
                ValidateByteExpression(flags.WorldX, "world tile flags X");
                ValidateByteExpression(flags.WorldY, "world tile flags Y");
                return;
            case Sdk2DOperation.CameraAabbTiles cameraAabb:
                ValidateCameraAabbTiles(capabilities, cameraAabb);
                return;
            case Sdk2DOperation.CameraAabbHitTop cameraAabb:
                ValidateCameraAabbHitTop(capabilities, cameraAabb);
                return;
            case Sdk2DOperation.DrawLogicalSprite draw:
                ValidateDrawLogicalSprite(capabilities, draw);
                return;
            case Sdk2DOperation.SetCameraPosition camera:
                ValidateByteExpression(camera.X, "camera X");
                ValidateByteExpression(camera.Y, "camera Y");
                RequireAxes(capabilities, camera.Axes);
                RequireFineScroll(capabilities, camera.Axes);
                RequireCameraMovementBudget(capabilities, camera.Axes);
                return;
            case Sdk2DOperation.ApplyCamera camera:
                RequireAxes(capabilities, camera.Axes);
                return;
            case Sdk2DOperation.StreamMapColumn column:
                ValidateByteExpression(column.TargetColumn, "stream map target column");
                ValidateByteExpression(column.SourceColumn, "stream map source column");
                TargetCapabilityChecks.RequireScrollAxis(capabilities, ScrollAxes.Horizontal);
                RequireBackgroundTileWriteBudget(capabilities, column.Height, "streaming a visible map column");
                return;
            case Sdk2DOperation.StreamMapRow row:
                TargetCapabilityChecks.RequireScrollAxis(capabilities, ScrollAxes.Vertical);
                RequireBackgroundTileWriteBudget(capabilities, row.Width, "streaming a visible map row");
                return;
            case Sdk2DOperation.SetHudTile hud:
                TargetCapabilityChecks.RequireHudMode(capabilities, hud.Mode);
                return;
            default:
                throw new InvalidOperationException($"Unsupported SDK operation '{operation.GetType().Name}'.");
        }
    }

    private static void ValidateDrawLogicalSprite(Target2DCapabilities capabilities, Sdk2DOperation.DrawLogicalSprite draw)
    {
        ValidateByteExpression(draw.X, "sprite X");
        ValidateByteExpression(draw.Y, "sprite Y");
        ValidateByteExpression(draw.Frame, "sprite frame");
        if (draw.FlipX is not null)
        {
            ValidateByteExpression(draw.FlipX, "sprite FlipX");
            TargetCapabilityChecks.RequireSpriteTransform(capabilities, SpriteTransform.FlipX);
        }

        if (draw.PaletteSlot < 0 || draw.PaletteSlot >= capabilities.SpritePaletteSlots)
        {
            throw new InvalidOperationException(
                $"Target '{capabilities.Name}' supports sprite palette slots 0..{capabilities.SpritePaletteSlots - 1}, but slot {draw.PaletteSlot} was requested.");
        }

        TargetCapabilityChecks.RequireSpriteTransform(capabilities, draw.StaticTransform);
    }

    private static void ValidateCameraAabbTiles(Target2DCapabilities capabilities, Sdk2DOperation.CameraAabbTiles cameraAabb)
    {
        RequireCollisionQuery(capabilities, CollisionQueryMode.CameraRelativeAabb, "camera-relative AABB collision");

        ValidateCameraAabbGeometry(
            capabilities,
            cameraAabb.ScreenX,
            cameraAabb.Width,
            cameraAabb.Height,
            cameraAabb.WorldY,
            cameraAabb.Flags);
    }

    private static void ValidateCameraAabbHitTop(Target2DCapabilities capabilities, Sdk2DOperation.CameraAabbHitTop cameraAabb)
    {
        RequireCollisionQuery(capabilities, CollisionQueryMode.CameraRelativeAabbHitTop, "camera-relative AABB hit-top");

        ValidateCameraAabbGeometry(
            capabilities,
            cameraAabb.ScreenX,
            cameraAabb.Width,
            cameraAabb.Height,
            cameraAabb.WorldY,
            cameraAabb.Flags);
    }

    private static void ValidateCameraAabbGeometry(
        Target2DCapabilities capabilities,
        SdkByteExpression screenX,
        SdkAabbExtent width,
        int height,
        SdkByteExpression worldY,
        WorldTileFlags flags)
    {
        ValidateByteExpression(screenX, "camera AABB screen X");
        ValidateAabbWidth(capabilities, screenX, width);

        if (height < 0 || height > 255)
        {
            throw new InvalidOperationException($"camera AABB height must be between 0 and 255 for target '{capabilities.Name}'.");
        }

        ValidateByteExpression(worldY, "camera AABB world Y");
        ValidateCollisionFlags(flags, "camera AABB flags");
    }

    private static void RequireCollisionQuery(Target2DCapabilities capabilities, CollisionQueryMode mode, string queryName)
    {
        if (!capabilities.SupportsCollisionQuery(mode))
        {
            throw new InvalidOperationException($"Target '{capabilities.Name}' does not support {queryName} queries.");
        }
    }

    private static void ValidateAabbWidth(Target2DCapabilities capabilities, SdkByteExpression screenX, SdkAabbExtent width)
    {
        switch (width)
        {
            case SdkAabbExtent.Constant constant when constant.Value < 0 || constant.Value > capabilities.ScreenPixels.Width:
                throw new InvalidOperationException($"camera AABB width must be between 0 and {capabilities.ScreenPixels.Width} for target '{capabilities.Name}'.");
            case SdkAabbExtent.Constant constant:
                if (screenX is SdkByteExpression.Constant constantScreenX)
                {
                    if (constantScreenX.Value >= capabilities.ScreenPixels.Width)
                    {
                        throw new InvalidOperationException($"camera AABB screen X must be between 0 and {capabilities.ScreenPixels.Width - 1} for target '{capabilities.Name}'.");
                    }

                    if (constantScreenX.Value + constant.Value > capabilities.ScreenPixels.Width)
                    {
                        throw new InvalidOperationException($"camera AABB screen span must fit within target '{capabilities.Name}' visible width {capabilities.ScreenPixels.Width}.");
                    }
                }

                return;
            case SdkAabbExtent.SpriteWidth { SpriteId.Length: > 0 }:
                return;
            case SdkAabbExtent.SpriteWidth:
                throw new InvalidOperationException("camera AABB sprite width asset id must not be empty.");
            default:
                throw new InvalidOperationException($"camera AABB width uses unsupported SDK extent '{width.GetType().Name}'.");
        }
    }

    private static void ValidateCollisionFlags(WorldTileFlags flags, string context)
    {
        const WorldTileFlags allowed = WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform;
        if ((flags & ~allowed) != 0)
        {
            throw new InvalidOperationException($"{context} contains unsupported collision flags '{flags}'.");
        }
    }

    private static void ValidateByteExpression(SdkByteExpression expression, string context)
    {
        switch (expression)
        {
            case SdkByteExpression.Constant { Value: < 0 or > 255 } constant:
                throw new InvalidOperationException($"{context} constant must be between 0 and 255, got {constant.Value}.");
            case SdkByteExpression.Constant:
                return;
            case SdkByteExpression.Variable variable:
                ValidateStorageLocation(variable.Location, context);
                return;
            default:
                throw new InvalidOperationException($"{context} uses unsupported SDK byte expression '{expression.GetType().Name}'.");
        }
    }

    private static void ValidateStorageLocation(SdkStorageLocation location, string context)
    {
        switch (location)
        {
            case SdkStorageLocation.Local { Name.Length: > 0 }:
                return;
            case SdkStorageLocation.Local:
                throw new InvalidOperationException($"{context} local storage name must not be empty.");
            case SdkStorageLocation.Field field:
                ValidateStorageLocation(field.Target, context);
                if (field.FieldName.Length == 0)
                {
                    throw new InvalidOperationException($"{context} field name must not be empty.");
                }

                return;
            case SdkStorageLocation.IndexedElement { BaseName.Length: > 0, Index: >= 0 and <= 255 }:
                return;
            case SdkStorageLocation.IndexedElement { BaseName.Length: 0 }:
                throw new InvalidOperationException($"{context} indexed storage base name must not be empty.");
            case SdkStorageLocation.IndexedElement indexed:
                throw new InvalidOperationException($"{context} indexed storage index must be between 0 and 255, got {indexed.Index}.");
            case SdkStorageLocation.RuntimeIndexedField { BaseName.Length: 0 }:
                throw new InvalidOperationException($"{context} runtime indexed field base name must not be empty.");
            case SdkStorageLocation.RuntimeIndexedField { FieldName.Length: 0 }:
                throw new InvalidOperationException($"{context} runtime indexed field name must not be empty.");
            case SdkStorageLocation.RuntimeIndexedField runtimeIndexed:
                ValidateByteExpression(runtimeIndexed.Index, context);
                return;
            default:
                throw new InvalidOperationException($"{context} uses unsupported SDK storage location '{location.GetType().Name}'.");
        }
    }

    private static void RequireAxes(Target2DCapabilities capabilities, ScrollAxes axes)
    {
        if (axes.HasFlag(ScrollAxes.Horizontal))
        {
            TargetCapabilityChecks.RequireScrollAxis(capabilities, ScrollAxes.Horizontal);
        }

        if (axes.HasFlag(ScrollAxes.Vertical))
        {
            RequireVerticalCameraAxis(capabilities);
        }
    }

    private static void RequireVerticalCameraAxis(Target2DCapabilities capabilities)
    {
        if (capabilities.SupportsScrollAxis(ScrollAxes.Vertical))
        {
            return;
        }

        if (capabilities.Name == "nes")
        {
            throw new InvalidOperationException(
                "Target 'nes': vertical camera movement is not supported on NES yet; see docs/CameraVerticalScrollRoadmap.md before enabling NES vertical scroll.");
        }

        TargetCapabilityChecks.RequireScrollAxis(capabilities, ScrollAxes.Vertical);
    }

    private static void RequireFineScroll(Target2DCapabilities capabilities, ScrollAxes axes)
    {
        if (axes.HasFlag(ScrollAxes.Horizontal) && !capabilities.SupportsFineScrollX)
        {
            throw new InvalidOperationException($"Target '{capabilities.Name}' does not support horizontal fine scrolling.");
        }

        if (axes.HasFlag(ScrollAxes.Vertical) && !capabilities.SupportsFineScrollY)
        {
            throw new InvalidOperationException($"Target '{capabilities.Name}' does not support vertical fine scrolling.");
        }
    }

    private static void RequireCameraMovementBudget(Target2DCapabilities capabilities, ScrollAxes axes)
    {
        // Targets that cannot write background tiles at runtime do not stream while
        // scrolling: they fine-scroll the viewport within a pre-loaded background buffer
        // (for example NES with no runtime column streaming), so a camera position set
        // costs no background tile writes. Streaming targets must still fit the per-frame
        // budget for the new column/row revealed by the move.
        if (capabilities.MaxBackgroundTileWritesPerFrame == 0)
        {
            return;
        }

        var columnWrites = axes.HasFlag(ScrollAxes.Horizontal) ? capabilities.ScreenTiles.Height : 0;
        var rowWrites = axes.HasFlag(ScrollAxes.Vertical) ? capabilities.ScreenTiles.Width : 0;
        var requiredWrites = columnWrites + rowWrites;
        if (requiredWrites == 0)
        {
            return;
        }

        RequireBackgroundTileWriteBudget(capabilities, requiredWrites, CameraMovementDescription(axes, columnWrites, rowWrites));
    }

    private static string CameraMovementDescription(ScrollAxes axes, int columnWrites, int rowWrites)
    {
        if (axes.HasFlag(ScrollAxes.Horizontal) && axes.HasFlag(ScrollAxes.Vertical))
        {
            return $"moving the camera diagonally ({columnWrites} column tiles + {rowWrites} row tiles)";
        }

        if (axes.HasFlag(ScrollAxes.Horizontal))
        {
            return $"moving the camera horizontally ({columnWrites} column tiles)";
        }

        return $"moving the camera vertically ({rowWrites} row tiles)";
    }

    private static void RequireBackgroundTileWriteBudget(Target2DCapabilities capabilities, int requiredWrites, string operationDescription)
    {
        if (capabilities.MaxBackgroundTileWritesPerFrame >= requiredWrites)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Target '{capabilities.Name}' supports {capabilities.MaxBackgroundTileWritesPerFrame} background tile writes per frame, but {requiredWrites} are required for {operationDescription}.");
    }

    private static int BackgroundTileWrites(Sdk2DOperation operation)
    {
        return operation switch
        {
            Sdk2DOperation.StreamMapColumn column => column.Height,
            Sdk2DOperation.StreamMapRow row => row.Width,
            _ => 0,
        };
    }
}
