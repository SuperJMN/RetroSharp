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
        var publication = Assert.Single(scheduler.CreateCpuWorkReport([]).Contributors);
        Assert.Equal(SdkCpuWorkContributorIds.SpritePublish, publication.Id);
        Assert.Equal((1_983L, 1_983L), (publication.TotalLower, publication.TotalUpper));
    }
}
