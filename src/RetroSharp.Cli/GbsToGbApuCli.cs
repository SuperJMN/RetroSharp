namespace RetroSharp.Cli;

/// <summary>
/// Owns the complete gbs-to-apu conversion subcommand surface: exporting a Game Boy APU trace
/// from a `.gbs` file (`gbs-to-gbapu`), dumping a trace back to text (`gbapu-dump`), and
/// rejecting other `gbs-to-*` commands as unsupported. <see cref="CliRunner"/> dispatches here
/// before falling back to the regular build pipeline.
/// </summary>
internal static class GbsToGbApuCli
{
    /// <summary>
    /// Attempts to handle <paramref name="args"/> as one of this subcommand's forms. Returns
    /// <see langword="false"/> when <c>args[0]</c> is not one of them, in which case
    /// <paramref name="exitCode"/> must be ignored and the caller should continue with the
    /// regular build pipeline.
    /// </summary>
    internal static bool TryHandle(string[] args, TextWriter stdout, TextWriter stderr, out int exitCode)
    {
        switch (args[0])
        {
            case "gbs-to-gbapu":
                exitCode = ExportGbApuTrace(args[1..], stderr);
                return true;
            case "gbapu-dump":
                exitCode = DumpGbApuTrace(args, stdout, stderr);
                return true;
            default:
                if (args[0].StartsWith("gbs-to-", StringComparison.Ordinal))
                {
                    stderr.WriteLine($"Unknown command '{args[0]}'.");
                    exitCode = 1;
                    return true;
                }

                exitCode = 0;
                return false;
        }
    }

    private static int ExportGbApuTrace(string[] args, TextWriter stderr)
    {
        try
        {
            var exportOptions = GbsToGbApuCommandLineParser.Parse(args);
            var result = RetroSharp.GameBoy.GameBoyGbsToGbApuExporter.Export(exportOptions);
            stderr.WriteLine(
                $"Wrote Game Boy APU trace: {exportOptions.OutputPath} ({result.EventCount} events, {result.DurationCycles / 4194304.0:0.00}s, loop cycle {result.LoopCycle})");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int DumpGbApuTrace(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (args.Length < 2)
            {
                throw new ArgumentException("gbapu-dump requires a trace path: gbapu-dump <file.gbapu|file.gbapu.json>.");
            }

            var dumpPath = args[1];
            var trace = RetroSharp.GameBoy.GameBoyApuTraceBinary.LooksLikeBinary(dumpPath)
                ? RetroSharp.GameBoy.GameBoyApuTraceBinary.Read(dumpPath)
                : RetroSharp.GameBoy.GameBoyApuTraceFile.Read(dumpPath);

            stderr.WriteLine(
                $"; gbapu trace: {trace.Events.Count} events, {trace.DurationCycles / 4194304.0:0.00}s, loopCycle {trace.LoopCycle}, replayHz {trace.Metadata.ReplayHz?.ToString("0.0000") ?? "?"}");
            if (!string.IsNullOrEmpty(trace.Metadata.Title))
            {
                stderr.WriteLine($"; title: {trace.Metadata.Title}");
            }

            var absolute = 0L;
            foreach (var traceEvent in trace.Events)
            {
                absolute += traceEvent.DeltaCycles;
                stdout.WriteLine($"{absolute:X8} ff{traceEvent.Address & 0xFF:x2}={traceEvent.Value:x2}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }
}
