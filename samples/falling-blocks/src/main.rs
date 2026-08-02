void SetupVideo()
{
    Video.Init();
    Palette.Background(0, 0, 1, 2, 3);
    Palette.Sprite(0, 0, 1, 2, 3);
    Sprite.Asset(block, "assets/block.json");
}

void SetupField()
{
    Tilemap.Fill(0, 0, 20, Board.Height, Tile.Empty);

    Tilemap.Set(0, 0, Tile.Landed);
    Tilemap.Set(11, 0, Tile.Landed);
    Tilemap.Set(0, 1, Tile.Landed);
    Tilemap.Set(11, 1, Tile.Landed);
    Tilemap.Set(0, 2, Tile.Landed);
    Tilemap.Set(11, 2, Tile.Landed);
    Tilemap.Set(0, 3, Tile.Landed);
    Tilemap.Set(11, 3, Tile.Landed);
    Tilemap.Set(0, 4, Tile.Landed);
    Tilemap.Set(11, 4, Tile.Landed);
    Tilemap.Set(0, 5, Tile.Landed);
    Tilemap.Set(11, 5, Tile.Landed);
    Tilemap.Set(0, 6, Tile.Landed);
    Tilemap.Set(11, 6, Tile.Landed);
    Tilemap.Set(0, 7, Tile.Landed);
    Tilemap.Set(11, 7, Tile.Landed);
    Tilemap.Set(0, 8, Tile.Landed);
    Tilemap.Set(11, 8, Tile.Landed);
    Tilemap.Set(0, 9, Tile.Landed);
    Tilemap.Set(11, 9, Tile.Landed);
    Tilemap.Set(0, 10, Tile.Landed);
    Tilemap.Set(11, 10, Tile.Landed);
    Tilemap.Set(0, 11, Tile.Landed);
    Tilemap.Set(11, 11, Tile.Landed);
    Tilemap.Set(0, 12, Tile.Landed);
    Tilemap.Set(11, 12, Tile.Landed);
    Tilemap.Set(0, 13, Tile.Landed);
    Tilemap.Set(11, 13, Tile.Landed);
    Tilemap.Set(0, 14, Tile.Landed);
    Tilemap.Set(11, 14, Tile.Landed);
    Tilemap.Set(0, 15, Tile.Landed);
    Tilemap.Set(11, 15, Tile.Landed);
    Tilemap.Set(0, 16, Tile.Landed);
    Tilemap.Set(11, 16, Tile.Landed);
    Tilemap.Set(1, 16, Tile.Landed);
    Tilemap.Set(2, 16, Tile.Landed);
    Tilemap.Set(3, 16, Tile.Landed);
    Tilemap.Set(4, 16, Tile.Landed);
    Tilemap.Set(5, 16, Tile.Landed);
    Tilemap.Set(6, 16, Tile.Landed);
    Tilemap.Set(7, 16, Tile.Landed);
    Tilemap.Set(8, 16, Tile.Landed);
    Tilemap.Set(9, 16, Tile.Landed);
    Tilemap.Set(10, 16, Tile.Landed);
}

void Main()
{
    SetupVideo();
    SetupField();

    u8 board[160];
    u8 shapes[28] = [
        4, 5, 6, 7,
        1, 2, 5, 6,
        4, 5, 6, 1,
        1, 2, 4, 5,
        0, 1, 5, 6,
        0, 4, 5, 6,
        2, 4, 5, 6
    ];
    u8 piece = Piece.T;
    u8 pieceBase = 8;
    u8 nextPiece = Piece.I;
    u8 nextBase = 0;
    u8 rotation = 0;
    u8 pieceX = 3;
    u8 pieceY = 0;
    u8 dirtyBase = 8;
    u8 dirtyRotation = 0;
    u8 dirtyX = 3;
    u8 dirtyY = 0;
    u8 dirtyCell = 4;
    u8 candidateRotation = 0;
    u8 candidateX = 3;
    u8 candidateY = 0;
    u8 cell = 0;
    u8 cellX = 0;
    u8 cellY = 0;
    u8 index = 0;
    u8 copy = 0;
    u8 fallCounter = 0;
    u8 fallDelay = Timing.InitialFallDelay;
    u8 horizontalCooldown = 0;
    u8 row = 0;
    u8 column = 0;
    u8 shiftRow = 0;
    u8 redrawRow = 255;
    u8 lineCount = 0;
    bool blocked = false;
    bool full = false;
    bool lockRequested = false;
    bool gameOver = false;
    bool attempt = false;
    bool fallDue = false;

    while (true)
    {
        Video.WaitVBlank();

        if (redrawRow < Board.Height)
        {
            column = 0;
            index = Board.Index(dirtyCell, redrawRow);
            while (column < 5)
            {
                copy = board[index] == 0 ? Tile.Empty : Tile.Landed;
                Tilemap.Set(
                    Board.OriginX + dirtyCell,
                    redrawRow,
                    copy);
                column += 1;
                dirtyCell += 1;
                index += 1;
            }

            if (dirtyCell == Board.Width)
            {
                Tilemap.Set(
                    Board.MeterX,
                    redrawRow,
                    redrawRow + lineCount >= Board.Height ? Tile.Meter : Tile.Empty);

                dirtyCell = 0;
                redrawRow += 1;
                if (redrawRow == Board.Height)
                {
                    redrawRow = 255;
                    dirtyCell = 4;
                }
            }

            Input.Poll();
            continue;
        }

        if (dirtyCell < 4)
        {
            index = dirtyBase + dirtyCell;
            copy = shapes[index];
            cellX = dirtyX + Pieces.X(dirtyBase, dirtyRotation, dirtyCell, copy);
            cellY = dirtyY + Pieces.Y(dirtyBase, dirtyRotation, dirtyCell, copy);
            Tilemap.Set(Board.OriginX + cellX, cellY, Tile.Landed);
            dirtyCell += 1;
        }

        index = pieceBase;
        copy = shapes[index];
        cellX = pieceX + Pieces.X(pieceBase, rotation, 0, copy);
        cellY = pieceY + Pieces.Y(pieceBase, rotation, 0, copy);
        cellX += Board.OriginX;
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        index = pieceBase + 1;
        copy = shapes[index];
        cellX = pieceX + Pieces.X(pieceBase, rotation, 1, copy);
        cellY = pieceY + Pieces.Y(pieceBase, rotation, 1, copy);
        cellX += Board.OriginX;
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        index = pieceBase + 2;
        copy = shapes[index];
        cellX = pieceX + Pieces.X(pieceBase, rotation, 2, copy);
        cellY = pieceY + Pieces.Y(pieceBase, rotation, 2, copy);
        cellX += Board.OriginX;
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        index = pieceBase + 3;
        copy = shapes[index];
        cellX = pieceX + Pieces.X(pieceBase, rotation, 3, copy);
        cellY = pieceY + Pieces.Y(pieceBase, rotation, 3, copy);
        cellX += Board.OriginX;
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        index = nextBase;
        copy = shapes[index];
        cellX = Preview.OriginX + Pieces.X(nextBase, 0, 0, copy);
        if (nextBase != 0)
        {
            cellX += 1;
        }

        cellY = Preview.OriginY + Pieces.Y(nextBase, 0, 0, copy);
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        index = nextBase + 1;
        copy = shapes[index];
        cellX = Preview.OriginX + Pieces.X(nextBase, 0, 1, copy);
        if (nextBase != 0)
        {
            cellX += 1;
        }

        cellY = Preview.OriginY + Pieces.Y(nextBase, 0, 1, copy);
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        index = nextBase + 2;
        copy = shapes[index];
        cellX = Preview.OriginX + Pieces.X(nextBase, 0, 2, copy);
        if (nextBase != 0)
        {
            cellX += 1;
        }

        cellY = Preview.OriginY + Pieces.Y(nextBase, 0, 2, copy);
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        index = nextBase + 3;
        copy = shapes[index];
        cellX = Preview.OriginX + Pieces.X(nextBase, 0, 3, copy);
        if (nextBase != 0)
        {
            cellX += 1;
        }

        cellY = Preview.OriginY + Pieces.Y(nextBase, 0, 3, copy);
        cellX += cellX;
        cellX += cellX;
        cellX += cellX;
        cellY += cellY;
        cellY += cellY;
        cellY += cellY;
        cellY = gameOver ? SpriteLayout.HiddenY : cellY;
        Sprite.Draw(block, cellX, cellY, 0, false, 0);

        Input.Poll();

        if (gameOver)
        {
            if (Input.WasPressed(Button.Start))
            {
                index = 0;
                while (index < Board.CellCount)
                {
                    board[index] = 0;
                    index += 1;
                }

                piece = Piece.T;
                pieceBase = 8;
                nextPiece = Piece.I;
                nextBase = 0;
                rotation = 0;
                pieceX = 3;
                pieceY = 0;
                fallCounter = 0;
                fallDelay = Timing.InitialFallDelay;
                horizontalCooldown = 0;
                lineCount = 0;
                redrawRow = 0;
                dirtyCell = 0;
                gameOver = false;
            }

            continue;
        }

        candidateX = pieceX;
        candidateY = pieceY;
        candidateRotation = rotation;
        attempt = false;

        if (Input.WasPressed(Button.A))
        {
            candidateRotation += 1;
            if (candidateRotation == 4)
            {
                candidateRotation = 0;
            }

            attempt = true;
        }
        else if (Input.WasPressed(Button.B))
        {
            candidateRotation = candidateRotation == 0 ? 3 : candidateRotation - 1;
            attempt = true;
        }
        else if (Input.IsDown(Button.Left))
        {
            if (Input.WasPressed(Button.Left) || horizontalCooldown == 0)
            {
                if (pieceX > 0)
                {
                    candidateX -= 1;
                    attempt = true;
                }

                horizontalCooldown = Input.WasPressed(Button.Left)
                    ? Timing.InitialHorizontalDelay
                    : Timing.RepeatHorizontalDelay;
            }
            else
            {
                horizontalCooldown -= 1;
            }
        }
        else if (Input.IsDown(Button.Right))
        {
            if (Input.WasPressed(Button.Right) || horizontalCooldown == 0)
            {
                candidateX += 1;
                attempt = true;
                horizontalCooldown = Input.WasPressed(Button.Right)
                    ? Timing.InitialHorizontalDelay
                    : Timing.RepeatHorizontalDelay;
            }
            else
            {
                horizontalCooldown -= 1;
            }
        }
        else
        {
            horizontalCooldown = 0;
        }

        if (attempt)
        {
            blocked = false;
            cell = 0;
            while (cell < 4 && !blocked)
            {
                index = pieceBase + cell;
                copy = shapes[index];
                cellX = candidateX + Pieces.X(pieceBase, candidateRotation, cell, copy);
                cellY = candidateY + Pieces.Y(pieceBase, candidateRotation, cell, copy);
                if (cellX >= Board.Width || cellY >= Board.Height)
                {
                    blocked = true;
                }
                else
                {
                    index = Board.Index(cellX, cellY);
                    if (board[index] != 0)
                    {
                        blocked = true;
                    }
                }

                cell += 1;
            }

            if (!blocked)
            {
                pieceX = candidateX;
                rotation = candidateRotation;
            }
        }

        lockRequested = false;
        if (Input.WasPressed(Button.Up))
        {
            while (!lockRequested)
            {
                candidateY = pieceY + 1;
                blocked = false;
                cell = 0;
                while (cell < 4 && !blocked)
                {
                    index = pieceBase + cell;
                    copy = shapes[index];
                    cellX = pieceX + Pieces.X(pieceBase, rotation, cell, copy);
                    cellY = candidateY + Pieces.Y(pieceBase, rotation, cell, copy);
                    if (cellX >= Board.Width || cellY >= Board.Height)
                    {
                        blocked = true;
                    }
                    else
                    {
                        index = Board.Index(cellX, cellY);
                        if (board[index] != 0)
                        {
                            blocked = true;
                        }
                    }

                    cell += 1;
                }

                if (blocked)
                {
                    lockRequested = true;
                }
                else
                {
                    pieceY = candidateY;
                }
            }
        }
        else
        {
            fallCounter += 1;
            fallDue = false;
            if (Input.IsDown(Button.Down))
            {
                if (fallCounter >= Timing.SoftDropDelay)
                {
                    fallCounter = 0;
                    fallDue = true;
                }
            }
            else if (fallCounter >= fallDelay)
            {
                fallCounter = 0;
                fallDue = true;
            }

            if (fallDue)
            {
                candidateY = pieceY + 1;
                blocked = false;
                cell = 0;
                while (cell < 4 && !blocked)
                {
                    index = pieceBase + cell;
                    copy = shapes[index];
                    cellX = pieceX + Pieces.X(pieceBase, rotation, cell, copy);
                    cellY = candidateY + Pieces.Y(pieceBase, rotation, cell, copy);
                    if (cellX >= Board.Width || cellY >= Board.Height)
                    {
                        blocked = true;
                    }
                    else
                    {
                        index = Board.Index(cellX, cellY);
                        if (board[index] != 0)
                        {
                            blocked = true;
                        }
                    }

                    cell += 1;
                }

                if (blocked)
                {
                    lockRequested = true;
                }
                else
                {
                    pieceY = candidateY;
                }
            }
        }

        if (!lockRequested)
        {
            continue;
        }

        cell = 0;
        while (cell < 4)
        {
            index = pieceBase + cell;
            copy = shapes[index];
            cellX = pieceX + Pieces.X(pieceBase, rotation, cell, copy);
            cellY = pieceY + Pieces.Y(pieceBase, rotation, cell, copy);
            index = Board.Index(cellX, cellY);
            board[index] = 1;
            cell += 1;
        }

        dirtyBase = pieceBase;
        dirtyRotation = rotation;
        dirtyX = pieceX;
        dirtyY = pieceY;
        dirtyCell = 0;

        row = Board.Height - 1;
        while (true)
        {
            full = true;
            column = 0;
            while (column < Board.Width)
            {
                index = Board.Index(column, row);
                if (board[index] == 0)
                {
                    full = false;
                }

                column += 1;
            }

            if (full)
            {
                if (lineCount < Board.Height)
                {
                    lineCount += 1;
                }

                if (fallDelay > Timing.MinimumFallDelay)
                {
                    fallDelay -= 1;
                }

                shiftRow = row;
                while (shiftRow > 0)
                {
                    candidateY = shiftRow - 1;
                    column = 0;
                    while (column < Board.Width)
                    {
                        index = Board.Index(column, shiftRow);
                        copy = board[Board.Index(column, candidateY)];
                        board[index] = copy;
                        column += 1;
                    }

                    shiftRow -= 1;
                }

                column = 0;
                while (column < Board.Width)
                {
                    board[Board.Index(column, 0)] = 0;
                    column += 1;
                }

                redrawRow = 0;
                dirtyCell = 0;
            }
            else if (row == 0)
            {
                break;
            }
            else
            {
                row -= 1;
            }
        }

        piece = nextPiece;
        pieceBase = 0;
        cell = piece;
        while (cell > 0)
        {
            pieceBase += 4;
            cell -= 1;
        }

        nextPiece += 3;
        if (nextPiece >= 7)
        {
            nextPiece -= 7;
        }

        nextBase = 0;
        cell = nextPiece;
        while (cell > 0)
        {
            nextBase += 4;
            cell -= 1;
        }

        rotation = 0;
        pieceX = 3;
        pieceY = 0;
        fallCounter = 0;

        blocked = false;
        cell = 0;
        while (cell < 4 && !blocked)
        {
            index = pieceBase + cell;
            copy = shapes[index];
            cellX = pieceX + Pieces.X(pieceBase, rotation, cell, copy);
            cellY = pieceY + Pieces.Y(pieceBase, rotation, cell, copy);
            index = Board.Index(cellX, cellY);
            if (board[index] != 0)
            {
                blocked = true;
            }

            cell += 1;
        }

        if (blocked)
        {
            gameOver = true;
        }
    }
}
