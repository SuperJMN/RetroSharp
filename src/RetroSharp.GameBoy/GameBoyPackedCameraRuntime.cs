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
    internal const ushort AudioTickCount = 0xC19D;
    internal const ushort EdgePreparationBankSession = 0xC19E;
    internal const ushort EdgeSubcellX = 0xC19F;
    internal const ushort EdgeSubcellY = 0xC1A0;
    internal const ushort EdgeLocalX = 0xC1A1;
    internal const ushort EdgeLocalY = 0xC1A2;
    internal const ushort EdgeValidWidth = 0xC1A3;
    internal const ushort EdgeValidHeight = 0xC1A4;
    internal const ushort EdgeReuseReady = 0xC1A5;
    internal const ushort EdgeVisualSlot = 0xC1A6;
    internal const ushort EdgeCellIndex = 0xC1A7;
    internal const ushort EdgeSameMetatile = 0xC1A8;
    internal const ushort EdgeExpansionAddressLow = 0xC1A9;
    internal const ushort EdgeExpansionAddressHigh = 0xC1AA;
    internal const ushort EdgeExpansionBank = 0xC1AB;
    internal const ushort DiagonalTargetXLow = 0xC1AC;
    internal const ushort DiagonalTargetXHigh = 0xC1AD;
    internal const ushort DiagonalTargetYLow = 0xC1AE;
    internal const ushort DiagonalTargetYHigh = 0xC1AF;
    internal const ushort DiagonalPreferredPreparationAxis = 0xC1B0;
    internal const ushort DiagonalPreparedAxis = 0xC1B1;
    internal const ushort DiagonalNextPreparationAxis = 0xC1B2;
    internal const ushort VisualCache2Valid = 0xC1B3;
    internal const ushort VisualCache2ChunkLow = 0xC1B4;
    internal const ushort VisualCache2ChunkHigh = 0xC1B5;
    internal const ushort VisualCache3Valid = 0xC1B6;
    internal const ushort VisualCache3ChunkLow = 0xC1B7;
    internal const ushort VisualCache3ChunkHigh = 0xC1B8;
    internal const ushort VisualCache4Valid = 0xC1B9;
    internal const ushort VisualCache4ChunkLow = 0xC1BA;
    internal const ushort VisualCache4ChunkHigh = 0xC1BB;
    internal const ushort VisualCache5Valid = 0xC1BC;
    internal const ushort VisualCache5ChunkLow = 0xC1BD;
    internal const ushort VisualCache5ChunkHigh = 0xC1BE;
    internal const ushort VisualCache0Age = 0xC1BF;
    internal const ushort VisualCache1Age = 0xC1C0;
    internal const ushort VisualCache2Age = 0xC1C1;
    internal const ushort VisualCache3Age = 0xC1C2;
    internal const ushort VisualCache4Age = 0xC1C3;
    internal const ushort VisualCache5Age = 0xC1C4;
    internal const ushort VisualPendingRowGroupLow = 0xC1C5;
    internal const ushort VisualPendingRowGroupHigh = 0xC1C6;
    internal const ushort VisualPendingColumnGroupLow = 0xC1C7;
    internal const ushort VisualPendingColumnGroupHigh = 0xC1C8;
    internal const ushort VisualLastColumnGroupValid = 0xC1C9;
    internal const ushort VisualLastColumnGroupLow = 0xC1CA;
    internal const ushort VisualLastColumnGroupHigh = 0xC1CB;
    internal const ushort VisualLastRowGroupValid = 0xC1CC;
    internal const ushort VisualLastRowGroupLow = 0xC1CD;
    internal const ushort VisualLastRowGroupHigh = 0xC1CE;
    internal const ushort VisualCacheRowGroupLowStart = 0xC1CF;
    internal const ushort VisualCacheRowGroupHighStart = 0xC1D5;
    internal const ushort VisualCacheColumnGroupLowStart = 0xC1DB;
    internal const ushort VisualCacheColumnGroupHighStart = 0xC1E1;
    internal const ushort DiagonalTargetFresh = 0xC1E7;
    internal const ushort CommitEnteredVBlank = 0xC1E8;
    internal const ushort DirectoryWorkInCommit = 0xC1E9;
    internal const ushort DecodeWorkInVBlank = 0xC1EA;
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

    internal static void EmitWaitIfInVBlank(GbBuilder builder)
    {
        var safe = builder.CreateLabel("packed_world_decode_element_safe");
        builder.LoadHighRamA(0x44); // LY
        builder.CompareImmediate(143);
        builder.JumpAbsolute(0xDA, safe);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitIfInVBlankLabel);
        builder.Label(safe);
    }

    internal static void EmitGuardCriticalWork(GbBuilder builder, ushort counterAddress)
    {
        EmitWaitOutsideVBlank(builder);
        EmitRecordCriticalWork(builder, counterAddress);
    }

    internal static void EmitWaitOutsideVBlankRoutine(GbBuilder builder)
    {
        const byte safeActiveScanlineEnd = 128;
        var lcdDisabled = builder.CreateLabel("packed_world_lcd_disabled");
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
        builder.Emit(0xF5); // PUSH AF; the guard is transparent to callers when LCD timing is inactive.
        builder.LoadHighRamA(0x40); // LCDC
        builder.AndImmediate(0x80);
        builder.JumpAbsolute(0xCA, lcdDisabled);
        builder.Emit(0xF1); // POP AF
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
        builder.Label(lcdDisabled);
        builder.Emit(0xF1); // POP AF
        builder.Emit(0xC9); // RET
    }

    internal static void EmitObserveFrameWrapRoutine(GbBuilder builder)
    {
        var initialize = builder.CreateLabel("packed_world_observe_initialize");
        var store = builder.CreateLabel("packed_world_observe_store");
        var done = builder.CreateLabel("packed_world_observe_done");
        builder.Label(GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
        builder.Emit(0xF5); // PUSH AF
        builder.Emit(0xC5); // PUSH BC
        builder.LoadA(GameBoyPackedCameraRuntime.WaitAudioEnabled);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadHighRamA(0x40); // LCDC
        builder.AndImmediate(0x80);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadHighRamA(0x44); // LY
        builder.StoreA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.LoadA(GameBoyPackedCameraRuntime.LastObservedLyValid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, initialize);
        builder.LoadA(GameBoyPackedCameraRuntime.LastObservedLy);
        builder.LoadBFromA();
        builder.LoadA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.CompareB();
        builder.JumpAbsolute(0xD2, store); // JP NC: LY has not wrapped.
        builder.LoadA(GameBoyPackedCameraRuntime.WaitAudioTicked);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, store);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
        builder.JumpAbsolute(store);
        builder.Label(initialize);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyPackedCameraRuntime.LastObservedLyValid);
        builder.Label(store);
        builder.LoadA(GameBoyPackedCameraRuntime.CurrentLy);
        builder.StoreA(GameBoyPackedCameraRuntime.LastObservedLy);
        builder.Label(done);
        builder.Emit(0xC1); // POP BC
        builder.Emit(0xF1); // POP AF
        builder.Emit(0xC9); // RET
    }

    internal static void EmitWaitIfInVBlankRoutine(GbBuilder builder, bool enablePackedAudioService)
    {
        var wait = builder.CreateLabel("packed_world_wait_while_in_vblank");
        var done = builder.CreateLabel("packed_world_wait_if_in_vblank_done");
        builder.Label(GameBoyRomBuilder.WorldPackWaitIfInVBlankLabel);
        builder.Emit(0xF5); // PUSH AF
        builder.LoadHighRamA(0x40); // LCDC
        builder.AndImmediate(0x80);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadHighRamA(0x44); // LY
        builder.CompareImmediate(143); // Leave one scanline for the current decode element.
        builder.JumpAbsolute(0xDA, done);
        builder.Label(wait);
        builder.LoadHighRamA(0x44);
        builder.CompareImmediate(144);
        builder.JumpAbsolute(0xD2, wait);
        if (enablePackedAudioService)
        {
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackObserveFrameWrapLabel);
        }

        builder.Label(done);
        builder.Emit(0xF1); // POP AF
        builder.Emit(0xC9); // RET
    }

    internal static void EmitRecordCriticalWork(GbBuilder builder, ushort counterAddress)
    {
        var done = builder.CreateLabel("packed_camera_critical_work_done");
        var inspectVBlank = builder.CreateLabel("packed_camera_critical_work_inspect_vblank");
        var commitCounter = counterAddress == GameBoyPackedCameraRuntime.DirectoryWorkInVBlank
            ? GameBoyPackedCameraRuntime.DirectoryWorkInCommit
            : counterAddress;
        var vblankCounter = counterAddress == GameBoyPackedCameraRuntime.DecodeWorkInCommit
            ? GameBoyPackedCameraRuntime.DecodeWorkInVBlank
            : counterAddress;
        builder.Emit(0xF5); // PUSH AF
        builder.LoadA(GameBoyPackedCameraRuntime.CriticalSection);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, inspectVBlank);
        builder.LoadA(commitCounter);
        builder.AddAImmediate(1);
        builder.StoreA(commitCounter);
        builder.JumpAbsolute(done);
        builder.Label(inspectVBlank);
        builder.LoadHighRamA(0x44);
        builder.CompareImmediate(144);
        builder.JumpAbsolute(0xDA, done);
        builder.LoadA(vblankCounter);
        builder.AddAImmediate(1);
        builder.StoreA(vblankCounter);
        builder.Label(done);
        builder.Emit(0xF1); // POP AF
    }
}
