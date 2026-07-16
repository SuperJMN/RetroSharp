namespace RetroSharp.Architecture.Tests;

public sealed class GameBoySdkLoweringArchitectureTests
{
    private const string LowererPath = "src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.cs";

    private static readonly string[] PurposeNamedModules =
    [
        "src/RetroSharp.GameBoy/GameBoyRomLayout.cs",
        "src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.cs",
        "src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.cs",
        "src/RetroSharp.GameBoy/GameBoySdkLoweringContext.cs",
        "src/RetroSharp.GameBoy/GameBoySdkStreamReader.cs",
        "src/RetroSharp.GameBoy/GbBuilder.cs",
    ];

    private static readonly string[] RuntimeFeatureModules =
    [
        "src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.Audio.cs",
        "src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.Calls.cs",
        "src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.ControlFlow.cs",
        "src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.Expressions.cs",
        "src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.Functions.cs",
        "src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.Storage.cs",
    ];

    private static readonly string[] SdkFeatureModules =
    [
        "src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.FrameInput.cs",
        "src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.Sprites.cs",
        "src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.CameraStreaming.cs",
        "src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.Collision.cs",
    ];

    [Fact]
    public void Game_boy_sdk_lowerer_owns_emission_without_delegating_to_runtime_compiler()
    {
        var root = RepositoryRoot();
        var lowererSource = File.ReadAllText(Path.Combine(root, LowererPath));

        Assert.DoesNotContain("GameBoyRuntimeCompiler", lowererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("interface IGameBoySdkLoweringTarget", lowererSource, StringComparison.Ordinal);
        Assert.Contains("sealed partial class GameBoySdkOperationLowerer", lowererSource, StringComparison.Ordinal);
        Assert.Contains("public void Emit(Sdk2DOperation operation)", lowererSource, StringComparison.Ordinal);

        foreach (var path in SdkFeatureModules)
        {
            var fullPath = Path.Combine(root, path);
            Assert.True(File.Exists(fullPath), $"Game Boy SDK feature module '{path}' must exist.");
            var source = File.ReadAllText(fullPath);
            Assert.Contains("partial class GameBoySdkOperationLowerer", source, StringComparison.Ordinal);
            Assert.Contains("builder.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GameBoyRuntimeCompiler", source, StringComparison.Ordinal);
        }

        var runtimeSources = Directory
            .GetFiles(Path.Combine(root, "src/RetroSharp.GameBoy"), "GameBoyRuntimeCompiler*.cs")
            .Select(File.ReadAllText)
            .ToArray();
        Assert.Contains(runtimeSources, source => source.Contains("sdkOperationLowerer.Emit(operation)", StringComparison.Ordinal));
        Assert.All(runtimeSources, source =>
        {
            Assert.DoesNotContain("void EmitPollInput()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitDrawLogicalSprite(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitSetCameraPosition(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitReadWorldTileFlags(", source, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Game_boy_cartridge_runtime_stream_and_byte_modules_are_physically_separate()
    {
        var root = RepositoryRoot();

        foreach (var path in PurposeNamedModules)
        {
            Assert.True(File.Exists(Path.Combine(root, path)), $"Game Boy purpose-named module '{path}' must exist.");
        }


        foreach (var path in RuntimeFeatureModules)
        {
            var fullPath = Path.Combine(root, path);
            Assert.True(File.Exists(fullPath), $"Game Boy runtime feature module '{path}' must exist.");
            Assert.Contains("partial class GameBoyRuntimeCompiler", File.ReadAllText(fullPath), StringComparison.Ordinal);
        }

        var romBuilderSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.GameBoy/GameBoyRomBuilder.cs"));
        Assert.DoesNotContain("class GameBoyRomLayout", romBuilderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class GameBoyRuntimeCompiler", romBuilderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class Sdk2DStreamReader", romBuilderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class SdkAudioStreamReader", romBuilderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class GbBuilder", romBuilderSource, StringComparison.Ordinal);

        var streamReaderSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.GameBoy/GameBoySdkStreamReader.cs"));
        Assert.Contains("class Sdk2DStreamReader", streamReaderSource, StringComparison.Ordinal);
        Assert.Contains("class SdkAudioStreamReader", streamReaderSource, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
