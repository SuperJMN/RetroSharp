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
    public void Vertical_camera_lowers_on_nes_when_four_screen_preloaded_buffer_can_cover_the_world()
    {
        var verticalSource = TallFreeScrollSource();

        // NES free scroll uses a preloaded four-screen background for maps up to 64x60,
        // so the same diagonal camera movement has no row/column streaming cost.
        var nesRom = NesRomCompiler.CompileSource(verticalSource);
        Assert.Equal(0x08, nesRom[6] & 0x08);
    }

    [Fact]
    public void Vertical_only_camera_still_lowers_on_game_boy_and_nes()
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

        var gbRom = GameBoyRomCompiler.CompileSource(verticalSource);
        Assert.Equal(32768, gbRom.Length);

        var nesRom = NesRomCompiler.CompileSource(verticalSource);
        Assert.NotEmpty(nesRom);
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

    [Fact]
    public void Same_actor_pool_with_multi_sprite_png_compiles_on_game_boy_and_nes_when_it_fits_each_budget()
    {
        const string actorSource = """
            void main() {
                world.Column(0, 1, 2);
                world.Map(1, 10, 2);
                camera.Init(1, 10, 2);
                sprite.Asset(player, "samples/runner/assets/mario-player.png", 18, 32);
                actor.Pool(enemies, 2);
                enemy.Def(PlayerProxy, sprite: player, behavior: Walker, speed: 1, hitboxWidth: 18, hitboxHeight: 32);
                enemies[0].active = 1;
                enemies[0].kind = PlayerProxy;
                enemies[0].x = 72;
                enemies[0].xHi = 0;
                enemies[0].y = 32;

                loop {
                    video.WaitVBlank();
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

    private static string TallFreeScrollSource()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 60).Select(row => row % 4 + 1));
        return $$"""
            void main() {
                video.Init();
                world.Column(0, {{tallColumn}});
                world.Column(63, {{tallColumn}});
                world.Map(64, 0, 60);
                camera.Init(64, 0, 60);

                u8 cameraX = 0;
                u8 cameraY = 0;
                loop {
                    video.WaitVBlank();
                    cameraX += 1;
                    cameraY += 1;
                    camera.SetPosition(cameraX, cameraY);
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
