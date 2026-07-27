namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    private const byte PendingStreamNone = 0;
    private const byte PendingStreamColumn = 1;
    private const byte PendingStreamRow = 2;
    private const byte PendingStreamQueued = 1;
    private const byte PendingStreamSingle = 1;
    private const byte PendingStreamDouble = 2;
    private const int VisibleScreenTileWidth = 20;
    private const int VisibleScreenTileWidthWithPartial = VisibleScreenTileWidth + 1;
    private const int VisibleScreenTileHeight = 18;
    private const int BackgroundTileMapWidth = 32;
    private const int BackgroundTileMapHeight = 32;
    // Refill value for the vertical-motion countdown (GameBoyRuntimeMemoryLayout.PackedCamera
    // .DiagonalVerticalMotionCountdown): keep the prefetch armed for a frame after the last vertical
    // step (one frame survives the per-frame decrement) so a diagonal that briefly runs level does
    // not thrash between prefetch and reactive paths, without over-arming a reversing follow camera.
    private const byte DiagonalVerticalMotionFrames = 2;
    private const byte DiagonalColumnPreparationEmitted = 1;
    private const byte DiagonalRowPreparationEmitted = 2;
    // Prefetch the next column edge two pixels into each tile, so the freshly revealed column is
    // already resident and the camera never has to stall at the tile boundary.
    private const byte DiagonalColumnPrefetchFineOffset = 2;

    private void EmitStreamMapColumn(Sdk2DOperation.StreamMapColumn operation)
    {
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("map_stream_column requires at least one map_column declaration.");
        }

        var y = CheckedRange(operation.Y, 0, 31, "map_stream_column argument 3");
        var height = CheckedRange(operation.Height, 1, program.MapColumnHeight, "map_stream_column argument 4");

        for (var row = 0; row < height; row++)
        {
            context.EmitByteExpressionToA(operation.SourceColumn);
            EmitReadOnlyMapByteAtSourceColumnInA(GameBoyRomBuilder.MapRowLabel(row));
            builder.LoadBFromA();

            var rowAddress = 0x9800 + (y + row) * 32;
            context.EmitByteExpressionToA(operation.TargetColumn);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    internal void EmitCameraInit(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 3);
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("camera_init requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var mapWidth = CheckedRange(GameBoyVideoProgram.ConstValue(args[0], "camera_init argument 1"), 1, 4096, "camera_init argument 1");
        var mapDataColumnCount = program.MapColumns.Keys.Max() + 1;
        if (mapWidth > mapDataColumnCount)
        {
            throw new InvalidOperationException($"camera_init argument 1 must not exceed the declared map column count ({mapDataColumnCount}).");
        }

        var y = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "camera_init argument 2"), 0, 31, "camera_init argument 2");
        var requestedHeight = CheckedRange(GameBoyVideoProgram.ConstValue(args[2], "camera_init argument 3"), 1, program.MapColumnHeight, "camera_init argument 3");
        var maxBufferedHeight = 32 - y;
        var height = requestedHeight;
        if (requestedHeight > maxBufferedHeight)
        {
            if (!ProgramQueuesRowStreaming())
            {
                throw new InvalidOperationException("camera_init stream area exceeds the Game Boy background tilemap height.");
            }

            height = maxBufferedHeight;
        }

        state.CameraMapWidth = mapWidth;
        state.CameraStreamY = y;
        state.CameraStreamHeight = requestedHeight;

        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.XLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.XHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineX);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
        if (mapWidth > byte.MaxValue)
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumnHigh);
        }

        builder.LoadAImmediate(VisibleScreenTileWidthWithPartial);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.RightBackgroundColumn);
        builder.LoadAImmediate(31);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.LeftBackgroundColumn);
        if (mapWidth <= byte.MaxValue)
        {
            builder.LoadAImmediate(VisibleScreenTileWidthWithPartial % mapWidth);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn);
            builder.LoadAImmediate(mapWidth - 1);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn);
        }
        else
        {
            context.EmitStoreSplitWordImmediate(
                GameBoyRuntimeMemoryLayout.Camera.RightSourceColumn,
                GameBoyRuntimeMemoryLayout.Camera.RightSourceColumnHigh,
                VisibleScreenTileWidthWithPartial % mapWidth);
            context.EmitStoreSplitWordImmediate(
                GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumn,
                GameBoyRuntimeMemoryLayout.Camera.LeftSourceColumnHigh,
                mapWidth - 1);
        }

        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.YLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.YHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.FineY);
        if (usesPackedCameraRuntime)
        {
            EmitInitializePackedCameraRuntime();
        }

        if (ProgramQueuesDiagonalStreaming())
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnCount);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowCount);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalColumnPrefetchLatch);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalVerticalMotionCountdown);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalRowPrefetchLatch);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreparationEmittedThisFrame);
            builder.LoadAImmediate(PendingStreamColumn);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalNextStreamKind);
        }
        else
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamKind);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.PendingStreamCount);
        }

        builder.LoadAImmediate(y);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.TopBackgroundRow);
        // The bottom edge tracks the row just below the visible window, not the full buffered/stream
        // height: for a tall map the streamed height is clamped to the 32-row background buffer, and
        // (y + 32) % 32 == y would collapse the bottom edge onto the top so every downward row crossing
        // streamed into the top band and left the real bottom rows permanently stale. Offsetting by the
        // visible tile height keeps the bottom edge a screen-height below the top.
        var bottomRowOffset = Math.Min(height, VisibleScreenTileHeight);
        builder.LoadAImmediate((y + bottomRowOffset) % 32);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.BottomBackgroundRow);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.TopSourceRow);
        builder.LoadAImmediate(bottomRowOffset % program.MapColumnHeight);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Camera.BottomSourceRow);
    }

    private void EmitSetCameraPosition(Sdk2DOperation.SetCameraPosition operation)
    {
        var config = EnsureCameraConfigured("camera_set_position");
        var packedDiagonal = usesPackedCameraRuntime
                             && operation.Axes.HasFlag(ScrollAxes.Horizontal)
                             && operation.Axes.HasFlag(ScrollAxes.Vertical);

        if (packedDiagonal)
        {
            context.EmitWordExpressionToA(operation.X, highByte: false);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetXLow);
            context.EmitWordExpressionToA(operation.X, highByte: true);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetXHigh);
            context.EmitWordExpressionToA(operation.Y, highByte: false);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetYLow);
            context.EmitWordExpressionToA(operation.Y, highByte: true);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetYHigh);
            var preparationStateReady = builder.CreateLabel("packed_camera_preparation_state_ready");
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreparationEmittedThisFrame);
            builder.CompareImmediate(DiagonalRowPreparationEmitted);
            builder.JumpAbsolute(0xCA, preparationStateReady);
            builder.LoadAImmediate(0);
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalPreparationEmittedThisFrame);
            builder.Label(preparationStateReady);
            EmitPrefetchPackedDiagonalRow(config);
        }

        if (operation.Axes.HasFlag(ScrollAxes.Horizontal))
        {
            if (ProgramQueuesDiagonalStreaming() && operation.Axes.HasFlag(ScrollAxes.Vertical))
            {
                // Age the "recent vertical motion" countdown once per frame. The vertical walk step
                // refreshes it whenever the camera actually scrolls vertically, so the horizontal
                // column prefetch only fires during real diagonal motion. Purely horizontal motion
                // lets the countdown reach zero and falls back to the cheaper reactive crossing, so a
                // heavy horizontal-only frame is never burdened with an early ~full-frame edge decode.
                var ageDone = builder.CreateLabel("packed_camera_vertical_motion_age_done");
                builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalVerticalMotionCountdown);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xCA, ageDone);
                builder.SubtractAImmediate(1);
                builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalVerticalMotionCountdown);
                builder.Label(ageDone);
            }

            if (packedDiagonal)
            {
                EmitCameraSetAxisPosition(
                    GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetXLow,
                    GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetXHigh,
                    GameBoyRuntimeMemoryLayout.Camera.XLow,
                    GameBoyRuntimeMemoryLayout.Camera.XHigh,
                    () => EmitCameraMoveLeftStep(config),
                    () => EmitCameraMoveRightStep(config),
                    "camera_set_position_right",
                    "camera_set_position_x_end");
            }
            else
            {
                EmitCameraSetAxisPosition(
                    operation.X,
                    GameBoyRuntimeMemoryLayout.Camera.XLow,
                    GameBoyRuntimeMemoryLayout.Camera.XHigh,
                    () => EmitCameraMoveLeftStep(config),
                    () => EmitCameraMoveRightStep(config),
                    "camera_set_position_right",
                    "camera_set_position_x_end");
            }
        }

        if (operation.Axes.HasFlag(ScrollAxes.Vertical))
        {
            if (packedDiagonal)
            {
                EmitCameraSetAxisPosition(
                    GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetYLow,
                    GameBoyRuntimeMemoryLayout.PackedCamera.DiagonalTargetYHigh,
                    GameBoyRuntimeMemoryLayout.Camera.YLow,
                    GameBoyRuntimeMemoryLayout.Camera.YHigh,
                    () => EmitCameraMoveUpStep(config),
                    () => EmitCameraMoveDownStep(config),
                    "camera_set_position_down",
                    "camera_set_position_y_end");
            }
            else
            {
                EmitCameraSetAxisPosition(
                    operation.Y,
                    GameBoyRuntimeMemoryLayout.Camera.YLow,
                    GameBoyRuntimeMemoryLayout.Camera.YHigh,
                    () => EmitCameraMoveUpStep(config),
                    () => EmitCameraMoveDownStep(config),
                    "camera_set_position_down",
                    "camera_set_position_y_end");
            }
        }
    }

    private void EmitApplyCamera(Sdk2DOperation.ApplyCamera operation)
    {
        var config = EnsureCameraConfigured("camera_apply");

        if (usesPackedCameraRuntime)
        {
            EmitApplyPackedCamera(config);
            return;
        }

        // Drain any column/row queued by last frame's camera move into VRAM now, while we are at the
        // top of the frame inside VBlank. This replaces the per-crossing extra WaitVBlank, so a
        // scrolling frame no longer costs two VBlanks for a single audio update.
        EmitCommitPendingStream(config);

        if (ProgramQueuesDiagonalStreaming())
        {
            // A simultaneous diagonal crossing drains one axis per VBlank. Keep both hardware
            // coordinates on the last fully resident viewport until the other edge is committed;
            // publishing both logical coordinates here would expose the still-pending edge for
            // one retained frame (most visibly when moving up/left).
            var done = builder.CreateLabel("camera_apply_diagonal_done");
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalColumnKind);
            builder.CompareImmediate(PendingStreamNone);
            builder.JumpAbsolute(0xC2, done); // JP NZ,done
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.PendingDiagonalRowKind);
            builder.CompareImmediate(PendingStreamNone);
            builder.JumpAbsolute(0xC2, done); // JP NZ,done
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.XLow);
            builder.StoreHighRamA(0x43);
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.YLow);
            builder.StoreHighRamA(0x42);
            builder.Label(done);
            return;
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.XLow);
        builder.StoreHighRamA(0x43);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.YLow);
        builder.StoreHighRamA(0x42);
    }
}
