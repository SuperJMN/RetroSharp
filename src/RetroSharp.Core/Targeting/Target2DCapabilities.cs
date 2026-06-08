namespace RetroSharp.Core.Targeting;

public sealed record Target2DCapabilities(
    string Name,
    Size2D ScreenPixels,
    Size2D ScreenTiles,
    Size2D TileSize,
    Size2D BackgroundBufferTiles,
    ScrollAxes ScrollAxes,
    bool SupportsFineScrollX,
    bool SupportsFineScrollY,
    int MaxBackgroundTileWritesPerFrame,
    int MaxAttributeWritesPerFrame,
    int SpriteCount,
    SpriteSizeMode SpriteSizeModes,
    int MaxSpritesPerScanline,
    int SpritePaletteSlots,
    int BackgroundPaletteSlots,
    SpriteTransform SupportedSpriteTransforms,
    HudMode HudModes)
{
    public bool SupportsScrollAxis(ScrollAxes axis)
    {
        return axis != ScrollAxes.None && ScrollAxes.HasFlag(axis);
    }

    public bool SupportsSpriteSize(SpriteSizeMode mode)
    {
        return mode != SpriteSizeMode.None && SpriteSizeModes.HasFlag(mode);
    }

    public bool SupportsSpriteTransform(SpriteTransform transform)
    {
        return transform != SpriteTransform.None && SupportedSpriteTransforms.HasFlag(transform);
    }

    public bool SupportsHudMode(HudMode mode)
    {
        return mode != HudMode.None && HudModes.HasFlag(mode);
    }
}
