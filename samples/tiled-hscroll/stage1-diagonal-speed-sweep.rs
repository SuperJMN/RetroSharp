import RetroSharp.Portable2D;

type Pixel = i16;

static class Level
{
    const i16 Width = 312;
    const i16 StreamY = 0;
    const i16 StreamHeight = 40;
}

inline pure [target("gb")] Pixel MaxCameraX() => 2336;

inline pure [target("nes")] Pixel MaxCameraX() => 2240;

enum SweepPhase
{
    Slow = 0,
    RewindToMedium = 1,
    Medium = 2,
    RewindToFast = 3,
    Fast = 4,
    RewindToSlow = 5
}

enum VerticalDirection
{
    Down = 0,
    Up = 1
}

static class Sweep
{
    const u8 EndHoldTicks = 120;
    const u8 StartHoldTicks = 120;
    const u8 MediumStep = 1;
    const u8 FastStep = 2;
    const u8 RewindStep = 2;
}

class SweepState
{
    Pixel x;
    Pixel y;
    u8 phase;
    u8 verticalDirection;
    u8 slowDivider;
    u8 holdTicks;

    inline void Reset()
    {
        x = 0;
        y = 0;
        phase = SweepPhase.Slow;
        verticalDirection = VerticalDirection.Down;
        slowDivider = 0;
        holdTicks = Sweep.StartHoldTicks;
    }

    inline void MoveVertical(Pixel maxY)
    {
        if (verticalDirection == VerticalDirection.Down)
        {
            if (y < maxY)
            {
                y += 1;
            }
            else
            {
                verticalDirection = VerticalDirection.Up;
                y -= 1;
            }
        }
        else
        {
            if (y > 0)
            {
                y -= 1;
            }
            else
            {
                verticalDirection = VerticalDirection.Down;
                y += 1;
            }
        }
    }

    void MoveForward(u8 pixels, Pixel maxX, Pixel maxY)
    {
        u8 moved = 0;
        while (moved < pixels)
        {
            if (x < maxX)
            {
                x += 1;
                MoveVertical(maxY);
            }
            moved += 1;
        }
    }

    void Rewind(u8 pixels)
    {
        u8 moved = 0;
        while (moved < pixels)
        {
            if (x > 0)
            {
                x -= 1;
            }
            if (y > 0)
            {
                y -= 1;
            }
            moved += 1;
        }
    }

    inline void BeginForward(u8 nextPhase)
    {
        phase = nextPhase;
        y = 0;
        verticalDirection = VerticalDirection.Down;
        holdTicks = Sweep.StartHoldTicks;
    }

    inline void BeginRewind(u8 nextPhase)
    {
        phase = nextPhase;
        holdTicks = Sweep.EndHoldTicks;
    }

    inline void Update(Pixel maxX, Pixel maxY)
    {
        if (holdTicks > 0)
        {
            holdTicks -= 1;
        }
        else if (phase == SweepPhase.Slow)
        {
            if (slowDivider == 0)
            {
                MoveForward(1, maxX, maxY);
                slowDivider = 1;
            }
            else
            {
                slowDivider -= 1;
            }

            if (x >= maxX)
            {
                BeginRewind(SweepPhase.RewindToMedium);
            }
        }
        else if (phase == SweepPhase.RewindToMedium)
        {
            Rewind(Sweep.RewindStep);
            if (x == 0)
            {
                BeginForward(SweepPhase.Medium);
            }
        }
        else if (phase == SweepPhase.Medium)
        {
            MoveForward(Sweep.MediumStep, maxX, maxY);
            if (x >= maxX)
            {
                BeginRewind(SweepPhase.RewindToFast);
            }
        }
        else if (phase == SweepPhase.RewindToFast)
        {
            Rewind(Sweep.RewindStep);
            if (x == 0)
            {
                BeginForward(SweepPhase.Fast);
            }
        }
        else if (phase == SweepPhase.Fast)
        {
            MoveForward(Sweep.FastStep, maxX, maxY);
            if (x >= maxX)
            {
                BeginRewind(SweepPhase.RewindToSlow);
            }
        }
        else
        {
            Rewind(Sweep.RewindStep);
            if (x == 0)
            {
                BeginForward(SweepPhase.Slow);
                slowDivider = 0;
            }
        }
    }
}

void Main()
{
    Video.Init();

    World.Load("../shared/platformer-assets/maps/stage1.tmx");
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);

    Pixel maxCameraX = MaxCameraX();
    Pixel maxCameraY = Camera.VerticalScrollMax();
    SweepState sweep;
    sweep.Reset();

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
        Camera.SetPosition(sweep.x, sweep.y);
        sweep.Update(maxCameraX, maxCameraY);
    }
}
