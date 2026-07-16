namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    public void EmitOamShadowClear()
    {
        var clearLabel = builder.CreateLabel("oam_clear");

        if (useDirectOamWrites)
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(0x2003);
            builder.LoadAImmediate(0xFF);
            builder.LoadXImmediate(0);
            builder.Label(clearLabel);
            builder.StoreAAbsolute(0x2004);
            builder.IncrementX();
            builder.BranchRelative(0xD0, clearLabel); // BNE clearLabel
            return;
        }

        builder.LoadAImmediate(0xFF);
        builder.LoadXImmediate(0);
        builder.Label(clearLabel);
        builder.StoreAAbsoluteX(NesRuntimeMemoryLayout.Sprite.OamShadow);
        builder.IncrementX();
        builder.BranchRelative(0xD0, clearLabel);   // BNE clearLabel
        EmitOamDma();
    }

    private int SpriteWidth(string assetName)
    {
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        return asset.LogicalWidth;
    }

    internal void EmitSpriteWidth(FunctionCall call)
    {
        builder.LoadAImmediate(SpriteWidth(call));
    }

    internal void EmitCameraVerticalScrollMax()
    {
        if (cameraConfig is not { } config)
        {
            throw new InvalidOperationException("camera_vertical_scroll_max requires camera_init to run before it.");
        }

        var screenHeight = NesTarget.Capabilities.ScreenTiles.Height;
        var maxPixels = Math.Max(0, (config.StreamHeight - screenHeight) * 8);
        if (maxPixels > 255)
        {
            throw new InvalidOperationException(
                $"Camera.VerticalScrollMax() would be {maxPixels}px, which exceeds the 8-bit camera range; use a shorter world.");
        }

        builder.LoadAImmediate(maxPixels & 0xFF);
    }

    private int SpriteWidth(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var assetName = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sprite_width argument 1");
        return SpriteWidth(assetName);
    }

    internal void EmitAnimationFrame(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 2);
        var clip = AnimationClipArg(call);
        var tickExpression = call.Parameters.ElementAt(1);
        if (TryConst(tickExpression, out var tick))
        {
            builder.LoadAImmediate(clip.FrameAtTick(tick % clip.DurationTicks));
            return;
        }

        EmitExpressionToA(tickExpression);
        EmitAnimationFrameFromTickInA(clip);
    }

    private void EmitAnimationFrameFromTickInA(SpriteAnimationClip clip)
    {
        var moduloLabel = builder.CreateLabel("animation_frame_modulo");
        var afterModuloLabel = builder.CreateLabel("animation_frame_after_modulo");
        var endLabel = builder.CreateLabel("animation_frame_end");

        builder.Label(moduloLabel);
        builder.CompareImmediate(clip.DurationTicks);
        builder.BranchRelative(0x90, afterModuloLabel); // BCC afterModuloLabel
        builder.SetCarry();
        builder.SubtractImmediate(clip.DurationTicks);
        builder.JumpAbsolute(moduloLabel);

        builder.Label(afterModuloLabel);
        for (var i = 0; i < clip.FrameCount - 1; i++)
        {
            var nextFrameLabel = builder.CreateLabel("animation_frame_next");
            builder.CompareImmediate(clip.FrameStartTicks[i + 1]);
            builder.BranchRelative(0xB0, nextFrameLabel); // BCS nextFrameLabel
            builder.LoadAImmediate(clip.FrameIndices[i]);
            builder.JumpAbsolute(endLabel);
            builder.Label(nextFrameLabel);
        }

        builder.LoadAImmediate(clip.FrameIndices[^1]);
        builder.Label(endLabel);
    }

    private SpriteAnimationClip AnimationClipArg(FunctionCall call)
    {
        var clipName = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "animation_frame argument 1");
        if (!program.AnimationClips.TryGetValue(clipName, out var clip))
        {
            throw new InvalidOperationException($"Unknown animation clip '{clipName}'. Declare it with animation_clip(...).");
        }

        return clip;
    }

    private static IReadOnlyList<int> AabbSampleOffsets(int size)
    {
        var offsets = new List<int>();
        for (var offset = 0; offset < size; offset += 8)
        {
            offsets.Add(offset);
        }

        var lastOffset = size - 1;
        if (!offsets.Contains(lastOffset))
        {
            offsets.Add(lastOffset);
        }

        return offsets;
    }

    private NesCameraConfig EnsureCameraConfigured(string callName)
    {
        if (cameraConfig is not { } config)
        {
            throw new InvalidOperationException($"{callName} requires camera_init(...) to be emitted first.");
        }

        return config;
    }

    internal void EmitDrawLogicalSprite(Sdk2DOperation.DrawLogicalSprite operation)
    {
        if (!program.SpriteAssets.TryGetValue(operation.SpriteId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{operation.SpriteId}'. Declare it with sprite_asset(...).");
        }

        var physicalPaletteSlot = program.ResolveSpritePaletteBaseSlot(operation.SpriteId, operation.PaletteSlot);
        var requiredPaletteSlot = physicalPaletteSlot + asset.MaxPaletteSlotOffset;
        if (requiredPaletteSlot >= NesTarget.Capabilities.SpritePaletteSlots)
        {
            throw new InvalidOperationException(
                $"NES sprite asset '{asset.Name}' needs sprite palette slot {requiredPaletteSlot} for an automatic PNG overlay, but target 'nes' supports slots 0..{NesTarget.Capabilities.SpritePaletteSlots - 1}.");
        }

        var firstHardwareSprite = nextHardwareSprite;
        if (firstHardwareSprite + asset.Pieces.Count > NesTarget.Capabilities.SpriteCount)
        {
            throw new InvalidOperationException($"NES sprite_draw calls exceed the {NesTarget.Capabilities.SpriteCount} hardware sprite OAM limit.");
        }

        nextHardwareSprite += asset.Pieces.Count;
        if (useDirectOamWrites)
        {
            builder.LoadAImmediate(firstHardwareSprite * 4);
            builder.StoreAAbsolute(0x2003);
        }

        for (var pieceIndex = 0; pieceIndex < asset.Pieces.Count; pieceIndex++)
        {
            var piece = asset.Pieces[pieceIndex];
            var oamAddress = (ushort)(NesRuntimeMemoryLayout.Sprite.OamShadow + (firstHardwareSprite + pieceIndex) * 4);

            EmitSpriteDrawY(operation.Y, piece.YOffset, oamAddress);

            EmitSpriteTile(operation.Frame, asset, piece.TileOffset);
            EmitStoreOamByte((ushort)(oamAddress + 1));

            EmitSpriteDrawAttributes(operation.FlipX, physicalPaletteSlot + piece.PaletteSlotOffset, (ushort)(oamAddress + 2));

            EmitSpriteDrawX(operation.X, operation.FlipX, asset, piece, (ushort)(oamAddress + 3));
        }

        if (!useDirectOamWrites)
        {
            EmitOamDma();
        }
    }

    private void EmitSpriteDrawY(SdkByteExpression yExpression, int offset, ushort oamAddress)
    {
        EmitSdkByteExpressionToA(yExpression);
        EmitAddSignedImmediate(offset - 1 - BottomOverscanInset());
        EmitStoreOamByte(oamAddress);
    }

    private void EmitSpriteTile(SdkByteExpression frameExpression, NesCompiledSpriteAsset asset, int pieceTileOffset)
    {
        if (frameExpression is SdkByteExpression.Constant constant)
        {
            if (constant.Value < 0 || constant.Value >= asset.FrameCount)
            {
                throw new InvalidOperationException($"sprite_draw argument 4 must be between 0 and {asset.FrameCount - 1}.");
            }

            builder.LoadAImmediate(asset.FirstTile + constant.Value * asset.TilesPerFrame + pieceTileOffset);
            return;
        }

        EmitSdkByteExpressionToA(frameExpression);
        EmitMultiplyAByConstant(asset.TilesPerFrame);
        EmitAddSignedImmediate(asset.FirstTile + pieceTileOffset);
    }

    private void EmitSpriteDrawX(
        SdkByteExpression xExpression,
        SdkByteExpression? flipXExpression,
        NesCompiledSpriteAsset asset,
        NesMetaspritePiece piece,
        ushort oamAddress)
    {
        var normalOffset = piece.XOffset;
        var flippedOffset = asset.LogicalWidth - 8 - piece.XOffset;
        if (flipXExpression is null || normalOffset == flippedOffset)
        {
            EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
            return;
        }

        var normalLabel = builder.CreateLabel("sprite_x_normal");
        var endLabel = builder.CreateLabel("sprite_x_end");

        EmitSdkByteExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, normalLabel); // BEQ normalLabel

        EmitSpriteDrawXAtOffset(xExpression, flippedOffset, oamAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(normalLabel);
        EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
        builder.Label(endLabel);
    }

    private void EmitSpriteDrawXAtOffset(SdkByteExpression xExpression, int offset, ushort oamAddress)
    {
        EmitSdkByteExpressionToA(xExpression);
        EmitAddSignedImmediate(offset);
        EmitStoreOamByte(oamAddress);
    }

    private void EmitSpriteDrawAttributes(SdkByteExpression? flipXExpression, int paletteSlot, ushort oamAddress)
    {
        if (flipXExpression is null || (TrySdkConst(flipXExpression, out var constant) && constant == 0))
        {
            builder.LoadAImmediate(paletteSlot);
            EmitStoreOamByte(oamAddress);
            return;
        }

        if (TrySdkConst(flipXExpression, out _))
        {
            builder.LoadAImmediate(paletteSlot | 0x40);
            EmitStoreOamByte(oamAddress);
            return;
        }

        var noFlipLabel = builder.CreateLabel("sprite_flags_no_flip");
        var storeLabel = builder.CreateLabel("sprite_flags_store");

        EmitSdkByteExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, noFlipLabel); // BEQ noFlipLabel

        builder.LoadAImmediate(paletteSlot | 0x40);
        builder.JumpAbsolute(storeLabel);

        builder.Label(noFlipLabel);
        builder.LoadAImmediate(paletteSlot);

        builder.Label(storeLabel);
        EmitStoreOamByte(oamAddress);
    }

    private void EmitStoreOamByte(ushort shadowAddress)
    {
        builder.StoreAAbsolute(useDirectOamWrites ? (ushort)0x2004 : shadowAddress);
    }

    private void EmitMultiplyAByConstant(int factor)
    {
        if (factor == 1)
        {
            return;
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        builder.LoadAImmediate(0);
        for (var i = 0; i < factor; i++)
        {
            builder.ClearCarry();
            builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
        }
    }

    private static bool TrySdkConst(SdkByteExpression expression, out int value)
    {
        if (expression is SdkByteExpression.Constant constant)
        {
            value = constant.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TrySdkConst(SdkWordExpression expression, out int value)
    {
        if (expression is SdkWordExpression.Constant constant)
        {
            value = constant.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private void EmitAddSignedImmediate(int offset)
    {
        if (offset == 0)
        {
            return;
        }

        if (offset is < -255 or > 255)
        {
            throw new InvalidOperationException("NES sprite piece offset must fit in one byte for the current sprite spike.");
        }

        if (offset > 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(offset);
            return;
        }

        builder.SetCarry();
        builder.SubtractImmediate(-offset);
    }

    internal void EmitOamDma()
    {
        builder.LoadAImmediate((NesRuntimeMemoryLayout.Sprite.OamShadow >> 8) & 0xFF);
        builder.StoreAAbsolute(OamDmaAddress);
    }

}
