namespace RetroSharp.FunctionalAcceptance;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

internal enum GeneratedCodePerformanceWorkload
{
    WideSpawn,
    ActivePool,
}

internal sealed record GeneratedCodePerformanceCase(
    string Id,
    GeneratedCodePerformanceWorkload Workload,
    int Scale,
    int PoolCapacity,
    int ObjectCount);

internal static class GeneratedCodePerformanceFixtureCatalog
{
    public static IReadOnlyList<GeneratedCodePerformanceCase> Cases { get; } =
    [
        .. new[] { 3, 8, 16, 24, 32, 48, 64, 96, 128 }
            .Select(count => new GeneratedCodePerformanceCase(
                $"wide-spawn-{count}",
                GeneratedCodePerformanceWorkload.WideSpawn,
                count,
                PoolCapacity: 2,
                ObjectCount: count)),
        .. new[] { 1, 2, 4, 8 }
            .Select(capacity => new GeneratedCodePerformanceCase(
                $"active-pool-{capacity}",
                GeneratedCodePerformanceWorkload.ActivePool,
                capacity,
                PoolCapacity: capacity,
                ObjectCount: capacity)),
    ];
}

internal sealed class GeneratedCodePerformanceMaterializedFixture : IDisposable
{
    private bool disposed;

    internal GeneratedCodePerformanceMaterializedFixture(
        string directory,
        string projectPath,
        string source,
        string mapPath,
        string spriteAssetPath)
    {
        Directory = directory;
        ProjectPath = projectPath;
        Source = source;
        MapPath = mapPath;
        SpriteAssetPath = spriteAssetPath;
    }

    public string Directory { get; }

    public string ProjectPath { get; }

    public string Source { get; }

    public string MapPath { get; }

    public string SpriteAssetPath { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

internal static class GeneratedCodePerformanceFixtureMaterializer
{
    private const string PoolDeclaration = "Actors.Pool(enemies, 2);";

    public static GeneratedCodePerformanceMaterializedFixture Materialize(
        string fixtureRoot,
        GeneratedCodePerformanceCase fixture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureRoot);
        ArgumentNullException.ThrowIfNull(fixture);

        var sourceTemplatePath = RequiredFile(fixtureRoot, "src", "main.rs");
        var projectTemplatePath = RequiredFile(fixtureRoot, "fixture.retrosharp.json");
        var mapTemplatePath = RequiredFile(fixtureRoot, "assets", "actors.tmj");
        var spriteTemplatePath = RequiredFile(fixtureRoot, "assets", "actor.json");
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"retrosharp-generated-performance-{fixture.Id}-{Guid.NewGuid():N}");

        try
        {
            var sourceDirectory = Path.Combine(directory, "src");
            var assetDirectory = Path.Combine(directory, "assets");
            System.IO.Directory.CreateDirectory(sourceDirectory);
            System.IO.Directory.CreateDirectory(assetDirectory);

            var sourceTemplate = File.ReadAllText(sourceTemplatePath);
            var source = ReplaceExactlyOnce(
                sourceTemplate,
                PoolDeclaration,
                $"Actors.Pool(enemies, {fixture.PoolCapacity.ToString(CultureInfo.InvariantCulture)});");
            var sourcePath = Path.Combine(sourceDirectory, "main.rs");
            File.WriteAllText(sourcePath, source);

            var map = JsonNode.Parse(File.ReadAllText(mapTemplatePath))?.AsObject()
                ?? throw new InvalidOperationException($"Generated-code performance map '{mapTemplatePath}' is empty.");
            var actorLayer = map["layers"]?.AsArray()
                .Select(layer => layer?.AsObject())
                .SingleOrDefault(layer => layer?["name"]?.GetValue<string>() == "actors")
                ?? throw new InvalidOperationException($"Generated-code performance map '{mapTemplatePath}' has no 'actors' object layer.");
            var objects = new JsonArray();
            for (var index = 0; index < fixture.ObjectCount; index++)
            {
                var x = fixture.Workload == GeneratedCodePerformanceWorkload.WideSpawn
                    ? 1000 + (index * 200)
                    : 16 + (index * 16);
                objects.Add(new JsonObject
                {
                    ["id"] = index + 1,
                    ["type"] = "Goomba",
                    ["x"] = x,
                    ["y"] = 8,
                });
            }

            actorLayer["objects"] = objects;
            var mapPath = Path.Combine(assetDirectory, "actors.tmj");
            File.WriteAllText(mapPath, map.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

            var projectPath = Path.Combine(directory, "fixture.retrosharp.json");
            var spriteAssetPath = Path.Combine(assetDirectory, "actor.json");
            File.Copy(projectTemplatePath, projectPath);
            File.Copy(spriteTemplatePath, spriteAssetPath);

            return new GeneratedCodePerformanceMaterializedFixture(
                directory,
                projectPath,
                source,
                mapPath,
                spriteAssetPath);
        }
        catch
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }

            throw;
        }
    }

    private static string RequiredFile(string root, params string[] parts)
    {
        var path = parts.Aggregate(root, Path.Combine);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"Generated-code performance fixture file '{path}' was not found.");
    }

    private static string ReplaceExactlyOnce(string source, string oldValue, string newValue)
    {
        var first = source.IndexOf(oldValue, StringComparison.Ordinal);
        var last = source.LastIndexOf(oldValue, StringComparison.Ordinal);
        if (first < 0 || first != last)
        {
            throw new InvalidOperationException(
                $"Generated-code performance source must contain exactly one '{oldValue}' declaration.");
        }

        return string.Concat(source.AsSpan(0, first), newValue, source.AsSpan(first + oldValue.Length));
    }
}

internal interface IGeneratedCodePerformanceMachine
{
    int ExpectedBootResetCount { get; }

    int PhysicalFrames { get; }

    long LogicalWaitCompletions { get; }

    long Cycles { get; }

    int ResetCount { get; }

    void AdvancePhysicalFrames(int frames);
}

internal sealed record GeneratedCodePerformanceHardwareDeclaration(
    int DeclaredHardwareSprites,
    int HardwareSpriteLimit,
    int DeclaredScanlineSprites,
    int ScanlineSpriteLimit);

internal sealed record GeneratedCodePerformanceArtifact(
    FunctionalTarget Target,
    string SelectedProfile,
    byte[] Rom,
    string CpuModel,
    GeneratedCodePerformanceHardwareDeclaration Hardware);

internal sealed record GeneratedCodePerformancePreparedArtifact(
    GeneratedCodePerformanceArtifact Artifact,
    IGeneratedCodePerformanceMachine Machine);

internal interface IGeneratedCodePerformanceTargetAdapter
{
    FunctionalTarget Target { get; }

    GeneratedCodePerformancePreparedArtifact Build(GeneratedCodePerformanceCase fixture);
}

internal sealed record GeneratedCodePerformanceMatrixResult(
    FunctionalTarget Target,
    IReadOnlyList<GeneratedCodePerformanceRow> Rows,
    string Report);

internal sealed record GeneratedCodePerformanceRow(
    FunctionalTarget Target,
    string CaseId,
    GeneratedCodePerformanceWorkload Workload,
    int Scale,
    int PoolCapacity,
    int ObjectCount,
    int WarmUpPhysicalFrames,
    int ObservedPhysicalFrames,
    long LogicalWaitDelta,
    int LongestMiss,
    long ObservationCycles,
    string CpuModel,
    string SelectedProfile,
    int RomBytes,
    string RomSha256,
    string BootStatus,
    int BootResetCount,
    int ResetReentries,
    string WaitStatus,
    int DeclaredHardwareSprites,
    int HardwareSpriteLimit,
    int DeclaredScanlineSprites,
    int ScanlineSpriteLimit);

internal static class GeneratedCodePerformanceProtocol
{
    public const int WarmUpFrames = 20;
    public const int ObservationFrames = 100;

    public static GeneratedCodePerformanceRow Measure(
        GeneratedCodePerformanceCase fixture,
        GeneratedCodePerformanceArtifact artifact,
        IGeneratedCodePerformanceMachine machine)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(machine);
        ValidateHardware(fixture, artifact);

        var initialPhysicalFrames = machine.PhysicalFrames;
        machine.AdvancePhysicalFrames(WarmUpFrames);
        var expectedWarmUpFrame = checked(initialPhysicalFrames + WarmUpFrames);
        if (machine.PhysicalFrames != expectedWarmUpFrame)
        {
            throw new InvalidOperationException(
                $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' failed to boot: expected physical frame {expectedWarmUpFrame} after warm-up, observed {machine.PhysicalFrames}.");
        }

        if (machine.ResetCount != machine.ExpectedBootResetCount)
        {
            throw new InvalidOperationException(
                $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' failed to boot cleanly: expected reset count {machine.ExpectedBootResetCount}, observed {machine.ResetCount}.");
        }

        var waitsBefore = machine.LogicalWaitCompletions;
        var previousWaits = waitsBefore;
        var cyclesBefore = machine.Cycles;
        var longestMiss = 0;
        var currentMiss = 0;
        for (var frame = 1; frame <= ObservationFrames; frame++)
        {
            machine.AdvancePhysicalFrames(1);
            var expectedPhysicalFrame = checked(expectedWarmUpFrame + frame);
            if (machine.PhysicalFrames != expectedPhysicalFrame)
            {
                throw new InvalidOperationException(
                    $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' failed to advance observation frame {frame}: expected physical frame {expectedPhysicalFrame}, observed {machine.PhysicalFrames}.");
            }

            if (machine.ResetCount != machine.ExpectedBootResetCount)
            {
                throw new InvalidOperationException(
                    $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' re-entered its reset vector during observation frame {frame}: expected reset count {machine.ExpectedBootResetCount}, observed {machine.ResetCount}.");
            }

            var currentWaits = machine.LogicalWaitCompletions;
            if (currentWaits < previousWaits)
            {
                throw new InvalidOperationException(
                    $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' reported a decreasing logical wait count during observation frame {frame}: {previousWaits} to {currentWaits}.");
            }

            if (currentWaits == previousWaits)
            {
                currentMiss++;
                longestMiss = Math.Max(longestMiss, currentMiss);
            }
            else
            {
                currentMiss = 0;
            }

            previousWaits = currentWaits;
        }

        var logicalWaitDelta = checked(previousWaits - waitsBefore);
        if (logicalWaitDelta == 0)
        {
            throw new InvalidOperationException(
                $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' completed no logical frame waits during {ObservationFrames} observed physical frames.");
        }

        var observationCycles = checked(machine.Cycles - cyclesBefore);
        if (observationCycles <= 0)
        {
            throw new InvalidOperationException(
                $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' reported non-positive observation-window cycles: {observationCycles}.");
        }

        return new GeneratedCodePerformanceRow(
            artifact.Target,
            fixture.Id,
            fixture.Workload,
            fixture.Scale,
            fixture.PoolCapacity,
            fixture.ObjectCount,
            WarmUpFrames,
            ObservationFrames,
            logicalWaitDelta,
            longestMiss,
            observationCycles,
            artifact.CpuModel,
            artifact.SelectedProfile,
            artifact.Rom.Length,
            Convert.ToHexStringLower(SHA256.HashData(artifact.Rom)),
            BootStatus: "ok",
            BootResetCount: machine.ExpectedBootResetCount,
            ResetReentries: 0,
            WaitStatus: "ok",
            artifact.Hardware.DeclaredHardwareSprites,
            artifact.Hardware.HardwareSpriteLimit,
            artifact.Hardware.DeclaredScanlineSprites,
            artifact.Hardware.ScanlineSpriteLimit);
    }

    private static void ValidateHardware(
        GeneratedCodePerformanceCase fixture,
        GeneratedCodePerformanceArtifact artifact)
    {
        if (artifact.Hardware.DeclaredHardwareSprites > artifact.Hardware.HardwareSpriteLimit)
        {
            throw new InvalidOperationException(
                $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' declares {artifact.Hardware.DeclaredHardwareSprites} hardware sprites, exceeding its {artifact.Hardware.HardwareSpriteLimit} sprite limit.");
        }

        if (artifact.Hardware.DeclaredScanlineSprites > artifact.Hardware.ScanlineSpriteLimit)
        {
            throw new InvalidOperationException(
                $"Generated-code performance fixture '{fixture.Id}' on '{artifact.Target.StableId()}' declares {artifact.Hardware.DeclaredScanlineSprites} sprites on one scanline, exceeding its {artifact.Hardware.ScanlineSpriteLimit} scanline limit.");
        }
    }
}

internal static class GeneratedCodePerformanceMatrixRunner
{
    public static GeneratedCodePerformanceMatrixResult RunTwice(
        IGeneratedCodePerformanceTargetAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var first = RunOnce(adapter);
        var second = RunOnce(adapter);
        for (var index = 0; index < first.Count; index++)
        {
            var firstRow = first[index];
            var secondRow = second[index];
            if (!string.Equals(firstRow.RomSha256, secondRow.RomSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated-code performance target '{adapter.Target.StableId()}' produced different ROM hashes between independent runs for fixture '{firstRow.CaseId}'.");
            }
        }

        var firstReport = GeneratedCodePerformanceReport.Serialize(first);
        var secondReport = GeneratedCodePerformanceReport.Serialize(second);
        if (!string.Equals(firstReport, secondReport, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generated-code performance target '{adapter.Target.StableId()}' produced different reports between independent runs.");
        }

        return new GeneratedCodePerformanceMatrixResult(adapter.Target, first, firstReport);
    }

    public static GeneratedCodePerformanceMatrixResult RunTwiceAndVerifySnapshot(
        IGeneratedCodePerformanceTargetAdapter adapter,
        string snapshotPath)
    {
        var result = RunTwice(adapter);
        var expected = GeneratedCodePerformanceReport.ReadSnapshot(snapshotPath)
            .Where(row => row.Target == result.Target)
            .ToArray();
        if (!expected.SequenceEqual(result.Rows))
        {
            throw new InvalidOperationException(
                $"Generated-code performance target '{result.Target.StableId()}' differs from the intentional characterization snapshot. Review and refresh these rows deliberately:\n{result.Report}");
        }

        return result;
    }

    private static IReadOnlyList<GeneratedCodePerformanceRow> RunOnce(
        IGeneratedCodePerformanceTargetAdapter adapter)
    {
        var rows = new List<GeneratedCodePerformanceRow>(GeneratedCodePerformanceFixtureCatalog.Cases.Count);
        foreach (var fixture in GeneratedCodePerformanceFixtureCatalog.Cases)
        {
            var prepared = adapter.Build(fixture);
            if (prepared.Artifact.Target != adapter.Target)
            {
                throw new InvalidOperationException(
                    $"Generated-code performance adapter '{adapter.Target.StableId()}' built fixture '{fixture.Id}' for target '{prepared.Artifact.Target.StableId()}'.");
            }

            rows.Add(GeneratedCodePerformanceProtocol.Measure(
                fixture,
                prepared.Artifact,
                prepared.Machine));
        }

        return rows;
    }
}

internal static class GeneratedCodePerformanceReport
{
    private const int ColumnCount = 23;

    public static string Serialize(IEnumerable<GeneratedCodePerformanceRow> rows) =>
        string.Join('\n', rows.Select(SerializeRow)) + "\n";

    public static IReadOnlyList<GeneratedCodePerformanceRow> ReadSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(ParseRow)
            .ToArray();
    }

    private static string SerializeRow(GeneratedCodePerformanceRow row) => string.Join(
        '\t',
        row.Target.StableId(),
        row.CaseId,
        Workload(row.Workload),
        Number(row.Scale),
        Number(row.PoolCapacity),
        Number(row.ObjectCount),
        Number(row.WarmUpPhysicalFrames),
        Number(row.ObservedPhysicalFrames),
        Number(row.LogicalWaitDelta),
        Number(row.LongestMiss),
        Number(row.ObservationCycles),
        row.CpuModel,
        row.SelectedProfile,
        Number(row.RomBytes),
        row.RomSha256,
        row.BootStatus,
        Number(row.BootResetCount),
        Number(row.ResetReentries),
        row.WaitStatus,
        Number(row.DeclaredHardwareSprites),
        Number(row.HardwareSpriteLimit),
        Number(row.DeclaredScanlineSprites),
        Number(row.ScanlineSpriteLimit));

    private static GeneratedCodePerformanceRow ParseRow(string line)
    {
        var columns = line.Split('\t');
        if (columns.Length != ColumnCount)
        {
            throw new InvalidOperationException(
                $"Generated-code performance snapshot row has {columns.Length} columns instead of {ColumnCount}: '{line}'.");
        }

        return new GeneratedCodePerformanceRow(
            columns[0] switch
            {
                "gb" => FunctionalTarget.GameBoy,
                "nes" => FunctionalTarget.Nes,
                _ => throw new InvalidOperationException($"Unknown generated-code performance target '{columns[0]}'."),
            },
            columns[1],
            columns[2] switch
            {
                "wide-spawn" => GeneratedCodePerformanceWorkload.WideSpawn,
                "active-pool" => GeneratedCodePerformanceWorkload.ActivePool,
                _ => throw new InvalidOperationException($"Unknown generated-code performance workload '{columns[2]}'."),
            },
            ParseNumber<int>(columns[3]),
            ParseNumber<int>(columns[4]),
            ParseNumber<int>(columns[5]),
            ParseNumber<int>(columns[6]),
            ParseNumber<int>(columns[7]),
            ParseNumber<long>(columns[8]),
            ParseNumber<int>(columns[9]),
            ParseNumber<long>(columns[10]),
            columns[11],
            columns[12],
            ParseNumber<int>(columns[13]),
            columns[14],
            columns[15],
            ParseNumber<int>(columns[16]),
            ParseNumber<int>(columns[17]),
            columns[18],
            ParseNumber<int>(columns[19]),
            ParseNumber<int>(columns[20]),
            ParseNumber<int>(columns[21]),
            ParseNumber<int>(columns[22]));
    }

    private static string Workload(GeneratedCodePerformanceWorkload workload) => workload switch
    {
        GeneratedCodePerformanceWorkload.WideSpawn => "wide-spawn",
        GeneratedCodePerformanceWorkload.ActivePool => "active-pool",
        _ => throw new ArgumentOutOfRangeException(nameof(workload), workload, null),
    };

    private static string Number<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    private static T ParseNumber<T>(string value) where T : IParsable<T> =>
        T.Parse(value, CultureInfo.InvariantCulture);
}
