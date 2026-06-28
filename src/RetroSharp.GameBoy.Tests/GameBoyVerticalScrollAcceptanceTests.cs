namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using Xunit;

public sealed class GameBoyVerticalScrollAcceptanceTests
{
    [Fact]
    public void Game_boy_vertical_scroll_sample_compiles_collects_vertical_camera_and_streams_fresh_rows()
    {
        var samplePath = RepositoryFile("samples/gameboy-vscroll/vscroll.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath)
            ?? throw new InvalidOperationException("Could not locate vertical scroll sample directory.");
        var source = File.ReadAllText(samplePath);

        var operations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(operations.OfType<Sdk2DOperation.SetCameraPosition>()));

        Assert.True(camera.Axes.HasFlag(ScrollAxes.Vertical));
        Assert.IsType<SdkByteExpression.Variable>(camera.Y);

        var rom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, rom.Length);

        var scrolling = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        var still = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(StationarySource(source), sampleDirectory))
        {
            CycleAccurateLy = true,
        };

        var observedFreshRow = false;
        for (var frames = 1; frames <= 260; frames++)
        {
            scrolling.RunFrames(frames);
            still.RunFrames(frames);

            // Row 0 is overwritten only after vertical movement wraps the 32-row GB background buffer.
            // A stationary ROM keeps the initial source row there.
            const ushort topRow = 0x9800;
            for (var column = 0; column < 20; column++)
            {
                var scrolledTile = scrolling.Vram((ushort)(topRow + column));
                var stillTile = still.Vram((ushort)(topRow + column));
                Assert.InRange(scrolledTile, (byte)0, (byte)64);

                if (scrolledTile != stillTile)
                {
                    observedFreshRow = true;
                }
            }

            if (observedFreshRow)
            {
                break;
            }
        }

        Assert.True(observedFreshRow, "The vertical scroll sample did not stream a fresh row into the wrapped background buffer.");
    }

    [Fact]
    public void Game_boy_tall_tiled_world_load_streams_fresh_rows_when_camera_moves_vertically()
    {
        var samplePath = RepositoryFile("samples/tiled-tall/tall.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath)
            ?? throw new InvalidOperationException("Could not locate tall Tiled sample directory.");
        var source = File.ReadAllText(samplePath);
        Assert.Contains("camera.Init(World.Width, World.StreamY, World.Height);", source, StringComparison.Ordinal);

        var operations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(operations.OfType<Sdk2DOperation.SetCameraPosition>()));

        Assert.True(camera.Axes.HasFlag(ScrollAxes.Vertical));
        Assert.IsType<SdkByteExpression.Variable>(camera.Y);

        var rom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, rom.Length);

        var scrolling = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        var still = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(StationaryTallTiledSource(source), sampleDirectory))
        {
            CycleAccurateLy = true,
        };

        var observedFreshRow = false;
        for (var frame = 1; frame <= 220; frame++)
        {
            scrolling.RunFrames(frame);
            still.RunFrames(frame);

            const ushort topRow = 0x9800;
            var changedColumns = 0;
            for (var column = 0; column < 20; column++)
            {
                if (scrolling.Vram((ushort)(topRow + column)) != still.Vram((ushort)(topRow + column)))
                {
                    changedColumns++;
                }
            }

            if (changedColumns >= 8)
            {
                observedFreshRow = true;
                break;
            }
        }

        Assert.True(observedFreshRow, "The tall Tiled world.Load sample did not stream fresh rows into the wrapped background buffer.");
    }

    [Fact]
    public void Game_boy_free_scroll_sample_streams_fresh_columns_and_rows_with_staggered_diagonal_commit()
    {
        var samplePath = RepositoryFile("samples/nes-free-scroll/freescroll.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath)
            ?? throw new InvalidOperationException("Could not locate free scroll sample directory.");
        var source = File.ReadAllText(samplePath);

        var operations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(operations.OfType<Sdk2DOperation.SetCameraPosition>()));

        Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, camera.Axes);

        var rom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, rom.Length);

        var scrolling = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        var still = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(StationaryFreeScrollSource(source), sampleDirectory))
        {
            CycleAccurateLy = true,
        };

        var observedFreshColumn = false;
        var observedFreshRow = false;
        for (var frame = 1; frame <= 500; frame++)
        {
            scrolling.RunFrames(frame);
            still.RunFrames(frame);

            const ushort background = 0x9800;
            for (var column = 0; column < 32; column++)
            {
                var changedRows = 0;
                for (var row = 1; row < 18; row++)
                {
                    var address = (ushort)(background + row * 32 + column);
                    if (scrolling.Vram(address) != still.Vram(address))
                    {
                        changedRows++;
                    }
                }

                if (changedRows >= 2)
                {
                    observedFreshColumn = true;
                }
            }

            for (var row = 0; row < 32; row++)
            {
                var changedColumns = 0;
                for (var column = 1; column < 32; column++)
                {
                    var address = (ushort)(background + row * 32 + column);
                    if (scrolling.Vram(address) != still.Vram(address))
                    {
                        changedColumns++;
                    }
                }

                if (changedColumns >= 2)
                {
                    observedFreshRow = true;
                }
            }

            if (observedFreshColumn && observedFreshRow)
            {
                break;
            }
        }

        Assert.True(observedFreshColumn, "The diagonal free-scroll sample did not stream a fresh wrapped column.");
        Assert.True(observedFreshRow, "The diagonal free-scroll sample did not stream a fresh wrapped row.");
    }

    private static string StationarySource(string source)
    {
        return source
            .Replace("camera.SetPosition(0, cameraY);", "camera.SetPosition(0, 0);", StringComparison.Ordinal)
            .Replace("if (direction == 1) {", "if (false) {", StringComparison.Ordinal);
    }

    private static string StationaryFreeScrollSource(string source)
    {
        return source
            .Replace("cameraX += stepX;", "cameraX = 0;", StringComparison.Ordinal)
            .Replace("cameraY += stepY;", "cameraY = 0;", StringComparison.Ordinal)
            .Replace("camera.SetPosition(cameraX, cameraY);", "camera.SetPosition(0, 0);", StringComparison.Ordinal);
    }

    private static string StationaryTallTiledSource(string source)
    {
        return source
            .Replace("if (direction == 1) {", "if (false) {", StringComparison.Ordinal);
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
