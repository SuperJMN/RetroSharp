import RetroSharp.Portable2D;

void SetupVideo() {
    Video.Init();
    Palette.Set(0, 0);
    Palette.Set(1, 1);
    Palette.Set(2, 2);
    Palette.Set(3, 3);
    ObjectPalette.Set(0, 0);
    ObjectPalette.Set(1, 0);
    ObjectPalette.Set(2, 1);
    ObjectPalette.Set(3, 3);
    Sprite.Asset(enemy_slug, "../assets/enemy-slug.gb.png", 16, 16);
    Animation.Clip(enemy_walk, 0, 12, 12);
    return;
}

void DrawBackground() {
    Tilemap.Set(2, 4, 1);
    Tilemap.Set(3, 4, 1);
    Tilemap.Set(4, 5, 1);
    Tilemap.Set(10, 7, 2);
    Tilemap.Set(11, 8, 2);
    return;
}

void Main() {
    SetupVideo();
    DrawBackground();
    i16 enemyX = 128;
    i16 enemyFrame = 0;
    i16 enemyTick = 0;

    loop {
        Video.WaitVBlank();
        Sprite.Draw(enemy_slug, enemyX, 89, enemyFrame, false, 0);
        Sprite.Draw(enemy_slug, 40, 57, enemyFrame, true, 0);
        enemyX--;
        if (enemyX <= 96) {
            enemyX = 136;
        }
        enemyTick++;
        enemyFrame = Animation.Frame(enemy_walk, enemyTick);
    }
}
