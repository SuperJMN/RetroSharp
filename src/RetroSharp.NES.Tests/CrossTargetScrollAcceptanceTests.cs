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
    public void Nes_streams_columns_for_world_maps_wider_than_one_visible_nametable()
    {
        var source = WideHorizontalScrollSource(width: 40, streamY: 0, height: 30);

        var rom = NesRomCompiler.CompileSource(source, RepoRoot());
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(0x01, rom[6] & 0x01);
        Assert.True(CountOccurrences(prg, [0x8D, 0x07, 0x20]) >= 32, "NES should emit runtime PPUDATA writes for a full streamed map column, beyond palette and startup nametable upload.");
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

    [Fact]
    public void Runner_shaped_camera_collision_lowers_on_nes()
    {
        const string collisionSource = """
            void main() {
                world.Column(0, 1, 2);
                world.Flags(0, 0, 1);
                world.Map(1, 10, 2);
                camera.Init(1, 10, 2);
                loop {
                    video.WaitVBlank();
                    u8 footY = 8;
                    u8 hit = camera.AabbTiles(72, footY, 16, 8, 1);
                }
            }
            """;

        var gbRom = GameBoyRomCompiler.CompileSource(collisionSource);
        Assert.Equal(32768, gbRom.Length);

        var nesRom = NesRomCompiler.CompileSource(collisionSource);
        Assert.NotEmpty(nesRom);
    }

    [Fact]
    public void Runner_shaped_camera_hit_top_lowers_on_nes()
    {
        const string collisionSource = """
            void main() {
                world.Column(0, 1, 2);
                world.Flags(0, 0, 1);
                world.Map(1, 10, 2);
                camera.Init(1, 10, 2);
                loop {
                    video.WaitVBlank();
                    u8 footY = 16;
                    u8 hitTop = camera.AabbHitTop(72, footY - 8, 16, 16, 1);
                }
            }
            """;

        var gbRom = GameBoyRomCompiler.CompileSource(collisionSource);
        Assert.Equal(32768, gbRom.Length);

        var nesRom = NesRomCompiler.CompileSource(collisionSource);
        Assert.NotEmpty(nesRom);
    }

    [Fact]
    public void Actor_framework_animation_and_tile_helpers_lower_on_game_boy_and_nes()
    {
        const string actorSource = """
            void main() {
                world.Column(0, 1, 2);
                world.Flags(0, 0, 1);
                world.Map(1, 10, 2);
                camera.Init(1, 10, 2);
                sprite.Asset(marker, "samples/cross-target-camera/marker.json");
                animation.Clip(walk, 0, 4);
                actor.Pool(enemies, 1);
                enemy.Def(Goomba, sprite: marker, behavior: Walker, speed: 1, animation: walk, hitboxWidth: 8, hitboxHeight: 8);
                enemies[0].active = 1;
                enemies[0].kind = Goomba;
                enemies[0].x = 72;
                enemies[0].xHi = 0;
                enemies[0].y = 16;

                loop {
                    video.WaitVBlank();
                    enemies.Update();
                    enemies.TouchTiles(0, 1);
                    enemies.LandOnTiles(4, 12, 1);
                    enemies.Draw();
                }
            }
            """;

        var gbRom = GameBoyRomCompiler.CompileSource(actorSource, RepoRoot());
        Assert.Equal(32768, gbRom.Length);

        var nesRom = NesRomCompiler.CompileSource(actorSource, RepoRoot());
        Assert.NotEmpty(nesRom);
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

    private static string WideHorizontalScrollSource(int width, int streamY, int height)
    {
        var columns = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, width).Select(index =>
                $"            world.Column({index}, {WideColumnTiles(index, height)});"));

        return $$"""
            enum World {
                Width = {{width}},
                StreamY = {{streamY}},
                Height = {{height}}
            }

            void main() {
                video.Init();
            {{columns}}
                world.Map(World.Width, World.StreamY, World.Height);
                camera.Init(World.Width, World.StreamY, World.Height);

                loop {
                    video.WaitVBlank();
                    input.Poll();
                    let cameraX = button_hold_ticks(right);
                    camera.SetPosition(cameraX, 0);
                    camera.Apply();
                }
            }
            """;
    }

    private static string WideColumnTiles(int column, int height)
    {
        return string.Join(", ", Enumerable.Range(0, height).Select(row => ((column + row) % 5) + 1));
    }

    private static int CountOccurrences(byte[] haystack, byte[] needle)
    {
        var count = 0;
        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                count++;
            }
        }

        return count;
    }
}
