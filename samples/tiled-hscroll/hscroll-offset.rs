import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 312;
    const i16 StreamY = 0;
    const i16 StreamHeight = 40;
    // Keep the focused non-zero-Y canary short enough to exercise both
    // directions inside its 420-frame functional window.
    const i16 MaxCameraX = 96;
    const u8 BottomAlignmentTicks = 64;
}

void Main()
{
    Video.Init();

    World.Load("stage1-full.tmj");
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);

    i16 cameraX = 0;
    i16 cameraY = Camera.VerticalScrollMax();
    u8 direction = 1;
    u8 bottomAlignmentTicks = 0;

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
        Camera.SetPosition(cameraX, cameraY);

        if (bottomAlignmentTicks < Level.BottomAlignmentTicks)
        {
            bottomAlignmentTicks++;
        }
        else if (direction == 1)
        {
            if (cameraX < Level.MaxCameraX)
            {
                cameraX += 1;
            }
            else
            {
                direction = 0;
            }
        }
        else
        {
            if (cameraX > 0)
            {
                cameraX -= 1;
            }
            else
            {
                direction = 1;
            }
        }
    }
}
