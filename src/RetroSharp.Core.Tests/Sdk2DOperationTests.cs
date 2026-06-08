namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using Xunit;

public sealed class Sdk2DOperationTests
{
    [Fact]
    public void Operation_records_keep_semantic_sdk_data()
    {
        Sdk2DOperation[] operations =
        [
            new Sdk2DOperation.WaitFrame(),
            new Sdk2DOperation.PollInput(),
            new Sdk2DOperation.DrawLogicalSprite(
                SpriteId: "player",
                LogicalSize: new Size2D(16, 27),
                X: 72,
                Y: 80,
                Frame: 1,
                PaletteSlot: 1,
                Transform: SpriteTransform.FlipX),
            new Sdk2DOperation.SetCameraPosition(X: 128, Y: 32, Axes: ScrollAxes.Horizontal | ScrollAxes.Vertical),
            new Sdk2DOperation.ApplyCamera(Axes: ScrollAxes.Horizontal),
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 31, SourceColumn: 64, Y: 0, Height: 18),
            new Sdk2DOperation.StreamMapRow(TargetRow: 29, SourceRow: 40, X: 0, Width: 20),
            new Sdk2DOperation.ReadWorldTile(WorldId: "level1", WorldX: 16, WorldY: 24),
            new Sdk2DOperation.ReadWorldTileFlags(WorldId: "level1", WorldX: 16, WorldY: 24),
            new Sdk2DOperation.SetHudTile(Mode: HudMode.Window, X: 1, Y: 0, Tile: 42),
        ];

        Assert.IsType<Sdk2DOperation.WaitFrame>(operations[0]);
        Assert.IsType<Sdk2DOperation.PollInput>(operations[1]);
        var sprite = Assert.IsType<Sdk2DOperation.DrawLogicalSprite>(operations[2]);
        Assert.Equal("player", sprite.SpriteId);
        Assert.Equal(new Size2D(16, 27), sprite.LogicalSize);
        Assert.Equal(SpriteTransform.FlipX, sprite.Transform);
        Assert.IsType<Sdk2DOperation.SetCameraPosition>(operations[3]);
        Assert.IsType<Sdk2DOperation.ApplyCamera>(operations[4]);
        Assert.IsType<Sdk2DOperation.StreamMapColumn>(operations[5]);
        Assert.IsType<Sdk2DOperation.StreamMapRow>(operations[6]);
        Assert.IsType<Sdk2DOperation.ReadWorldTile>(operations[7]);
        Assert.IsType<Sdk2DOperation.ReadWorldTileFlags>(operations[8]);
        Assert.IsType<Sdk2DOperation.SetHudTile>(operations[9]);
    }

    [Fact]
    public void Validator_accepts_operations_that_match_capabilities()
    {
        var capabilities = FullCapabilities();

        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.WaitFrame());
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.PollInput());
        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.DrawLogicalSprite("player", new Size2D(16, 27), X: 72, Y: 80, Frame: 0, PaletteSlot: 1, Transform: SpriteTransform.FlipX));
        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.SetCameraPosition(X: 32, Y: 16, Axes: ScrollAxes.Horizontal | ScrollAxes.Vertical));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.ApplyCamera(ScrollAxes.Horizontal));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.StreamMapColumn(TargetColumn: 31, SourceColumn: 64, Y: 0, Height: 18));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.StreamMapRow(TargetRow: 29, SourceRow: 40, X: 0, Width: 20));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.ReadWorldTile("level1", WorldX: 16, WorldY: 24));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.ReadWorldTileFlags("level1", WorldX: 16, WorldY: 24));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.SetHudTile(HudMode.Window, X: 1, Y: 0, Tile: 42));
    }

    [Fact]
    public void Validator_rejects_unsupported_hud_operations_with_exact_capability_error()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                FullCapabilities(),
                new Sdk2DOperation.SetHudTile(HudMode.SplitScroll, X: 1, Y: 0, Tile: 42)));

        Assert.Equal(
            "Target 'gb' does not support SplitScroll HUD. Use Window HUD, SpriteHud, or disable HUD for this target.",
            exception.Message);
    }

    private static Target2DCapabilities FullCapabilities()
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
            HudModes: HudMode.Window | HudMode.Sprite);
    }
}
