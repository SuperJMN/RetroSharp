const MapWidth = 32;
const MapHeight = 30;

enum Tile { Empty = 0, Face = 1, Eye = 2, Mouth = 3, Platform = 4, PlatformEdge = 5 }

void setup_video() {
    video_init();
    palette_set(0, 15);
    palette_set(1, 39);
    palette_set(2, 22);
    palette_set(3, 48);
    return;
}

void clear_background() {
    tilemap_fill(0, 0, MapWidth, MapHeight, Tile.Empty);
    return;
}

void draw_face() {
    tilemap_set(13, 10, Tile.Face);
    tilemap_set(14, 10, Tile.Face);
    tilemap_set(15, 10, Tile.Face);
    tilemap_set(16, 10, Tile.Face);
    tilemap_set(17, 10, Tile.Face);
    tilemap_set(18, 10, Tile.Face);

    tilemap_set(12, 11, Tile.Face);
    tilemap_set(13, 11, Tile.Face);
    tilemap_set(14, 11, Tile.Eye);
    tilemap_set(15, 11, Tile.Face);
    tilemap_set(16, 11, Tile.Face);
    tilemap_set(17, 11, Tile.Eye);
    tilemap_set(18, 11, Tile.Face);
    tilemap_set(19, 11, Tile.Face);

    tilemap_set(12, 12, Tile.Face);
    tilemap_set(13, 12, Tile.Face);
    tilemap_set(14, 12, Tile.Eye);
    tilemap_set(15, 12, Tile.Face);
    tilemap_set(16, 12, Tile.Face);
    tilemap_set(17, 12, Tile.Eye);
    tilemap_set(18, 12, Tile.Face);
    tilemap_set(19, 12, Tile.Face);

    tilemap_set(12, 13, Tile.Face);
    tilemap_set(13, 13, Tile.Face);
    tilemap_set(14, 13, Tile.Face);
    tilemap_set(15, 13, Tile.Face);
    tilemap_set(16, 13, Tile.Face);
    tilemap_set(17, 13, Tile.Face);
    tilemap_set(18, 13, Tile.Face);
    tilemap_set(19, 13, Tile.Face);

    tilemap_set(13, 14, Tile.Face);
    tilemap_set(14, 14, Tile.Mouth);
    tilemap_set(15, 14, Tile.Face);
    tilemap_set(16, 14, Tile.Face);
    tilemap_set(17, 14, Tile.Mouth);
    tilemap_set(18, 14, Tile.Face);

    tilemap_set(14, 15, Tile.Face);
    tilemap_set(15, 15, Tile.Mouth);
    tilemap_set(16, 15, Tile.Mouth);
    tilemap_set(17, 15, Tile.Face);
    return;
}

void draw_platform() {
    tilemap_set(13, 18, Tile.PlatformEdge);
    tilemap_set(14, 18, Tile.Platform);
    tilemap_set(15, 18, Tile.Platform);
    tilemap_set(16, 18, Tile.Platform);
    tilemap_set(17, 18, Tile.Platform);
    tilemap_set(18, 18, Tile.PlatformEdge);
    return;
}

void main() {
    setup_video();
    clear_background();
    draw_face();
    draw_platform();
    video_present();
    return;
}
