type Pixel = i16;

enum World {
    Width = 16,
    StreamY = 9,
    Height = 6,
    PixelWrap = 128
}

enum Player {
    ScreenX = 72,
    StartY = 73,
    PlatformY = 41,
    GroundY = 73,
    EnemyHitY = 72,
    FallResetY = 116
}

enum CollisionProbe {
    PlatformStartY = 42,
    PlatformEndY = 59,
    GroundY = 32
}

enum Jump {
    Velocity = 252,
    HazardBounceVelocity = 248,
    BoostTicks = 12
}

enum Enemy {
    StartX = 144,
    GroundY = 89,
    PlatformX = 40,
    PlatformY = 57,
    WrapAtX = 96,
    RespawnX = 152,
    HitStartX = 68,
    HitEndX = 91
}

enum CollisionFlag { None = 0, Solid = 1, Hazard = 2 }

class PlayerState {
    Pixel y;
    Pixel velocityY;
    Pixel grounded;
    Pixel displayFrame;
    bool displayFlipX;
    Pixel animTick;
    Pixel jumping;
    Pixel jumpTicks;
    Pixel hitFlashTicks;

    inline void Reset() {
        y = Player.StartY;
        velocityY = 0;
        grounded = 1;
        displayFrame = 0;
        jumping = 0;
        jumpTicks = 0;
        hitFlashTicks = 0;
    }

    inline void ApplyGravity() {
        grounded = 0;
        velocityY += 1;
        y += velocityY;
        if (velocityY >= World.PixelWrap) {
            if (y >= World.PixelWrap) {
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

    inline void BounceFromHazard() {
        velocityY = Jump.HazardBounceVelocity;
        grounded = 0;
        displayFrame = 4;
        hitFlashTicks = 12;
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

        if (hitFlashTicks != 0) {
            hitFlashTicks--;
            displayFrame = 4;
        } else {
            SelectDisplayFrame(view.moving);
        }
    }
}

class EnemyState {
    Pixel x;
    Pixel frame;
    Pixel tick;

    inline void Spawn() {
        x = Enemy.StartX;
        frame = 0;
        tick = 0;
    }

    inline void Step() {
        x--;
        if (x <= Enemy.WrapAtX) {
            x = Enemy.RespawnX;
        }

        tick++;
        frame = animation.Frame(enemy_walk, tick);
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
    Pixel failTile;
    Pixel hazardHit;
    Pixel impactHit;
    Pixel resetRequested;

    inline void Begin() {
        footTile = 0;
        failTile = 0;
        hazardHit = 0;
        impactHit = 0;
        resetRequested = 0;
    }

    inline void ResolvePlatformLanding(PlayerState player, Pixel footLeftX, Pixel footCenterX, Pixel footRightX) {
        if (player.y in CollisionProbe.PlatformStartY..CollisionProbe.PlatformEndY && player.velocityY < World.PixelWrap && player.velocityY != 0) {
            footTile = collision_aabb_tiles(footLeftX, 0, 1, 8, CollisionFlag.Solid);
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footCenterX, 0, 1, 8, CollisionFlag.Solid);
            }
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footRightX, 0, 1, 8, CollisionFlag.Solid);
            }
            if (footTile != 0) {
                player.Land(Player.PlatformY);
            }
        }
    }

    inline void ResolveGroundAndHazards(PlayerState player, Pixel footLeftX, Pixel footCenterX, Pixel footRightX) {
        if (player.y >= Player.GroundY + 1) {
            failTile = collision_aabb_tiles(footLeftX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Hazard);
            if (failTile == 0) {
                failTile = collision_aabb_tiles(footCenterX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Hazard);
            }
            if (failTile == 0) {
                failTile = collision_aabb_tiles(footRightX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Hazard);
            }
            if (failTile != 0) {
                resetRequested = 1;
                hazardHit = 1;
            }

            footTile = collision_aabb_tiles(footLeftX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Solid);
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footCenterX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Solid);
            }
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footRightX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Solid);
            }

            if (footTile != 0) {
                player.Land(Player.GroundY);
            }
        }
    }

    inline void ResolveFallAndEnemyContact(PlayerState player, EnemyState enemy) {
        if (player.grounded == 0) {
            if (player.y >= Player.FallResetY) {
                resetRequested = 1;
            }
        }

        if (enemy.x in Enemy.HitStartX..Enemy.HitEndX) {
            if (player.y >= Player.EnemyHitY) {
                resetRequested = 1;
                impactHit = 1;
            }
        }
    }

    inline void ResolveReset(PlayerState player, EnemyState enemy) {
        if (resetRequested != 0) {
            footTile = 0;
            failTile = 0;
            player.Reset();
            enemy.Spawn();
            if (hazardHit != 0) {
                player.BounceFromHazard();
            }
            if (impactHit != 0) {
                player.BounceFromHazard();
            }
        }
    }
}

inline pure Pixel WrapWorldX(Pixel x) => x >= World.PixelWrap ? x - World.PixelWrap : x;

inline void PresentFrame(PlayerState player, EnemyState enemy) {
    video.WaitVBlank();
    camera.Apply();
    sprite.Draw(mario_player, Player.ScreenX, player.y, player.displayFrame, player.displayFlipX, 0);
    sprite.Draw(enemy_slug, enemy.x, Enemy.GroundY, enemy.frame, false, 0);
    sprite.Draw(enemy_slug, Enemy.PlatformX, Enemy.PlatformY, enemy.frame, true, 0);
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
    sprite.Asset(enemy_slug, "assets/enemy-slug.gb.png", 16, 16);
    animation.Clip(run, 1, 6, 6, 6);
    animation.Clip(enemy_walk, 0, 12, 12);
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
    EnemyState enemy;
    CameraState view;
    FrameState frame;
    player.Reset();
    enemy.Spawn();

    loop {
        PresentFrame(player, enemy);
        input.Poll();

        frame.Begin();
        player.ApplyGravity();

        let playerWorldX = (view.x + Player.ScreenX) |> WrapWorldX();
        let footLeftX = playerWorldX;
        let footCenterX = (playerWorldX + 8) |> WrapWorldX();
        let footRightX = (playerWorldX + 17) |> WrapWorldX();

        frame.ResolvePlatformLanding(player, footLeftX, footCenterX, footRightX);
        frame.ResolveGroundAndHazards(player, footLeftX, footCenterX, footRightX);
        frame.ResolveFallAndEnemyContact(player, enemy);
        frame.ResolveReset(player, enemy);
        player.HandleJumpInput();
        view.HandleHorizontalInput(player);
        enemy.Step();
        player.UpdateRunAnimation(view);

    }
}
