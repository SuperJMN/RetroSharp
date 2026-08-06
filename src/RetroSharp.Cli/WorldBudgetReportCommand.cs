using System.Text.Json;

namespace RetroSharp.Cli;

/// <summary>
/// Handles the `--world-budget-report` flag: validates it is not combined with build-output
/// options, inspects the world without compiling a cartridge, and writes the JSON report to
/// stdout.
/// </summary>
internal static class WorldBudgetReportCommand
{
    internal static int Execute(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (options.SymbolsOutputPath is not null)
            {
                throw new ArgumentException("--world-budget-report cannot be combined with --symbols-out.");
            }
            if (options.OutputPath is not null)
            {
                throw new ArgumentException("--world-budget-report writes JSON to stdout and cannot be combined with --out.");
            }
            if (options.CapacityReport)
            {
                throw new ArgumentException("--world-budget-report inspects a world without building and cannot be combined with --capacity-report.");
            }

            var target = options.Target?.ToLowerInvariant()
                ?? throw new ArgumentException("--world-budget-report requires --target gb or --target nes.");
            var report = RetroSharp.Cli.WorldBudgetReportFactory.Create(target, options.InputPath!);
            stdout.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }
}
