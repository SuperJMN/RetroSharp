namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesFallingBlocksSmokeTests
{
    [Fact]
    public void Falling_blocks_builds_boots_and_keeps_updating_the_board_safely()
    {
        var projectPath = RepositoryFile("samples/falling-blocks/falling-blocks.retrosharp.json");
        var romPath = Path.Combine(Path.GetTempPath(), $"retrosharp-falling-blocks-{Guid.NewGuid():N}.nes");
        var symbolsPath = Path.ChangeExtension(romPath, ".sym");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RetroSharp.Cli.CliRunner.Run(
                ["--target", "nes", "--symbols-out", symbolsPath, "--out", romPath, projectPath],
                stdout,
                stderr);

            Assert.True(exitCode == 0, stdout.ToString() + stderr);
            var cpu = new NesTestCpu(File.ReadAllBytes(romPath));

            for (var frame = 1; frame <= 300 && !HasStartedRendering(cpu); frame++)
            {
                cpu.RunFrames(frame);
            }

            Assert.True(HasStartedRendering(cpu), "Falling Blocks did not enable rendering and reach its first VBlank.");
            cpu.RunFrames(cpu.PhysicalFrames + 5);
            AssertGameOverHidesPiecesAndStartRestarts(cpu, symbolsPath);
            AssertAllTetrominoesHaveDistinctFourCellSilhouettes(cpu, symbolsPath);
            AssertAllRotationsReachLeftWall(cpu, symbolsPath);
            AssertNextPiecePreviewUpdates(cpu, symbolsPath);
            var boardAddress = SymbolAddress(symbolsPath, "board[0]");
            var redrawRowAddress = SymbolAddress(symbolsPath, "redrawRow");
            var redrawCellAddress = SymbolAddress(symbolsPath, "redrawCell");
            cpu.SetRam(boardAddress, 1);
            cpu.SetRam(redrawRowAddress, 0);
            cpu.SetRam(redrawCellAddress, 0);
            cpu.SetRam(SymbolAddress(symbolsPath, "game.lineCount"), 1);
            var redrawWritesBefore = cpu.PpuWrites.Count;

            cpu.RunFrames(cpu.PhysicalFrames + 40);

            Assert.True(
                cpu.PpuVram(0x2001) == 6,
                $"board={cpu.Ram(boardAddress)}, "
                + $"redrawRow={cpu.Ram(redrawRowAddress)}, "
                + $"redrawCell={cpu.Ram(redrawCellAddress)}"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    cpu.PpuWrites
                        .Skip(redrawWritesBefore)
                        .Take(24)));
            Assert.Equal(3, cpu.PpuVram(0x21F2));
            AssertSettledBlockKeepsActiveAppearance(cpu, cpu.PpuVram(0x2001));
            Assert.True(cpu.PpuWrites.Count >= redrawWritesBefore + (BoardHeight * (BoardWidth + 1)));
            var waitsBefore = cpu.VBlankWaitCompletions;
            var writesBefore = cpu.PpuWrites.Count;
            var resetsBefore = cpu.ResetCount;
            cpu.Held.Add("up");

            cpu.RunFrames(cpu.PhysicalFrames + 120);

            Assert.True(cpu.VBlankWaitCompletions > waitsBefore, "Falling Blocks stopped completing Video.WaitVBlank.");
            Assert.True(cpu.PpuWrites.Count > writesBefore, "Falling Blocks stopped publishing board cells.");
            Assert.Equal(resetsBefore, cpu.ResetCount);
            var unsafePpuWrites = cpu.PpuWrites
                .Where(write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank")
                .ToArray();
            Assert.True(
                unsafePpuWrites.Length == 0,
                string.Join(Environment.NewLine, unsafePpuWrites.Take(8).Select(write =>
                {
                    var timing = cpu.PpuTiming(write.Cycle, true);
                    return $"${write.Register:X4} at {timing.Phase} {timing.Scanline}:{timing.Dot}";
                })));
            Assert.True(
                cpu.Oam(3) is >= 1 and <= 159,
                string.Join(", ", Enumerable.Range(0, 20).Select(index => cpu.Oam((byte)index))));
            AssertTileHasInk(cpu, cpu.PpuVram(0x2001), background: true);
            AssertTileHasInk(cpu, cpu.Oam(1), background: false);
        }
        finally
        {
            File.Delete(romPath);
            File.Delete(symbolsPath);
        }
    }

    private const int BoardWidth = 10;
    private const int BoardHeight = 16;

    private static bool HasStartedRendering(NesTestCpu cpu) =>
        cpu.RenderingEnabled && cpu.VBlankWaitCompletions > 0;

    private static void AssertGameOverHidesPiecesAndStartRestarts(
        NesTestCpu cpu,
        string symbolsPath)
    {
        var boardAddress = SymbolAddress(symbolsPath, "board[0]");
        var gameOverAddress = SymbolAddress(symbolsPath, "game.gameOver");

        for (ushort x = 3; x <= 6; x++)
        {
            cpu.SetRam((ushort)(boardAddress + 10 + x), 1);
        }

        cpu.Held.Add("up");
        cpu.RunFrames(cpu.PhysicalFrames + 3);
        cpu.Held.Remove("up");

        Assert.Equal(1, cpu.Ram(gameOverAddress));
        Assert.All(
            Enumerable.Range(0, 8),
            slot => Assert.InRange(
                cpu.Oam((byte)(slot * 4)),
                (byte)240,
                (byte)254));

        cpu.RunFrames(cpu.PhysicalFrames + 1);
        cpu.Held.Add("start");
        cpu.RunFrames(cpu.PhysicalFrames + 2);
        cpu.Held.Remove("start");
        cpu.RunFrames(cpu.PhysicalFrames + 40);

        Assert.Equal(0, cpu.Ram(gameOverAddress));
        Assert.Equal(8, cpu.Ram(SymbolAddress(symbolsPath, "active.shapeOffset")));
        Assert.Equal(0, cpu.Ram(SymbolAddress(symbolsPath, "nextBase")));
        Assert.Equal(0, cpu.Ram(SymbolAddress(symbolsPath, "game.lineCount")));
        Assert.All(
            Enumerable.Range(0, BoardWidth * BoardHeight),
            index => Assert.Equal(0, cpu.Ram((ushort)(boardAddress + index))));
        Assert.All(
            Enumerable.Range(0, 8),
            slot => Assert.True(
                cpu.Oam((byte)(slot * 4)) is <= 239 or 255,
                $"sprite slot {slot} remained hidden after restart at OAM Y "
                + cpu.Oam((byte)(slot * 4))));
    }

    private static void AssertAllTetrominoesHaveDistinctFourCellSilhouettes(
        NesTestCpu cpu,
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
            cpu.SetRam(pieceBaseAddress, pieceBase);
            cpu.SetRam(rotationAddress, 0);
            cpu.SetRam(pieceXAddress, 3);
            cpu.SetRam(pieceYAddress, 4);
            cpu.SetRam(fallCounterAddress, 0);
            cpu.SetRam(gameOverAddress, 0);
            cpu.RunFrames(cpu.PhysicalFrames + 2);
            signatures.Add(ActiveTetrominoSignature(cpu));
        }

        Assert.Equal(7, signatures.Distinct(StringComparer.Ordinal).Count());
    }

    private static string ActiveTetrominoSignature(NesTestCpu cpu)
    {
        var cells = Enumerable.Range(0, 4)
            .Select(slot => (
                Y: cpu.Oam((byte)(slot * 4)),
                Tile: cpu.Oam((byte)(slot * 4 + 1)),
                X: cpu.Oam((byte)(slot * 4 + 3))))
            .ToArray();

        Assert.All(cells, cell => Assert.InRange(cell.X, (byte)1, (byte)159));
        Assert.Single(cells.Select(cell => cell.Tile).Distinct());
        Assert.Equal(4, cells.Select(cell => (cell.X, cell.Y)).Distinct().Count());
        var minX = cells.Min(cell => cell.X);
        var minY = cells.Min(cell => cell.Y);
        return string.Join(
            ";",
            cells.Select(cell => $"{cell.X - minX},{cell.Y - minY}")
                .Order(StringComparer.Ordinal));
    }

    private static void AssertAllRotationsReachLeftWall(NesTestCpu cpu, string symbolsPath)
    {
        var pieceBaseAddress = SymbolAddress(symbolsPath, "active.shapeOffset");
        var rotationAddress = SymbolAddress(symbolsPath, "active.rotation");
        var pieceXAddress = SymbolAddress(symbolsPath, "active.x");
        var pieceYAddress = SymbolAddress(symbolsPath, "active.y");
        var fallCounterAddress = SymbolAddress(symbolsPath, "game.fallCounter");
        var gameOverAddress = SymbolAddress(symbolsPath, "game.gameOver");

        cpu.SetRam(pieceBaseAddress, 0);
        cpu.SetRam(rotationAddress, 1);
        cpu.SetRam(pieceXAddress, 3);
        cpu.SetRam(pieceYAddress, 4);
        cpu.SetRam(fallCounterAddress, 0);
        cpu.SetRam(gameOverAddress, 0);
        cpu.Held.Add("left");
        cpu.RunFrames(cpu.PhysicalFrames + 20);
        cpu.Held.Remove("left");
        cpu.RunFrames(cpu.PhysicalFrames + 2);
        Assert.Equal(0, cpu.Ram(pieceXAddress));
        Assert.Equal(
            8,
            Enumerable.Range(0, 4).Min(slot => cpu.Oam((byte)(slot * 4 + 3))));

        for (byte pieceBase = 0; pieceBase < 28; pieceBase += 4)
        {
            for (byte rotation = 0; rotation < 4; rotation += 1)
            {
                cpu.SetRam(pieceBaseAddress, pieceBase);
                cpu.SetRam(rotationAddress, rotation);
                cpu.SetRam(pieceXAddress, 0);
                cpu.SetRam(pieceYAddress, 4);
                cpu.SetRam(fallCounterAddress, 0);
                cpu.SetRam(gameOverAddress, 0);
                cpu.RunFrames(cpu.PhysicalFrames + 2);

                var leftmostX = Enumerable.Range(0, 4)
                    .Min(slot => cpu.Oam((byte)(slot * 4 + 3)));
                Assert.True(
                    leftmostX == 8,
                    $"pieceBase={pieceBase}, rotation={rotation}: expected leftmost X 8, got {leftmostX}.");
            }
        }
    }

    private static void AssertNextPiecePreviewUpdates(NesTestCpu cpu, string symbolsPath)
    {
        var nextBaseAddress = SymbolAddress(symbolsPath, "nextBase");

        cpu.SetRam(nextBaseAddress, 0);
        cpu.RunFrames(cpu.PhysicalFrames + 2);
        var line = PreviewCells(cpu);
        Assert.Single(line.Select(cell => cell.Y).Distinct());
        Assert.Equal(4, line.Select(cell => cell.X).Distinct().Count());

        cpu.SetRam(nextBaseAddress, 4);
        cpu.RunFrames(cpu.PhysicalFrames + 2);
        var square = PreviewCells(cpu);
        Assert.Equal(2, square.Select(cell => cell.X).Distinct().Count());
        Assert.Equal(2, square.Select(cell => cell.Y).Distinct().Count());
        Assert.False(
            line.Select(cell => (cell.X, cell.Y)).Order()
                .SequenceEqual(square.Select(cell => (cell.X, cell.Y)).Order()));

        cpu.SetRam(nextBaseAddress, 0);
    }

    private static (byte X, byte Y, byte Tile)[] PreviewCells(NesTestCpu cpu)
    {
        var cells = Enumerable.Range(4, 4)
            .Select(slot => (
                X: cpu.Oam((byte)(slot * 4 + 3)),
                Y: cpu.Oam((byte)(slot * 4)),
                Tile: cpu.Oam((byte)(slot * 4 + 1))))
            .ToArray();

        Assert.All(cells, cell => Assert.InRange(cell.X, (byte)104, (byte)159));
        Assert.Single(cells.Select(cell => cell.Tile).Distinct());
        Assert.Equal(4, cells.Select(cell => (cell.X, cell.Y)).Distinct().Count());
        return cells;
    }

    private static ushort SymbolAddress(string symbolsPath, string symbol) =>
        Convert.ToUInt16(
            File.ReadLines(symbolsPath)
                .Single(line => line.EndsWith($" {symbol}", StringComparison.Ordinal))
                .Split(' ', 2)[0],
            16);

    private static void AssertTileHasInk(NesTestCpu cpu, byte tile, bool background)
    {
        var pattern = TilePattern(cpu, tile, background);
        Assert.True(
            pattern.Any(value => value != 0),
            $"tile={tile}, background={background}, ppuctrl=${cpu.PpuControl:X2}, pattern={Convert.ToHexString(pattern)}");
    }

    private static void AssertSettledBlockKeepsActiveAppearance(NesTestCpu cpu, byte landedTile)
    {
        Assert.Equal(
            Enumerable.Range(0, 4).Select(index => cpu.PpuVram((ushort)(0x3F10 + index))),
            Enumerable.Range(0, 4).Select(index => cpu.PpuVram((ushort)(0x3F00 + index))));
        Assert.Equal(TilePattern(cpu, cpu.Oam(1), background: false), TilePattern(cpu, landedTile, background: true));
    }

    private static byte[] TilePattern(NesTestCpu cpu, byte tile, bool background)
    {
        var patternTable = (cpu.PpuControl & (background ? 0x10 : 0x08)) != 0 ? 0x1000 : 0;
        var tileAddress = patternTable + (tile * 16);
        return Enumerable.Range(0, 16)
            .Select(offset => cpu.PpuVram((ushort)(tileAddress + offset)))
            .ToArray();
    }

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
