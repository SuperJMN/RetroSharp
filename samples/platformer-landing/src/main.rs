import RetroSharp.Portable2D;

type Pixel = i16;

static class Level
{
    const i16 Width = 64;
    const i16 Height = 40;
}

static class Player
{
    const i16 StartX = 72;
    const i16 StartY = 273;
    const i16 FootOffset = 31;
    const i16 FallResetY = 336;
}

static class DeadZone
{
    const i16 Left = 64;
    const i16 Right = 96;
}

static class CollisionProbe
{
    const i16 LandingSearchTopOffset = 3;
    const i16 LandingSearchHeight = 9;
    const i16 WallProbeHeight = 8;
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

class PlayerState
{
    Pixel x;
    Pixel y;
    i8 velocityY;
    bool grounded;
    bool jumpActive;
    bool flipX;
    bool resetInputLatch;
    Pixel verticalSubpixel;
    Pixel supportProbeX;
    u8 jumpCount;
    u8 landingCount;
    u8 gameplayResetCount;
    u8 supportProbeCount;
    u8 wallContactCount;

    inline void Initialize(Pixel cameraX)
    {
        x = cameraX + Player.StartX;
        y = Player.StartY;
        velocityY = 0;
        grounded = true;
        jumpActive = false;
        flipX = false;
        resetInputLatch = false;
        verticalSubpixel = 0;
        supportProbeX = x - 8;
        jumpCount = 0;
        landingCount = 0;
        gameplayResetCount = 0;
        supportProbeCount = 0;
        wallContactCount = 0;
    }

    inline void ResetAfterFall(Pixel cameraX)
    {
        x = cameraX + Player.StartX;
        y = Player.StartY;
        velocityY = 0;
        grounded = true;
        jumpActive = false;
        flipX = false;
        resetInputLatch = true;
        verticalSubpixel = 0;
        supportProbeX = x - 8;
        gameplayResetCount += 1;
    }

    inline void ApplyGravity()
    {
        if (!grounded)
        {
            if (jumpActive && Input.IsDown(Button.A) && velocityY < Jump.HeldGravityThreshold)
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

            Pixel motion = verticalSubpixel + velocityY;
            while (motion < 0)
            {
                y -= 1;
                motion += Jump.Subpixel;
            }
            while (motion >= Jump.Subpixel)
            {
                y += 1;
                motion -= Jump.Subpixel;
            }
            verticalSubpixel = motion;
        }
    }

    inline void StartJump()
    {
        velocityY = Jump.TakeoffVelocity;
        grounded = false;
        jumpActive = true;
        verticalSubpixel = 0;
        jumpCount += 1;
    }

    inline void Land(Pixel targetY)
    {
        y = targetY;
        velocityY = 0;
        grounded = true;
        verticalSubpixel = 0;
        if (jumpActive)
        {
            landingCount += 1;
        }
        jumpActive = false;
    }

    inline pure bool SupportProbeDue()
    {
        return x >= supportProbeX + 8 || x + 8 <= supportProbeX;
    }
}

class CameraState
{
    Pixel x;
    Pixel y;

    inline pure Pixel ScreenX(PlayerState player) => player.x - x;
    inline pure Pixel ScreenY(PlayerState player) => player.y - y;

    inline void MoveRight(PlayerState player, Pixel wallProbeY)
    {
        let screenX = ScreenX(player);
        let probeX = screenX + 1;
        if (Camera.AabbTiles(probeX, wallProbeY, Sprite.Width(player_sprite), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0)
        {
            player.x += 1;
            if (screenX >= DeadZone.Right)
            {
                x += 1;
            }
        }
        else if (player.wallContactCount < 255)
        {
            player.wallContactCount += 1;
        }
        player.flipX = false;
    }

    inline void MoveLeft(PlayerState player, Pixel wallProbeY)
    {
        let screenX = ScreenX(player);
        let probeX = screenX - 1;
        if (Camera.AabbTiles(probeX, wallProbeY, Sprite.Width(player_sprite), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0)
        {
            player.x -= 1;
            if (screenX <= DeadZone.Left && x > 0)
            {
                x -= 1;
            }
        }
        else if (player.wallContactCount < 255)
        {
            player.wallContactCount += 1;
        }
        player.flipX = true;
    }
}

void ResolveLanding(PlayerState player, Pixel screenX, Pixel previousFootY, Pixel footY)
{
    if (player.velocityY >= 0)
    {
        i16 top = Camera.AabbHitTop(screenX, footY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(player_sprite), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);
        if (top >= 0 && player.supportProbeCount < 255)
        {
            player.supportProbeCount += 1;
        }
        if (top >= 0 && previousFootY <= top && footY >= top)
        {
            player.Land(top - Player.FootOffset);
        }
        else
        {
            player.grounded = false;
        }
    }
}

void ProbeGroundSupport(PlayerState player, Pixel screenX, Pixel footY)
{
    player.supportProbeX = player.x;
    i16 top = Camera.AabbHitTop(screenX, footY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(player_sprite), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);
    if (top >= 0)
    {
        if (player.supportProbeCount < 255)
        {
            player.supportProbeCount += 1;
        }
    }
    else
    {
        player.grounded = false;
    }
}

void Main()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 0, 1, 3);
    Sprite.Asset(player_sprite, "../runner/assets/mario-player.png", 18, 32);
    World.Load("assets/platformer-landing.tmj");
    Camera.Init(Level.Width, 0, Level.Height);

    CameraState view;
    PlayerState player;
    view.x = 0;
    view.y = Camera.VerticalScrollMax();
    player.Initialize(view.x);

    while (true)
    {
        Video.WaitVBlank();
        Camera.Apply();
        let displayX = view.ScreenX(player);
        let displayY = view.ScreenY(player);
        let displayFrame = player.grounded ? 0 : 4;
        Sprite.Draw(player_sprite, displayX, displayY, displayFrame, player.flipX, 0);
        Input.Poll();

        i16 previousFootY = player.y + Player.FootOffset;
        player.ApplyGravity();
        i16 footY = player.y + Player.FootOffset;
        let screenX = view.ScreenX(player);
        if (player.grounded)
        {
            if (player.SupportProbeDue())
            {
                ProbeGroundSupport(player, screenX, footY);
            }
        }
        else
        {
            ResolveLanding(player, screenX, previousFootY, footY);
        }

        if (player.y >= Player.FallResetY)
        {
            player.ResetAfterFall(view.x);
        }

        if (player.resetInputLatch)
        {
            if (!Input.IsDown(Button.Left) && !Input.IsDown(Button.Right) && !Input.IsDown(Button.A))
            {
                player.resetInputLatch = false;
            }
        }
        else if (Input.WasPressed(Button.A) && player.grounded)
        {
            player.StartJump();
        }

        i16 wallProbeY = player.y + Player.FootOffset - CollisionProbe.WallProbeHeight;
        if (!player.resetInputLatch && Input.IsDown(Button.Right))
        {
            view.MoveRight(player, wallProbeY);
        }
        if (!player.resetInputLatch && Input.IsDown(Button.Left))
        {
            view.MoveLeft(player, wallProbeY);
        }
        Camera.SetPosition(view.x, view.y);
    }
}
