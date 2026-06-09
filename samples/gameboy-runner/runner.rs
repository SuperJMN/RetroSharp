type Pixel = i16;

const WorldWidth = 16;
const WorldStreamY = 9;
const WorldHeight = 6;
const WorldWrap = 128;
const PlayerScreenX = 72;
const PlayerStartY = 73;
const PlayerPlatformY = 41;
const PlayerGroundY = 73;
const PlayerEnemyHitY = 72;
const PlayerFallResetY = 116;
const PlatformProbeStartY = 42;
const PlatformProbeEndY = 59;
const GroundProbeY = 32;
const JumpVelocity = 252;
const HazardBounceVelocity = 248;
const JumpBoostTicks = 12;
const EnemyStartX = 128;
const EnemyGroundY = 89;
const EnemyPlatformX = 40;
const EnemyPlatformY = 57;
const EnemyWrapAtX = 96;
const EnemyRespawnX = 136;
const EnemyHitStartX = 68;
const EnemyHitEndX = 91;

enum CollisionFlag { None = 0, Solid = 1, Hazard = 2 }

inline pure Pixel WrapWorldX(Pixel x) => x >= WorldWrap ? x - WorldWrap : x;

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

void draw_background() {
    tilemap.Set(2, 4, 1);
    tilemap.Set(3, 4, 1);
    tilemap.Set(4, 5, 1);
    tilemap.Set(11, 3, 1);
    tilemap.Set(12, 3, 1);
    tilemap.Set(13, 4, 1);
    tilemap.Set(22, 4, 1);
    tilemap.Set(23, 4, 1);
    tilemap.Set(24, 5, 1);
    tilemap.Set(1, 8, 2);
    tilemap.Set(2, 8, 2);
    tilemap.Set(3, 8, 2);
    tilemap.Set(9, 8, 2);
    tilemap.Set(10, 7, 2);
    tilemap.Set(11, 8, 2);
    tilemap.Set(16, 8, 2);
    tilemap.Set(17, 8, 2);
    tilemap.Set(25, 8, 2);
    tilemap.Set(26, 8, 2);
    tilemap.Set(27, 8, 2);
    return;
}

void define_world() {
    world.Column(0, 0, 0, 2, 0, 4, 5);
    world.Column(1, 0, 0, 2, 0, 4, 5);
    world.Column(2, 0, 0, 0, 0, 4, 5);
    world.Column(3, 0, 0, 0, 0, 4, 5);
    world.Column(4, 0, 0, 0, 0, 4, 5);
    world.Column(5, 5, 0, 0, 0, 4, 5);
    world.Column(6, 5, 0, 0, 0, 4, 5);
    world.Column(7, 5, 0, 0, 0, 3, 5);
    world.Column(8, 5, 0, 2, 0, 3, 5);
    world.Column(9, 0, 0, 2, 0, 4, 5);
    world.Column(10, 0, 0, 2, 0, 4, 5);
    world.Column(11, 0, 0, 0, 0, 4, 5);
    world.Column(12, 0, 0, 0, 0, 3, 5);
    world.Column(13, 0, 0, 0, 0, 3, 5);
    world.Column(14, 0, 0, 0, 0, 0, 0);
    world.Column(15, 0, 0, 0, 0, 0, 0);
    world.Flags(0, 0, 0, 0, 0, 1, 1);
    world.Flags(1, 0, 0, 0, 0, 1, 1);
    world.Flags(2, 0, 0, 0, 0, 1, 1);
    world.Flags(3, 0, 0, 0, 0, 1, 1);
    world.Flags(4, 0, 0, 0, 0, 1, 1);
    world.Flags(5, 1, 0, 0, 0, 1, 1);
    world.Flags(6, 1, 0, 0, 0, 1, 1);
    world.Flags(7, 1, 0, 0, 0, 2, 1);
    world.Flags(8, 1, 0, 0, 0, 2, 1);
    world.Flags(9, 0, 0, 0, 0, 1, 1);
    world.Flags(10, 0, 0, 0, 0, 1, 1);
    world.Flags(11, 0, 0, 0, 0, 1, 1);
    world.Flags(12, 0, 0, 0, 0, 2, 1);
    world.Flags(13, 0, 0, 0, 0, 2, 1);
    world.Flags(14, 0, 0, 0, 0, 0, 0);
    world.Flags(15, 0, 0, 0, 0, 0, 0);
    return;
}

void main() {
    setup_video();
    draw_background();
    define_world();
    world.Map(WorldWidth, WorldStreamY, WorldHeight);
    camera.Init(WorldWidth, WorldStreamY, WorldHeight);
    Pixel cameraX = 0;
    Pixel footTile = 0;
    Pixel failTile = 0;
    Pixel hazardHit = 0;
    Pixel playerY = PlayerStartY;
    Pixel velocityY = 0;
    Pixel grounded = 1;
    Pixel displayFrame = 0;
    bool displayFlipX = false;
    Pixel animTick = 0;
    Pixel jumping = 0;
    Pixel jumpTicks = 0;
    Pixel moving = 0;
    Pixel resetRequested = 0;
    Pixel enemyX = EnemyStartX;
    Pixel enemyFrame = 0;
    Pixel enemyTick = 0;

    loop {
        video.WaitVBlank();
        camera.Apply();
        sprite.Draw(mario_player, PlayerScreenX, playerY, displayFrame, displayFlipX, 0);
        sprite.Draw(enemy_slug, enemyX, EnemyGroundY, enemyFrame, false, 0);
        sprite.Draw(enemy_slug, EnemyPlatformX, EnemyPlatformY, enemyFrame, true, 0);

        input.Poll();

        resetRequested = 0;
        hazardHit = 0;
        grounded = 0;
        velocityY += 1;
        playerY += velocityY;
        if (velocityY >= WorldWrap) {
            if (playerY >= WorldWrap) {
                playerY = 0;
                velocityY = 0;
                jumping = 0;
            }
        }

        footTile = 0;
        failTile = 0;
        let playerWorldX = (cameraX + PlayerScreenX) |> WrapWorldX();
        let footLeftX = playerWorldX;
        let footCenterX = (playerWorldX + 8) |> WrapWorldX();
        let footRightX = (playerWorldX + 17) |> WrapWorldX();

        if (playerY in PlatformProbeStartY..PlatformProbeEndY && velocityY < WorldWrap && velocityY != 0) {
            footTile = collision_aabb_tiles(footLeftX, 0, 1, 8, CollisionFlag.Solid);
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footCenterX, 0, 1, 8, CollisionFlag.Solid);
            }
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footRightX, 0, 1, 8, CollisionFlag.Solid);
            }
            if (footTile != 0) {
                playerY = PlayerPlatformY;
                velocityY = 0;
                grounded = 1;
                jumping = 0;
            }
        }

        if (playerY >= PlayerGroundY + 1) {
            failTile = collision_aabb_tiles(footLeftX, GroundProbeY, 1, 8, CollisionFlag.Hazard);
            if (failTile == 0) {
                failTile = collision_aabb_tiles(footCenterX, GroundProbeY, 1, 8, CollisionFlag.Hazard);
            }
            if (failTile == 0) {
                failTile = collision_aabb_tiles(footRightX, GroundProbeY, 1, 8, CollisionFlag.Hazard);
            }
            if (failTile != 0) {
                resetRequested = 1;
                hazardHit = 1;
            }

            footTile = collision_aabb_tiles(footLeftX, GroundProbeY, 1, 8, CollisionFlag.Solid);
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footCenterX, GroundProbeY, 1, 8, CollisionFlag.Solid);
            }
            if (footTile == 0) {
                footTile = collision_aabb_tiles(footRightX, GroundProbeY, 1, 8, CollisionFlag.Solid);
            }

            if (footTile != 0) {
                playerY = PlayerGroundY;
                velocityY = 0;
                grounded = 1;
                jumping = 0;
            }
        }

        if (grounded == 0) {
            if (playerY >= PlayerFallResetY) {
                resetRequested = 1;
            }
        }

        if (enemyX in EnemyHitStartX..EnemyHitEndX) {
            if (playerY >= PlayerEnemyHitY) {
                resetRequested = 1;
            }
        }

        if (resetRequested != 0) {
            footTile = 0;
            failTile = 0;
            playerY = PlayerStartY;
            velocityY = 0;
            grounded = 1;
            displayFrame = 0;
            jumping = 0;
            jumpTicks = 0;
            enemyX = EnemyStartX;
            enemyFrame = 0;
            enemyTick = 0;
            if (hazardHit != 0) {
                velocityY = HazardBounceVelocity;
                grounded = 0;
                displayFrame = 4;
            }
        }

        if (button_just_pressed(a) != 0) {
            if (grounded != 0) {
                velocityY = JumpVelocity;
                grounded = 0;
                jumping = 1;
            }
        }

        if (jumping != 0) {
            jumpTicks = button_hold_ticks(a);
            if (button_down(a) != 0) {
                if (jumpTicks < JumpBoostTicks) {
                    velocityY -= 1;
                }
            }

            if (button_just_released(a) != 0) {
                jumping = 0;
            }
        }

        moving = 0;
        if (button_down(right) != 0) {
            moving = 1;
            displayFlipX = false;
            cameraX += 1;
        }

        if (button_down(left) != 0) {
            moving = 1;
            displayFlipX = true;
            cameraX -= 1;
        }

        if (moving != 0) {
            camera.SetPosition(cameraX, 0);
        }

        enemyX--;
        if (enemyX <= EnemyWrapAtX) {
            enemyX = EnemyRespawnX;
        }

        enemyTick++;
        enemyFrame = animation.Frame(enemy_walk, enemyTick);

        if (moving != 0) {
            animTick++;
        } else {
            animTick = 0;
        }

        displayFrame = grounded switch {
            0 => 4,
            _ => moving switch {
                0 => 0,
                _ => animation.Frame(run, animTick)
            }
        };

    }
}
