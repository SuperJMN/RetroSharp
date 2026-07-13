import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 48;
    const i16 StreamY = 10;
    const i16 Height = 4;
}

static class Marker
{
    const i16 ScreenX = 72;
    const i16 ScreenY = 72;
}

void Main()
{
    Video.Init();

    World.Column(0, 1, 2, 3, 4);
    World.Column(1, 2, 3, 4, 5);
    World.Column(2, 3, 4, 5, 1);
    World.Column(3, 4, 5, 1, 2);
    World.Column(4, 5, 1, 2, 3);
    World.Column(5, 1, 2, 3, 4);
    World.Column(6, 2, 3, 4, 5);
    World.Column(7, 3, 4, 5, 1);
    World.Column(8, 1, 2, 3, 4);
    World.Column(9, 2, 3, 4, 5);
    World.Column(10, 3, 4, 5, 1);
    World.Column(11, 4, 5, 1, 2);
    World.Column(12, 5, 1, 2, 3);
    World.Column(13, 1, 2, 3, 4);
    World.Column(14, 2, 3, 4, 5);
    World.Column(15, 3, 4, 5, 1);
    World.Column(16, 1, 2, 3, 4);
    World.Column(17, 2, 3, 4, 5);
    World.Column(18, 3, 4, 5, 1);
    World.Column(19, 4, 5, 1, 2);
    World.Column(20, 5, 1, 2, 3);
    World.Column(21, 1, 2, 3, 4);
    World.Column(22, 2, 3, 4, 5);
    World.Column(23, 3, 4, 5, 1);
    World.Column(24, 1, 2, 3, 4);
    World.Column(25, 2, 3, 4, 5);
    World.Column(26, 3, 4, 5, 1);
    World.Column(27, 4, 5, 1, 2);
    World.Column(28, 5, 1, 2, 3);
    World.Column(29, 1, 2, 3, 4);
    World.Column(30, 2, 3, 4, 5);
    World.Column(31, 3, 4, 5, 1);
    World.Column(32, 1, 2, 3, 4);
    World.Column(33, 2, 3, 4, 5);
    World.Column(34, 3, 4, 5, 1);
    World.Column(35, 4, 5, 1, 2);
    World.Column(36, 5, 1, 2, 3);
    World.Column(37, 1, 2, 3, 4);
    World.Column(38, 2, 3, 4, 5);
    World.Column(39, 3, 4, 5, 1);
    World.Column(40, 1, 2, 3, 4);
    World.Column(41, 2, 3, 4, 5);
    World.Column(42, 3, 4, 5, 1);
    World.Column(43, 4, 5, 1, 2);
    World.Column(44, 5, 1, 2, 3);
    World.Column(45, 1, 2, 3, 4);
    World.Column(46, 2, 3, 4, 5);
    World.Column(47, 3, 4, 5, 1);
    World.Flags(0, 0, 0, 1, 1);
    World.Flags(1, 0, 0, 1, 1);
    World.Flags(2, 0, 0, 1, 1);
    World.Flags(3, 0, 0, 1, 1);
    World.Flags(4, 0, 0, 1, 1);
    World.Flags(5, 0, 0, 1, 1);
    World.Flags(6, 0, 0, 1, 1);
    World.Flags(7, 0, 0, 1, 1);
    World.Map(Level.Width, Level.StreamY, Level.Height);
    Camera.Init(Level.Width, Level.StreamY, Level.Height);
    Sprite.Asset(marker, "marker.json");

    u8 cameraX = 0;

    while (true)
    {
        Camera.SetPosition(cameraX, 0);
        Video.WaitVBlank();
        Camera.Apply();
        u8 frame = 0;
        bool flipX = false;
        Sprite.Draw(marker, Marker.ScreenX, Marker.ScreenY, frame, flipX, 0);

        Input.Poll();
        if (Input.IsDown(Button.Right))
        {
            if (cameraX < 80)
            {
                cameraX += 1;
            }
        }
        else if (Input.IsDown(Button.Left))
        {
            if (cameraX > 0)
            {
                cameraX -= 1;
            }
        }
    }
}
