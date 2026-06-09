const MapWidth = 32;
const MapHeight = 30;

enum Tile { Empty = 0, Face = 1, Eye = 2, Mouth = 3, Platform = 4, PlatformEdge = 5 }

void setup_video() {
    video.Init();
    palette.Set(0, 15);
    palette.Set(1, 39);
    palette.Set(2, 22);
    palette.Set(3, 48);
    return;
}

void clear_background() {
    tilemap.Fill(0, 0, MapWidth, MapHeight, Tile.Empty);
    return;
}

void draw_face() {
    tilemap.Set(13, 10, Tile.Face);
    tilemap.Set(14, 10, Tile.Face);
    tilemap.Set(15, 10, Tile.Face);
    tilemap.Set(16, 10, Tile.Face);
    tilemap.Set(17, 10, Tile.Face);
    tilemap.Set(18, 10, Tile.Face);

    tilemap.Set(12, 11, Tile.Face);
    tilemap.Set(13, 11, Tile.Face);
    tilemap.Set(14, 11, Tile.Eye);
    tilemap.Set(15, 11, Tile.Face);
    tilemap.Set(16, 11, Tile.Face);
    tilemap.Set(17, 11, Tile.Eye);
    tilemap.Set(18, 11, Tile.Face);
    tilemap.Set(19, 11, Tile.Face);

    tilemap.Set(12, 12, Tile.Face);
    tilemap.Set(13, 12, Tile.Face);
    tilemap.Set(14, 12, Tile.Eye);
    tilemap.Set(15, 12, Tile.Face);
    tilemap.Set(16, 12, Tile.Face);
    tilemap.Set(17, 12, Tile.Eye);
    tilemap.Set(18, 12, Tile.Face);
    tilemap.Set(19, 12, Tile.Face);

    tilemap.Set(12, 13, Tile.Face);
    tilemap.Set(13, 13, Tile.Face);
    tilemap.Set(14, 13, Tile.Face);
    tilemap.Set(15, 13, Tile.Face);
    tilemap.Set(16, 13, Tile.Face);
    tilemap.Set(17, 13, Tile.Face);
    tilemap.Set(18, 13, Tile.Face);
    tilemap.Set(19, 13, Tile.Face);

    tilemap.Set(13, 14, Tile.Face);
    tilemap.Set(14, 14, Tile.Mouth);
    tilemap.Set(15, 14, Tile.Face);
    tilemap.Set(16, 14, Tile.Face);
    tilemap.Set(17, 14, Tile.Mouth);
    tilemap.Set(18, 14, Tile.Face);

    tilemap.Set(14, 15, Tile.Face);
    tilemap.Set(15, 15, Tile.Mouth);
    tilemap.Set(16, 15, Tile.Mouth);
    tilemap.Set(17, 15, Tile.Face);
    return;
}

void draw_platform() {
    tilemap.Set(13, 18, Tile.PlatformEdge);
    tilemap.Set(14, 18, Tile.Platform);
    tilemap.Set(15, 18, Tile.Platform);
    tilemap.Set(16, 18, Tile.Platform);
    tilemap.Set(17, 18, Tile.Platform);
    tilemap.Set(18, 18, Tile.PlatformEdge);
    return;
}

void main() {
    setup_video();
    clear_background();
    draw_face();
    draw_platform();
    video.Present();
    return;
}
