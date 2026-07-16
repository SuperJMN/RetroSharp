namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    private bool ProgramQueuesRowStreaming()
    {
        foreach (var operation in program.SdkOperations)
        {
            if (operation is Sdk2DOperation.SetCameraPosition position
                && position.Axes.HasFlag(ScrollAxes.Vertical))
            {
                return true;
            }
        }

        return false;
    }

    private bool ProgramQueuesDiagonalStreaming()
    {
        foreach (var operation in program.SdkOperations)
        {
            if (operation is Sdk2DOperation.SetCameraPosition position
                && position.Axes.HasFlag(ScrollAxes.Horizontal)
                && position.Axes.HasFlag(ScrollAxes.Vertical))
            {
                return true;
            }
        }

        return false;
    }

    private void EmitQueuePendingColumn(ushort backgroundColumnAddress, ushort sourceColumnAddress)
    {
        var sourceHighAddress = state.CameraMapWidth > byte.MaxValue
            ? GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn == sourceColumnAddress
                ? GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh
                : GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh
            : (ushort?)null;
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitQueuePendingStaggeredStream(
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnCount,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSource,
                backgroundColumnAddress,
                sourceColumnAddress,
                sourceHighAddress,
                sourceHighAddress is null ? null : GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSourceHigh,
                sourceHighAddress is null ? null : GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnSecondSourceHigh);
            return;
        }

        EmitQueuePendingStream(
            PendingStreamColumn,
            backgroundColumnAddress,
            sourceColumnAddress,
            sourceHighAddress,
            sourceHighAddress is null ? null : GameBoyRuntimeMemoryLayout.Camera.PendingStreamSourceHigh,
            sourceHighAddress is null ? null : GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSourceHigh);
    }

    private void EmitQueuePendingRow(ushort backgroundRowAddress, ushort sourceRowAddress)
    {
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitQueuePendingStaggeredStream(
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowCount,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSource,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondTarget,
                GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowSecondSource,
                backgroundRowAddress,
                sourceRowAddress,
                null,
                null,
                null);
            return;
        }

        EmitQueuePendingStream(PendingStreamRow, backgroundRowAddress, sourceRowAddress, null, null, null);
    }

    private void EmitQueuePendingStream(
        byte kind,
        ushort targetAddress,
        ushort sourceAddress,
        ushort? sourceHighAddress,
        ushort? pendingSourceHighAddress,
        ushort? pendingSecondSourceHighAddress)
    {
        var storeFirstLabel = builder.CreateLabel("camera_queue_store_first");
        var storeSecondLabel = builder.CreateLabel("camera_queue_store_second");
        var doneLabel = builder.CreateLabel("camera_queue_done");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, storeFirstLabel); // JP Z,storeFirstLabel
        builder.CompareImmediate(kind);
        builder.JumpAbsolute(0xC2, storeFirstLabel); // JP NZ,storeFirstLabel

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount);
        builder.CompareImmediate(PendingStreamSingle);
        builder.JumpAbsolute(0xCA, storeSecondLabel); // JP Z,storeSecondLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeFirstLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamTarget);
        builder.LoadA(sourceAddress);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamSource);
        EmitCopyOptionalHighByte(sourceHighAddress, pendingSourceHighAddress);
        builder.LoadAImmediate(kind);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind);
        builder.LoadAImmediate(PendingStreamSingle);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount);
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeSecondLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondTarget);
        builder.LoadA(sourceAddress);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamSecondSource);
        EmitCopyOptionalHighByte(sourceHighAddress, pendingSecondSourceHighAddress);
        builder.LoadAImmediate(PendingStreamDouble);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount);

        builder.Label(doneLabel);
    }

    private void EmitQueuePendingStaggeredStream(
        ushort pendingKindAddress,
        ushort pendingCountAddress,
        ushort pendingTargetAddress,
        ushort pendingSourceAddress,
        ushort pendingSecondTargetAddress,
        ushort pendingSecondSourceAddress,
        ushort targetAddress,
        ushort sourceAddress,
        ushort? sourceHighAddress,
        ushort? pendingSourceHighAddress,
        ushort? pendingSecondSourceHighAddress)
    {
        var storeFirstLabel = builder.CreateLabel("camera_queue_staggered_store_first");
        var storeSecondLabel = builder.CreateLabel("camera_queue_staggered_store_second");
        var doneLabel = builder.CreateLabel("camera_queue_staggered_done");

        builder.LoadA(pendingKindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, storeFirstLabel); // JP Z,storeFirstLabel
        builder.LoadA(pendingCountAddress);
        builder.CompareImmediate(PendingStreamSingle);
        builder.JumpAbsolute(0xCA, storeSecondLabel); // JP Z,storeSecondLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeFirstLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(pendingTargetAddress);
        builder.LoadA(sourceAddress);
        builder.StoreA(pendingSourceAddress);
        EmitCopyOptionalHighByte(sourceHighAddress, pendingSourceHighAddress);
        builder.LoadAImmediate(PendingStreamQueued);
        builder.StoreA(pendingKindAddress);
        builder.LoadAImmediate(PendingStreamSingle);
        builder.StoreA(pendingCountAddress);
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeSecondLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(pendingSecondTargetAddress);
        builder.LoadA(sourceAddress);
        builder.StoreA(pendingSecondSourceAddress);
        EmitCopyOptionalHighByte(sourceHighAddress, pendingSecondSourceHighAddress);
        builder.LoadAImmediate(PendingStreamDouble);
        builder.StoreA(pendingCountAddress);

        builder.Label(doneLabel);
    }

    private void EmitCopyOptionalHighByte(ushort? sourceHighAddress, ushort? targetHighAddress)
    {
        if (sourceHighAddress is not { } source || targetHighAddress is not { } target)
        {
            return;
        }

        builder.LoadA(source);
        builder.StoreA(target);
    }

    private void EmitCameraSetAxisPosition(
        SdkWordExpression requestedPosition,
        ushort currentLowAddress,
        ushort currentHighAddress,
        Action moveNegative,
        Action movePositive,
        string positiveLabelName,
        string endLabelName)
    {
        var loopLabel = builder.CreateLabel(endLabelName + "_step");
        var movePositiveLabel = builder.CreateLabel(positiveLabelName);
        var moveNegativeLabel = builder.CreateLabel(endLabelName + "_negative");
        var endLabel = builder.CreateLabel(endLabelName);

        // Cache the requested absolute position and walk the camera toward it, one pixel per step, up
        // to the per-frame streaming budget. Reaching the target early exits the loop, so callers set
        // the desired position once per frame instead of stepping the camera manually.
        context.EmitWordExpressionToA(requestedPosition, highByte: false);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.SetPositionTarget);
        context.EmitWordExpressionToA(requestedPosition, highByte: true);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.SetPositionTargetHigh);
        EmitCameraSetAxisPosition(
            GameBoyRuntimeMemoryLayout.Camera.SetPositionTarget,
            GameBoyRuntimeMemoryLayout.Camera.SetPositionTargetHigh,
            currentLowAddress,
            currentHighAddress,
            moveNegative,
            movePositive,
            positiveLabelName,
            endLabelName);
    }

    private void EmitCameraSetAxisPosition(
        ushort targetLowAddress,
        ushort targetHighAddress,
        ushort currentLowAddress,
        ushort currentHighAddress,
        Action moveNegative,
        Action movePositive,
        string positiveLabelName,
        string endLabelName)
    {
        var loopLabel = builder.CreateLabel(endLabelName + "_step");
        var movePositiveLabel = builder.CreateLabel(positiveLabelName);
        var moveNegativeLabel = builder.CreateLabel(endLabelName + "_negative");
        var endLabel = builder.CreateLabel(endLabelName);

        builder.LoadAImmediate(CameraSetPositionMaxStepsPerFrame);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.SetPositionStepsRemaining);

        builder.Label(loopLabel);

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.SetPositionStepsRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel); // JP Z,endLabel: streaming budget spent
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.SetPositionStepsRemaining);

        builder.LoadA(targetHighAddress);
        builder.LoadBFromA();
        builder.LoadA(currentHighAddress);
        builder.CompareB();
        builder.JumpAbsolute(0xDA, movePositiveLabel); // JP C: current high < target high
        builder.JumpAbsolute(0xC2, moveNegativeLabel); // JP NZ: current high > target high

        builder.LoadA(targetLowAddress);
        builder.LoadBFromA();
        builder.LoadA(currentLowAddress);
        builder.CompareB();
        builder.JumpAbsolute(0xCA, endLabel); // JP Z,endLabel: reached target
        builder.JumpAbsolute(0xDA, movePositiveLabel); // JP C: current low < target low

        builder.Label(moveNegativeLabel);
        moveNegative();
        builder.JumpAbsolute(loopLabel);

        builder.Label(movePositiveLabel);
        movePositive();
        builder.JumpAbsolute(loopLabel);

        builder.Label(endLabel);
    }

    internal void EmitCameraMoveRight(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraMoveRightStep(config);
    }

    private void EmitCameraMoveRightStep(GameBoyCameraConfig config)
    {
        if (usesPackedCameraRuntime)
        {
            EmitPackedCameraMoveRightStep(config);
            return;
        }

        var endLabel = builder.CreateLabel("camera_move_right_end");

        EmitIncrement16(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        EmitQueuePendingColumn(GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn, GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn, 32);
        if (config.MapWidth <= byte.MaxValue)
        {
            EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, config.MapWidth);
            EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, config.MapWidth);
            EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, config.MapWidth);
        }
        else
        {
            EmitIncrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh, config.MapWidth);
            EmitIncrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh, config.MapWidth);
            EmitIncrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh, config.MapWidth);
        }

        builder.Label(endLabel);
    }

    internal void EmitCameraMoveLeft(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraMoveLeftStep(config);
    }

    private void EmitCameraMoveLeftStep(GameBoyCameraConfig config)
    {
        if (usesPackedCameraRuntime)
        {
            EmitPackedCameraMoveLeftStep(config);
            return;
        }

        var endLabel = builder.CreateLabel("camera_move_left_end");

        EmitDecrement16(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(7);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        EmitQueuePendingColumn(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn, GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn, 32);
        if (config.MapWidth <= byte.MaxValue)
        {
            EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, config.MapWidth);
            EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, config.MapWidth);
            EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, config.MapWidth);
        }
        else
        {
            EmitDecrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh, config.MapWidth);
            EmitDecrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh, config.MapWidth);
            EmitDecrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh, config.MapWidth);
        }

        builder.Label(endLabel);
    }

    private void EmitCameraMoveDownStep(GameBoyCameraConfig config)
    {
        if (usesPackedCameraRuntime)
        {
            EmitPackedCameraMoveDownStep(config);
            return;
        }

        var endLabel = builder.CreateLabel("camera_move_down_end");

        EmitIncrement16(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow, config.SourceHeight);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow, config.SourceHeight);
        // After a downward tile crossing, the fine-scroll-exposed bottom row is the advanced row.
        EmitQueuePendingRow(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow, GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveUpStep(GameBoyCameraConfig config)
    {
        if (usesPackedCameraRuntime)
        {
            EmitPackedCameraMoveUpStep(config);
            return;
        }

        var endLabel = builder.CreateLabel("camera_move_up_end");

        EmitDecrement16(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(7);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow, config.SourceHeight);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow, config.SourceHeight);
        EmitQueuePendingRow(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, GameBoyRuntimeMemoryLayout.Camera.TopSourceRow);

        builder.Label(endLabel);
    }

    private void EmitInitializePackedCameraRuntime()
    {
        var loop = builder.CreateLabel("packed_camera_state_clear_loop");
        builder.LoadHl(GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow);
        builder.Emit(0x06, checked((byte)(GameBoyRuntimeMemoryLayout.PackedCamera.DecodeWorkInVBlank - GameBoyRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow + 1))); // LD B,length
        builder.XorA();
        builder.Label(loop);
        builder.Emit(0x22); // LD (HL+),A
        builder.Emit(0x05); // DEC B
        builder.JumpRelative(0x20, loop); // JR NZ,loop
    }

    private void EmitPackedCameraMoveRightStep(GameBoyCameraConfig config)
    {
        var crossing = builder.CreateLabel("packed_camera_move_right_crossing");
        var advanceCrossing = builder.CreateLabel("packed_camera_move_right_advance_crossing");
        var end = builder.CreateLabel("packed_camera_move_right_end");
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.CompareImmediate(7);
        builder.JumpAbsolute(0xCA, crossing);
        EmitIncrement16(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.JumpAbsolute(end);

        builder.Label(crossing);
        EmitTryCancelPackedReverse(GameBoyPackedCameraRuntime.Column, GameBoyPackedCameraRuntime.Negative);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, advanceCrossing);
        EmitPackedDiagonalPreparationAllowed(GameBoyPackedCameraRuntime.Column);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, end);
        EmitRequestPackedColumn(
            GameBoyPackedCameraRuntime.Positive,
            GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn,
            config.MapWidth > byte.MaxValue ? GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh : null,
            GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.JumpAbsolute(0xCA, end);
        EmitRecordPackedDiagonalPreparedAxis(GameBoyPackedCameraRuntime.Column);
        EmitQueuePreparedPackedColumn(
            GameBoyPackedCameraRuntime.Positive,
            GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn,
            GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn,
            config.MapWidth > byte.MaxValue ? GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh : null);
        builder.Label(advanceCrossing);
        EmitIncrement16(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn, 32);
        if (config.MapWidth <= byte.MaxValue)
        {
            EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, config.MapWidth);
            EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, config.MapWidth);
            EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, config.MapWidth);
        }
        else
        {
            EmitIncrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh, config.MapWidth);
            EmitIncrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh, config.MapWidth);
            EmitIncrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh, config.MapWidth);
        }

        builder.Label(end);
    }

    private void EmitPackedCameraMoveLeftStep(GameBoyCameraConfig config)
    {
        var crossing = builder.CreateLabel("packed_camera_move_left_crossing");
        var advanceCrossing = builder.CreateLabel("packed_camera_move_left_advance_crossing");
        var end = builder.CreateLabel("packed_camera_move_left_end");
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, crossing);
        EmitDecrement16(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.JumpAbsolute(end);

        builder.Label(crossing);
        EmitTryCancelPackedReverse(GameBoyPackedCameraRuntime.Column, GameBoyPackedCameraRuntime.Positive);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, advanceCrossing);
        EmitPackedDiagonalPreparationAllowed(GameBoyPackedCameraRuntime.Column);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, end);
        EmitRequestPackedColumn(
            GameBoyPackedCameraRuntime.Negative,
            GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn,
            config.MapWidth > byte.MaxValue ? GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh : null,
            GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.JumpAbsolute(0xCA, end);
        EmitRecordPackedDiagonalPreparedAxis(GameBoyPackedCameraRuntime.Column);
        EmitQueuePreparedPackedColumn(
            GameBoyPackedCameraRuntime.Negative,
            GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn,
            GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn,
            config.MapWidth > byte.MaxValue ? GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh : null);
        builder.Label(advanceCrossing);
        EmitDecrement16(GameBoyRuntimeMemoryLayout.Camera.XLow, GameBoyRuntimeMemoryLayout.Camera.XHigh);
        builder.LoadAImmediate(7);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn, 32);
        if (config.MapWidth <= byte.MaxValue)
        {
            EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, config.MapWidth);
            EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, config.MapWidth);
            EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, config.MapWidth);
        }
        else
        {
            EmitDecrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn, GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh, config.MapWidth);
            EmitDecrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn, GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh, config.MapWidth);
            EmitDecrementWordAddressModulo(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn, GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh, config.MapWidth);
        }

        builder.Label(end);
    }

    private void EmitPackedCameraMoveDownStep(GameBoyCameraConfig config)
    {
        var crossing = builder.CreateLabel("packed_camera_move_down_crossing");
        var cancelledCrossing = builder.CreateLabel("packed_camera_move_down_cancelled_crossing");
        var advanceCamera = builder.CreateLabel("packed_camera_move_down_advance_camera");
        var end = builder.CreateLabel("packed_camera_move_down_end");
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.CompareImmediate(7);
        builder.JumpAbsolute(0xCA, crossing);
        EmitIncrement16(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.JumpAbsolute(end);

        builder.Label(crossing);
        EmitTryCancelPackedReverse(GameBoyPackedCameraRuntime.Row, GameBoyPackedCameraRuntime.Negative);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, cancelledCrossing);
        EmitPackedDiagonalPreparationAllowed(GameBoyPackedCameraRuntime.Row);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, end);
        EmitRequestPackedRow(
            GameBoyPackedCameraRuntime.Positive,
            GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow,
            GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow,
            config,
            cursorDelta: 1);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.JumpAbsolute(0xCA, end);
        EmitRecordPackedDiagonalPreparedAxis(GameBoyPackedCameraRuntime.Row);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow, config.SourceHeight);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow, config.SourceHeight);
        EmitQueuePreparedPackedRow(
            GameBoyPackedCameraRuntime.Positive,
            GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow,
            GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow);
        builder.JumpAbsolute(advanceCamera);

        builder.Label(cancelledCrossing);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow, 32);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow, config.SourceHeight);
        EmitIncrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow, config.SourceHeight);
        builder.Label(advanceCamera);
        EmitIncrement16(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.Label(end);
    }

    private void EmitPackedCameraMoveUpStep(GameBoyCameraConfig config)
    {
        var crossing = builder.CreateLabel("packed_camera_move_up_crossing");
        var cancelledCrossing = builder.CreateLabel("packed_camera_move_up_cancelled_crossing");
        var advanceCamera = builder.CreateLabel("packed_camera_move_up_advance_camera");
        var end = builder.CreateLabel("packed_camera_move_up_end");
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, crossing);
        EmitDecrement16(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.JumpAbsolute(end);

        builder.Label(crossing);
        EmitTryCancelPackedReverse(GameBoyPackedCameraRuntime.Row, GameBoyPackedCameraRuntime.Positive);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, cancelledCrossing);
        EmitPackedDiagonalPreparationAllowed(GameBoyPackedCameraRuntime.Row);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, end);
        EmitRequestPackedRow(
            GameBoyPackedCameraRuntime.Negative,
            GameBoyRuntimeMemoryLayout.Camera.TopSourceRow,
            GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow,
            config,
            cursorDelta: -1);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.NoSlot);
        builder.JumpAbsolute(0xCA, end);
        EmitRecordPackedDiagonalPreparedAxis(GameBoyPackedCameraRuntime.Row);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow, config.SourceHeight);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow, config.SourceHeight);
        EmitQueuePreparedPackedRow(
            GameBoyPackedCameraRuntime.Negative,
            GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow,
            GameBoyRuntimeMemoryLayout.Camera.TopSourceRow);
        builder.JumpAbsolute(advanceCamera);

        builder.Label(cancelledCrossing);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow, 32);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow, config.SourceHeight);
        EmitDecrementAddressModulo(GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow, config.SourceHeight);
        builder.Label(advanceCamera);
        EmitDecrement16(GameBoyRuntimeMemoryLayout.Camera.YLow, GameBoyRuntimeMemoryLayout.Camera.YHigh);
        builder.LoadAImmediate(7);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        builder.Label(end);
    }

    private void EmitPackedDiagonalPreparationAllowed(byte axis)
    {
        if (!SerializePackedDiagonalPreparation || !ProgramQueuesDiagonalStreaming())
        {
            builder.LoadAImmediate(1);
            return;
        }

        var allowed = builder.CreateLabel("packed_diagonal_preparation_allowed");
        var denied = builder.CreateLabel("packed_diagonal_preparation_denied");
        var done = builder.CreateLabel("packed_diagonal_preparation_check_done");
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreparedAxis);
        builder.CompareImmediate(axis);
        builder.JumpAbsolute(0xCA, allowed);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xC2, denied);
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreferredPreparationAxis);
        builder.CompareImmediate(axis);
        builder.JumpAbsolute(0xC2, denied);
        builder.Label(allowed);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(done);
        builder.Label(denied);
        builder.XorA();
        builder.Label(done);
    }

    private void EmitRecordPackedDiagonalPreparedAxis(byte axis)
    {
        if (!SerializePackedDiagonalPreparation || !ProgramQueuesDiagonalStreaming())
        {
            return;
        }

        builder.LoadAImmediate(axis);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreparedAxis);
    }

    private void EmitTryCancelPackedReverse(byte axis, byte pendingDirection)
    {
        var diagonal = ProgramQueuesDiagonalStreaming();
        var kindAddress = axis == GameBoyPackedCameraRuntime.Column
            ? diagonal ? GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind : GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind
            : diagonal ? GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind : GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind;
        var countAddress = axis == GameBoyPackedCameraRuntime.Column
            ? diagonal ? GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnCount : GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount
            : diagonal ? GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowCount : GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount;
        var firstDirectionAddress = axis == GameBoyPackedCameraRuntime.Column
            ? diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnDirection : GameBoyRuntimeMemoryLayout.PackedCamera.PendingDirection
            : diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowDirection : GameBoyRuntimeMemoryLayout.PackedCamera.PendingDirection;
        var secondDirectionAddress = axis == GameBoyPackedCameraRuntime.Column
            ? diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSecondDirection : GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondDirection
            : diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSecondDirection : GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondDirection;
        var firstSlotAddress = axis == GameBoyPackedCameraRuntime.Column
            ? diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSlot : GameBoyRuntimeMemoryLayout.PackedCamera.PendingSlot
            : diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSlot : GameBoyRuntimeMemoryLayout.PackedCamera.PendingSlot;
        var secondSlotAddress = axis == GameBoyPackedCameraRuntime.Column
            ? diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalColumnSecondSlot : GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondSlot
            : diagonal ? GameBoyRuntimeMemoryLayout.PackedCamera.PendingDiagonalRowSecondSlot : GameBoyRuntimeMemoryLayout.PackedCamera.PendingSecondSlot;

        var checkFirst = builder.CreateLabel("packed_camera_reverse_check_first");
        var cancelFirst = builder.CreateLabel("packed_camera_reverse_cancel_first");
        var cancelSecond = builder.CreateLabel("packed_camera_reverse_cancel_second");
        var cancelledFirst = builder.CreateLabel("packed_camera_reverse_cancelled_first");
        var cancelledSecond = builder.CreateLabel("packed_camera_reverse_cancelled_second");
        var notCancelled = builder.CreateLabel("packed_camera_reverse_not_cancelled");
        var done = builder.CreateLabel("packed_camera_reverse_done");

        builder.LoadA(kindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, notCancelled);
        builder.LoadA(countAddress);
        builder.CompareImmediate(PendingStreamDouble);
        builder.JumpAbsolute(0xC2, checkFirst);
        builder.LoadA(secondDirectionAddress);
        builder.CompareImmediate(pendingDirection);
        builder.JumpAbsolute(0xCA, cancelSecond);
        builder.JumpAbsolute(notCancelled);

        builder.Label(checkFirst);
        builder.LoadA(firstDirectionAddress);
        builder.CompareImmediate(pendingDirection);
        builder.JumpAbsolute(0xCA, cancelFirst);
        builder.JumpAbsolute(notCancelled);

        builder.Label(cancelSecond);
        EmitReleasePackedQueuedSlot(secondSlotAddress, cancelledSecond, notCancelled);
        builder.Label(cancelledSecond);
        builder.LoadAImmediate(PendingStreamSingle);
        builder.StoreA(countAddress);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(done);

        builder.Label(cancelFirst);
        EmitReleasePackedQueuedSlot(firstSlotAddress, cancelledFirst, notCancelled);
        builder.Label(cancelledFirst);
        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(kindAddress);
        builder.StoreA(countAddress);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(done);

        builder.Label(notCancelled);
        builder.LoadAImmediate(0);
        builder.Label(done);
    }

    private void EmitReleasePackedQueuedSlot(ushort slotAddress, string released, string notReleased)
    {
        var slot0 = builder.CreateLabel("packed_camera_reverse_release_slot_0");
        var slot1 = builder.CreateLabel("packed_camera_reverse_release_slot_1");
        builder.LoadA(slotAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, slot0);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xCA, slot1);
        builder.JumpAbsolute(notReleased);

        builder.Label(slot0);
        EmitReleasePackedSlotIfMutable(GameBoyRuntimeMemoryLayout.PackedCamera.Slot0, 0, released, notReleased);
        builder.Label(slot1);
        EmitReleasePackedSlotIfMutable(GameBoyRuntimeMemoryLayout.PackedCamera.Slot1, 1, released, notReleased);
    }

    private void EmitReleasePackedSlotIfMutable(ushort metadata, byte slot, string released, string notReleased)
    {
        builder.LoadA(checked((ushort)(metadata + GameBoyPackedCameraRuntime.StateOffset)));
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Committing);
        builder.JumpAbsolute(0xCA, notReleased);
        builder.CompareImmediate(GameBoyPackedCameraRuntime.Resident);
        builder.JumpAbsolute(0xC2, notReleased);
        builder.LoadAImmediate(slot);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        EmitSetPackedSelectedSlotState(GameBoyPackedCameraRuntime.Released);
        EmitIncrementPackedCounter(GameBoyRuntimeMemoryLayout.PackedCamera.ReleaseCount);
        builder.JumpAbsolute(released);
    }
}
