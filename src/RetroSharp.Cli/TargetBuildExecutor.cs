namespace RetroSharp.Cli;

/// <summary>
/// Compiles a single <see cref="RetroSharpBuildInput"/> for its target (nes or gb), writes the
/// ROM and any requested sidecars (runtime ABI, symbols, capacity report), and reports success or
/// failure the same way <see cref="CliRunner"/> always has: a printed message and an exit code.
/// </summary>
internal static class TargetBuildExecutor
{
    internal static int Execute(RetroSharpBuildInput buildInput, TextWriter stdout, TextWriter stderr)
    {
        if (buildInput.RuntimeAbiOutputPath is not null && buildInput.Target != "nes")
        {
            stderr.WriteLine("--runtime-abi-out is only supported for target nes.");
            return 1;
        }

        if (buildInput.CapacityReport && buildInput.Target != "nes")
        {
            stderr.WriteLine("--capacity-report is only supported for target nes.");
            return 1;
        }

        if (buildInput.Target == "nes")
        {
            return ExecuteNes(buildInput, stdout, stderr);
        }

        if (buildInput.Target is "gb" or "gameboy")
        {
            return ExecuteGameBoy(buildInput, stderr);
        }

        stderr.WriteLine($"Unknown target '{buildInput.Target}'. Supported targets: nes, gb");
        return 1;
    }

    private static int ExecuteNes(RetroSharpBuildInput buildInput, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var sdkLibraryRegistry = SdkRegistryResolver.ResolveLibraries(buildInput.LibraryPaths);
            var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                buildInput.Source,
                buildInput.BaseDirectory,
                sdkLibraryRegistry: sdkLibraryRegistry,
                sdkLibraryImports: buildInput.LibraryImports,
                sdkPluginRegistry: SdkRegistryResolver.ResolvePlugins(buildInput.Plugins));
            var outputPath = buildInput.OutputPath ?? BuildOutputWriter.DefaultOutputPath(buildInput, ".nes");
            BuildOutputWriter.WriteBytes(outputPath, result.Rom);
            stderr.WriteLine($"Wrote NES ROM: {outputPath}");
            if (buildInput.RuntimeAbiOutputPath is not null)
            {
                BuildOutputWriter.WriteText(
                    buildInput.RuntimeAbiOutputPath,
                    RetroSharp.NES.NesRuntimeAbiProjection.Serialize(result));
                stderr.WriteLine($"Wrote NES runtime ABI: {buildInput.RuntimeAbiOutputPath}");
            }
            if (buildInput.SymbolsOutputPath is not null)
            {
                BuildOutputWriter.WriteText(
                    buildInput.SymbolsOutputPath,
                    RetroSharp.NES.NesSymbolFileProjection.Serialize(result));
                stderr.WriteLine($"Wrote NES symbols: {buildInput.SymbolsOutputPath}");
            }
            if (buildInput.CapacityReport)
            {
                var capacity = RetroSharp.NES.NesCapacityReportProjection.Create(result);
                stdout.WriteLine(RetroSharp.NES.NesCapacityReportProjection.Serialize(capacity));
                foreach (var warning in capacity.Warnings)
                {
                    stderr.WriteLine($"warning: {warning.Message}");
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int ExecuteGameBoy(RetroSharpBuildInput buildInput, TextWriter stderr)
    {
        try
        {
            var sdkLibraryRegistry = SdkRegistryResolver.ResolveLibraries(buildInput.LibraryPaths);
            var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
                buildInput.Source,
                buildInput.BaseDirectory,
                sdkLibraryRegistry: sdkLibraryRegistry,
                sdkLibraryImports: buildInput.LibraryImports,
                sdkPluginRegistry: SdkRegistryResolver.ResolvePlugins(buildInput.Plugins));
            var outputPath = buildInput.OutputPath ?? BuildOutputWriter.DefaultOutputPath(buildInput, ".gb");
            BuildOutputWriter.WriteBytes(outputPath, result.Rom);
            stderr.WriteLine($"Wrote Game Boy ROM: {outputPath}");
            if (buildInput.SymbolsOutputPath is not null)
            {
                BuildOutputWriter.WriteText(
                    buildInput.SymbolsOutputPath,
                    RetroSharp.GameBoy.GameBoySymbolFileProjection.Serialize(result));
                stderr.WriteLine($"Wrote Game Boy symbols: {buildInput.SymbolsOutputPath}");
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
