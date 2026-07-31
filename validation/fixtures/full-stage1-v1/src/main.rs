import RetroSharp.Portable2D;

void Main()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    World.Load("assets/stage1.tmx");
    Camera.Init(156, 0, 20);

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
    }
}
