import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 50;
    const i16 StreamY = 0;
    const i16 Height = 60;
    const i16 MaxCameraX = 144;
    const i16 MaxCameraY = 240;
}

void Main()
{
    Video.Init();

    World.Load("free-scroll.tmj");
    Camera.Init(Level.Width, Level.StreamY, Level.Height);

    i16 cameraX = 0;
    i16 cameraY = 0;
    u8 direction = 1;

    while (true)
    {
        Video.WaitVBlank();

        if (direction == 1)
        {
            if (cameraX < Level.MaxCameraX && cameraY < Level.MaxCameraY)
            {
                cameraX += 1;
                cameraY += 1;
            }
            else
            {
                direction = 0;
                cameraX -= 1;
                cameraY -= 1;
            }
        }
        else
        {
            if (cameraX > 0 && cameraY > 0)
            {
                cameraX -= 1;
                cameraY -= 1;
            }
            else
            {
                direction = 1;
                cameraX += 1;
                cameraY += 1;
            }
        }

        Camera.SetPosition(cameraX, cameraY);
        Camera.Apply();
    }
}
