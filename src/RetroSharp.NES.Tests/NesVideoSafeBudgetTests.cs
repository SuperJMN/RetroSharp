namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// The NES packed-camera profile performs two independent pieces of work inside one hardware
/// VBlank: the packed background column commit and the retained-OAM publication. Each used to
/// carry its own isolated cap and neither was checked against the other, so a program could be
/// emitted whose joint commit overran VBlank and wrote to <c>$2007</c>/<c>$2004</c> on rendered
/// scanlines. On hardware that is visible corruption.
/// <para>
/// These tests hold the terminal condition: every shipping NES sample completes its video-safe
/// work inside VBlank while the camera is actually streaming, and a configuration that cannot
/// is rejected at build time instead of emitted silently.
/// </para>
/// </summary>
public sealed class NesVideoSafeBudgetTests(ITestOutputHelper output)
{
    public static TheoryData<string> NesSamples()
    {
        var data = new TheoryData<string>();
        foreach (var sample in NesSampleProjectBuilds.NesSamples())
        {
            data.Add(sample.RelativePath);
        }

        return data;
    }

    /// <summary>
    /// The player-visible acceptance scene: the runner scrolling under held input, which drives a
    /// full-height streamed band and 23 retained sprites through the same VBlank.
    /// </summary>
    [Theory]
    [MemberData(nameof(NesSamples))]
    public void Nes_samples_publish_only_inside_vblank(string projectRelativePath)
    {
        var rom = NesVideoSafeObserver.BuildNesRom(projectRelativePath);
        foreach (var held in NesVideoSafeObserver.HeldInputs)
        {
            var observation = NesVideoSafeObserver.Observe(rom, held);
            output.WriteLine($"{projectRelativePath} held=[{string.Join(",", held),-7}] {observation}");
            var scene = $"{projectRelativePath} holding [{string.Join(",", held)}]";
            Assert.True(
                observation.UnsafePpuWrites == 0,
                $"{scene} performed {observation.UnsafePpuWrites} PPU writes outside VBlank.");
            Assert.True(
                observation.UnsafeOamWrites == 0,
                $"{scene} performed {observation.UnsafeOamWrites} OAM writes outside VBlank.");
            Assert.True(
                observation.NewResets == 0,
                $"{scene} reset {observation.NewResets} times during steady state.");
        }
    }

    /// <summary>
    /// A streaming band that is tall enough to overrun VBlank on its own, driven by held input so
    /// the camera actually commits every few frames.
    /// </summary>
    [Fact]
    public void Tall_streaming_band_with_retained_sprites_publishes_only_inside_vblank()
    {
        var directory = NesVideoSafeObserver.RepositoryDirectory("samples/phase-banked-frame");
        var scene = File.ReadAllText(Path.Combine(directory, "src", "scene.rs"))
            .Replace("const i16 StreamHeight = 4;", "const i16 StreamHeight = 30;", StringComparison.Ordinal);
        Assert.Contains("StreamHeight = 30;", scene, StringComparison.Ordinal);
        var source = scene + Environment.NewLine + File.ReadAllText(Path.Combine(directory, "src", "control.rs"));
        var built = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [RetroSharp.Sdk.SdkImportResolver.Portable2D]);

        var observation = NesVideoSafeObserver.Observe(built.Rom, ["right", "b"]);
        output.WriteLine($"streamHeight=30 {observation}");
        Assert.True(observation.MaximumDataWrites > 30, "The band must actually commit while streaming.");
        Assert.True(observation.MaximumOamWrites > 0, "Retained sprites must share the same window.");
        Assert.Equal(0, observation.UnsafePpuWrites);
        Assert.Equal(0, observation.UnsafeOamWrites);
        Assert.Equal(0, observation.NewResets);
    }

    /// <summary>
    /// The modelled cost must be an upper bound on what the emitted code actually spends, or the
    /// build-time diagnostic would accept a program that still corrupts the display.
    /// </summary>
    [Theory]
    [InlineData("samples/runner/runner.retrosharp.json", 40, 92)]
    [InlineData("samples/phase-banked-frame/phase-banked-frame.retrosharp.json", 4, 76)]
    public void Modelled_video_safe_cost_bounds_measured_consumption(
        string projectRelativePath,
        int streamHeight,
        int retainedOamBytes)
    {
        var plan = NesFramePlan.Create(
            NesPhysicalFrameScheduler.CodeBankedProfileName,
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamBytes,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);
        var modelled = plan.VideoSafeCycleCost(new NesPackedColumnCommit(0, streamHeight));

        var rom = NesVideoSafeObserver.BuildNesRom(projectRelativePath);
        var observation = NesVideoSafeObserver.Observe(rom, ["right", "b"]);
        var measured = plan.VideoSafeCycleLimit - observation.MinimumVBlankSlack;
        output.WriteLine($"{projectRelativePath} modelled={modelled} measured={measured}");

        Assert.True(
            measured <= modelled,
            $"{projectRelativePath} spent {measured} video-safe cycles but the budget model allows only {modelled}.");
        Assert.True(
            modelled <= plan.VideoSafeCycleLimit,
            $"{projectRelativePath} is modelled at {modelled} cycles, above the {plan.VideoSafeCycleLimit}-cycle window.");
    }

    [Fact]
    public void Joint_commit_that_cannot_fit_vblank_is_rejected_with_the_offending_numbers()
    {
        var plan = NesFramePlan.Create(
            NesPhysicalFrameScheduler.CodeBankedProfileName,
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 152,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);
        var commit = new NesPackedColumnCommit(0, NesPackedCameraBudget.MaximumColumnPayloadTiles);

        var error = Assert.Throws<InvalidOperationException>(
            () => plan.RequireVideoSafeBudget(commit, "NES camera streamed band height 40"));

        output.WriteLine(error.Message);
        Assert.Contains("band height 40", error.Message, StringComparison.Ordinal);
        Assert.Contains("40 column tiles", error.Message, StringComparison.Ordinal);
        Assert.Contains("152 retained OAM bytes", error.Message, StringComparison.Ordinal);
        Assert.Contains(plan.VideoSafeCycleLimit.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains(
            plan.VideoSafeCycleCost(commit).ToString(),
            error.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The check has to run where the camera is configured, not only as a callable helper,
    /// otherwise an over-budget program is still emitted silently.
    /// </summary>
    [Fact]
    public void Configuring_an_over_budget_band_fails_before_emission()
    {
        var plan = NesFramePlan.Create(
            NesPhysicalFrameScheduler.CodeBankedProfileName,
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 152,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);
        var scheduler = new NesPhysicalFrameScheduler(new PrgBuilder(), plan);
        var config = new NesCameraConfig(
            MapWidth: 312,
            MapHeight: 40,
            StreamY: 0,
            StreamHeight: NesPackedCameraBudget.MaximumColumnPayloadTiles,
            UseFourScreenNametables: true);
        Assert.True(config.UsesStaticColumnBand);

        var error = Assert.Throws<InvalidOperationException>(() => scheduler.ConfigurePackedColumnBand(config));

        output.WriteLine(error.Message);
        Assert.Contains("band height 40", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipping_band_and_sprite_shapes_are_accepted()    {
        // The runner: a full 40-row static band together with 23 retained hardware sprites.
        var runner = NesFramePlan.Create(
            NesPhysicalFrameScheduler.CodeBankedProfileName,
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 92,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);
        runner.RequireVideoSafeBudget(
            new NesPackedColumnCommit(0, NesPackedCameraBudget.MaximumColumnPayloadTiles),
            "runner");

        // audio-mixed-load: no background commit, but the largest supported sprite publication.
        var sprites = NesFramePlan.Create(
            NesPhysicalFrameScheduler.CodeBankedProfileName,
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 152,
            usesPackedCameraRuntime: false,
            useSequentialOamPublication: true,
            useFourScreenNametables: false);
        sprites.RequireVideoSafeBudget(null, "audio-mixed-load");
    }
}
