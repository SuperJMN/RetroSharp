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
    private const int WarmupFrames = 40;
    private const int ObservedFrames = 120;

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
        var directory = RepositoryDirectory("samples/phase-banked-frame");
        var scene = File.ReadAllText(Path.Combine(directory, "src", "scene.rs"))
            .Replace("const i16 StreamHeight = 4;", $"const i16 StreamHeight = {streamHeight};", StringComparison.Ordinal);
        Assert.Contains($"StreamHeight = {streamHeight};", scene, StringComparison.Ordinal);
        var source = scene + Environment.NewLine + File.ReadAllText(Path.Combine(directory, "src", "control.rs"));
        var built = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        var config = new NesCameraConfig(312, 30, 0, streamHeight, (built.Rom[6] & 0x08) != 0);
        var observation = Observe(built.Rom, ["right", "b"]);
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
        var repository = RepositoryDirectory("samples");
        foreach (var romPath in Directory
            .GetFiles(repository, "*.nes", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            var rom = File.ReadAllBytes(romPath);
            var id = Path.GetRelativePath(Path.GetDirectoryName(repository)!, romPath);
            foreach (var held in new[] { Array.Empty<string>(), ["right"], ["right", "b"] })
            {
                output.WriteLine($"{id,-52} held=[{string.Join(",", held),-7}] {Observe(rom, held)}");
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
        var projectPath = RepositoryFile(projectRelativePath);
        var romPath = Path.Combine(AppContext.BaseDirectory, $"video-safe-probe-{Guid.NewGuid():N}.nes");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RetroSharp.Cli.CliRunner.Run(
                ["--target", "nes", "--out", romPath, projectPath],
                stdout,
                stderr);
            Assert.True(exitCode == 0, stdout + stderr.ToString());

            var rom = File.ReadAllBytes(romPath);
            foreach (var held in new[] { Array.Empty<string>(), ["right"], ["right", "b"] })
            {
                output.WriteLine($"{projectRelativePath} held=[{string.Join(",", held),-7}] {Observe(rom, held)}");
            }
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    private static VideoSafeObservation Observe(byte[] rom, IReadOnlyCollection<string> held)
    {
        var cpu = new NesTestCpu(rom);
        cpu.Held.UnionWith(held);
        cpu.RunFrames(WarmupFrames);
        var startFrame = cpu.PhysicalFrames;
        var startPpu = cpu.PpuWrites.Count;
        var startOam = cpu.OamWrites.Count;
        var resetsBefore = cpu.ResetCount;
        cpu.RunFrames(startFrame + ObservedFrames);

        var ppuWrites = cpu.PpuWrites.Skip(startPpu).ToArray();
        var oamWrites = cpu.OamWrites.Skip(startOam).ToArray();
        bool IsUnsafe(long cycle, bool rendering) =>
            rendering && cpu.PpuTiming(cycle, true).Phase != "vblank";

        var bursts = Bursts(ppuWrites);
        return new VideoSafeObservation(
            ppuWrites.Count(write => IsUnsafe(write.Cycle, write.RenderingEnabled)),
            oamWrites.Count(write => IsUnsafe(write.Cycle, write.RenderingEnabled)),
            bursts.Count,
            bursts.Count == 0 ? 0 : bursts.Max(burst => burst.DataWrites),
            bursts.Count == 0 ? 0 : bursts.Max(burst => burst.OamWrites),
            bursts.Count == 0 ? 0 : bursts.Max(burst => burst.End - burst.Start),
            bursts.Count == 0 ? 0 : bursts.Min(burst => VBlankCyclesRemaining(cpu, burst.End)),
            bursts.Count == 0 ? 0 : Median(bursts.Select(burst => VBlankCyclesElapsed(cpu, burst.Start)).ToArray()),
            cpu.ResetCount - resetsBefore);
    }

    private sealed record VideoSafeObservation(
        int UnsafePpuWrites,
        int UnsafeOamWrites,
        int Bursts,
        int MaximumDataWrites,
        int MaximumOamWrites,
        long LongestBurstCycles,
        long MinimumVBlankSlack,
        long MedianEntryLatency,
        int NewResets)
    {
        public override string ToString() =>
            $"unsafePpu={UnsafePpuWrites,4} unsafeOam={UnsafeOamWrites,4} bursts={Bursts,3} " +
            $"maxPpuData={MaximumDataWrites,3} maxOamBytes={MaximumOamWrites,3} " +
            $"longestBurst={LongestBurstCycles,5} entryLatency={MedianEntryLatency,4} " +
            $"minVblankSlack={MinimumVBlankSlack,6}";
    }

    // CPU cycles elapsed since hardware VBlank started (scanline 241, dot 0).
    private static long VBlankCyclesElapsed(NesTestCpu cpu, long cycle) =>
        AbsoluteDot(cpu, cycle) / 3 - 241 * 341 / 3;

    // CPU cycles left in hardware VBlank after the given cycle; negative means the work overran.
    private static long VBlankCyclesRemaining(NesTestCpu cpu, long cycle) =>
        (261 * 341 - AbsoluteDot(cpu, cycle)) / 3;

    private static long AbsoluteDot(NesTestCpu cpu, long cycle)
    {
        var timing = cpu.PpuTiming(cycle, true);
        // VBlank work that spills past the frame end wraps to a low scanline; keep it monotone.
        var scanline = timing.Scanline < 200 ? timing.Scanline + 262 : timing.Scanline;
        return scanline * 341 + timing.Dot;
    }

    // One burst is one frame's video-safe work: PPU register writes separated by less than a
    // few scanlines of idle time.
    private static List<Burst> Bursts(IReadOnlyList<NesPpuWrite> writes)
    {
        var bursts = new List<Burst>();
        Burst? current = null;
        foreach (var write in writes)
        {
            if (current is null || write.Cycle - current.End > 400)
            {
                current = new Burst(write.Cycle) { End = write.Cycle };
                bursts.Add(current);
            }

            current.End = write.Cycle;
            if (write.Register == 0x2007)
            {
                current.DataWrites++;
            }
            else if (write.Register == 0x2004)
            {
                current.OamWrites++;
            }
        }

        return bursts;
    }

    private sealed record Burst(long Start)
    {
        public long End { get; set; }

        public int DataWrites { get; set; }

        public int OamWrites { get; set; }
    }

    private static long Median(IReadOnlyList<long> values)
    {
        if (values.Count == 0)
        {
            return -1;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

    private static string RepositoryDirectory(string relativePath) =>
        Locate(relativePath, Directory.Exists);

    private static string RepositoryFile(string relativePath) =>
        Locate(relativePath, File.Exists);

    private static string Locate(string relativePath, Func<string, bool> exists)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find '{relativePath}'.");
    }
}
