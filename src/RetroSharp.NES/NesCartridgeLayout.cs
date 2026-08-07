using RetroSharp.Core.Sdk;

namespace RetroSharp.NES;

internal enum NesCartridgeProfile
{
    Mapper0,
    Mmc3Tvrom,
}

internal enum NesProgramLinkMode
{
    Fixed,
    BankedR6,
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
    Mmc3ProgramPrg,
    Mmc3R6Capacity,
    Mmc3WorldPackCapacity,
    Mmc3WorldPackAddressability,
    Mmc3BankNumberCeiling,
    Mmc3HotPhaseSize,
    FixedPrg,
    Dpcm,
}

/// <summary>
/// Tags a final-link failure with the capacity it exhausted so that profile and board
/// selection can escalate deliberately instead of matching on message text.
/// </summary>
internal static class NesLinkConstraints
{
    internal static InvalidOperationException Failure(NesLinkConstraint constraint, string message)
    {
        var exception = new InvalidOperationException(message);
        exception.Data[nameof(NesLinkConstraint)] = constraint;
        return exception;
    }
}

internal sealed record NesPrgBuild(
    byte[] Bytes,
    int UsedBytes,
    int? InlineWorldPackOffset,
    byte[] PinnedDataBytes,
    IReadOnlyList<NesDpcmBuildPlacement> DpcmPlacements,
    int FixedPayloadBytes,
    IReadOnlyDictionary<string, ushort> FixedSymbols,
    int ProgramR6Bytes,
    int FixedVeneerBytes,
    IReadOnlyList<NesLinkedProgramSegment> ProgramSegments,
    IReadOnlyDictionary<string, NesPrgSymbol> ProgramSymbols,
    IReadOnlyList<NesPrgPlacementUnit> PlacementUnits,
    IReadOnlyList<NesRuntimeUserVariable> UserVariables,
    IReadOnlyList<NesSharedSdkSubroutine> SharedSdkSubroutines,
    string FrameProfile,
    SdkCpuWorkReport FrameCpuWork,
    int FixedHeadroomBytes,
    NesProgramBankPlacementReport? BankPlacement,
    NesUserFunctionCallAccountingReport UserFunctionCalls,
    IReadOnlyList<NesOutlinedUserFunction> OutlinedUserFunctions);

internal sealed record NesDpcmBuildPlacement(ushort SourceAddress, ushort CpuAddress, int Length);

internal sealed record NesRomBuildResult(byte[] Rom, NesRomBuildReport Report);

internal sealed record NesRomBuildReport(
    string SelectedProfile,
    int PrgRomSize,
    int ChrRomSize,
    int FixedPayloadBytes,
    int ProgramR6Bytes,
    int FixedVeneerBytes,
    int PinnedR7Bytes,
    int BootR7Bytes,
    int ResidentChrBytes,
    IReadOnlyList<NesRomBuildSegment> Segments,
    IReadOnlyDictionary<string, ushort> FixedSymbols,
    IReadOnlyDictionary<string, NesPrgSymbol> BankedSymbols,
    IReadOnlyList<NesPrgPlacementUnit> PlacementUnits,
    IReadOnlyList<NesRuntimeUserVariable> UserVariables,
    IReadOnlyList<NesRuntimeRegion> RuntimeRegions,
    IReadOnlyList<NesSharedSdkSubroutine> SharedSdkSubroutines,
    SdkCpuWorkReport CpuWork,
    int FixedHeadroomBytes,
    NesProgramBankPlacementReport? BankPlacement,
    NesUserFunctionCallAccountingReport UserFunctionCalls,
    IReadOnlyList<NesOutlinedUserFunction> OutlinedUserFunctions);

/// <summary>
/// One user or generated function emitted once and reached by <see cref="CallSites"/> JSRs.
/// <see cref="OverridesInlineHint"/> names the helpers whose <c>inline</c> hint the target chose
/// to override, so the decision is auditable from a production build report.
/// </summary>
internal sealed record NesOutlinedUserFunction(
    string Function,
    string Label,
    ushort CpuAddress,
    NesUserFunctionPhase Phase,
    int CallSites,
    bool OverridesInlineHint);

/// <summary>
/// One shared SDK operation body emitted once and reached by <see cref="CallSites"/> JSRs.
/// Reported so that deduplication is auditable from a production build instead of from
/// emitted-byte inspection.
/// </summary>
internal sealed record NesSharedSdkSubroutine(string Label, ushort CpuAddress, int CallSites);

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
    internal const int Mmc3PrgBankSize = 8 * 1_024;

    /// <summary>MMC3 R6/R7 bank-select values are 6-bit, capping PRG at 64 banks (512 KiB).</summary>
    internal const int Mmc3MaximumBankNumber = 63;

    internal const int Mmc3SmallestPrgBankCount = 8;

    private const int PinnedR7Bank = 1;
    private const int BootR7Bank = 2;
    private const int FixedRuntimeBankCount = 2;

    /// <summary>
    /// MMC3 boards tried in ascending order by the final link: 64, 128, 256 and 512 KiB. A larger
    /// board is only reached after every smaller one has proven it cannot hold the image.
    /// </summary>
    internal static IReadOnlyList<int> Mmc3PrgBankCounts { get; } = [8, 16, 32, 64];

    /// <summary>Physical banks reachable through R6 at <c>$8000-$9FFF</c>, in placement order.</summary>
    public IReadOnlyList<int> R6PoolBanks => PrgSections
        .Where(section => section.Kind is NesPrgSectionKind.WorldR6)
        .OrderBy(section => section.PhysicalOffset)
        .Select(section => section.PhysicalBank)
        .ToArray();

    public static NesCartridgeLayout Create(NesCartridgeProfile profile, bool useFourScreenNametables) =>
        Create(profile, useFourScreenNametables, Mmc3SmallestPrgBankCount);

    public static NesCartridgeLayout Create(
        NesCartridgeProfile profile,
        bool useFourScreenNametables,
        int mmc3PrgBankCount) =>
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
            NesCartridgeProfile.Mmc3Tvrom => CreateMmc3Tvrom(mmc3PrgBankCount),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
        };

    /// <summary>
    /// Builds the MMC3 PRG mode 0 board of <paramref name="prgBankCount"/> 8 KiB banks. The
    /// contract is identical at every size: bank 1 is pinned R7 data, bank 2 is the boot-only R7
    /// upload, the top two banks are the fixed <c>$C000-$FFFF</c> region, and every remaining bank
    /// belongs to the R6 pool shared by <c>WorldPack</c> and the banked program.
    /// </summary>
    private static NesCartridgeLayout CreateMmc3Tvrom(int prgBankCount)
    {
        if (!Mmc3PrgBankCounts.Contains(prgBankCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(prgBankCount),
                prgBankCount,
                $"NES MMC3/TVROM supports {string.Join(
                    ", ",
                    Mmc3PrgBankCounts.Select(count => $"{count * Mmc3PrgBankSize / 1_024} KiB"))} PRG boards.");
        }

        // MMC3 R6/R7 bank-select values are 6-bit, so no emitted bank number may exceed 63. A
        // board past that ceiling is unbuildable on this mapper, so it is not a promotion signal.
        if (prgBankCount - 1 > Mmc3MaximumBankNumber)
        {
            throw NesLinkConstraints.Failure(
                NesLinkConstraint.Mmc3BankNumberCeiling,
                $"NES MMC3/TVROM PRG board of {prgBankCount} banks needs bank number {prgBankCount - 1}, " +
                $"beyond the mapper's 6-bit maximum of {Mmc3MaximumBankNumber}.");
        }

        var sections = new NesPrgSectionLayout[prgBankCount];
        for (var bank = 0; bank < prgBankCount; bank++)
        {
            var kind = bank switch
            {
                PinnedR7Bank => NesPrgSectionKind.PinnedR7,
                BootR7Bank => NesPrgSectionKind.BootR7,
                _ when bank >= prgBankCount - FixedRuntimeBankCount => NesPrgSectionKind.FixedRuntime,
                _ => NesPrgSectionKind.WorldR6,
            };
            sections[bank] = new NesPrgSectionLayout(bank, bank * Mmc3PrgBankSize, Mmc3PrgBankSize, kind);
        }

        var prgRomSize = prgBankCount * Mmc3PrgBankSize;
        return new NesCartridgeLayout(
            prgBankCount == Mmc3SmallestPrgBankCount
                ? "MMC3/TVROM"
                : $"MMC3/TVROM-{prgRomSize / 1_024}K",
            prgRomSize,
            sections,
            ChrRomSize: 16 * 1_024,
            HeaderFlags6: 0x48,
            UseFourScreenNametables: true,
            FixedRuntimeCpuBaseAddress: 0xC000,
            FixedRuntimePhysicalOffset: (prgBankCount - FixedRuntimeBankCount) * Mmc3PrgBankSize,
            FixedRuntimeSize: FixedRuntimeBankCount * Mmc3PrgBankSize,
            FixedTrailerStartAddress: 0xFF80,
            EmitMmc3Foundation: true);
    }
}

internal sealed class BranchOutOfRangeException(string label, int delta)
    : InvalidOperationException($"Branch to '{label}' is out of range.")
{
    public string Label { get; } = label;

    public int Delta { get; } = delta;
}
