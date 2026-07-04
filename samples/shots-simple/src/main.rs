import RetroSharp.Portable2D;

// Focused sample: prove that the hero projectile pool never shows more than 2
// shots on screen at once. A fixed-cadence timer stands in for a player mashing
// the B (fire) button much faster than a shot can leave the screen, so the
// pool cap - not the fire rate - is what limits the on-screen count.

static class Screen
{
    const i16 Width = 8;
    const i16 StreamY = 10;
    const i16 Height = 4;
}

static class Player
{
    const u8 X = 24;
    const u8 Y = 72;
    const u8 ShotX = 36;
    const u8 ShotY = 76;
}

static class Fire
{
    // Simulated B press cadence, in frames. Well below a shot's time on screen,
    // so without a cap the screen would fill up with shots.
    const u8 Interval = 10;

    // Travel direction passed to shots.Request(...): 0 = rightward (+X).
    const u8 Right = 0;
}

void SetupVideo()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 1, 2, 3);
    Sprite.Asset(player, "assets/player.json");
    Sprite.Asset(shot, "assets/shot.json");
}

// A blank world (tile 0 everywhere) keeps the background clean so the demo is
// only about the shots. The world just satisfies the camera/streaming runtime.
void DefineWorld()
{
    World.Column(0, 0, 0, 0, 0);
    World.Column(1, 0, 0, 0, 0);
    World.Column(2, 0, 0, 0, 0);
    World.Column(3, 0, 0, 0, 0);
    World.Column(4, 0, 0, 0, 0);
    World.Column(5, 0, 0, 0, 0);
    World.Column(6, 0, 0, 0, 0);
    World.Column(7, 0, 0, 0, 0);
    World.Map(Screen.Width, Screen.StreamY, Screen.Height);
}

void Main()
{
    SetupVideo();
    DefineWorld();
    Camera.Init(Screen.Width, Screen.StreamY, Screen.Height);

    Projectiles.Pool(shots, hero: 2, enemy: 1, requests: 2, offscreenMargin: 16);
    Projectiles.Def(Shot, team: Hero, sprite: shot, speedX: 2, speedY: 0, damage: 1, lifetime: 255, hitboxWidth: 8, hitboxHeight: 8);

    u8 fireTick = 0;
    u8 queued = 0;

    while (true)
    {
        // Present the frame simulated last tick.
        Video.WaitVBlank();
        Camera.Apply();
        Sprite.Draw(player, Player.X, Player.Y, 0, false, 0);
        shots.Draw();

        // Simulate a player pressing B on a fixed cadence.
        fireTick += 1;
        if (fireTick == Fire.Interval)
        {
            fireTick = 0;
            shots.Request(Shot, Player.ShotX, Player.ShotY, Fire.Right, queued);
        }

        shots.ProcessRequests();
        shots.Update();
    }
}
