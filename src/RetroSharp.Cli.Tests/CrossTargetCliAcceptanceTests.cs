namespace RetroSharp.Cli.Tests;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

public sealed class CrossTargetCliAcceptanceTests
{
    [Fact]
    public void Cli_world_budget_report_is_explicit_deterministic_json_and_default_output_is_byte_compatible()
    {
        using var workspace = TemporaryWorkspace();
        var source = RepositoryFile("samples/tiled-free-scroll/free-scroll.rs");
        var map = RepositoryFile("samples/tiled-free-scroll/free-scroll.tmj");
        var rom = Path.Combine(workspace.Path, "free-scroll.gb");

        var normal = RunCli("--target", "gb", "--out", rom, source);

        Assert.Equal(0, normal.ExitCode);
        Assert.Equal(string.Empty, normal.StandardOutput);
        Assert.Equal($"Wrote Game Boy ROM: {rom}{Environment.NewLine}", normal.StandardError);

        var nesRom = Path.Combine(workspace.Path, "free-scroll.nes");
        var normalNes = RunCli("--target", "nes", "--out", nesRom, source);

        Assert.Equal(0, normalNes.ExitCode);
        Assert.Equal(string.Empty, normalNes.StandardOutput);
        Assert.Equal($"Wrote NES ROM: {nesRom}{Environment.NewLine}", normalNes.StandardError);

        var first = RunCli("--target", "gb", "--world-budget-report", map);
        var second = RunCli("--target", "gb", "--world-budget-report", map);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(string.Empty, first.StandardError);
        Assert.Equal(first.StandardOutput, second.StandardOutput);
        using var json = JsonDocument.Parse(first.StandardOutput);
        var root = json.RootElement;
        Assert.Equal("retrosharp.world-budget/v1", root.GetProperty("schema").GetString());
        Assert.Equal("gb", root.GetProperty("target").GetString());
        Assert.Equal(50, root.GetProperty("world").GetProperty("hardwareWidth").GetInt32());
        Assert.Equal(60, root.GetProperty("world").GetProperty("hardwareHeight").GetInt32());
        Assert.True(root.GetProperty("pack").GetProperty("totalBytes").GetInt32() > 0);
        Assert.Equal(
            new[] { "addressing", "rom-prg", "chr-tile-count", "staging-ram", "vblank" },
            root.GetProperty("diagnostics").EnumerateArray()
                .Select(item => item.GetProperty("category").GetString()));
    }

    [Theory]
    [InlineData("gb", 4_299, 3_000, 112, 2_550, 770, 312, 82, 298, 554, 8_192, 21, 0)]
    [InlineData("nes", 4_317, 3_000, 112, 2_762, 770, 312, 90, 338, 594, 2_048, 32, 9)]
    public void Cli_world_budget_report_uses_real_small_and_full_stage1_packs(
        string target,
        int smallPackBytes,
        int smallVisualStoredBytes,
        int smallCollisionStoredBytes,
        int stage1PackBytes,
        int stage1VisualStoredBytes,
        int stage1CollisionStoredBytes,
        int stage1BackgroundTiles,
        int stage1StagingBytes,
        int stagingLimitBytes,
        int physicalRamCapacityBytes,
        int tileWrites,
        int attributeWrites)
    {
        var small = RunCli(
            "--target",
            target,
            "--world-budget-report",
            RepositoryFile("samples/tiled-free-scroll/free-scroll.tmj"));
        var smallSecond = RunCli(
            "--target",
            target,
            "--world-budget-report",
            RepositoryFile("samples/tiled-free-scroll/free-scroll.tmj"));

        Assert.Equal(0, small.ExitCode);
        Assert.Equal(string.Empty, small.StandardError);
        Assert.Equal(small.StandardOutput, smallSecond.StandardOutput);
        using var smallJson = JsonDocument.Parse(small.StandardOutput);
        Assert.Equal(smallPackBytes, smallJson.RootElement.GetProperty("pack").GetProperty("totalBytes").GetInt32());
        Assert.Equal(smallVisualStoredBytes, smallJson.RootElement.GetProperty("pack").GetProperty("visualStoredBytes").GetInt32());
        Assert.Equal(smallCollisionStoredBytes, smallJson.RootElement.GetProperty("pack").GetProperty("collisionStoredBytes").GetInt32());

        using var stage1Workspace = CreateNormalizedFullStage1();
        var stage1 = RunCli(
            "--target",
            target,
            "--world-budget-report",
            Path.Combine(stage1Workspace.Path, "stage1.normalized.tmj"));
        var stage1Second = RunCli(
            "--target",
            target,
            "--world-budget-report",
            Path.Combine(stage1Workspace.Path, "stage1.normalized.tmj"));

        Assert.Equal(0, stage1.ExitCode);
        Assert.Equal(string.Empty, stage1.StandardError);
        Assert.Equal(stage1.StandardOutput, stage1Second.StandardOutput);
        using var stage1Json = JsonDocument.Parse(stage1.StandardOutput);
        var root = stage1Json.RootElement;
        Assert.Equal(156, root.GetProperty("world").GetProperty("sourceWidth").GetInt32());
        Assert.Equal(20, root.GetProperty("world").GetProperty("sourceHeight").GetInt32());
        Assert.Equal(312, root.GetProperty("world").GetProperty("hardwareWidth").GetInt32());
        Assert.Equal(40, root.GetProperty("world").GetProperty("hardwareHeight").GetInt32());
        Assert.Equal(12_480, root.GetProperty("world").GetProperty("hardwareCells").GetInt32());
        Assert.Equal(3_120, root.GetProperty("world").GetProperty("metatilePlacements").GetInt32());
        Assert.Equal(53, root.GetProperty("world").GetProperty("uniqueVisualMetatiles").GetInt32());
        Assert.Equal(60, root.GetProperty("world").GetProperty("chunks").GetInt32());
        Assert.Equal(stage1PackBytes, root.GetProperty("pack").GetProperty("totalBytes").GetInt32());
        Assert.Equal(
            (stage1VisualStoredBytes, stage1CollisionStoredBytes),
            (root.GetProperty("pack").GetProperty("visualStoredBytes").GetInt32(),
             root.GetProperty("pack").GetProperty("collisionStoredBytes").GetInt32()));
        Assert.Equal(stage1BackgroundTiles, root.GetProperty("targetTiles").GetProperty("generatedBackgroundTiles").GetInt32());
        Assert.Equal(stage1StagingBytes, root.GetProperty("stagingRam").GetProperty("usedBytes").GetInt32());
        Assert.Equal(stagingLimitBytes, root.GetProperty("stagingRam").GetProperty("limitBytes").GetInt32());
        Assert.Equal(physicalRamCapacityBytes, root.GetProperty("stagingRam").GetProperty("physicalRamCapacityBytes").GetInt32());
        var stagingDiagnostic = Assert.Single(
            root.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("category").GetString() == "staging-ram");
        Assert.Equal("worldpack-v1-staging-maximum", stagingDiagnostic.GetProperty("profile").GetString());
        Assert.Equal(tileWrites, root.GetProperty("vblank").GetProperty("tileWritesUsed").GetInt32());
        Assert.Equal(attributeWrites, root.GetProperty("vblank").GetProperty("attributeWritesUsed").GetInt32());
        Assert.True(root.GetProperty("cartridge").GetProperty("romPrgBytesUsed").GetInt32() > 0);
        Assert.True(root.GetProperty("cartridge").GetProperty("requiredBanks").GetInt32() > 0);
        Assert.Contains(
            target == "nes" ? "nes-mmc3-tvrom-v1-accepted-future" : "gb-simple-mbc1-current",
            root.GetProperty("acceptedProfiles").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("selectedProfile").ValueKind);

        if (target == "nes")
        {
            var tiles = root.GetProperty("targetTiles");
            Assert.Equal(16_384, tiles.GetProperty("acceptedFuturePhysicalChrCapacityBytes").GetInt32());
            Assert.Equal(8_192, tiles.GetProperty("currentPhysicalChrCapacityBytes").GetInt32());
            Assert.Equal(8_192, tiles.GetProperty("residentChrByteLimit").GetInt32());
            Assert.Equal(256, tiles.GetProperty("tileIndexLimit").GetInt32());
            var profiles = root.GetProperty("profileRequirements").EnumerateArray().ToArray();
            var current = Assert.Single(profiles, profile => profile.GetProperty("name").GetString() == "nes-mapper-0-current");
            var future = Assert.Single(profiles, profile => profile.GetProperty("name").GetString() == "nes-mmc3-tvrom-v1-accepted-future");
            Assert.True(current.GetProperty("implementedByCurrentCompiler").GetBoolean());
            Assert.Equal(32_768, current.GetProperty("bankBytes").GetInt32());
            Assert.False(future.GetProperty("implementedByCurrentCompiler").GetBoolean());
            Assert.Equal(8_192, future.GetProperty("bankBytes").GetInt32());
            Assert.Equal(65_536, future.GetProperty("romPrgAllocationBytes").GetInt32());
            Assert.Equal(16_384, future.GetProperty("physicalChrCapacityBytes").GetInt32());
            Assert.Equal(8_192, future.GetProperty("residentChrByteLimit").GetInt32());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("cartridge").GetProperty("allocatedRomBytes").ValueKind);
        }

        var chrDiagnostic = Assert.Single(
            root.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("category").GetString() == "chr-tile-count");
        Assert.Equal(6 + stage1BackgroundTiles, chrDiagnostic.GetProperty("usage").GetProperty("tileIndexes").GetInt32());
        Assert.Equal((6 + stage1BackgroundTiles) * 16, chrDiagnostic.GetProperty("usage").GetProperty("residentChrBytes").GetInt32());
        Assert.Equal(256, chrDiagnostic.GetProperty("limit").GetProperty("tileIndexes").GetInt32());
    }

    [Fact]
    public void Cli_builds_portable_sample_for_game_boy_and_nes_under_temp_directory()
    {
        using var workspace = TemporaryWorkspace();
        var sample = RepositoryFile("samples/cross-target-camera/camera.rs");

        var gameBoyRom = Path.Combine(workspace.Path, "cross-target.gb");
        var gameBoy = RunCli("--target", "gb", "--out", gameBoyRom, sample);

        Assert.Equal(0, gameBoy.ExitCode);
        Assert.True(File.Exists(gameBoyRom), gameBoy.CombinedOutput);
        Assert.Equal(32768, new FileInfo(gameBoyRom).Length);
        Assert.Contains("Wrote Game Boy ROM:", gameBoy.CombinedOutput, StringComparison.Ordinal);

        var nesRom = Path.Combine(workspace.Path, "cross-target.nes");
        var nes = RunCli("--target", "nes", "--out", nesRom, sample);

        Assert.Equal(0, nes.ExitCode);
        Assert.True(File.Exists(nesRom), nes.CombinedOutput);
        Assert.Equal(ExpectedRomSize("nes"), new FileInfo(nesRom).Length);
        Assert.Contains("Wrote NES ROM:", nes.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_builds_actor_framework_sample_for_game_boy_and_nes_under_temp_directory()
    {
        using var workspace = TemporaryWorkspace();
        var sample = RepositoryFile("samples/actor-framework/actors.rs");
        var source = File.ReadAllText(sample);

        Assert.Contains("""World.Load("actors.tmj");""", source);
        Assert.Contains("""Actors.SpawnLayer(enemies, "actors.tmj", "actors");""", source);
        Assert.Contains("""Camera.SetPosition(cameraX, 0);""", source);
        Assert.DoesNotContain("enemies[0].kind", source);

        var gameBoyRom = Path.Combine(workspace.Path, "actors.gb");
        var gameBoy = RunCli("--target", "gb", "--out", gameBoyRom, sample);

        Assert.Equal(0, gameBoy.ExitCode);
        Assert.True(File.Exists(gameBoyRom), gameBoy.CombinedOutput);
        Assert.Equal(32768, new FileInfo(gameBoyRom).Length);

        var nesRom = Path.Combine(workspace.Path, "actors.nes");
        var nes = RunCli("--target", "nes", "--out", nesRom, sample);

        Assert.Equal(0, nes.ExitCode);
        Assert.True(File.Exists(nesRom), nes.CombinedOutput);
        Assert.Equal(ExpectedRomSize("nes"), new FileInfo(nesRom).Length);
    }

    [Fact]
    public void Cli_builds_runner_projectile_sample_for_game_boy_and_nes_under_temp_directory()
    {
        using var workspace = TemporaryWorkspace();
        const string projectRelativePath = "samples/runner-projectile/runner-projectile.retrosharp.json";
        var sample = RepositoryFile(projectRelativePath);
        var source = File.ReadAllText(RepositoryFile("samples/runner-projectile/src/main.rs"));
        var manifest = LoadManifest();
        var manifestEntry = Assert.Single(manifest.Samples, entry => entry.Path == projectRelativePath);

        Assert.Equal(new[] { "gb", "nes" }, manifestEntry.Targets);
        Assert.Contains("static class Mario", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static class Hero", source, StringComparison.Ordinal);
        Assert.Contains("const u8 ShotY = 116;", source, StringComparison.Ordinal);
        Assert.Contains("""Sprite.Asset(mario_player, "../runner/assets/mario-player.png", 18, 32);""", source, StringComparison.Ordinal);
        Assert.Contains("""Sprite.Asset(mario_shot, "assets/mario-shot.json");""", source, StringComparison.Ordinal);
        Assert.Contains("""Projectiles.Def(MarioFireball, team: Hero""", source, StringComparison.Ordinal);
        Assert.Contains("""tileCollision: Bounce""", source, StringComparison.Ordinal);
        Assert.Contains("""shots.TouchTiles(0, 1);""", source, StringComparison.Ordinal);
        Assert.Contains("""Input.WasPressed(Button.B)""", source, StringComparison.Ordinal);
        Assert.Contains("""shots.Request(MarioFireball""", source, StringComparison.Ordinal);

        var gameBoyRom = Path.Combine(workspace.Path, "runner-projectile.gb");
        var gameBoy = RunCli("--target", "gb", "--out", gameBoyRom, sample);

        Assert.Equal(0, gameBoy.ExitCode);
        Assert.True(File.Exists(gameBoyRom), gameBoy.CombinedOutput);
        Assert.Equal(32768, new FileInfo(gameBoyRom).Length);

        var nesRom = Path.Combine(workspace.Path, "runner-projectile.nes");
        var nes = RunCli("--target", "nes", "--out", nesRom, sample);

        Assert.Equal(0, nes.ExitCode);
        Assert.True(File.Exists(nesRom), nes.CombinedOutput);
        Assert.Equal(ExpectedRomSize("nes"), new FileInfo(nesRom).Length);
    }

    [Fact]
    public void Cli_builds_every_manifest_sample_for_declared_targets()
    {
        using var workspace = TemporaryWorkspace();
        var manifest = LoadManifest();

        foreach (var sample in manifest.Samples)
        {
            foreach (var target in sample.Targets)
            {
                var extension = target == "nes" ? ".nes" : ".gb";
                var output = Path.Combine(
                    workspace.Path,
                    Path.GetFileNameWithoutExtension(sample.Path).Replace('.', '-') + "-" + target + extension);
                var args = new List<string>
                {
                    "--target",
                    target,
                };
                foreach (var libraryPath in sample.LibraryPaths ?? [])
                {
                    args.Add("--lib-path");
                    args.Add(RepositoryPath(libraryPath));
                }

                args.AddRange(["--out", output, RepositoryFile(sample.Path)]);
                var result = RunCli(args.ToArray());

                Assert.Equal(0, result.ExitCode);
                Assert.True(File.Exists(output), result.CombinedOutput);
                Assert.Equal(ExpectedRomSize(sample.Path, target), new FileInfo(output).Length);
            }
        }
    }

    [Fact]
    public void Sample_rom_script_dry_run_lists_tracked_rom_outputs_by_default()
    {
        var result = RunProcess("python3", RepositoryFile("tools/gameboy/generate_sample_roms.py"), "--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("samples/gameboy-drawing/drawing.rs", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("samples/gameboy-drawing/drawing.gb", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("samples/runner/runner.retrosharp.json", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("--out samples/runner/bin/runner.gb", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("--target nes", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("--out samples/runner/bin/runner.nes", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("samples/runner-projectile/runner-projectile.retrosharp.json", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("--out samples/runner-projectile/bin/runner-projectile.gb", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("--out samples/runner-projectile/bin/runner-projectile.nes", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("--out samples/runner/runner.gb", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("--out samples/runner/runner.nes", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("--lib-path samples/runner/lib", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("samples/gameboy-hud/hud.rs", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("samples/runner/diagnostics/00-static-background.rs", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_project_builds_game_sources_without_a_local_library_package()
    {
        using var workspace = TemporaryWorkspace();
        var projectPath = RepositoryFile("samples/runner/runner.retrosharp.json");
        using var projectJson = JsonDocument.Parse(File.ReadAllText(projectPath));
        var root = projectJson.RootElement;
        var sourcePaths = root.GetProperty("sources")
            .EnumerateArray()
            .Select(source => source.GetString())
            .ToArray();
        var targets = root.GetProperty("targets")
            .EnumerateArray()
            .Select(target => target.GetString())
            .ToArray();
        var outputs = root.GetProperty("outputs");

        Assert.DoesNotContain(root.EnumerateObject(), property => property.NameEquals("target"));
        Assert.DoesNotContain(root.EnumerateObject(), property => property.NameEquals("libraryPaths"));
        Assert.Equal("Runner", root.GetProperty("rootNamespace").GetString());
        Assert.Equal("src", root.GetProperty("sourceRoot").GetString());
        Assert.Equal("physical", root.GetProperty("namespaceMode").GetString());
        Assert.Equal(new[] { "gb", "nes" }, targets);
        Assert.Equal("bin/runner.gb", outputs.GetProperty("gb").GetString());
        Assert.Equal("bin/runner.nes", outputs.GetProperty("nes").GetString());
        Assert.Contains("src/level/constants.rs", sourcePaths);
        Assert.Contains("src/player/state.rs", sourcePaths);
        Assert.Contains("src/camera/state.rs", sourcePaths);
        Assert.Contains("src/frame/state.rs", sourcePaths);
        Assert.Contains("src/main.rs", sourcePaths);
        Assert.DoesNotContain("runner.rs", sourcePaths);
        Assert.False(File.Exists(Path.Combine(RepositoryRoot(), "samples/runner/runner.rs")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot(), "samples/runner/music")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot(), "samples/runner/maps")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "samples/runner/assets/music/runner.gb.vgz")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "samples/runner/assets/music/runner.nes.vgz")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "samples/runner/assets/maps/stage1.tmj")));
        var mainSource = File.ReadAllText(RepositoryFile("samples/runner/src/main.rs"));
        Assert.DoesNotContain("import Runner.Framework;", mainSource, StringComparison.Ordinal);
        Assert.Contains("""Music.Asset(runner_theme, "assets/music/runner.vgz");""", mainSource, StringComparison.Ordinal);
        Assert.Contains("""World.Load("assets/maps/stage1.tmj");""", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("stage1.playable.tmj", mainSource, StringComparison.Ordinal);

        var outputPath = Path.Combine(workspace.Path, "runner.gb");
        var result = RunCli("--target", "gb", "--out", outputPath, projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(131072, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_reports_unsupported_feature_diagnostics_with_nonzero_exit_code()
    {
        using var workspace = TemporaryWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "unsupported-hud.rs");
        var outputPath = Path.Combine(workspace.Path, "unsupported-hud.nes");
        File.WriteAllText(
            sourcePath,
            """
            import RetroSharp.Portable2D;

            void Main() {
                Video.Init();
                Hud.SetTile(window, 0, 0, 1);
                return;
            }
            """);

        var result = RunCli("--target", "nes", "--out", outputPath, sourcePath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(outputPath), result.CombinedOutput);
        Assert.Contains("Target 'nes' does not support Window HUD. Use disable HUD for this target.", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_builds_game_boy_rom_with_registered_sdk_plugin_option()
    {
        using var workspace = TemporaryWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "ground-probe.rs");
        var outputPath = Path.Combine(workspace.Path, "ground-probe.gb");
        File.WriteAllText(
            sourcePath,
            """
            import RetroSharp.Platformer2D;

            void Main()
            {
                Platformer.GroundProbe();
            }
            """);

        var result = RunCli("--target", "gb", "--sdk-plugin", "RetroSharp.Platformer2D", "--out", outputPath, sourcePath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_reports_plugin_feature_unsupported_on_nes_with_nonzero_exit_code()
    {
        using var workspace = TemporaryWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "ground-probe.rs");
        var outputPath = Path.Combine(workspace.Path, "ground-probe.nes");
        File.WriteAllText(
            sourcePath,
            """
            import RetroSharp.Platformer2D;

            void Main()
            {
                Platformer.GroundProbe();
            }
            """);

        var result = RunCli("--target", "nes", "--sdk-plugin", "RetroSharp.Platformer2D", "--out", outputPath, sourcePath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(outputPath), result.CombinedOutput);
        Assert.Contains(
            "Target 'nes' does not support SDK plugin feature 'RetroSharp.Platformer2D.GroundProbe'",
            result.CombinedOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_builds_game_boy_rom_with_sdk_plugin_declared_in_project_manifest()
    {
        using var workspace = TemporaryWorkspace();
        File.WriteAllText(
            Path.Combine(workspace.Path, "ground-probe.rs"),
            """
            import RetroSharp.Platformer2D;

            void Main()
            {
                Platformer.GroundProbe();
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "ground-probe.retrosharp.json");
        File.WriteAllText(
            projectPath,
            """
            {
                "target": "gb",
                "sources": ["ground-probe.rs"],
                "plugins": ["RetroSharp.Platformer2D"],
                "output": "ground-probe.gb"
            }
            """);

        var outputPath = Path.Combine(workspace.Path, "ground-probe.gb");
        var result = RunCli("--target", "gb", "--out", outputPath, projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_reports_unknown_sdk_plugin_with_nonzero_exit_code()
    {
        using var workspace = TemporaryWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "probe.rs");
        var outputPath = Path.Combine(workspace.Path, "probe.gb");
        File.WriteAllText(
            sourcePath,
            """
            void Main()
            {
                return;
            }
            """);

        var result = RunCli("--target", "gb", "--sdk-plugin", "RetroSharp.DoesNotExist", "--out", outputPath, sourcePath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unknown SDK plugin 'RetroSharp.DoesNotExist'", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_builds_game_boy_rom_with_local_library_manifest_from_lib_path()
    {
        using var workspace = TemporaryWorkspace();
        var libraryRoot = Path.Combine(workspace.Path, "lib");
        WriteLibraryPackage(
            libraryRoot,
            "acme-wait",
            "Acme.Wait",
            "wait.rs",
            """
            [target("gb")]
            [intrinsic("wait_frame")]
            extern void acme_wait_frame();

            [target("nes")]
            [intrinsic("wait_frame")]
            extern void acme_wait_frame();

            class AcmeWait
            {
                static inline void Tick()
                {
                    acme_wait_frame();
                }
            }
            """,
            "gb",
            "nes");
        var sourcePath = Path.Combine(workspace.Path, "use-acme.rs");
        File.WriteAllText(
            sourcePath,
            """
            import Acme.Wait;

            void Main() {
                AcmeWait.Tick();
            }
            """);
        var outputPath = Path.Combine(workspace.Path, "use-acme.gb");

        var result = RunCli("--target", "gb", "--lib-path", libraryRoot, "--out", outputPath, sourcePath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_builds_game_boy_rom_with_physical_namespace_library_manifest_from_lib_path()
    {
        using var workspace = TemporaryWorkspace();
        var packageDirectory = Path.Combine(workspace.Path, "lib", "acme-motion");
        var sourceRoot = Path.Combine(packageDirectory, "src");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "player"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "camera"));
        File.WriteAllText(
            Path.Combine(sourceRoot, "player", "rules.rs"),
            """
            static class Rules
            {
                const Start = 1;
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceRoot, "camera", "rules.rs"),
            """
            static class Rules
            {
                const Start = 2;
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceRoot, "api.rs"),
            """
            [target("gb")]
            [intrinsic("wait_frame")]
            extern void acme_motion_wait_frame();

            class MotionApi
            {
                static inline void Tick()
                {
                    const playerStart = Player.Rules.Start;
                    const cameraStart = Camera.Rules.Start;
                    acme_motion_wait_frame();
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(packageDirectory, "retrosharp-library.json"),
            """
            {
              "import": "Acme.Motion",
              "rootNamespace": "Acme.Motion",
              "sourceRoot": "src",
              "namespaceMode": "physical",
              "sources": [
                "src/player/rules.rs",
                "src/camera/rules.rs",
                "src/api.rs"
              ],
              "targets": [ "gb" ]
            }
            """);
        var sourcePath = Path.Combine(workspace.Path, "use-acme-motion.rs");
        File.WriteAllText(
            sourcePath,
            """
            import Acme.Motion;

            void Main() {
                const playerStart = Player.Rules.Start;
                const cameraStart = Camera.Rules.Start;
                MotionApi.Tick();
            }
            """);
        var outputPath = Path.Combine(workspace.Path, "use-acme-motion.gb");

        var result = RunCli("--target", "gb", "--lib-path", Path.Combine(workspace.Path, "lib"), "--out", outputPath, sourcePath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_reports_missing_library_path_with_nonzero_exit_code()
    {
        using var workspace = TemporaryWorkspace();
        var sourcePath = Path.Combine(workspace.Path, "main.rs");
        var missingLibraryPath = Path.Combine(workspace.Path, "missing-lib");
        var outputPath = Path.Combine(workspace.Path, "main.gb");
        File.WriteAllText(
            sourcePath,
            """
            void Main() {
            }
            """);

        var result = RunCli("--target", "gb", "--lib-path", missingLibraryPath, "--out", outputPath, sourcePath);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(outputPath), result.CombinedOutput);
        Assert.Contains($"Library path '{missingLibraryPath}' does not exist.", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_builds_game_boy_rom_from_retrosharp_project_file()
    {
        using var workspace = TemporaryWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "Program.rs"),
            """
            void Main() {
                Startup.Wait();
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "Startup.rs"),
            """
            class Startup
            {
                static inline void Wait()
                {
                    Video.WaitVBlank();
                }
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "retrosharp.json");
        var outputPath = Path.Combine(workspace.Path, "bin", "runner.gb");
        File.WriteAllText(
            projectPath,
            """
            {
              "target": "gb",
              "output": "bin/runner.gb",
              "libraries": [
                "RetroSharp.Portable2D"
              ],
              "sources": [
                "src/Program.rs",
                "src/Startup.rs"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
        Assert.Contains($"Wrote Game Boy ROM: {outputPath}", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_builds_all_declared_project_targets_to_declared_outputs()
    {
        using var workspace = TemporaryWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "main.rs"),
            """
            void Main() {
                Video.WaitVBlank();
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "runner.retrosharp.json");
        var gameBoyOutput = Path.Combine(workspace.Path, "bin", "runner.gb");
        var nesOutput = Path.Combine(workspace.Path, "bin", "runner.nes");
        File.WriteAllText(
            projectPath,
            """
            {
              "targets": [ "gb", "nes" ],
              "outputs": {
                "gb": "bin/runner.gb",
                "nes": "bin/runner.nes"
              },
              "libraries": [
                "RetroSharp.Portable2D"
              ],
              "sources": [
                "src/main.rs"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(gameBoyOutput), result.CombinedOutput);
        Assert.True(File.Exists(nesOutput), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(gameBoyOutput).Length);
        Assert.Equal(ExpectedRomSize("nes"), new FileInfo(nesOutput).Length);
        Assert.Contains($"Wrote Game Boy ROM: {gameBoyOutput}", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains($"Wrote NES ROM: {nesOutput}", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_project_physical_namespaces_follow_source_folders()
    {
        using var workspace = TemporaryWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "player"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "camera"));
        File.WriteAllText(
            Path.Combine(sourceDirectory, "player", "rules.rs"),
            """
            static class Rules
            {
                const Start = 1;
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "camera", "rules.rs"),
            """
            static class Rules
            {
                const Start = 2;
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "main.rs"),
            """
            void Main() {
                const playerStart = Player.Rules.Start;
                const cameraStart = Camera.Rules.Start;
                Video.WaitVBlank();
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "runner.retrosharp.json");
        var outputPath = Path.Combine(workspace.Path, "bin", "runner.gb");
        File.WriteAllText(
            projectPath,
            """
            {
              "target": "gb",
              "output": "bin/runner.gb",
              "rootNamespace": "Game",
              "sourceRoot": "src",
              "namespaceMode": "physical",
              "libraries": [
                "RetroSharp.Portable2D"
              ],
              "sources": [
                "src/player/rules.rs",
                "src/camera/rules.rs",
                "src/main.rs"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_project_file_accepts_root_qualified_physical_namespace_types_and_static_calls()
    {
        using var workspace = TemporaryWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "player"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "frame"));
        File.WriteAllText(
            Path.Combine(sourceDirectory, "player", "state.rs"),
            """
            class PlayerState
            {
                u8 x;

                inline void Reset()
                {
                    x = 0;
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "frame", "presenter.rs"),
            """
            inline void PresentFrame(Runner.Player.PlayerState player)
            {
                Video.WaitVBlank();
            }

            class FramePresenter
            {
                static inline void Present(Runner.Player.PlayerState player)
                {
                    PresentFrame(player);
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "main.rs"),
            """
            void Main() {
                Runner.Player.PlayerState player;
                player.Reset();
                Runner.Frame.PresentFrame(player);
                Runner.Frame.FramePresenter.Present(player);
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "runner.retrosharp.json");
        var outputPath = Path.Combine(workspace.Path, "bin", "runner.gb");
        File.WriteAllText(
            projectPath,
            """
            {
              "target": "gb",
              "output": "bin/runner.gb",
              "rootNamespace": "Runner",
              "sourceRoot": "src",
              "namespaceMode": "physical",
              "libraries": [
                "RetroSharp.Portable2D"
              ],
              "sources": [
                "src/player/state.rs",
                "src/frame/presenter.rs",
                "src/main.rs"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_project_file_accepts_physical_namespace_usings()
    {
        using var workspace = TemporaryWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "player"));
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "frame"));
        File.WriteAllText(
            Path.Combine(sourceDirectory, "player", "state.rs"),
            """
            class PlayerState
            {
                u8 x;

                inline void Reset()
                {
                    x = 0;
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "frame", "presenter.rs"),
            """
            using Runner.Player;

            inline void PresentFrame(PlayerState player)
            {
                Video.WaitVBlank();
            }

            class FramePresenter
            {
                static inline void Present(PlayerState player)
                {
                    PresentFrame(player);
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "main.rs"),
            """
            using Runner.Player;
            using Runner.Frame;

            void Main() {
                PlayerState player;
                player.Reset();
                PresentFrame(player);
                FramePresenter.Present(player);
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "runner.retrosharp.json");
        var outputPath = Path.Combine(workspace.Path, "bin", "runner.gb");
        File.WriteAllText(
            projectPath,
            """
            {
              "target": "gb",
              "output": "bin/runner.gb",
              "rootNamespace": "Runner",
              "sourceRoot": "src",
              "namespaceMode": "physical",
              "libraries": [
                "RetroSharp.Portable2D"
              ],
              "sources": [
                "src/player/state.rs",
                "src/frame/presenter.rs",
                "src/main.rs"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_project_file_uses_declared_library_paths()
    {
        using var workspace = TemporaryWorkspace();
        var libraryRoot = Path.Combine(workspace.Path, "lib");
        WriteLibraryPackage(
            libraryRoot,
            "acme-wait",
            "Acme.Wait",
            "wait.rs",
            """
            [target("gb")]
            [intrinsic("wait_frame")]
            extern void acme_wait_frame();

            class AcmeWait
            {
                static inline void Tick()
                {
                    acme_wait_frame();
                }
            }
            """,
            "gb");
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "Program.rs"),
            """
            import Acme.Wait;

            void Main() {
                AcmeWait.Tick();
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "runner.retrosharp.json");
        var outputPath = Path.Combine(workspace.Path, "bin", "runner.gb");
        File.WriteAllText(
            projectPath,
            """
            {
              "target": "gb",
              "output": "bin/runner.gb",
              "sources": [
                "src/Program.rs"
              ],
              "libraryPaths": [
                "lib"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_project_file_loads_manifest_libraries()
    {
        using var workspace = TemporaryWorkspace();
        var libraryRoot = Path.Combine(workspace.Path, "lib");
        WriteLibraryPackage(
            libraryRoot,
            "acme-wait",
            "Acme.Wait",
            "wait.rs",
            """
            [target("gb")]
            [intrinsic("wait_frame")]
            extern void acme_wait_frame();

            class AcmeWait
            {
                static inline void Tick()
                {
                    acme_wait_frame();
                }
            }
            """,
            "gb");
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "Program.rs"),
            """
            void Main() {
                AcmeWait.Tick();
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "runner.retrosharp.json");
        var outputPath = Path.Combine(workspace.Path, "bin", "runner.gb");
        File.WriteAllText(
            projectPath,
            """
            {
              "target": "gb",
              "output": "bin/runner.gb",
              "sources": [
                "src/Program.rs"
              ],
              "libraryPaths": [
                "lib"
              ],
              "libraries": [
                "Acme.Wait"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(32768, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Cli_project_file_rejects_unknown_manifest_library()
    {
        using var workspace = TemporaryWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "Program.rs"),
            """
            void Main() {
            }
            """);
        var projectPath = Path.Combine(workspace.Path, "runner.retrosharp.json");
        File.WriteAllText(
            projectPath,
            """
            {
              "target": "gb",
              "sources": [
                "src/Program.rs"
              ],
              "libraries": [
                "Acme.Missing"
              ]
            }
            """);

        var result = RunCli(projectPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown import 'Acme.Missing'.", result.CombinedOutput, StringComparison.Ordinal);
    }

    private static CliResult RunCli(params string[] args)
    {
        var processArgs = new List<string>
        {
            CliAssembly(),
        };
        processArgs.AddRange(args);

        return RunProcess("dotnet", processArgs.ToArray());
    }

    private static CliResult RunProcess(string fileName, params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} command timed out: {string.Join(" ", args)}");
        }

        return new CliResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static TemporaryDirectory TemporaryWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "retrosharp-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    private static TemporaryDirectory CreateNormalizedFullStage1()
    {
        var workspace = TemporaryWorkspace();
        var sourcePath = RepositoryFile("samples/runner/assets/maps/stage1.tmj");
        var root = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
                   ?? throw new InvalidOperationException($"{sourcePath} is empty.");
        var height = root["height"]?.GetValue<int>()
                     ?? throw new InvalidOperationException($"{sourcePath} does not declare height.");
        root["properties"] = new JsonArray(
            MapProperty("retrosharpStreamY", 0),
            MapProperty("retrosharpWorldY", 0),
            MapProperty("retrosharpWorldHeight", height));
        foreach (var layer in root["layers"]?.AsArray() ?? [])
        {
            if (layer?["type"]?.GetValue<string>() == "tilelayer")
            {
                layer["name"] = "world";
            }
        }

        File.WriteAllText(
            Path.Combine(workspace.Path, "stage1.normalized.tmj"),
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Copy(RepositoryFile("samples/runner/assets/maps/stage1.tsx"), Path.Combine(workspace.Path, "stage1.tsx"));
        File.Copy(RepositoryFile("samples/runner/assets/maps/stage1.png"), Path.Combine(workspace.Path, "stage1.png"));
        return workspace;
    }

    private static JsonObject MapProperty(string name, int value) => new()
    {
        ["name"] = name,
        ["type"] = "int",
        ["value"] = value,
    };

    private static SampleManifest LoadManifest()
    {
        var json = File.ReadAllText(RepositoryFile("samples/manifest.json"));
        var manifest = JsonSerializer.Deserialize<SampleManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return manifest ?? throw new InvalidOperationException("samples/manifest.json is empty.");
    }

    private static int ExpectedRomSize(string target)
    {
        return ExpectedRomSize(string.Empty, target);
    }

    private static int ExpectedRomSize(string samplePath, string target)
    {
        return target switch
        {
            "gb" when samplePath == "samples/runner/runner.retrosharp.json" => 131072,
            "gb" => 32768,
            "nes" when samplePath == "samples/runner/runner.retrosharp.json" => 81936,
            "nes" => 40976,
            _ => throw new InvalidOperationException($"Unexpected sample target '{target}'."),
        };
    }

    private static string RepositoryFile(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
        }

        return path;
    }

    private static string RepositoryPath(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new InvalidOperationException($"Could not find repository path '{relativePath}'.");
        }

        return path;
    }

    private static void WriteLibraryPackage(
        string libraryRoot,
        string packageDirectoryName,
        string importPath,
        string sourceName,
        string source,
        params string[] targets)
    {
        var packageDirectory = Path.Combine(libraryRoot, packageDirectoryName);
        Directory.CreateDirectory(packageDirectory);
        var targetList = string.Join(", ", targets.Select(target => "\"" + target + "\""));
        File.WriteAllText(
            Path.Combine(packageDirectory, "retrosharp-library.json"),
            $$"""
              {
                "import": "{{importPath}}",
                "sources": [ "{{sourceName}}" ],
                "targets": [ {{targetList}} ]
              }
              """);
        File.WriteAllText(Path.Combine(packageDirectory, sourceName), source);
    }

    private static string CliAssembly()
    {
        var configuration = TestConfiguration();
        return RepositoryFile($"src/RetroSharp.Cli/bin/{configuration}/net10.0/RetroSharp.Cli.dll");
    }

    private static string TestConfiguration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = directory.Parent;
        return configurationDirectory?.Name
            ?? throw new InvalidOperationException($"Could not infer test configuration from '{AppContext.BaseDirectory}'.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => $"{StandardOutput}{StandardError}";
    }

    private sealed record SampleManifest(SampleEntry[] Samples);

    private sealed record SampleEntry(string Path, string[] Targets, string[]? LibraryPaths = null);

    private sealed class TemporaryDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
