namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using Xunit;
using Xunit.Abstractions;

public sealed class NesOamDmaPublicationTests(ITestOutputHelper output)
{
    private const string OneSpriteFixture = "validation/fixtures/nes-oam-dma-v1/one-sprite.retrosharp.json";
    private const string SixtySpritesFixture = "validation/fixtures/nes-oam-dma-v1/sixty-sprites.retrosharp.json";

    [Fact]
    public void Mmc3_packed_publication_cost_is_flat_between_one_and_sixty_sprites()
    {
        var one = Measure(OneSpriteFixture);
        var sixty = Measure(SixtySpritesFixture);

        output.WriteLine(Describe(one));
        output.WriteLine(Describe(sixty));

        Assert.True(sixty.HardwareSprites > one.HardwareSprites);
        Assert.Equal(one.DmaPublicationCycles, sixty.DmaPublicationCycles);
        Assert.Equal(NesFramePlan.OamDmaCycles, sixty.DmaPublicationCycles);
        Assert.True(
            sixty.LegacySequentialPublicationCycles > one.LegacySequentialPublicationCycles,
            "the legacy $2004 route must remain the scaling control.");
        Assert.True(
            sixty.LegacySequentialVideoSafeCycles > sixty.VideoSafeLimit,
            "the 60-sprite fixture documents a shape that the legacy sequential publication could not fit.");
        Assert.True(sixty.VideoSafeKnownUpper <= sixty.VideoSafeLimit);
    }

    [Theory]
    [InlineData(OneSpriteFixture)]
    [InlineData(SixtySpritesFixture)]
    public void Dma_oam_matches_the_legacy_2004_prefix_publication(string fixture)
    {
        var measurement = Measure(fixture);
        var legacyOam = Enumerable.Repeat((byte)0xFF, 256).ToArray();
        Array.Copy(measurement.DmaTransfer.SourceSnapshot, legacyOam, measurement.RetainedOamBytes);

        output.WriteLine(Describe(measurement));

        Assert.Equal(legacyOam, measurement.DmaTransfer.SourceSnapshot);
    }

    [Fact]
    public void Sixty_visible_sprites_publish_without_unsafe_writes_or_resets()
    {
        var measurement = Measure(SixtySpritesFixture);

        output.WriteLine(Describe(measurement));
        output.WriteLine(measurement.Observation.ToString());

        Assert.Equal(60, measurement.HardwareSprites);
        Assert.Equal(0, measurement.Observation.UnsafePpuWrites);
        Assert.Equal(0, measurement.Observation.UnsafeOamWrites);
        Assert.Equal(0, measurement.Observation.NewResets);
        Assert.True(measurement.Observation.MaximumOamWrites >= 256);
        Assert.True(measurement.Observation.MinimumVBlankSlack > 0);
    }

    private static Measurement Measure(string fixture)
    {
        var build = NesSampleProjectBuilds.Build(fixture);
        Assert.StartsWith("nes-mmc3", build.Report.SelectedProfile, StringComparison.Ordinal);
        var cpu = new NesTestCpu(build.Rom);
        cpu.RunFrames(80);
        var transfer = cpu.OamDmaTransfers.Last();
        var hardwareSprites = PublishedHardwareSprites(transfer.SourceSnapshot);
        var retainedOamBytes = hardwareSprites * 4;
        var videoSafe = Assert.Single(
            build.Report.CpuWork.Windows,
            window => window.Id == SdkCpuWorkWindowIds.VideoSafe);
        var publication = Assert.Single(
            videoSafe.Contributors,
            contributor => contributor.Id == SdkCpuWorkContributorIds.SpritePublish);
        var dmaCycles = publication.TotalUpper
            ?? throw new InvalidOperationException("DMA publication must have a finite upper bound.");
        var knownUpper = videoSafe.KnownUpper
            ?? throw new InvalidOperationException("The video-safe window must have a finite upper bound.");
        var legacyPublicationCycles = NesOamPublicationSchedule.CpuCyclesFor(retainedOamBytes);
        var legacyVideoSafeCycles = knownUpper - dmaCycles + legacyPublicationCycles;
        var observation = NesVideoSafeObserver.Observe(build.Rom, ["right", "b"]);

        return new Measurement(
            fixture,
            build.Report.SelectedProfile,
            transfer,
            hardwareSprites,
            retainedOamBytes,
            dmaCycles,
            legacyPublicationCycles,
            knownUpper,
            legacyVideoSafeCycles,
            videoSafe.Capacity,
            observation);
    }

    private static int PublishedHardwareSprites(IReadOnlyList<byte> sourceSnapshot)
    {
        var count = 0;
        for (var offset = 0; offset < sourceSnapshot.Count; offset += 4)
        {
            if (sourceSnapshot.Skip(offset).Take(4).All(value => value == 0xFF))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static string Describe(Measurement measurement) =>
        $"{measurement.Fixture}: profile={measurement.Profile} sprites={measurement.HardwareSprites} " +
        $"retainedBytes={measurement.RetainedOamBytes} dma={measurement.DmaPublicationCycles} " +
        $"legacy2004={measurement.LegacySequentialPublicationCycles} " +
        $"videoSafe={measurement.VideoSafeKnownUpper}/{measurement.VideoSafeLimit} " +
        $"legacyVideoSafe={measurement.LegacySequentialVideoSafeCycles}/{measurement.VideoSafeLimit}";

    private sealed record Measurement(
        string Fixture,
        string Profile,
        NesOamDmaTransfer DmaTransfer,
        int HardwareSprites,
        int RetainedOamBytes,
        long DmaPublicationCycles,
        long LegacySequentialPublicationCycles,
        long VideoSafeKnownUpper,
        long LegacySequentialVideoSafeCycles,
        long VideoSafeLimit,
        VideoSafeObservation Observation);
}
