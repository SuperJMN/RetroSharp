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
}

void SetupAudio()
{
    Music.Asset(runner_theme, "assets/music/runner.vgz");
    Sfx.Asset(jump_sfx, "assets/sfx/smb-jump.vgm");
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

    PlayerState player;
    CameraState view;
    FrameState frame;
    u8 goombaX = 176;
    u8 goombaY = Viewport.Height - 32;
    u8 goombaFrame = 0;
    bool goombaAdvance;

    view.ResetMotion();
    view.y = Camera.VerticalScrollMax();
    player.x = Player.StartX;
    player.Land(Player.StartY);

    while (true)
    {
        // Commit the prepared camera edge at the start of VBlank, then present
        // the frame simulated last tick before servicing audio and input.
        Video.WaitVBlank();
        Camera.Apply();
        PresentFrame(player, view);
        Sprite.Draw(goomba, goombaX, goombaY, goombaFrame, false, 0);
        Audio.Update();
        Input.Poll();

        SimulatePlayer(player, view, frame);
        goombaAdvance = !goombaAdvance;
        if (goombaAdvance)
        {
            if (goombaX == 0)
            {
                goombaY = Viewport.Height;
            }
            else
            {
                goombaX -= 1;
                if ((goombaX & 15) == 0)
                {
                    goombaFrame ^= 1;
                }
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
