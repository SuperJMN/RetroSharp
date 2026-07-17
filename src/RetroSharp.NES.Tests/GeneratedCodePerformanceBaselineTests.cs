namespace RetroSharp.NES.Tests;

using RetroSharp.Cli;
using RetroSharp.FunctionalAcceptance;
using Xunit;
using Xunit.Abstractions;

public sealed class GeneratedCodePerformanceBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void Exact_cli_roms_reproduce_the_nes_snapshot_twice()
    {
        var result = GeneratedCodePerformanceMatrixRunner.RunTwiceAndVerifySnapshot(
            new NesPerformanceTargetAdapter(
                RepositoryDirectory("validation/generated-code-performance")),
            RepositoryFile("validation/generated-code-performance/baseline.tsv"));

        output.WriteLine(result.Report);
        Assert.Equal(13, result.Rows.Count);
    }

    private sealed class NesPerformanceTargetAdapter(string fixtureRoot) : IGeneratedCodePerformanceTargetAdapter
    {
        public FunctionalTarget Target => FunctionalTarget.Nes;

        public GeneratedCodePerformancePreparedArtifact Build(GeneratedCodePerformanceCase fixture)
        {
            using var materialized = GeneratedCodePerformanceFixtureMaterializer.Materialize(fixtureRoot, fixture);
            var outputPath = Path.Combine(materialized.Directory, "out", $"{fixture.Id}.nes");
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliRunner.Run(
                ["--target", "nes", "--out", outputPath, materialized.ProjectPath],
                stdout,
                stderr);
            if (exitCode != 0 || stdout.ToString().Length != 0)
            {
                throw new InvalidOperationException(
                    $"NES generated-code performance CLI build failed for '{fixture.Id}': {stderr}{stdout}");
            }

            var cliRom = File.ReadAllBytes(outputPath);
            var direct = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                materialized.Source,
                materialized.Directory,
                sdkLibraryImports: ["RetroSharp.Portable2D"]);
            if (!direct.Rom.SequenceEqual(cliRom))
            {
                throw new InvalidOperationException(
                    $"NES generated-code performance CLI bytes differ from the target compiler report bytes for '{fixture.Id}'.");
            }

            return new GeneratedCodePerformancePreparedArtifact(
                new GeneratedCodePerformanceArtifact(
                    Target,
                    SelectedProfile: direct.Report.SelectedProfile,
                    Rom: cliRom,
                    CpuModel: nameof(NesTestCpu),
                    Hardware: new GeneratedCodePerformanceHardwareDeclaration(
                        fixture.PoolCapacity,
                        HardwareSpriteLimit: 64,
                        fixture.PoolCapacity,
                        ScanlineSpriteLimit: 8)),
                new NesPerformanceMachine(cliRom));
        }
    }

    private static string RepositoryDirectory(string relativePath) =>
        Path.GetDirectoryName(RepositoryFile(Path.Combine(relativePath, "fixture.retrosharp.json")))!;

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private sealed class NesPerformanceMachine(byte[] rom) : IGeneratedCodePerformanceMachine
    {
        private readonly NesTestCpu cpu = new(rom);

        public int ExpectedBootResetCount => 1;

        public int PhysicalFrames => cpu.PhysicalFrames;

        public long LogicalWaitCompletions => cpu.VBlankWaitCompletions;

        public long Cycles => cpu.Cycles;

        public int ResetCount => cpu.ResetCount;

        public void AdvancePhysicalFrames(int frames) => cpu.RunFrames(cpu.PhysicalFrames + frames);
    }
}
