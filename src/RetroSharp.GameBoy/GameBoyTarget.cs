namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;

public static class GameBoyTarget
{
    public static TargetIntrinsicCatalog Intrinsics { get; } = new(
        "gb",
        "Game Boy",
        [
            TargetIntrinsicDescriptor.WaitFrame("wait_frame", arity: 0),
            TargetIntrinsicDescriptor.WaitFrame("wait_vblank", arity: 0),
            TargetIntrinsicDescriptor.PollInput("poll_input", arity: 0),
            TargetIntrinsicDescriptor.UpdateAudio("audio_update", arity: 0),
            TargetIntrinsicDescriptor.ReadWorldTileFlags("world_tile_flags_at", arity: 2),
            TargetIntrinsicDescriptor.ReadWorldTileFlags(
                "world_tile_flags_for_world",
                runtimeArity: 2,
                compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.WorldId)]),
            TargetIntrinsicDescriptor.SetCameraPosition("camera_set_position", arity: 2),
            TargetIntrinsicDescriptor.ApplyCamera("camera_apply", arity: 0),
        ]);

    public static TargetAudioCapabilities AudioCapabilities { get; } = new(
        Name: "gb",
        SupportsBgm: true,
        SupportedMusicFormats: ["uge", "gbapu", "vgm"]);

    public static Target2DCapabilities Capabilities { get; } = new(
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
        CollisionQueries: CollisionQueryMode.WorldTileFlags | CollisionQueryMode.WorldAabb | CollisionQueryMode.CameraRelativeAabb | CollisionQueryMode.CameraRelativeAabbHitTop)
    {
        StaggersCameraMovementStreams = true,
    };
}
