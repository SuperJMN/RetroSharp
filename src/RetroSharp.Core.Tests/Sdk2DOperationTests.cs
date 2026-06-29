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
                X: new SdkByteExpression.Constant(72),
                Y: Field("player", "y"),
                Frame: Field("player", "frame"),
                FlipX: Field("player", "flipX"),
                PaletteSlot: 1,
                StaticTransform: SpriteTransform.None),
            new Sdk2DOperation.SetCameraPosition(X: 128, Y: 32, Axes: ScrollAxes.Horizontal | ScrollAxes.Vertical),
            new Sdk2DOperation.ApplyCamera(Axes: ScrollAxes.Horizontal),
            new Sdk2DOperation.StreamMapColumn(
                TargetColumn: Local("targetColumn"),
                SourceColumn: Local("sourceColumn"),
                Y: 0,
                Height: 18),
            new Sdk2DOperation.StreamMapRow(TargetRow: 29, SourceRow: 40, X: 0, Width: 20),
            new Sdk2DOperation.ReadWorldTile(WorldId: "level1", WorldX: 16, WorldY: 24),
            new Sdk2DOperation.ReadWorldTileFlags(WorldId: "level1", WorldX: 16, WorldY: 24),
            new Sdk2DOperation.CameraAabbTiles(
                WorldId: "level1",
                ScreenX: 72,
                WorldY: Field("player", "footY"),
                Width: 16,
                Height: 8,
                Flags: WorldTileFlags.Solid),
            new Sdk2DOperation.CameraAabbHitTop(
                WorldId: "level1",
                ScreenX: 72,
                WorldY: Field("player", "footY"),
                Width: 16,
                Height: 40,
                Flags: WorldTileFlags.Solid),
            new Sdk2DOperation.SetHudTile(Mode: HudMode.Window, X: 1, Y: 0, Tile: 42),
        ];

        Assert.IsType<Sdk2DOperation.WaitFrame>(operations[0]);
        Assert.IsType<Sdk2DOperation.PollInput>(operations[1]);
        var sprite = Assert.IsType<Sdk2DOperation.DrawLogicalSprite>(operations[2]);
        Assert.Equal("player", sprite.SpriteId);
        Assert.Equal(new SdkByteExpression.Constant(72), sprite.X);
        Assert.Equal(Field("player", "y"), sprite.Y);
        Assert.Equal(Field("player", "frame"), sprite.Frame);
        Assert.Equal(Field("player", "flipX"), sprite.FlipX);
        Assert.Equal(SpriteTransform.None, sprite.StaticTransform);
        Assert.IsType<Sdk2DOperation.SetCameraPosition>(operations[3]);
        Assert.IsType<Sdk2DOperation.ApplyCamera>(operations[4]);
        var column = Assert.IsType<Sdk2DOperation.StreamMapColumn>(operations[5]);
        Assert.Equal(Local("targetColumn"), column.TargetColumn);
        Assert.Equal(Local("sourceColumn"), column.SourceColumn);
        Assert.IsType<Sdk2DOperation.StreamMapRow>(operations[6]);
        Assert.IsType<Sdk2DOperation.ReadWorldTile>(operations[7]);
        Assert.IsType<Sdk2DOperation.ReadWorldTileFlags>(operations[8]);
        var cameraAabb = Assert.IsType<Sdk2DOperation.CameraAabbTiles>(operations[9]);
        Assert.Equal(new SdkByteExpression.Constant(72), cameraAabb.ScreenX);
        Assert.Equal(Field("player", "footY"), cameraAabb.WorldY);
        Assert.Equal(new SdkAabbExtent.Constant(16), cameraAabb.Width);
        Assert.Equal(WorldTileFlags.Solid, cameraAabb.Flags);
        var cameraHitTop = Assert.IsType<Sdk2DOperation.CameraAabbHitTop>(operations[10]);
        Assert.Equal(40, cameraHitTop.Height);
        Assert.Equal(WorldTileFlags.Solid, cameraHitTop.Flags);
        Assert.IsType<Sdk2DOperation.SetHudTile>(operations[11]);
    }

    [Fact]
    public void Validator_accepts_operations_that_match_capabilities()
    {
        var capabilities = FullCapabilities();

        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.WaitFrame());
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.PollInput());
        Sdk2DOperationValidator.Validate(
                capabilities,
                new Sdk2DOperation.DrawLogicalSprite(
                    "player",
                X: Field("player", "x"),
                Y: Field("player", "y"),
                Frame: Field("player", "frame"),
                FlipX: Field("player", "flipX"),
                PaletteSlot: 1,
                StaticTransform: SpriteTransform.None));
        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.SetCameraPosition(X: 32, Y: 0, Axes: ScrollAxes.Horizontal));
        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.SetCameraPosition(X: 0, Y: 16, Axes: ScrollAxes.Vertical));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.ApplyCamera(ScrollAxes.Horizontal));
        Sdk2DOperationValidator.Validate(
                capabilities,
                new Sdk2DOperation.StreamMapColumn(
                TargetColumn: Local("targetColumn"),
                SourceColumn: Local("sourceColumn"),
                Y: 0,
                Height: 18));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.StreamMapRow(TargetRow: 29, SourceRow: 40, X: 0, Width: 20));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.ReadWorldTile("level1", WorldX: 16, WorldY: 24));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.ReadWorldTileFlags("level1", WorldX: 16, WorldY: 24));
        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.CameraAabbTiles(
                WorldId: "level1",
                ScreenX: 72,
                WorldY: Field("player", "footY"),
                Width: 16,
                Height: 8,
                Flags: WorldTileFlags.Solid));
        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.CameraAabbHitTop(
                WorldId: "level1",
                ScreenX: 72,
                WorldY: Field("player", "footY"),
                Width: 16,
                Height: 40,
                Flags: WorldTileFlags.Solid));
        Sdk2DOperationValidator.Validate(capabilities, new Sdk2DOperation.SetHudTile(HudMode.Window, X: 1, Y: 0, Tile: 42));
    }

    [Fact]
    public void Validator_rejects_camera_aabb_span_outside_screen_width()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                FullCapabilities(),
                new Sdk2DOperation.CameraAabbTiles(
                    WorldId: "level1",
                    ScreenX: 150,
                    WorldY: Field("player", "footY"),
                    Width: 16,
                    Height: 8,
                    Flags: WorldTileFlags.Solid)));

        Assert.Equal(
            "camera AABB screen span must fit within target 'gb' visible width 160.",
            exception.Message);
    }

    [Fact]
    public void Validator_accepts_camera_aabb_with_byte_backed_screen_x()
    {
        var capabilities = FullCapabilities();

        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.CameraAabbTiles(
                WorldId: "level1",
                ScreenX: Local("actorScreenX"),
                WorldY: Field("actor", "footY"),
                Width: 16,
                Height: 8,
                Flags: WorldTileFlags.Solid));
        Sdk2DOperationValidator.Validate(
            capabilities,
            new Sdk2DOperation.CameraAabbHitTop(
                WorldId: "level1",
                ScreenX: Local("actorScreenX"),
                WorldY: Field("actor", "footY"),
                Width: 16,
                Height: 16,
                Flags: WorldTileFlags.Solid));
    }

    [Fact]
    public void Validator_rejects_camera_aabb_when_target_has_no_collision_query_support()
    {
        var capabilities = FullCapabilities() with
        {
            Name = "nes",
            CollisionQueries = CollisionQueryMode.None,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                capabilities,
                new Sdk2DOperation.CameraAabbTiles(
                    WorldId: "level1",
                    ScreenX: 72,
                    WorldY: Field("player", "footY"),
                    Width: 16,
                    Height: 8,
                    Flags: WorldTileFlags.Solid)));

        Assert.Equal(
            "Target 'nes' does not support camera-relative AABB collision queries.",
            exception.Message);
    }

    [Fact]
    public void Validator_rejects_world_tile_flags_when_target_has_no_flag_query_support()
    {
        var capabilities = FullCapabilities() with
        {
            Name = "nes",
            CollisionQueries = CollisionQueryMode.None,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                capabilities,
                new Sdk2DOperation.ReadWorldTileFlags("level1", WorldX: 16, WorldY: 24)));

        Assert.Equal(
            "Target 'nes' does not support world tile flag queries.",
            exception.Message);
    }

    [Fact]
    public void Validator_rejects_camera_aabb_hit_top_when_target_has_no_hit_query_support()
    {
        var capabilities = FullCapabilities() with
        {
            Name = "nes",
            CollisionQueries = CollisionQueryMode.CameraRelativeAabb,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                capabilities,
                new Sdk2DOperation.CameraAabbHitTop(
                    WorldId: "level1",
                    ScreenX: 72,
                    WorldY: Field("player", "footY"),
                    Width: 16,
                    Height: 40,
                    Flags: WorldTileFlags.Solid)));

        Assert.Equal(
            "Target 'nes' does not support camera-relative AABB hit-top queries.",
            exception.Message);
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

    [Fact]
    public void Validator_rejects_diagonal_camera_movement_on_streaming_target_when_combined_streaming_exceeds_budget_without_staggering()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                FullCapabilities(),
                new Sdk2DOperation.SetCameraPosition(
                    Local("cameraX"),
                    Local("cameraY"),
                    ScrollAxes.Horizontal | ScrollAxes.Vertical)));

        Assert.Equal(
            "Target 'gb' supports 21 background tile writes per frame, but 40 are required for moving the camera diagonally (19 column tiles + 21 row tiles).",
            exception.Message);
    }

    [Fact]
    public void Validator_accepts_diagonal_camera_movement_when_streaming_target_staggers_edges_within_budget()
    {
        var target = FullCapabilities() with
        {
            MaxBackgroundTileWritesPerFrame = 21,
            StaggersCameraMovementStreams = true,
        };

        Sdk2DOperationValidator.Validate(
            target,
            new Sdk2DOperation.SetCameraPosition(
                Local("cameraX"),
                Local("cameraY"),
                ScrollAxes.Horizontal | ScrollAxes.Vertical));
    }

    [Fact]
    public void Validator_rejects_staggered_diagonal_camera_movement_when_one_edge_exceeds_budget()
    {
        var target = FullCapabilities() with
        {
            MaxBackgroundTileWritesPerFrame = 19,
            StaggersCameraMovementStreams = true,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                target,
                new Sdk2DOperation.SetCameraPosition(
                    Local("cameraX"),
                    Local("cameraY"),
                    ScrollAxes.Horizontal | ScrollAxes.Vertical)));

        Assert.Equal(
            "Target 'gb' supports 19 background tile writes per frame, but 21 are required for moving the camera diagonally with staggered streaming (max of 19 column tiles and 21 row tiles).",
            exception.Message);
    }

    [Fact]
    public void Validator_accepts_horizontal_fine_scroll_camera_on_target_without_streaming_budget()
    {
        var fineScrollOnlyTarget = FullCapabilities() with
        {
            ScrollAxes = ScrollAxes.Horizontal,
            MaxBackgroundTileWritesPerFrame = 0,
        };

        Sdk2DOperationValidator.Validate(
            fineScrollOnlyTarget,
            new Sdk2DOperation.SetCameraPosition(X: 32, Y: 0, Axes: ScrollAxes.Horizontal));
    }

    [Fact]
    public void Validator_rejects_camera_position_when_target_lacks_required_fine_scroll()
    {
        var target = FullCapabilities() with
        {
            SupportsFineScrollX = false,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                target,
                new Sdk2DOperation.SetCameraPosition(X: 32, Y: 0, Axes: ScrollAxes.Horizontal)));

        Assert.Equal(
            "Target 'gb' does not support horizontal fine scrolling.",
            exception.Message);
    }

    [Fact]
    public void Validator_rejects_stream_map_row_when_target_supports_vertical_camera_but_not_runtime_row_streaming()
    {
        var target = FullCapabilities() with
        {
            CameraMovementStreamsBackground = false,
            RuntimeBackgroundStreamingAxes = ScrollAxes.Horizontal,
        };

        Sdk2DOperationValidator.Validate(
            target,
            new Sdk2DOperation.SetCameraPosition(X: 0, Y: 16, Axes: ScrollAxes.Vertical));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                target,
                new Sdk2DOperation.StreamMapRow(TargetRow: 29, SourceRow: 40, X: 0, Width: 20)));

        Assert.Equal(
            "Target 'gb' does not support runtime vertical background streaming.",
            exception.Message);
    }

    [Fact]
    public void Validator_rejects_stream_map_column_when_height_exceeds_budget()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.Validate(
                FullCapabilities(),
                new Sdk2DOperation.StreamMapColumn(TargetColumn: 31, SourceColumn: 64, Y: 0, Height: 22)));

        Assert.Equal(
            "Target 'gb' supports 21 background tile writes per frame, but 22 are required for streaming a visible map column.",
            exception.Message);
    }

    [Fact]
    public void Frame_validator_accepts_combined_background_streaming_at_budget()
    {
        Sdk2DOperationValidator.ValidateFrame(
            FullCapabilities(),
            [
                new Sdk2DOperation.StreamMapColumn(TargetColumn: 31, SourceColumn: 64, Y: 0, Height: 12),
                new Sdk2DOperation.StreamMapRow(TargetRow: 17, SourceRow: 48, X: 0, Width: 8),
            ]);
    }

    [Fact]
    public void Frame_validator_rejects_combined_background_streaming_that_exceeds_budget()
    {
        var frameOperations = new Sdk2DOperation[]
        {
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 31, SourceColumn: 64, Y: 0, Height: 12),
            new Sdk2DOperation.StreamMapRow(TargetRow: 17, SourceRow: 48, X: 0, Width: 10),
        };

        foreach (var operation in frameOperations)
        {
            Sdk2DOperationValidator.Validate(FullCapabilities(), operation);
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.ValidateFrame(FullCapabilities(), frameOperations));

        Assert.Equal(
            "Target 'gb' supports 21 background tile writes per frame, but 22 are required for streaming background tiles in one frame.",
            exception.Message);
    }

    [Fact]
    public void Frame_validator_rejects_unsupported_sprite_size_modes()
    {
        var target = FullCapabilities() with
        {
            SpriteSizeModes = SpriteSizeMode.Sprite8x8,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Sdk2DOperationValidator.ValidateFrameBudget(
                target,
                new Sdk2DFrameBudget(spriteSizeModes: SpriteSizeMode.Sprite8x16)));

        Assert.Equal(
            "Target 'gb' does not support 8x16 sprite size mode.",
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
            MaxBackgroundTileWritesPerFrame: 21,
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

    private static SdkByteExpression.Variable Local(string name)
    {
        return new SdkByteExpression.Variable(new SdkStorageLocation.Local(name));
    }

    private static SdkByteExpression.Variable Field(string baseName, string fieldName)
    {
        return new SdkByteExpression.Variable(
            new SdkStorageLocation.Field(
                new SdkStorageLocation.Local(baseName),
                fieldName));
    }
}
