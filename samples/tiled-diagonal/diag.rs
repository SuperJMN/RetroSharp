import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 40;
    const i16 StreamY = 0;
    const i16 Height = 40;
}

static class Scroll
{
    const i16 Max = 160;
}

void Main()
{
    Video.Init();

    World.Load("diag.tmj");
    Camera.Init(Level.Width, Level.StreamY, Level.Height);

    u8 cameraX = 0;
    u8 cameraY = 0;
    u8 direction = 1;

    while (true)
    {
        Video.WaitVBlank();
        Camera.SetPosition(cameraX, cameraY);
        Camera.Apply();

        if (direction == 1)
        {
            if (cameraX < Scroll.Max)
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
            if (cameraX > 0)
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
    }
}
