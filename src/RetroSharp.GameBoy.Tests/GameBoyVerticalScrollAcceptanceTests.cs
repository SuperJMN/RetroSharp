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

    private static string StationarySource(string source)
    {
        return source
            .Replace("camera.SetPosition(0, cameraY);", "camera.SetPosition(0, 0);", StringComparison.Ordinal)
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
