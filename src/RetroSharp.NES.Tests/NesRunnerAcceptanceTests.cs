namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class NesRunnerAcceptanceTests
{
    [Fact]
    public void Nes_runner_sample_compiles_without_audio()
    {
        var sourcePath = RepositoryFile("samples/runner/runner.nes.rs");
        var source = File.ReadAllText(sourcePath);
        var rom = NesRomCompiler.CompileSource(source, Path.GetDirectoryName(sourcePath));

        Assert.Equal(24592, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
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
