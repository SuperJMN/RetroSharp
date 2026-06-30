type Pixel = i16;

enum World {
    Width = 48,
    StreamY = 0,
    Height = 96,
    StreamHeight = 96,
    SignedVelocityWrap = 128,
    PixelWidth = 384
}

enum Player {
    StartX = 72,
    StartY = 193,
    FootOffset = 31,
    FallResetY = 240,
    TopWrapY = 240
}

enum DeadZone {
    Left = 64,
    Right = 96,
    Top = 56,
    Bottom = 88
}

enum CameraBounds {
    MaxY = 196
}

enum CollisionProbe {
    LandingSearchTopOffset = 4,
    LandingSearchHeight = 12,
    LeftWallProbeOffset = 1,
    RightWallProbeOffset = 1,
    WallProbeHeight = 8,
    CeilingProbeTopOffset = 28,
    CeilingProbeHeight = 4,
    NoTileHit = 255
}

enum Jump {
    Velocity = 253,
    BoostTicks = 12,
    GravityFrames = 2,
    BoostTickMask = 1,
    BounceVelocity = 2
}

enum HorizontalMotion {
    None = 0,
    Right = 1,
    Left = 2,
    WalkSpeed = 8,
    RunMaxSpeed = 12,
    SubpixelScale = 8,
    RunAcceleration = 1,
    Friction = 2
}

enum RunAnimation {
    CycleTicks = 144
}

enum CollisionFlag { None = 0, Solid = 1 }

class PlayerState {
    Pixel x;
    Pixel y;
    Pixel velocityY;
    bool grounded;
    Pixel displayFrame;
    bool displayFlipX;
    Pixel animTick;
    bool jumping;
    Pixel jumpTicks;
    Pixel gravityTick;

    inline void Reset(CameraState view) {
        x = view.x + Player.StartX;
        y = view.y + Player.StartY;
        velocityY = 0;
        grounded = true;
        displayFrame = 0;
        jumping = false;
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
            grounded = false;
            y += velocityY;
        }
        if (velocityY >= World.SignedVelocityWrap) {
            if (y > Player.TopWrapY) {
                y = 0;
                velocityY = 0;
                jumping = false;
            }
        }
    }

    inline void Land(Pixel targetY) {
        y = targetY;
        velocityY = 0;
        grounded = true;
        jumping = false;
        gravityTick = 0;
    }

    inline void BounceDown() {
        velocityY = Jump.BounceVelocity;
        grounded = false;
        jumping = false;
        gravityTick = 0;
    }

    inline void StartJump() {
        velocityY = Jump.Velocity;
        grounded = false;
        jumping = true;
        gravityTick = 0;
    }

    inline void SelectDisplayFrame(bool moving) {
        displayFrame = grounded switch {
            false => 4,
            _ => moving switch {
                false => 0,
                _ => animation.Frame(run, animTick)
            }
        };
    }

    inline void HandleJumpInput() {
        if (Input.WasPressed(Button.A)) {
            if (grounded) {
                StartJump();
            }
        }

        if (jumping) {
            jumpTicks = Input.HoldTicks(Button.A);
            if (Input.IsDown(Button.A)) {
                if (jumpTicks < Jump.BoostTicks) {
                    if ((jumpTicks & Jump.BoostTickMask) != 0) {
                        velocityY -= 1;
                    }
                }
            }

            if (Input.WasReleased(Button.A)) {
                jumping = false;
            }
        }
    }

    inline void UpdateRunAnimation(CameraState view) {
        if (view.moving) {
            animTick += view.speed;
            if (animTick >= RunAnimation.CycleTicks) {
                animTick -= RunAnimation.CycleTicks;
            }
        } else {
            animTick = 0;
        }

        SelectDisplayFrame(view.moving);
    }
}

class CameraState {
    Pixel x;
    Pixel y;
    Pixel screenX;
    Pixel screenY;
    Pixel leftProbeX;
    Pixel rightProbeX;
    bool moving;
    Pixel speed;
    Pixel direction;
    Pixel movementRemainder;

    inline void ResetMotion() {
        moving = false;
        speed = 0;
        direction = HorizontalMotion.None;
        movementRemainder = 0;
    }

    inline pure Pixel ScreenX(PlayerState player) => player.x - x;
    inline pure Pixel ScreenY(PlayerState player) => player.y - y;

    inline void CaptureScreen(PlayerState player) {
        screenX = ScreenX(player);
        screenY = ScreenY(player);
    }

    inline void ApplyPosition() {
        camera.SetPosition(x, y);
    }

    inline void FollowPlayer(PlayerState player) {
        CaptureScreen(player);

        if (screenY > DeadZone.Bottom) {
            if (y < CameraBounds.MaxY) {
                y += 1;
            }
        } else if (screenY < DeadZone.Top) {
            if (y > 0) {
                y -= 1;
            }
        }
    }

    inline void StartDirection(Pixel desiredDirection) {
        direction = desiredDirection;
        speed = HorizontalMotion.WalkSpeed;
        movementRemainder = 0;
    }

    inline void AccelerateRun() {
        if (speed < HorizontalMotion.RunMaxSpeed) {
            speed += HorizontalMotion.RunAcceleration;
            if (speed > HorizontalMotion.RunMaxSpeed) {
                speed = HorizontalMotion.RunMaxSpeed;
            }
        }
    }

    inline void DecelerateToWalk() {
        if (speed > HorizontalMotion.WalkSpeed) {
            speed -= HorizontalMotion.Friction;
            if (speed < HorizontalMotion.WalkSpeed) {
                speed = HorizontalMotion.WalkSpeed;
            }
        }
    }

    inline void HoldDirection(bool grounded) {
        if (grounded) {
            if (Input.IsDown(Button.B)) {
                AccelerateRun();
            } else {
                DecelerateToWalk();
            }
        }
    }

    inline void ApplyFriction() {
        if (speed <= HorizontalMotion.Friction) {
            speed = 0;
            movementRemainder = 0;
            direction = HorizontalMotion.None;
        } else {
            speed -= HorizontalMotion.Friction;
        }
    }

    inline void UpdateIntent(Pixel desiredDirection, bool grounded) {
        if (desiredDirection == HorizontalMotion.None) {
            if (grounded) {
                ApplyFriction();
            }
        }
        if (desiredDirection == HorizontalMotion.Right) {
            if (direction == HorizontalMotion.Right) {
                HoldDirection(grounded);
            } else {
                StartDirection(HorizontalMotion.Right);
            }
        }
        if (desiredDirection == HorizontalMotion.Left) {
            if (direction == HorizontalMotion.Left) {
                HoldDirection(grounded);
            } else {
                StartDirection(HorizontalMotion.Left);
            }
        }
    }

    inline void UpdateFacing(PlayerState player) {
        if (direction == HorizontalMotion.Right) {
            player.displayFlipX = false;
        }
        if (direction == HorizontalMotion.Left) {
            player.displayFlipX = true;
        }
    }

    inline void MoveRightOnePixel(PlayerState player, Pixel wallProbeY) {
        CaptureScreen(player);
        rightProbeX = screenX + CollisionProbe.RightWallProbeOffset;
        if (camera.AabbTiles(rightProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0) {
            moving = true;
            player.x += 1;
            if (screenX >= DeadZone.Right) {
                x += 1;
            }
        } else {
            ResetMotion();
        }
    }

    inline void MoveLeftOnePixel(PlayerState player, Pixel wallProbeY) {
        CaptureScreen(player);
        leftProbeX = screenX - CollisionProbe.LeftWallProbeOffset;
        if (camera.AabbTiles(leftProbeX, wallProbeY, Sprite.Width(mario_player), CollisionProbe.WallProbeHeight, CollisionFlag.Solid) == 0) {
            moving = true;
            player.x -= 1;
            if (screenX <= DeadZone.Left) {
                if (x > 0) {
                    x -= 1;
                }
            }
        } else {
            ResetMotion();
        }
    }

    inline void ApplyMotionStep(PlayerState player, Pixel wallProbeY) {
        if (movementRemainder >= HorizontalMotion.SubpixelScale) {
            movementRemainder -= HorizontalMotion.SubpixelScale;
            if (direction == HorizontalMotion.Right) {
                MoveRightOnePixel(player, wallProbeY);
            }
            if (direction == HorizontalMotion.Left) {
                MoveLeftOnePixel(player, wallProbeY);
            }
        }
    }

    inline void ApplyMotion(PlayerState player, Pixel wallProbeY) {
        moving = false;
        if (speed != 0) {
            movementRemainder += speed;
            ApplyMotionStep(player, wallProbeY);
            ApplyMotionStep(player, wallProbeY);
        }
    }

    inline void HandleHorizontalInput(PlayerState player, Pixel footWorldY) {
        let wallProbeY = footWorldY - CollisionProbe.WallProbeHeight;
        Pixel desiredDirection = HorizontalMotion.None;
        if (Input.IsDown(Button.Right)) {
            desiredDirection = HorizontalMotion.Right;
        }

        if (Input.IsDown(Button.Left)) {
            desiredDirection = HorizontalMotion.Left;
        }

        UpdateIntent(desiredDirection, player.grounded);
        UpdateFacing(player);
        ApplyMotion(player, wallProbeY);
    }
}

class FrameState {
    Pixel footTile;
    bool resetRequested;

    inline void Begin() {
        footTile = CollisionProbe.NoTileHit;
        resetRequested = false;
    }

    inline void ResolveSolidLanding(PlayerState player, Pixel screenX, Pixel footWorldY) {
        if (player.velocityY < World.SignedVelocityWrap && player.velocityY != 0) {
            footTile = camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);
            if (footTile != CollisionProbe.NoTileHit) {
                player.Land(footTile - Player.FootOffset);
            }
        }
    }

    inline void ResolveFall(PlayerState player) {
        if (!player.grounded) {
            if (player.y >= Player.FallResetY) {
                resetRequested = true;
            }
        }
    }

    inline void ResolveCeilingHit(PlayerState player, Pixel screenX, Pixel footWorldY) {
        if (player.velocityY >= World.SignedVelocityWrap) {
            let headProbeY = footWorldY - CollisionProbe.CeilingProbeTopOffset;
            if (camera.AabbTiles(screenX, headProbeY, Sprite.Width(mario_player), CollisionProbe.CeilingProbeHeight, CollisionFlag.Solid) != 0) {
                player.BounceDown();
            }
        }
    }

    inline void ResolveReset(PlayerState player, CameraState view) {
        if (resetRequested) {
            footTile = CollisionProbe.NoTileHit;
            player.Reset(view);
            view.ResetMotion();
        }
    }
}

inline void PresentFrame(PlayerState player, CameraState view) {
    view.CaptureScreen(player);
    video.WaitVBlank();
    sprite.Draw(mario_player, view.screenX, view.screenY, player.displayFrame, player.displayFlipX, 0);
}

void setup_video() {
    video.Init();
    palette.Background(0, 0, 1, 2, 3);
    palette.Sprite(0, 0, 0, 1, 3);
    sprite.Asset(mario_player, "assets/mario-player.png", 18, 32);
    animation.Clip(run, 1, 48, 48, 48);
    sprite.Asset(goomba, "assets/goomba.png", 16, 16);
    animation.Clip(goomba_walk, 0, 16, 16);
    return;
}

void setup_audio() {
    music.Asset(runner_theme, "music/runner.vgz");
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
    camera.Init(World.Width, World.StreamY, World.StreamHeight);
    PlayerState player;
    CameraState view;
    FrameState frame;
    u8 goombaTick = 0;
    view.ResetMotion();
    player.Reset(view);

    actor.Pool(goombas, 1);
    enemy.Def(Goomba, sprite: goomba, behavior: Patrol, animation: goomba_walk, speed: 1, cooldown: 96, hitboxWidth: 16, hitboxHeight: 16);

    loop {
        PresentFrame(player, view);
        camera.Apply();
        goombas.Draw();
        audio.Update();
        input.Poll();

        actor.SpawnLayer(goombas, "maps/runner.tmj", "actors");
        goombaTick ^= 1;
        if (goombaTick == 0) {
            goombas.Update();
        }

        frame.Begin();
        player.ApplyGravity();

        let footWorldY = player.y + Player.FootOffset;
        view.CaptureScreen(player);

        frame.ResolveSolidLanding(player, view.screenX, footWorldY);
        frame.ResolveCeilingHit(player, view.screenX, footWorldY);
        frame.ResolveFall(player);
        frame.ResolveReset(player, view);
        view.FollowPlayer(player);
        player.HandleJumpInput();
        let movementFootWorldY = player.y + Player.FootOffset;
        view.HandleHorizontalInput(player, movementFootWorldY);
        view.ApplyPosition();
        view.ApplyPosition();
        player.UpdateRunAnimation(view);

    }
}
