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
    StartX = 128,
    GroundY = 89,
    PlatformX = 40,
    PlatformY = 57,
    WrapAtX = 96,
    RespawnX = 136,
    HitStartX = 68,
    HitEndX = 91
}

enum CollisionFlag { None = 0, Solid = 1, Hazard = 2 }

struct PlayerState {
    Pixel y;
    Pixel velocityY;
    Pixel grounded;
    Pixel displayFrame;
    bool displayFlipX;
    Pixel animTick;
    Pixel jumping;
    Pixel jumpTicks;
}

struct EnemyState {
    Pixel x;
    Pixel frame;
    Pixel tick;
}

struct CameraState {
    Pixel x;
    Pixel moving;
}

struct FrameState {
    Pixel footTile;
    Pixel failTile;
    Pixel hazardHit;
    Pixel resetRequested;
}

inline pure Pixel WrapWorldX(Pixel x) => x >= World.PixelWrap ? x - World.PixelWrap : x;

inline void PresentFrame(PlayerState player, EnemyState enemy) {
    video.WaitVBlank();
    camera.Apply();
    sprite.Draw(mario_player, Player.ScreenX, player.y, player.displayFrame, player.displayFlipX, 0);
    sprite.Draw(enemy_slug, enemy.x, Enemy.GroundY, enemy.frame, false, 0);
    sprite.Draw(enemy_slug, Enemy.PlatformX, Enemy.PlatformY, enemy.frame, true, 0);
}

inline void Reset(this PlayerState player) {
    player.y = Player.StartY;
    player.velocityY = 0;
    player.grounded = 1;
    player.displayFrame = 0;
    player.jumping = 0;
    player.jumpTicks = 0;
}

inline void ApplyGravity(this PlayerState player) {
    player.grounded = 0;
    player.velocityY += 1;
    player.y += player.velocityY;
    if (player.velocityY >= World.PixelWrap) {
        if (player.y >= World.PixelWrap) {
            player.y = 0;
            player.velocityY = 0;
            player.jumping = 0;
        }
    }
}

inline void Land(this PlayerState player, Pixel y) {
    player.y = y;
    player.velocityY = 0;
    player.grounded = 1;
    player.jumping = 0;
}

inline void BounceFromHazard(this PlayerState player) {
    player.velocityY = Jump.HazardBounceVelocity;
    player.grounded = 0;
    player.displayFrame = 4;
}

inline void StartJump(this PlayerState player) {
    player.velocityY = Jump.Velocity;
    player.grounded = 0;
    player.jumping = 1;
}

inline void SelectDisplayFrame(this PlayerState player, Pixel moving) {
    player.displayFrame = player.grounded switch {
        0 => 4,
        _ => moving switch {
            0 => 0,
            _ => animation.Frame(run, player.animTick)
        }
    };
}

inline void HandleJumpInput(this PlayerState player) {
    if (button_just_pressed(a) != 0) {
        if (player.grounded != 0) {
            player.StartJump();
        }
    }

    if (player.jumping != 0) {
        player.jumpTicks = button_hold_ticks(a);
        if (button_down(a) != 0) {
            if (player.jumpTicks < Jump.BoostTicks) {
                player.velocityY -= 1;
            }
        }

        if (button_just_released(a) != 0) {
            player.jumping = 0;
        }
    }
}

inline void UpdateRunAnimation(this PlayerState player, CameraState view) {
    if (view.moving != 0) {
        player.animTick++;
    } else {
        player.animTick = 0;
    }

    player.SelectDisplayFrame(view.moving);
}

inline void Spawn(this EnemyState enemy) {
    enemy.x = Enemy.StartX;
    enemy.frame = 0;
    enemy.tick = 0;
}

inline void Step(this EnemyState enemy) {
    enemy.x--;
    if (enemy.x <= Enemy.WrapAtX) {
        enemy.x = Enemy.RespawnX;
    }

    enemy.tick++;
    enemy.frame = animation.Frame(enemy_walk, enemy.tick);
}

inline void HandleHorizontalInput(this CameraState view, PlayerState player) {
    view.moving = 0;
    if (button_down(right) != 0) {
        view.moving = 1;
        player.displayFlipX = false;
        view.x += 1;
    }

    if (button_down(left) != 0) {
        view.moving = 1;
        player.displayFlipX = true;
        view.x -= 1;
    }

    if (view.moving != 0) {
        camera.SetPosition(view.x, 0);
    }
}

inline void Begin(this FrameState frame) {
    frame.footTile = 0;
    frame.failTile = 0;
    frame.hazardHit = 0;
    frame.resetRequested = 0;
}

inline void ResolvePlatformLanding(this FrameState frame, PlayerState player, Pixel footLeftX, Pixel footCenterX, Pixel footRightX) {
    if (player.y in CollisionProbe.PlatformStartY..CollisionProbe.PlatformEndY && player.velocityY < World.PixelWrap && player.velocityY != 0) {
        frame.footTile = collision_aabb_tiles(footLeftX, 0, 1, 8, CollisionFlag.Solid);
        if (frame.footTile == 0) {
            frame.footTile = collision_aabb_tiles(footCenterX, 0, 1, 8, CollisionFlag.Solid);
        }
        if (frame.footTile == 0) {
            frame.footTile = collision_aabb_tiles(footRightX, 0, 1, 8, CollisionFlag.Solid);
        }
        if (frame.footTile != 0) {
            player.Land(Player.PlatformY);
        }
    }
}

inline void ResolveGroundAndHazards(this FrameState frame, PlayerState player, Pixel footLeftX, Pixel footCenterX, Pixel footRightX) {
    if (player.y >= Player.GroundY + 1) {
        frame.failTile = collision_aabb_tiles(footLeftX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Hazard);
        if (frame.failTile == 0) {
            frame.failTile = collision_aabb_tiles(footCenterX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Hazard);
        }
        if (frame.failTile == 0) {
            frame.failTile = collision_aabb_tiles(footRightX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Hazard);
        }
        if (frame.failTile != 0) {
            frame.resetRequested = 1;
            frame.hazardHit = 1;
        }

        frame.footTile = collision_aabb_tiles(footLeftX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Solid);
        if (frame.footTile == 0) {
            frame.footTile = collision_aabb_tiles(footCenterX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Solid);
        }
        if (frame.footTile == 0) {
            frame.footTile = collision_aabb_tiles(footRightX, CollisionProbe.GroundY, 1, 8, CollisionFlag.Solid);
        }

        if (frame.footTile != 0) {
            player.Land(Player.GroundY);
        }
    }
}

inline void ResolveFallAndEnemyContact(this FrameState frame, PlayerState player, EnemyState enemy) {
    if (player.grounded == 0) {
        if (player.y >= Player.FallResetY) {
            frame.resetRequested = 1;
        }
    }

    if (enemy.x in Enemy.HitStartX..Enemy.HitEndX) {
        if (player.y >= Player.EnemyHitY) {
            frame.resetRequested = 1;
        }
    }
}

inline void ResolveReset(this FrameState frame, PlayerState player, EnemyState enemy) {
    if (frame.resetRequested != 0) {
        frame.footTile = 0;
        frame.failTile = 0;
        player.Reset();
        enemy.Spawn();
        if (frame.hazardHit != 0) {
            player.BounceFromHazard();
        }
    }
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
    world.Map(World.Width, World.StreamY, World.Height);
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
