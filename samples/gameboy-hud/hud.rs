void main() {
    video_init();
    palette_set(0, 0);
    palette_set(1, 1);
    palette_set(2, 2);
    palette_set(3, 3);

    hud_set_tile(window, 0, 0, 5);
    hud_set_tile(window, 1, 0, 1);
    hud_set_tile(window, 2, 0, 2);
    hud_set_tile(window, 3, 0, 3);

    while (true) {
        video_wait_vblank();
    }
}
