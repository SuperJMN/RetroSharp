enum HudTile { Blank = 0, One = 1, Two = 2, Three = 3, Icon = 5 }

void main() {
    Video.Init();
    Palette.Set(0, HudTile.Blank);
    Palette.Set(1, HudTile.One);
    Palette.Set(2, HudTile.Two);
    Palette.Set(3, HudTile.Three);

    Hud.SetTile(window, 0, 0, HudTile.Icon);
    Hud.SetTile(window, 1, 0, HudTile.One);
    Hud.SetTile(window, 2, 0, HudTile.Two);
    Hud.SetTile(window, 3, 0, HudTile.Three);

    loop {
        Video.WaitVBlank();
    }
}
