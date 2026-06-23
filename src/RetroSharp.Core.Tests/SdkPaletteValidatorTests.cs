namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using Xunit;

public sealed class SdkPaletteValidatorTests
{
    [Fact]
    public void Accepts_palette_slots_with_four_colors_inside_target_capabilities()
    {
        var capabilities = Capabilities();

        SdkPaletteValidator.Validate(capabilities, PaletteKind.Background, slot: 0, colorCount: 4);
        SdkPaletteValidator.Validate(capabilities, PaletteKind.Sprite, slot: 1, colorCount: 4);
    }

    [Fact]
    public void Rejects_palette_declarations_that_do_not_have_four_colors()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SdkPaletteValidator.Validate(Capabilities(), PaletteKind.Sprite, slot: 0, colorCount: 3));

        Assert.Equal("palette declarations must contain exactly 4 colors, got 3.", exception.Message);
    }

    [Fact]
    public void Rejects_sprite_palette_slot_outside_target_capabilities()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SdkPaletteValidator.Validate(Capabilities(), PaletteKind.Sprite, slot: 2, colorCount: 4));

        Assert.Equal("Target 'gb' supports sprite palette slots 0..1, but palette slot 2 was requested.", exception.Message);
    }

    [Fact]
    public void Rejects_background_palette_slot_outside_target_capabilities()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SdkPaletteValidator.Validate(Capabilities(), PaletteKind.Background, slot: 1, colorCount: 4));

        Assert.Equal("Target 'gb' supports background palette slots 0..0, but palette slot 1 was requested.", exception.Message);
    }

    private static Target2DCapabilities Capabilities()
    {
        return new Target2DCapabilities(
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
            HudModes: HudMode.Window | HudMode.Sprite,
            CollisionQueries: CollisionQueryMode.WorldTileFlags | CollisionQueryMode.WorldAabb | CollisionQueryMode.CameraRelativeAabb | CollisionQueryMode.CameraRelativeAabbHitTop);
    }
}
