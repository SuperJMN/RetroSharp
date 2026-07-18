namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal sealed record GameBoyPhysicalFrameWindow(string Id, long Capacity);

internal sealed record GameBoyFrameWork(string ContributorId, string WindowId);

internal sealed record GameBoyStagedFrameWork(
    string Id,
    string PrepareWindowId,
    string CommitWindowId,
    int MaximumPhysicalFrames);

internal sealed record GameBoyFramePlan(
    string CartridgeProfile,
    bool UsesRetainedOam,
    bool UsesPackedCameraRuntime,
    byte MaximumCameraWalkStepsPerFrame,
    bool SerializePackedDiagonalPreparation,
    IReadOnlyList<GameBoyPhysicalFrameWindow> Windows,
    IReadOnlyList<GameBoyFrameWork> MandatoryWork,
    IReadOnlyList<GameBoyStagedFrameWork> StagedWork)
{
    private const long DmgCyclesPerFrame = 70_224;
    private const long DmgCyclesPerVideoSafeWindow = 4_560;
    private const byte CameraWalkStepsPerFrame = 16;
    private const int PackedCameraMaximumPhysicalFrames = 3;

    internal static GameBoyFramePlan Create(
        GameBoyVideoProgram program,
        GameBoyRomLayout layout,
        bool usesPackedCameraRuntime)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(layout);

        var profile = ReferenceEquals(layout, GameBoyRomLayout.RomOnly)
            ? "gb-rom-only-current"
            : "gb-simple-mbc1-current";
        return Create(
            profile,
            program.SdkOperations.Any(operation => operation is Sdk2DOperation.WaitFrame),
            program.SdkOperations.Any(operation => operation is Sdk2DOperation.DrawLogicalSprite),
            usesPackedCameraRuntime);
    }

    internal static GameBoyFramePlan Create(
        string cartridgeProfile,
        bool hasFrameBoundary,
        bool usesRetainedOam,
        bool usesPackedCameraRuntime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartridgeProfile);
        var work = new List<GameBoyFrameWork>();
        if (hasFrameBoundary)
        {
            work.Add(new GameBoyFrameWork(
                SdkCpuWorkContributorIds.FrameBoundaryActive,
                SdkCpuWorkWindowIds.VideoSafe));
        }

        if (usesRetainedOam)
        {
            work.Add(new GameBoyFrameWork(
                SdkCpuWorkContributorIds.SpriteDraw,
                SdkCpuWorkWindowIds.Frame));
            work.Add(new GameBoyFrameWork(
                SdkCpuWorkContributorIds.SpritePublish,
                SdkCpuWorkWindowIds.VideoSafe));
        }

        if (usesPackedCameraRuntime)
        {
            work.Add(new GameBoyFrameWork(
                SdkCpuWorkContributorIds.CameraApply,
                SdkCpuWorkWindowIds.Frame));
            work.Add(new GameBoyFrameWork(
                SdkCpuWorkContributorIds.WorldPrepare,
                SdkCpuWorkWindowIds.Frame));
            work.Add(new GameBoyFrameWork(
                SdkCpuWorkContributorIds.WorldCommit,
                SdkCpuWorkWindowIds.VideoSafe));
        }

        return new GameBoyFramePlan(
            cartridgeProfile,
            usesRetainedOam,
            usesPackedCameraRuntime,
            CameraWalkStepsPerFrame,
            SerializePackedDiagonalPreparation: true,
            [
                new GameBoyPhysicalFrameWindow(SdkCpuWorkWindowIds.Frame, DmgCyclesPerFrame),
                new GameBoyPhysicalFrameWindow(SdkCpuWorkWindowIds.VideoSafe, DmgCyclesPerVideoSafeWindow),
            ],
            work,
            usesPackedCameraRuntime
                ? [new GameBoyStagedFrameWork(
                    "packed-camera-edge",
                    SdkCpuWorkWindowIds.Frame,
                    SdkCpuWorkWindowIds.VideoSafe,
                    PackedCameraMaximumPhysicalFrames)]
                : []);
    }

    internal SdkCpuWorkReport CreateCpuWorkReport(IEnumerable<Sdk2DOperation> operations) =>
        ProjectCpuWork(SdkCpuWorkReportFactory.ForGameBoy(CartridgeProfile, operations));

    internal SdkCpuWorkReport ProjectCpuWork(SdkCpuWorkReport wholeFrame)
    {
        ArgumentNullException.ThrowIfNull(wholeFrame);
        if (wholeFrame.Target != "gb" || wholeFrame.Profile != CartridgeProfile)
        {
            throw new InvalidOperationException(
                $"Game Boy frame plan '{CartridgeProfile}' cannot project {wholeFrame.Target}/{wholeFrame.Profile} CPU work.");
        }

        var windows = Windows.Select(window => window.Id == SdkCpuWorkWindowIds.Frame
            ? SdkCpuWorkWindowReport.Create(
                window.Id,
                window.Capacity,
                wholeFrame.Contributors,
                wholeFrame.Unknowns)
            : ProjectWindow(window, wholeFrame)).ToArray();
        return wholeFrame with { Windows = windows };
    }

    private SdkCpuWorkWindowReport ProjectWindow(
        GameBoyPhysicalFrameWindow window,
        SdkCpuWorkReport wholeFrame)
    {
        var contributorIds = MandatoryWork
            .Where(work => work.WindowId == window.Id)
            .Select(work => work.ContributorId)
            .ToHashSet(StringComparer.Ordinal);
        var contributors = wholeFrame.Contributors
            .Where(contributor =>
                contributorIds.Contains(contributor.Id) ||
                contributor.DetailOf is { } parent && contributorIds.Contains(parent))
            .ToArray();
        var unknowns = wholeFrame.Unknowns
            .Where(unknown => contributorIds.Contains(unknown.Id))
            .ToArray();
        return SdkCpuWorkWindowReport.Create(window.Id, window.Capacity, contributors, unknowns);
    }
}
