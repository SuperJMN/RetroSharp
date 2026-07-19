namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class NesFramePlanTests
{
    [Fact]
    public void Packed_retained_profile_declares_physical_windows_and_bounded_staging()
    {
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 152,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);

        Assert.Equal("nes-mmc3-tvrom-v1", plan.CartridgeProfile);
        Assert.True(plan.UsesRetainedOam);
        Assert.Equal(152, plan.RetainedOamByteCount);
        Assert.True(plan.UseSequentialOamPublication);
        Assert.Equal(8, plan.MaximumCameraWalkStepsPerFrame);
        Assert.Equal(8, plan.CameraRowTileWritesPerFrame);
        Assert.Equal(4, plan.CameraRowAttributePhase);
        Assert.Equal(
            [
                (SdkCpuWorkWindowIds.Frame, 29_780L),
                (SdkCpuWorkWindowIds.VideoSafe, 2_273L),
            ],
            plan.Windows.Select(window => (window.Id, window.Capacity)));
        Assert.Contains(plan.MandatoryWork, work =>
            work.ContributorId == SdkCpuWorkContributorIds.SpritePublish &&
            work.WindowId == SdkCpuWorkWindowIds.VideoSafe);
        Assert.Contains(plan.MandatoryWork, work =>
            work.ContributorId == SdkCpuWorkContributorIds.WorldCommit &&
            work.WindowId == SdkCpuWorkWindowIds.VideoSafe);

        var staging = Assert.Single(plan.StagedWork);
        Assert.Equal(NesFramePlan.CameraRowStagingId, staging.Id);
        Assert.Equal(56, staging.MaximumPhysicalFrames);
    }

    [Fact]
    public void Sequential_publication_rejects_an_oam_prefix_outside_the_accepted_window_profile()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 156,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true));

        Assert.Contains("at most 38 hardware sprites", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cpu_work_projection_uses_the_selected_plan_windows()
    {
        var plan = NesFramePlan.Create(
            "nes-mapper-0-current",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 4,
            usesPackedCameraRuntime: false,
            useSequentialOamPublication: false,
            useFourScreenNametables: false);

        var report = plan.ProjectCpuWork(SdkCpuWorkReportFactory.ForNes(plan.CartridgeProfile));

        Assert.Equal(
            plan.Windows.Select(window => window.Id),
            report.Windows.Select(window => window.Id));
        var videoSafe = Assert.Single(report.Windows, window => window.Id == SdkCpuWorkWindowIds.VideoSafe);
        Assert.Equal(2_273, videoSafe.Capacity);
        Assert.Contains(videoSafe.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.SpritePublish);
        Assert.DoesNotContain(videoSafe.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.UserDynamicLoop);
    }
}
