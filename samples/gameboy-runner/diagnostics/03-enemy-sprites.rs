void setup_video() {
    video_init();
    palette_set(0, 0);
    palette_set(1, 1);
    palette_set(2, 2);
    palette_set(3, 3);
    object_palette_set(0, 0);
    object_palette_set(1, 0);
    object_palette_set(2, 1);
    object_palette_set(3, 3);
    sprite_asset(enemy_slug, "../assets/enemy-slug.gb.png", 16, 16);
    animation_clip(enemy_walk, 0, 12, 12);
    return;
}

void draw_background() {
    tilemap_set(2, 4, 1);
    tilemap_set(3, 4, 1);
    tilemap_set(4, 5, 1);
    tilemap_set(10, 7, 2);
    tilemap_set(11, 8, 2);
    return;
}

void main() {
    setup_video();
    draw_background();
    i16 enemyX = 128;
    i16 enemyFrame = 0;
    i16 enemyTick = 0;

    loop {
        video_wait_vblank();
        sprite_draw(enemy_slug, enemyX, 89, enemyFrame, false, 0);
        sprite_draw(enemy_slug, 40, 57, enemyFrame, true, 0);
        enemyX--;
        if (enemyX <= 96) {
            enemyX = 136;
        }
        enemyTick++;
        enemyFrame = animation_frame(enemy_walk, enemyTick);
    }
}
