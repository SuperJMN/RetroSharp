namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    public void EmitCameraStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        if (usePackedCamera)
        {
            // The packed WorldPack and camera runtimes exclusively own $0326..$03FF.
            // Clear that exact control/scratch block before validation can read it;
            // mapper shadows end at $0325 and staging begins at $0400.
            EmitClearAbsoluteRange(
                NesRuntimeMemoryLayout.WorldPack.ValidationState,
                NesRuntimeMemoryLayout.WorldPack.CollisionCellResult,
                "nes_packed_runtime_state_clear");
        }

        if (useFourScreenNametables)
        {
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);
        }

        if (usePackedCamera)
        {
            builder.LoadAImmediate(NesPackedCameraRuntime.NoSlot);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.SelectedSlot);
        }
    }

    private void EmitClearAbsoluteRange(ushort start, ushort endInclusive, string labelName)
    {
        var length = endInclusive - start + 1;
        var loop = builder.CreateLabel(labelName);
        builder.LoadXImmediate(0);
        builder.Label(loop);
        builder.StoreAAbsoluteX(start);
        builder.IncrementX();
        builder.CompareXImmediate(length);
        builder.BranchRelative(0xD0, loop); // BNE loop
    }

    internal void EmitCameraInit(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 3);
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("camera_init requires world_map(...) data for the NES target.");
        var mapWidth = NesVideoProgram.ConstValue(call.Parameters.ElementAt(0), "camera_init argument 1");
        if (mapWidth is < 1 or > 4096)
        {
            throw new InvalidOperationException("NES camera_init map width must be between 1 and 4096 hardware cells.");
        }

        if (mapWidth > worldMap.Width)
        {
            throw new InvalidOperationException($"camera_init argument 1 must not exceed the declared world_map width ({worldMap.Width}).");
        }

        var streamY = NesVideoProgram.ConstValue(call.Parameters.ElementAt(1), "camera_init argument 2");
        var height = NesVideoProgram.ConstValue(call.Parameters.ElementAt(2), "camera_init argument 3");
        if (height > worldMap.Height)
        {
            throw new InvalidOperationException($"camera_init argument 3 must not exceed the declared world_map height ({worldMap.Height}).");
        }

        var bufferedHeight = height;
        if (useFourScreenNametables)
        {
            if (streamY < 0 || height < 1 || streamY >= 60)
            {
                throw new InvalidOperationException("NES four-screen free scroll stream area must fit within the 60-row four-screen height.");
            }

            bufferedHeight = Math.Min(height, 60 - streamY);
        }
        else if (streamY < 0 || height < 1 || streamY + height > 30)
        {
            throw new InvalidOperationException("camera_init stream area must fit within the NES visible nametable height.");
        }

        cameraConfig = new NesCameraConfig(mapWidth, worldMap.Height, streamY, bufferedHeight, useFourScreenNametables);
        var horizontalUsesWord = Math.Max(0, (mapWidth - NesTarget.Capabilities.ScreenTiles.Width) * 8) > byte.MaxValue;
        var verticalUsesWord = useFourScreenNametables
                               && Math.Max(0, (worldMap.Height - NesTarget.Capabilities.ScreenTiles.Height) * 8) > byte.MaxValue;
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.X);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceColumn);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
        if (horizontalUsesWord && mapWidth > byte.MaxValue)
        {
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.XHigh);
        }

        if (mapWidth > byte.MaxValue)
        {
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.TileColumnHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceColumnHigh);
        }

        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingStreamFlags);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingRowPhase);
        if (usePackedCamera)
        {
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.XHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.YHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.PendingAxes);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraTileColumn);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.VisibleCameraTileRow);
        }

        if (useFourScreenNametables)
        {
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.NewY);
            if (verticalUsesWord && worldMap.Height > byte.MaxValue)
            {
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.YHigh);
            }

            if (worldMap.Height > byte.MaxValue)
            {
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.TileRowHigh);
                builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.SourceRowHigh);
            }

            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TargetRow);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.SourceRow);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.StreamRemaining);
        }

        builder.LoadAImmediate(PendingStreamColumn);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.PendingNextStream);
    }

    internal void EmitSetCameraPosition(Sdk2DOperation.SetCameraPosition operation)
    {
        var config = EnsureCameraConfigured("camera_set_position");

        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        var horizontalFitsByte = Math.Max(0, (config.MapWidth - NesTarget.Capabilities.ScreenTiles.Width) * 8) <= byte.MaxValue;
        if (ShouldStreamColumnsForCamera(config))
        {
            if (horizontalFitsByte)
            {
                EmitWalkByteCameraAxisToTarget(
                    operation.X,
                    NesRuntimeMemoryLayout.Camera.X,
                    NesRuntimeMemoryLayout.Camera.NewX,
                    () => EmitStreamColumnForCameraPosition(config),
                    "nes_camera_x");
            }
            else
            {
                EmitWalkCameraAxisToTarget(
                    operation.X,
                    NesRuntimeMemoryLayout.Camera.X,
                    NesRuntimeMemoryLayout.Camera.XHigh,
                    NesRuntimeMemoryLayout.Camera.NewX,
                    NesRuntimeMemoryLayout.Camera.XHigh,
                    () => EmitStreamColumnForCameraPosition(config),
                    "nes_camera_x");
            }
        }
        else if (config.MapWidth > 32)
        {
            if (horizontalFitsByte)
            {
                EmitWalkByteCameraAxisToTarget(
                    operation.X,
                    NesRuntimeMemoryLayout.Camera.X,
                    NesRuntimeMemoryLayout.Camera.NewX,
                    () => EmitTrackCameraXPosition(config),
                    "nes_camera_x");
            }
            else
            {
                EmitWalkCameraAxisToTarget(
                    operation.X,
                    NesRuntimeMemoryLayout.Camera.X,
                    NesRuntimeMemoryLayout.Camera.XHigh,
                    NesRuntimeMemoryLayout.Camera.NewX,
                    NesRuntimeMemoryLayout.Camera.XHigh,
                    () => EmitTrackCameraXPosition(config),
                    "nes_camera_x");
            }
        }
        else
        {
            EmitSdkWordExpressionToA(operation.X, highByte: false);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.X);
            EmitSdkWordExpressionToA(operation.X, highByte: true);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.XHigh);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.NewX);
            builder.ShiftRightA();
            builder.ShiftRightA();
            builder.ShiftRightA();
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.TileColumnHigh);
            if (usePackedCamera)
            {
                EmitPublishVisibleCameraX();
            }
        }

        if (operation.Axes.HasFlag(ScrollAxes.Vertical))
        {
            if (!config.UseFourScreenNametables)
            {
                throw new InvalidOperationException("NES vertical camera movement requires four-screen nametable VRAM.");
            }

            if (config.MapHeight > 30)
            {
                var verticalFitsByte = Math.Max(0, (config.MapHeight - NesTarget.Capabilities.ScreenTiles.Height) * 8) <= byte.MaxValue;
                if (verticalFitsByte)
                {
                    EmitWalkByteCameraAxisToTarget(
                        operation.Y,
                        NesRuntimeMemoryLayout.Camera.Y,
                        NesRuntimeMemoryLayout.Camera.NewY,
                        () => EmitTrackCameraYPosition(config),
                        "nes_camera_y");
                }
                else
                {
                    EmitWalkCameraAxisToTarget(
                        operation.Y,
                        NesRuntimeMemoryLayout.Camera.Y,
                        NesRuntimeMemoryLayout.Camera.YHigh,
                        NesRuntimeMemoryLayout.Camera.NewY,
                        NesRuntimeMemoryLayout.Camera.YHigh,
                        () => EmitTrackCameraYPosition(config),
                        "nes_camera_y");
                }
            }
            else
            {
                EmitWalkByteCameraAxisToTarget(
                    operation.Y,
                    NesRuntimeMemoryLayout.Camera.Y,
                    NesRuntimeMemoryLayout.Camera.NewY,
                    EmitTrackShortCameraYPosition,
                    "nes_camera_y");
            }
        }
    }

    // Walks the camera axis one pixel per step toward the target held in A on entry, driving the
    // existing single-pixel tracking/streaming routine through the axis' "new position" scratch. The
    // per-frame step budget bounds the walk to at most one tile crossing, honoring the streaming slot.
    private void EmitWalkByteCameraAxisToTarget(
        SdkWordExpression requestedPosition,
        byte currentAddress,
        byte stepTargetAddress,
        Action emitTrackStep,
        string labelPrefix)
    {
        var loopLabel = builder.CreateLabel(labelPrefix + "_walk");
        var budgetOkLabel = builder.CreateLabel(labelPrefix + "_walk_budget_ok");
        var reachedCheckLabel = builder.CreateLabel(labelPrefix + "_walk_moving");
        var stepForwardLabel = builder.CreateLabel(labelPrefix + "_walk_forward");
        var doStepLabel = builder.CreateLabel(labelPrefix + "_walk_step");
        var endLabel = builder.CreateLabel(labelPrefix + "_walk_end");

        EmitSdkWordExpressionToA(requestedPosition, highByte: false);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkTarget);
        builder.LoadAImmediate(CameraWalkMaxStepsPerFrame);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);

        builder.Label(loopLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);
        builder.BranchRelative(0xD0, budgetOkLabel);
        builder.JumpAbsolute(endLabel);

        builder.Label(budgetOkLabel);
        builder.SetCarry();
        builder.SubtractImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.WalkTarget);
        builder.SetCarry();
        builder.SubtractZeroPage(currentAddress);
        builder.BranchRelative(0xD0, reachedCheckLabel);
        builder.JumpAbsolute(endLabel);

        builder.Label(reachedCheckLabel);
        builder.CompareImmediate(0x80);
        builder.BranchRelative(0x90, stepForwardLabel);

        builder.LoadAZeroPage(currentAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);
        builder.StoreAZeroPage(stepTargetAddress);
        builder.JumpAbsolute(doStepLabel);

        builder.Label(stepForwardLabel);
        builder.LoadAZeroPage(currentAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(stepTargetAddress);

        builder.Label(doStepLabel);
        emitTrackStep();
        builder.JumpAbsolute(loopLabel);

        builder.Label(endLabel);
    }

    private void EmitWalkCameraAxisToTarget(
        SdkWordExpression requestedPosition,
        byte currentLowAddress,
        ushort currentHighAddress,
        byte stepTargetLowAddress,
        ushort stepTargetHighAddress,
        Action emitTrackStep,
        string labelPrefix)
    {
        var loopLabel = builder.CreateLabel(labelPrefix + "_walk");
        var budgetOkLabel = builder.CreateLabel(labelPrefix + "_walk_budget_ok");
        var stepForwardLabel = builder.CreateLabel(labelPrefix + "_walk_forward");
        var doStepLabel = builder.CreateLabel(labelPrefix + "_walk_step");
        var endLabel = builder.CreateLabel(labelPrefix + "_walk_end");

        var stepBackwardLabel = builder.CreateLabel(labelPrefix + "_walk_backward");
        var lowDiffersLabel = builder.CreateLabel(labelPrefix + "_walk_low_differs");

        EmitSdkWordExpressionToA(requestedPosition, highByte: false);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkTarget);
        EmitSdkWordExpressionToA(requestedPosition, highByte: true);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkTargetHigh);
        builder.LoadAImmediate(CameraWalkMaxStepsPerFrame);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);

        builder.Label(loopLabel);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);
        builder.BranchRelative(0xD0, budgetOkLabel); // BNE budgetOkLabel
        builder.JumpAbsolute(endLabel);              // step budget spent (far jump)

        builder.Label(budgetOkLabel);
        builder.SetCarry();
        builder.SubtractImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.WalkSteps);

        builder.LoadAAbsolute(currentHighAddress);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.Camera.WalkTargetHigh);
        builder.BranchRelative(0x90, stepForwardLabel); // BCC: current high < target high
        builder.BranchRelative(0xD0, stepBackwardLabel); // BNE: current high > target high
        builder.LoadAZeroPage(currentLowAddress);
        builder.CompareAbsolute(NesRuntimeMemoryLayout.Camera.WalkTarget);
        builder.BranchRelative(0xD0, lowDiffersLabel); // BNE: low bytes differ
        builder.JumpAbsolute(endLabel); // reached target
        builder.Label(lowDiffersLabel);
        builder.BranchRelative(0x90, stepForwardLabel); // BCC: current low < target low

        builder.Label(stepBackwardLabel);
        builder.LoadAZeroPage(currentLowAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);
        builder.StoreAZeroPage(stepTargetLowAddress);
        builder.LoadAAbsolute(currentHighAddress);
        builder.SubtractImmediate(0);
        builder.StoreAAbsolute(stepTargetHighAddress);
        builder.JumpAbsolute(doStepLabel);

        builder.Label(stepForwardLabel);
        builder.LoadAZeroPage(currentLowAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(stepTargetLowAddress);
        builder.LoadAAbsolute(currentHighAddress);
        builder.AddImmediate(0);
        builder.StoreAAbsolute(stepTargetHighAddress);

        builder.Label(doStepLabel);
        emitTrackStep();
        builder.JumpAbsolute(loopLabel);

        builder.Label(endLabel);
    }

    internal void EmitApplyCamera(Sdk2DOperation.ApplyCamera operation)
    {
        var config = EnsureCameraConfigured("camera_apply");
        var applyLabel = builder.CreateLabel("camera_apply_now");
        var doneLabel = builder.CreateLabel("camera_apply_done");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, applyLabel); // BEQ applyLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(applyLabel);
        EmitApplyCameraNow(config);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);

        builder.Label(doneLabel);
    }

    private void EmitApplyPendingCameraScrollAtVBlank()
    {
        if (cameraConfig is not { } config)
        {
            return;
        }

        EmitApplyCameraNow(config);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Camera.ScrollApplied);
    }

    private void EmitApplyCameraNow(NesCameraConfig config)
    {
        EmitCommitPendingCameraStream(config);
        if (useFourScreenNametables)
        {
            EmitRestoreFourScreenCameraScroll();
        }
        else
        {
            EmitRestoreCameraScroll();
        }
    }

    internal void EmitSdkByteExpressionToA(SdkByteExpression expression)
    {
        switch (expression)
        {
            case SdkByteExpression.Constant constant:
                builder.LoadAImmediate(constant.Value);
                break;
            case SdkByteExpression.Variable variable:
                EmitSdkStorageLocationToA(variable.Location);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SDK byte expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitSdkWordExpressionToA(SdkWordExpression expression, bool highByte)
    {
        switch (expression)
        {
            case SdkWordExpression.Constant constant:
                builder.LoadAImmediate(highByte ? (constant.Value >> 8) & 0xFF : constant.Value & 0xFF);
                break;
            case SdkWordExpression.Variable variable:
                EmitSdkWordStorageLocationToA(variable.Location, highByte);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SDK word expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitSdkWordStorageLocationToA(SdkStorageLocation location, bool highByte)
    {
        if (!highByte)
        {
            EmitSdkStorageLocationToA(location);
            return;
        }

        var type = location is SdkStorageLocation.RuntimeIndexedField runtimeIndexed
            ? VariableStorageType(IndexedMemberName(runtimeIndexed.BaseName, 0, runtimeIndexed.FieldName))
            : VariableStorageType(StorageKey(location));
        if (!IsWordBackedType(type))
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (location is SdkStorageLocation.RuntimeIndexedField runtime)
        {
            EmitRuntimeMemberIndexToX(runtime.BaseName, runtime.Index);
            builder.LoadAZeroPageX(HighAddress(RuntimeIndexedMemberBaseAddress(runtime.BaseName, runtime.FieldName)));
            return;
        }

        builder.LoadAZeroPage(HighAddress(VariableAddress(StorageKey(location))));
    }

    private void EmitSdkStorageLocationToA(SdkStorageLocation location)
    {
        switch (location)
        {
            case SdkStorageLocation.RuntimeIndexedField runtimeIndexed:
                EmitRuntimeMemberIndexToX(runtimeIndexed.BaseName, runtimeIndexed.Index);
                builder.LoadAZeroPageX(RuntimeIndexedMemberBaseAddress(runtimeIndexed.BaseName, runtimeIndexed.FieldName));
                break;
            default:
                builder.LoadAZeroPage(VariableAddress(StorageKey(location)));
                break;
        }
    }

}
