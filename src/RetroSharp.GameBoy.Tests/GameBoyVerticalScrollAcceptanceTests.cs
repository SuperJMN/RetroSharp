namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using System.Text.RegularExpressions;
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
        Assert.Contains("Camera.Init(Level.Width, Level.StreamY, Level.Height);", source, StringComparison.Ordinal);

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

        Assert.True(observedFreshRow, "The tall Tiled World.Load sample did not stream fresh rows into the wrapped background buffer.");
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

    [Fact]
    public void Game_boy_diagonal_tiled_world_load_streams_fresh_columns_and_rows_from_full_map()
    {
        var samplePath = RepositoryFile("samples/tiled-diagonal/diag.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath)
            ?? throw new InvalidOperationException("Could not locate diagonal Tiled sample directory.");
        var source = File.ReadAllText(samplePath);
        Assert.Contains("World.Load(\"diag.tmj\");", source, StringComparison.Ordinal);
        Assert.Contains("Camera.Init(Level.Width, Level.StreamY, Level.Height);", source, StringComparison.Ordinal);

        var operations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(operations.OfType<Sdk2DOperation.SetCameraPosition>()));

        Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, camera.Axes);
        Assert.IsType<SdkByteExpression.Variable>(camera.X);
        Assert.IsType<SdkByteExpression.Variable>(camera.Y);

        var rom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, rom.Length);

        var scrolling = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        var still = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(StationaryDiagonalTiledSource(source), sampleDirectory))
        {
            CycleAccurateLy = true,
        };

        var observedFreshColumn = false;
        var observedFreshRow = false;
        var maxChangedRows = 0;
        var maxChangedColumns = 0;
        for (var frame = 1; frame <= 520; frame++)
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

                if (changedRows >= 8)
                {
                    observedFreshColumn = true;
                }

                maxChangedRows = Math.Max(maxChangedRows, changedRows);
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

                if (changedColumns >= 8)
                {
                    observedFreshRow = true;
                }

                maxChangedColumns = Math.Max(maxChangedColumns, changedColumns);
            }

            if (observedFreshColumn && observedFreshRow)
            {
                break;
            }
        }

        Assert.True(observedFreshColumn, $"The diagonal Tiled World.Load sample did not stream a fresh wrapped column. Max changed rows: {maxChangedRows}.");
        Assert.True(observedFreshRow, $"The diagonal Tiled World.Load sample did not stream a fresh wrapped row. Max changed columns: {maxChangedColumns}.");
    }

    [Fact]
    public void Game_boy_camera_set_position_streams_fast_horizontal_targets_without_one_tile_ceiling()
    {
        const int mapWidth = 64;
        const int mapHeight = 18;
        const int stepPixels = 16;
        var source = FastCameraSource(mapWidth, mapHeight, stepPixels, vertical: false);
        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        var scxWrites = cpu.RunUntilIoRegisterWrites(0xFF43, 12, maxInstructions: 50_000_000);
        var movementWrites = scxWrites.Where(value => value != 0).ToArray();

        Assert.True(movementWrites.Length >= 8, $"Expected at least 8 non-zero SCX writes, got: {string.Join(", ", scxWrites)}.");
        Assert.Equal((byte)stepPixels, movementWrites[0]);
        Assert.Equal((byte)(stepPixels * 8), movementWrites[7]);
        AssertVisibleTilesMatchGeneratedWorld(cpu, mapWidth, mapHeight, tileByRow: false, "fast horizontal camera");

        var unsafeWrites = cpu.VramWrites
            .Where(write => write is { Address: >= 0x9800 and < 0x9C00, LcdEnabled: true, Applied: false })
            .Take(8)
            .ToArray();
        Assert.True(
            unsafeWrites.Length == 0,
            "Fast camera streaming wrote background tiles outside VBlank: "
            + string.Join(", ", unsafeWrites.Select(write => $"0x{write.Address:X4}=0x{write.Value:X2} LY={write.Ly} cycles={write.Cycles}")));
    }

    [Fact]
    public void Game_boy_camera_set_position_streams_fast_vertical_targets_without_one_tile_ceiling()
    {
        const int mapWidth = 21;
        const int mapHeight = 64;
        const int stepPixels = 16;
        var source = FastCameraSource(mapWidth, mapHeight, stepPixels, vertical: true);
        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        var scyWrites = cpu.RunUntilIoRegisterWrites(0xFF42, 12, maxInstructions: 50_000_000);
        var movementWrites = scyWrites.Where(value => value != 0).ToArray();

        Assert.True(movementWrites.Length >= 8, $"Expected at least 8 non-zero SCY writes, got: {string.Join(", ", scyWrites)}.");
        Assert.Equal((byte)stepPixels, movementWrites[0]);
        Assert.Equal((byte)(stepPixels * 8), movementWrites[7]);
        AssertVisibleTilesMatchGeneratedWorld(cpu, mapWidth, mapHeight, tileByRow: true, "fast vertical camera");

        var unsafeWrites = cpu.VramWrites
            .Where(write => write is { Address: >= 0x9800 and < 0x9C00, LcdEnabled: true, Applied: false })
            .Take(8)
            .ToArray();
        Assert.True(
            unsafeWrites.Length == 0,
            "Fast vertical camera streaming wrote background tiles outside VBlank: "
            + string.Join(", ", unsafeWrites.Select(write => $"0x{write.Address:X4}=0x{write.Value:X2} LY={write.Ly} cycles={write.Cycles}")));
    }

    [Fact]
    public void Game_boy_dead_zone_follow_sample_keeps_scroll_still_inside_band_then_follows_both_axes()
    {
        var samplePath = RepositoryFile("samples/deadzone-follow/deadzone.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath)
            ?? throw new InvalidOperationException("Could not locate dead-zone follow sample directory.");
        var source = File.ReadAllText(samplePath);

        Assert.Contains("World.Load(\"deadzone.tmj\");", source, StringComparison.Ordinal);

        var operations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var camera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(operations.OfType<Sdk2DOperation.SetCameraPosition>()));

        Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, camera.Axes);

        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(source, sampleDirectory))
        {
            CycleAccurateLy = true,
        };

        cpu.RunFrames(2);
        var earlySpriteX = cpu.Oam(0xFE01);
        var earlySpriteY = cpu.Oam(0xFE00);

        cpu.RunFrames(12);
        Assert.Equal(0, cpu.IoRegister(0xFF43)); // SCX
        Assert.Equal(0, cpu.IoRegister(0xFF42)); // SCY
        Assert.True(cpu.Oam(0xFE01) > earlySpriteX, "The player point should move on screen while still inside the horizontal dead-zone.");
        Assert.True(cpu.Oam(0xFE00) > earlySpriteY, "The player point should move on screen while still inside the vertical dead-zone.");

        cpu.RunFrames(55);
        var followedX = cpu.IoRegister(0xFF43);
        var followedY = cpu.IoRegister(0xFF42);
        Assert.InRange(followedX, (byte)1, (byte)64);
        Assert.InRange(followedY, (byte)1, (byte)64);
        Assert.Equal(followedX, followedY);

        cpu.RunFrames(56);
        Assert.Equal((byte)(followedX + 1), cpu.IoRegister(0xFF43));
        Assert.Equal((byte)(followedY + 1), cpu.IoRegister(0xFF42));
    }

    private static string FastCameraSource(int mapWidth, int mapHeight, int stepPixels, bool vertical)
    {
        var columns = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, mapWidth)
                .Select(column =>
                {
                    var tiles = string.Join(
                        ", ",
                        Enumerable.Range(0, mapHeight)
                            .Select(row => (vertical ? row + 1 : column + 1).ToString()));
                    return $"    World.Column({column}, {tiles});";
                }));
        var position = vertical
            ? $"Camera.SetPosition(0, cameraY);"
            : $"Camera.SetPosition(cameraX, 0);";
        var variable = vertical
            ? "u8 cameraY = 0;"
            : "u8 cameraX = 0;";
        var movement = vertical
            ? $"cameraY += {stepPixels};"
            : $"cameraX += {stepPixels};";

        return $$"""
                 void Main() {
                     Video.Init();
                 {{columns}}
                     World.Map({{mapWidth}}, 0, {{mapHeight}});
                     Camera.Init({{mapWidth}}, 0, {{mapHeight}});
                     {{variable}}
                     while (true) {
                         Video.WaitVBlank();
                         Camera.Apply();
                         {{movement}}
                         {{position}}
                     }
                 }
                 """;
    }

    private static void AssertVisibleTilesMatchGeneratedWorld(GameBoyTestCpu cpu, int mapWidth, int mapHeight, bool tileByRow, string label)
    {
        var scx = cpu.IoRegister(0xFF43);
        var scy = cpu.IoRegister(0xFF42);
        var cameraX = cpu.Wram(0xC0E0) | cpu.Wram(0xC0E1) << 8;
        var cameraY = cpu.Wram(0xC0E8) | cpu.Wram(0xC0E9) << 8;
        var firstBufferColumn = scx / 8;
        var firstBufferRow = scy / 8;
        var screenColumns = scx % 8 == 0 ? 20 : 21;
        var screenRows = scy % 8 == 0 ? 18 : 19;
        var mismatches = new List<string>();

        for (var screenRow = 0; screenRow < screenRows; screenRow++)
        {
            var sourceRow = (cameraY + screenRow * 8) / 8 % mapHeight;
            var bufferRow = (firstBufferRow + screenRow) % 32;
            for (var screenColumn = 0; screenColumn < screenColumns; screenColumn++)
            {
                var sourceColumn = (cameraX + screenColumn * 8) / 8 % mapWidth;
                var bufferColumn = (firstBufferColumn + screenColumn) % 32;
                var expected = (byte)((tileByRow ? sourceRow : sourceColumn) + 1);
                var actual = cpu.Vram((ushort)(0x9800 + bufferRow * 32 + bufferColumn));
                if (actual != expected)
                {
                    mismatches.Add(
                        $"{label}: camera=({cameraX},{cameraY}) scx={scx} scy={scy} screen=({screenColumn},{screenRow}) "
                        + $"state={CameraState(cpu)} "
                        + $"buffer=({bufferColumn},{bufferRow}) source=({sourceColumn},{sourceRow}) "
                        + $"expected=0x{expected:X2} actual=0x{actual:X2}");
                    if (mismatches.Count >= 12)
                    {
                        break;
                    }
                }
            }

            if (mismatches.Count >= 12)
            {
                break;
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static string CameraState(GameBoyTestCpu cpu)
    {
        return $"fine=({cpu.Wram(0xC0E2)},{cpu.Wram(0xC0EA)}) "
            + $"screenLeft={cpu.Wram(0xC0E3)} bgLeft={cpu.Wram(0xC0E5)} bgRight={cpu.Wram(0xC0E4)} "
            + $"srcLeft={cpu.Wram(0xC0E7)} srcRight={cpu.Wram(0xC0E6)} "
            + $"topBg={cpu.Wram(0xC0EB)} bottomBg={cpu.Wram(0xC0EC)} "
            + $"topSrc={cpu.Wram(0xC0ED)} bottomSrc={cpu.Wram(0xC0EE)} "
            + $"pending={cpu.Wram(0xC119)}/{cpu.Wram(0xC13A)}/{cpu.Wram(0xC11A)}/{cpu.Wram(0xC11B)}/{cpu.Wram(0xC13B)}/{cpu.Wram(0xC13C)} "
            + $"diagCol={cpu.Wram(0xC11F)}/{cpu.Wram(0xC13D)}/{cpu.Wram(0xC120)}/{cpu.Wram(0xC121)}/{cpu.Wram(0xC13E)}/{cpu.Wram(0xC13F)} "
            + $"diagRow={cpu.Wram(0xC122)}/{cpu.Wram(0xC140)}/{cpu.Wram(0xC123)}/{cpu.Wram(0xC124)}/{cpu.Wram(0xC141)}/{cpu.Wram(0xC142)}";
    }

    private static string StationarySource(string source)
    {
        var stationary = source
            .Replace("Camera.SetPosition(0, cameraY);", "Camera.SetPosition(0, 0);", StringComparison.Ordinal);
        return ReplaceDirectionConditionWithFalse(stationary);
    }

    private static string StationaryFreeScrollSource(string source)
    {
        return source
            .Replace("cameraX += stepX;", "cameraX = 0;", StringComparison.Ordinal)
            .Replace("cameraY += stepY;", "cameraY = 0;", StringComparison.Ordinal)
            .Replace("Camera.SetPosition(cameraX, cameraY);", "Camera.SetPosition(0, 0);", StringComparison.Ordinal);
    }

    private static string StationaryTallTiledSource(string source)
    {
        return ReplaceDirectionConditionWithFalse(source);
    }

    private static string StationaryDiagonalTiledSource(string source)
    {
        return source
            .Replace("cameraX += 1;", "cameraX = 0;", StringComparison.Ordinal)
            .Replace("cameraY += 1;", "cameraY = 0;", StringComparison.Ordinal)
            .Replace("cameraX -= 1;", "cameraX = 0;", StringComparison.Ordinal)
            .Replace("cameraY -= 1;", "cameraY = 0;", StringComparison.Ordinal);
    }

    private static string ReplaceDirectionConditionWithFalse(string source)
    {
        return Regex.Replace(source, @"if\s*\(\s*direction\s*==\s*1\s*\)", "if (false)");
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
