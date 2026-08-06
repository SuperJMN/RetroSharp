namespace RetroSharp.NES.Tests;

using System.Security.Cryptography;
using System.Text.Json;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

/// <summary>
/// Owns the MMC3 PRG board contract: the layout is generated from a bank count, the final link
/// picks the smallest board that fits, and the emitted image and iNES header follow that choice.
/// </summary>
public sealed class NesMmc3PrgBoardTests
{
    public static TheoryData<int, int> SupportedBoards => new()
    {
        { 8, 64 },
        { 16, 128 },
        { 32, 256 },
        { 64, 512 },
    };

    [Theory]
    [MemberData(nameof(SupportedBoards))]
    public void Every_supported_board_keeps_the_bank_roles_and_grows_only_the_r6_pool(
        int prgBankCount,
        int prgKib)
    {
        var layout = NesCartridgeLayout.Create(
            NesCartridgeProfile.Mmc3Tvrom,
            useFourScreenNametables: false,
            prgBankCount);

        Assert.Equal(prgKib * 1_024, layout.PrgRomSize);
        Assert.Equal(prgBankCount, layout.PrgSections.Count);
        Assert.Equal(
            Enumerable.Range(0, prgBankCount),
            layout.PrgSections.Select(section => section.PhysicalBank));
        Assert.Equal(
            Enumerable.Range(0, prgBankCount).Select(bank => bank * 8 * 1_024),
            layout.PrgSections.Select(section => section.PhysicalOffset));
        Assert.All(layout.PrgSections, section => Assert.Equal(8 * 1_024, section.Size));
        Assert.All(
            layout.PrgSections,
            section => Assert.InRange(section.PhysicalBank, 0, NesCartridgeLayout.Mmc3MaximumBankNumber));

        Assert.Equal(NesPrgSectionKind.PinnedR7, layout.PrgSections[1].Kind);
        Assert.Equal(NesPrgSectionKind.BootR7, layout.PrgSections[2].Kind);
        Assert.All(
            layout.PrgSections.TakeLast(2),
            section => Assert.Equal(NesPrgSectionKind.FixedRuntime, section.Kind));

        // The R6 pool absorbs every bank a bigger board adds: bank 0 plus banks 3..count-3.
        Assert.Equal(
            new[] { 0 }.Concat(Enumerable.Range(3, prgBankCount - 5)),
            layout.R6PoolBanks);
        Assert.Equal(prgBankCount - 4, layout.R6PoolBanks.Count);

        // The fixed region stays the top two banks at $C000-$FFFF, whatever the board size.
        Assert.Equal(0xC000, layout.FixedRuntimeCpuBaseAddress);
        Assert.Equal(16 * 1_024, layout.FixedRuntimeSize);
        Assert.Equal((prgBankCount - 2) * 8 * 1_024, layout.FixedRuntimePhysicalOffset);
        Assert.Equal(layout.PrgRomSize, layout.FixedRuntimePhysicalOffset + layout.FixedRuntimeSize);
        Assert.Equal(0xFF80, layout.FixedTrailerStartAddress);
        Assert.Equal(0x48, layout.HeaderFlags6);
        Assert.Equal(16 * 1_024, layout.ChrRomSize);
    }

    [Fact]
    public void The_smallest_board_is_the_unchanged_64_kib_tvrom_layout()
    {
        var explicitLayout = NesCartridgeLayout.Create(
            NesCartridgeProfile.Mmc3Tvrom,
            useFourScreenNametables: true,
            NesCartridgeLayout.Mmc3SmallestPrgBankCount);
        var defaultLayout = NesCartridgeLayout.Create(NesCartridgeProfile.Mmc3Tvrom, useFourScreenNametables: true);

        Assert.Equal(explicitLayout.PrgSections, defaultLayout.PrgSections);
        Assert.Equal(explicitLayout with { PrgSections = [] }, defaultLayout with { PrgSections = [] });
        Assert.Equal("MMC3/TVROM", defaultLayout.Name);
        Assert.Equal(64 * 1_024, defaultLayout.PrgRomSize);
        Assert.Equal(new[] { 0, 3, 4, 5 }, defaultLayout.R6PoolBanks);
        Assert.Equal(6 * 8 * 1_024, defaultLayout.FixedRuntimePhysicalOffset);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(128)]
    public void Unsupported_board_sizes_are_rejected(int prgBankCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NesCartridgeLayout.Create(
            NesCartridgeProfile.Mmc3Tvrom,
            useFourScreenNametables: false,
            prgBankCount));
    }

    [Theory]
    [MemberData(nameof(SupportedBoards))]
    public void The_ines_header_follows_the_emitted_image_without_changing_mapper_or_mirroring(
        int prgBankCount,
        int prgKib)
    {
        const string source = """
                              void Main() {
                                  return;
                              }
                              """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromCodeBankTestsWithReport(
            source,
            mmc3PrgBankCount: prgBankCount).Rom;

        Assert.Equal(16 + (prgKib * 1_024) + (16 * 1_024), rom.Length);
        Assert.Equal(new byte[] { (byte)'N', (byte)'E', (byte)'S', 0x1A }, rom[..4]);
        Assert.Equal((byte)(prgKib / 16), rom[4]);
        Assert.Equal(0x02, rom[5]);
        Assert.Equal(0x48, rom[6]);
        Assert.Equal(0x00, rom[7]);

        // MMC3 PRG mode 0 fixes $C000-$FFFF to the top two physical banks, so the vectors always
        // live in the last six bytes of the image and point back into the fixed region.
        var prg = rom.AsSpan(16, prgKib * 1_024);
        Assert.InRange(ReadWord(prg, prg.Length - 6), 0xC000, 0xFFF9);
        Assert.InRange(ReadWord(prg, prg.Length - 4), 0xC000, 0xFFF9);
        Assert.InRange(ReadWord(prg, prg.Length - 2), 0xC000, 0xFFF9);
    }

    [Fact]
    public void A_program_that_outgrows_the_64_kib_r6_pool_links_boots_and_ticks_on_the_next_board_up()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            NesPrgBoardEscalationFixture.Source,
            NesPrgBoardEscalationFixture.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var world = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var program = result.Report.Segments
            .Where(segment => segment.Owner.StartsWith("program:r6:", StringComparison.Ordinal))
            .ToArray();
        var pool = NesCartridgeLayout
            .Create(NesCartridgeProfile.Mmc3Tvrom, useFourScreenNametables: true, 16)
            .R6PoolBanks;

        Assert.Equal(NesPhysicalFrameScheduler.CodeBankedProfileName, result.Report.SelectedProfile);
        Assert.Equal(128 * 1_024, result.Report.PrgRomSize);
        Assert.Equal(0, world.PhysicalBank);

        // The 64 KiB board leaves three R6 banks once the pack owns one; this program needs more.
        Assert.True(
            program.Length > 3,
            $"Expected a program that cannot fit the 64 KiB R6 pool; it used {program.Length} bank(s).");
        Assert.All(program, segment => Assert.Contains(segment.PhysicalBank, pool));
        Assert.DoesNotContain(program, segment => segment.PhysicalBank == world.PhysicalBank);
        Assert.Equal(program.Length, program.Select(segment => segment.PhysicalBank).Distinct().Count());

        var ticks = Assert.Single(result.Report.UserVariables, variable => variable.Name == "ticks").Address;
        var counter = Assert.Single(result.Report.UserVariables, variable => variable.Name == "counter.value").Address;
        var visitedProgramBanks = new HashSet<int>();
        var cpu = new NesTestCpu(result.Rom);
        cpu.OnStep = step =>
        {
            if (step.ProgramCounter is >= 0x8000 and <= 0x9FFF)
            {
                visitedProgramBanks.Add(cpu.CurrentR6Bank);
            }
        };

        cpu.RunFrames(90);
        var settledTicks = cpu.Ram(ticks);
        var settledFrames = cpu.PhysicalFrames;
        cpu.RunFrames(settledFrames + 60);

        // The generated fold stream ends with counter.value == 8; see the fixture source header.
        Assert.Equal(8, cpu.Ram(counter));
        Assert.Equal(
            cpu.PhysicalFrames - settledFrames,
            unchecked((byte)(cpu.Ram(ticks) - settledTicks)));
        Assert.Equal(1, cpu.ResetCount);
        Assert.NotEmpty(cpu.ApuWrites);
        Assert.Equal(1, cpu.CurrentR7Bank);
        Assert.Equal(program.Select(segment => segment.PhysicalBank).ToHashSet(), visitedProgramBanks);
        Assert.DoesNotContain(
            cpu.PpuWrites,
            write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank");
        Assert.DoesNotContain(
            cpu.OamWrites,
            write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank");
    }

    [Fact]
    public void The_larger_board_keeps_the_public_runtime_abi_at_v1()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            NesPrgBoardEscalationFixture.Source,
            NesPrgBoardEscalationFixture.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        using var document = JsonDocument.Parse(NesRuntimeAbiProjection.Serialize(result));
        var root = document.RootElement;

        Assert.Equal(128 * 1_024, result.Report.PrgRomSize);
        Assert.Equal("retrosharp.nes.runtime-abi", root.GetProperty("contract").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("nes", root.GetProperty("target").GetString());
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(result.Rom)),
            root.GetProperty("romSha256").GetString());
        string[] expectedProperties =
        [
            "contract",
            "version",
            "target",
            "abiFingerprint",
            "romSha256",
            "ranges",
            "addresses",
            "rangeAliases",
            "addressAliases",
            "constants",
            "runtimeRegions",
            "userVariables",
        ];
        Assert.Equal(expectedProperties, root.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static ushort ReadWord(ReadOnlySpan<byte> prg, int offset) =>
        (ushort)(prg[offset] | (prg[offset + 1] << 8));

    /// <summary>
    /// A pack the current board's R6 pool cannot hold is what step 4 of the selection contract
    /// escalates on, and the diagnostic must report that board's real pool.
    /// </summary>
    [Fact]
    public void A_pack_larger_than_the_r6_pool_reports_that_pool_and_asks_for_a_larger_board()
    {
        var pool = R6Sections(8);
        var pack = new byte[(pool.Count * 8 * 1_024) + 1];

        var failure = Assert.Throws<InvalidOperationException>(() => NesWorldPackPlacement.Create(pack, pool));

        Assert.True(NesRomBuilder.RequiresLargerPrgBoard(failure));
        Assert.Contains($"{pack.Length} bytes", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"provides {pool.Count * 8 * 1_024} bytes", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The banked reader indexes segments from bits 13-15 of a 16-bit offset, so a pack past eight
    /// segments fails the same way on every board. It must name that limit and must not escalate.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void A_pack_past_the_readers_eight_segment_ceiling_never_asks_for_a_larger_board(int prgBankCount)
    {
        var pool = R6Sections(prgBankCount);
        var pack = new byte[NesWorldPackPlacement.MaximumAddressablePackBytes + 1];

        var failure = Assert.Throws<InvalidOperationException>(() => NesWorldPackPlacement.Create(pack, pool));

        Assert.False(NesRomBuilder.RequiresLargerPrgBoard(failure));
        Assert.Contains("at most 8 R6 segments (65536 bytes)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("larger PRG board cannot lift this limit", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_largest_addressable_pack_still_places_on_a_board_with_enough_banks()
    {
        var pack = new byte[NesWorldPackPlacement.MaximumAddressablePackBytes];

        var placement = NesWorldPackPlacement.Create(pack, R6Sections(16));

        Assert.Equal(8, placement.Segments.Count);
        Assert.Equal(pack.Length, placement.Segments.Sum(segment => segment.Length));
        Assert.Equal(new[] { 0, 3, 4, 5, 6, 7, 8, 9 }, placement.Segments.Select(segment => segment.PhysicalBank));
        Assert.Equal(0, placement.TranslateOffset(0).PhysicalBank);
        Assert.Equal(9, placement.TranslateOffset(pack.Length - 1).PhysicalBank);
    }

    /// <summary>
    /// A board past the mapper's 6-bit bank number is unbuildable, so it must not be mistaken for
    /// an R6 pool shortage that a bigger board could resolve.
    /// </summary>
    [Fact]
    public void The_six_bit_bank_number_ceiling_is_not_a_larger_board_signal()
    {
        var failure = NesLinkConstraints.Failure(NesLinkConstraint.Mmc3BankNumberCeiling, "ceiling");

        Assert.False(NesRomBuilder.RequiresLargerPrgBoard(failure));
    }

    private static IReadOnlyList<NesPrgSectionLayout> R6Sections(int prgBankCount) =>
        NesCartridgeLayout
            .Create(NesCartridgeProfile.Mmc3Tvrom, useFourScreenNametables: true, prgBankCount)
            .PrgSections
            .Where(section => section.Kind is NesPrgSectionKind.WorldR6)
            .OrderBy(section => section.PhysicalOffset)
            .ToArray();
}
