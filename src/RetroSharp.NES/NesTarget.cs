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
            TargetIntrinsicDescriptor.InitializeVideo("video_init", arity: 0),
            TargetIntrinsicDescriptor.PresentVideo("video_present", arity: 0),
            TargetIntrinsicDescriptor.PollInput("poll_input", arity: 0),
            TargetIntrinsicDescriptor.ButtonDown("button_down", arity: 1),
            TargetIntrinsicDescriptor.ButtonJustPressed("button_just_pressed", arity: 1),
            TargetIntrinsicDescriptor.ButtonJustReleased("button_just_released", arity: 1),
            TargetIntrinsicDescriptor.ButtonHoldTicks("button_hold_ticks", arity: 1),
            TargetIntrinsicDescriptor.InitializeAudio("audio_init", arity: 0),
            TargetIntrinsicDescriptor.UpdateAudio("audio_update", arity: 0),
            TargetIntrinsicDescriptor.PlayMusic(
                "music_play",
                runtimeArity: 0,
                compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]),
            TargetIntrinsicDescriptor.PlaySoundEffect(
                "sfx_play",
                runtimeArity: 0,
                compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]),
            TargetIntrinsicDescriptor.StopMusic("music_stop", arity: 0),
            TargetIntrinsicDescriptor.InitializeCamera("camera_init", arity: 3),
            TargetIntrinsicDescriptor.DrawLogicalSprite(
                "sprite_draw",
                runtimeArity: 4,
                compileTimeOperands:
                [
                    new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef),
                    new TargetIntrinsicCompileTimeOperand(5, TargetIntrinsicOperandRole.ConstPaletteSlot),
                ]),
            TargetIntrinsicDescriptor.ReadSpriteWidth(
                "sprite_width",
                runtimeArity: 0,
                compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]),
            TargetIntrinsicDescriptor.ReadAnimationFrame(
                "animation_frame",
                runtimeArity: 1,
                compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]),
            TargetIntrinsicDescriptor.CameraAabbTiles(
                "camera_aabb_tiles",
                runtimeArity: 4,
                compileTimeOperands:
                [
                    new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.WorldId),
                    new TargetIntrinsicCompileTimeOperand(5, TargetIntrinsicOperandRole.EnumFlags),
                ]),
            TargetIntrinsicDescriptor.CameraAabbHitTop(
                "camera_aabb_hit_top",
                runtimeArity: 4,
                compileTimeOperands:
                [
                    new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.WorldId),
                    new TargetIntrinsicCompileTimeOperand(5, TargetIntrinsicOperandRole.EnumFlags),
                ]),
            TargetIntrinsicDescriptor.CameraScreenAabbTiles(
                "camera_screen_aabb_tiles",
                runtimeArity: 4,
                compileTimeOperands:
                [
                    new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.WorldId),
                    new TargetIntrinsicCompileTimeOperand(5, TargetIntrinsicOperandRole.EnumFlags),
                ]),
            TargetIntrinsicDescriptor.CameraScreenAabbHitTop(
                "camera_screen_aabb_hit_top",
                runtimeArity: 4,
                compileTimeOperands:
                [
                    new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.WorldId),
                    new TargetIntrinsicCompileTimeOperand(5, TargetIntrinsicOperandRole.EnumFlags),
                ]),
            TargetIntrinsicDescriptor.SetCameraPosition("camera_set_position", arity: 2),
            TargetIntrinsicDescriptor.ApplyCamera("camera_apply", arity: 0),
            TargetIntrinsicDescriptor.CameraVerticalScrollMax("camera_vertical_scroll_max", arity: 0),
        ]);

    public static TargetAudioCapabilities AudioCapabilities { get; } = new(
        Name: "nes",
        SupportsBgm: true,
        SupportedMusicFormats: ["vgm"],
        SupportsSfx: true,
        SupportedSfxFormats: ["vgm"]);

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
