namespace RetroSharp.NES.Tests;

using System.Text.Json;
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
            warning => warning.Region == NesCapacityReportProjection.BankedRegionName);
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
        Assert.Equal(NesCapacityReportProjection.FixedRegionName, warning.Region);
        Assert.Equal(nearCliff.FixedRegion.HeadroomBytes, warning.HeadroomBytes);
        Assert.True(warning.HeadroomPercent < warning.ThresholdPercent);
        Assert.Contains("warning, not an error", warning.Message, StringComparison.Ordinal);
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
        Assert.Equal(
            build.Report.FixedHeadroomBytes,
            root.GetProperty("fixedRegion").GetProperty("headroomBytes").GetInt32());
        Assert.Equal(
            build.Report.BankPlacement!.ProgramR6HeadroomBytes,
            root.GetProperty("bankedProgram").GetProperty("region").GetProperty("headroomBytes").GetInt32());
        Assert.NotEmpty(root.GetProperty("bankedProgram").GetProperty("phases").EnumerateArray());
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

    private static NesCapacityReport Report(string source) =>
        NesCapacityReportProjection.Create(RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(source));

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
