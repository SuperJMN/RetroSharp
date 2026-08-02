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
    Tilemap.Fill(Board.LeftWallX, 0, 1, Board.Height + 1, Tile.Landed);
    Tilemap.Fill(Board.RightWallX, 0, 1, Board.Height + 1, Tile.Landed);
    Tilemap.Fill(Board.LeftWallX, Board.Height, Board.RightWallX + 1, 1, Tile.Landed);
}

void Main()
{
    SetupVideo();
    SetupField();

    u8 board[Board.CellCount];
    u8 shapes[] = [
        4, 5, 6, 7,
        1, 2, 5, 6,
        4, 5, 6, 1,
        1, 2, 4, 5,
        0, 1, 5, 6,
        0, 4, 5, 6,
        2, 4, 5, 6
    ];
    PiecePose active;
    PiecePose candidate;
    PiecePose landed;
    CellPosition position;
    GameState game;
    u8 nextPiece = Piece.I;
    u8 nextBase = 0;
    u8 redrawCell = Pieces.CellCount;
    u8 redrawRow = Board.NoRedraw;
    u8 cell = 0;
    u8 index = 0;
    u8 copy = 0;
    u8 row = 0;
    u8 column = 0;
    u8 shiftRow = 0;
    bool blocked = false;
    bool lockRequested = false;
    bool attempt = false;

    active.Spawn(Piece.T);
    game.Reset();

    while (true)
    {
        Video.WaitVBlank();

        if (redrawRow < Board.Height)
        {
            index = Board.Index(redrawCell, redrawRow);
            for (column = 0; column < Board.RedrawBatchSize; column++)
            {
                copy = board[index] == 0 ? Tile.Empty : Tile.Landed;
                Tilemap.Set(Board.OriginX + redrawCell, redrawRow, copy);
                redrawCell++;
                index++;
            }

            if (redrawCell == Board.Width)
            {
                Tilemap.Set(
                    Board.MeterX,
                    redrawRow,
                    redrawRow + game.lineCount >= Board.Height ? Tile.Meter : Tile.Empty);

                redrawCell = 0;
                redrawRow++;
                if (redrawRow == Board.Height)
                {
                    redrawRow = Board.NoRedraw;
                    redrawCell = Pieces.CellCount;
                }
            }

            Input.Poll();
            continue;
        }

        if (redrawCell < Pieces.CellCount)
        {
            index = landed.CodeIndex(redrawCell);
            copy = shapes[index];
            position.Locate(landed, redrawCell, copy);
            Tilemap.Set(Board.OriginX + position.x, position.y, Tile.Landed);
            redrawCell++;
        }

        position.Present(
            piece: active,
            cell: 0,
            code: shapes[active.CodeIndex(0)],
            originX: Board.OriginX,
            originY: 0,
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        position.Present(
            piece: active,
            cell: 1,
            code: shapes[active.CodeIndex(1)],
            originX: Board.OriginX,
            originY: 0,
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        position.Present(
            piece: active,
            cell: 2,
            code: shapes[active.CodeIndex(2)],
            originX: Board.OriginX,
            originY: 0,
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        position.Present(
            piece: active,
            cell: 3,
            code: shapes[active.CodeIndex(3)],
            originX: Board.OriginX,
            originY: 0,
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        position.PresentPreview(
            shapeOffset: nextBase,
            cell: 0,
            code: shapes[nextBase],
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        position.PresentPreview(
            shapeOffset: nextBase,
            cell: 1,
            code: shapes[nextBase + 1],
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        position.PresentPreview(
            shapeOffset: nextBase,
            cell: 2,
            code: shapes[nextBase + 2],
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        position.PresentPreview(
            shapeOffset: nextBase,
            cell: 3,
            code: shapes[nextBase + 3],
            hidden: game.gameOver);
        Sprite.Draw(block, position.x, position.y, 0, false, 0);

        Input.Poll();

        if (game.gameOver)
        {
            if (Input.WasPressed(Button.Start))
            {
                for (index = 0; index < countof(board); index++)
                {
                    board[index] = 0;
                }

                active.Spawn(Piece.T);
                nextPiece = Piece.I;
                nextBase = 0;
                game.Reset();
                redrawRow = 0;
                redrawCell = 0;
            }

            continue;
        }

        candidate.CopyFrom(active);
        attempt = false;

        if (Input.WasPressed(Button.A))
        {
            candidate.RotateClockwise();
            attempt = true;
        }
        else if (Input.WasPressed(Button.B))
        {
            candidate.RotateCounterClockwise();
            attempt = true;
        }
        else if (Input.IsDown(Button.Left))
        {
            if (Input.WasPressed(Button.Left) || game.horizontalCooldown == 0)
            {
                if (active.x > 0)
                {
                    candidate.x--;
                    attempt = true;
                }

                game.horizontalCooldown = Input.WasPressed(Button.Left)
                    ? Timing.InitialHorizontalDelay
                    : Timing.RepeatHorizontalDelay;
            }
            else
            {
                game.horizontalCooldown--;
            }
        }
        else if (Input.IsDown(Button.Right))
        {
            if (Input.WasPressed(Button.Right) || game.horizontalCooldown == 0)
            {
                candidate.x++;
                attempt = true;
                game.horizontalCooldown = Input.WasPressed(Button.Right)
                    ? Timing.InitialHorizontalDelay
                    : Timing.RepeatHorizontalDelay;
            }
            else
            {
                game.horizontalCooldown--;
            }
        }
        else
        {
            game.horizontalCooldown = 0;
        }

        if (attempt)
        {
            blocked = false;
            for (cell = 0; cell < Pieces.CellCount && !blocked; cell++)
            {
                index = candidate.CodeIndex(cell);
                copy = shapes[index];
                position.Locate(candidate, cell, copy);
                if (position.x >= Board.Width || position.y >= Board.Height)
                {
                    blocked = true;
                }
                else
                {
                    index = Board.Index(position.x, position.y);
                    if (board[index] != 0)
                    {
                        blocked = true;
                    }
                }
            }

            if (!blocked)
            {
                active.CopyFrom(candidate);
            }
        }

        lockRequested = false;
        if (Input.WasPressed(Button.Up))
        {
            while (!lockRequested)
            {
                candidate.CopyFrom(active);
                candidate.y++;
                blocked = false;
                for (cell = 0; cell < Pieces.CellCount && !blocked; cell++)
                {
                    index = candidate.CodeIndex(cell);
                    copy = shapes[index];
                    position.Locate(candidate, cell, copy);
                    if (position.x >= Board.Width || position.y >= Board.Height)
                    {
                        blocked = true;
                    }
                    else
                    {
                        index = Board.Index(position.x, position.y);
                        if (board[index] != 0)
                        {
                            blocked = true;
                        }
                    }
                }

                if (blocked)
                {
                    lockRequested = true;
                }
                else
                {
                    active.CopyFrom(candidate);
                }
            }
        }
        else
        {
            game.fallCounter++;
            copy = Input.IsDown(Button.Down) ? Timing.SoftDropDelay : game.fallDelay;
            if (game.fallCounter >= copy)
            {
                game.fallCounter = 0;
                candidate.CopyFrom(active);
                candidate.y++;
                blocked = false;
                for (cell = 0; cell < Pieces.CellCount && !blocked; cell++)
                {
                    index = candidate.CodeIndex(cell);
                    copy = shapes[index];
                    position.Locate(candidate, cell, copy);
                    if (position.x >= Board.Width || position.y >= Board.Height)
                    {
                        blocked = true;
                    }
                    else
                    {
                        index = Board.Index(position.x, position.y);
                        if (board[index] != 0)
                        {
                            blocked = true;
                        }
                    }
                }

                if (blocked)
                {
                    lockRequested = true;
                }
                else
                {
                    active.CopyFrom(candidate);
                }
            }
        }

        if (!lockRequested)
        {
            continue;
        }

        for (cell = 0; cell < Pieces.CellCount; cell++)
        {
            index = active.CodeIndex(cell);
            copy = shapes[index];
            position.Locate(active, cell, copy);
            index = Board.Index(position.x, position.y);
            board[index] = 1;
        }

        landed.CopyFrom(active);
        redrawCell = 0;

        row = Board.Height - 1;
        while (true)
        {
            for (column = 0; column < Board.Width; column++)
            {
                index = Board.Index(column, row);
                if (board[index] == 0)
                {
                    break;
                }
            }

            if (column == Board.Width)
            {
                game.RegisterClearedLine();

                shiftRow = row;
                while (shiftRow > 0)
                {
                    candidate.y = shiftRow - 1;
                    for (column = 0; column < Board.Width; column++)
                    {
                        index = Board.Index(column, shiftRow);
                        copy = board[Board.Index(column, candidate.y)];
                        board[index] = copy;
                    }

                    shiftRow--;
                }

                for (column = 0; column < Board.Width; column++)
                {
                    board[Board.Index(column, 0)] = 0;
                }

                redrawRow = 0;
                redrawCell = 0;
            }
            else if (row == 0)
            {
                break;
            }
            else
            {
                row--;
            }
        }

        active.Spawn(nextPiece);

        nextPiece += 3;
        if (nextPiece >= Pieces.Count)
        {
            nextPiece -= Pieces.Count;
        }

        nextBase = nextPiece;
        nextBase += nextBase;
        nextBase += nextBase;
        game.fallCounter = 0;

        blocked = false;
        for (cell = 0; cell < Pieces.CellCount && !blocked; cell++)
        {
            index = active.CodeIndex(cell);
            copy = shapes[index];
            position.Locate(active, cell, copy);
            index = Board.Index(position.x, position.y);
            if (board[index] != 0)
            {
                blocked = true;
            }
        }

        if (blocked)
        {
            game.gameOver = true;
        }
    }
}
