const MapWidth = 32;
const MapHeight = 32;

enum Tile { Empty = 0, Face = 1, Eye = 2, Mouth = 3, Platform = 4, PlatformEdge = 5 }

void setup_video() {
    video.Init();
    palette.Set(0, Tile.Empty);
    palette.Set(1, Tile.Face);
    palette.Set(2, Tile.Eye);
    palette.Set(3, Tile.Mouth);
    return;
}

void clear_background() {
    tilemap.Fill(0, 0, MapWidth, MapHeight, Tile.Empty);
    return;
}

void draw_face() {
    tilemap.Set(7, 4, Tile.Face);
    tilemap.Set(8, 4, Tile.Face);
    tilemap.Set(9, 4, Tile.Face);
    tilemap.Set(10, 4, Tile.Face);
    tilemap.Set(11, 4, Tile.Face);
    tilemap.Set(12, 4, Tile.Face);

    tilemap.Set(6, 5, Tile.Face);
    tilemap.Set(7, 5, Tile.Face);
    tilemap.Set(8, 5, Tile.Eye);
    tilemap.Set(9, 5, Tile.Face);
    tilemap.Set(10, 5, Tile.Face);
    tilemap.Set(11, 5, Tile.Eye);
    tilemap.Set(12, 5, Tile.Face);
    tilemap.Set(13, 5, Tile.Face);

    tilemap.Set(6, 6, Tile.Face);
    tilemap.Set(7, 6, Tile.Face);
    tilemap.Set(8, 6, Tile.Eye);
    tilemap.Set(9, 6, Tile.Face);
    tilemap.Set(10, 6, Tile.Face);
    tilemap.Set(11, 6, Tile.Eye);
    tilemap.Set(12, 6, Tile.Face);
    tilemap.Set(13, 6, Tile.Face);

    tilemap.Set(6, 7, Tile.Face);
    tilemap.Set(7, 7, Tile.Face);
    tilemap.Set(8, 7, Tile.Face);
    tilemap.Set(9, 7, Tile.Face);
    tilemap.Set(10, 7, Tile.Face);
    tilemap.Set(11, 7, Tile.Face);
    tilemap.Set(12, 7, Tile.Face);
    tilemap.Set(13, 7, Tile.Face);

    tilemap.Set(7, 8, Tile.Face);
    tilemap.Set(8, 8, Tile.Mouth);
    tilemap.Set(9, 8, Tile.Face);
    tilemap.Set(10, 8, Tile.Face);
    tilemap.Set(11, 8, Tile.Mouth);
    tilemap.Set(12, 8, Tile.Face);

    tilemap.Set(8, 9, Tile.Face);
    tilemap.Set(9, 9, Tile.Mouth);
    tilemap.Set(10, 9, Tile.Mouth);
    tilemap.Set(11, 9, Tile.Face);
    return;
}

void draw_platform() {
    tilemap.Set(7, 12, Tile.PlatformEdge);
    tilemap.Set(8, 12, Tile.Platform);
    tilemap.Set(9, 12, Tile.Platform);
    tilemap.Set(10, 12, Tile.Platform);
    tilemap.Set(11, 12, Tile.Platform);
    tilemap.Set(12, 12, Tile.PlatformEdge);
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
