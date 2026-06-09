enum HudTile { Blank = 0, One = 1, Two = 2, Three = 3, Icon = 5 }

void main() {
    video.Init();
    palette.Set(0, HudTile.Blank);
    palette.Set(1, HudTile.One);
    palette.Set(2, HudTile.Two);
    palette.Set(3, HudTile.Three);

    hud.SetTile(window, 0, 0, HudTile.Icon);
    hud.SetTile(window, 1, 0, HudTile.One);
    hud.SetTile(window, 2, 0, HudTile.Two);
    hud.SetTile(window, 3, 0, HudTile.Three);

    loop {
        video.WaitVBlank();
    }
}
