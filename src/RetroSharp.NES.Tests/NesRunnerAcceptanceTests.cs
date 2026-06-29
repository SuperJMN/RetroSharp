namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.NES;
using Xunit;

public sealed class NesRunnerAcceptanceTests
{
    [Fact]
    public void Nes_runner_uses_the_shared_runner_source()
    {
        Assert.False(File.Exists(RepositoryFileOrNull("samples/runner/runner.nes.rs")));
    }

    [Fact]
    public void Nes_runner_sample_compiles_with_audio_as_noop()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var source = File.ReadAllText(sourcePath);
        var rom = NesRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(40976, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
    }

    [Fact]
    public void Nes_runner_uses_dead_zone_2d_camera_path()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.rs");
        var runnerDirectory = Path.GetDirectoryName(sourcePath);
        var source = File.ReadAllText(sourcePath);

        var operations = NesRomCompiler.CollectSdkOperations(source, runnerDirectory);
        Assert.Equal(2, operations.OfType<Sdk2DOperation.SetCameraPosition>().Count());
        Assert.All(
            operations.OfType<Sdk2DOperation.SetCameraPosition>(),
            operation => Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, operation.Axes));

        var rom = NesRomCompiler.CompileSource(source, runnerDirectory);
        Assert.Equal(0x08, rom[6] & 0x08);
    }

    [Fact]
    public void Nes_lowers_runtime_animation_frame_for_runner_state()
    {
        const string source = """
            void main() {
                animation.Clip(run, 1, 6, 6, 6);
                sprite.Asset(player, "samples/runner/assets/mario-player.png", 18, 32);
                u8 tick = 0;
                loop {
                    video.WaitVBlank();
                    tick++;
                    let frame = animation.Frame(run, tick);
                    sprite.Draw(player, 72, 88, frame, false, 0);
                }
            }
            """;

        var rom = NesRomCompiler.CompileSource(source, RepoRoot());
        Assert.NotEmpty(rom);
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

    private static string? RepositoryFileOrNull(string relativePath)
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

        return null;
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
