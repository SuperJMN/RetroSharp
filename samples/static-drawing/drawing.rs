import RetroSharp.Portable2D;

const MapWidth = 32;

enum Tile
{
    Empty = 0,
    Face = 1,
    Eye = 2,
    Mouth = 3,
    Platform = 4,
    PlatformEdge = 5,
}

[target("gb")]
void SetupVideo()
{
    Video.Init();
    Palette.Set(0, Tile.Empty);
    Palette.Set(1, Tile.Face);
    Palette.Set(2, Tile.Eye);
    Palette.Set(3, Tile.Mouth);
    return;
}

[target("nes")]
void SetupVideo()
{
    Video.Init();
    Palette.Set(0, 15);
    Palette.Set(1, 39);
    Palette.Set(2, 22);
    Palette.Set(3, 48);
    return;
}

[target("gb")]
void ClearBackground()
{
    Tilemap.Fill(0, 0, MapWidth, 32, Tile.Empty);
    return;
}

[target("nes")]
void ClearBackground()
{
    Tilemap.Fill(0, 0, MapWidth, 30, Tile.Empty);
    return;
}

[target("gb")]
void DrawFace()
{
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

[target("nes")]
void DrawFace()
{
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

[target("gb")]
void DrawPlatform()
{
    Tilemap.Set(7, 12, Tile.PlatformEdge);
    Tilemap.Set(8, 12, Tile.Platform);
    Tilemap.Set(9, 12, Tile.Platform);
    Tilemap.Set(10, 12, Tile.Platform);
    Tilemap.Set(11, 12, Tile.Platform);
    Tilemap.Set(12, 12, Tile.PlatformEdge);
    return;
}

[target("nes")]
void DrawPlatform()
{
    Tilemap.Set(13, 18, Tile.PlatformEdge);
    Tilemap.Set(14, 18, Tile.Platform);
    Tilemap.Set(15, 18, Tile.Platform);
    Tilemap.Set(16, 18, Tile.Platform);
    Tilemap.Set(17, 18, Tile.Platform);
    Tilemap.Set(18, 18, Tile.PlatformEdge);
    return;
}

void Main()
{
    SetupVideo();
    ClearBackground();
    DrawFace();
    DrawPlatform();
    Video.Present();
    return;
}
