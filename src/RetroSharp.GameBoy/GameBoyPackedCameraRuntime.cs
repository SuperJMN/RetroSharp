namespace RetroSharp.GameBoy;

internal static class GameBoyPackedCameraRuntime
{
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
        0 => GameBoyRuntimeMemoryLayout.PackedCamera.Slot0,
        1 => GameBoyRuntimeMemoryLayout.PackedCamera.Slot1,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}

internal static class GameBoyPackedCameraRuntimeEmitter
{
    private const byte SafeActiveScanlineEnd = 128;

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

    internal static void EmitGuardRleCriticalWork(GbBuilder builder, ushort counterAddress)
    {
        EmitWaitForSafeRleCriticalWork(builder);
        EmitRecordCriticalWork(builder, counterAddress);
    }

    internal static void EmitWaitForSafeRleCriticalWork(GbBuilder builder)
    {
        var wait = builder.CreateLabel("packed_world_critical_guard_wait");
        var done = builder.CreateLabel("packed_world_critical_guard_done");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioEnabled);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, wait); // JP NZ,wait: the full guard observes frame wraps for audio.
        builder.LoadHighRamA(0x40); // LCDC
        builder.AndImmediate(0x80);
        builder.JumpAbsolute(0xCA, done); // JP Z,done when LCD timing is inactive.
        builder.LoadHighRamA(0x44); // LY
        builder.CompareImmediate(SafeActiveScanlineEnd);
        builder.JumpAbsolute(0xDA, done); // JP C,done on the safe active-display fast path.
        builder.Label(wait);
        EmitWaitOutsideVBlank(builder);
        builder.Label(done);
    }

    internal static void EmitWaitOutsideVBlankRoutine(GbBuilder builder)
    {
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
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLyValid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, firstObservation);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLy);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.CompareB();
        builder.JumpAbsolute(0xDA, wrapped); // JP C: current LY wrapped below the previous observation.
        builder.JumpAbsolute(classify);

        builder.Label(firstObservation);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLyValid);
        builder.JumpAbsolute(classify);

        builder.Label(wrapped);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioEnabled);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, wrappedWithoutAudio);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioTicked);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, wrappedWithoutAudio);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
        builder.Label(wrappedWithoutAudio);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioTicked);

        builder.Label(classify);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLy);
        builder.CompareImmediate(SafeActiveScanlineEnd);
        builder.JumpAbsolute(0xDA, safeActive); // JP C,safeActive

        builder.Label(waitForVBlank);
        builder.LoadHighRamA(0x44);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.CompareImmediate(144);
        builder.JumpAbsolute(0xDA, waitForVBlank); // LY 136-143: wait for this frame's VBlank.
        builder.Label(inVBlank);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioEnabled);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, waitForSafeActive);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioTicked);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, audioAlreadyTicked);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioTicked);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
        builder.Label(audioAlreadyTicked);
        builder.Label(waitForSafeActive);
        builder.LoadHighRamA(0x44);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.CompareImmediate(SafeActiveScanlineEnd);
        builder.JumpAbsolute(0xD2, waitForSafeActive); // Wait through VBlank and the guard band.
        builder.Label(safeActive);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLy);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioTicked);
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
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioEnabled);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadHighRamA(0x40); // LCDC
        builder.AndImmediate(0x80);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadHighRamA(0x44); // LY
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLyValid);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, initialize);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLy);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.CompareB();
        builder.JumpAbsolute(0xD2, store); // JP NC: LY has not wrapped.
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioTicked);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, store);
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
        builder.JumpAbsolute(store);
        builder.Label(initialize);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLyValid);
        builder.Label(store);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CurrentLy);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.LastObservedLy);
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
        var commitCounter = counterAddress == GameBoyRuntimeMemoryLayout.PackedCamera.DirectoryWorkInVBlank
            ? GameBoyRuntimeMemoryLayout.PackedCamera.DirectoryWorkInCommit
            : counterAddress;
        var vblankCounter = counterAddress == GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit
            ? GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInVBlank
            : counterAddress;
        builder.Emit(0xF5); // PUSH AF
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CriticalSection);
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
