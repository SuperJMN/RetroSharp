import RetroSharp.Portable2D;

type Pixel = i16;

static class Level
{
    const i16 Width = 312;
    const i16 StreamY = 0;
    const i16 StreamHeight = 40;
}

static class Fast
{
    const Pixel HorizontalMax = 320;
    const u8 Subpixel = 16;
    const u8 Speed = 20;
}

enum VerticalDirection
{
    Down = 0,
    Up = 1
}

enum HorizontalDirection
{
    Right = 0,
    Left = 1
}

void Main()
{
    Video.Init();

    World.Load("../shared/platformer-assets/maps/stage1.tmx");
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);

    Pixel maxCameraY = Camera.VerticalScrollMax();
    Pixel x = 0;
    Pixel y = 0;
    u8 vertical = VerticalDirection.Down;
    u8 horizontal = HorizontalDirection.Right;
    u8 remainder = 0;

    while (true)
    {
        Video.WaitVBlank();
        Camera.SetPosition(x, y);
        Camera.Apply();

        if (vertical == VerticalDirection.Down)
        {
            if (y < maxCameraY)
            {
                y += 1;
            }
            else
            {
                vertical = VerticalDirection.Up;
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
                vertical = VerticalDirection.Down;
                y += 1;
            }
        }

        remainder += Fast.Speed;
        u8 steps = 0;
        while (steps < 2)
        {
            if (remainder >= Fast.Subpixel)
            {
                remainder -= Fast.Subpixel;
                if (horizontal == HorizontalDirection.Right)
                {
                    if (x < Fast.HorizontalMax)
                    {
                        x += 1;
                    }
                    else
                    {
                        horizontal = HorizontalDirection.Left;
                        x -= 1;
                    }
                }
                else
                {
                    if (x > 0)
                    {
                        x -= 1;
                    }
                    else
                    {
                        horizontal = HorizontalDirection.Right;
                        x += 1;
                    }
                }
            }

            steps += 1;
        }
    }
}
