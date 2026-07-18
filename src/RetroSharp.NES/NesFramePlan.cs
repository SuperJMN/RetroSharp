namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal sealed record NesPhysicalFrameWindow(string Id, long Capacity);

internal sealed record NesFrameWork(string ContributorId, string WindowId);

internal sealed record NesStagedFrameWork(
    string Id,
    string PrepareWindowId,
    string CommitWindowId,
    int MaximumPhysicalFrames);

internal sealed record NesFramePlan(
    string CartridgeProfile,
    bool UseFourScreenNametables,
    bool UsesRetainedOam,
    int RetainedOamByteCount,
    bool UsesPackedCameraRuntime,
    bool UseSequentialOamPublication,
    byte MaximumCameraWalkStepsPerFrame,
    int CameraRowTileWritesPerFrame,
    int CameraRowAttributePhase,
    IReadOnlyList<NesPhysicalFrameWindow> Windows,
    IReadOnlyList<NesFrameWork> MandatoryWork,
    IReadOnlyList<NesStagedFrameWork> StagedWork)
{
    internal const string CameraRowStagingId = "camera-row-stream";
    private const long NtscCpuCyclesPerFrame = 29_780;
    private const long NtscCpuCyclesPerVideoSafeWindow = 2_273;
    private const byte CameraWalkStepsPerFrame = 8;
    private const int DefaultCameraRowTileWritesPerFrame = 8;
    private const int DefaultCameraRowAttributePhase = 4;
    private const int MaximumSequentialOamBytes = 152;

    internal static NesFramePlan Create(
        NesVideoProgram program,
        NesCartridgeLayout layout,
        bool usesPackedCameraRuntime)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(layout);

        return Create(
            program,
            layout.EmitMmc3Foundation ? "nes-mmc3-tvrom-v1" : "nes-mapper-0-current",
            layout.UseFourScreenNametables,
            usesPackedCameraRuntime,
            layout.EmitMmc3Foundation && usesPackedCameraRuntime);
    }

    internal static NesFramePlan Create(
        NesVideoProgram program,
        string cartridgeProfile,
        bool useFourScreenNametables,
        bool usesPackedCameraRuntime,
        bool useSequentialOamPublication)
    {
        ArgumentNullException.ThrowIfNull(program);
        var retainedOamByteCount = Math.Min(
            256,
            program.SdkOperationStream
                .OfType<Sdk2DOperation.DrawLogicalSprite>()
                .Sum(operation => program.SpriteAssets[operation.SpriteId].Pieces.Count * 4));
        return Create(
            cartridgeProfile,
            program.SdkOperationStream.Any(operation => operation is Sdk2DOperation.WaitFrame),
            retainedOamByteCount > 0,
            retainedOamByteCount,
            usesPackedCameraRuntime,
            useSequentialOamPublication,
            useFourScreenNametables);
    }

    internal static NesFramePlan Create(
        string cartridgeProfile,
        bool hasFrameBoundary,
        bool usesRetainedOam,
        int retainedOamByteCount,
        bool usesPackedCameraRuntime,
        bool useSequentialOamPublication,
        bool useFourScreenNametables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartridgeProfile);
        if (retainedOamByteCount is < 0 or > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedOamByteCount),
                retainedOamByteCount,
                "Retained OAM byte count must be between 0 and 256.");
        }

        if (usesRetainedOam != (retainedOamByteCount > 0))
        {
            throw new InvalidOperationException("NES retained OAM usage and byte count must describe the same plan.");
        }

        if (useSequentialOamPublication && retainedOamByteCount > MaximumSequentialOamBytes)
        {
            throw new InvalidOperationException(
                "NES MMC3 retained OAM publication supports at most 38 hardware sprites within the current VBlank budget.");
        }

        var work = new List<NesFrameWork>();
        if (hasFrameBoundary)
        {
            work.Add(new NesFrameWork(
                SdkCpuWorkContributorIds.FrameBoundaryActive,
                SdkCpuWorkWindowIds.VideoSafe));
        }

        if (usesRetainedOam)
        {
            work.Add(new NesFrameWork(
                SdkCpuWorkContributorIds.SpriteDraw,
                SdkCpuWorkWindowIds.Frame));
            work.Add(new NesFrameWork(
                SdkCpuWorkContributorIds.SpritePublish,
                SdkCpuWorkWindowIds.VideoSafe));
        }

        if (usesPackedCameraRuntime)
        {
            work.Add(new NesFrameWork(
                SdkCpuWorkContributorIds.CameraApply,
                SdkCpuWorkWindowIds.Frame));
            work.Add(new NesFrameWork(
                SdkCpuWorkContributorIds.WorldPrepare,
                SdkCpuWorkWindowIds.Frame));
            work.Add(new NesFrameWork(
                SdkCpuWorkContributorIds.WorldCommit,
                SdkCpuWorkWindowIds.VideoSafe));
        }

        return new NesFramePlan(
            cartridgeProfile,
            useFourScreenNametables,
            usesRetainedOam,
            retainedOamByteCount,
            usesPackedCameraRuntime,
            useSequentialOamPublication,
            CameraWalkStepsPerFrame,
            DefaultCameraRowTileWritesPerFrame,
            DefaultCameraRowAttributePhase,
            [
                new NesPhysicalFrameWindow(SdkCpuWorkWindowIds.Frame, NtscCpuCyclesPerFrame),
                new NesPhysicalFrameWindow(SdkCpuWorkWindowIds.VideoSafe, NtscCpuCyclesPerVideoSafeWindow),
            ],
            work,
            usesPackedCameraRuntime || useFourScreenNametables
                ? [new NesStagedFrameWork(
                    CameraRowStagingId,
                    SdkCpuWorkWindowIds.Frame,
                    SdkCpuWorkWindowIds.VideoSafe,
                    DefaultCameraRowAttributePhase + 1)]
                : []);
    }

    internal SdkCpuWorkReport CreateCpuWorkReport(IEnumerable<Sdk2DOperation> operations)
    {
        var report = SdkCpuWorkReportFactory.ForNes(
            CartridgeProfile,
            UseSequentialOamPublication ? null : operations);
        if (UseSequentialOamPublication && UsesRetainedOam)
        {
            var publicationCycles = checked(RetainedOamByteCount * 13L + 7);
            report = SdkCpuWorkReport.Create(
                report.Target,
                report.Profile,
                report.Unit,
                report.FrameWindow,
                report.Contributors.Append(SdkCpuWorkContributor.Create(
                    SdkCpuWorkContributorIds.SpritePublish,
                    SdkCpuWorkContributorCategories.TargetRuntime,
                    "one sequential retained OAM publication prefix",
                    count: 1,
                    unitLower: publicationCycles,
                    unitUpper: publicationCycles,
                    calibration: "NesFramePlan.SequentialOamPublication/v1")),
                report.Unknowns.Where(unknown => unknown.Id != SdkCpuWorkContributorIds.SpritePublish));
        }

        return ProjectCpuWork(report);
    }

    internal SdkCpuWorkReport ProjectCpuWork(SdkCpuWorkReport wholeFrame)
    {
        ArgumentNullException.ThrowIfNull(wholeFrame);
        if (wholeFrame.Target != "nes" || wholeFrame.Profile != CartridgeProfile)
        {
            throw new InvalidOperationException(
                $"NES frame plan '{CartridgeProfile}' cannot project {wholeFrame.Target}/{wholeFrame.Profile} CPU work.");
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
        NesPhysicalFrameWindow window,
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
