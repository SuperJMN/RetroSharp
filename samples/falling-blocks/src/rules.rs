static class Board
{
    const u8 Width = 10;
    const u8 Height = 16;
    const u8 OriginX = 1;
    const u8 LeftWallX = 0;
    const u8 RightWallX = Board.OriginX + Board.Width;
    const u8 MeterX = 18;
    const u8 CellCount = Board.Width * Board.Height;
    const u8 RedrawBatchSize = 5;
    const u8 NoRedraw = 255;

    static inline pure u8 Index(u8 x, u8 y) =>
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
    const u8 CellCount = 4;
    const u8 Count = 7;

    static inline pure u8 X(u8 shapeOffset, u8 rotation, u8 cell, u8 code) =>
        shapeOffset == 0
            ? (rotation == 0 || rotation == 2 ? cell : 0)
            : shapeOffset == 4
                ? (code & 3) - 1
                : rotation == 0
                    ? code & 3
                : rotation == 1
                    ? 1 - (code < 4 ? 0 : 1)
                    : rotation == 2
                        ? 2 - (code & 3)
                        : (code < 4 ? 0 : 1);

    static inline pure u8 Y(u8 shapeOffset, u8 rotation, u8 cell, u8 code) =>
        shapeOffset == 0
            ? (rotation == 0 ? 1 : rotation == 2 ? 2 : cell)
            : shapeOffset == 4 || rotation == 0
                ? (code < 4 ? 0 : 1)
                : rotation == 1
                    ? code & 3
                    : rotation == 2
                        ? 2 - (code < 4 ? 0 : 1)
                        : 2 - (code & 3);
}

class PiecePose
{
    u8 shapeOffset;
    u8 rotation;
    u8 x;
    u8 y;

    inline void Spawn(u8 kind)
    {
        shapeOffset = kind;
        shapeOffset += shapeOffset;
        shapeOffset += shapeOffset;
        rotation = 0;
        x = 3;
        y = 0;
    }

    inline void CopyFrom(PiecePose source)
    {
        shapeOffset = source.shapeOffset;
        rotation = source.rotation;
        x = source.x;
        y = source.y;
    }

    inline void RotateClockwise()
    {
        rotation++;
        if (rotation == 4)
        {
            rotation = 0;
        }
    }

    inline void RotateCounterClockwise()
    {
        rotation = rotation switch
        {
            0 => 3,
            _ => rotation - 1
        };
    }

    inline pure u8 CodeIndex(u8 cell) => shapeOffset + cell;
}

class CellPosition
{
    u8 x;
    u8 y;

    inline void Locate(PiecePose piece, u8 cell, u8 code)
    {
        x = piece.x + Pieces.X(piece.shapeOffset, piece.rotation, cell, code);
        y = piece.y + Pieces.Y(piece.shapeOffset, piece.rotation, cell, code);
    }

    inline void Present(
        PiecePose piece,
        u8 cell,
        u8 code,
        u8 originX,
        u8 originY,
        bool hidden)
    {
        Locate(piece, cell, code);
        x += originX;
        y += originY;
        ScaleToPixels(hidden);
    }

    inline void PresentPreview(u8 shapeOffset, u8 cell, u8 code, bool hidden)
    {
        x = Preview.OriginX + Pieces.X(shapeOffset, 0, cell, code);
        if (shapeOffset != 0)
        {
            x++;
        }

        y = Preview.OriginY + Pieces.Y(shapeOffset, 0, cell, code);
        ScaleToPixels(hidden);
    }

    inline void ScaleToPixels(bool hidden)
    {
        x += x;
        x += x;
        x += x;
        y += y;
        y += y;
        y += y;
        if (hidden)
        {
            y = SpriteLayout.HiddenY;
        }
    }
}

class GameState
{
    u8 fallCounter;
    u8 fallDelay;
    u8 horizontalCooldown;
    u8 lineCount;
    bool gameOver;

    inline void Reset()
    {
        fallCounter = 0;
        fallDelay = Timing.InitialFallDelay;
        horizontalCooldown = 0;
        lineCount = 0;
        gameOver = false;
    }

    inline void RegisterClearedLine()
    {
        if (lineCount < Board.Height)
        {
            lineCount++;
        }

        if (fallDelay > Timing.MinimumFallDelay)
        {
            fallDelay--;
        }
    }
}
