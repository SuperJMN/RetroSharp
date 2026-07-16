namespace RetroSharp.Architecture.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;

public sealed class RuntimeMemoryOwnershipArchitectureTests
{
    [Fact]
    public void Game_boy_reserved_runtime_addresses_are_owned_only_by_the_layout()
    {
        ArchitectureSymbolAssertions.AssertRuntimeMemoryOwnership(
            typeof(GameBoyRomCompiler).Assembly,
            "RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout");
    }

    [Fact]
    public void Nes_reserved_runtime_addresses_are_owned_only_by_the_layout()
    {
        ArchitectureSymbolAssertions.AssertRuntimeMemoryOwnership(
            typeof(NesRomCompiler).Assembly,
            "RetroSharp.NES.NesRuntimeMemoryLayout",
            // Byte-valued stream/state sentinels share $FF numerically but are not RAM addresses.
            "RetroSharp.NES.NesSoundEffectAssetCompiler.EndOfEffectMarker",
            "RetroSharp.NES.NesPackedCameraRuntime.NoSlot");
    }
}
