import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 96;
    const i16 StreamY = 0;
    const i16 StreamHeight = 2;
}

void Main()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    World.Load("assets/spawns-1.tmx");
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);
    Actors.Pool(enemies, 1);
    Enemies.Def(Goomba, behavior: Walker, speed: 1, hp: 1, cooldown: 255, hitboxWidth: 16, hitboxHeight: 16);

    i16 cameraX = 0;
    Camera.SetPosition(cameraX, 0);

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
        Input.Poll();
        Actors.SpawnWindow(enemies, "assets/spawns-1.tmx", "actors", 0, 192);
        enemies.Update();
        cameraX += 1;
        Camera.SetPosition(cameraX, 0);
    }
}
