import RetroSharp.Portable2D;

// Focused sample: same 2-shot hero pool cap as shots-simple, but with bouncing
// projectiles. Each shot is a GravityArc that bounces off a solid floor while it
// travels right. A fixed-cadence timer stands in for a player mashing the B
// (fire) button, and the hero: 2 pool proves that only two bouncing shots are
// ever live at once no matter how fast the button is pressed.

static class Level
{
    const i16 Width = 20;
    const i16 StreamY = 10;
    const i16 Height = 18;
}

static class Player
{
    const u8 X = 16;
    const u8 Y = 120;
    const u8 ShotX = 32;
    const u8 ShotY = 96;
}

static class Fire
{
    // Simulated B press cadence, in frames. Well below a shot's time on screen,
    // so without a cap the screen would fill up with shots.
    const u8 Interval = 12;

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

// A solid floor along the bottom two rows: the tile ids draw a visible floor and
// the matching World.Flags mark those cells Solid (1) so shots can bounce off it.
void DefineWorld()
{
    World.Column(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(13, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(14, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(18, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);
    World.Column(19, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 5);

    World.Flags(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(13, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(14, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(17, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(18, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Flags(19, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1);
    World.Map(Level.Width, Level.StreamY, Level.Height);
}

void Main()
{
    SetupVideo();
    DefineWorld();
    Camera.Init(Level.Width, Level.StreamY, Level.Height);

    Projectiles.Pool(shots, hero: 2, enemy: 1, requests: 2, offscreenMargin: 16);
    Projectiles.Def(Shot, team: Hero, sprite: shot, speedX: 2, speedY: 0, damage: 1, lifetime: 255, hitboxWidth: 8, hitboxHeight: 8, behavior: GravityArc, tileCollision: Bounce, bounceSpeedY: 6);

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
        shots.TouchTiles(0, 1);
    }
}
