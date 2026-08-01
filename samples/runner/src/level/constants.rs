type Pixel = i16;

static class Level
{
    const i16 Width = 312;
    const i16 StreamY = 0;
    const i16 Height = 40;
    const i16 StreamHeight = 40;
    const i16 PixelWidth = 2496;
}

static class Player
{
    const i16 StartX = 72;
    const i16 StartY = 273;
    const i16 FootOffset = 31;
    const i16 FallResetY = 320;
}

static class DeadZone
{
    const i16 Left = Viewport.Width / 4;
    const i16 Right = Viewport.Width * 3 / 8;
    const i16 Top = 56;
    const i16 Bottom = 88;
}

static class CollisionProbe
{
    const i16 LandingSearchTopOffset = 3;
    const i16 LandingSearchHeight = 9;
    const i16 LeftWallProbeOffset = 1;
    const i16 RightWallProbeOffset = 1;
    const i16 WallProbeHeight = 8;
    const i16 CeilingProbeTopOffset = 28;
    const i16 CeilingProbeHeight = 4;
}

static class Jump
{
    const i8 StandingVelocity = -56;
    const i8 WalkingVelocity = -58;
    const i8 RunningVelocity = -60;
    const i8 PSpeedVelocity = -64;
    const i8 HeldGravityThreshold = -32;
    const i8 HeldGravity = 1;
    const i8 ReleasedGravity = 5;
    const i8 TerminalVelocity = 69;
    const i8 BounceVelocity = 32;
    const i16 Subpixel = 16;
}

enum Direction
{
    None = 0,
    Right = 1,
    Left = 2
}

static class MotionSpeed
{
    const i16 Walk = 20;
    const i16 RunMax = 32;
    const i16 Subpixel = 16;
    const i16 Acceleration = 1;
    const i16 Friction = 1;
    const i16 SkidAcceleration = 2;
    const u8 MaxSteps = 2;
}

static class RunAnimation
{
    const u8 CycleTicks = 144;
}

enum CollisionFlag
{
    None = 0,
    Solid = 1,
    Platform = 4,
    Landable = 5,
}
