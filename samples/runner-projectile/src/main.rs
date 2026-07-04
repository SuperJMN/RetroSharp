import RetroSharp.Portable2D;

static class Mario
{
    const u8 X = 72;
    const u8 Y = 96;
    const u8 ShotRightX = 96;
    const u8 ShotLeftX = 64;
    const u8 ShotY = 116;
}

// Projectile travel direction passed to shots.Request(...): 0 = rightward (+X),
// any non-zero value = leftward (-X).
static class Aim
{
    const u8 Right = 0;
    const u8 Left = 1;
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
    Effects.Pool(fx, capacity: 4, requests: 4);
    Effects.Def(MuzzleFlash, sprite: mario_shot, lifetime: 4);
    Projectiles.Pool(shots, hero: 2, enemy: 1, requests: 2, offscreenMargin: 16, effects: fx);
    Projectiles.Def(MarioShot, team: Hero, sprite: mario_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 48, hitboxWidth: 8, hitboxHeight: 8, spawnEffect: MuzzleFlash);

    u8 queuedRight = 0;
    u8 queuedLeft = 0;

    while (true)
    {
        Video.WaitVBlank();
        Sprite.Draw(mario_player, Mario.X, Mario.Y, 0, false, 0);
        shots.Draw();
        fx.Draw();
        Input.Poll();

        if (Input.WasPressed(Button.B))
        {
            shots.Request(MarioShot, Mario.ShotRightX, Mario.ShotY, Aim.Right, queuedRight);
        }

        if (Input.WasPressed(Button.A))
        {
            shots.Request(MarioShot, Mario.ShotLeftX, Mario.ShotY, Aim.Left, queuedLeft);
        }

        shots.ProcessRequests();
        fx.ProcessRequests();
        shots.Update();
        fx.Update();
    }
}
