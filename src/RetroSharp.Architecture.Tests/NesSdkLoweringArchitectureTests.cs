namespace RetroSharp.Architecture.Tests;

using RetroSharp.NES;
using RetroSharp.NES.Tests;

public sealed class NesSdkLoweringArchitectureTests
{
    private static readonly PhysicalFileContract[] PhysicalModules =
    [
        new("src/RetroSharp.NES/NesCartridgeLayout.cs", "Cartridge layout and placement have a dedicated physical navigation root.", [typeof(NesCartridgeLayout)]),
        new("src/RetroSharp.NES/NesRuntimeCompiler.cs", "Runtime compilation has a dedicated physical navigation root.", [typeof(NesRuntimeCompiler)]),
        new("src/RetroSharp.NES/NesSdkStreamReader.cs", "Collected SDK stream consumption has a dedicated physical navigation root.", [typeof(NesSdkStreamReader)]),
        new("src/RetroSharp.NES/NesSdkOperationLowerer.cs", "Portable SDK emission has a dedicated physical navigation root.", [typeof(NesSdkOperationLowerer)]),
        new("src/RetroSharp.NES/PrgBuilder.cs", "NES PRG byte building has a dedicated physical navigation root.", [typeof(PrgBuilder)]),
    ];

    [Fact]
    public void Nes_sdk_lowerer_owns_emission_without_delegating_to_runtime_compiler()
    {
        ArchitectureSymbolAssertions.AssertSdkOperationOwnership(
            typeof(NesSdkOperationLowerer),
            typeof(NesRuntimeCompiler));
    }

    [Fact]
    public void Nes_cartridge_runtime_stream_and_prg_modules_are_physically_separate()
    {
        ArchitecturePhysicalAssertions.AssertModuleOwnership(
            "src/RetroSharp.NES/NesRomBuilder.cs",
            PhysicalModules);
    }

    [Fact]
    public void Lowering_regressions_are_owned_by_the_declared_focused_sdk_suites()
    {
        ArchitectureSymbolAssertions.AssertFocusedTestOwnership(
            typeof(NesRomCompilerTests),
            [
                typeof(NesSdkCameraStreamingLoweringTests),
                typeof(NesSdkCollisionLoweringTests),
                typeof(NesSdkFrameInputLoweringTests),
                typeof(NesSdkSpriteLoweringTests),
            ]);
    }
}
