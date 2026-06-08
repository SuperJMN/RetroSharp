namespace RetroSharp.GameBoy;

using RetroSharp.Core.Targeting;

public static class GameBoyTarget
{
    public static Target2DCapabilities Capabilities { get; } = new(
        Name: "gb",
        ScreenPixels: new Size2D(160, 144),
        ScreenTiles: new Size2D(20, 18),
        TileSize: new Size2D(8, 8),
        BackgroundBufferTiles: new Size2D(32, 32),
        ScrollAxes: ScrollAxes.Horizontal | ScrollAxes.Vertical,
        SupportsFineScrollX: true,
        SupportsFineScrollY: true,
        MaxBackgroundTileWritesPerFrame: 20,
        MaxAttributeWritesPerFrame: 0,
        SpriteCount: 40,
        SpriteSizeModes: SpriteSizeMode.Sprite8x8 | SpriteSizeMode.Sprite8x16,
        MaxSpritesPerScanline: 10,
        SpritePaletteSlots: 2,
        BackgroundPaletteSlots: 1,
        SupportedSpriteTransforms: SpriteTransform.FlipX | SpriteTransform.FlipY,
        HudModes: HudMode.Window | HudMode.Sprite);
}
