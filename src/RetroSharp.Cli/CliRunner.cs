namespace RetroSharp.Cli;

public static class CliRunner
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 1)
        {
            stderr.WriteLine("No source file has been specified");
            return 1;
        }

        if (GbsToGbApuCli.TryHandle(args, stdout, stderr, out var gbsExitCode))
        {
            return gbsExitCode;
        }

        var options = CommandLineParser.Parse(args);
        if (options.InputPath is null)
        {
            stderr.WriteLine("No source file has been specified");
            return 1;
        }

        if (options.WorldBudgetReport)
        {
            return WorldBudgetReportCommand.Execute(options, stdout, stderr);
        }

        IReadOnlyList<RetroSharpBuildInput> buildInputs;
        try
        {
            buildInputs = ProjectBuildInputResolver.Resolve(options);
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }

        foreach (var buildInput in buildInputs)
        {
            var result = TargetBuildExecutor.Execute(buildInput, stdout, stderr);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }
}
