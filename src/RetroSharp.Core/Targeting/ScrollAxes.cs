namespace RetroSharp.Core.Targeting;

[Flags]
public enum ScrollAxes
{
    None = 0,
    Horizontal = 1 << 0,
    Vertical = 1 << 1,
}
