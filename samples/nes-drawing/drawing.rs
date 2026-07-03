import RetroSharp.Portable2D;

const MapWidth = 32;
const MapHeight = 30;

enum Tile { Empty = 0, Face = 1, Eye = 2, Mouth = 3, Platform = 4, PlatformEdge = 5 }

void SetupVideo() {
    Video.Init();
    Palette.Set(0, 15);
    Palette.Set(1, 39);
    Palette.Set(2, 22);
    Palette.Set(3, 48);
    return;
}

void ClearBackground() {
    Tilemap.Fill(0, 0, MapWidth, MapHeight, Tile.Empty);
    return;
}

void DrawFace() {
    Tilemap.Set(13, 10, Tile.Face);
    Tilemap.Set(14, 10, Tile.Face);
    Tilemap.Set(15, 10, Tile.Face);
    Tilemap.Set(16, 10, Tile.Face);
    Tilemap.Set(17, 10, Tile.Face);
    Tilemap.Set(18, 10, Tile.Face);

    Tilemap.Set(12, 11, Tile.Face);
    Tilemap.Set(13, 11, Tile.Face);
    Tilemap.Set(14, 11, Tile.Eye);
    Tilemap.Set(15, 11, Tile.Face);
    Tilemap.Set(16, 11, Tile.Face);
    Tilemap.Set(17, 11, Tile.Eye);
    Tilemap.Set(18, 11, Tile.Face);
    Tilemap.Set(19, 11, Tile.Face);

    Tilemap.Set(12, 12, Tile.Face);
    Tilemap.Set(13, 12, Tile.Face);
    Tilemap.Set(14, 12, Tile.Eye);
    Tilemap.Set(15, 12, Tile.Face);
    Tilemap.Set(16, 12, Tile.Face);
    Tilemap.Set(17, 12, Tile.Eye);
    Tilemap.Set(18, 12, Tile.Face);
    Tilemap.Set(19, 12, Tile.Face);

    Tilemap.Set(12, 13, Tile.Face);
    Tilemap.Set(13, 13, Tile.Face);
    Tilemap.Set(14, 13, Tile.Face);
    Tilemap.Set(15, 13, Tile.Face);
    Tilemap.Set(16, 13, Tile.Face);
    Tilemap.Set(17, 13, Tile.Face);
    Tilemap.Set(18, 13, Tile.Face);
    Tilemap.Set(19, 13, Tile.Face);

    Tilemap.Set(13, 14, Tile.Face);
    Tilemap.Set(14, 14, Tile.Mouth);
    Tilemap.Set(15, 14, Tile.Face);
    Tilemap.Set(16, 14, Tile.Face);
    Tilemap.Set(17, 14, Tile.Mouth);
    Tilemap.Set(18, 14, Tile.Face);

    Tilemap.Set(14, 15, Tile.Face);
    Tilemap.Set(15, 15, Tile.Mouth);
    Tilemap.Set(16, 15, Tile.Mouth);
    Tilemap.Set(17, 15, Tile.Face);
    return;
}

void DrawPlatform() {
    Tilemap.Set(13, 18, Tile.PlatformEdge);
    Tilemap.Set(14, 18, Tile.Platform);
    Tilemap.Set(15, 18, Tile.Platform);
    Tilemap.Set(16, 18, Tile.Platform);
    Tilemap.Set(17, 18, Tile.Platform);
    Tilemap.Set(18, 18, Tile.PlatformEdge);
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
