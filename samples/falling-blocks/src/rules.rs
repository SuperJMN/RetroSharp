static class Board
{
    const u8 Width = 10;
    const u8 Height = 16;
    const u8 CellCount = 160;
    const u8 OriginX = 1;
    const u8 MeterX = 18;

    static inline u8 Index(u8 x, u8 y) =>
        y + (y + (y + (y + (y + (y + (y + (y + (y + (y + x)))))))));
}

static class Preview
{
    const u8 OriginX = 13;
    const u8 OriginY = 1;
}

static class SpriteLayout
{
    const u8 HiddenY = 248;
}

static class Timing
{
    const u8 InitialFallDelay = 30;
    const u8 MinimumFallDelay = 6;
    const u8 SoftDropDelay = 2;
    const u8 InitialHorizontalDelay = 8;
    const u8 RepeatHorizontalDelay = 2;
}

enum Tile
{
    Empty = 0,
    Meter = 3,
    Landed = 6,
}

enum Piece
{
    I = 0,
    O = 1,
    T = 2,
    S = 3,
    Z = 4,
    J = 5,
    L = 6,
}

static class Pieces
{
    static inline u8 X(u8 pieceBase, u8 rotation, u8 cell, u8 code) =>
        pieceBase == 0
            ? (rotation == 0 || rotation == 2 ? cell : 0)
            : pieceBase == 4
                ? (code & 3) - 1
                : rotation == 0
                    ? code & 3
                : rotation == 1
                    ? 1 - (code < 4 ? 0 : 1)
                    : rotation == 2
                        ? 2 - (code & 3)
                        : (code < 4 ? 0 : 1);

    static inline u8 Y(u8 pieceBase, u8 rotation, u8 cell, u8 code) =>
        pieceBase == 0
            ? (rotation == 0 ? 1 : rotation == 2 ? 2 : cell)
            : pieceBase == 4 || rotation == 0
                ? (code < 4 ? 0 : 1)
                : rotation == 1
                    ? code & 3
                    : rotation == 2
                        ? 2 - (code < 4 ? 0 : 1)
                        : 2 - (code & 3);
}
