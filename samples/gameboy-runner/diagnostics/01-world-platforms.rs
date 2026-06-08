void setup_video() {
    video_init();
    palette_set(0, 0);
    palette_set(1, 1);
    palette_set(2, 2);
    palette_set(3, 3);
    return;
}

void draw_background() {
    tilemap_set(2, 4, 1);
    tilemap_set(3, 4, 1);
    tilemap_set(4, 5, 1);
    tilemap_set(11, 3, 1);
    tilemap_set(12, 3, 1);
    tilemap_set(13, 4, 1);
    tilemap_set(22, 4, 1);
    tilemap_set(23, 4, 1);
    tilemap_set(24, 5, 1);
    tilemap_set(1, 8, 2);
    tilemap_set(2, 8, 2);
    tilemap_set(3, 8, 2);
    tilemap_set(9, 8, 2);
    tilemap_set(10, 7, 2);
    tilemap_set(11, 8, 2);
    tilemap_set(16, 8, 2);
    tilemap_set(17, 8, 2);
    tilemap_set(25, 8, 2);
    tilemap_set(26, 8, 2);
    tilemap_set(27, 8, 2);
    return;
}

void define_world() {
    world_column(0, 0, 0, 2, 0, 4, 5);
    world_column(1, 0, 0, 2, 0, 4, 5);
    world_column(2, 0, 0, 0, 0, 4, 5);
    world_column(3, 0, 0, 0, 0, 4, 5);
    world_column(4, 0, 0, 0, 0, 4, 5);
    world_column(5, 5, 0, 0, 0, 4, 5);
    world_column(6, 5, 0, 0, 0, 4, 5);
    world_column(7, 5, 0, 0, 0, 3, 5);
    world_column(8, 5, 0, 2, 0, 3, 5);
    world_column(9, 0, 0, 2, 0, 4, 5);
    world_column(10, 0, 0, 2, 0, 4, 5);
    world_column(11, 0, 0, 0, 0, 4, 5);
    world_column(12, 0, 0, 0, 0, 3, 5);
    world_column(13, 0, 0, 0, 0, 3, 5);
    world_column(14, 0, 0, 0, 0, 0, 0);
    world_column(15, 0, 0, 0, 0, 0, 0);
    world_flags(0, 0, 0, 0, 0, 1, 1);
    world_flags(1, 0, 0, 0, 0, 1, 1);
    world_flags(2, 0, 0, 0, 0, 1, 1);
    world_flags(3, 0, 0, 0, 0, 1, 1);
    world_flags(4, 0, 0, 0, 0, 1, 1);
    world_flags(5, 1, 0, 0, 0, 1, 1);
    world_flags(6, 1, 0, 0, 0, 1, 1);
    world_flags(7, 1, 0, 0, 0, 2, 1);
    world_flags(8, 1, 0, 0, 0, 2, 1);
    world_flags(9, 0, 0, 0, 0, 1, 1);
    world_flags(10, 0, 0, 0, 0, 1, 1);
    world_flags(11, 0, 0, 0, 0, 1, 1);
    world_flags(12, 0, 0, 0, 0, 2, 1);
    world_flags(13, 0, 0, 0, 0, 2, 1);
    world_flags(14, 0, 0, 0, 0, 0, 0);
    world_flags(15, 0, 0, 0, 0, 0, 0);
    return;
}

void main() {
    setup_video();
    draw_background();
    define_world();
    world_map(16, 9, 6);

    while (true) {
        video_wait_vblank();
    }
}
