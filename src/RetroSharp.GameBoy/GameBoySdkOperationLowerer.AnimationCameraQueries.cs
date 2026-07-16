namespace RetroSharp.GameBoy;

using System.Globalization;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    internal void EmitSpriteWidth(FunctionCall call)
    {
        builder.LoadAImmediate(SpriteWidth(call));
    }

    internal void EmitCameraVerticalScrollMax()
    {
        builder.LoadAImmediate(CameraVerticalScrollMaxValue());
    }

    private byte CameraVerticalScrollMaxValue()
    {
        if (state.CameraStreamHeight is not { } streamHeight)
        {
            throw new InvalidOperationException("camera_vertical_scroll_max requires camera_init to run before it.");
        }

        var maxPixels = Math.Max(0, (streamHeight - VisibleScreenTileHeight) * 8);
        if (maxPixels > 255)
        {
            throw new InvalidOperationException(
                $"Camera.VerticalScrollMax() would be {maxPixels}px, which exceeds the 8-bit camera range; use a shorter world.");
        }

        return (byte)maxPixels;
    }

    internal void EmitAnimationFrame(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var clip = AnimationClipArg(call);
        var tickExpression = call.Parameters.ElementAt(1);
        if (context.TrySourceConstant(tickExpression, out var tick))
        {
            builder.LoadAImmediate(clip.FrameAtTick(tick % clip.DurationTicks));
            return;
        }

        context.EmitSourceExpressionToA(tickExpression);
        EmitAnimationFrameFromTickInA(clip);
    }

    private void EmitAnimationFrameFromTickInA(SpriteAnimationClip clip)
    {
        var moduloLabel = builder.CreateLabel("animation_frame_modulo");
        var afterModuloLabel = builder.CreateLabel("animation_frame_after_modulo");
        var endLabel = builder.CreateLabel("animation_frame_end");
        var frameLabels = Enumerable
            .Range(0, Math.Max(clip.FrameCount - 1, 0))
            .Select(_ => builder.CreateLabel("animation_frame_match"))
            .ToArray();

        builder.Label(moduloLabel);
        builder.CompareImmediate(clip.DurationTicks);
        builder.JumpAbsolute(0xDA, afterModuloLabel); // JP C,afterModuloLabel
        builder.SubtractAImmediate(clip.DurationTicks);
        builder.JumpAbsolute(moduloLabel);

        builder.Label(afterModuloLabel);
        for (var i = 0; i < clip.FrameCount - 1; i++)
        {
            builder.CompareImmediate(clip.FrameStartTicks[i + 1]);
            builder.JumpAbsolute(0xDA, frameLabels[i]); // JP C,frameLabel
        }

        builder.LoadAImmediate(clip.FrameIndices[^1]);
        builder.JumpAbsolute(endLabel);

        for (var i = 0; i < frameLabels.Length; i++)
        {
            builder.Label(frameLabels[i]);
            builder.LoadAImmediate(clip.FrameIndices[i]);
            builder.JumpAbsolute(endLabel);
        }

        builder.Label(endLabel);
    }

    private SpriteAnimationClip AnimationClipArg(FunctionCall call)
    {
        var clipName = GameBoyVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "animation_frame argument 1");
        if (!program.AnimationClips.TryGetValue(clipName, out var clip))
        {
            throw new InvalidOperationException($"Unknown animation clip '{clipName}'. Declare it with animation_clip(...).");
        }

        return clip;
    }

    internal void EmitCameraTileColumnAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraTileColumnAt(call.Parameters.ElementAt(0), config.MapWidth);
    }

    internal void EmitCameraTileColumnAt(ExpressionSyntax screenColumnExpression, int mapWidth)
    {
        var wrapLabel = builder.CreateLabel("camera_tile_column_wrap");
        var endLabel = builder.CreateLabel("camera_tile_column_end");

        context.EmitSourceExpressionToA(screenColumnExpression);
        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenLeftColumn);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    internal void EmitCameraSpanTileAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 3);
        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_tile_at");
        var foundLabel = builder.CreateLabel("camera_span_tile_found");
        var endLabel = builder.CreateLabel("camera_span_tile_end");

        for (var screenColumn = span.FirstScreenColumn; screenColumn <= span.LastScreenColumn; screenColumn++)
        {
            EmitCameraTileColumnAt(new ConstantSyntax(screenColumn.ToString(CultureInfo.InvariantCulture)), config.MapWidth);
            EmitMapTileAtSourceColumnInA(span.Row);
            builder.CompareImmediate(0);
            builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.Label(endLabel);
    }

    internal void EmitCameraSpanHasTile(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_has_tile");
        var tile = CheckedRange(context.ConstRuntimeValue(call.Parameters.ElementAt(3), "camera_span_has_tile argument 4"), 0, 255, "camera_span_has_tile argument 4");
        var foundLabel = builder.CreateLabel("camera_span_has_tile_found");
        var endLabel = builder.CreateLabel("camera_span_has_tile_end");

        for (var screenColumn = span.FirstScreenColumn; screenColumn <= span.LastScreenColumn; screenColumn++)
        {
            EmitCameraTileColumnAt(new ConstantSyntax(screenColumn.ToString(CultureInfo.InvariantCulture)), config.MapWidth);
            EmitMapTileAtSourceColumnInA(span.Row);
            builder.CompareImmediate(tile);
            builder.JumpAbsolute(0xCA, foundLabel); // JP Z,foundLabel
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitCameraSpanHasFlags(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        if (program.MapFlagColumnHeight == 0)
        {
            throw new InvalidOperationException("camera_span_has_flags requires world_map collision flag data.");
        }

        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_has_flags");
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        var flags = CheckedRange(context.ConstRuntimeValue(call.Parameters.ElementAt(3), "camera_span_has_flags argument 4"), 1, allowedFlags, "camera_span_has_flags argument 4");
        var foundLabel = builder.CreateLabel("camera_span_has_flags_found");
        var endLabel = builder.CreateLabel("camera_span_has_flags_end");

        for (var screenColumn = span.FirstScreenColumn; screenColumn <= span.LastScreenColumn; screenColumn++)
        {
            EmitCameraTileColumnAt(new ConstantSyntax(screenColumn.ToString(CultureInfo.InvariantCulture)), config.MapWidth);
            EmitMapFlagsAtSourceColumnInA(span.Row);
            builder.AndImmediate(flags);
            builder.CompareImmediate(0);
            builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private CameraSpanInfo BuildCameraSpan(FunctionCall call, int mapWidth, string callContext)
    {
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException($"{callContext} requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var screenX = CheckedRange(context.ConstRuntimeValue(args[0], $"{callContext} argument 1"), 0, 255, $"{callContext} argument 1");
        var width = CheckedRange(context.ConstRuntimeValue(args[1], $"{callContext} argument 2"), 1, 255, $"{callContext} argument 2");
        var row = CheckedRange(context.ConstRuntimeValue(args[2], $"{callContext} argument 3"), 0, program.MapColumnHeight - 1, $"{callContext} argument 3");
        var firstScreenColumn = screenX / 8;
        var lastScreenColumn = (screenX + width - 1) / 8;
        if (lastScreenColumn > 31)
        {
            throw new InvalidOperationException($"{callContext} span must fit within the Game Boy background tilemap width.");
        }

        if (lastScreenColumn - firstScreenColumn + 1 > mapWidth)
        {
            throw new InvalidOperationException($"{callContext} span must not cover more columns than the configured camera map width.");
        }

        return new CameraSpanInfo(firstScreenColumn, lastScreenColumn, row);
    }
}
