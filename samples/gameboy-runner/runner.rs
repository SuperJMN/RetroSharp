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

void draw_starting_scene() {
    tilemap_fill(0, 0, 32, 32, 0);
    tilemap_fill(0, 14, 32, 1, 5);
    tilemap_fill(0, 13, 32, 1, 4);
    tilemap_fill(13, 13, 3, 2, 0);
    tilemap_fill(29, 13, 3, 2, 0);
    tilemap_set(2, 12, 5);
    tilemap_set(7, 13, 3);
    tilemap_set(10, 11, 5);
    tilemap_set(18, 12, 5);
    tilemap_set(23, 13, 3);
    tilemap_set(26, 11, 5);
    return;
}

void define_level_columns() {
    map_column(0, 0, 0, 4, 5);
    map_column(1, 0, 0, 4, 5);
    map_column(2, 0, 5, 4, 5);
    map_column(3, 0, 0, 4, 5);
    map_column(4, 0, 0, 4, 5);
    map_column(5, 0, 0, 4, 5);
    map_column(6, 0, 0, 4, 5);
    map_column(7, 0, 0, 3, 5);
    map_column(8, 0, 0, 4, 5);
    map_column(9, 0, 0, 4, 5);
    map_column(10, 5, 0, 4, 5);
    map_column(11, 0, 0, 4, 5);
    map_column(12, 0, 0, 4, 5);
    map_column(13, 0, 0, 0, 0);
    map_column(14, 0, 0, 0, 0);
    map_column(15, 0, 0, 0, 0);
    return;
}

void main() {
    setup_video();
    draw_starting_scene();
    define_level_columns();
    i16 camera = 0;
    i16 fine = 0;
    i16 streamColumn = 20;
    i16 leftStreamColumn = 31;
    i16 screenLeftColumn = 0;
    i16 rightSourceColumn = 4;
    i16 leftSourceColumn = 15;
    i16 playerLeftFootColumn = 0;
    i16 playerRightFootColumn = 0;
    i16 footTile = 0;
    i16 failTile = 0;
    i16 playerY = 73;
    i16 velocityY = 0;
    i16 grounded = 1;
    i16 frame = 0;
    i16 displayFrame = 0;
    i16 displayFlags = 0;
    i16 animTick = 0;
    i16 jumping = 0;
    i16 jumpTicks = 0;
    i16 moving = 0;
    i16 resetRequested = 0;

    while (true) {
        video_wait_vblank();
        scroll_set(camera, 0);
        sprite_draw(mario_player, 72, playerY, displayFrame, displayFlags);

        input_poll();

        resetRequested = 0;
        grounded = 0;
        velocityY = velocityY + 1;
        playerY = playerY + velocityY;
        playerLeftFootColumn = screenLeftColumn + 9;
        if (playerLeftFootColumn >= 16) {
            playerLeftFootColumn = playerLeftFootColumn - 16;
        }

        playerRightFootColumn = playerLeftFootColumn + 1;
        if (playerRightFootColumn == 16) {
            playerRightFootColumn = 0;
        }

        footTile = 0;
        failTile = 0;
        if (playerY >= 74) {
            failTile = map_tile_at(playerLeftFootColumn, 2);
            if (failTile == 3) {
                resetRequested = 1;
            }

            if (failTile != 0) {
                if (failTile != 3) {
                    footTile = failTile;
                }
            }

            failTile = map_tile_at(playerRightFootColumn, 2);
            if (failTile == 3) {
                resetRequested = 1;
            }

            if (failTile != 0) {
                if (failTile != 3) {
                    footTile = failTile;
                }
            }

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
            displayFlags = 0;
            camera = camera + 1;
            fine = fine + 1;
        }

        if (button_down(left) != 0) {
            moving = 1;
            displayFlags = 32;
            camera = camera - 1;
            fine = fine - 1;
        }

        if (fine == 8) {
            fine = 0;
            map_stream_column(streamColumn, rightSourceColumn, 11, 4);

            streamColumn = streamColumn + 1;
            if (streamColumn == 32) {
                streamColumn = 0;
            }

            leftStreamColumn = leftStreamColumn + 1;
            if (leftStreamColumn == 32) {
                leftStreamColumn = 0;
            }

            screenLeftColumn = screenLeftColumn + 1;
            if (screenLeftColumn == 16) {
                screenLeftColumn = 0;
            }

            rightSourceColumn = rightSourceColumn + 1;
            if (rightSourceColumn == 16) {
                rightSourceColumn = 0;
            }

            leftSourceColumn = leftSourceColumn + 1;
            if (leftSourceColumn == 16) {
                leftSourceColumn = 0;
            }
        }

        if (fine == 255) {
            fine = 7;

            map_stream_column(leftStreamColumn, leftSourceColumn, 11, 4);

            streamColumn = streamColumn - 1;
            if (streamColumn == 255) {
                streamColumn = 31;
            }

            leftStreamColumn = leftStreamColumn - 1;
            if (leftStreamColumn == 255) {
                leftStreamColumn = 31;
            }

            screenLeftColumn = screenLeftColumn - 1;
            if (screenLeftColumn == 255) {
                screenLeftColumn = 15;
            }

            rightSourceColumn = rightSourceColumn - 1;
            if (rightSourceColumn == 255) {
                rightSourceColumn = 15;
            }

            leftSourceColumn = leftSourceColumn - 1;
            if (leftSourceColumn == 255) {
                leftSourceColumn = 15;
            }
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

        if (resetRequested != 0) {
            playerLeftFootColumn = 0;
            playerRightFootColumn = 0;
            footTile = 0;
            failTile = 0;
            playerY = 73;
            velocityY = 0;
            grounded = 1;
            displayFrame = 0;
            jumping = 0;
            jumpTicks = 0;
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
