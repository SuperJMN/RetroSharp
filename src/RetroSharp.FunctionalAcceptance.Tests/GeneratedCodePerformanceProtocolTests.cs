namespace RetroSharp.FunctionalAcceptance.Tests;

using System.Text.Json.Nodes;
using Xunit;

public sealed class GeneratedCodePerformanceProtocolTests
{
    [Fact]
    public void Canonical_fixture_materializes_the_complete_discovery_matrix_and_phase_order()
    {
        var fixtureRoot = RepositoryDirectory("validation/generated-code-performance");

        Assert.Equal(13, GeneratedCodePerformanceFixtureCatalog.Cases.Count);
        foreach (var fixture in GeneratedCodePerformanceFixtureCatalog.Cases)
        {
            using var materialized = GeneratedCodePerformanceFixtureMaterializer.Materialize(fixtureRoot, fixture);
            var source = materialized.Source;
            var map = JsonNode.Parse(File.ReadAllText(materialized.MapPath))!.AsObject();
            var objects = map["layers"]!.AsArray()
                .Select(layer => layer!.AsObject())
                .Single(layer => layer["name"]!.GetValue<string>() == "actors")["objects"]!
                .AsArray();

            Assert.True(File.Exists(materialized.ProjectPath));
            Assert.True(File.Exists(materialized.SpriteAssetPath));
            Assert.Equal(fixture.PoolCapacity, ActorPoolCapacity(source));
            Assert.Equal(fixture.ObjectCount, objects.Count);
            AssertPhaseOrder(source, "Actors.SpawnLayer", "enemies.Update", "enemies.TouchTiles", "enemies.LandOnTiles", "enemies.Draw");

            for (var index = 0; index < objects.Count; index++)
            {
                var actor = objects[index]!.AsObject();
                var expectedX = fixture.Workload == GeneratedCodePerformanceWorkload.WideSpawn
                    ? 1000 + (index * 200)
                    : 16 + (index * 16);
                Assert.Equal(index + 1, actor["id"]!.GetValue<int>());
                Assert.Equal("Goomba", actor["type"]!.GetValue<string>());
                Assert.Equal(expectedX, actor["x"]!.GetValue<int>());
                Assert.Equal(8, actor["y"]!.GetValue<int>());
            }
        }
    }

    [Fact]
    public void Snapshot_has_exactly_one_row_per_target_and_fixture()
    {
        var rows = GeneratedCodePerformanceReport.ReadSnapshot(
            RepositoryFile("validation/generated-code-performance/baseline.tsv"));

        Assert.Equal(26, rows.Count);
        Assert.Equal(13, rows.Count(row => row.Target == FunctionalTarget.GameBoy));
        Assert.Equal(13, rows.Count(row => row.Target == FunctionalTarget.Nes));
        Assert.Equal(26, rows.Select(row => (row.Target, row.CaseId)).Distinct().Count());
        Assert.All(rows, row => Assert.IsType<FunctionalTarget>(row.Target));
    }

    [Fact]
    public void Protocol_rejects_a_machine_that_does_not_boot_to_warmup_frame()
    {
        var machine = new FakePerformanceMachine { AdvancePhysicalFramesAction = _ => { } };

        var exception = Assert.Throws<InvalidOperationException>(() => Measure(machine));

        Assert.Equal(
            "Generated-code performance fixture 'wide-spawn-3' on 'gb' failed to boot: expected physical frame 20 after warm-up, observed 0.",
            exception.Message);
    }

    [Fact]
    public void Protocol_rejects_reset_vector_reentry_during_observation()
    {
        var machinePhysicalFrames = 0;
        var machine = new FakePerformanceMachine
        {
            ExpectedBootResetCount = 1,
            AdvancePhysicalFramesAction = frames =>
            {
                machinePhysicalFrames += frames;
            },
        };
        machine.ReadPhysicalFrames = () => machinePhysicalFrames;
        machine.ReadResetCount = () => machinePhysicalFrames > GeneratedCodePerformanceProtocol.WarmUpFrames ? 2 : 1;
        machine.ReadLogicalWaitCompletions = () => machinePhysicalFrames;

        var exception = Assert.Throws<InvalidOperationException>(() => Measure(machine));

        Assert.Equal(
            "Generated-code performance fixture 'wide-spawn-3' on 'gb' re-entered its reset vector during observation frame 1: expected reset count 1, observed 2.",
            exception.Message);
    }

    [Fact]
    public void Protocol_rejects_an_observation_window_without_completed_frame_waits()
    {
        var machinePhysicalFrames = 0;
        var machine = new FakePerformanceMachine
        {
            AdvancePhysicalFramesAction = frames => machinePhysicalFrames += frames,
            ReadPhysicalFrames = () => machinePhysicalFrames,
        };

        var exception = Assert.Throws<InvalidOperationException>(() => Measure(machine));

        Assert.Equal(
            "Generated-code performance fixture 'wide-spawn-3' on 'gb' completed no logical frame waits during 100 observed physical frames.",
            exception.Message);
    }

    [Fact]
    public void Protocol_records_warmup_and_observation_frames_and_serializes_with_literal_lf()
    {
        var physicalFrames = 0;
        var machine = new FakePerformanceMachine
        {
            AdvancePhysicalFramesAction = frames => physicalFrames += frames,
            ReadPhysicalFrames = () => physicalFrames,
            ReadLogicalWaitCompletions = () => physicalFrames,
            ReadCycles = () => physicalFrames * 1_000L,
        };

        var row = Measure(machine);
        var report = GeneratedCodePerformanceReport.Serialize([row]);

        Assert.Equal(20, row.WarmUpPhysicalFrames);
        Assert.Equal(100, row.ObservedPhysicalFrames);
        Assert.DoesNotContain('\r', report);
        Assert.EndsWith("\n", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Matrix_runner_executes_two_independent_complete_runs_and_owns_report_serialization()
    {
        var adapter = new FakePerformanceTargetAdapter(deterministic: true);

        var result = GeneratedCodePerformanceMatrixRunner.RunTwice(adapter);

        Assert.Equal(26, adapter.BuildCalls);
        Assert.Equal(13, result.Rows.Count);
        Assert.Equal(GeneratedCodePerformanceReport.Serialize(result.Rows), result.Report);
    }

    [Fact]
    public void Matrix_runner_rejects_rom_hash_drift_between_independent_runs()
    {
        var adapter = new FakePerformanceTargetAdapter(deterministic: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GeneratedCodePerformanceMatrixRunner.RunTwice(adapter));

        Assert.Equal(
            "Generated-code performance target 'gb' produced different ROM hashes between independent runs for fixture 'wide-spawn-3'.",
            exception.Message);
    }

    private static GeneratedCodePerformanceRow Measure(IGeneratedCodePerformanceMachine machine) =>
        GeneratedCodePerformanceProtocol.Measure(
            GeneratedCodePerformanceFixtureCatalog.Cases[0],
            new GeneratedCodePerformanceArtifact(
                Target: FunctionalTarget.GameBoy,
                SelectedProfile: "test-profile",
                Rom: [0x01, 0x02],
                CpuModel: "FakePerformanceMachine",
                Hardware: new GeneratedCodePerformanceHardwareDeclaration(2, 40, 2, 10)),
            machine);

    private static int ActorPoolCapacity(string source)
    {
        const string prefix = "Actors.Pool(enemies, ";
        var start = source.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = source.IndexOf(')', start);
        return int.Parse(source[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AssertPhaseOrder(string source, params string[] phases)
    {
        var previous = -1;
        foreach (var phase in phases)
        {
            var current = source.IndexOf(phase, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected phase '{phase}' after the previous phase in the canonical source.");
            previous = current;
        }
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var path = RepositoryPath(relativePath);
        return Directory.Exists(path)
            ? path
            : throw new InvalidOperationException($"Could not find repository directory '{relativePath}'.");
    }

    private static string RepositoryFile(string relativePath)
    {
        var path = RepositoryPath(relativePath);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private static string RepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(relativePath);
    }

    private sealed class FakePerformanceMachine : IGeneratedCodePerformanceMachine
    {
        public int ExpectedBootResetCount { get; init; }

        public Func<int> ReadPhysicalFrames { get; set; } = () => 0;

        public Func<long> ReadLogicalWaitCompletions { get; set; } = () => 0;

        public Func<long> ReadCycles { get; set; } = () => 0;

        public Func<int> ReadResetCount { get; set; } = () => 0;

        public Action<int> AdvancePhysicalFramesAction { get; init; } = _ => { };

        public int PhysicalFrames => ReadPhysicalFrames();

        public long LogicalWaitCompletions => ReadLogicalWaitCompletions();

        public long Cycles => ReadCycles();

        public int ResetCount => ReadResetCount();

        public void AdvancePhysicalFrames(int frames) => AdvancePhysicalFramesAction(frames);
    }

    private sealed class FakePerformanceTargetAdapter(bool deterministic) : IGeneratedCodePerformanceTargetAdapter
    {
        public int BuildCalls { get; private set; }

        public FunctionalTarget Target => FunctionalTarget.GameBoy;

        public GeneratedCodePerformancePreparedArtifact Build(GeneratedCodePerformanceCase fixture)
        {
            BuildCalls++;
            var physicalFrames = 0;
            var machine = new FakePerformanceMachine
            {
                AdvancePhysicalFramesAction = frames => physicalFrames += frames,
                ReadPhysicalFrames = () => physicalFrames,
                ReadLogicalWaitCompletions = () => physicalFrames,
                ReadCycles = () => physicalFrames * 1_000L,
            };
            var runMarker = deterministic ? 0 : BuildCalls;
            return new GeneratedCodePerformancePreparedArtifact(
                new GeneratedCodePerformanceArtifact(
                    Target,
                    SelectedProfile: "test-profile",
                    Rom: [(byte)fixture.Scale, (byte)runMarker],
                    CpuModel: nameof(FakePerformanceMachine),
                    Hardware: new GeneratedCodePerformanceHardwareDeclaration(
                        fixture.PoolCapacity,
                        HardwareSpriteLimit: 40,
                        fixture.PoolCapacity,
                        ScanlineSpriteLimit: 10)),
                machine);
        }
    }
}
