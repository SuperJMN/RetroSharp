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

    /// <summary>
    /// CPU cycles that elapse between the start of hardware VBlank and the first video-safe store
    /// a frame is able to perform: NMI dispatch, the frame-pending handshake, the
    /// <c>$2002</c> recheck and the publication-state store.
    /// </summary>
    /// <remarks>
    /// Measured with <c>NesTestCpu</c> on every shipping NES sample that drives a packed camera:
    /// entry latency on non-committing frames lands between 184 and 186 cycles. 192 is the
    /// documented upper bound. It is reserved, never spent, so a program that exactly meets the
    /// remaining budget still starts writing inside VBlank.
    /// </remarks>
    internal const long NmiEntryReserveCycles = 192;

    // Frame-boundary bookkeeping performed inside the video-safe window on every frame:
    // frame counter, pending-flag clears and the tail that re-arms the handshake.
    private const long FrameBoundaryOverheadCycles = 64;

    // Dispatch of one packed background column commit before any PPU payload byte moves:
    // JSR into the commit edge, slot selection, the 16-bit tag and payload-length validation,
    // the selected-state publication and the slot-stride select.
    private const long PackedColumnCommitOverheadCycles = 544;

    // Static-band payload coefficients, one per emitted shape in NesPackedColumnCommit.
    private const long PackedColumnPpuAddressPairCycles = 20;
    private const long PackedColumnTileWriteCycles = 9;
    private const long PackedColumnAttributeSelectCycles = 8;
    private const long PackedColumnAttributeWriteCycles = 23;

    // Camera-relative commits resolve every address at run time through the row tables, so each
    // write costs materially more than the folded static-band form.
    private const long CameraRelativePpuAddressPairCycles = 60;
    private const long CameraRelativeTileWriteCycles = 17;
    private const long CameraRelativeAttributeWriteCycles = 70;
    private const long CameraRelativeNameTableRuns = 2;

    // $4014 sprite DMA: 513 cycles plus the store that starts it, rounded up for the odd-cycle
    // alignment penalty.
    internal const long OamDmaCycles = 514;

    private const string VideoSafeBudgetCalibration = "NesFramePlan.VideoSafeBudget/v1";

    internal long VideoSafeCycleLimit =>
        Windows.Single(window => window.Id == SdkCpuWorkWindowIds.VideoSafe).Capacity;

    /// <summary>
    /// Upper bound on the CPU cycles this frame spends inside the hardware VBlank window,
    /// counting the NMI entry reserve, frame-boundary bookkeeping, the packed background column
    /// commit and the retained-OAM publication that share the same window.
    /// </summary>
    internal long VideoSafeCycleCost(NesPackedColumnCommit? columnCommit) =>
        NmiEntryReserveCycles
        + FrameBoundaryOverheadCycles
        + (columnCommit is { } commit ? PackedColumnCommitCycles(commit) : 0)
        + RetainedOamPublicationCycles;

    internal long RetainedOamPublicationCycles => UsesRetainedOam
        ? UseSequentialOamPublication
            ? SequentialOamPublicationCycles
            : OamDmaCycles
        : 0;

    // Mirrors NesOamPublicationSchedule's straight-line shape: one reset pair plus two stores per
    // retained byte. The schedule itself is scheduler-owned, so the coefficient is restated here
    // and pinned by NesFramePlanTests.
    private long SequentialOamPublicationCycles => checked(RetainedOamByteCount * 8L + 6L);

    /// <summary>
    /// Rejects a configuration whose per-frame video-safe work cannot complete inside VBlank.
    /// Without this the compiler emits the overrun silently and the tail of the commit lands on
    /// rendered scanlines as visible corruption.
    /// </summary>
    internal void RequireVideoSafeBudget(NesPackedColumnCommit? columnCommit, string configurationDescription)
    {
        var cost = VideoSafeCycleCost(columnCommit);
        var limit = VideoSafeCycleLimit;
        if (cost <= limit)
        {
            return;
        }

        var shape = columnCommit is { } commit
            ? $"{commit.Length} column tiles plus {commit.AttributeCount} attribute bytes"
            : "no background column commit";
        var sprites = UsesRetainedOam
            ? $"{RetainedOamByteCount} retained OAM bytes ({RetainedOamByteCount / 4} hardware sprites)"
            : "no retained sprites";
        throw new InvalidOperationException(
            $"{configurationDescription} does not fit the NTSC video-safe window: publishing {shape} " +
            $"together with {sprites} costs up to {cost} CPU cycles per frame, but only {limit} are " +
            $"available (of which {NmiEntryReserveCycles} are reserved for NMI entry). Reduce the " +
            "streamed band height or the number of retained sprites.");
    }

    private static long PackedColumnCommitCycles(NesPackedColumnCommit commit)
    {
        if (commit.FixedStart is null)
        {
            return PackedColumnCommitOverheadCycles
                + CameraRelativeNameTableRuns * CameraRelativePpuAddressPairCycles
                + commit.Length * CameraRelativeTileWriteCycles
                + commit.AttributeCount * CameraRelativeAttributeWriteCycles;
        }

        var groups = commit.AttributeGroups.ToArray();
        var nameTableSelects = groups
            .Select(group => group.Row / NesPackedCameraRuntime.NameTableRows)
            .Aggregate((Count: 0, Previous: -1), (state, half) =>
                half == state.Previous ? state : (state.Count + 1, half))
            .Count;
        return PackedColumnCommitOverheadCycles
            + commit.TileRuns.Count() * PackedColumnPpuAddressPairCycles
            + commit.Length * PackedColumnTileWriteCycles
            + nameTableSelects * PackedColumnAttributeSelectCycles
            + groups.Length * PackedColumnAttributeWriteCycles;
    }

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
        var collectedOperations = NesSdkProgramOperations.Collected(program.SdkProgram);
        var retainedOamByteCount = Math.Min(
            256,
            collectedOperations
                .OfType<Sdk2DOperation.DrawLogicalSprite>()
                .Sum(operation => program.SpriteAssets[operation.SpriteId].Pieces.Count * 4));
        return Create(
            cartridgeProfile,
            collectedOperations.Any(operation => operation is Sdk2DOperation.WaitFrame),
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

        var frame = new NesFramePlan(
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
        // A program with no packed camera still shares VBlank between the frame boundary and the
        // retained-OAM publication, so the floor of the joint budget is checked here.
        frame.RequireVideoSafeBudget(null, $"NES {cartridgeProfile} frame plan");
        return frame;
    }

    internal SdkCpuWorkReport ProjectCpuWork(
        SdkCpuWorkReport wholeFrame,
        NesPackedColumnCommit? videoSafeColumnCommit = null,
        IReadOnlyList<SdkCpuWorkContributor>? videoSafeSourceContributors = null)
    {
        ArgumentNullException.ThrowIfNull(wholeFrame);
        if (wholeFrame.Target != "nes" || wholeFrame.Profile != CartridgeProfile)
        {
            throw new InvalidOperationException(
                $"NES frame plan '{CartridgeProfile}' cannot project {wholeFrame.Target}/{wholeFrame.Profile} CPU work.");
        }

        var sourceContributors = videoSafeSourceContributors ?? [];
        var windows = Windows.Select(window => window.Id == SdkCpuWorkWindowIds.Frame
            ? SdkCpuWorkWindowReport.Create(
                window.Id,
                window.Capacity,
                wholeFrame.Contributors,
                wholeFrame.Unknowns)
            : window.Id == SdkCpuWorkWindowIds.VideoSafe
                ? ProjectVideoSafeWindow(window, videoSafeColumnCommit, sourceContributors)
            : ProjectWindow(window, wholeFrame)).ToArray();
        return wholeFrame with { Windows = windows };
    }

    private SdkCpuWorkWindowReport ProjectVideoSafeWindow(
        NesPhysicalFrameWindow window,
        NesPackedColumnCommit? columnCommit,
        IReadOnlyList<SdkCpuWorkContributor> sourceContributors)
    {
        var contributors = new List<SdkCpuWorkContributor>
        {
            SdkCpuWorkContributor.Create(
                SdkCpuWorkContributorIds.FrameBoundaryActive,
                SdkCpuWorkContributorCategories.TargetRuntime,
                "NMI entry reserve and frame-boundary bookkeeping",
                count: 1,
                unitLower: NmiEntryReserveCycles + FrameBoundaryOverheadCycles,
                unitUpper: NmiEntryReserveCycles + FrameBoundaryOverheadCycles,
                calibration: VideoSafeBudgetCalibration),
        };

        var unknowns = new List<SdkCpuWorkUnknown>();
        if (columnCommit is { } commit)
        {
            var commitCycles = PackedColumnCommitCycles(commit);
            contributors.Add(SdkCpuWorkContributor.Create(
                SdkCpuWorkContributorIds.WorldCommit,
                SdkCpuWorkContributorCategories.TargetRuntime,
                "one packed background column commit",
                count: 1,
                unitLower: commitCycles,
                unitUpper: commitCycles,
                calibration: VideoSafeBudgetCalibration));
        }
        else if (UsesPackedCameraRuntime)
        {
            unknowns.Add(new SdkCpuWorkUnknown(
                SdkCpuWorkContributorIds.WorldCommit,
                "Packed column commit shape was not configured when this projection was made."));
        }

        var retainedOamCycles = RetainedOamPublicationCycles;
        if (retainedOamCycles > 0)
        {
            contributors.Add(SdkCpuWorkContributor.Create(
                SdkCpuWorkContributorIds.SpritePublish,
                SdkCpuWorkContributorCategories.TargetRuntime,
                "one retained OAM publication boundary",
                count: 1,
                unitLower: retainedOamCycles,
                unitUpper: retainedOamCycles,
                calibration: VideoSafeBudgetCalibration));
        }

        contributors.AddRange(sourceContributors);
        return SdkCpuWorkWindowReport.Create(window.Id, window.Capacity, contributors, unknowns);
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
