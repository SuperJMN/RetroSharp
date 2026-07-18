namespace RetroSharp.Architecture.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.GameBoy.Tests;

public sealed class GameBoySdkLoweringArchitectureTests
{
    private static readonly PhysicalFileContract[] PhysicalModules =
    [
        new("src/RetroSharp.GameBoy/GameBoyRomLayout.cs", "Cartridge layout and placement have a dedicated physical navigation root.", [typeof(GameBoyRomLayout)]),
        new("src/RetroSharp.GameBoy/GameBoyFramePlan.cs", "Physical frame scheduling has one target-private authority.", [typeof(GameBoyFramePlan)]),
        new("src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.cs", "Runtime compilation has a dedicated physical navigation root.", [typeof(GameBoyRuntimeCompiler)]),
        new("src/RetroSharp.GameBoy/GameBoySdkStreamReader.cs", "Collected SDK stream consumption has a dedicated physical navigation root.", [typeof(Sdk2DStreamReader), typeof(SdkAudioStreamReader)]),
        new("src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.cs", "Portable SDK emission has a dedicated physical navigation root.", [typeof(GameBoySdkOperationLowerer)]),
        new("src/RetroSharp.GameBoy/GbBuilder.cs", "Game Boy byte building has a dedicated physical navigation root.", [typeof(GbBuilder)]),
    ];

    [Fact]
    public void Game_boy_sdk_lowerer_owns_emission_without_delegating_to_runtime_compiler()
    {
        ArchitectureSymbolAssertions.AssertSdkOperationOwnership(
            typeof(GameBoySdkOperationLowerer),
            typeof(GameBoyRuntimeCompiler));
    }

    [Fact]
    public void Game_boy_cartridge_runtime_stream_and_byte_modules_are_physically_separate()
    {
        ArchitecturePhysicalAssertions.AssertModuleOwnership(
            "src/RetroSharp.GameBoy/GameBoyRomBuilder.cs",
            PhysicalModules);
    }

    [Fact]
    public void Game_boy_rom_builder_consumes_frame_planning_without_owning_cpu_window_policy()
    {
        var calls = ArchitectureSymbolAssertions.CalledMethods(typeof(GameBoyRomBuilder));
        var lowererCalls = ArchitectureSymbolAssertions.CalledMethods(typeof(GameBoySdkOperationLowerer));

        Assert.Contains(calls, method => method.DeclaringType == typeof(GameBoyFramePlan));
        Assert.Contains(lowererCalls, method => method.DeclaringType == typeof(GameBoyFramePlan));
        Assert.DoesNotContain(calls, method => method.DeclaringType == typeof(SdkCpuWorkReportFactory));
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
