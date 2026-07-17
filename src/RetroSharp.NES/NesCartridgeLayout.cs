using RetroSharp.Core.Sdk;

namespace RetroSharp.NES;

internal enum NesCartridgeProfile
{
    Mapper0,
    Mmc3Tvrom,
}

internal enum NesPrgSectionKind
{
    WorldR6,
    PinnedR7,
    BootR7,
    FixedRuntime,
}

internal enum NesLinkConstraint
{
    Mapper0Prg,
    Mapper0Dpcm,
    FixedPrg,
    Dpcm,
}

internal sealed record NesPrgBuild(
    byte[] Bytes,
    int UsedBytes,
    int? InlineWorldPackOffset,
    byte[] PinnedDataBytes,
    IReadOnlyList<NesDpcmBuildPlacement> DpcmPlacements,
    int FixedPayloadBytes,
    IReadOnlyDictionary<string, ushort> FixedSymbols,
    IReadOnlyList<NesRuntimeUserVariable> UserVariables);

internal sealed record NesDpcmBuildPlacement(ushort SourceAddress, ushort CpuAddress, int Length);

internal sealed record NesRomBuildResult(byte[] Rom, NesRomBuildReport Report);

internal sealed record NesRomBuildReport(
    string SelectedProfile,
    int PrgRomSize,
    int ChrRomSize,
    int FixedPayloadBytes,
    int PinnedR7Bytes,
    int BootR7Bytes,
    int ResidentChrBytes,
    IReadOnlyList<NesRomBuildSegment> Segments,
    IReadOnlyDictionary<string, ushort> FixedSymbols,
    IReadOnlyList<NesRuntimeUserVariable> UserVariables,
    IReadOnlyList<NesRuntimeRegion> RuntimeRegions,
    SdkCpuWorkReport CpuWork);

internal sealed record NesRuntimeUserVariable(
    string Name,
    string Type,
    ushort Address,
    int Size);

internal sealed record NesRuntimeRegion(
    string Name,
    ushort Start,
    int Length,
    string Owner);

internal sealed record NesRomBuildSegment(
    string Owner,
    string Window,
    int RelativeOffset,
    int PhysicalStart,
    int Length,
    int PhysicalBank,
    ushort CpuAddress);

internal sealed record NesPrgSectionLayout(
    int PhysicalBank,
    int PhysicalOffset,
    int Size,
    NesPrgSectionKind Kind);

internal sealed record NesCartridgeLayout(
    string Name,
    int PrgRomSize,
    IReadOnlyList<NesPrgSectionLayout> PrgSections,
    int ChrRomSize,
    byte HeaderFlags6,
    bool UseFourScreenNametables,
    ushort FixedRuntimeCpuBaseAddress,
    int FixedRuntimePhysicalOffset,
    int FixedRuntimeSize,
    ushort FixedTrailerStartAddress,
    bool EmitMmc3Foundation)
{
    public static NesCartridgeLayout Create(NesCartridgeProfile profile, bool useFourScreenNametables) =>
        profile switch
        {
            NesCartridgeProfile.Mapper0 => new NesCartridgeLayout(
                "mapper-0",
                PrgRomSize: 32 * 1_024,
                PrgSections:
                [
                    new NesPrgSectionLayout(0, 0, 32 * 1_024, NesPrgSectionKind.FixedRuntime),
                ],
                ChrRomSize: 8 * 1_024,
                HeaderFlags6: (byte)(useFourScreenNametables ? 0x09 : 0x01),
                UseFourScreenNametables: useFourScreenNametables,
                FixedRuntimeCpuBaseAddress: 0x8000,
                FixedRuntimePhysicalOffset: 0,
                FixedRuntimeSize: 32 * 1_024,
                FixedTrailerStartAddress: 0xFFFA,
                EmitMmc3Foundation: false),
            NesCartridgeProfile.Mmc3Tvrom => new NesCartridgeLayout(
                "MMC3/TVROM",
                PrgRomSize: 64 * 1_024,
                PrgSections:
                [
                    new NesPrgSectionLayout(0, 0 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.WorldR6),
                    new NesPrgSectionLayout(1, 1 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.PinnedR7),
                    new NesPrgSectionLayout(2, 2 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.BootR7),
                    new NesPrgSectionLayout(3, 3 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.WorldR6),
                    new NesPrgSectionLayout(4, 4 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.WorldR6),
                    new NesPrgSectionLayout(5, 5 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.WorldR6),
                    new NesPrgSectionLayout(6, 6 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.FixedRuntime),
                    new NesPrgSectionLayout(7, 7 * 8 * 1_024, 8 * 1_024, NesPrgSectionKind.FixedRuntime),
                ],
                ChrRomSize: 16 * 1_024,
                HeaderFlags6: 0x48,
                UseFourScreenNametables: true,
                FixedRuntimeCpuBaseAddress: 0xC000,
                FixedRuntimePhysicalOffset: 6 * 8 * 1_024,
                FixedRuntimeSize: 16 * 1_024,
                FixedTrailerStartAddress: 0xFF80,
                EmitMmc3Foundation: true),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
        };
}

internal sealed class BranchOutOfRangeException(string label, int delta)
    : InvalidOperationException($"Branch to '{label}' is out of range.")
{
    public string Label { get; } = label;

    public int Delta { get; } = delta;
}
