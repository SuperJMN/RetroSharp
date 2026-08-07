import RetroSharp.Portable2D;

void Main()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 0, 1, 3);
    Sprite.Asset(marker, "assets/marker.json");
    World.Load("../full-stage1-v1/assets/stage1.tmx");
    Camera.Init(156, 0, 20);

    i16 cameraX = 0;
    Camera.SetPosition(cameraX, 0);

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
        Sprite.Draw(marker, 16, 16, 0, false, 0);
        Input.Poll();
        if (cameraX < 224)
        {
            cameraX += 2;
        }
        Camera.SetPosition(cameraX, 0);
    }
}
