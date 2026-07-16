namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    private void EmitApplyPackedCamera(GameBoyCameraConfig config)
    {
        if (SerializePackedDiagonalPreparation && ProgramQueuesDiagonalStreaming())
        {
            EmitResumePackedDiagonalPreparation(config);
        }

        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitSucceeded);
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitApplyPackedDiagonalCamera(config);
        }
        else
        {
            var commitRow = builder.CreateLabel("packed_camera_apply_row");
            var nothingPending = builder.CreateLabel("packed_camera_apply_nothing_pending");
            var publish = builder.CreateLabel("packed_camera_apply_publish");
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind);
            builder.CompareImmediate(PendingStreamNone);
            builder.JumpAbsolute(0xCA, nothingPending);
            builder.CompareImmediate(PendingStreamRow);
            builder.JumpAbsolute(0xCA, commitRow);
            EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh);
            EmitCommitPackedPendingEdge(
                GameBoyPackedCameraRuntime.Column,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSourceHigh,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSourceHigh,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingSlot,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondSlot,
                config);
            builder.JumpAbsolute(publish);

            builder.Label(commitRow);
            EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh);
            EmitCommitPackedPendingEdge(
                GameBoyPackedCameraRuntime.Row,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSource,
                null,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSource,
                null,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondDirection,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingSlot,
                GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondSlot,
                config);
            builder.JumpAbsolute(publish);

            builder.Label(nothingPending);
            EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh);
            EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh);
            builder.Label(publish);
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow);
        builder.StoreHighRamA(0x43);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow);
        builder.StoreHighRamA(0x42);
    }

    private void EmitResumePackedDiagonalPreparation(GameBoyCameraConfig config)
    {
        var resume = builder.CreateLabel("packed_diagonal_resume_preparation");
        var done = builder.CreateLabel("packed_diagonal_resume_done");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetFresh);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, resume);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetFresh);
        builder.JumpAbsolute(done);

        builder.Label(resume);
        builder.XorA();
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreparedAxis);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalNextPreparationAxis);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreferredPreparationAxis);
        EmitCameraSetAxisPosition(
            GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetXLow,
            GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetXHigh,
            GameBoyRuntimeMemoryLayout.Camera.XLow,
            GameBoyRuntimeMemoryLayout.Camera.XHigh,
            () => EmitCameraMoveLeftStep(config),
            () => EmitCameraMoveRightStep(config),
            "camera_resume_position_right",
            "camera_resume_position_x_end");
        EmitCameraSetAxisPosition(
            GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetYLow,
            GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetYHigh,
            GameBoyRuntimeMemoryLayout.Camera.YLow,
            GameBoyRuntimeMemoryLayout.Camera.YHigh,
            () => EmitCameraMoveUpStep(config),
            () => EmitCameraMoveDownStep(config),
            "camera_resume_position_down",
            "camera_resume_position_y_end");
        EmitAdvancePackedDiagonalPreparationAxis();
        builder.Label(done);
    }

    private void EmitApplyPackedDiagonalCamera(GameBoyCameraConfig config)
    {
        var select = builder.CreateLabel("packed_camera_diagonal_select");
        var checkRow = builder.CreateLabel("packed_camera_diagonal_check_row");
        var commitColumn = builder.CreateLabel("packed_camera_diagonal_commit_column");
        var commitRow = builder.CreateLabel("packed_camera_diagonal_commit_row");
        var columnVisibilityDone = builder.CreateLabel("packed_camera_diagonal_column_visibility_done");
        var rowVisibilityDone = builder.CreateLabel("packed_camera_diagonal_row_visibility_done");
        var done = builder.CreateLabel("packed_camera_diagonal_apply_done");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xC2, columnVisibilityDone);
        EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh);
        builder.Label(columnVisibilityDone);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xC2, rowVisibilityDone);
        EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh);
        builder.Label(rowVisibilityDone);

        builder.Label(select);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, checkRow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, commitColumn);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalNextStreamKind);
        builder.CompareImmediate(PendingStreamRow);
        builder.JumpAbsolute(0xCA, commitRow);

        builder.Label(commitColumn);
        EmitCommitPackedPendingEdge(
            GameBoyPackedCameraRuntime.Column,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnCount,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSourceHigh,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSourceHigh,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSecondDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSlot,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSecondSlot,
            config);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitSucceeded);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadAImmediate(PendingStreamRow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalNextStreamKind);
        builder.JumpAbsolute(done);

        builder.Label(checkRow);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, done);

        builder.Label(commitRow);
        EmitCommitPackedPendingEdge(
            GameBoyPackedCameraRuntime.Row,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowCount,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSource,
            null,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondSource,
            null,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSecondDirection,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSlot,
            GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSecondSlot,
            config);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitSucceeded);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadAImmediate(PendingStreamColumn);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalNextStreamKind);
        builder.Label(done);
    }

    private void EmitCommitPackedPendingEdge(
        byte axis,
        ushort kindAddress,
        ushort countAddress,
        ushort targetAddress,
        ushort sourceAddress,
        ushort? sourceHighAddress,
        ushort secondTargetAddress,
        ushort secondSourceAddress,
        ushort? secondSourceHighAddress,
        ushort directionAddress,
        ushort secondDirectionAddress,
        ushort slotAddress,
        ushort secondSlotAddress,
        GameBoyCameraConfig config)
    {
        var validateSlot0 = builder.CreateLabel("packed_camera_commit_validate_slot_0");
        var validateSlot1 = builder.CreateLabel("packed_camera_commit_validate_slot_1");
        var commit = builder.CreateLabel("packed_camera_commit_edge");
        var invalid = builder.CreateLabel("packed_camera_commit_invalid");
        var single = builder.CreateLabel("packed_camera_commit_single");
        var publishCurrent = builder.CreateLabel("packed_camera_commit_publish_current");
        var release = builder.CreateLabel("packed_camera_commit_release");
        var done = builder.CreateLabel("packed_camera_commit_done");

        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitSucceeded);

        builder.LoadA(slotAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, validateSlot0);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, validateSlot1);
        builder.JumpAbsolute(invalid);

        builder.Label(validateSlot0);
        EmitValidatePackedSlot(
            GameBoyRuntimeMemoryLayout.PackedCamera.Slot0,
            packedWorldRuntimeLayout!.EdgeSlots[0],
            0,
            axis,
            targetAddress,
            sourceAddress,
            sourceHighAddress,
            directionAddress,
            commit,
            invalid);

        builder.Label(validateSlot1);
        EmitValidatePackedSlot(
            GameBoyRuntimeMemoryLayout.PackedCamera.Slot1,
            packedWorldRuntimeLayout!.EdgeSlots[1],
            1,
            axis,
            targetAddress,
            sourceAddress,
            sourceHighAddress,
            directionAddress,
            commit,
            invalid);

        builder.Label(commit);
        EmitCopyByte(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow, GameBoyRuntimeMemoryLayout.PackedCamera.LastCommittedWorldEdgeLow);
        EmitCopyByte(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh, GameBoyRuntimeMemoryLayout.PackedCamera.LastCommittedWorldEdgeHigh);
        EmitCopyByte(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis, GameBoyRuntimeMemoryLayout.PackedCamera.LastCommittedAxis);
        EmitCopyByte(GameBoyRuntimeMemoryLayout.PackedCamera.CommitDirection, GameBoyRuntimeMemoryLayout.PackedCamera.LastCommittedDirection);
        EmitSetPackedSelectedSlotState(GameBoyPackedCameraRuntime.Committing);
        EmitIncrementPackedCounter(GameBoyRuntimeMemoryLayout.PackedCamera.CommitCount);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.LastCommitVramWrites);
        GameBoyRomBuilder.EmitEnterVBlank(builder);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CriticalSection);
        EmitCopyPackedEdgeToVram(axis);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CriticalSection);

        builder.LoadA(countAddress);
        builder.CompareImmediate(PendingStreamDouble);
        builder.JumpAbsolute(0xC2, single);
        EmitCopyByte(secondTargetAddress, targetAddress);
        EmitCopyByte(secondSourceAddress, sourceAddress);
        if (sourceHighAddress is { } firstHigh && secondSourceHighAddress is { } secondHigh)
        {
            EmitCopyByte(secondHigh, firstHigh);
        }

        EmitCopyByte(secondDirectionAddress, directionAddress);
        EmitCopyByte(secondSlotAddress, slotAddress);
        builder.LoadAImmediate(PendingStreamSingle);
        builder.StoreA(countAddress);
        EmitPublishCommittedCrossing(axis, config);
        builder.JumpAbsolute(release);

        builder.Label(single);
        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(kindAddress);
        builder.StoreA(countAddress);
        builder.Label(publishCurrent);
        if (axis == GameBoyPackedCameraRuntime.Column)
        {
            EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh);
        }
        else
        {
            EmitCopyWord(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow, GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh);
        }

        builder.JumpAbsolute(release);
        builder.Label(release);
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitSucceeded);
        EmitSetPackedSelectedSlotState(GameBoyPackedCameraRuntime.Released);
        EmitIncrementPackedCounter(GameBoyRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        builder.JumpAbsolute(done);
        builder.Label(invalid);
        builder.Label(done);
    }

    private void EmitValidatePackedSlot(
        ushort metadata,
        GameBoyWramRange payload,
        byte slot,
        byte expectedAxis,
        ushort targetAddress,
        ushort sourceAddress,
        ushort? sourceHighAddress,
        ushort directionAddress,
        string valid,
        string invalid)
    {
        EmitCompareAddressToImmediate(metadata + GameBoyPackedCameraRuntime.StateOffset, GameBoyPackedCameraRuntime.Resident, invalid);
        EmitCompareAddressToImmediate(metadata + GameBoyPackedCameraRuntime.AxisOffset, expectedAxis, invalid);
        EmitCompareAddresses(metadata + GameBoyPackedCameraRuntime.DirectionOffset, directionAddress, invalid);
        EmitCompareAddresses(metadata + GameBoyPackedCameraRuntime.WorldEdgeLowOffset, sourceAddress, invalid);
        if (sourceHighAddress is { } high)
        {
            EmitCompareAddresses(metadata + GameBoyPackedCameraRuntime.WorldEdgeHighOffset, high, invalid);
        }
        else
        {
            EmitCompareAddressToImmediate(metadata + GameBoyPackedCameraRuntime.WorldEdgeHighOffset, 0, invalid);
        }

        EmitCompareAddresses(metadata + GameBoyPackedCameraRuntime.TargetOffset, targetAddress, invalid);
        builder.LoadAImmediate(slot);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.LoadDe(payload.Start);
        EmitCopyByte(checked((ushort)(metadata + GameBoyPackedCameraRuntime.TargetOffset)), GameBoyRuntimeMemoryLayout.PackedCamera.CommitTarget);
        EmitCopyByte(checked((ushort)(metadata + GameBoyPackedCameraRuntime.TargetStartOffset)), GameBoyRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
        EmitCopyByte(checked((ushort)(metadata + GameBoyPackedCameraRuntime.WorldEdgeLowOffset)), GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        EmitCopyByte(checked((ushort)(metadata + GameBoyPackedCameraRuntime.WorldEdgeHighOffset)), GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        EmitCopyByte(checked((ushort)(metadata + GameBoyPackedCameraRuntime.DirectionOffset)), GameBoyRuntimeMemoryLayout.PackedCamera.CommitDirection);
        builder.LoadAImmediate(expectedAxis);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitAxis);
        builder.JumpAbsolute(valid);
    }

    private void EmitCopyPackedEdgeToVram(byte axis)
    {
        var loop = builder.CreateLabel("packed_camera_commit_copy_loop");
        if (axis == GameBoyPackedCameraRuntime.Column)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitTarget);
            builder.LoadCFromA();
            EmitBackgroundTileAddressToHl(GameBoyRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
            builder.Emit(0x06, 19); // LD B,19
            var noCarry = builder.CreateLabel("packed_camera_column_target_no_carry");
            var noWrap = builder.CreateLabel("packed_camera_column_target_no_wrap");
            builder.Label(loop);
            builder.Emit(0x1A); // LD A,(DE)
            builder.StoreHlA();
            builder.Emit(0x13); // INC DE
            builder.LoadAFromL();
            builder.AddAImmediate(32);
            builder.LoadLFromA();
            builder.JumpAbsolute(0xD2, noCarry);
            builder.Emit(0x24); // INC H
            builder.LoadAFromH();
            builder.CompareImmediate(0x9C);
            builder.JumpAbsolute(0xC2, noWrap);
            builder.LoadHImmediate(0x98);
            builder.Label(noWrap);
            builder.Label(noCarry);
            builder.Emit(0x05); // DEC B
            builder.JumpAbsolute(0xC2, loop);
            builder.LoadAImmediate(19);
        }
        else
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitTargetStart);
            builder.LoadCFromA();
            EmitBackgroundTileAddressToHl(GameBoyRuntimeMemoryLayout.PackedCamera.CommitTarget);
            builder.Emit(0x06, 21); // LD B,21
            var wrap = builder.CreateLabel("packed_camera_row_target_wrap");
            var next = builder.CreateLabel("packed_camera_row_target_next");
            builder.Label(loop);
            builder.Emit(0x1A); // LD A,(DE)
            builder.Emit(0x22); // LD (HL+),A
            builder.Emit(0x13); // INC DE
            builder.Emit(0x0C); // INC C
            builder.Emit(0x79); // LD A,C
            builder.CompareImmediate(32);
            builder.JumpRelative(0x28, wrap); // JR Z,wrap
            builder.JumpRelative(0x18, next); // JR next
            builder.Label(wrap);
            builder.Emit(0x0E, 0x00); // LD C,0
            EmitBackgroundTileAddressToHl(GameBoyRuntimeMemoryLayout.PackedCamera.CommitTarget);
            builder.Label(next);
            builder.Emit(0x05); // DEC B
            builder.JumpRelative(0x20, loop); // JR NZ,loop
            builder.LoadAImmediate(21);
        }

        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.LastCommitVramWrites);
    }

    private void EmitPublishCommittedCrossing(byte axis, GameBoyCameraConfig config)
    {
        var negative = builder.CreateLabel("packed_camera_publish_negative_crossing");
        var store = builder.CreateLabel("packed_camera_publish_crossing_store");
        var visibleLow = axis == GameBoyPackedCameraRuntime.Column
            ? GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow
            : GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow;
        var visibleHigh = axis == GameBoyPackedCameraRuntime.Column
            ? GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh
            : GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh;

        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitDirection);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Negative);
        builder.JumpAbsolute(0xCA, negative);
        EmitLoadCommitWorldEdgeToHl();
        var lookahead = axis == GameBoyPackedCameraRuntime.Column ? 20 : 18;
        var modulo = axis == GameBoyPackedCameraRuntime.Column ? config.MapWidth : config.SourceHeight;
        EmitSubtractHlModuloConstant(lookahead, modulo);
        for (var index = 0; index < 3; index++)
        {
            builder.Emit(0x29); // ADD HL,HL
        }

        builder.JumpAbsolute(store);
        builder.Label(negative);
        EmitLoadCommitWorldEdgeToHl();
        for (var index = 0; index < 3; index++)
        {
            builder.Emit(0x29); // ADD HL,HL
        }

        builder.LoadBc(7);
        builder.AddHlBc();
        builder.Label(store);
        builder.LoadAFromL();
        builder.StoreA(visibleLow);
        builder.LoadAFromH();
        builder.StoreA(visibleHigh);
    }

    private void EmitLoadCommitWorldEdgeToHl()
    {
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh);
        builder.LoadHFromA();
    }

    private void EmitSubtractHlModuloConstant(int value, int modulo)
    {
        var noWrap = builder.CreateLabel("subtract_word_modulo_no_wrap");

        builder.LoadAFromL();
        builder.SubtractAImmediate(value & 0xFF);
        builder.LoadLFromA();
        builder.LoadAFromH();
        builder.SbcAImmediate((value >> 8) & 0xFF);
        builder.LoadHFromA();
        builder.JumpAbsolute(0xD2, noWrap); // JP NC,noWrap
        builder.LoadBc(checked((ushort)modulo));
        builder.AddHlBc();
        builder.Label(noWrap);
    }

    private void EmitSetPackedSelectedSlotState(byte state)
    {
        var slot1 = builder.CreateLabel("packed_camera_set_state_slot_1");
        var done = builder.CreateLabel("packed_camera_set_state_done");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.LoadAImmediate(state);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.Slot0 + GameBoyPackedCameraRuntime.StateOffset);
        builder.JumpAbsolute(done);
        builder.Label(slot1);
        builder.LoadAImmediate(state);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.Slot1 + GameBoyPackedCameraRuntime.StateOffset);
        builder.Label(done);
    }

    private void EmitIncrementPackedCounter(ushort address)
    {
        builder.LoadA(address);
        builder.AddAImmediate(1);
        builder.StoreA(address);
    }

    private void EmitCompareAddressToImmediate(int address, byte expected, string mismatch)
    {
        builder.LoadA(checked((ushort)address));
        builder.CompareImmediate(expected);
        builder.JumpAbsolute(0xC2, mismatch);
    }

    private void EmitCompareAddresses(int left, ushort right, string mismatch)
    {
        builder.LoadA(right);
        builder.LoadBFromA();
        builder.LoadA(checked((ushort)left));
        builder.CompareB();
        builder.JumpAbsolute(0xC2, mismatch);
    }

    private void EmitCopyWord(ushort sourceLow, ushort sourceHigh, ushort targetLow, ushort targetHigh)
    {
        EmitCopyByte(sourceLow, targetLow);
        EmitCopyByte(sourceHigh, targetHigh);
    }

    private void EmitCommitPendingStream(GameBoyCameraConfig config)
    {
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitCommitStaggeredPendingStream(config);
            return;
        }

        // Rows are only ever queued by camera movement with a non-zero Y (the up/down move steps
        // are unreachable otherwise), so only emit the large row streamer when the program can
        // actually scroll vertically. The column streamer is small and always emitted.
        var emitRowCommit = ProgramQueuesRowStreaming();

        var rowLabel = builder.CreateLabel("camera_commit_row");
        var clearLabel = builder.CreateLabel("camera_commit_clear");
        var doneLabel = builder.CreateLabel("camera_commit_done");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, doneLabel);      // JP Z,doneLabel: nothing queued
        if (emitRowCommit)
        {
            builder.CompareImmediate(PendingStreamRow);
            builder.JumpAbsolute(0xCA, rowLabel);   // JP Z,rowLabel
        }

        // Column crossing: stream the queued background and visible-world column(s).
        EmitCommitPendingColumnSlots(
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSourceHigh,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSourceHigh,
            config);

        if (emitRowCommit)
        {
            builder.JumpAbsolute(clearLabel);
            builder.Label(rowLabel);
            EmitCommitPendingRowSlots(
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSource,
                config);
            builder.Label(clearLabel);
        }

        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount);

        builder.Label(doneLabel);
    }

    private void EmitCommitStaggeredPendingStream(GameBoyCameraConfig config)
    {
        var checkRowLabel = builder.CreateLabel("camera_commit_check_row");
        var commitColumnLabel = builder.CreateLabel("camera_commit_column");
        var commitRowLabel = builder.CreateLabel("camera_commit_row");
        var doneLabel = builder.CreateLabel("camera_commit_done");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, checkRowLabel); // JP Z,checkRowLabel
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, commitColumnLabel); // JP Z,commitColumnLabel
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalNextStreamKind);
        builder.CompareImmediate(PendingStreamRow);
        builder.JumpAbsolute(0xCA, commitRowLabel); // JP Z,commitRowLabel

        builder.Label(commitColumnLabel);
        EmitCommitPendingColumnSlots(
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnCount,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSourceHigh,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSourceHigh,
            config);
        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnCount);
        builder.LoadAImmediate(PendingStreamRow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalNextStreamKind);
        builder.JumpAbsolute(doneLabel);

        builder.Label(checkRowLabel);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, doneLabel); // JP Z,doneLabel

        builder.Label(commitRowLabel);
        EmitCommitPendingRowSlots(
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowCount,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSource,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondTarget,
            GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondSource,
            config);
        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowCount);
        builder.LoadAImmediate(PendingStreamColumn);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalNextStreamKind);

        builder.Label(doneLabel);
    }

    private void EmitCommitPendingColumnSlots(
        ushort pendingCountAddress,
        ushort pendingTargetAddress,
        ushort pendingSourceAddress,
        ushort pendingSourceHighAddress,
        ushort pendingSecondTargetAddress,
        ushort pendingSecondSourceAddress,
        ushort pendingSecondSourceHighAddress,
        GameBoyCameraConfig config)
    {
        EmitVisibleWorldStreamColumnFromAddresses(pendingTargetAddress, pendingSourceAddress, pendingSourceHighAddress, config);
        EmitBackgroundStreamColumnFromAddresses(pendingTargetAddress, pendingSourceAddress, pendingSourceHighAddress, config);

        var doneLabel = builder.CreateLabel("camera_commit_second_column_done");
        builder.LoadA(pendingCountAddress);
        builder.CompareImmediate(PendingStreamDouble);
        builder.JumpAbsolute(0xC2, doneLabel); // JP NZ,doneLabel

        EmitVisibleWorldStreamColumnFromAddresses(pendingSecondTargetAddress, pendingSecondSourceAddress, pendingSecondSourceHighAddress, config);
        EmitBackgroundStreamColumnFromAddresses(pendingSecondTargetAddress, pendingSecondSourceAddress, pendingSecondSourceHighAddress, config);

        builder.Label(doneLabel);
    }

    private void EmitCommitPendingRowSlots(
        ushort pendingCountAddress,
        ushort pendingTargetAddress,
        ushort pendingSourceAddress,
        ushort pendingSecondTargetAddress,
        ushort pendingSecondSourceAddress,
        GameBoyCameraConfig config)
    {
        EmitMapStreamRowFromSourceRowAddress(pendingTargetAddress, pendingSourceAddress, config);

        var doneLabel = builder.CreateLabel("camera_commit_second_row_done");
        builder.LoadA(pendingCountAddress);
        builder.CompareImmediate(PendingStreamDouble);
        builder.JumpAbsolute(0xC2, doneLabel); // JP NZ,doneLabel

        EmitMapStreamRowFromSourceRowAddress(pendingSecondTargetAddress, pendingSecondSourceAddress, config);

        builder.Label(doneLabel);
    }
}
