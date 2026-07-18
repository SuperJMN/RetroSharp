namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class SdkCpuWorkReportTests
{
    [Fact]
    public void Contributor_computes_checked_totals()
    {
        var contributor = SdkCpuWorkContributor.Create(
            "generated.test",
            SdkCpuWorkContributorCategories.Generated,
            "three generated visits",
            count: 3,
            unitLower: 10,
            unitUpper: 14,
            calibration: "test/v1");

        Assert.Equal(30, contributor.TotalLower);
        Assert.Equal(42, contributor.TotalUpper);
    }

    [Theory]
    [InlineData("gb", "t-cycles", 70_224, 70_223, 70_223)]
    [InlineData("gb", "t-cycles", 70_224, 70_224, 70_224)]
    [InlineData("nes", "cpu-cycles", 29_780, 29_779, 29_779)]
    [InlineData("nes", "cpu-cycles", 29_780, 29_780, 29_780)]
    public void Report_status_is_fits_when_finite_known_work_is_within_target_window(
        string target,
        string unit,
        long frameWindow,
        long lower,
        long upper)
    {
        var report = CreateReport(target, unit, frameWindow, lower, upper, []);

        Assert.Equal(SdkCpuWorkStatuses.Fits, report.Status);
        Assert.Equal(lower, report.KnownLower);
        Assert.Equal(upper, report.KnownUpper);
    }

    [Theory]
    [InlineData("gb", "t-cycles", 70_224, 70_224, 70_225)]
    [InlineData("nes", "cpu-cycles", 29_780, 29_780, 29_781)]
    public void Report_status_is_crosses_when_bounded_known_work_can_exceed_target_window(
        string target,
        string unit,
        long frameWindow,
        long lower,
        long upper)
    {
        var report = CreateReport(target, unit, frameWindow, lower, upper, []);

        Assert.Equal(SdkCpuWorkStatuses.Crosses, report.Status);
    }

    [Theory]
    [InlineData("gb", "t-cycles", 70_224, 70_225, 70_225)]
    [InlineData("nes", "cpu-cycles", 29_780, 29_781, 29_781)]
    public void Report_status_is_exceeds_when_lower_bound_cannot_fit_target_window(
        string target,
        string unit,
        long frameWindow,
        long lower,
        long upper)
    {
        var report = CreateReport(target, unit, frameWindow, lower, upper, []);

        Assert.Equal(SdkCpuWorkStatuses.Exceeds, report.Status);
    }

    [Theory]
    [InlineData("gb", "t-cycles", 70_224)]
    [InlineData("nes", "cpu-cycles", 29_780)]
    public void Report_status_is_incomplete_when_unknown_work_remains(
        string target,
        string unit,
        long frameWindow)
    {
        var report = CreateReport(
            target,
            unit,
            frameWindow,
            lower: 1,
            upper: 1,
            [new SdkCpuWorkUnknown(SdkCpuWorkContributorIds.RuntimeUncalibratedState, "not calibrated")]);

        Assert.Equal(SdkCpuWorkStatuses.Incomplete, report.Status);
        Assert.Equal(1, report.KnownLower);
        Assert.Equal(1, report.KnownUpper);
    }

    [Fact]
    public void Existing_report_creation_keeps_window_projection_additive()
    {
        var report = CreateReport("gb", "t-cycles", 70_224, 1, 1, []);

        Assert.Empty(report.Windows);
    }

    [Theory]
    [InlineData(100, 99, 99, false, SdkCpuWorkStatuses.Fits)]
    [InlineData(100, 100, 101, false, SdkCpuWorkStatuses.Crosses)]
    [InlineData(100, 101, 101, false, SdkCpuWorkStatuses.Exceeds)]
    [InlineData(100, 99, 99, true, SdkCpuWorkStatuses.Incomplete)]
    public void Physical_window_uses_the_same_checked_status_contract(
        long capacity,
        long lower,
        long upper,
        bool hasUnknown,
        string expectedStatus)
    {
        var window = SdkCpuWorkWindowReport.Create(
            SdkCpuWorkWindowIds.VideoSafe,
            capacity,
            [TestContributor(lower, upper)],
            hasUnknown
                ? [new SdkCpuWorkUnknown(SdkCpuWorkContributorIds.RuntimeUncalibratedState, "not calibrated")]
                : []);

        Assert.Equal(SdkCpuWorkWindowIds.VideoSafe, window.Id);
        Assert.Equal(capacity, window.Capacity);
        Assert.Equal(lower, window.KnownLower);
        Assert.Equal(upper, window.KnownUpper);
        Assert.Equal(expectedStatus, window.Status);
    }

    [Fact]
    public void Report_retains_ordered_physical_windows_without_changing_whole_frame_fields()
    {
        var videoSafe = SdkCpuWorkWindowReport.Create(
            SdkCpuWorkWindowIds.VideoSafe,
            capacity: 4_560,
            [TestContributor(120, 140)],
            []);

        var report = SdkCpuWorkReport.Create(
            "nes",
            profile: "nes-test",
            unit: "cpu-cycles",
            frameWindow: 29_780,
            [TestContributor(1_000, 1_200)],
            [],
            [videoSafe]);

        Assert.Equal(29_780, report.FrameWindow);
        Assert.Equal(1_000, report.KnownLower);
        Assert.Equal(1_200, report.KnownUpper);
        Assert.Equal(SdkCpuWorkStatuses.Fits, report.Status);
        Assert.Same(videoSafe, Assert.Single(report.Windows));
    }

    private static SdkCpuWorkReport CreateReport(
        string target,
        string unit,
        long frameWindow,
        long lower,
        long upper,
        IReadOnlyList<SdkCpuWorkUnknown> unknowns) =>
        SdkCpuWorkReport.Create(
            target,
            profile: $"{target}-test",
            unit,
            frameWindow,
            [TestContributor(lower, upper)],
            unknowns);

    private static SdkCpuWorkContributor TestContributor(long lower, long upper) =>
        SdkCpuWorkContributor.Create(
            "generated.test",
            SdkCpuWorkContributorCategories.Generated,
            "one test contributor",
            count: 1,
            unitLower: lower,
            unitUpper: upper,
            calibration: "test/v1");
}
