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
    sprite.Asset(mario_player, "../assets/mario-player.gb.png", 18, 32);
    animation.Clip(run, 1, 6, 6, 6);
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
    world.Column(0, 0, 0, 0, 0, 4, 5);
    world.Column(1, 0, 0, 0, 0, 4, 5);
    world.Column(2, 0, 0, 0, 0, 4, 5);
    world.Column(3, 0, 0, 0, 0, 4, 5);
    world.Column(4, 0, 0, 0, 0, 4, 5);
    world.Column(5, 0, 0, 0, 0, 4, 5);
    world.Column(6, 0, 0, 0, 0, 4, 5);
    world.Column(7, 0, 0, 0, 0, 4, 5);
    world.Column(8, 0, 0, 0, 0, 4, 5);
    world.Column(9, 0, 0, 0, 0, 4, 5);
    world.Column(10, 0, 0, 0, 0, 4, 5);
    world.Column(11, 0, 0, 0, 0, 4, 5);
    world.Column(12, 0, 0, 0, 0, 4, 5);
    world.Column(13, 0, 0, 0, 0, 4, 5);
    world.Column(14, 0, 0, 0, 0, 4, 5);
    world.Column(15, 0, 0, 0, 0, 4, 5);
    world.Flags(0, 0, 0, 0, 0, 1, 1);
    world.Flags(1, 0, 0, 0, 0, 1, 1);
    world.Flags(2, 0, 0, 0, 0, 1, 1);
    world.Flags(3, 0, 0, 0, 0, 1, 1);
    world.Flags(4, 0, 0, 0, 0, 1, 1);
    world.Flags(5, 0, 0, 0, 0, 1, 1);
    world.Flags(6, 0, 0, 0, 0, 1, 1);
    world.Flags(7, 0, 0, 0, 0, 1, 1);
    world.Flags(8, 0, 0, 0, 0, 1, 1);
    world.Flags(9, 0, 0, 0, 0, 1, 1);
    world.Flags(10, 0, 0, 0, 0, 1, 1);
    world.Flags(11, 0, 0, 0, 0, 1, 1);
    world.Flags(12, 0, 0, 0, 0, 1, 1);
    world.Flags(13, 0, 0, 0, 0, 1, 1);
    world.Flags(14, 0, 0, 0, 0, 1, 1);
    world.Flags(15, 0, 0, 0, 0, 1, 1);
    return;
}

void main() {
    setup_video();
    draw_background();
    define_world();
    world.Map(16, 9, 6);
    camera.Init(16, 9, 6);
    i16 cameraX = 0;
    i16 playerWorldX = 72;
    i16 footTile = 0;
    i16 playerY = 73;
    i16 velocityY = 0;
    i16 grounded = 1;
    i16 displayFrame = 0;
    bool displayFlipX = false;
    i16 animTick = 0;
    i16 jumping = 0;
    i16 jumpTicks = 0;
    i16 moving = 0;
    i16 resetRequested = 0;

    loop {
        video.WaitVBlank();
        camera.Apply();
        sprite.Draw(mario_player, 72, playerY, displayFrame, displayFlipX, 0);

        input.Poll();

        resetRequested = 0;
        grounded = 0;
        velocityY += 1;
        playerY += velocityY;
        if (velocityY >= 128) {
            if (playerY >= 128) {
                playerY = 0;
                velocityY = 0;
                jumping = 0;
            }
        }

        footTile = 0;
        playerWorldX = cameraX + 72;
        if (playerWorldX >= 128) {
            playerWorldX = playerWorldX - 128;
        }

        if (playerY >= 74) {
            footTile = collision_aabb_tiles(playerWorldX, 32, sprite_width(mario_player), 8, 1);

            if (footTile != 0) {
                playerY = 73;
                velocityY = 0;
                grounded = 1;
                jumping = 0;
            }
        }

        if (grounded == 0) {
            if (playerY >= 116) {
                resetRequested = 1;
            }
        }

        if (resetRequested != 0) {
            footTile = 0;
            playerY = 73;
            velocityY = 0;
            grounded = 1;
            displayFrame = 0;
            jumping = 0;
            jumpTicks = 0;
        }

        if (button_just_pressed(a) != 0) {
            if (grounded != 0) {
                velocityY = 252;
                grounded = 0;
                jumping = 1;
            }
        }

        if (jumping != 0) {
            jumpTicks = button_hold_ticks(a);
            if (button_down(a) != 0) {
                if (jumpTicks < 12) {
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

        if (moving != 0) {
            animTick++;
        } else {
            animTick = 0;
        }

        if (grounded == 0) {
            displayFrame = 4;
        } else {
            if (moving != 0) {
                displayFrame = animation.Frame(run, animTick);
            } else {
                displayFrame = 0;
            }
        }

    }
}
