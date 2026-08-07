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
        Sprite.Draw(marker, 8, 16, 0, false, 0);
        Sprite.Draw(marker, 32, 16, 0, false, 0);
        Sprite.Draw(marker, 56, 16, 0, false, 0);
        Sprite.Draw(marker, 80, 16, 0, false, 0);
        Sprite.Draw(marker, 104, 16, 0, false, 0);
        Sprite.Draw(marker, 128, 16, 0, false, 0);
        Sprite.Draw(marker, 152, 16, 0, false, 0);
        Sprite.Draw(marker, 176, 16, 0, false, 0);
        Sprite.Draw(marker, 8, 40, 0, false, 0);
        Sprite.Draw(marker, 32, 40, 0, false, 0);
        Sprite.Draw(marker, 56, 40, 0, false, 0);
        Sprite.Draw(marker, 80, 40, 0, false, 0);
        Sprite.Draw(marker, 104, 40, 0, false, 0);
        Sprite.Draw(marker, 128, 40, 0, false, 0);
        Sprite.Draw(marker, 152, 40, 0, false, 0);
        Sprite.Draw(marker, 176, 40, 0, false, 0);
        Sprite.Draw(marker, 8, 64, 0, false, 0);
        Sprite.Draw(marker, 32, 64, 0, false, 0);
        Sprite.Draw(marker, 56, 64, 0, false, 0);
        Sprite.Draw(marker, 80, 64, 0, false, 0);
        Sprite.Draw(marker, 104, 64, 0, false, 0);
        Sprite.Draw(marker, 128, 64, 0, false, 0);
        Sprite.Draw(marker, 152, 64, 0, false, 0);
        Sprite.Draw(marker, 176, 64, 0, false, 0);
        Sprite.Draw(marker, 8, 88, 0, false, 0);
        Sprite.Draw(marker, 32, 88, 0, false, 0);
        Sprite.Draw(marker, 56, 88, 0, false, 0);
        Sprite.Draw(marker, 80, 88, 0, false, 0);
        Sprite.Draw(marker, 104, 88, 0, false, 0);
        Sprite.Draw(marker, 128, 88, 0, false, 0);
        Sprite.Draw(marker, 152, 88, 0, false, 0);
        Sprite.Draw(marker, 176, 88, 0, false, 0);
        Sprite.Draw(marker, 8, 112, 0, false, 0);
        Sprite.Draw(marker, 32, 112, 0, false, 0);
        Sprite.Draw(marker, 56, 112, 0, false, 0);
        Sprite.Draw(marker, 80, 112, 0, false, 0);
        Sprite.Draw(marker, 104, 112, 0, false, 0);
        Sprite.Draw(marker, 128, 112, 0, false, 0);
        Sprite.Draw(marker, 152, 112, 0, false, 0);
        Sprite.Draw(marker, 176, 112, 0, false, 0);
        Sprite.Draw(marker, 8, 136, 0, false, 0);
        Sprite.Draw(marker, 32, 136, 0, false, 0);
        Sprite.Draw(marker, 56, 136, 0, false, 0);
        Sprite.Draw(marker, 80, 136, 0, false, 0);
        Sprite.Draw(marker, 104, 136, 0, false, 0);
        Sprite.Draw(marker, 128, 136, 0, false, 0);
        Sprite.Draw(marker, 152, 136, 0, false, 0);
        Sprite.Draw(marker, 176, 136, 0, false, 0);
        Sprite.Draw(marker, 8, 160, 0, false, 0);
        Sprite.Draw(marker, 32, 160, 0, false, 0);
        Sprite.Draw(marker, 56, 160, 0, false, 0);
        Sprite.Draw(marker, 80, 160, 0, false, 0);
        Sprite.Draw(marker, 104, 160, 0, false, 0);
        Sprite.Draw(marker, 128, 160, 0, false, 0);
        Sprite.Draw(marker, 152, 160, 0, false, 0);
        Sprite.Draw(marker, 176, 160, 0, false, 0);
        Sprite.Draw(marker, 8, 184, 0, false, 0);
        Sprite.Draw(marker, 32, 184, 0, false, 0);
        Sprite.Draw(marker, 56, 184, 0, false, 0);
        Sprite.Draw(marker, 80, 184, 0, false, 0);
        Input.Poll();
        if (cameraX < 224)
        {
            cameraX += 2;
        }
        Camera.SetPosition(cameraX, 0);
    }
}
