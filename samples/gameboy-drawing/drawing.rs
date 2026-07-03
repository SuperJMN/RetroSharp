import RetroSharp.Portable2D;

const MapWidth = 32;
const MapHeight = 32;

enum Tile { Empty = 0, Face = 1, Eye = 2, Mouth = 3, Platform = 4, PlatformEdge = 5 }

void SetupVideo() {
    Video.Init();
    Palette.Set(0, Tile.Empty);
    Palette.Set(1, Tile.Face);
    Palette.Set(2, Tile.Eye);
    Palette.Set(3, Tile.Mouth);
    return;
}

void ClearBackground() {
    Tilemap.Fill(0, 0, MapWidth, MapHeight, Tile.Empty);
    return;
}

void DrawFace() {
    Tilemap.Set(7, 4, Tile.Face);
    Tilemap.Set(8, 4, Tile.Face);
    Tilemap.Set(9, 4, Tile.Face);
    Tilemap.Set(10, 4, Tile.Face);
    Tilemap.Set(11, 4, Tile.Face);
    Tilemap.Set(12, 4, Tile.Face);

    Tilemap.Set(6, 5, Tile.Face);
    Tilemap.Set(7, 5, Tile.Face);
    Tilemap.Set(8, 5, Tile.Eye);
    Tilemap.Set(9, 5, Tile.Face);
    Tilemap.Set(10, 5, Tile.Face);
    Tilemap.Set(11, 5, Tile.Eye);
    Tilemap.Set(12, 5, Tile.Face);
    Tilemap.Set(13, 5, Tile.Face);

    Tilemap.Set(6, 6, Tile.Face);
    Tilemap.Set(7, 6, Tile.Face);
    Tilemap.Set(8, 6, Tile.Eye);
    Tilemap.Set(9, 6, Tile.Face);
    Tilemap.Set(10, 6, Tile.Face);
    Tilemap.Set(11, 6, Tile.Eye);
    Tilemap.Set(12, 6, Tile.Face);
    Tilemap.Set(13, 6, Tile.Face);

    Tilemap.Set(6, 7, Tile.Face);
    Tilemap.Set(7, 7, Tile.Face);
    Tilemap.Set(8, 7, Tile.Face);
    Tilemap.Set(9, 7, Tile.Face);
    Tilemap.Set(10, 7, Tile.Face);
    Tilemap.Set(11, 7, Tile.Face);
    Tilemap.Set(12, 7, Tile.Face);
    Tilemap.Set(13, 7, Tile.Face);

    Tilemap.Set(7, 8, Tile.Face);
    Tilemap.Set(8, 8, Tile.Mouth);
    Tilemap.Set(9, 8, Tile.Face);
    Tilemap.Set(10, 8, Tile.Face);
    Tilemap.Set(11, 8, Tile.Mouth);
    Tilemap.Set(12, 8, Tile.Face);

    Tilemap.Set(8, 9, Tile.Face);
    Tilemap.Set(9, 9, Tile.Mouth);
    Tilemap.Set(10, 9, Tile.Mouth);
    Tilemap.Set(11, 9, Tile.Face);
    return;
}

void DrawPlatform() {
    Tilemap.Set(7, 12, Tile.PlatformEdge);
    Tilemap.Set(8, 12, Tile.Platform);
    Tilemap.Set(9, 12, Tile.Platform);
    Tilemap.Set(10, 12, Tile.Platform);
    Tilemap.Set(11, 12, Tile.Platform);
    Tilemap.Set(12, 12, Tile.PlatformEdge);
    return;
}

void Main() {
    SetupVideo();
    ClearBackground();
    DrawFace();
    DrawPlatform();
    Video.Present();
    return;
}
