namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal enum NesFrameBoundaryPurpose
{
    Gameplay,
    ExplicitVideoTransfer,
}

internal readonly record struct NesCameraConfig(
    int MapWidth,
    int MapHeight,
    int StreamY,
    int StreamHeight,
    bool UseFourScreenNametables)
{
    internal bool CanStreamColumns => UseFourScreenNametables ? MapWidth > 64 : MapWidth > 32;

    internal bool CanStreamRows => UseFourScreenNametables && MapHeight > 60;

    internal bool CanStreamAnyAxis => CanStreamColumns || CanStreamRows;
}

internal enum NesPendingCameraStream : byte
{
    None = 0,
    Column = 1,
    Row = 2,
}

internal enum NesCameraPublicationState : byte
{
    Ready = 0,
    Applied = 1,
    SuppressedForCurrentTick = 0x80,
}

internal abstract record NesVideoSafeTransfer
{
    internal sealed record StreamColumn(NesCameraConfig Config) : NesVideoSafeTransfer;

    internal sealed record StreamRow(int TargetRow, int SourceRow, int X, int Width) : NesVideoSafeTransfer;

    internal sealed record PendingColumn(NesCameraConfig Config) : NesVideoSafeTransfer;

    internal sealed record BeginPackedCommit : NesVideoSafeTransfer;

    internal sealed record FinalizePackedCommit : NesVideoSafeTransfer;

    internal sealed record StagedRowTiles(NesCameraConfig Config, int TilesPerPhase) : NesVideoSafeTransfer;

    internal sealed record StagedRowAttributes(NesCameraConfig Config) : NesVideoSafeTransfer;

    internal sealed record RestoreCameraScroll(NesCameraConfig Config) : NesVideoSafeTransfer;
}

internal sealed class NesPhysicalFrameScheduler
{
    private const ushort OamDmaAddress = 0x4014;
    internal const string FrameSignalNmiHandlerLabel = "nes_frame_signal_nmi_handler";
    private readonly PrgBuilder builder;
    private readonly NesFramePlan plan;
    private readonly NesOamPublicationSchedule? oamPublicationSchedule;
    private readonly NesPackedCameraPhaseSchedule? packedCameraPhaseSchedule;
    private readonly NesStagedFrameWork? cameraRowStaging;

    internal NesPhysicalFrameScheduler(PrgBuilder builder, NesFramePlan plan)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);
        this.builder = builder;
        this.plan = plan;
        if (plan.UseSequentialOamPublication && plan.UsesRetainedOam)
        {
            oamPublicationSchedule = NesOamPublicationSchedule.Create(
                NesRuntimeMemoryLayout.Sprite.OamShadow,
                plan.RetainedOamByteCount);
        }

        if (plan.UsesPackedCameraRuntime)
        {
            packedCameraPhaseSchedule = NesPackedCameraPhaseSchedule.Create(plan.RetainedOamByteCount);
        }

        if (plan.UsesPackedCameraRuntime || plan.UseFourScreenNametables)
        {
            cameraRowStaging = plan.StagedWork.SingleOrDefault(work =>
                work.Id == NesFramePlan.CameraRowStagingId)
                ?? throw new InvalidOperationException("NES camera row scheduling requires one declared staging deadline.");
            var expectedDeadline = packedCameraPhaseSchedule?.MaximumRowFrames
                                   ?? plan.CameraRowAttributePhase + 1;
            if (cameraRowStaging.MaximumPhysicalFrames != expectedDeadline)
            {
                throw new InvalidOperationException(
                    "NES camera row staging deadline must match the emitted tile and attribute phase schedule.");
            }
        }
        else if (plan.StagedWork.Count != 0)
        {
            throw new InvalidOperationException("NES frame policy declares staged work for a runtime without camera-row scheduling.");
        }
    }

    internal static NesPhysicalFrameScheduler Create(
        PrgBuilder builder,
        NesVideoProgram program,
        NesCartridgeLayout layout,
        bool usesPackedCameraRuntime) =>
        new(builder, NesFramePlan.Create(program, layout, usesPackedCameraRuntime));

    internal static NesPhysicalFrameScheduler Create(
        PrgBuilder builder,
        NesVideoProgram program,
        bool useFourScreenNametables,
        bool usesPackedCameraRuntime,
        bool useSequentialOamPublication) =>
        Create(
            builder,
            program,
            useSequentialOamPublication ? "nes-mmc3-tvrom-v1" : "nes-mapper-0-current",
            useFourScreenNametables,
            usesPackedCameraRuntime,
            useSequentialOamPublication);

    internal static NesPhysicalFrameScheduler Create(
        PrgBuilder builder,
        NesVideoProgram program,
        string cartridgeProfile,
        bool useFourScreenNametables,
        bool usesPackedCameraRuntime,
        bool useSequentialOamPublication) =>
        new(
            builder,
            NesFramePlan.Create(
                program,
                cartridgeProfile,
                useFourScreenNametables,
                usesPackedCameraRuntime,
                useSequentialOamPublication));

    internal SdkCpuWorkReport CreateCpuWorkReport(IEnumerable<Sdk2DOperation> operations)
    {
        var report = SdkCpuWorkReportFactory.ForNes(
            plan.CartridgeProfile,
            oamPublicationSchedule is null ? operations : null);
        if (oamPublicationSchedule is not null)
        {
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
                    unitLower: oamPublicationSchedule.CpuCycles,
                    unitUpper: oamPublicationSchedule.CpuCycles,
                    calibration: "NesOamPublicationSchedule/v1")),
                report.Unknowns.Where(unknown => unknown.Id != SdkCpuWorkContributorIds.SpritePublish));
        }

        return plan.ProjectCpuWork(report);
    }

    internal string SelectedProfile => plan.CartridgeProfile;

    internal bool UsesPackedCameraRuntime => plan.UsesPackedCameraRuntime;

    internal bool UseFourScreenNametables => plan.UseFourScreenNametables;

    internal void EmitMaximumCameraWalkStepsToA() =>
        builder.LoadAImmediate(plan.MaximumCameraWalkStepsPerFrame);

    internal void EmitFrameBoundary(NesFrameBoundaryPurpose purpose)
    {
        EmitFrameBoundary(purpose, frameEmitter: null, cameraConfig: null);
    }

    internal void EmitFrameBoundary(
        NesFrameBoundaryPurpose purpose,
        NesSdkOperationLowerer? frameEmitter,
        NesCameraConfig? cameraConfig)
    {
        if (plan.UsesPackedCameraRuntime)
        {
            EmitPackedFrameBoundary(purpose, frameEmitter, cameraConfig);
            return;
        }

        var clearLabel = builder.CreateLabel("vblank_clear");
        var setLabel = builder.CreateLabel("vblank");
        builder.Label(clearLabel);
        builder.Emit(0x2C, 0x02, 0x20);
        builder.BranchRelative(0x30, clearLabel);
        builder.Label(setLabel);
        builder.Emit(0x2C, 0x02, 0x20);
        builder.BranchRelative(0x10, setLabel);
        EmitPendingCameraWork(purpose, frameEmitter, cameraConfig);
        EmitRetainedOamWork();
    }

    internal void EmitVideoSafeTransfer(
        NesVideoSafeTransfer transfer,
        NesSdkOperationLowerer frameEmitter)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(frameEmitter);
        EmitFrameBoundary(NesFrameBoundaryPurpose.ExplicitVideoTransfer, frameEmitter, cameraConfig: null);
        frameEmitter.EmitVideoSafeTransfer(transfer);
    }

    internal void EmitCameraSchedulingInitialization()
    {
        builder.LoadAImmediate((byte)NesPendingCameraStream.Column);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);
    }

    internal void EmitCameraApplication(
        NesSdkOperationLowerer frameEmitter,
        NesCameraConfig config)
    {
        ArgumentNullException.ThrowIfNull(frameEmitter);
        var usesPackedStreaming = plan.UsesPackedCameraRuntime && config.CanStreamAnyAxis;
        if (usesPackedStreaming)
        {
            frameEmitter.EmitVideoSafeTransfer(new NesVideoSafeTransfer.BeginPackedCommit());
        }
        else
        {
            EmitRawPendingCameraCommit(frameEmitter, config);
        }

        frameEmitter.EmitVideoSafeTransfer(new NesVideoSafeTransfer.RestoreCameraScroll(config));
        if (usesPackedStreaming)
        {
            frameEmitter.EmitVideoSafeTransfer(new NesVideoSafeTransfer.FinalizePackedCommit());
        }
        builder.LoadAImmediate((byte)NesCameraPublicationState.Applied);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);
    }

    private void EmitStagedCameraRowCommit(
        NesSdkOperationLowerer frameEmitter,
        NesCameraConfig config)
    {
        ArgumentNullException.ThrowIfNull(frameEmitter);
        var tilesLabel = builder.CreateLabel("nes_camera_row_tiles");
        var attributesLabel = builder.CreateLabel("nes_camera_row_attrs");
        var doneLabel = builder.CreateLabel("nes_camera_row_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.CompareImmediate(CameraRowAttributePhase);
        builder.BranchRelative(0xD0, tilesLabel);
        builder.JumpAbsolute(attributesLabel);

        builder.Label(tilesLabel);
        frameEmitter.EmitVideoSafeTransfer(
            new NesVideoSafeTransfer.StagedRowTiles(config, plan.CameraRowTileWritesPerFrame));
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.JumpAbsolute(doneLabel);

        builder.Label(attributesLabel);
        frameEmitter.EmitVideoSafeTransfer(new NesVideoSafeTransfer.StagedRowAttributes(config));
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate(0xFF ^ (byte)NesPendingCameraStream.Row);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);

        builder.Label(doneLabel);
    }

    internal void EmitPackedCameraRuntime(NesWorldPackRuntimePlan runtimePlan)
    {
        ArgumentNullException.ThrowIfNull(runtimePlan);
        NesPackedCameraRuntimeEmitter.Emit(
            builder,
            runtimePlan,
            packedCameraPhaseSchedule
            ?? throw new InvalidOperationException("Packed camera runtime emission requires a physical phase schedule."));
    }

    private int CameraRowAttributePhase =>
        cameraRowStaging?.MaximumPhysicalFrames - 1
        ?? throw new InvalidOperationException("NES camera-row phase emission requires validated staging policy.");

    internal void EmitOamShadowClear()
    {
        if (plan.UseSequentialOamPublication)
        {
            var clearLabel = builder.CreateLabel("oam_clear");
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(0x2003);
            builder.LoadAImmediate(0xFF);
            builder.LoadXImmediate(0);
            builder.Label(clearLabel);
            builder.StoreAAbsoluteX(NesRuntimeMemoryLayout.Sprite.OamShadow);
            builder.StoreAAbsolute(0x2004);
            builder.IncrementX();
            builder.BranchRelative(0xD0, clearLabel);
            return;
        }

        EmitOamShadowReset(clearCompletePage: true);
        builder.LoadAImmediate((NesRuntimeMemoryLayout.Sprite.OamShadow >> 8) & 0xFF);
        builder.StoreAAbsolute(OamDmaAddress);
    }

    internal void EmitFrameSignalNmiHandler()
    {
        builder.Label(FrameSignalNmiHandlerLabel);
        builder.PushA();
        var frameReady = builder.CreateLabel("nes_frame_signal_nmi_frame_ready");
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow);
        builder.BranchRelative(0xD0, frameReady);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh);
        builder.Label(frameReady);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
        builder.PullA();
        builder.Emit(0x40);
    }

    private void EmitPackedFrameBoundary(
        NesFrameBoundaryPurpose purpose,
        NesSdkOperationLowerer? frameEmitter,
        NesCameraConfig? cameraConfig)
    {
        var awaitFreshFrame = builder.CreateLabel("nes_packed_await_fresh_frame");
        var end = builder.CreateLabel("nes_packed_wait_frame_end");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
        builder.BranchRelative(0xF0, awaitFreshFrame);
        if (purpose == NesFrameBoundaryPurpose.Gameplay && cameraConfig is not null)
        {
            builder.LoadAImmediate((byte)NesCameraPublicationState.SuppressedForCurrentTick);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        }
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
        builder.JumpAbsolute(end);
        builder.Label(awaitFreshFrame);
        var pending = builder.CreateLabel("nes_packed_frame_pending");
        builder.Label(pending);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
        builder.BranchRelative(0xF0, pending);
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
        var hardwareVBlank = builder.CreateLabel("nes_packed_hardware_vblank");
        builder.Label(hardwareVBlank);
        builder.Emit(0x2C, 0x02, 0x20);
        builder.BranchRelative(0x10, hardwareVBlank);
        if (purpose == NesFrameBoundaryPurpose.Gameplay && cameraConfig is not null)
        {
            builder.LoadAImmediate((byte)NesCameraPublicationState.Ready);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        }
        EmitOamPublicationIfRetained();
        EmitPendingCameraWork(purpose, frameEmitter, cameraConfig);
        EmitOamShadowResetIfRetained();
        builder.Label(end);
    }

    private void EmitPendingCameraWork(
        NesFrameBoundaryPurpose purpose,
        NesSdkOperationLowerer? frameEmitter,
        NesCameraConfig? cameraConfig)
    {
        if (purpose == NesFrameBoundaryPurpose.Gameplay &&
            frameEmitter is not null &&
            cameraConfig is { } config)
        {
            EmitCameraApplication(frameEmitter, config);
        }
    }

    private void EmitRawPendingCameraCommit(
        NesSdkOperationLowerer frameEmitter,
        NesCameraConfig config)
    {
        if (!config.CanStreamAnyAxis)
        {
            return;
        }

        if (config.CanStreamColumns && config.CanStreamRows)
        {
            EmitStaggeredPendingCameraCommit(frameEmitter, config);
            return;
        }

        EmitSinglePendingCameraCommit(
            frameEmitter,
            config,
            config.CanStreamColumns ? NesPendingCameraStream.Column : NesPendingCameraStream.Row);
    }

    private void EmitSinglePendingCameraCommit(
        NesSdkOperationLowerer frameEmitter,
        NesCameraConfig config,
        NesPendingCameraStream kind)
    {
        var commitLabel = builder.CreateLabel("nes_camera_commit_pending");
        var doneLabel = builder.CreateLabel("nes_camera_commit_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate((byte)kind);
        builder.CompareImmediate((byte)NesPendingCameraStream.None);
        builder.BranchRelative(0xD0, commitLabel);
        builder.JumpAbsolute(doneLabel);

        builder.Label(commitLabel);
        EmitPendingCameraStream(frameEmitter, config, kind);

        builder.Label(doneLabel);
    }

    private void EmitStaggeredPendingCameraCommit(
        NesSdkOperationLowerer frameEmitter,
        NesCameraConfig config)
    {
        var checkRowLabel = builder.CreateLabel("nes_camera_commit_check_row");
        var columnPendingLabel = builder.CreateLabel("nes_camera_commit_column_pending");
        var bothPendingLabel = builder.CreateLabel("nes_camera_commit_both_pending");
        var rowNextLabel = builder.CreateLabel("nes_camera_commit_row_next");
        var rowPendingLabel = builder.CreateLabel("nes_camera_commit_row_pending");
        var commitColumnLabel = builder.CreateLabel("nes_camera_commit_column");
        var commitRowLabel = builder.CreateLabel("nes_camera_commit_row");
        var doneLabel = builder.CreateLabel("nes_camera_commit_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate((byte)NesPendingCameraStream.Column);
        builder.CompareImmediate((byte)NesPendingCameraStream.None);
        builder.BranchRelative(0xD0, columnPendingLabel);
        builder.JumpAbsolute(checkRowLabel);

        builder.Label(columnPendingLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate((byte)NesPendingCameraStream.Row);
        builder.CompareImmediate((byte)NesPendingCameraStream.None);
        builder.BranchRelative(0xD0, bothPendingLabel);
        builder.JumpAbsolute(commitColumnLabel);

        builder.Label(bothPendingLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);
        builder.CompareImmediate((byte)NesPendingCameraStream.Row);
        builder.BranchRelative(0xF0, rowNextLabel);
        builder.JumpAbsolute(commitColumnLabel);

        builder.Label(rowNextLabel);
        builder.JumpAbsolute(commitRowLabel);

        builder.Label(checkRowLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.AndImmediate((byte)NesPendingCameraStream.Row);
        builder.CompareImmediate((byte)NesPendingCameraStream.None);
        builder.BranchRelative(0xD0, rowPendingLabel);
        builder.JumpAbsolute(doneLabel);

        builder.Label(rowPendingLabel);
        builder.JumpAbsolute(commitRowLabel);

        builder.Label(commitColumnLabel);
        EmitPendingCameraStream(frameEmitter, config, NesPendingCameraStream.Column);
        builder.LoadAImmediate((byte)NesPendingCameraStream.Row);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);
        builder.JumpAbsolute(doneLabel);

        builder.Label(commitRowLabel);
        EmitPendingCameraStream(frameEmitter, config, NesPendingCameraStream.Row);
        builder.LoadAImmediate((byte)NesPendingCameraStream.Column);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);

        builder.Label(doneLabel);
    }

    private void EmitPendingCameraStream(
        NesSdkOperationLowerer frameEmitter,
        NesCameraConfig config,
        NesPendingCameraStream kind)
    {
        switch (kind)
        {
            case NesPendingCameraStream.Column:
                frameEmitter.EmitVideoSafeTransfer(new NesVideoSafeTransfer.PendingColumn(config));
                return;
            case NesPendingCameraStream.Row:
                EmitStagedCameraRowCommit(frameEmitter, config);
                return;
            default:
                throw new NotSupportedException($"Unsupported NES pending camera stream {kind}.");
        }
    }

    private void EmitRetainedOamWork()
    {
        if (!plan.UsesRetainedOam)
        {
            return;
        }

        EmitOamPublication();
        EmitOamShadowReset();
    }

    private void EmitOamPublicationIfRetained()
    {
        if (plan.UsesRetainedOam)
        {
            EmitOamPublication();
        }
    }

    private void EmitOamShadowResetIfRetained()
    {
        if (plan.UsesRetainedOam)
        {
            EmitOamShadowReset();
        }
    }

    private void EmitOamPublication()
    {
        if (oamPublicationSchedule is null)
        {
            builder.LoadAImmediate((NesRuntimeMemoryLayout.Sprite.OamShadow >> 8) & 0xFF);
            builder.StoreAAbsolute(OamDmaAddress);
            return;
        }

        oamPublicationSchedule.Emit(builder);
    }

    private void EmitOamShadowReset(bool clearCompletePage = false)
    {
        var byteCount = clearCompletePage ? 256 : plan.RetainedOamByteCount;
        if (byteCount == 0)
        {
            return;
        }

        var clearLabel = builder.CreateLabel("oam_shadow_reset");
        builder.LoadAImmediate(0xFF);
        if (byteCount < 256)
        {
            builder.LoadXImmediate(0);
            builder.Label(clearLabel);
            builder.StoreAAbsoluteX(NesRuntimeMemoryLayout.Sprite.OamShadow);
            builder.IncrementX();
            builder.CompareXImmediate(byteCount);
            builder.BranchRelative(0xD0, clearLabel);
            return;
        }

        builder.LoadXImmediate(0);
        builder.Label(clearLabel);
        builder.StoreAAbsoluteX(NesRuntimeMemoryLayout.Sprite.OamShadow);
        builder.IncrementX();
        builder.BranchRelative(0xD0, clearLabel);
    }
}
