namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;

public static class NesTarget
{
    public static TargetIntrinsicCatalog Intrinsics { get; } = new(
        "nes",
        "NES",
        [
            TargetIntrinsicDescriptor.WaitFrame("wait_frame", arity: 0),
            TargetIntrinsicDescriptor.WaitFrame("wait_vblank", arity: 0),
            TargetIntrinsicDescriptor.PollInput("poll_input", arity: 0),
            TargetIntrinsicDescriptor.UpdateAudio("audio_update", arity: 0),
            TargetIntrinsicDescriptor.SetCameraPosition("camera_set_position", arity: 2),
            TargetIntrinsicDescriptor.ApplyCamera("camera_apply", arity: 0),
        ]);

    public static TargetAudioCapabilities AudioCapabilities { get; } = new(
        Name: "nes",
        SupportsBgm: true,
        SupportedMusicFormats: ["vgm"]);

    public static Target2DCapabilities Capabilities { get; } = new(
        Name: "nes",
        ScreenPixels: new Size2D(256, 240),
        ScreenTiles: new Size2D(32, 30),
        TileSize: new Size2D(8, 8),
        BackgroundBufferTiles: new Size2D(64, 60),
        ScrollAxes: ScrollAxes.Horizontal | ScrollAxes.Vertical,
        SupportsFineScrollX: true,
        SupportsFineScrollY: true,
        MaxBackgroundTileWritesPerFrame: 32,
        MaxAttributeWritesPerFrame: 9,
        SpriteCount: 64,
        SpriteSizeModes: SpriteSizeMode.Sprite8x8 | SpriteSizeMode.Sprite8x16,
        MaxSpritesPerScanline: 8,
        SpritePaletteSlots: 4,
        BackgroundPaletteSlots: 4,
        SupportedSpriteTransforms: SpriteTransform.FlipX | SpriteTransform.FlipY,
        HudModes: HudMode.None,
        CollisionQueries: CollisionQueryMode.CameraRelativeAabb | CollisionQueryMode.CameraRelativeAabbHitTop)
    {
        CameraMovementStreamsBackground = false,
        RuntimeBackgroundStreamingAxes = ScrollAxes.Horizontal | ScrollAxes.Vertical,
    };
}
