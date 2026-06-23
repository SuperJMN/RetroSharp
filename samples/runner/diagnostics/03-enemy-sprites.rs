void setup_video() {
    video.Init();
    palette.Set(0, 0);
    palette.Set(1, 1);
    palette.Set(2, 2);
    palette.Set(3, 3);
    objectPalette.Set(0, 0);
    objectPalette.Set(1, 0);
    objectPalette.Set(2, 1);
    objectPalette.Set(3, 3);
    sprite.Asset(enemy_slug, "../assets/enemy-slug.gb.png", 16, 16);
    animation.Clip(enemy_walk, 0, 12, 12);
    return;
}

void draw_background() {
    tilemap.Set(2, 4, 1);
    tilemap.Set(3, 4, 1);
    tilemap.Set(4, 5, 1);
    tilemap.Set(10, 7, 2);
    tilemap.Set(11, 8, 2);
    return;
}

void main() {
    setup_video();
    draw_background();
    i16 enemyX = 128;
    i16 enemyFrame = 0;
    i16 enemyTick = 0;

    loop {
        video.WaitVBlank();
        sprite.Draw(enemy_slug, enemyX, 89, enemyFrame, false, 0);
        sprite.Draw(enemy_slug, 40, 57, enemyFrame, true, 0);
        enemyX--;
        if (enemyX <= 96) {
            enemyX = 136;
        }
        enemyTick++;
        enemyFrame = animation.Frame(enemy_walk, enemyTick);
    }
}
