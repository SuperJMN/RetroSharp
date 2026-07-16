namespace RetroSharp.Architecture.Tests;

using RetroSharp.GameBoy;
using RetroSharp.GameBoy.Tests;

public sealed class GameBoySdkLoweringArchitectureTests
{
    private static readonly PhysicalFileContract[] PhysicalModules =
    [
        new("src/RetroSharp.GameBoy/GameBoyRomLayout.cs", "Cartridge layout and placement have a dedicated physical navigation root."),
        new("src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.cs", "Runtime compilation has a dedicated physical navigation root."),
        new("src/RetroSharp.GameBoy/GameBoySdkStreamReader.cs", "Collected SDK stream consumption has a dedicated physical navigation root."),
        new("src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.cs", "Portable SDK emission has a dedicated physical navigation root."),
        new("src/RetroSharp.GameBoy/GbBuilder.cs", "Game Boy byte building has a dedicated physical navigation root."),
    ];

    [Fact]
    public void Game_boy_sdk_lowerer_owns_emission_without_delegating_to_runtime_compiler()
    {
        ArchitectureSymbolAssertions.AssertSdkOperationOwnership(
            typeof(GameBoyRomCompiler).Assembly,
            "RetroSharp.GameBoy.GameBoySdkOperationLowerer",
            "RetroSharp.GameBoy.GameBoyRuntimeCompiler");
    }

    [Fact]
    public void Game_boy_cartridge_runtime_stream_and_byte_modules_are_physically_separate()
    {
        ArchitecturePhysicalAssertions.AssertFilesExist(PhysicalModules);
    }

    [Fact]
    public void Lowering_regressions_are_owned_by_the_declared_focused_sdk_suites()
    {
        ArchitectureSymbolAssertions.AssertFocusedTestOwnership(
            typeof(GameBoyRomCompilerTests),
            [
                typeof(GameBoySdkAnimationLoweringTests),
                typeof(GameBoySdkCameraRuntimeLoweringTests),
                typeof(GameBoySdkCameraStreamingLoweringTests),
                typeof(GameBoySdkCollisionRuntimeLoweringTests),
                typeof(GameBoySdkCollisionLoweringTests),
                typeof(GameBoySdkFrameInputLoweringTests),
                typeof(GameBoySdkSpriteLoweringTests),
            ]);
    }
}
