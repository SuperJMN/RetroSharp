namespace RetroSharp.GameBoy;

internal static class GameBoyPackedCameraRuntime
{
    internal const ushort VisibleCameraXLow = 0xC14D;
    internal const ushort VisibleCameraXHigh = 0xC14E;
    internal const ushort VisibleCameraYLow = 0xC14F;
    internal const ushort VisibleCameraYHigh = 0xC150;
    internal const ushort CriticalSection = 0xC151;
    internal const ushort RequestCount = 0xC152;
    internal const ushort PrepareCount = 0xC153;
    internal const ushort ResidentCount = 0xC154;
    internal const ushort CommitCount = 0xC155;
    internal const ushort ReleaseCount = 0xC156;
    internal const ushort BankWorkInCommit = 0xC157;
    internal const ushort DecodeWorkInCommit = 0xC158;
    internal const ushort LastCommitVramWrites = 0xC159;
    internal const ushort IteratorLow = 0xC15A;
    internal const ushort IteratorHigh = 0xC15B;
    internal const ushort PreparedSlot = 0xC15C;
    internal const ushort CommitTarget = 0xC15D;
    internal const ushort CommitTargetStart = 0xC15E;
    internal const ushort CommitWorldEdgeLow = 0xC15F;

    internal const ushort PendingDirection = 0xC160;
    internal const ushort PendingSecondDirection = 0xC161;
    internal const ushort PendingSlot = 0xC162;
    internal const ushort PendingSecondSlot = 0xC163;
    internal const ushort PendingDiagonalColumnDirection = 0xC164;
    internal const ushort PendingDiagonalColumnSecondDirection = 0xC165;
    internal const ushort PendingDiagonalColumnSlot = 0xC166;
    internal const ushort PendingDiagonalColumnSecondSlot = 0xC167;
    internal const ushort PendingDiagonalRowDirection = 0xC168;
    internal const ushort PendingDiagonalRowSecondDirection = 0xC169;
    internal const ushort PendingDiagonalRowSlot = 0xC16A;
    internal const ushort PendingDiagonalRowSecondSlot = 0xC16B;
    internal const ushort CommitWorldEdgeHigh = 0xC16C;
    internal const ushort CommitDirection = 0xC16D;
    internal const ushort CommitAxis = 0xC16E;
    internal const ushort SelectedSlot = 0xC16F;

    internal const ushort Slot0 = 0xC170;
    internal const ushort Slot1 = 0xC17A;
    internal const ushort DestinationLow = 0xC184;
    internal const ushort DestinationHigh = 0xC185;
    internal const ushort PayloadRemaining = 0xC186;
    internal const ushort TileScratch = 0xC187;
    internal const ushort LastCommittedWorldEdgeLow = 0xC188;
    internal const ushort LastCommittedWorldEdgeHigh = 0xC189;
    internal const ushort LastCommittedAxis = 0xC18A;
    internal const ushort LastCommittedDirection = 0xC18B;
    internal const ushort CommitSucceeded = 0xC18C;
    internal const ushort VisualCacheValid = 0xC18D;
    internal const ushort VisualCacheChunkLow = 0xC18E;
    internal const ushort VisualCacheChunkHigh = 0xC18F;
    internal const ushort PendingVisualChunkLow = 0xC190;
    internal const ushort PendingVisualChunkHigh = 0xC191;
    internal const ushort VisualSelectedSlot = 0xC192;
    internal const ushort VisualCache1Valid = 0xC193;
    internal const ushort VisualCache1ChunkLow = 0xC194;
    internal const ushort VisualCache1ChunkHigh = 0xC195;
    internal const ushort VisualReplacementNext = 0xC196;
    internal const ushort WaitAudioEnabled = 0xC197;
    internal const ushort WaitAudioTicked = 0xC198;
    internal const ushort CurrentLy = 0xC199;
    internal const ushort LastObservedLy = 0xC19A;
    internal const ushort LastObservedLyValid = 0xC19B;
    internal const ushort DirectoryWorkInVBlank = 0xC19C;
    internal const int SlotMetadataBytes = 10;

    internal const int StateOffset = 0;
    internal const int AxisOffset = 1;
    internal const int DirectionOffset = 2;
    internal const int WorldEdgeLowOffset = 3;
    internal const int WorldEdgeHighOffset = 4;
    internal const int TargetOffset = 5;
    internal const int TargetStartOffset = 6;
    internal const int OrthogonalLowOffset = 7;
    internal const int OrthogonalHighOffset = 8;
    internal const int PayloadLengthOffset = 9;

    internal const byte Empty = 0;
    internal const byte Requested = 1;
    internal const byte Preparing = 2;
    internal const byte Resident = 3;
    internal const byte Committing = 4;
    internal const byte Released = 5;

    internal const byte Column = 1;
    internal const byte Row = 2;
    internal const byte Negative = 1;
    internal const byte Positive = 2;
    internal const byte NoSlot = 0xFF;

    internal static ushort SlotMetadata(int slot) => slot switch
    {
        0 => Slot0,
        1 => Slot1,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}

internal static class GameBoyPackedCameraRuntimeEmitter
{
    internal static void EmitWaitOutsideVBlank(GbBuilder builder)
    {
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitOutsideVBlankLabel);
    }

    internal static void EmitWaitOutsideVBlankRoutine(GbBuilder builder)
    {
        const byte safeActiveScanlineEnd = 136;
        var firstObservation = builder.CreateLabel("packed_world_first_ly_observation");
        var wrapped = builder.CreateLabel("packed_world_ly_wrapped");
        var wrappedWithoutAudio = builder.CreateLabel("packed_world_wrap_without_audio");
        var classify = builder.CreateLabel("packed_world_classify_ly");
        var waitForVBlank = builder.CreateLabel("packed_world_wait_for_vblank");
        var inVBlank = builder.CreateLabel("packed_world_in_vblank");
        var waitForSafeActive = builder.CreateLabel("packed_world_wait_for_safe_active");
        var audioAlreadyTicked = builder.CreateLabel("packed_world_audio_already_ticked");
        var safeActive = builder.CreateLabel("packed_world_safe_active");
        builder.Label(GameBoyRomBuilder.WorldPackWaitOutsideVBlankLabel);
        builder.Emit(0xC5); // PUSH BC; callers may own byte/element counters in BC.
        builder.LoadHighRamA(0x44); // LY
        builder.StoreA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.LoadA(GameBoyPackedCameraRuntime.LastObservedLyValid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, firstObservation);
        builder.LoadA(GameBoyPackedCameraRuntime.LastObservedLy);
        builder.LoadBFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.CompareB();
        builder.JumpAbsolute(0xDA, wrapped); // JP C: current LY wrapped below the previous observation.
        builder.JumpAbsolute(classify);

        builder.Label(firstObservation);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.LastObservedLyValid);
        builder.JumpAbsolute(classify);

        builder.Label(wrapped);
        builder.LoadA(GameBoyPackedCameraRuntime.WaitAudioEnabled);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, wrappedWithoutAudio);
        builder.LoadA(GameBoyPackedCameraRuntime.WaitAudioTicked);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, wrappedWithoutAudio);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
        builder.Label(wrappedWithoutAudio);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyPackedCameraRuntime.WaitAudioTicked);

        builder.Label(classify);
        builder.LoadA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.StoreA(GameBoyPackedCameraRuntime.LastObservedLy);
        builder.CompareImmediate(safeActiveScanlineEnd);
        builder.JumpAbsolute(0xDA, safeActive); // JP C,safeActive

        builder.Label(waitForVBlank);
        builder.LoadHighRamA(0x44);
        builder.StoreA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.CompareImmediate(144);
        builder.JumpAbsolute(0xDA, waitForVBlank); // LY 136-143: wait for this frame's VBlank.
        builder.Label(inVBlank);
        builder.LoadA(GameBoyPackedCameraRuntime.WaitAudioEnabled);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, waitForSafeActive);
        builder.LoadA(GameBoyPackedCameraRuntime.WaitAudioTicked);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, audioAlreadyTicked);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.WaitAudioTicked);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
        builder.Label(audioAlreadyTicked);
        builder.Label(waitForSafeActive);
        builder.LoadHighRamA(0x44);
        builder.StoreA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.CompareImmediate(safeActiveScanlineEnd);
        builder.JumpAbsolute(0xD2, waitForSafeActive); // Wait through VBlank and the guard band.
        builder.Label(safeActive);
        builder.LoadA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.StoreA(GameBoyPackedCameraRuntime.LastObservedLy);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyPackedCameraRuntime.WaitAudioTicked);
        builder.Emit(0xC1); // POP BC
        builder.Emit(0xC9); // RET
    }

    internal static void EmitRecordCriticalWork(GbBuilder builder, ushort counterAddress)
    {
        var done = builder.CreateLabel("packed_camera_critical_work_done");
        builder.Emit(0xF5); // PUSH AF
        builder.LoadA(GameBoyPackedCameraRuntime.CriticalSection);
        builder.CompareImmediate(0);
        var record = builder.CreateLabel("packed_camera_critical_work_record");
        builder.JumpAbsolute(0xC2, record);
        builder.LoadHighRamA(0x44);
        builder.CompareImmediate(144);
        builder.JumpAbsolute(0xDA, done);
        builder.Label(record);
        builder.LoadA(counterAddress);
        builder.AddAImmediate(1);
        builder.StoreA(counterAddress);
        builder.Label(done);
        builder.Emit(0xF1); // POP AF
    }
}
