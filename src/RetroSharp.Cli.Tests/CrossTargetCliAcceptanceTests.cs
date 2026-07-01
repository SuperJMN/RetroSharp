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
                var result = RunCli("--target", target, "--out", output, RepositoryFile(sample.Path));

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
        Assert.Contains("samples/runner/runner.rs", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("samples/runner/runner.gb", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("--target nes", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("samples/runner/runner.nes", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("samples/gameboy-hud/hud.rs", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("samples/runner/diagnostics/00-static-background.rs", result.CombinedOutput, StringComparison.Ordinal);
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
            "gb" when samplePath == "samples/runner/runner.rs" => 65536,
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

    private sealed record SampleEntry(string Path, string[] Targets);

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
