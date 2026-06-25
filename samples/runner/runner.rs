type Pixel = i16;

enum World {
    Width = 68,
    StreamY = 9,
    Height = 14,
    SignedVelocityWrap = 128,
    PixelWidth = 544
}

enum Player {
    ScreenX = 72,
    LeftWallProbeX = 71,
    RightWallProbeX = 73,
    StartY = 105,
    WorldOriginY = 41,
    FallResetY = 116
}

enum CollisionProbe {
    LandingSearchTopOffset = 32,
    LandingSearchHeight = 40,
    WallProbeHeight = 8,
    NoTileHit = 255
}

enum Jump {
    Velocity = 253,
    BoostTicks = 12,
    GravityFrames = 2,
    BoostTickMask = 1
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
    Pixel gravityTick;

    inline void Reset() {
        y = Player.StartY;
        velocityY = 0;
        grounded = 1;
        displayFrame = 0;
        jumping = 0;
        jumpTicks = 0;
        gravityTick = 0;
    }

    inline void ApplyGravity() {
        gravityTick++;
        if (gravityTick >= Jump.GravityFrames) {
            gravityTick = 0;
            velocityY += 1;
        }
        if (velocityY != 0) {
            grounded = 0;
            y += velocityY;
        }
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
        gravityTick = 0;
    }

    inline void StartJump() {
        velocityY = Jump.Velocity;
        grounded = 0;
        jumping = 1;
        gravityTick = 0;
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
                    if ((jumpTicks & Jump.BoostTickMask) != 0) {
                        velocityY -= 1;
                    }
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

    inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY) {
        moving = 0;
        let wallProbeY = footWorldY - CollisionProbe.WallProbeHeight;
        if (button_down(right) != 0) {
            player.displayFlipX = false;
            if (camera.AabbTiles(Player.RightWallProbeX, wallProbeY, sprite_width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0) {
                moving = 1;
                x += 1;
            }
        }

        if (button_down(left) != 0) {
            player.displayFlipX = true;
            if (camera.AabbTiles(Player.LeftWallProbeX, wallProbeY, sprite_width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0) {
                moving = 1;
                x -= 1;
            }
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
        footTile = CollisionProbe.NoTileHit;
        resetRequested = 0;
    }

    inline void ResolveSolidLanding(PlayerState player, Pixel footWorldY) {
        if (player.velocityY < World.SignedVelocityWrap && player.velocityY != 0) {
            footTile = camera.AabbHitTop(Player.ScreenX, footWorldY - CollisionProbe.LandingSearchTopOffset, sprite_width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);
            if (footTile != CollisionProbe.NoTileHit) {
                player.Land(footTile + Player.WorldOriginY);
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
            footTile = CollisionProbe.NoTileHit;
            player.Reset();
        }
    }
}

inline void PresentFrame(PlayerState player) {
    video.WaitVBlank();
    audio.Update();
    camera.Apply();
    sprite.Draw(mario_player, Player.ScreenX, player.y, player.displayFrame, player.displayFlipX, 0);
}

void setup_video() {
    video.Init();
    palette.Background(0, 0, 1, 2, 3);
    palette.Sprite(0, 0, 0, 1, 3);
    sprite.Asset(mario_player, "assets/mario-player.png", 18, 32);
    animation.Clip(run, 1, 6, 6, 6);
    return;
}

void setup_audio() {
    music.Asset(runner_theme, "music/delight.gbapu");
    audio.Init();
    music.Play(runner_theme);
    return;
}

void load_world() {
    world.Load("maps/runner.tmj");
    return;
}

void main() {
    setup_video();
    setup_audio();
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

        let footWorldY = player.y - Player.WorldOriginY;

        frame.ResolveSolidLanding(player, footWorldY);
        frame.ResolveFall(player);
        frame.ResolveReset(player);
        player.HandleJumpInput();
        let movementFootWorldY = player.y - Player.WorldOriginY;
        view.HandleHorizontalInput(player, movementFootWorldY);
        player.UpdateRunAnimation(view);

    }
}
