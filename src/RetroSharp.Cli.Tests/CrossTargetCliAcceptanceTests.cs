namespace RetroSharp.Cli.Tests;

using System.Diagnostics;
using System.Text.Json;
using Xunit;

public sealed class CrossTargetCliAcceptanceTests
{
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
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "samples/runner/assets/maps/runner.tmj")));
        var mainSource = File.ReadAllText(RepositoryFile("samples/runner/src/main.rs"));
        Assert.DoesNotContain("import Runner.Framework;", mainSource, StringComparison.Ordinal);
        Assert.Contains("""Music.Asset(runner_theme, "assets/music/runner.vgz");""", mainSource, StringComparison.Ordinal);
        Assert.Contains("""World.Load("assets/maps/runner.tmj");""", mainSource, StringComparison.Ordinal);
        Assert.Contains("""Actors.SpawnLayer(goombas, "assets/maps/runner.tmj", "actors");""", mainSource, StringComparison.Ordinal);

        var outputPath = Path.Combine(workspace.Path, "runner.gb");
        var result = RunCli("--target", "gb", "--out", outputPath, projectPath);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath), result.CombinedOutput);
        Assert.Equal(65536, new FileInfo(outputPath).Length);
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
            "gb" when samplePath == "samples/runner/runner.retrosharp.json" => 65536,
            "gb" => 32768,
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
