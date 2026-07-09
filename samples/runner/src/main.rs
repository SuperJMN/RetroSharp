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
    World.Load("assets/maps/stage1.playable.tmj");
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

    view.ResetMotion();
    view.y = Camera.VerticalScrollMax();
    player.Reset(view);

    while (true)
    {
        // Present the frame simulated last tick, then service audio and latch input.
        PresentFrame(player, view);
        Camera.Apply();
        Audio.Update();
        Input.Poll();

        SimulatePlayer(player, view, frame);
    }
}

inline void SimulatePlayer(PlayerState player, CameraState view, FrameState frame)
{
    frame.Begin();
    player.ApplyGravity();

    let footWorldY = player.y + Player.FootOffset;
    let screenX = view.ScreenX(player);

    frame.ResolveSolidLanding(player, screenX, footWorldY);
    frame.ResolveCeilingHit(player, screenX, footWorldY);
    frame.ResolveFall(player);
    frame.ResolveReset(player, view);

    view.FollowPlayer(player);
    player.HandleJumpInput();

    let movementFootWorldY = player.y + Player.FootOffset;
    view.HandleHorizontalInput(player, movementFootWorldY);
    view.ApplyPosition();
    player.UpdateRunAnimation(view);
}
