void main() {
    video_init();
    palette_set(0, 0);
    palette_set(1, 1);
    palette_set(2, 2);
    palette_set(3, 3);
    object_palette_set(0, 0);
    object_palette_set(1, 1);
    object_palette_set(2, 2);
    object_palette_set(3, 3);
    sprite_asset(mario_run, "assets/mario-run.gb.png", 16, 27);

    tilemap_fill(0, 0, 32, 32, 0);
    tilemap_fill(0, 14, 32, 1, 5);
    tilemap_fill(0, 13, 32, 1, 4);
    tilemap_set(4, 12, 5);
    tilemap_set(12, 11, 5);
    tilemap_set(20, 12, 5);
    tilemap_set(28, 11, 5);

    map_column(0, 0, 0, 3, 5);
    map_column(1, 0, 0, 3, 5);
    map_column(2, 0, 5, 3, 5);
    map_column(3, 0, 0, 3, 5);
    map_column(4, 0, 0, 3, 5);
    map_column(5, 0, 0, 3, 5);
    map_column(6, 0, 5, 3, 5);
    map_column(7, 0, 0, 3, 5);
    map_column(8, 0, 0, 3, 5);
    map_column(9, 0, 0, 3, 5);
    map_column(10, 5, 0, 3, 5);
    map_column(11, 0, 0, 3, 5);
    map_column(12, 0, 0, 3, 5);
    map_column(13, 0, 5, 3, 5);
    map_column(14, 0, 0, 3, 5);
    map_column(15, 0, 0, 3, 5);

    i16 camera = 0;
    i16 fine = 0;
    i16 streamColumn = 20;
    i16 mapColumn = 0;
    i16 collisionColumn = 0;
    i16 playerY = 40;
    i16 velocityY = 0;
    i16 grounded = 0;
    i16 frame = 0;
    i16 animTick = 0;

    while (true) {
        video_wait_vblank();

        grounded = 0;
        velocityY = velocityY + 1;
        playerY = playerY + velocityY;
        collisionColumn = mapColumn;
        if (playerY >= 78) {
            if (map_tile_at(collisionColumn, 2) != 0) {
                playerY = 77;
                velocityY = 0;
                grounded = 1;
            }
        }

        if (button_pressed(a) != 0) {
            if (grounded != 0) {
                velocityY = 252;
                grounded = 0;
            }
        }

        scroll_set(camera, 0);
        sprite_draw(mario_run, 72, playerY, frame);
        camera = camera + 1;
        fine = fine + 1;
        animTick = animTick + 1;

        if (fine == 8) {
            fine = 0;
            map_stream_column(streamColumn, mapColumn, 11, 4);

            streamColumn = streamColumn + 1;
            if (streamColumn == 32) {
                streamColumn = 0;
            }

            mapColumn = mapColumn + 1;
            if (mapColumn == 16) {
                mapColumn = 0;
            }
        }

        if (animTick == 6) {
            animTick = 0;
            frame = frame + 1;
            if (frame == 3) {
                frame = 0;
            }
        }
    }
}
