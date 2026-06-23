void setup_video() {
    video.Init();
    palette.Set(0, 0);
    palette.Set(1, 1);
    palette.Set(2, 2);
    palette.Set(3, 3);
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

void main() {
    setup_video();
    draw_background();

    loop {
        video.WaitVBlank();
    }
}
