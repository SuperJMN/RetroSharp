using Runner.Camera;
using Runner.Frame;
using Runner.Level;
using Runner.Player;

void SetupVideo()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 0, 1, 3);
    Sprite.Asset(mario_player, "assets/mario-player.png", 18, 32);
    Animation.Clip(run, 1, 48, 48, 48);
    Sprite.Asset(goomba, "assets/goomba.png", 16, 16);
    Animation.Clip(goomba_walk, 0, 16, 16);
}

void SetupAudio()
{
    Music.Asset(runner_theme, "assets/music/runner.vgz");
    Sfx.Asset(jump_sfx, "assets/sfx/smb-jump.vgm");
    Sfx.Asset(squish_sfx, "assets/sfx/squish.vgm");
    Audio.Init();
    Music.Play(runner_theme);
}

void LoadWorld()
{
    World.Load("assets/maps/stage1.tmx");
}

void Main()
{
    SetupVideo();
    SetupAudio();
    LoadWorld();
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);
    Actors.Pool(enemies, 1);
    Enemies.Def(Goomba, sprite: goomba, behavior: Patrol, animation: goomba_walk, speed: 1, hp: 1, cooldown: 255, hitboxWidth: 16, hitboxHeight: 16, defeatedFrame: 2);

    PlayerState player;
    CameraState view;
    FrameState frame;
    bool goombaAdvance;

    view.ResetMotion();
    view.y = Camera.VerticalScrollMax();
    player.x = Player.StartX;
    player.Land(Player.StartY);

    while (true)
    {
        // Commit the prepared camera edge at the start of VBlank, then present
        // the frame simulated last tick before servicing audio and input.
        u8 playerScreenX = (u8)view.ScreenX(player);
        u8 playerScreenY = (u8)view.ScreenY(player);
        Video.WaitVBlank();
        Camera.Apply();
        PresentFrame(player, playerScreenX, playerScreenY);
        if (enemies[0].health != 0)
        {
            enemies[0].state = 0;
        }
        enemies.DrawAndTouchPlayerTop(playerScreenX, playerScreenY, 18, 32, 8);
        Audio.Update();
        Input.Poll();

        if (enemies[0].health != 0 && player.velocityY > 0 && enemies[0].state != 0)
        {
            enemies[0].health = 0;
            Sfx.Play(squish_sfx);
            player.BounceAfterStomp(Input.IsDown(Button.A));
        }
        SimulatePlayer(player, view, frame);
        Actors.SpawnWindow(enemies, "assets/maps/stage1.tmx", "actors", 0, 192);
        goombaAdvance = !goombaAdvance;
        if (goombaAdvance)
        {
            if (enemies[0].health == 0)
            {
                if (enemies[0].active != 0)
                {
                    enemies[0].state += 1;
                    if (enemies[0].state == GoombaDeath.SquashedUpdates)
                    {
                        enemies[0].active = 0;
                    }
                }
            }
            else
            {
                enemies.Update();
            }
        }
    }
}

inline void SimulatePlayer(PlayerState player, CameraState view, FrameState frame)
{
    if (frame.IsRespawning())
    {
        frame.AdvanceRespawn(player, view);
    }
    else
    {
        i16 previousFootWorldY = player.y + Player.FootOffset;
        player.ApplyGravity();

        i16 footWorldY = player.y + Player.FootOffset;
        let screenX = view.ScreenX(player);

        frame.ResolveLanding(player, screenX, previousFootWorldY, footWorldY);
        frame.ResolveCeilingHit(player, screenX, footWorldY);
        view.FollowPlayer(player);
        player.HandleJumpInput(view.speed);

        i16 movementFootWorldY = player.y + Player.FootOffset;
        view.HandleHorizontalInput(player, movementFootWorldY);
        player.UpdateRunAnimation(view);
        frame.ResolveFall(player, view);
    }
    view.ApplyPosition();
}
