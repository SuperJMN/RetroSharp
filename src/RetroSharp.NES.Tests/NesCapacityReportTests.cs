namespace RetroSharp.NES.Tests;

using System.Text.Json;
using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using Xunit;

/// <summary>
/// The capacity report is diagnostic, so these tests assert relationships rather than sizes: that
/// each headroom figure is reported against the region that owns it, that a banked build names its
/// phases and per-bank occupancy, and that the near-cliff warning tracks how full a region really
/// is. Nothing here pins a byte count, a cycle count, a hash, or a golden document.
/// </summary>
public sealed class NesCapacityReportTests
{
    /// <summary>The mapper-0 hot-path canary.</summary>
    private const string Mapper0Sample = "samples/falling-blocks/falling-blocks.retrosharp.json";

    /// <summary>The banked canary that owns a frame loop, so it has a hot phase to place.</summary>
    private const string BankedSample = "samples/phase-banked-frame/phase-banked-frame.retrosharp.json";

    /// <summary>A versioned fixture with retained sprites and a frame boundary.</summary>
    private const string BankedFrameLoadFixture =
        "validation/fixtures/nes-banked-frame-load-v1/nes-banked-frame-load-v1.retrosharp.json";

    /// <summary>A versioned fixture whose banked program has a hot frame phase.</summary>
    private const string PrgBoardEscalationFixture =
        "validation/fixtures/nes-prg-board-escalation-v1/nes-prg-board-escalation-v1.retrosharp.json";

    [Fact]
    public void Mapper0_sample_reports_its_profile_size_and_fixed_headroom()
    {
        var build = NesSampleProjectBuilds.Build(Mapper0Sample);

        var report = NesCapacityReportProjection.Create(build);

        Assert.Equal(NesCapacityReportProjection.Schema, report.Schema);
        Assert.Equal("nes", report.Target);
        Assert.Equal(build.Report.SelectedProfile, report.SelectedProfile);
        Assert.Contains("mapper-0", report.SelectedProfile, StringComparison.Ordinal);
        Assert.Equal(build.Report.PrgRomSize, report.PrgRomSizeBytes);
        Assert.Equal(build.Report.ChrRomSize, report.ChrRomSizeBytes);
        AssertRegion(report.FixedRegion, NesCapacityReportProjection.FixedRegionName);
        Assert.Equal(build.Report.FixedPayloadBytes, report.FixedRegion.UsedBytes);
        Assert.Equal(build.Report.FixedHeadroomBytes, report.FixedRegion.HeadroomBytes);

        // Nothing is banked here, so the report says so instead of printing an empty phase map.
        Assert.Null(report.BankedProgram);
        Assert.Contains(report.Notes, note => note.Contains("fixed-resident", StringComparison.Ordinal));
        Assert.DoesNotContain(
            report.Warnings,
            warning => warning.Resource == NesCapacityReportProjection.BankedRegionName);
    }

    [Fact]
    public void Banked_sample_maps_every_phase_to_its_banks_and_reports_both_headroom_figures()
    {
        var build = NesSampleProjectBuilds.Build(BankedSample);
        var placement = Assert.IsType<NesProgramBankPlacementReport>(build.Report.BankPlacement);

        var report = NesCapacityReportProjection.Create(build);

        AssertRegion(report.FixedRegion, NesCapacityReportProjection.FixedRegionName);
        Assert.Equal(build.Report.FixedHeadroomBytes, report.FixedRegion.HeadroomBytes);

        var banked = Assert.IsType<NesCapacityBankedProgram>(report.BankedProgram);
        AssertRegion(banked.Region, NesCapacityReportProjection.BankedRegionName);
        Assert.Equal(placement.ProgramR6HeadroomBytes, banked.Region.HeadroomBytes);

        Assert.Equal(
            placement.Phases.Select(phase => phase.UnitName),
            banked.Phases.Select(phase => phase.Unit));
        Assert.All(banked.Phases, phase => Assert.NotEmpty(phase.Phase));
        var hot = Assert.Single(banked.Phases.Where(phase => phase.IsHotFramePhase));
        Assert.Equal(banked.HotPhaseUnit, hot.Unit);
        Assert.Equal(placement.HotPhasePhysicalBank, banked.HotPhasePhysicalBank);
        Assert.Contains(banked.HotPhasePhysicalBank!.Value, hot.PhysicalBanks);

        Assert.NotEmpty(banked.Banks);
        Assert.All(banked.Banks, bank => Assert.InRange(bank.UsedBytes, 0, bank.CapacityBytes));
        Assert.Equal(build.Report.ProgramR6Bytes, banked.Region.UsedBytes);
        foreach (var phase in banked.Phases.Where(phase => phase.Bytes > 0))
        {
            Assert.All(
                phase.PhysicalBanks,
                bank => Assert.Contains(banked.Banks, occupancy => occupancy.PhysicalBank == bank));
        }
    }

    [Fact]
    public void Fixed_headroom_shrinks_as_logic_is_added_and_only_a_nearly_full_region_warns()
    {
        var small = Report(Filler(50));
        var larger = Report(Filler(150));

        Assert.Equal(small.SelectedProfile, larger.SelectedProfile);
        Assert.Equal(small.FixedRegion.CapacityBytes, larger.FixedRegion.CapacityBytes);
        Assert.True(
            larger.FixedRegion.HeadroomBytes < small.FixedRegion.HeadroomBytes,
            "adding logic must consume fixed headroom");
        Assert.Empty(small.Warnings);
        Assert.Empty(larger.Warnings);

        // Calibrate from the two builds above instead of pinning a size, then aim just past the
        // near-cliff share so the region is nearly full but the program still fits.
        var bytesPerStatement = (larger.FixedRegion.UsedBytes - small.FixedRegion.UsedBytes) / 100.0;
        Assert.True(bytesPerStatement > 0, "the filler must emit code");
        var targetHeadroom =
            larger.FixedRegion.CapacityBytes * (NesCapacityReportProjection.NearCliffHeadroomPercent - 2) / 100.0;
        var statements = 150 + (int)((larger.FixedRegion.HeadroomBytes - targetHeadroom) / bytesPerStatement);

        var nearCliff = Report(Filler(statements));

        Assert.Equal(larger.SelectedProfile, nearCliff.SelectedProfile);
        Assert.True(nearCliff.FixedRegion.HeadroomBytes > 0, "the near-cliff program must still fit");
        Assert.True(nearCliff.FixedRegion.HeadroomBytes < larger.FixedRegion.HeadroomBytes);
        var warning = Assert.Single(nearCliff.Warnings);
        Assert.Equal(NesCapacityReportProjection.NearCliffCategory, warning.Category);
        Assert.Equal(NesCapacityReportProjection.FixedRegionName, warning.Resource);
        Assert.Equal(nearCliff.FixedRegion.HeadroomBytes, warning.Headroom);
        Assert.True(warning.HeadroomPercent < warning.ThresholdPercent);
        Assert.Contains("warning, not an error", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixture_video_safe_report_matches_the_enforced_frame_plan_cost()
    {
        var build = NesSampleProjectBuilds.Build(BankedFrameLoadFixture);
        var expectedPlan = NesFramePlan.Create(
            build.Report.SelectedProfile,
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 4,
            usesPackedCameraRuntime: false,
            useSequentialOamPublication: false,
            useFourScreenNametables: false);
        var imposed = expectedPlan.VideoSafeCycleCost(null);

        var report = NesCapacityReportProjection.Create(build);

        var videoSafe = Assert.Single(
            report.CpuWork.Windows,
            window => window.Window == SdkCpuWorkWindowIds.VideoSafe);
        Assert.Equal(imposed, videoSafe.KnownLowerCycles);
        Assert.Equal(imposed, videoSafe.KnownUpperCycles);
        Assert.InRange(imposed, 700, 800);

        var videoSafeResource = AssertResource(report, SdkCpuWorkWindowIds.VideoSafe);
        Assert.Equal(imposed, videoSafeResource.Used);
        Assert.Equal(videoSafe.CapacityCycles, videoSafeResource.Capacity);
    }

    [Fact]
    public void Resources_share_one_vocabulary_and_the_binding_constraint_has_the_lowest_relative_headroom()
    {
        var report = NesCapacityReportProjection.Create(NesSampleProjectBuilds.Build(PrgBoardEscalationFixture));

        Assert.False(string.IsNullOrWhiteSpace(report.BindingConstraint.Name));
        AssertResource(report, NesCapacityReportProjection.FixedRegionName);
        AssertResource(report, NesCapacityReportProjection.BankedRegionName);
        AssertResource(report, NesCapacityReportProjection.HotPhaseBankPrefix + report.BankedProgram!.HotPhaseUnit);
        AssertResource(report, SdkCpuWorkWindowIds.Frame);
        AssertResource(report, SdkCpuWorkWindowIds.VideoSafe);
        Assert.All(report.Resources, resource =>
        {
            Assert.False(string.IsNullOrWhiteSpace(resource.Name));
            Assert.False(string.IsNullOrWhiteSpace(resource.Unit));
            Assert.InRange(resource.Used, 0, resource.Capacity);
            Assert.Equal(resource.Capacity - resource.Used, resource.Headroom);
            Assert.False(string.IsNullOrWhiteSpace(resource.NextUnit));
            Assert.True(resource.NextUnitCost > 0);
            Assert.False(string.IsNullOrWhiteSpace(resource.Remedy));
        });

        var binding = Assert.Single(report.Resources, resource => resource.IsBindingConstraint);
        var lowestRelativeHeadroom = report.Resources
            .Where(resource => resource.Capacity > 0)
            .Min(resource => resource.Headroom / (double)resource.Capacity);
        Assert.Equal(report.BindingConstraint.Name, binding.Name);
        Assert.Equal(lowestRelativeHeadroom, binding.Headroom / (double)binding.Capacity);
    }

    [Fact]
    public void Near_cliff_warnings_cover_hot_phase_and_cpu_window_resources()
    {
        var report = NesCapacityReportProjection.Create(SyntheticNearCliffBuild());
        var hotResource = NesCapacityReportProjection.HotPhaseBankPrefix + "program:main:frame";

        Assert.Contains(report.Warnings, warning => warning.Resource == hotResource);
        Assert.Contains(report.Warnings, warning => warning.Resource == SdkCpuWorkWindowIds.VideoSafe);
        Assert.DoesNotContain(
            report.Warnings,
            warning => warning.Resource == NesCapacityReportProjection.BankedRegionName);
        Assert.Contains(
            report.BindingConstraint.Name,
            report.BindingConstraint.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Serialized_report_is_deterministic_json_that_names_both_headroom_figures()
    {
        var build = NesSampleProjectBuilds.Build(BankedSample);

        var json = NesCapacityReportProjection.Serialize(build);

        Assert.Equal(json, NesCapacityReportProjection.Serialize(build));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(NesCapacityReportProjection.Schema, root.GetProperty("schema").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("bindingConstraint").GetProperty("name").GetString()));
        Assert.Equal(
            build.Report.FixedHeadroomBytes,
            root.GetProperty("fixedRegion").GetProperty("headroomBytes").GetInt32());
        Assert.Equal(
            build.Report.BankPlacement!.ProgramR6HeadroomBytes,
            root.GetProperty("bankedProgram").GetProperty("region").GetProperty("headroomBytes").GetInt32());
        Assert.NotEmpty(root.GetProperty("bankedProgram").GetProperty("phases").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("resources").EnumerateArray());
        Assert.Contains(
            "ROM table",
            root.GetProperty("duplication").GetProperty("coverage").GetString()!,
            StringComparison.Ordinal);
    }

    private static void AssertRegion(NesCapacityRegion region, string expectedName)
    {
        Assert.Equal(expectedName, region.Region);
        Assert.Equal(region.UsedBytes + region.HeadroomBytes, region.CapacityBytes);
        Assert.True(region.UsedBytes > 0, $"{expectedName} must hold something");
        Assert.True(region.HeadroomBytes >= 0);
        Assert.InRange(region.UsedPercent, 0, 100);
    }

    private static NesCapacityResource AssertResource(NesCapacityReport report, string expectedName) =>
        Assert.Single(report.Resources, resource => resource.Name == expectedName);

    private static NesCapacityReport Report(string source) =>
        NesCapacityReportProjection.Create(RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(source));

    private static NesRomBuildResult SyntheticNearCliffBuild()
    {
        var hotCapacity = NesProgramBankPlanner.ProgramBankSize - NesProgramBankPlanner.BankEdgeJumpSize;
        var hotBytes = hotCapacity - 16;
        var placement = new NesProgramBankPlacementReport(
            [
                new NesProgramPhaseBankPlacement(
                    "program:main:frame",
                    NesPrgPlacementPhase.Hot,
                    [4],
                    hotBytes),
            ],
            HotPhasePhysicalBank: 4,
            HotPhaseUnitName: "program:main:frame",
            HotPhaseBytes: hotBytes,
            ProgramR6HeadroomBytes: 12_000,
            DuplicatedSharedBytes: 0,
            [
                new NesProgramBankOccupancy(4, hotBytes, NesProgramBankPlanner.ProgramBankSize),
                new NesProgramBankOccupancy(5, 0, NesProgramBankPlanner.ProgramBankSize),
            ]);
        var videoSafeContributor = SdkCpuWorkContributor.Create(
            SdkCpuWorkContributorIds.WorldCommit,
            SdkCpuWorkContributorCategories.TargetRuntime,
            "synthetic near-cliff video-safe budget",
            count: 1,
            unitLower: 2_200,
            unitUpper: 2_200,
            calibration: "test");
        var cpuWork = SdkCpuWorkReport.Create(
            "nes",
            "synthetic",
            "cpu-cycles",
            29_780,
            [],
            [],
            [
                SdkCpuWorkWindowReport.Create(SdkCpuWorkWindowIds.Frame, 29_780, [], []),
                SdkCpuWorkWindowReport.Create(SdkCpuWorkWindowIds.VideoSafe, 2_273, [videoSafeContributor], []),
            ]);
        var buildReport = new NesRomBuildReport(
            "synthetic",
            PrgRomSize: 65_536,
            ChrRomSize: 8_192,
            FixedPayloadBytes: 1_000,
            ProgramR6Bytes: hotBytes,
            FixedVeneerBytes: 0,
            PinnedR7Bytes: 0,
            BootR7Bytes: 0,
            ResidentChrBytes: 0,
            Segments: [],
            FixedSymbols: new Dictionary<string, ushort>(StringComparer.Ordinal),
            BankedSymbols: new Dictionary<string, NesPrgSymbol>(StringComparer.Ordinal),
            PlacementUnits: [],
            UserVariables: [],
            RuntimeRegions: [],
            SharedSdkSubroutines: [],
            CpuWork: cpuWork,
            FixedHeadroomBytes: 8_000,
            BankPlacement: placement,
            UserFunctionCalls: NesUserFunctionCallAccountingReport.Empty,
            OutlinedUserFunctions: []);
        return new NesRomBuildResult([], buildReport);
    }

    /// <summary>
    /// A program whose emitted size grows linearly with <paramref name="statements"/>, so a test can
    /// calibrate how much logic it takes to approach the fixed region instead of pinning a size.
    /// </summary>
    private static string Filler(int statements)
    {
        var body = string.Join(
            "\n",
            Enumerable.Range(0, statements).Select(index => $"  a = a + {index % 7 + 1}; b = b + a;"));
        return $"void Main() {{\n  u8 a = 1;\n  u8 b = 2;\n{body}\n}}\n";
    }
}
