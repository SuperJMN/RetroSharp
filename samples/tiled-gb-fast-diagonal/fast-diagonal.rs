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
    // Two pixels per frame on both axes: a new column and a new row every four
    // frames. This is the speed at which the diagonal streaming edges have the
    // least margin between preparation and visibility.
    const Pixel Step = 2;
    const Pixel HorizontalMax = 320;
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

    while (true)
    {
        Video.WaitVBlank();
        Camera.SetPosition(x, y);
        Camera.Apply();

        if (vertical == VerticalDirection.Down)
        {
            if (y < maxCameraY)
            {
                y += Fast.Step;
            }
            else
            {
                vertical = VerticalDirection.Up;
                y -= Fast.Step;
            }
        }
        else
        {
            if (y > 0)
            {
                y -= Fast.Step;
            }
            else
            {
                vertical = VerticalDirection.Down;
                y += Fast.Step;
            }
        }

        if (horizontal == HorizontalDirection.Right)
        {
            if (x < Fast.HorizontalMax)
            {
                x += Fast.Step;
            }
            else
            {
                horizontal = HorizontalDirection.Left;
                x -= Fast.Step;
            }
        }
        else
        {
            if (x > 0)
            {
                x -= Fast.Step;
            }
            else
            {
                horizontal = HorizontalDirection.Right;
                x += Fast.Step;
            }
        }
    }
}
