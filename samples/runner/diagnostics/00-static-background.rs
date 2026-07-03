import RetroSharp.Portable2D;

void SetupVideo() {
    Video.Init();
    Palette.Set(0, 0);
    Palette.Set(1, 1);
    Palette.Set(2, 2);
    Palette.Set(3, 3);
    return;
}

void DrawBackground() {
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

void Main() {
    SetupVideo();
    DrawBackground();

    while (true) {
        Video.WaitVBlank();
    }
}
