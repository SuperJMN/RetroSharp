namespace RetroSharp.NES.Tests;

/// <summary>
/// Shared measurement of what a NES ROM does inside the hardware VBlank window.
/// <para>
/// The MMC3 packed-camera profile performs two independent pieces of work in the same VBlank:
/// the packed background column commit and the retained-OAM publication. Neither is visible to
/// the other, so the only honest observer of the joint cost is the emitted ROM itself. This type
/// runs it and reports how far into VBlank the work reaches and whether any store landed on a
/// rendered scanline.
/// </para>
/// </summary>
internal static class NesVideoSafeObserver
{
    internal const int WarmupFrames = 40;
    internal const int ObservedFrames = 120;

    /// <summary>
    /// Idle exercises only the sprite publication. Holding a direction makes the camera stream,
    /// which adds the packed background commit to the same VBlank; adding the jump button is the
    /// running-jump scene of issue #477, where sprite count peaks while the camera is still
    /// streaming.
    /// </summary>
    internal static readonly IReadOnlyList<string[]> HeldInputs =
    [
        [],
        ["right"],
        ["right", "a"],
        ["right", "b"],
    ];

    internal static VideoSafeObservation Observe(byte[] rom, IReadOnlyCollection<string> held)
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

    /// <summary>
    /// Links a sample project the same way the CLI does, so the measurement is taken from code
    /// this build produces rather than from a tracked artifact that may be stale.
    /// </summary>
    internal static byte[] BuildNesRom(string projectRelativePath)
    {
        var projectPath = RepositoryFile(projectRelativePath);
        var romPath = Path.Combine(AppContext.BaseDirectory, $"video-safe-{Guid.NewGuid():N}.nes");
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var exitCode = RetroSharp.Cli.CliRunner.Run(
                ["--target", "nes", "--out", romPath, projectPath],
                stdout,
                stderr);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Building '{projectRelativePath}' for nes failed: {stdout}{stderr}");
            }

            return File.ReadAllBytes(romPath);
        }
        finally
        {
            File.Delete(romPath);
        }
    }

    // CPU cycles elapsed since hardware VBlank started (scanline 241, dot 0).
    internal static long VBlankCyclesElapsed(NesTestCpu cpu, long cycle) =>
        AbsoluteDot(cpu, cycle) / 3 - 241 * 341 / 3;

    // CPU cycles left in hardware VBlank after the given cycle; negative means the work overran.
    internal static long VBlankCyclesRemaining(NesTestCpu cpu, long cycle) =>
        (261 * 341 - AbsoluteDot(cpu, cycle)) / 3;

    internal static string RepositoryDirectory(string relativePath) =>
        Locate(relativePath, Directory.Exists);

    internal static string RepositoryFile(string relativePath) =>
        Locate(relativePath, File.Exists);

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

    private static long Median(IReadOnlyList<long> values)
    {
        if (values.Count == 0)
        {
            return -1;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        return ordered[ordered.Length / 2];
    }

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

    private sealed record Burst(long Start)
    {
        public long End { get; set; }

        public int DataWrites { get; set; }

        public int OamWrites { get; set; }
    }
}

internal sealed record VideoSafeObservation(
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
