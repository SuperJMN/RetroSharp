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
    Audio.Init();
    Music.Play(runner_theme);
}

void LoadWorld()
{
    World.Load("assets/maps/runner.tmj");
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
    u8 goombaTick = 0;

    view.ResetMotion();
    player.Reset(view);

    Actors.Pool(goombas, 1);
    Enemies.Def(Goomba, sprite: goomba, behavior: Patrol, animation: goomba_walk, speed: 1, cooldown: 96, hitboxWidth: 16, hitboxHeight: 16);

    while (true)
    {
        // Present the frame simulated last tick, then service audio and latch input.
        PresentFrame(player, view);
        Camera.Apply();
        goombas.Draw();
        Audio.Update();
        Input.Poll();

        // Enemies step at half the player's tick rate.
        Actors.SpawnLayer(goombas, "assets/maps/runner.tmj", "actors");
        goombaTick ^= 1;
        if (goombaTick == 0)
        {
            goombas.Update();
        }

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

