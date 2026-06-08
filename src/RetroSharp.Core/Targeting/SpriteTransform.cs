namespace RetroSharp.Core.Targeting;

[Flags]
public enum SpriteTransform
{
    None = 0,
    FlipX = 1 << 0,
    FlipY = 1 << 1,
}
