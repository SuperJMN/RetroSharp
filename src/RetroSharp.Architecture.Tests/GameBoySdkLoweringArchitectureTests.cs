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

    private static readonly string[] FocusedInputLoweringTests =
    [
        "Direct_button_read_and_bare_button_identifiers_are_rejected",
        "Bare_button_identifiers_are_rejected_by_input_facade",
        "Button_enum_members_are_accepted_by_input_facade",
        "Input_facade_predicates_lower_like_explicit_numeric_checks",
        "Compiles_tick_input_api_for_variable_jump",
        "Input_poll_settles_joypad_rows_before_latching_buttons",
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
        var frameInputSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.FrameInput.cs"));
        Assert.Contains(runtimeSources, source => source.Contains("sdkOperationLowerer.Emit(operation)", StringComparison.Ordinal));
        Assert.Contains("internal void EmitButtonDown(", frameInputSource, StringComparison.Ordinal);
        Assert.Contains("internal void EmitButtonJustPressed(", frameInputSource, StringComparison.Ordinal);
        Assert.Contains("internal void EmitButtonJustReleased(", frameInputSource, StringComparison.Ordinal);
        Assert.Contains("internal void EmitButtonHoldTicks(", frameInputSource, StringComparison.Ordinal);
        Assert.Contains("internal void EmitButtonPressed(", frameInputSource, StringComparison.Ordinal);
        Assert.Contains("internal void EmitInputStateInitialization(", frameInputSource, StringComparison.Ordinal);
        Assert.Contains("record struct GameBoyButton", frameInputSource, StringComparison.Ordinal);
        Assert.Contains(runtimeSources, source => source.Contains("sdkOperationLowerer.EmitButtonDown(call)", StringComparison.Ordinal));
        Assert.Contains(runtimeSources, source => source.Contains("sdkOperationLowerer.EmitButtonJustPressed(call)", StringComparison.Ordinal));
        Assert.Contains(runtimeSources, source => source.Contains("sdkOperationLowerer.EmitButtonJustReleased(call)", StringComparison.Ordinal));
        Assert.Contains(runtimeSources, source => source.Contains("sdkOperationLowerer.EmitButtonHoldTicks(call)", StringComparison.Ordinal));
        Assert.All(runtimeSources, source =>
        {
            Assert.DoesNotContain("void EmitPollInput()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitDrawLogicalSprite(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitSetCameraPosition(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitReadWorldTileFlags(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitButtonDown(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitButtonJustPressed(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitButtonJustReleased(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitButtonHoldTicks(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitButtonPressed(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("void EmitInputStateInitialization(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("record struct GameBoyButton", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GameBoyButton[] Buttons", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CameraAabbWidth(", source, StringComparison.Ordinal);
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

    [Fact]
    public void Input_lowering_regressions_live_in_the_focused_sdk_suite()
    {
        var root = RepositoryRoot();
        var monolithicSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.GameBoy.Tests/GameBoyRomCompilerTests.cs"));
        var focusedSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.GameBoy.Tests/GameBoySdkFrameInputLoweringTests.cs"));

        Assert.All(FocusedInputLoweringTests, testName =>
        {
            Assert.DoesNotContain($"void {testName}(", monolithicSource, StringComparison.Ordinal);
            Assert.Contains($"void {testName}(", focusedSource, StringComparison.Ordinal);
        });
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
