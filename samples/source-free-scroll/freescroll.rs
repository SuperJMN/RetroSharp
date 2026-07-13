import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 64;
    const i16 Height = 60;
}

void Main()
{
    Video.Init();

    World.Column(0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5);
    World.Column(15, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1);
    World.Column(31, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2);
    World.Column(32, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3);
    World.Column(47, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4);
    World.Column(63, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 5);
    World.Map(Level.Width, 0, Level.Height);
    Camera.Init(Level.Width, 0, Level.Height);

    u8 cameraX = 0;
    u8 cameraY = 0;
    u8 stepX = 1;
    u8 stepY = 1;
    u8 boundaryPause = 0;

    while (true)
    {
        Camera.SetPosition(cameraX, cameraY);
        Video.WaitVBlank();
        Camera.Apply();

        if ((((stepX == 1 && (cameraX & 7) == 0) || (stepX == 255 && (cameraX & 7) == 7))
            && ((stepY == 1 && (cameraY & 7) == 0) || (stepY == 255 && (cameraY & 7) == 7)))
            && boundaryPause == 0)
        {
            boundaryPause = 1;
        }
        else
        {
            boundaryPause = 0;
            if (cameraX == 248)
            {
                stepX = 255;
            }
            else if (cameraX == 0)
            {
                stepX = 1;
            }

            if (cameraY == 248)
            {
                stepY = 255;
            }
            else if (cameraY == 0)
            {
                stepY = 1;
            }

            cameraX += stepX;
            cameraY += stepY;
        }
    }
}
