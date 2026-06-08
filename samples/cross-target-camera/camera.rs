void main() {
    video_init();

    world_column(0, 1, 2, 3, 4);
    world_column(1, 2, 3, 4, 5);
    world_column(2, 3, 4, 5, 1);
    world_column(3, 4, 5, 1, 2);
    world_column(4, 5, 1, 2, 3);
    world_column(5, 1, 2, 3, 4);
    world_column(6, 2, 3, 4, 5);
    world_column(7, 3, 4, 5, 1);
    world_flags(0, 0, 0, 1, 1);
    world_flags(1, 0, 0, 1, 1);
    world_flags(2, 0, 0, 1, 1);
    world_flags(3, 0, 0, 1, 1);
    world_flags(4, 0, 0, 1, 1);
    world_flags(5, 0, 0, 1, 1);
    world_flags(6, 0, 0, 1, 1);
    world_flags(7, 0, 0, 1, 1);
    world_map(8, 10, 4);
    camera_init(8, 10, 4);
    sprite_asset(marker, "marker.json");

    i16 cameraX = 0;
    i16 markerY = 72;

    while (true) {
        video_wait_vblank();
        input_poll();
        cameraX = button_hold_ticks(right);
        camera_set_position(cameraX, 0);
        camera_apply();
        sprite_draw(marker, 72, markerY, 0, 0, 0);
    }
}
