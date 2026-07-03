import RetroSharp.Portable2D;

static class Mario
{
    const u8 X = 72;
    const u8 Y = 96;
    const u8 ShotX = 96;
    const u8 ShotY = 116;
}

void SetupVideo()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 0, 1, 3);
    Sprite.Asset(mario_player, "../runner/assets/mario-player.png", 18, 32);
    Sprite.Asset(mario_shot, "assets/mario-shot.json");
}

void Main()
{
    SetupVideo();
    Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
    Projectiles.Def(MarioShot, team: Hero, sprite: mario_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 48, hitboxWidth: 8, hitboxHeight: 8);

    u8 queued = 0;

    while (true)
    {
        Video.WaitVBlank();
        Sprite.Draw(mario_player, Mario.X, Mario.Y, 0, false, 0);
        shots.Draw();
        Input.Poll();

        if (Input.WasPressed(Button.B))
        {
            shots.Request(MarioShot, Mario.ShotX, Mario.ShotY, 0, queued);
        }

        shots.ProcessRequests();
        shots.Update();
    }
}
