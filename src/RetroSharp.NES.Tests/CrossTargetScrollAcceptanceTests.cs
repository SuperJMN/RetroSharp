namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
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

        void Main() {
            Video.Init();

            World.Column(0, 1, 2, 3, 4);
            World.Column(1, 2, 3, 4, 5);
            World.Column(2, 3, 4, 5, 1);
            World.Column(3, 4, 5, 1, 2);
            World.Column(4, 5, 1, 2, 3);
            World.Column(5, 1, 2, 3, 4);
            World.Column(6, 2, 3, 4, 5);
            World.Column(7, 3, 4, 5, 1);
            World.Map(World.Width, World.StreamY, World.Height);
            Camera.Init(World.Width, World.StreamY, World.Height);
            Sprite.Asset(marker, "samples/cross-target-camera/marker.json");

            loop {
                Video.WaitVBlank();
                Input.Poll();
                let cameraX = button_hold_ticks(right);
                u8 frame = 0;
                bool flipX = false;
                Camera.SetPosition(cameraX, 0);
                Camera.Apply();
                Sprite.Draw(marker, Marker.ScreenX, Marker.ScreenY, frame, flipX, 0);
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
    public void Free_scroll_sample_lowers_diagonal_camera_on_game_boy_and_nes()
    {
        var samplePath = RepositoryFile("samples/nes-free-scroll/freescroll.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath);
        var source = File.ReadAllText(samplePath);

        var gbOperations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var gbCamera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(gbOperations.OfType<Sdk2DOperation.SetCameraPosition>()));
        Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, gbCamera.Axes);

        var gbRom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, gbRom.Length);

        var nesRom = NesRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(0x08, nesRom[6] & 0x08);
    }

    [Fact]
    public void Wide_tall_tiled_sample_lowers_vertical_camera_on_game_boy_and_nes()
    {
        var samplePath = RepositoryFile("samples/tiled-vscroll/vscroll.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath);
        var source = File.ReadAllText(samplePath);

        var gbRom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, gbRom.Length);

        var nesRom = NesRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(0x08, nesRom[6] & 0x08);
    }

    [Fact]
    public void Tiled_free_scroll_sample_lowers_diagonal_camera_on_game_boy_and_nes()
    {
        var samplePath = RepositoryFile("samples/tiled-free-scroll/free-scroll.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath);
        var source = File.ReadAllText(samplePath);

        var gbOperations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var gbCamera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(gbOperations.OfType<Sdk2DOperation.SetCameraPosition>()));
        Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, gbCamera.Axes);

        var gbRom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, gbRom.Length);

        var nesRom = NesRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(0x08, nesRom[6] & 0x08);
    }

    [Fact]
    public void Dead_zone_follow_sample_lowers_diagonal_camera_on_game_boy_and_nes()
    {
        var samplePath = RepositoryFile("samples/deadzone-follow/deadzone.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath);
        var source = File.ReadAllText(samplePath);

        Assert.Contains("World.Load(\"deadzone.tmj\");", source, StringComparison.Ordinal);
        Assert.Contains("static class DeadZone", source, StringComparison.Ordinal);
        Assert.Contains("static class CameraBounds", source, StringComparison.Ordinal);

        var gbOperations = GameBoyRomCompiler.CollectSdkOperations(source, sampleDirectory);
        var gbCamera = Assert.IsType<Sdk2DOperation.SetCameraPosition>(
            Assert.Single(gbOperations.OfType<Sdk2DOperation.SetCameraPosition>()));
        Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, gbCamera.Axes);

        var gbRom = GameBoyRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(32768, gbRom.Length);

        var program = BuildNesProgram(source, sampleDirectory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        Assert.Equal(64, worldMap.Width);
        Assert.Equal(60, worldMap.Height);
        AssertHasTileInNametable(program, 0);
        AssertHasTileInNametable(program, 1);
        AssertHasTileInNametable(program, 2);
        AssertHasTileInNametable(program, 3);

        var nesRom = NesRomCompiler.CompileSource(source, sampleDirectory);
        Assert.Equal(0x08, nesRom[6] & 0x08);
    }

    [Fact]
    public void Tiled_free_scroll_sample_populates_four_screen_nametable_tiles_and_attributes_on_nes()
    {
        var samplePath = RepositoryFile("samples/tiled-free-scroll/free-scroll.rs");
        var sampleDirectory = Path.GetDirectoryName(samplePath);
        var source = File.ReadAllText(samplePath);

        var program = BuildNesProgram(source, sampleDirectory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        Assert.Equal(50, worldMap.Width);
        Assert.Equal(60, worldMap.Height);

        Assert.Equal(worldTileGrid.TileIdAt(0, 0), NameTableTileAt(program, 0, 0));
        Assert.Equal(worldTileGrid.TileIdAt(49, 0), NameTableTileAt(program, 49, 0));
        Assert.Equal(worldTileGrid.TileIdAt(0, 0), NameTableTileAt(program, 50, 0));
        Assert.Equal(worldTileGrid.TileIdAt(13, 0), NameTableTileAt(program, 63, 0));
        Assert.Equal(worldTileGrid.TileIdAt(0, 59), NameTableTileAt(program, 0, 59));
        Assert.Equal(worldTileGrid.TileIdAt(49, 59), NameTableTileAt(program, 49, 59));
        Assert.Equal(worldTileGrid.TileIdAt(0, 59), NameTableTileAt(program, 50, 59));
        Assert.Equal(worldTileGrid.TileIdAt(13, 59), NameTableTileAt(program, 63, 59));

        AssertHasTileInNametable(program, 0);
        AssertHasTileInNametable(program, 1);
        AssertHasTileInNametable(program, 2);
        AssertHasTileInNametable(program, 3);

        AssertHasAttributeInNametable(program, 0);
        AssertHasAttributeInNametable(program, 1);
        AssertHasAttributeInNametable(program, 2);
        AssertHasAttributeInNametable(program, 3);

        var nesRom = NesRomCompiler.CompileSource(source, sampleDirectory);
        var prg = nesRom.Skip(16).Take(32 * 1024).ToArray();
        Assert.True(CountOccurrences(prg, [0xA5, 0xE8, 0xC9, 0x1E]) > 0, "camera_apply should derive the vertical nametable bit from the four-screen buffer row.");
        Assert.True(CountOccurrences(prg, [0xA5, 0xEA, 0x29, 0x07]) > 0, "camera_apply should preserve fine Y when wrapping NES scroll Y at 240 pixels.");
    }

    [Fact]
    public void Vertical_only_camera_still_lowers_on_game_boy_and_nes()
    {
        const string verticalSource = """
            void Main() {
                World.Column(0, 1, 2);
                World.Map(1, 10, 2);
                Camera.Init(1, 10, 2);
                loop {
                    Video.WaitVBlank();
                    Camera.SetPosition(0, 1);
                    Camera.Apply();
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
            void Main() {
                World.Column(0, 1, 2);
                World.Flags(0, 0, 1);
                World.Map(1, 10, 2);
                Camera.Init(1, 10, 2);
                loop {
                    Video.WaitVBlank();
                    u8 footY = 8;
                    u8 hit = Camera.AabbTiles(72, footY, 16, 8, 1);
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
            void Main() {
                World.Column(0, 1, 2);
                World.Flags(0, 0, 1);
                World.Map(1, 10, 2);
                Camera.Init(1, 10, 2);
                loop {
                    Video.WaitVBlank();
                    u8 footY = 16;
                    u8 hitTop = Camera.AabbHitTop(72, footY - 8, 16, 16, 1);
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
            void Main() {
                World.Column(0, 1, 2);
                World.Flags(0, 0, 1);
                World.Map(1, 10, 2);
                Camera.Init(1, 10, 2);
                Sprite.Asset(marker, "samples/cross-target-camera/marker.json");
                Animation.Clip(walk, 0, 4);
                Actors.Pool(enemies, 1);
                Enemies.Def(Goomba, sprite: marker, behavior: Walker, speed: 1, animation: walk, hitboxWidth: 8, hitboxHeight: 8);
                enemies[0].active = 1;
                enemies[0].kind = Goomba;
                enemies[0].x = 72;
                enemies[0].xHi = 0;
                enemies[0].y = 16;

                loop {
                    Video.WaitVBlank();
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
            void Main() {
                World.Column(0, 1, 2);
                World.Map(1, 10, 2);
                Camera.Init(1, 10, 2);
                Sprite.Asset(player, "samples/runner/assets/mario-player.png", 18, 32);
                Actors.Pool(enemies, 2);
                Enemies.Def(PlayerProxy, sprite: player, behavior: Walker, speed: 1, hitboxWidth: 18, hitboxHeight: 32);
                enemies[0].active = 1;
                enemies[0].kind = PlayerProxy;
                enemies[0].x = 72;
                enemies[0].xHi = 0;
                enemies[0].y = 32;

                loop {
                    Video.WaitVBlank();
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

    private static string RepositoryFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
        }

        return path;
    }

    private static string WideHorizontalScrollSource(int width, int streamY, int height)
    {
        var columns = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, width).Select(index =>
                $"            World.Column({index}, {WideColumnTiles(index, height)});"));

        return $$"""
            enum World {
                Width = {{width}},
                StreamY = {{streamY}},
                Height = {{height}}
            }

            void Main() {
                Video.Init();
            {{columns}}
                World.Map(World.Width, World.StreamY, World.Height);
                Camera.Init(World.Width, World.StreamY, World.Height);

                loop {
                    Video.WaitVBlank();
                    Input.Poll();
                    let cameraX = button_hold_ticks(right);
                    Camera.SetPosition(cameraX, 0);
                    Camera.Apply();
                }
            }
            """;
    }

    private static string TallFreeScrollSource()
    {
        var tallColumn = string.Join(", ", Enumerable.Range(0, 60).Select(row => row % 4 + 1));
        return $$"""
            void Main() {
                Video.Init();
                World.Column(0, {{tallColumn}});
                World.Column(63, {{tallColumn}});
                World.Map(64, 0, 60);
                Camera.Init(64, 0, 60);

                u8 cameraX = 0;
                u8 cameraY = 0;
                loop {
                    Video.WaitVBlank();
                    cameraX += 1;
                    cameraY += 1;
                    Camera.SetPosition(cameraX, cameraY);
                    Camera.Apply();
                }
            }
            """;
    }

    private static string WideColumnTiles(int column, int height)
    {
        return string.Join(", ", Enumerable.Range(0, height).Select(row => ((column + row) % 5) + 1));
    }

    private static NesVideoProgram BuildNesProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(SdkLibrarySource.Merge(NesTarget.Intrinsics, source));
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : null);
        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        var lowered = ActorFrameworkLowerer.Lower(targetProgram, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        return NesVideoProgram.FromProgram(lowered, baseDirectory);
    }

    private static byte NameTableTileAt(NesVideoProgram program, int x, int y)
    {
        var nameTableX = x / 32;
        var nameTableY = y / 30;
        var nameTableBase = (nameTableY * 2 + nameTableX) * 1024;
        return program.NameTable[nameTableBase + y % 30 * 32 + x % 32];
    }

    private static void AssertHasTileInNametable(NesVideoProgram program, int nameTable)
    {
        Assert.Contains(
            program.NameTable.Skip(nameTable * 1024).Take(960),
            value => value != 0);
    }

    private static void AssertHasAttributeInNametable(NesVideoProgram program, int nameTable)
    {
        Assert.Contains(
            program.NameTable.Skip(nameTable * 1024 + 960).Take(64),
            value => value != 0);
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
