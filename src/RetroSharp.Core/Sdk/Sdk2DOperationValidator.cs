namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public static class Sdk2DOperationValidator
{
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
                ValidateByteExpression(flags.WorldX, "world tile flags X");
                ValidateByteExpression(flags.WorldY, "world tile flags Y");
                return;
            case Sdk2DOperation.DrawLogicalSprite draw:
                ValidateDrawLogicalSprite(capabilities, draw);
                return;
            case Sdk2DOperation.SetCameraPosition camera:
                ValidateByteExpression(camera.X, "camera X");
                ValidateByteExpression(camera.Y, "camera Y");
                RequireAxes(capabilities, camera.Axes);
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
            TargetCapabilityChecks.RequireScrollAxis(capabilities, ScrollAxes.Vertical);
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
}
