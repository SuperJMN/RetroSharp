type Pixel = i16;

static class Level {
    const i16 Width = 48;
    const i16 StreamY = 0;
    const i16 Height = 96;
    const i16 StreamHeight = 96;
    const i16 PixelWidth = 384;
}

static class Player {
    const i16 StartX = 72;
    const i16 StartY = 193;
    const i16 FootOffset = 31;
    const i16 FallResetY = 240;
}

static class DeadZone {
    const i16 Left = 64;
    const i16 Right = 96;
    const i16 Top = 56;
    const i16 Bottom = 88;
}

static class CameraBounds {
    const i16 MaxY = 196;
}

static class CollisionProbe {
    const i16 LandingSearchTopOffset = 4;
    const i16 LandingSearchHeight = 12;
    const i16 LeftWallProbeOffset = 1;
    const i16 RightWallProbeOffset = 1;
    const i16 WallProbeHeight = 8;
    const i16 CeilingProbeTopOffset = 28;
    const i16 CeilingProbeHeight = 4;
    const i16 NoTileHit = 255;
}

static class Jump {
    const i8 Velocity = -3;
    const u8 BoostTicks = 12;
    const u8 GravityFrames = 2;
    const u8 BoostTickMask = 1;
    const i8 BounceVelocity = 2;
}

enum Direction {
    None = 0,
    Right = 1,
    Left = 2
}

static class MotionSpeed {
    const i16 Walk = 8;
    const i16 RunMax = 12;
    const i16 Subpixel = 8;
    const i16 RunAcceleration = 1;
    const i16 Friction = 2;
}

static class RunAnimation {
    const u8 CycleTicks = 144;
}

enum CollisionFlag { None = 0, Solid = 1 }
