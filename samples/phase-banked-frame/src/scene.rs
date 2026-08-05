import RetroSharp.Portable2D;

type Pixel = i16;

static class Level
{
    const i16 Width = 312;
    const i16 StreamY = 0;
    const i16 StreamHeight = 4;
    const i16 GoalX = 2000;
    const i16 Step = 2;
}

static class Player
{
    const i16 ScreenX = 72;
    const i16 FootY = 176;
    const i16 ProbeHeight = 8;
    const i16 AnimTicksPerFrame = 8;
    const i16 AnimFrames = 3;
}

enum CollisionFlag
{
    Solid = 1,
}

class Scene
{
    Pixel cameraX;
    Pixel footY;
    u8 animTick;
    u8 frame;
    bool flipX;
    u8 contact;
    u8 ceiling;
    u8 ledge;

    inline void Reset()
    {
        cameraX = 0;
        footY = Player.FootY;
        animTick = 0;
        frame = 0;
        flipX = false;
    }

    inline void ReadInput()
    {
        if (Input.IsDown(Button.Right) && cameraX < Level.GoalX)
        {
            cameraX += Level.Step;
            flipX = false;
        }

        if (Input.IsDown(Button.Left) && cameraX > 0)
        {
            cameraX -= Level.Step;
            flipX = true;
        }

        if (Input.IsDown(Button.Up) && footY > 32)
        {
            footY -= 1;
        }

        if (Input.IsDown(Button.Down) && footY < Player.FootY)
        {
            footY += 1;
        }
    }

    inline void Animate()
    {
        if (Input.IsDown(Button.B))
        {
            animTick += 2;
        }
        else
        {
            animTick += 1;
        }

        if (animTick >= Player.AnimTicksPerFrame * Player.AnimFrames)
        {
            animTick = 0;
        }

        if (animTick < Player.AnimTicksPerFrame)
        {
            frame = 0;
        }
        else
        {
            if (animTick < Player.AnimTicksPerFrame * 2)
            {
                frame = 1;
            }
            else
            {
                frame = 2;
            }
        }
    }

    inline void Probe()
    {
        contact = Camera.AabbTiles(Player.ScreenX, footY, Sprite.Width(player), Player.ProbeHeight, CollisionFlag.Solid);
        contact |= Camera.AabbTiles(Player.ScreenX + 8, footY, Sprite.Width(player), Player.ProbeHeight, CollisionFlag.Solid);
        ceiling = Camera.AabbTiles(Player.ScreenX, footY - 24, Sprite.Width(player), Player.ProbeHeight, CollisionFlag.Solid);
        ledge = Camera.AabbTiles(Player.ScreenX + 16, footY + 8, Sprite.Width(player), Player.ProbeHeight, CollisionFlag.Solid);
    }

    inline bool ReachedGoal() => cameraX >= Level.GoalX;
}

void SetupScene()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 0, 1, 3);
    Sprite.Asset(player, "../shared/platformer-assets/sprites/mario-player.png", 18, 32);
    Music.Asset(theme, "../shared/platformer-assets/music/runner.vgz");
    Audio.Init();
    Music.Play(theme);
    World.Load("../shared/platformer-assets/maps/stage1.tmx");
    Camera.Init(Level.Width, Level.StreamY, Level.StreamHeight);
}

void StepFrame(Scene scene)
{
    Video.WaitVBlank();
    Camera.Apply();
    u8 spriteFrame = scene.frame;
    bool spriteFlip = scene.flipX;
    Pixel spriteY = scene.footY - 32;
    Sprite.Draw(player, Player.ScreenX, spriteY, spriteFrame, spriteFlip, 0);
    Audio.Update();
    Input.Poll();
    scene.ReadInput();
    scene.Animate();
    scene.Probe();
    Camera.SetPosition(scene.cameraX, 0);
}

void CompleteLevel(Scene scene)
{
    Music.Stop();
    scene.contact = 0;
    scene.ceiling = 0;
    scene.ledge = 0;
    scene.frame = 0;
}
