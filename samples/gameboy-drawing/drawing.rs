const MapWidth = 32;
const MapHeight = 32;

enum Tile { Empty = 0, Face = 1, Eye = 2, Mouth = 3, Platform = 4, PlatformEdge = 5 }

void setup_video() {
    video_init();
    palette_set(0, Tile.Empty);
    palette_set(1, Tile.Face);
    palette_set(2, Tile.Eye);
    palette_set(3, Tile.Mouth);
    return;
}

void clear_background() {
    tilemap_fill(0, 0, MapWidth, MapHeight, Tile.Empty);
    return;
}

void draw_face() {
    tilemap_set(7, 4, Tile.Face);
    tilemap_set(8, 4, Tile.Face);
    tilemap_set(9, 4, Tile.Face);
    tilemap_set(10, 4, Tile.Face);
    tilemap_set(11, 4, Tile.Face);
    tilemap_set(12, 4, Tile.Face);

    tilemap_set(6, 5, Tile.Face);
    tilemap_set(7, 5, Tile.Face);
    tilemap_set(8, 5, Tile.Eye);
    tilemap_set(9, 5, Tile.Face);
    tilemap_set(10, 5, Tile.Face);
    tilemap_set(11, 5, Tile.Eye);
    tilemap_set(12, 5, Tile.Face);
    tilemap_set(13, 5, Tile.Face);

    tilemap_set(6, 6, Tile.Face);
    tilemap_set(7, 6, Tile.Face);
    tilemap_set(8, 6, Tile.Eye);
    tilemap_set(9, 6, Tile.Face);
    tilemap_set(10, 6, Tile.Face);
    tilemap_set(11, 6, Tile.Eye);
    tilemap_set(12, 6, Tile.Face);
    tilemap_set(13, 6, Tile.Face);

    tilemap_set(6, 7, Tile.Face);
    tilemap_set(7, 7, Tile.Face);
    tilemap_set(8, 7, Tile.Face);
    tilemap_set(9, 7, Tile.Face);
    tilemap_set(10, 7, Tile.Face);
    tilemap_set(11, 7, Tile.Face);
    tilemap_set(12, 7, Tile.Face);
    tilemap_set(13, 7, Tile.Face);

    tilemap_set(7, 8, Tile.Face);
    tilemap_set(8, 8, Tile.Mouth);
    tilemap_set(9, 8, Tile.Face);
    tilemap_set(10, 8, Tile.Face);
    tilemap_set(11, 8, Tile.Mouth);
    tilemap_set(12, 8, Tile.Face);

    tilemap_set(8, 9, Tile.Face);
    tilemap_set(9, 9, Tile.Mouth);
    tilemap_set(10, 9, Tile.Mouth);
    tilemap_set(11, 9, Tile.Face);
    return;
}

void draw_platform() {
    tilemap_set(7, 12, Tile.PlatformEdge);
    tilemap_set(8, 12, Tile.Platform);
    tilemap_set(9, 12, Tile.Platform);
    tilemap_set(10, 12, Tile.Platform);
    tilemap_set(11, 12, Tile.Platform);
    tilemap_set(12, 12, Tile.PlatformEdge);
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
