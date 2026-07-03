import RetroSharp.Portable2D;

void SetupVideo()
{
    Video.Init();
    Palette.Set(0, 0);
    Palette.Set(1, 1);
    Palette.Set(2, 2);
    Palette.Set(3, 3);
    return;
}

void DrawBackground()
{
    Tilemap.Set(2, 4, 1);
    Tilemap.Set(3, 4, 1);
    Tilemap.Set(4, 5, 1);
    Tilemap.Set(11, 3, 1);
    Tilemap.Set(12, 3, 1);
    Tilemap.Set(13, 4, 1);
    Tilemap.Set(22, 4, 1);
    Tilemap.Set(23, 4, 1);
    Tilemap.Set(24, 5, 1);
    Tilemap.Set(1, 8, 2);
    Tilemap.Set(2, 8, 2);
    Tilemap.Set(3, 8, 2);
    Tilemap.Set(9, 8, 2);
    Tilemap.Set(10, 7, 2);
    Tilemap.Set(11, 8, 2);
    Tilemap.Set(16, 8, 2);
    Tilemap.Set(17, 8, 2);
    Tilemap.Set(25, 8, 2);
    Tilemap.Set(26, 8, 2);
    Tilemap.Set(27, 8, 2);
    return;
}

void DefineWorld()
{
    World.Column(0, 0, 0, 2, 0, 4, 5);
    World.Column(1, 0, 0, 2, 0, 4, 5);
    World.Column(2, 0, 0, 0, 0, 4, 5);
    World.Column(3, 0, 0, 0, 0, 4, 5);
    World.Column(4, 0, 0, 0, 0, 4, 5);
    World.Column(5, 5, 0, 0, 0, 4, 5);
    World.Column(6, 5, 0, 0, 0, 4, 5);
    World.Column(7, 5, 0, 0, 0, 3, 5);
    World.Column(8, 5, 0, 2, 0, 3, 5);
    World.Column(9, 0, 0, 2, 0, 4, 5);
    World.Column(10, 0, 0, 2, 0, 4, 5);
    World.Column(11, 0, 0, 0, 0, 4, 5);
    World.Column(12, 0, 0, 0, 0, 3, 5);
    World.Column(13, 0, 0, 0, 0, 3, 5);
    World.Column(14, 0, 0, 0, 0, 0, 0);
    World.Column(15, 0, 0, 0, 0, 0, 0);
    World.Flags(0, 0, 0, 0, 0, 1, 1);
    World.Flags(1, 0, 0, 0, 0, 1, 1);
    World.Flags(2, 0, 0, 0, 0, 1, 1);
    World.Flags(3, 0, 0, 0, 0, 1, 1);
    World.Flags(4, 0, 0, 0, 0, 1, 1);
    World.Flags(5, 1, 0, 0, 0, 1, 1);
    World.Flags(6, 1, 0, 0, 0, 1, 1);
    World.Flags(7, 1, 0, 0, 0, 2, 1);
    World.Flags(8, 1, 0, 0, 0, 2, 1);
    World.Flags(9, 0, 0, 0, 0, 1, 1);
    World.Flags(10, 0, 0, 0, 0, 1, 1);
    World.Flags(11, 0, 0, 0, 0, 1, 1);
    World.Flags(12, 0, 0, 0, 0, 2, 1);
    World.Flags(13, 0, 0, 0, 0, 2, 1);
    World.Flags(14, 0, 0, 0, 0, 0, 0);
    World.Flags(15, 0, 0, 0, 0, 0, 0);
    return;
}

void Main()
{
    SetupVideo();
    DrawBackground();
    DefineWorld();
    World.Map(16, 9, 6);

    while (true)
    {
        Video.WaitVBlank();
    }
}
