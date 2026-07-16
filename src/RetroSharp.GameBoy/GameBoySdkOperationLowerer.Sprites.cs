namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal sealed partial class GameBoySdkOperationLowerer
{
    private void EmitDrawLogicalSprite(Sdk2DOperation.DrawLogicalSprite operation)
    {
        if (!program.SpriteAssets.TryGetValue(operation.SpriteId, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy sprite asset '{operation.SpriteId}'. Declare it with sprite_asset(...).");
        }

        var firstHardwareSprite = state.NextHardwareSprite;
        if (firstHardwareSprite + asset.Pieces.Count > 40)
        {
            throw new InvalidOperationException("Game Boy sprite_draw calls exceed the 40 hardware sprite OAM limit.");
        }

        state.NextHardwareSprite += asset.Pieces.Count;
        for (var pieceIndex = 0; pieceIndex < asset.Pieces.Count; pieceIndex++)
        {
            var piece = asset.Pieces[pieceIndex];
            var oamAddress = (ushort)(GameBoyRuntimeMemoryLayout.Sprites.OamShadowStart + (firstHardwareSprite + pieceIndex) * 4);

            context.EmitByteExpressionToA(operation.Y);
            builder.AddAImmediate(16 + piece.YOffset);
            builder.StoreA(oamAddress);

            EmitSpriteDrawX(operation.X, operation.FlipX, asset, piece, (ushort)(oamAddress + 1));

            context.EmitByteExpressionToA(operation.Frame);
            EmitMultiplyAByConstant(asset.TilesPerFrame);
            builder.AddAImmediate(asset.FirstTile + piece.TileOffset);
            builder.StoreA((ushort)(oamAddress + 2));

            EmitSpriteDrawAttributes(operation.FlipX, operation.PaletteSlot, (ushort)(oamAddress + 3));
        }
    }

    private void EmitSpriteDrawX(
        SdkByteExpression xExpression,
        SdkByteExpression? flipXExpression,
        GameBoyCompiledSpriteAsset asset,
        GameBoyMetaspritePiece piece,
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

        context.EmitByteExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, normalLabel);

        EmitSpriteDrawXAtOffset(xExpression, flippedOffset, oamAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(normalLabel);
        EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
        builder.Label(endLabel);
    }

    private void EmitSpriteDrawAttributes(SdkByteExpression? flipXExpression, int paletteSlot, ushort oamAddress)
    {
        var paletteAttribute = SpritePaletteAttribute(paletteSlot);
        if (flipXExpression is null || (TrySdkConst(flipXExpression, out var constant) && constant == 0))
        {
            builder.LoadAImmediate(paletteAttribute);
            builder.StoreA(oamAddress);
            return;
        }

        if (TrySdkConst(flipXExpression, out _))
        {
            builder.LoadAImmediate(paletteAttribute | 0x20);
            builder.StoreA(oamAddress);
            return;
        }

        var noFlipLabel = builder.CreateLabel("sprite_flags_no_flip");
        var storeLabel = builder.CreateLabel("sprite_flags_store");

        context.EmitByteExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, noFlipLabel);

        builder.LoadAImmediate(paletteAttribute | 0x20);
        builder.JumpAbsolute(storeLabel);

        builder.Label(noFlipLabel);
        builder.LoadAImmediate(paletteAttribute);

        builder.Label(storeLabel);
        builder.StoreA(oamAddress);
    }

    private static int SpritePaletteAttribute(int paletteSlot) => (paletteSlot & 0x01) << 4;

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

    private void EmitSpriteDrawXAtOffset(SdkByteExpression xExpression, int offset, ushort oamAddress)
    {
        context.EmitByteExpressionToA(xExpression);
        builder.AddAImmediate(8 + offset);
        builder.StoreA(oamAddress);
    }

    private void EmitMultiplyAByConstant(int factor)
    {
        if (factor == 1)
        {
            return;
        }

        builder.LoadBFromA();
        builder.XorA();
        for (var i = 0; i < factor; i++)
        {
            builder.AddAFromB();
        }
    }
}
