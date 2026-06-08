namespace RetroSharp.Core.Targeting;

[Flags]
public enum SpriteSizeMode
{
    None = 0,
    Sprite8x8 = 1 << 0,
    Sprite8x16 = 1 << 1,
    Sprite16x16 = 1 << 2,
}
