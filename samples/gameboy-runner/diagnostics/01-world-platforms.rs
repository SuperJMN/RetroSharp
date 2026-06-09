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

void define_world() {
    world.Column(0, 0, 0, 2, 0, 4, 5);
    world.Column(1, 0, 0, 2, 0, 4, 5);
    world.Column(2, 0, 0, 0, 0, 4, 5);
    world.Column(3, 0, 0, 0, 0, 4, 5);
    world.Column(4, 0, 0, 0, 0, 4, 5);
    world.Column(5, 5, 0, 0, 0, 4, 5);
    world.Column(6, 5, 0, 0, 0, 4, 5);
    world.Column(7, 5, 0, 0, 0, 3, 5);
    world.Column(8, 5, 0, 2, 0, 3, 5);
    world.Column(9, 0, 0, 2, 0, 4, 5);
    world.Column(10, 0, 0, 2, 0, 4, 5);
    world.Column(11, 0, 0, 0, 0, 4, 5);
    world.Column(12, 0, 0, 0, 0, 3, 5);
    world.Column(13, 0, 0, 0, 0, 3, 5);
    world.Column(14, 0, 0, 0, 0, 0, 0);
    world.Column(15, 0, 0, 0, 0, 0, 0);
    world.Flags(0, 0, 0, 0, 0, 1, 1);
    world.Flags(1, 0, 0, 0, 0, 1, 1);
    world.Flags(2, 0, 0, 0, 0, 1, 1);
    world.Flags(3, 0, 0, 0, 0, 1, 1);
    world.Flags(4, 0, 0, 0, 0, 1, 1);
    world.Flags(5, 1, 0, 0, 0, 1, 1);
    world.Flags(6, 1, 0, 0, 0, 1, 1);
    world.Flags(7, 1, 0, 0, 0, 2, 1);
    world.Flags(8, 1, 0, 0, 0, 2, 1);
    world.Flags(9, 0, 0, 0, 0, 1, 1);
    world.Flags(10, 0, 0, 0, 0, 1, 1);
    world.Flags(11, 0, 0, 0, 0, 1, 1);
    world.Flags(12, 0, 0, 0, 0, 2, 1);
    world.Flags(13, 0, 0, 0, 0, 2, 1);
    world.Flags(14, 0, 0, 0, 0, 0, 0);
    world.Flags(15, 0, 0, 0, 0, 0, 0);
    return;
}

void main() {
    setup_video();
    draw_background();
    define_world();
    world.Map(16, 9, 6);

    loop {
        video.WaitVBlank();
    }
}
