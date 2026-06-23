namespace RetroSharp.Core.Targeting;

[Flags]
public enum CollisionQueryMode
{
    None = 0,
    WorldTileFlags = 1,
    WorldAabb = 2,
    CameraRelativeAabb = 4,
}
