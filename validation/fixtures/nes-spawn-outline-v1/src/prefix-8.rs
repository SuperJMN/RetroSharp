import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 256;
    const i16 StreamY = 0;
    const i16 StreamHeight = 40;
}

void Main()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Sprite.Asset(goomba, "../../../samples/shared/platformer-assets/sprites/goomba.nes.png", 16, 16);
    World.Load("assets/prefix-spawns-8.tmx");
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);
    Actors.Pool(enemies, 1);
    Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1, hp: 1, cooldown: 255, hitboxWidth: 16, hitboxHeight: 16);

    i16 cameraX = 0;
    Camera.SetPosition(cameraX, 0);

    while (true)
    {
        Video.WaitVBlank();
        Actors.SpawnWindow(enemies, "assets/prefix-spawns-8.tmx", "actors", 0, 192);
        Camera.Apply();
        Input.Poll();
        enemies.Update();
        enemies.Draw();
        cameraX += 1;
        Camera.SetPosition(cameraX, 0);
    }
}
