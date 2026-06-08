void setup_video() {
    video_init();
    palette_set(0, 0);
    palette_set(1, 1);
    palette_set(2, 2);
    palette_set(3, 3);
    return;
}

void clear_background() {
    tilemap_fill(0, 0, 32, 32, 0);
    return;
}

void draw_face() {
    tilemap_set(7, 4, 1);
    tilemap_set(8, 4, 1);
    tilemap_set(9, 4, 1);
    tilemap_set(10, 4, 1);
    tilemap_set(11, 4, 1);
    tilemap_set(12, 4, 1);

    tilemap_set(6, 5, 1);
    tilemap_set(7, 5, 1);
    tilemap_set(8, 5, 2);
    tilemap_set(9, 5, 1);
    tilemap_set(10, 5, 1);
    tilemap_set(11, 5, 2);
    tilemap_set(12, 5, 1);
    tilemap_set(13, 5, 1);

    tilemap_set(6, 6, 1);
    tilemap_set(7, 6, 1);
    tilemap_set(8, 6, 2);
    tilemap_set(9, 6, 1);
    tilemap_set(10, 6, 1);
    tilemap_set(11, 6, 2);
    tilemap_set(12, 6, 1);
    tilemap_set(13, 6, 1);

    tilemap_set(6, 7, 1);
    tilemap_set(7, 7, 1);
    tilemap_set(8, 7, 1);
    tilemap_set(9, 7, 1);
    tilemap_set(10, 7, 1);
    tilemap_set(11, 7, 1);
    tilemap_set(12, 7, 1);
    tilemap_set(13, 7, 1);

    tilemap_set(7, 8, 1);
    tilemap_set(8, 8, 3);
    tilemap_set(9, 8, 1);
    tilemap_set(10, 8, 1);
    tilemap_set(11, 8, 3);
    tilemap_set(12, 8, 1);

    tilemap_set(8, 9, 1);
    tilemap_set(9, 9, 3);
    tilemap_set(10, 9, 3);
    tilemap_set(11, 9, 1);
    return;
}

void draw_platform() {
    tilemap_set(7, 12, 5);
    tilemap_set(8, 12, 4);
    tilemap_set(9, 12, 4);
    tilemap_set(10, 12, 4);
    tilemap_set(11, 12, 4);
    tilemap_set(12, 12, 5);
    return;
}

void main() {
    setup_video();
    clear_background();
    draw_face();
    draw_platform();
    video_present();
    return;
}
