namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Cli;
using RetroSharp.FunctionalAcceptance;
using Xunit;
using Xunit.Abstractions;

public sealed class GeneratedCodePerformanceBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void Exact_cli_roms_reproduce_the_game_boy_snapshot_twice()
    {
        var result = GeneratedCodePerformanceMatrixRunner.RunTwiceAndVerifySnapshot(
            new GameBoyPerformanceTargetAdapter(
                RepositoryDirectory("validation/generated-code-performance")),
            RepositoryFile("validation/generated-code-performance/baseline.tsv"));

        output.WriteLine(result.Report);
        Assert.Equal(13, result.Rows.Count);
    }

    private sealed class GameBoyPerformanceTargetAdapter(string fixtureRoot) : IGeneratedCodePerformanceTargetAdapter
    {
        public FunctionalTarget Target => FunctionalTarget.GameBoy;

        public GeneratedCodePerformancePreparedArtifact Build(GeneratedCodePerformanceCase fixture)
        {
            using var materialized = GeneratedCodePerformanceFixtureMaterializer.Materialize(fixtureRoot, fixture);
            var outputPath = Path.Combine(materialized.Directory, "out", $"{fixture.Id}.gb");
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliRunner.Run(
                ["--target", "gb", "--out", outputPath, materialized.ProjectPath],
                stdout,
                stderr);
            if (exitCode != 0 || stdout.ToString().Length != 0)
            {
                throw new InvalidOperationException(
                    $"Game Boy generated-code performance CLI build failed for '{fixture.Id}': {stderr}{stdout}");
            }

            var cliRom = File.ReadAllBytes(outputPath);
            var direct = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
                materialized.Source,
                materialized.Directory,
                sdkLibraryImports: ["RetroSharp.Portable2D"]);
            if (!direct.Rom.SequenceEqual(cliRom))
            {
                throw new InvalidOperationException(
                    $"Game Boy generated-code performance CLI bytes differ from the target compiler report bytes for '{fixture.Id}'.");
            }

            return new GeneratedCodePerformancePreparedArtifact(
                new GeneratedCodePerformanceArtifact(
                    Target,
                    SelectedProfile: direct.Report.SelectedProfile,
                    Rom: cliRom,
                    CpuModel: nameof(GameBoyTestCpu),
                    Hardware: new GeneratedCodePerformanceHardwareDeclaration(
                        fixture.PoolCapacity,
                        HardwareSpriteLimit: 40,
                        fixture.PoolCapacity,
                        ScanlineSpriteLimit: 10)),
                new GameBoyPerformanceMachine(cliRom));
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

    private sealed class GameBoyPerformanceMachine : IGeneratedCodePerformanceMachine
    {
        private readonly GameBoyTestCpu cpu;
        private int physicalFrames;

        public GameBoyPerformanceMachine(byte[] rom)
        {
            cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        }

        public int ExpectedBootResetCount => 0;

        public int PhysicalFrames => physicalFrames;

        public long LogicalWaitCompletions => cpu.VBlankWaitCompletions;

        public long Cycles => cpu.Cycles;

        public int ResetCount => cpu.ResetCount;

        public void AdvancePhysicalFrames(int frames)
        {
            var targetPhysicalFrame = checked(physicalFrames + frames);
            cpu.RunFrames(targetPhysicalFrame);
            physicalFrames = targetPhysicalFrame;
        }
    }
}
