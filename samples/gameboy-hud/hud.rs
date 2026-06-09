enum HudTile { Blank = 0, One = 1, Two = 2, Three = 3, Icon = 5 }

void main() {
    video_init();
    palette_set(0, HudTile.Blank);
    palette_set(1, HudTile.One);
    palette_set(2, HudTile.Two);
    palette_set(3, HudTile.Three);

    hud_set_tile(window, 0, 0, HudTile.Icon);
    hud_set_tile(window, 1, 0, HudTile.One);
    hud_set_tile(window, 2, 0, HudTile.Two);
    hud_set_tile(window, 3, 0, HudTile.Three);

    loop {
        video_wait_vblank();
    }
}
