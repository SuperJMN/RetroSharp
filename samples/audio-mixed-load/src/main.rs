import RetroSharp.Portable2D;

static class Level
{
    const i16 Width = 64;
    const i16 Height = 20;
}

static class Player
{
    const i16 StartX = 72;
    const i16 GroundY = 113;
    const i16 FootOffset = 31;
}

static class Jump
{
    const i8 TakeoffVelocity = -56;
    const i8 HeldGravityThreshold = -32;
    const i8 HeldGravity = 1;
    const i8 ReleasedGravity = 5;
    const i8 TerminalVelocity = 69;
    const i16 Subpixel = 16;
}

enum CollisionFlag
{
    Solid = 1,
}

void Main()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 0, 1, 3);
    Sprite.Asset(player_sprite, "../runner/assets/mario-player.png", 18, 32);
    World.Load("../tiled-hscroll/stage1-short.tmj");
    Music.Asset(load_theme, "../runner/assets/music/runner.vgz");
    Sfx.Asset(load_sfx, "../runner/assets/sfx/smb-jump.vgm");
    Audio.Init();
    Music.Play(load_theme);
    Camera.Init(Level.Width, 0, Level.Height);

    i16 playerX = Player.StartX;
    i16 playerY = Player.GroundY;
    i16 cameraX = 0;
    i16 cameraY = Camera.VerticalScrollMax();
    i16 verticalSubpixel = 0;
    i8 velocityY = 0;
    bool grounded = true;
    bool patrolLeft = false;
    u8 patrolX = 144;
    u8 gameplayTick = 0;
    u8 audioTick = 0;
    u8 sfxCount = 0;
    u8 collisionProbeCount = 0;
    u8 collisionCadence = 0;
    i16 retainedPlayerScreenX = playerX - cameraX;
    i16 retainedPlayerScreenY = playerY - cameraY;
    u8 retainedPlayerFrame = 0;
    u8 retainedPatrolScreenX = patrolX;
    u8 retainedPatrolFrame = 1;
    bool retainedPatrolLeft = patrolLeft;

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
        let publishedPlayerScreenX = retainedPlayerScreenX;
        let publishedPlayerScreenY = retainedPlayerScreenY;
        let publishedPlayerFrame = retainedPlayerFrame;
        let publishedPatrolScreenX = retainedPatrolScreenX;
        let publishedPatrolFrame = retainedPatrolFrame;
        let publishedPatrolLeft = retainedPatrolLeft;
        let playerScreenX = playerX - cameraX;
        let playerScreenY = playerY - cameraY;
        let playerFrame = grounded ? 0 : 4;
        let patrolScreenX = patrolX;
        let patrolFrame = 1;
        Sprite.Draw(player_sprite, playerScreenX, playerScreenY, playerFrame, false, 0);
        Sprite.Draw(player_sprite, patrolScreenX, 96, patrolFrame, patrolLeft, 0);
        retainedPlayerScreenX = playerScreenX;
        retainedPlayerScreenY = playerScreenY;
        retainedPlayerFrame = playerFrame;
        retainedPatrolScreenX = patrolScreenX;
        retainedPatrolFrame = patrolFrame;
        retainedPatrolLeft = patrolLeft;
        Audio.Update();
        audioTick += 1;
        Input.Poll();
        gameplayTick += 1;

        if (Input.WasPressed(Button.A))
        {
            Sfx.Play(load_sfx);
            sfxCount += 1;
            if (grounded)
            {
                velocityY = Jump.TakeoffVelocity;
                verticalSubpixel = 0;
                grounded = false;
            }
        }

        if (!grounded)
        {
            if (Input.IsDown(Button.A) && velocityY < Jump.HeldGravityThreshold)
            {
                velocityY += Jump.HeldGravity;
            }
            else
            {
                velocityY += Jump.ReleasedGravity;
                if (velocityY > Jump.TerminalVelocity)
                {
                    velocityY = Jump.TerminalVelocity;
                }
            }

            i16 motion = verticalSubpixel + velocityY;
            while (motion < 0)
            {
                playerY -= 1;
                motion += Jump.Subpixel;
            }
            while (motion >= Jump.Subpixel)
            {
                playerY += 1;
                motion -= Jump.Subpixel;
            }
            verticalSubpixel = motion;
            if (playerY >= Player.GroundY)
            {
                playerY = Player.GroundY;
                verticalSubpixel = 0;
                velocityY = 0;
                grounded = true;
            }
        }

        collisionCadence += 1;
        if (collisionCadence >= 16)
        {
            collisionCadence = 0;
            i16 footProbeY = playerY + Player.FootOffset - 3;
            i16 wallProbeY = playerY + Player.FootOffset - 8;
            u8 collisionScreenX = (u8)playerScreenX;
            u8 wallScreenX = collisionScreenX + 1;
            let feet = Camera.AabbTiles(collisionScreenX, footProbeY, Sprite.Width(player_sprite), 9, CollisionFlag.Solid);
            let wall = Camera.AabbTiles(wallScreenX, wallProbeY, Sprite.Width(player_sprite), 8, CollisionFlag.Solid);
            if ((feet | wall) != 0 && collisionProbeCount < 255)
            {
                collisionProbeCount += 1;
            }
        }

        if (Input.IsDown(Button.Right))
        {
            playerX += 1;
            if (playerScreenX >= 96)
            {
                cameraX += 1;
            }
        }

        if (patrolLeft)
        {
            patrolX -= 1;
            if (patrolX <= 112)
            {
                patrolLeft = false;
            }
        }
        else
        {
            patrolX += 1;
            if (patrolX >= 184)
            {
                patrolLeft = true;
            }
        }

        Camera.SetPosition(cameraX, cameraY);
    }
}
