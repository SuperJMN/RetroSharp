void setup_video() {
    video_init();
    palette_set(0, 15);
    palette_set(1, 39);
    palette_set(2, 22);
    palette_set(3, 48);
    return;
}

void clear_background() {
    tilemap_fill(0, 0, 32, 30, 0);
    return;
}

void draw_face() {
    tilemap_set(13, 10, 1);
    tilemap_set(14, 10, 1);
    tilemap_set(15, 10, 1);
    tilemap_set(16, 10, 1);
    tilemap_set(17, 10, 1);
    tilemap_set(18, 10, 1);

    tilemap_set(12, 11, 1);
    tilemap_set(13, 11, 1);
    tilemap_set(14, 11, 2);
    tilemap_set(15, 11, 1);
    tilemap_set(16, 11, 1);
    tilemap_set(17, 11, 2);
    tilemap_set(18, 11, 1);
    tilemap_set(19, 11, 1);

    tilemap_set(12, 12, 1);
    tilemap_set(13, 12, 1);
    tilemap_set(14, 12, 2);
    tilemap_set(15, 12, 1);
    tilemap_set(16, 12, 1);
    tilemap_set(17, 12, 2);
    tilemap_set(18, 12, 1);
    tilemap_set(19, 12, 1);

    tilemap_set(12, 13, 1);
    tilemap_set(13, 13, 1);
    tilemap_set(14, 13, 1);
    tilemap_set(15, 13, 1);
    tilemap_set(16, 13, 1);
    tilemap_set(17, 13, 1);
    tilemap_set(18, 13, 1);
    tilemap_set(19, 13, 1);

    tilemap_set(13, 14, 1);
    tilemap_set(14, 14, 3);
    tilemap_set(15, 14, 1);
    tilemap_set(16, 14, 1);
    tilemap_set(17, 14, 3);
    tilemap_set(18, 14, 1);

    tilemap_set(14, 15, 1);
    tilemap_set(15, 15, 3);
    tilemap_set(16, 15, 3);
    tilemap_set(17, 15, 1);
    return;
}

void draw_platform() {
    tilemap_set(13, 18, 5);
    tilemap_set(14, 18, 4);
    tilemap_set(15, 18, 4);
    tilemap_set(16, 18, 4);
    tilemap_set(17, 18, 4);
    tilemap_set(18, 18, 5);
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
