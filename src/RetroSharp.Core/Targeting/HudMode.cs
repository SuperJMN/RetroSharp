namespace RetroSharp.Core.Targeting;

[Flags]
public enum HudMode
{
    None = 0,
    Window = 1 << 0,
    SplitScroll = 1 << 1,
    Sprite = 1 << 2,
}
