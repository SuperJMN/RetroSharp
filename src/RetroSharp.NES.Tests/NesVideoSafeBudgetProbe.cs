namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Investigation harness for the NES video-safe (VBlank) budget.
/// <para>
/// The MMC3 packed-camera profile performs two independent pieces of work inside the same
/// hardware VBlank: the packed background column commit
/// (<see cref="NesPackedCameraRuntime.MaximumColumnPayloadLength"/> tiles plus
/// <see cref="NesPackedCameraRuntime.MaximumAttributeBytes"/> attribute bytes) and the
/// sequential retained-OAM publication through <c>$2004</c> (up to 152 bytes). Each has its own
/// isolated cap; neither is checked against the other, and their sum can exceed the ~2,273 CPU
/// cycles of NTSC VBlank. When it does, the tail of the OAM publication lands on the pre-render
/// and visible scanlines, which is real hardware corruption.
/// </para>
/// <para>
/// This harness only measures and reports. It intentionally asserts nothing about the budget so
/// that it stays green both before and after a fix; the numbers it prints are the evidence.
/// </para>
/// </summary>
public sealed class NesVideoSafeBudgetProbe(ITestOutputHelper output)
{
    /// <summary>
    /// Sweeps the streaming band height of the <c>phase-banked-frame</c> scene, which publishes
    /// 76 retained OAM bytes per frame, and reports where the joint VBlank cost crosses over.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(30)]
    public void Stream_height_sweep(int streamHeight)
    {
        var directory = NesVideoSafeObserver.RepositoryDirectory("samples/phase-banked-frame");
        var scene = File.ReadAllText(Path.Combine(directory, "src", "scene.rs"))
            .Replace("const i16 StreamHeight = 4;", $"const i16 StreamHeight = {streamHeight};", StringComparison.Ordinal);
        Assert.Contains($"StreamHeight = {streamHeight};", scene, StringComparison.Ordinal);
        var source = scene + Environment.NewLine + File.ReadAllText(Path.Combine(directory, "src", "control.rs"));
        var built = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        var config = new NesCameraConfig(312, 30, 0, streamHeight, (built.Rom[6] & 0x08) != 0);
        var observation = NesVideoSafeObserver.Observe(built.Rom, ["right", "b"]);
        output.WriteLine(
            $"streamHeight={streamHeight,2} profile={built.Report.SelectedProfile} " +
            $"staticBand={config.UsesStaticColumnBand} tiles={config.ColumnPayloadLength,2} " +
            $"attributeBytes={config.ColumnCommit.AttributeCount} {observation}");

        // The scene keeps running either way: the defect is corruption, not a crash.
        Assert.Equal(0, observation.NewResets);
    }

    /// <summary>
    /// Reports the observed video-safe burst of every tracked NES sample ROM under
    /// <c>samples/</c>.
    /// </summary>
    [Fact]
    public void Shipping_sample_survey()
    {
        var repository = NesVideoSafeObserver.RepositoryDirectory("samples");
        foreach (var romPath in Directory
            .GetFiles(repository, "*.nes", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            var rom = File.ReadAllBytes(romPath);
            var id = Path.GetRelativePath(Path.GetDirectoryName(repository)!, romPath);
            foreach (var held in new[] { Array.Empty<string>(), ["right"], ["right", "b"] })
            {
                output.WriteLine($"{id,-52} held=[{string.Join(",", held),-7}] {NesVideoSafeObserver.Observe(rom, held)}");
            }
        }
    }

    /// <summary>
    /// Same survey against freshly linked ROMs, so the numbers are not read from a stale
    /// tracked artifact.
    /// </summary>
    [Theory]
    [InlineData("samples/runner/runner.retrosharp.json")]
    [InlineData("samples/audio-mixed-load/audio-mixed-load.retrosharp.json")]
    [InlineData("samples/phase-banked-frame/phase-banked-frame.retrosharp.json")]
    public void Freshly_built_project_survey(string projectRelativePath)
    {
        var rom = NesVideoSafeObserver.BuildNesRom(projectRelativePath);
        foreach (var held in NesVideoSafeObserver.HeldInputs)
        {
            output.WriteLine(
                $"{projectRelativePath} held=[{string.Join(",", held),-7}] {NesVideoSafeObserver.Observe(rom, held)}");
        }
    }
}
