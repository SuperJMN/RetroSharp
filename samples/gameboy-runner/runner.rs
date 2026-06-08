void setup_video() {
    video_init();
    palette_set(0, 0);
    palette_set(1, 1);
    palette_set(2, 2);
    palette_set(3, 3);
    object_palette_set(0, 0);
    object_palette_set(1, 0);
    object_palette_set(2, 1);
    object_palette_set(3, 3);
    sprite_asset(mario_player, "assets/mario-player.gb.png", 18, 32);
    return;
}

void define_world() {
    world_column(0, 0, 0, 4, 5);
    world_column(1, 0, 0, 4, 5);
    world_column(2, 0, 5, 4, 5);
    world_column(3, 0, 0, 4, 5);
    world_column(4, 0, 0, 4, 5);
    world_column(5, 0, 0, 4, 5);
    world_column(6, 0, 0, 4, 5);
    world_column(7, 0, 0, 3, 5);
    world_column(8, 0, 0, 4, 5);
    world_column(9, 0, 0, 4, 5);
    world_column(10, 5, 0, 4, 5);
    world_column(11, 0, 0, 4, 5);
    world_column(12, 0, 0, 4, 5);
    world_column(13, 0, 0, 0, 0);
    world_column(14, 0, 0, 0, 0);
    world_column(15, 0, 0, 0, 0);
    world_flags(0, 0, 0, 1, 1);
    world_flags(1, 0, 0, 1, 1);
    world_flags(2, 0, 1, 1, 1);
    world_flags(3, 0, 0, 1, 1);
    world_flags(4, 0, 0, 1, 1);
    world_flags(5, 0, 0, 1, 1);
    world_flags(6, 0, 0, 1, 1);
    world_flags(7, 0, 0, 2, 1);
    world_flags(8, 0, 0, 1, 1);
    world_flags(9, 0, 0, 1, 1);
    world_flags(10, 1, 0, 1, 1);
    world_flags(11, 0, 0, 1, 1);
    world_flags(12, 0, 0, 1, 1);
    world_flags(13, 0, 0, 0, 0);
    world_flags(14, 0, 0, 0, 0);
    world_flags(15, 0, 0, 0, 0);
    return;
}

void main() {
    setup_video();
    define_world();
    world_map(16, 11, 4);
    camera_init(16, 11, 4);
    i16 cameraX = 0;
    i16 footTile = 0;
    i16 failTile = 0;
    i16 playerY = 73;
    i16 velocityY = 0;
    i16 grounded = 1;
    i16 frame = 0;
    i16 displayFrame = 0;
    bool displayFlipX = false;
    i16 animTick = 0;
    i16 jumping = 0;
    i16 jumpTicks = 0;
    i16 moving = 0;
    i16 resetRequested = 0;

    while (true) {
        video_wait_vblank();
        camera_apply();
        sprite_draw(mario_player, 72, playerY, displayFrame, displayFlipX, 0);

        input_poll();

        resetRequested = 0;
        grounded = 0;
        velocityY = velocityY + 1;
        playerY = playerY + velocityY;

        footTile = 0;
        failTile = 0;
        if (playerY >= 74) {
            failTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 2);
            if (failTile != 0) {
                resetRequested = 1;
            }

            footTile = camera_span_has_flags(72, sprite_width(mario_player), 2, 1);

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
            failTile = 0;
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
                    velocityY = velocityY - 1;
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
            cameraX = cameraX + 1;
        }

        if (button_down(left) != 0) {
            moving = 1;
            displayFlipX = true;
            cameraX = cameraX - 1;
        }

        if (moving != 0) {
            camera_set_position(cameraX, 0);
        }

        if (moving != 0) {
            animTick = animTick + 1;
        } else {
            animTick = 0;
            frame = 0;
        }

        if (moving != 0) {
            if (animTick == 6) {
                animTick = 0;
                frame = frame + 1;
                if (frame == 3) {
                    frame = 0;
                }
            }
        }

        if (grounded == 0) {
            displayFrame = 4;
        } else {
            if (moving != 0) {
                displayFrame = frame + 1;
            } else {
                displayFrame = 0;
            }
        }

    }
}
