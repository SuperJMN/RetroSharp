namespace RetroSharp.GameBoy.Tests;

using Xunit;

public sealed class GameBoyFallingBlocksSmokeTests
{
    [Fact]
    public void Falling_blocks_builds_boots_and_keeps_updating_the_board_safely()
    {
        var projectPath = RepositoryFile("samples/falling-blocks/falling-blocks.retrosharp.json");
        var romPath = Path.Combine(Path.GetTempPath(), $"retrosharp-falling-blocks-{Guid.NewGuid():N}.gb");
        var symbolsPath = Path.ChangeExtension(romPath, ".sym");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RetroSharp.Cli.CliRunner.Run(
                ["--target", "gb", "--symbols-out", symbolsPath, "--out", romPath, projectPath],
                stdout,
                stderr);

            Assert.True(exitCode == 0, stdout.ToString() + stderr);
            var cpu = new GameBoyTestCpu(File.ReadAllBytes(romPath))
            {
                CycleAccurateLy = true,
                EnforceVblankVramWrites = true,
            };

            for (var frame = 1; frame <= 300 && !HasStartedRendering(cpu); frame++)
            {
                cpu.RunFrames(frame);
            }

            Assert.True(HasStartedRendering(cpu), "Falling Blocks did not enable rendering and reach its first VBlank.");
            cpu.RunAdditionalFrames(5);
            AssertGameOverHidesPiecesAndStartRestarts(cpu, symbolsPath);
            AssertAllTetrominoesHaveDistinctFourCellSilhouettes(cpu, symbolsPath);
            AssertAllRotationsReachLeftWall(cpu, symbolsPath);
            AssertNextPiecePreviewUpdates(cpu, symbolsPath);
            cpu.SetWram(SymbolAddress(symbolsPath, "board[0]"), 1);
            cpu.SetWram(SymbolAddress(symbolsPath, "redrawRow"), 0);
            cpu.SetWram(SymbolAddress(symbolsPath, "redrawCell"), 0);
            cpu.SetWram(SymbolAddress(symbolsPath, "game.lineCount"), 1);
            var redrawWritesBefore = cpu.VramWrites.Count;

            cpu.RunAdditionalFrames(40);

            AssertSafeVramWrites(cpu);
            Assert.Equal(6, cpu.Vram(0x9801));
            Assert.Equal(3, cpu.Vram(0x99F2));
            AssertSettledBlockKeepsActiveAppearance(cpu, cpu.Vram(0x9801));
            Assert.True(cpu.VramWrites.Count >= redrawWritesBefore + (BoardHeight * (BoardWidth + 1)));
            var waitsBefore = cpu.VBlankWaitCompletions;
            var writesBefore = cpu.VramWrites.Count;
            var resetsBefore = cpu.ResetCount;
            cpu.Held.Add("up");

            cpu.RunAdditionalFrames(120);

            Assert.True(cpu.VBlankWaitCompletions > waitsBefore, "Falling Blocks stopped completing Video.WaitVBlank.");
            Assert.True(cpu.VramWrites.Count > writesBefore, "Falling Blocks stopped publishing board cells.");
            Assert.Equal(resetsBefore, cpu.ResetCount);
            AssertSafeVramWrites(cpu);
            Assert.InRange(cpu.Oam(0xFE01), (byte)8, (byte)167);
            AssertTileHasInk(cpu, cpu.Vram(0x9801), background: true);
            AssertTileHasInk(cpu, cpu.Oam(0xFE02), background: false);
        }
        finally
        {
            File.Delete(romPath);
            File.Delete(symbolsPath);
        }
    }

    private const int BoardWidth = 10;
    private const int BoardHeight = 16;

    private static bool HasStartedRendering(GameBoyTestCpu cpu) =>
        (cpu.IoRegister(0xFF40) & 0x80) != 0 && cpu.VBlankWaitCompletions > 0;

    private static void AssertGameOverHidesPiecesAndStartRestarts(
        GameBoyTestCpu cpu,
        string symbolsPath)
    {
        var boardAddress = SymbolAddress(symbolsPath, "board[0]");
        var gameOverAddress = SymbolAddress(symbolsPath, "game.gameOver");

        for (ushort x = 3; x <= 6; x++)
        {
            cpu.SetWram((ushort)(boardAddress + 10 + x), 1);
        }

        cpu.Held.Add("up");
        cpu.RunAdditionalFrames(3);
        cpu.Held.Remove("up");

        Assert.Equal(1, cpu.Wram(gameOverAddress));
        Assert.All(
            Enumerable.Range(0, 8),
            slot => Assert.True(
                cpu.Oam((ushort)(0xFE00 + slot * 4)) is <= 8 or >= 160,
                $"sprite slot {slot} remained visible after game over at OAM Y "
                + cpu.Oam((ushort)(0xFE00 + slot * 4))));

        cpu.RunAdditionalFrames(1);
        cpu.Held.Add("start");
        cpu.RunAdditionalFrames(2);
        cpu.Held.Remove("start");
        cpu.RunAdditionalFrames(40);

        Assert.Equal(0, cpu.Wram(gameOverAddress));
        Assert.Equal(8, cpu.Wram(SymbolAddress(symbolsPath, "active.shapeOffset")));
        Assert.Equal(0, cpu.Wram(SymbolAddress(symbolsPath, "nextBase")));
        Assert.Equal(0, cpu.Wram(SymbolAddress(symbolsPath, "game.lineCount")));
        Assert.All(
            Enumerable.Range(0, BoardWidth * BoardHeight),
            index => Assert.Equal(0, cpu.Wram((ushort)(boardAddress + index))));
        Assert.All(
            Enumerable.Range(0, 8),
            slot => Assert.InRange(
                cpu.Oam((ushort)(0xFE00 + slot * 4)),
                (byte)9,
                (byte)159));
    }

    private static void AssertAllTetrominoesHaveDistinctFourCellSilhouettes(
        GameBoyTestCpu cpu,
        string symbolsPath)
    {
        var pieceBaseAddress = SymbolAddress(symbolsPath, "active.shapeOffset");
        var rotationAddress = SymbolAddress(symbolsPath, "active.rotation");
        var pieceXAddress = SymbolAddress(symbolsPath, "active.x");
        var pieceYAddress = SymbolAddress(symbolsPath, "active.y");
        var fallCounterAddress = SymbolAddress(symbolsPath, "game.fallCounter");
        var gameOverAddress = SymbolAddress(symbolsPath, "game.gameOver");
        var signatures = new List<string>();

        for (byte pieceBase = 0; pieceBase < 28; pieceBase += 4)
        {
            cpu.SetWram(pieceBaseAddress, pieceBase);
            cpu.SetWram(rotationAddress, 0);
            cpu.SetWram(pieceXAddress, 3);
            cpu.SetWram(pieceYAddress, 4);
            cpu.SetWram(fallCounterAddress, 0);
            cpu.SetWram(gameOverAddress, 0);
            cpu.RunAdditionalFrames(2);
            signatures.Add(ActiveTetrominoSignature(cpu));
        }

        Assert.Equal(7, signatures.Distinct(StringComparer.Ordinal).Count());
    }

    private static string ActiveTetrominoSignature(GameBoyTestCpu cpu)
    {
        var cells = Enumerable.Range(0, 4)
            .Select(slot => (
                Y: cpu.Oam((ushort)(0xFE00 + slot * 4)),
                X: cpu.Oam((ushort)(0xFE01 + slot * 4)),
                Tile: cpu.Oam((ushort)(0xFE02 + slot * 4))))
            .ToArray();

        Assert.All(cells, cell => Assert.InRange(cell.X, (byte)8, (byte)167));
        Assert.Single(cells.Select(cell => cell.Tile).Distinct());
        Assert.Equal(4, cells.Select(cell => (cell.X, cell.Y)).Distinct().Count());
        var minX = cells.Min(cell => cell.X);
        var minY = cells.Min(cell => cell.Y);
        return string.Join(
            ";",
            cells.Select(cell => $"{cell.X - minX},{cell.Y - minY}")
                .Order(StringComparer.Ordinal));
    }

    private static void AssertAllRotationsReachLeftWall(GameBoyTestCpu cpu, string symbolsPath)
    {
        var pieceBaseAddress = SymbolAddress(symbolsPath, "active.shapeOffset");
        var rotationAddress = SymbolAddress(symbolsPath, "active.rotation");
        var pieceXAddress = SymbolAddress(symbolsPath, "active.x");
        var pieceYAddress = SymbolAddress(symbolsPath, "active.y");
        var fallCounterAddress = SymbolAddress(symbolsPath, "game.fallCounter");
        var gameOverAddress = SymbolAddress(symbolsPath, "game.gameOver");

        cpu.SetWram(pieceBaseAddress, 0);
        cpu.SetWram(rotationAddress, 1);
        cpu.SetWram(pieceXAddress, 3);
        cpu.SetWram(pieceYAddress, 4);
        cpu.SetWram(fallCounterAddress, 0);
        cpu.SetWram(gameOverAddress, 0);
        cpu.Held.Add("left");
        cpu.RunAdditionalFrames(20);
        cpu.Held.Remove("left");
        cpu.RunAdditionalFrames(2);
        Assert.Equal(0, cpu.Wram(pieceXAddress));
        Assert.Equal(
            16,
            Enumerable.Range(0, 4).Min(slot => cpu.Oam((ushort)(0xFE01 + slot * 4))));

        for (byte pieceBase = 0; pieceBase < 28; pieceBase += 4)
        {
            for (byte rotation = 0; rotation < 4; rotation += 1)
            {
                cpu.SetWram(pieceBaseAddress, pieceBase);
                cpu.SetWram(rotationAddress, rotation);
                cpu.SetWram(pieceXAddress, 0);
                cpu.SetWram(pieceYAddress, 4);
                cpu.SetWram(fallCounterAddress, 0);
                cpu.SetWram(gameOverAddress, 0);
                cpu.RunAdditionalFrames(2);

                var leftmostHardwareX = Enumerable.Range(0, 4)
                    .Min(slot => cpu.Oam((ushort)(0xFE01 + slot * 4)));
                Assert.True(
                    leftmostHardwareX == 16,
                    $"pieceBase={pieceBase}, rotation={rotation}: expected leftmost hardware X 16, got {leftmostHardwareX}.");
            }
        }
    }

    private static void AssertNextPiecePreviewUpdates(GameBoyTestCpu cpu, string symbolsPath)
    {
        var nextBaseAddress = SymbolAddress(symbolsPath, "nextBase");

        cpu.SetWram(nextBaseAddress, 0);
        cpu.RunAdditionalFrames(2);
        var line = PreviewCells(cpu);
        Assert.Single(line.Select(cell => cell.Y).Distinct());
        Assert.Equal(4, line.Select(cell => cell.X).Distinct().Count());

        cpu.SetWram(nextBaseAddress, 4);
        cpu.RunAdditionalFrames(2);
        var square = PreviewCells(cpu);
        Assert.Equal(2, square.Select(cell => cell.X).Distinct().Count());
        Assert.Equal(2, square.Select(cell => cell.Y).Distinct().Count());
        Assert.False(
            line.Select(cell => (cell.X, cell.Y)).Order()
                .SequenceEqual(square.Select(cell => (cell.X, cell.Y)).Order()));

        cpu.SetWram(nextBaseAddress, 0);
    }

    private static (byte X, byte Y, byte Tile)[] PreviewCells(GameBoyTestCpu cpu)
    {
        var cells = Enumerable.Range(4, 4)
            .Select(slot => (
                X: cpu.Oam((ushort)(0xFE01 + slot * 4)),
                Y: cpu.Oam((ushort)(0xFE00 + slot * 4)),
                Tile: cpu.Oam((ushort)(0xFE02 + slot * 4))))
            .ToArray();

        Assert.All(cells, cell => Assert.InRange(cell.X, (byte)112, (byte)167));
        Assert.Single(cells.Select(cell => cell.Tile).Distinct());
        Assert.Equal(4, cells.Select(cell => (cell.X, cell.Y)).Distinct().Count());
        return cells;
    }

    private static void AssertSafeVramWrites(GameBoyTestCpu cpu)
    {
        var unsafeWrites = cpu.VramWrites.Where(write => write.LcdEnabled && !write.Applied).ToArray();
        Assert.True(
            unsafeWrites.Length == 0,
            string.Join(Environment.NewLine, unsafeWrites.Take(8)));
    }

    private static void AssertTileHasInk(GameBoyTestCpu cpu, byte tile, bool background)
    {
        Assert.Contains(TilePattern(cpu, tile, background), value => value != 0);
    }

    private static void AssertSettledBlockKeepsActiveAppearance(GameBoyTestCpu cpu, byte landedTile)
    {
        Assert.Equal(cpu.IoRegister(0xFF48), cpu.IoRegister(0xFF47));
        Assert.Equal(TilePattern(cpu, cpu.Oam(0xFE02), background: false), TilePattern(cpu, landedTile, background: true));
    }

    private static byte[] TilePattern(GameBoyTestCpu cpu, byte tile, bool background)
    {
        var unsignedAddressing = !background || (cpu.IoRegister(0xFF40) & 0x10) != 0;
        var tileAddress = unsignedAddressing
            ? 0x8000 + (tile * 16)
            : 0x9000 + ((sbyte)tile * 16);
        return Enumerable.Range(0, 16)
            .Select(offset => cpu.Vram((ushort)(tileAddress + offset)))
            .ToArray();
    }

    private static ushort SymbolAddress(string symbolsPath, string symbol) =>
        Convert.ToUInt16(
            File.ReadLines(symbolsPath)
                .Single(line => line.EndsWith($" {symbol}", StringComparison.Ordinal))
                .Split(' ', 2)[0],
            16);

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }
}
