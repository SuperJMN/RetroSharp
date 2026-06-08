namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public sealed record SpriteAssetMetadata(
    string Id,
    Size2D LogicalSize,
    Point2D Origin,
    Rect2D Hitbox,
    int PaletteSlots,
    IReadOnlyList<SpriteAnimationClip> AnimationClips)
{
    public static SpriteAssetMetadata Default(string id, Size2D logicalSize, int frameCount, int paletteSlots)
    {
        return new SpriteAssetMetadata(
            Id: id,
            LogicalSize: logicalSize,
            Origin: new Point2D(0, 0),
            Hitbox: new Rect2D(0, 0, logicalSize.Width, logicalSize.Height),
            PaletteSlots: paletteSlots,
            AnimationClips: [new SpriteAnimationClip("default", FirstFrame: 0, FrameCount: frameCount)]);
    }
}
