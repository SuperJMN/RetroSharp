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

void main() {
    setup_video();
    draw_background();

    while (true) {
        video_wait_vblank();
    }
}
