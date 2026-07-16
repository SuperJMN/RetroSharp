namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    internal void EmitTilemapFillColumn(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        var args = call.Parameters.ToList();
        var y = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "tilemap_fill_column argument 2"), 0, 31, "tilemap_fill_column argument 2");
        var height = CheckedRange(GameBoyVideoProgram.ConstValue(args[2], "tilemap_fill_column argument 3"), 1, 32 - y, "tilemap_fill_column argument 3");

        context.EmitSourceExpressionToA(args[3]);
        builder.LoadBFromA();

        for (var row = y; row < y + height; row++)
        {
            var rowAddress = 0x9800 + row * 32;
            context.EmitSourceExpressionToA(args[0]);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    internal void EmitScrollSet(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var args = call.Parameters.ToList();

        context.EmitSourceExpressionToA(args[0]);
        builder.StoreHighRamA(0x43);

        context.EmitSourceExpressionToA(args[1]);
        builder.StoreHighRamA(0x42);
    }

    internal void EmitSpriteSet(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var spriteId = CheckedRange(GameBoyVideoProgram.ConstValue(args[0], "sprite_set argument 1"), 0, 39, "sprite_set argument 1");
        var oamAddress = (ushort)(0xFE00 + spriteId * 4);

        context.EmitSourceExpressionToA(args[2]);
        builder.AddAImmediate(16);
        builder.StoreA(oamAddress);

        context.EmitSourceExpressionToA(args[1]);
        builder.AddAImmediate(8);
        builder.StoreA((ushort)(oamAddress + 1));

        context.EmitSourceExpressionToA(args[3]);
        builder.StoreA((ushort)(oamAddress + 2));

        context.EmitSourceExpressionToA(args[4]);
        builder.StoreA((ushort)(oamAddress + 3));
    }
    private bool UsesPackedWorldRuntime => packedWorldRuntimeLayout is not null;

    private GameBoyCameraConfig EnsureCameraConfigured(string callName)
    {
        if (state.CameraMapWidth is not { } mapWidth
            || state.CameraStreamY is not { } streamY
            || state.CameraStreamHeight is not { } streamHeight)
        {
            throw new InvalidOperationException($"{callName} requires camera_init(...) to be emitted first.");
        }

        return new GameBoyCameraConfig(mapWidth, streamY, streamHeight, program.MapColumnHeight);
    }

    private static int CheckedRange(int value, int min, int max, string context)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{context} must be between {min} and {max}.");
        }

        return value;
    }

    private readonly record struct GameBoyCameraConfig(int MapWidth, int StreamY, int StreamHeight, int SourceHeight);
}
