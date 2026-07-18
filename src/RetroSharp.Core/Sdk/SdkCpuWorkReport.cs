namespace RetroSharp.Core.Sdk;

public static class SdkCpuWorkContributorIds
{
    public const string FrameBoundaryActive = "frame.boundary.active";
    public const string InputPoll = "input.poll";
    public const string CameraPosition = "camera.position";
    public const string CameraApply = "camera.apply";
    public const string WorldPrepare = "world.prepare";
    public const string WorldCommit = "world.commit";
    public const string CollisionAabb = "collision.aabb";
    public const string CollisionHitTop = "collision.hit-top";
    public const string AudioUpdate = "audio.update";
    public const string SpriteDraw = "sprite.draw";
    public const string SpritePublish = "sprite.publish";
    public const string SpritePublishTransfer = "sprite.publish.transfer";
    public const string ActorSpawnRecycle = "actor.spawn.recycle";
    public const string ActorSpawnScan = "actor.spawn.scan";
    public const string ActorSpawnRecordRead = "actor.spawn.record-read";
    public const string ActorSpawnSlotSearch = "actor.spawn.slot-search";
    public const string ActorPhaseUpdate = "actor.phase.update";
    public const string ActorPhaseTouchTiles = "actor.phase.touch-tiles";
    public const string ActorPhaseLandOnTiles = "actor.phase.land-on-tiles";
    public const string ActorPhaseTouchPlayer = "actor.phase.touch-player";
    public const string ActorPhaseDraw = "actor.phase.draw";
    public const string TargetStructArrayAddress = "target.struct-array-address";
    public const string UserDynamicLoop = "user.dynamic-loop";
    public const string RuntimeUncalibratedState = "runtime.uncalibrated-state";
}

public static class SdkCpuWorkContributorCategories
{
    public const string Generated = "generated";
    public const string SdkRuntime = "sdk-runtime";
    public const string TargetRuntime = "target-runtime";
    public const string User = "user";
}

public static class SdkCpuWorkStatuses
{
    public const string Fits = "fits";
    public const string Crosses = "crosses";
    public const string Exceeds = "exceeds";
    public const string Incomplete = "incomplete";
}

public static class SdkCpuWorkWindowIds
{
    public const string Frame = "frame";
    public const string VideoSafe = "video-safe";
}

public sealed record SdkCpuWorkContributor(
    string Id,
    string Category,
    string Basis,
    int Count,
    long UnitLower,
    long? UnitUpper,
    long TotalLower,
    long? TotalUpper,
    string Calibration,
    string? DetailOf = null)
{
    public static SdkCpuWorkContributor Create(
        string id,
        string category,
        string basis,
        int count,
        long unitLower,
        long? unitUpper,
        string calibration,
        string? detailOf = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(basis);
        ArgumentException.ThrowIfNullOrWhiteSpace(calibration);
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "CPU-work contributor count must be non-negative.");
        }

        if (unitLower < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitLower), unitLower, "CPU-work contributor lower unit cost must be non-negative.");
        }

        if (unitUpper is { } upper && upper < unitLower)
        {
            throw new ArgumentOutOfRangeException(nameof(unitUpper), unitUpper, "CPU-work contributor upper unit cost must be greater than or equal to the lower unit cost.");
        }

        return new SdkCpuWorkContributor(
            id,
            category,
            basis,
            count,
            unitLower,
            unitUpper,
            checked(unitLower * count),
            unitUpper is { } finiteUpper ? checked(finiteUpper * count) : null,
            calibration,
            detailOf);
    }
}

public sealed record SdkCpuWorkUnknown(string Id, string Reason);

public sealed record SdkCpuWorkWindowReport(
    string Id,
    long Capacity,
    long KnownLower,
    long? KnownUpper,
    string Status,
    IReadOnlyList<SdkCpuWorkContributor> Contributors,
    IReadOnlyList<SdkCpuWorkUnknown> Unknowns)
{
    public static SdkCpuWorkWindowReport Create(
        string id,
        long capacity,
        IEnumerable<SdkCpuWorkContributor> contributors,
        IEnumerable<SdkCpuWorkUnknown> unknowns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "CPU-work window capacity must be non-negative.");
        }

        var orderedContributors = contributors.ToArray();
        var orderedUnknowns = unknowns.ToArray();
        var (knownLower, knownUpper, status) = SdkCpuWorkRangeComposition.Compose(
            capacity,
            orderedContributors,
            orderedUnknowns);

        return new SdkCpuWorkWindowReport(
            id,
            capacity,
            knownLower,
            knownUpper,
            status,
            orderedContributors,
            orderedUnknowns);
    }
}

public sealed record SdkCpuWorkReport(
    string Target,
    string Profile,
    string Unit,
    long FrameWindow,
    long KnownLower,
    long? KnownUpper,
    string Status,
    IReadOnlyList<SdkCpuWorkContributor> Contributors,
    IReadOnlyList<SdkCpuWorkUnknown> Unknowns)
{
    public IReadOnlyList<SdkCpuWorkWindowReport> Windows { get; init; } = [];

    public static SdkCpuWorkReport Create(
        string target,
        string profile,
        string unit,
        long frameWindow,
        IEnumerable<SdkCpuWorkContributor> contributors,
        IEnumerable<SdkCpuWorkUnknown> unknowns,
        IEnumerable<SdkCpuWorkWindowReport>? windows = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        if (frameWindow < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWindow), frameWindow, "CPU-work frame window must be non-negative.");
        }

        var orderedContributors = contributors.ToArray();
        var orderedUnknowns = unknowns.ToArray();

        var (knownLower, knownUpper, status) = SdkCpuWorkRangeComposition.Compose(
            frameWindow,
            orderedContributors,
            orderedUnknowns);

        return new SdkCpuWorkReport(
            target,
            profile,
            unit,
            frameWindow,
            knownLower,
            knownUpper,
            status,
            orderedContributors,
            orderedUnknowns)
        {
            Windows = windows?.ToArray() ?? [],
        };
    }
}

internal static class SdkCpuWorkRangeComposition
{
    internal static (long KnownLower, long? KnownUpper, string Status) Compose(
        long capacity,
        IReadOnlyCollection<SdkCpuWorkContributor> contributors,
        IReadOnlyCollection<SdkCpuWorkUnknown> unknowns)
    {
        var knownLower = SumLower(contributors);
        var knownUpper = SumUpper(contributors);
        return (knownLower, knownUpper, Classify(capacity, knownLower, knownUpper, unknowns));
    }

    private static long SumLower(IEnumerable<SdkCpuWorkContributor> contributors)
    {
        var result = 0L;
        foreach (var contributor in contributors)
        {
            result = checked(result + contributor.TotalLower);
        }

        return result;
    }

    private static long? SumUpper(IEnumerable<SdkCpuWorkContributor> contributors)
    {
        var result = 0L;
        foreach (var contributor in contributors)
        {
            if (contributor.TotalUpper is not { } upper)
            {
                return null;
            }

            result = checked(result + upper);
        }

        return result;
    }

    private static string Classify(
        long capacity,
        long knownLower,
        long? knownUpper,
        IReadOnlyCollection<SdkCpuWorkUnknown> unknowns)
    {
        if (knownLower > capacity)
        {
            return SdkCpuWorkStatuses.Exceeds;
        }

        if (knownUpper is { } finiteUpper)
        {
            if (finiteUpper > capacity)
            {
                return SdkCpuWorkStatuses.Crosses;
            }

            return unknowns.Count == 0 ? SdkCpuWorkStatuses.Fits : SdkCpuWorkStatuses.Incomplete;
        }

        return SdkCpuWorkStatuses.Incomplete;
    }
}

public static class SdkCpuWorkReportFactory
{
    private static readonly SdkCpuWorkUnknown[] InitialUnknownCoverage =
    [
        Unknown(SdkCpuWorkContributorIds.FrameBoundaryActive, "Frame-boundary active setup, handler, commit, and return work is not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.InputPoll, "Target input snapshot and held/edge-state update work is not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.CameraPosition, "Camera request and logical-position work is not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.CameraApply, "Camera application work is not yet calibrated independently from world phases."),
        Unknown(SdkCpuWorkContributorIds.WorldPrepare, "Selected raw or packed edge preparation paths are not yet calibrated for this profile."),
        Unknown(SdkCpuWorkContributorIds.WorldCommit, "Selected visible-edge commit paths are not yet calibrated for this profile."),
        Unknown(SdkCpuWorkContributorIds.CollisionAabb, "Camera-relative AABB query paths are not yet calibrated for this profile."),
        Unknown(SdkCpuWorkContributorIds.CollisionHitTop, "Camera-relative hit-top query paths are not yet calibrated for this profile."),
        Unknown(SdkCpuWorkContributorIds.AudioUpdate, "Selected audio service paths do not yet have an accepted finite descriptor."),
        Unknown(SdkCpuWorkContributorIds.SpriteDraw, "Logical/metasprite draw preparation work is not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.SpritePublish, "The complete retained sprite publication boundary is not yet calibrated; only the transfer detail is numeric."),
        Unknown(SdkCpuWorkContributorIds.ActorSpawnRecycle, "Generated recycle traversal branch paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.ActorSpawnScan, "Generated spawn candidate traversal branch paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.ActorSpawnRecordRead, "Generated spawn record-read paths are not yet calibrated for this target/profile."),
        Unknown(SdkCpuWorkContributorIds.ActorSpawnSlotSearch, "Generated fixed-pool slot-search branch paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.ActorPhaseUpdate, "Generated update traversal paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.ActorPhaseTouchTiles, "Generated tile-touch traversal and reachable query paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.ActorPhaseLandOnTiles, "Generated landing traversal and reachable query paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.ActorPhaseTouchPlayer, "Generated player-contact traversal paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.ActorPhaseDraw, "Generated draw traversal paths are not yet calibrated."),
        Unknown(SdkCpuWorkContributorIds.TargetStructArrayAddress, "Target-owned struct-array address details must be composed below owning phase descriptors before charging."),
        Unknown(SdkCpuWorkContributorIds.UserDynamicLoop, "Arbitrary user loops are outside the compiler-known finite CPU-work subtotal."),
        Unknown(SdkCpuWorkContributorIds.RuntimeUncalibratedState, "Compiler/runtime-owned state-dependent work without accepted finite descriptors remains explicitly unknown."),
    ];

    public static SdkCpuWorkReport ForGameBoy(
        string profile,
        IEnumerable<Sdk2DOperation>? operations = null) =>
        CreateTargetReport(
            target: "gb",
            profile,
            unit: "t-cycles",
            frameWindow: 70_224,
            RetainedOamContributors(
                operations,
                unitLower: 640,
                unitUpper: 640,
                calibration: "GameBoyTestCpu.SpriteDma/v1"));

    public static SdkCpuWorkReport ForNes(
        string profile,
        IEnumerable<Sdk2DOperation>? operations = null) =>
        CreateTargetReport(
            target: "nes",
            profile,
            unit: "cpu-cycles",
            frameWindow: 29_780,
            RetainedOamContributors(
                operations,
                unitLower: 513,
                unitUpper: 514,
                calibration: "NesTestCpu.SpriteDma/v1"));

    private static SdkCpuWorkReport CreateTargetReport(
        string target,
        string profile,
        string unit,
        long frameWindow,
        IReadOnlyList<SdkCpuWorkContributor> contributors)
    {
        var frame = SdkCpuWorkWindowReport.Create(
            SdkCpuWorkWindowIds.Frame,
            frameWindow,
            contributors,
            InitialUnknownCoverage);

        return SdkCpuWorkReport.Create(
            target,
            profile,
            unit,
            frameWindow,
            contributors,
            InitialUnknownCoverage,
            [frame]);
    }

    private static IReadOnlyList<SdkCpuWorkContributor> RetainedOamContributors(
        IEnumerable<Sdk2DOperation>? operations,
        long unitLower,
        long unitUpper,
        string calibration)
    {
        if (operations?.Any(operation => operation is Sdk2DOperation.DrawLogicalSprite) != true)
        {
            return [];
        }

        return
        [
            SdkCpuWorkContributor.Create(
                SdkCpuWorkContributorIds.SpritePublishTransfer,
                SdkCpuWorkContributorCategories.TargetRuntime,
                "one retained sprite publication transfer",
                count: 1,
                unitLower,
                unitUpper,
                calibration,
                detailOf: SdkCpuWorkContributorIds.SpritePublish),
        ];
    }

    private static SdkCpuWorkUnknown Unknown(string id, string reason) => new(id, reason);
}
