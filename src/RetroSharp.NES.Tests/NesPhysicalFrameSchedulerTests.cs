namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using Xunit;
using static NesSdkOperationBoundaryTests;

public sealed class NesPhysicalFrameSchedulerTests
{
    [Fact]
    public void Mapper_zero_gameplay_boundary_emits_the_existing_fresh_vblank_edge()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mapper-0-current",
            hasFrameBoundary: true,
            usesRetainedOam: false,
            retainedOamByteCount: 0,
            usesPackedCameraRuntime: false,
            useSequentialOamPublication: false,
            useFourScreenNametables: false);
        var scheduler = new NesPhysicalFrameScheduler(builder, plan);

        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        Assert.Equal(
            [0x2C, 0x02, 0x20, 0x30, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB],
            builder.Build());
    }

    [Fact]
    public void Packed_gameplay_boundary_publishes_oam_only_after_a_fresh_hardware_vblank()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 4,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: false,
            useFourScreenNametables: true);
        var scheduler = new NesPhysicalFrameScheduler(builder, plan);

        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        var bytes = builder.Build();
        var pendingSignal = IndexOfSequence(bytes, [0xAD, 0x8F, 0x03]);
        var hardwareVBlank = IndexOfSequence(bytes, [0x2C, 0x02, 0x20, 0x10, 0xFB]);
        var oamDma = IndexOfSequence(bytes, [0xA9, 0x02, 0x8D, 0x14, 0x40]);
        var shadowReset = IndexOfSequence(bytes, [0xA9, 0xFF, 0xA2, 0x00, 0x9D, 0x00, 0x02]);

        Assert.True(pendingSignal >= 0);
        Assert.True(hardwareVBlank > pendingSignal);
        Assert.True(oamDma > hardwareVBlank);
        Assert.True(shadowReset > oamDma);
    }

    [Fact]
    public void Sequential_publication_bytes_and_cpu_projection_come_from_the_same_scheduler()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 152,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);
        var scheduler = new NesPhysicalFrameScheduler(builder, plan);

        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        Assert.True(IndexOfSequence(
            builder.Build(),
            [0xA9, 0x00, 0x8D, 0x03, 0x20, 0xA2, 0x68, 0xBD, 0x98, 0x01, 0x8D, 0x04, 0x20, 0xE8, 0xD0, 0xF7]) >= 0);
        var report = scheduler.CreateCpuWorkReport([]);
        var publication = Assert.Single(report.Contributors);
        Assert.Equal(SdkCpuWorkContributorIds.SpritePublish, publication.Id);
        Assert.Equal((2_135L, 2_135L), (publication.TotalLower, publication.TotalUpper));
        Assert.DoesNotContain(report.Contributors, contributor =>
            contributor.Id == SdkCpuWorkContributorIds.SpritePublishTransfer);
        Assert.DoesNotContain(report.Unknowns, unknown =>
            unknown.Id == SdkCpuWorkContributorIds.SpritePublish);
        var videoSafe = Assert.Single(report.Windows, window => window.Id == SdkCpuWorkWindowIds.VideoSafe);
        Assert.Equal((2_135L, 2_135L), (videoSafe.KnownLower, videoSafe.KnownUpper));
    }

    [Fact]
    public void Sequential_profile_without_retained_oam_needs_no_publication_schedule()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: false,
            retainedOamByteCount: 0,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);

        var scheduler = new NesPhysicalFrameScheduler(builder, plan);
        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        Assert.DoesNotContain(
            scheduler.CreateCpuWorkReport([]).Contributors,
            contributor => contributor.Id == SdkCpuWorkContributorIds.SpritePublish);
        Assert.True(IndexOfSequence(builder.Build(), [0x8D, 0x03, 0x20]) < 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Camera_row_deadline_must_match_the_emitted_phase_schedule(bool usesPackedCameraRuntime)
    {
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: false,
            retainedOamByteCount: 0,
            usesPackedCameraRuntime,
            useSequentialOamPublication: false,
            useFourScreenNametables: true);
        var staging = Assert.Single(plan.StagedWork);
        plan = plan with
        {
            StagedWork = [staging with { MaximumPhysicalFrames = staging.MaximumPhysicalFrames + 1 }],
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new NesPhysicalFrameScheduler(new PrgBuilder(), plan));

        Assert.Contains("deadline", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
