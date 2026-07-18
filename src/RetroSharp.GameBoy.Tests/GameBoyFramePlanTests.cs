namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class GameBoyFramePlanTests
{
    [Fact]
    public void Packed_retained_profile_declares_physical_windows_and_bounded_staging()
    {
        var plan = GameBoyFramePlan.Create(
            "gb-simple-mbc1-current",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            usesPackedCameraRuntime: true);

        Assert.Equal("gb-simple-mbc1-current", plan.CartridgeProfile);
        Assert.True(plan.UsesRetainedOam);
        Assert.True(plan.UsesPackedCameraRuntime);
        Assert.Equal(16, plan.MaximumCameraWalkStepsPerFrame);
        Assert.True(plan.SerializePackedDiagonalPreparation);
        Assert.Equal(
            [
                (SdkCpuWorkWindowIds.Frame, 70_224L),
                (SdkCpuWorkWindowIds.VideoSafe, 4_560L),
            ],
            plan.Windows.Select(window => (window.Id, window.Capacity)));
        Assert.Contains(plan.MandatoryWork, work =>
            work.ContributorId == SdkCpuWorkContributorIds.SpritePublish &&
            work.WindowId == SdkCpuWorkWindowIds.VideoSafe);
        Assert.Contains(plan.MandatoryWork, work =>
            work.ContributorId == SdkCpuWorkContributorIds.WorldPrepare &&
            work.WindowId == SdkCpuWorkWindowIds.Frame);
        Assert.Contains(plan.MandatoryWork, work =>
            work.ContributorId == SdkCpuWorkContributorIds.WorldCommit &&
            work.WindowId == SdkCpuWorkWindowIds.VideoSafe);

        var staging = Assert.Single(plan.StagedWork);
        Assert.Equal("packed-camera-edge", staging.Id);
        Assert.Equal(SdkCpuWorkWindowIds.Frame, staging.PrepareWindowId);
        Assert.Equal(SdkCpuWorkWindowIds.VideoSafe, staging.CommitWindowId);
        Assert.Equal(3, staging.MaximumPhysicalFrames);
    }

    [Fact]
    public void Cpu_work_projection_uses_the_selected_plan_windows()
    {
        var plan = GameBoyFramePlan.Create(
            "gb-rom-only-current",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            usesPackedCameraRuntime: false);

        var report = plan.ProjectCpuWork(SdkCpuWorkReportFactory.ForGameBoy(plan.CartridgeProfile));

        Assert.Equal(plan.CartridgeProfile, report.Profile);
        Assert.Equal(
            plan.Windows.Select(window => window.Id),
            report.Windows.Select(window => window.Id));
        var videoSafe = Assert.Single(report.Windows, window => window.Id == SdkCpuWorkWindowIds.VideoSafe);
        Assert.Equal(4_560, videoSafe.Capacity);
        Assert.Contains(videoSafe.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.FrameBoundaryActive);
        Assert.Contains(videoSafe.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.SpritePublish);
        Assert.DoesNotContain(videoSafe.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.UserDynamicLoop);
    }
}
