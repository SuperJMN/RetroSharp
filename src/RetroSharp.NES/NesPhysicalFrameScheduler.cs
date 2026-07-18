namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal enum NesFrameBoundaryPurpose
{
    Gameplay,
    ExplicitVideoTransfer,
}

internal sealed class NesPhysicalFrameScheduler
{
    private const ushort OamDmaAddress = 0x4014;
    internal const string FrameSignalNmiHandlerLabel = "nes_frame_signal_nmi_handler";
    private readonly PrgBuilder builder;
    private readonly NesFramePlan plan;

    internal NesPhysicalFrameScheduler(PrgBuilder builder, NesFramePlan plan)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);
        this.builder = builder;
        this.plan = plan;
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

    internal SdkCpuWorkReport CreateCpuWorkReport(IEnumerable<Sdk2DOperation> operations) =>
        plan.CreateCpuWorkReport(operations);

    internal string SelectedProfile => plan.CartridgeProfile;

    internal bool UsesPackedCameraRuntime => plan.UsesPackedCameraRuntime;

    internal bool UseFourScreenNametables => plan.UseFourScreenNametables;

    internal void EmitMaximumCameraWalkStepsToA() =>
        builder.LoadAImmediate(plan.MaximumCameraWalkStepsPerFrame);

    internal (int TilesPerPhase, int AttributePhase) PackedCameraRowSchedule() =>
        (plan.PackedCameraRowTileWritesPerFrame, plan.PackedCameraRowAttributePhase);

    internal void EmitFrameBoundary(NesFrameBoundaryPurpose purpose)
    {
        EmitFrameBoundary(purpose, frameEmitter: null);
    }

    internal void EmitFrameBoundary(
        NesFrameBoundaryPurpose purpose,
        NesSdkOperationLowerer? frameEmitter)
    {
        if (plan.UsesPackedCameraRuntime)
        {
            EmitPackedFrameBoundary(purpose, frameEmitter);
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
        EmitPendingCameraWork(purpose, frameEmitter);
        EmitRetainedOamWork();
    }

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
        NesSdkOperationLowerer? frameEmitter)
    {
        var awaitFreshFrame = builder.CreateLabel("nes_packed_await_fresh_frame");
        var end = builder.CreateLabel("nes_packed_wait_frame_end");
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
        builder.BranchRelative(0xF0, awaitFreshFrame);
        if (purpose == NesFrameBoundaryPurpose.Gameplay)
        {
            frameEmitter?.EmitRetainPendingCameraAcrossStaleBoundary();
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
        EmitPendingCameraWork(purpose, frameEmitter);
        EmitRetainedOamWork();
        builder.Label(end);
    }

    private static void EmitPendingCameraWork(
        NesFrameBoundaryPurpose purpose,
        NesSdkOperationLowerer? frameEmitter)
    {
        if (purpose == NesFrameBoundaryPurpose.Gameplay)
        {
            frameEmitter?.EmitApplyPendingCameraScrollAtVBlank();
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

    private void EmitOamPublication()
    {
        if (!plan.UseSequentialOamPublication)
        {
            builder.LoadAImmediate((NesRuntimeMemoryLayout.Sprite.OamShadow >> 8) & 0xFF);
            builder.StoreAAbsolute(OamDmaAddress);
            return;
        }

        var startIndex = 256 - plan.RetainedOamByteCount;
        var biasedShadowAddress = checked((ushort)(NesRuntimeMemoryLayout.Sprite.OamShadow - startIndex));
        var publish = builder.CreateLabel("oam_shadow_publish");
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0x2003);
        builder.LoadXImmediate(startIndex);
        builder.Label(publish);
        builder.LoadAAbsoluteX(biasedShadowAddress);
        builder.StoreAAbsolute(0x2004);
        builder.IncrementX();
        builder.BranchRelative(0xD0, publish);
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
