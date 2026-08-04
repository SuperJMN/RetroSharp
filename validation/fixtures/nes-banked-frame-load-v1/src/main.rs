import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 80;
    const i16 StreamY = 0;
    const i16 StreamHeight = 4;
}

void Main()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 0, 1, 3);
    Sprite.Asset(player, "../../../samples/shared/platformer-assets/sprites/mario-player.png", 18, 32);
    Sprite.Asset(goomba, "../../../samples/shared/platformer-assets/sprites/goomba.png", 16, 16);
    Animation.Clip(goomba_walk, 0, 16, 16);
    Music.Asset(theme, "../../../samples/shared/platformer-assets/music/runner.vgz");
    Audio.Init();
    Music.Play(theme);
    World.Load("assets/world.tmx");
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);
    Actors.Pool(enemies, 1);
    Enemies.Def(Goomba, sprite: goomba, behavior: Patrol, animation: goomba_walk, speed: 1, hp: 1, cooldown: 255, hitboxWidth: 16, hitboxHeight: 16);

    i16 cameraX = 0;
    u8 collisionWork;
    bool actorAdvance;
    Camera.SetPosition(cameraX, 0);

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
        Sprite.Draw(player, 72, 0, 0, false, 0);
        enemies.Draw();
        Audio.Update();
        Input.Poll();

        if (Input.IsDown(Button.Right) && Input.IsDown(Button.B) && cameraX < 224)
        {
            cameraX += 2;
        }

        Actors.SpawnLayer(enemies, "assets/world.tmx", "actors");
        actorAdvance = !actorAdvance;
        if (actorAdvance)
        {
            enemies.Update();
            enemies.TouchTiles(0, 1);
            enemies.LandOnTiles(8, 16, 1);
        }

        collisionWork = Camera.AabbTiles(56, 0, Sprite.Width(player), 8, 1);
        collisionWork |= Camera.AabbTiles(88, 0, Sprite.Width(player), 8, 1);
        Camera.SetPosition(cameraX, 0);
    }
}
