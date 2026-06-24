namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class NesRunnerAcceptanceTests
{
    [Fact]
    public void Nes_runner_source_tracks_game_boy_runner_except_audio()
    {
        var gameBoySource = File.ReadAllText(RepositoryFile("samples/runner/runner.rs"));
        var nesSource = File.ReadAllText(RepositoryFile("samples/runner/runner.nes.rs"));

        Assert.Equal(NormalizeForNesRunner(gameBoySource), Normalize(nesSource));
    }

    [Fact]
    public void Nes_runner_sample_compiles_without_audio()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.nes.rs");
        var source = File.ReadAllText(sourcePath);
        var rom = NesRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(40976, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
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

    private static string NormalizeForNesRunner(string source)
    {
        source = RemoveFunction(source, "setup_audio");
        source = source.Replace("    audio.Update();\n", string.Empty, StringComparison.Ordinal);
        source = source.Replace("    setup_audio();\n", string.Empty, StringComparison.Ordinal);
        return Normalize(source);
    }

    private static string Normalize(string source)
    {
        return source.ReplaceLineEndings("\n").Trim();
    }

    private static string RemoveFunction(string source, string functionName)
    {
        source = source.ReplaceLineEndings("\n");
        var start = source.IndexOf($"void {functionName}()", StringComparison.Ordinal);
        if (start < 0)
        {
            return source;
        }

        var depth = 0;
        for (var index = start; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    var end = index + 1;
                    while (end < source.Length && source[end] == '\n')
                    {
                        end++;
                    }

                    return string.Concat(source.AsSpan(0, start), source.AsSpan(end));
                }
            }
        }

        throw new InvalidOperationException($"Could not remove function '{functionName}'.");
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
