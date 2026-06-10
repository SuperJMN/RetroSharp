type Pixel = i16;

enum World {
    Width = 32,
    StreamY = 9,
    Height = 14,
    SignedVelocityWrap = 128,
    PixelWidth = 256
}

enum Player {
    ScreenX = 72,
    StartY = 105,
    WorldOriginY = 41,
    FallResetY = 116
}

enum CollisionProbe {
    LandingHeight = 8,
    TileMask = 248
}

enum Jump {
    Velocity = 252,
    BoostTicks = 12
}

enum CollisionFlag { None = 0, Solid = 1 }

class PlayerState {
    Pixel y;
    Pixel velocityY;
    Pixel grounded;
    Pixel displayFrame;
    bool displayFlipX;
    Pixel animTick;
    Pixel jumping;
    Pixel jumpTicks;

    inline void Reset() {
        y = Player.StartY;
        velocityY = 0;
        grounded = 1;
        displayFrame = 0;
        jumping = 0;
        jumpTicks = 0;
    }

    inline void ApplyGravity() {
        grounded = 0;
        velocityY += 1;
        y += velocityY;
        if (velocityY >= World.SignedVelocityWrap) {
            if (y >= World.SignedVelocityWrap) {
                y = 0;
                velocityY = 0;
                jumping = 0;
            }
        }
    }

    inline void Land(Pixel targetY) {
        y = targetY;
        velocityY = 0;
        grounded = 1;
        jumping = 0;
    }

    inline void StartJump() {
        velocityY = Jump.Velocity;
        grounded = 0;
        jumping = 1;
    }

    inline void SelectDisplayFrame(Pixel moving) {
        displayFrame = grounded switch {
            0 => 4,
            _ => moving switch {
                0 => 0,
                _ => animation.Frame(run, animTick)
            }
        };
    }

    inline void HandleJumpInput() {
        if (button_just_pressed(a) != 0) {
            if (grounded != 0) {
                StartJump();
            }
        }

        if (jumping != 0) {
            jumpTicks = button_hold_ticks(a);
            if (button_down(a) != 0) {
                if (jumpTicks < Jump.BoostTicks) {
                    velocityY -= 1;
                }
            }

            if (button_just_released(a) != 0) {
                jumping = 0;
            }
        }
    }

    inline void UpdateRunAnimation(CameraState view) {
        if (view.moving != 0) {
            animTick++;
        } else {
            animTick = 0;
        }

        SelectDisplayFrame(view.moving);
    }
}

class CameraState {
    Pixel x;
    Pixel moving;

    inline void HandleHorizontalInput(PlayerState player) {
        moving = 0;
        if (button_down(right) != 0) {
            moving = 1;
            player.displayFlipX = false;
            x += 1;
        }

        if (button_down(left) != 0) {
            moving = 1;
            player.displayFlipX = true;
            x -= 1;
        }

        if (moving != 0) {
            camera.SetPosition(x, 0);
        }
    }
}

class FrameState {
    Pixel footTile;
    Pixel resetRequested;

    inline void Begin() {
        footTile = 0;
        resetRequested = 0;
    }

    inline void ResolveSolidLanding(PlayerState player, Pixel playerWorldX, Pixel footWorldY) {
        if (player.velocityY < World.SignedVelocityWrap && player.velocityY != 0) {
            footTile = collision_aabb_tiles(playerWorldX, footWorldY, sprite_width(mario_player), CollisionProbe.LandingHeight, CollisionFlag.Solid);
            if (footTile != 0) {
                let landedWorldY = footWorldY & CollisionProbe.TileMask;
                player.Land(landedWorldY + Player.WorldOriginY);
            }
        }
    }

    inline void ResolveFall(PlayerState player) {
        if (player.grounded == 0) {
            if (player.y >= Player.FallResetY) {
                resetRequested = 1;
            }
        }
    }

    inline void ResolveReset(PlayerState player) {
        if (resetRequested != 0) {
            footTile = 0;
            player.Reset();
        }
    }
}

inline pure Pixel WrapWorldX(Pixel x) => x;

inline void PresentFrame(PlayerState player) {
    video.WaitVBlank();
    camera.Apply();
    sprite.Draw(mario_player, Player.ScreenX, player.y, player.displayFrame, player.displayFlipX, 0);
}

void setup_video() {
    video.Init();
    palette.Set(0, 0);
    palette.Set(1, 1);
    palette.Set(2, 2);
    palette.Set(3, 3);
    objectPalette.Set(0, 0);
    objectPalette.Set(1, 0);
    objectPalette.Set(2, 1);
    objectPalette.Set(3, 3);
    sprite.Asset(mario_player, "assets/mario-player.gb.png", 18, 32);
    animation.Clip(run, 1, 6, 6, 6);
    return;
}

void load_world() {
    world.Load("maps/runner.tmj");
    return;
}

void main() {
    setup_video();
    load_world();
    camera.Init(World.Width, World.StreamY, World.Height);
    PlayerState player;
    CameraState view;
    FrameState frame;
    player.Reset();

    loop {
        PresentFrame(player);
        input.Poll();

        frame.Begin();
        player.ApplyGravity();

        let playerWorldX = (view.x + Player.ScreenX) |> WrapWorldX();
        let footWorldY = player.y - Player.WorldOriginY;

        frame.ResolveSolidLanding(player, playerWorldX, footWorldY);
        frame.ResolveFall(player);
        frame.ResolveReset(player);
        player.HandleJumpInput();
        view.HandleHorizontalInput(player);
        player.UpdateRunAnimation(view);

    }
}
