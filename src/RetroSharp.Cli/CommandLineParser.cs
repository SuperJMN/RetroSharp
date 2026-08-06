namespace RetroSharp.Cli;

/// <summary>
/// Parses the top-level RetroSharp CLI arguments (target, output paths, library/plugin lists,
/// and report flags) into a <see cref="CliOptions"/>. Does not handle the gbs-to-gbapu or
/// gbapu-dump subcommands; those are parsed and dispatched by <see cref="GbsToGbApuCli"/>.
/// </summary>
internal static class CommandLineParser
{
    internal static CliOptions Parse(string[] args)
    {
        string? inputPath = null;
        string? outputPath = null;
        string? runtimeAbiOutputPath = null;
        string? symbolsOutputPath = null;
        string? target = null;
        var libraryPaths = new List<string>();
        var plugins = new List<string>();
        var worldBudgetReport = false;
        var capacityReport = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target":
                    if (i + 1 >= args.Length) throw new ArgumentException("--target requires a value.");
                    target = args[++i].ToLowerInvariant();
                    break;
                case "--out":
                case "-o":
                    if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} requires a value.");
                    outputPath = args[++i];
                    break;
                case "--lib-path":
                    if (i + 1 >= args.Length) throw new ArgumentException("--lib-path requires a value.");
                    libraryPaths.Add(args[++i]);
                    break;
                case "--runtime-abi-out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--runtime-abi-out requires a value.");
                    runtimeAbiOutputPath = args[++i];
                    break;
                case "--symbols-out":
                    if (i + 1 >= args.Length) throw new ArgumentException("--symbols-out requires a value.");
                    symbolsOutputPath = args[++i];
                    break;
                case "--sdk-plugin":
                    if (i + 1 >= args.Length) throw new ArgumentException("--sdk-plugin requires a value.");
                    plugins.Add(args[++i]);
                    break;
                case "--world-budget-report":
                    worldBudgetReport = true;
                    break;
                case "--capacity-report":
                    capacityReport = true;
                    break;
                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{args[i]}'.");
                    }

                    inputPath ??= args[i];
                    break;
            }
        }

        return new CliOptions(
            inputPath,
            outputPath,
            runtimeAbiOutputPath,
            symbolsOutputPath,
            target,
            libraryPaths,
            plugins,
            worldBudgetReport,
            capacityReport);
    }
}
