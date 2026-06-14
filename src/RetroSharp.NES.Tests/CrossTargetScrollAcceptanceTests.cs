namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.NES;
using Xunit;

// Acceptance: one source program of horizontal scroll and logical sprite drawing produces the SAME shared
// portable operations and lowers (validates + builds) on both Game Boy and NES,
// through the shared Sdk2DOperation model rather than parallel target paths.
public sealed class CrossTargetScrollAcceptanceTests
{
    private const string HorizontalScrollSource = """
        enum World {
            Width = 8,
            StreamY = 10,
            Height = 4
        }

        enum Marker {
            ScreenX = 72,
            ScreenY = 72
        }

        void main() {
            video.Init();

            world.Column(0, 1, 2, 3, 4);
            world.Column(1, 2, 3, 4, 5);
            world.Column(2, 3, 4, 5, 1);
            world.Column(3, 4, 5, 1, 2);
            world.Column(4, 5, 1, 2, 3);
            world.Column(5, 1, 2, 3, 4);
            world.Column(6, 2, 3, 4, 5);
            world.Column(7, 3, 4, 5, 1);
            world.Map(World.Width, World.StreamY, World.Height);
            camera.Init(World.Width, World.StreamY, World.Height);
            sprite.Asset(marker, "samples/cross-target-camera/marker.json");

            loop {
                video.WaitVBlank();
                input.Poll();
                let cameraX = button_hold_ticks(right);
                u8 frame = 0;
                bool flipX = false;
                camera.SetPosition(cameraX, 0);
                camera.Apply();
                sprite.Draw(marker, Marker.ScreenX, Marker.ScreenY, frame, flipX, 0);
            }
        }
        """;

    [Fact]
    public void Same_source_collects_identical_shared_operations_for_both_targets()
    {
        var baseDirectory = RepoRoot();
        var gbOperations = GameBoyRomCompiler.CollectSdkOperations(HorizontalScrollSource, baseDirectory);
        var nesOperations = NesRomCompiler.CollectSdkOperations(HorizontalScrollSource, baseDirectory);

        var gbTypes = gbOperations.Select(operation => operation.GetType()).ToArray();
        var nesTypes = nesOperations.Select(operation => operation.GetType()).ToArray();

        Assert.Equal(gbTypes, nesTypes);
        Assert.Contains(typeof(Sdk2DOperation.WaitFrame), gbTypes);
        Assert.Contains(typeof(Sdk2DOperation.PollInput), gbTypes);
        Assert.Contains(typeof(Sdk2DOperation.SetCameraPosition), gbTypes);
        Assert.Contains(typeof(Sdk2DOperation.DrawLogicalSprite), gbTypes);
    }

    [Fact]
    public void Same_source_lowers_to_a_rom_on_both_targets()
    {
        var baseDirectory = RepoRoot();
        var gbRom = GameBoyRomCompiler.CompileSource(HorizontalScrollSource, baseDirectory);
        var nesRom = NesRomCompiler.CompileSource(HorizontalScrollSource, baseDirectory);

        Assert.Equal(32768, gbRom.Length);
        Assert.NotEmpty(nesRom);
    }

    [Fact]
    public void Vertical_camera_is_rejected_on_nes_but_accepted_on_game_boy_via_shared_capabilities()
    {
        const string verticalSource = """
            void main() {
                world.Column(0, 1, 2);
                world.Map(1, 10, 2);
                camera.Init(1, 10, 2);
                loop {
                    video.WaitVBlank();
                    camera.SetPosition(0, 1);
                    camera.Apply();
                }
            }
            """;

        // Game Boy supports vertical scrolling: the shared model accepts it.
        var gbRom = GameBoyRomCompiler.CompileSource(verticalSource);
        Assert.Equal(32768, gbRom.Length);

        // NES is horizontal-only: the shared validator rejects the vertical axis.
        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(verticalSource));
        Assert.Contains("does not support vertical scrolling", exception.Message);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate RetroSharp repository root.");
    }
}
