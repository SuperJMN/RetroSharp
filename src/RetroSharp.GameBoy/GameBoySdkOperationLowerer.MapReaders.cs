namespace RetroSharp.GameBoy;

using System.Globalization;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    private void EmitMapTileAtSourceColumnInA(int row)
    {
        EmitReadOnlyMapByteAtSourceColumnInA(GameBoyRomBuilder.MapRowLabel(row));
    }

    private void EmitCameraMapFlagsAtSourceColumnInA(int row, int mapWidth)
    {
        if (UsesPackedWorldRuntime && mapWidth > byte.MaxValue)
        {
            builder.LoadEFromA();
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumnHigh);
            builder.LoadDFromA();
            builder.LoadHl(checked((ushort)row));
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackCollisionLookupLabel);
            return;
        }

        EmitMapFlagsAtSourceColumnInA(row);
    }

    private void EmitCameraMapFlagsAtSourceColumnInBAndRowInC(int mapWidth)
    {
        if (UsesPackedWorldRuntime && mapWidth > byte.MaxValue)
        {
            builder.Emit(0x58); // LD E,B
            builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.ScreenTileFlagsColumnHigh);
            builder.LoadDFromA();
            builder.Emit(0x69); // LD L,C
            builder.LoadHImmediate(0);
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackCollisionLookupLabel);
            return;
        }

        EmitMapFlagsAtSourceColumnInBAndRowInC();
    }

    private void EmitMapFlagsAtSourceColumnInA(int row)
    {
        if (UsesPackedWorldRuntime)
        {
            builder.LoadEFromA();
            builder.LoadDImmediate(0);
            builder.LoadHl(checked((ushort)row));
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackCollisionLookupLabel);
            return;
        }

        EmitReadOnlyMapFlagsByteAtSourceColumnInA(checked(row * MapFlagColumnCount()));
    }

    private int MapColumnCount()
    {
        return program.MapColumns.Keys.Max() + 1;
    }

    private int MapFlagColumnCount()
    {
        return program.MapFlagColumns.Keys.Max() + 1;
    }

    private void EmitReadOnlyMapFlagsByteAtSourceColumnInA(int rowOffset)
    {
        if (romLayout.TryReadOnlyDataPlacement(GameBoyRomBuilder.MapFlagDataLabel, out var placement))
        {
            builder.LoadAImmediate(placement.Bank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            EmitMapDataByteAtSourceColumnInA(placement.Address, rowOffset);
            RestoreProgramBankAfterReadOnlyDataRead();
            return;
        }

        EmitMapDataByteAtSourceColumnInA(GameBoyRomBuilder.MapFlagDataLabel, rowOffset);
    }

    private void EmitMapFlagsAtSourceColumnInBAndRowInC()
    {
        if (UsesPackedWorldRuntime)
        {
            builder.Emit(0x58); // LD E,B
            builder.LoadDImmediate(0);
            builder.Emit(0x69); // LD L,C
            builder.LoadHImmediate(0);
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackCollisionLookupLabel);
            return;
        }

        if (romLayout.TryReadOnlyDataPlacement(GameBoyRomBuilder.MapFlagDataLabel, out var placement))
        {
            builder.LoadAImmediate(placement.Bank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            EmitMapDataByteAtSourceColumnInBAndRowInC(placement.Address, MapFlagColumnCount());
            RestoreProgramBankAfterReadOnlyDataRead();
            return;
        }

        EmitMapDataByteAtSourceColumnInBAndRowInC(GameBoyRomBuilder.MapFlagDataLabel, MapFlagColumnCount());
    }

    private void EmitMapTileAtSourceColumnInBAndRowInC()
    {
        if (romLayout.TryReadOnlyDataPlacement(GameBoyRomBuilder.MapDataLabel, out var placement))
        {
            builder.LoadAImmediate(placement.Bank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            EmitMapDataByteAtSourceColumnInBAndRowInC(placement.Address, MapColumnCount());
            RestoreProgramBankAfterReadOnlyDataRead();
            return;
        }

        EmitMapDataByteAtSourceColumnInBAndRowInC(GameBoyRomBuilder.MapDataLabel, MapColumnCount());
    }

    private void EmitMapDataByteAtSourceColumnInA(ushort baseAddress, int rowOffset)
    {
        builder.LoadHl(baseAddress);
        EmitAddConstantToHl(rowOffset);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitMapDataByteAtSourceColumnInA(string baseLabel, int rowOffset)
    {
        builder.LoadHl(baseLabel);
        EmitAddConstantToHl(rowOffset);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitMapDataByteAtSourceColumnInBAndRowInC(ushort baseAddress, int rowWidth)
    {
        builder.LoadHl(baseAddress);
        EmitAddRuntimeRowOffsetToHl(rowWidth);
        builder.LoadAFromB();
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitMapDataByteAtSourceColumnInBAndRowInC(string baseLabel, int rowWidth)
    {
        builder.LoadHl(baseLabel);
        EmitAddRuntimeRowOffsetToHl(rowWidth);
        builder.LoadAFromB();
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitAddRuntimeRowOffsetToHl(int rowWidth)
    {
        var doneLabel = builder.CreateLabel("map_flags_row_offset_done");
        var loopLabel = builder.CreateLabel("map_flags_row_offset_loop");

        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, doneLabel); // JP Z,doneLabel
        builder.LoadDe((ushort)rowWidth);
        builder.Label(loopLabel);
        builder.AddHlDe();
        builder.DecrementA();
        builder.JumpAbsolute(0xC2, loopLabel); // JP NZ,loopLabel
        builder.Label(doneLabel);
    }

    private void EmitAddressWithOffsetModuloToA(ushort address, int offset, int modulo)
    {
        var endLabel = builder.CreateLabel("address_offset_modulo_end");

        builder.LoadA(address);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.CompareImmediate(modulo);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(modulo);
        builder.Label(endLabel);
    }

    private void EmitAddConstantToHl(int offset)
    {
        if (offset == 0)
        {
            return;
        }

        builder.LoadDe((ushort)offset);
        builder.AddHlDe();
    }

    private void RestoreProgramBankAfterReadOnlyDataRead()
    {
        if (romLayout.ProgramTailBankCount == 0)
        {
            return;
        }

        builder.LoadBFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
        builder.LoadAFromB();
    }

    private void EmitReadOnlyMapByteAtSourceColumnInA(string rowLabel)
    {
        if (romLayout.TryReadOnlyDataPlacement(rowLabel, out _))
        {
            builder.JumpAbsolute(0xCD, ReadOnlyDataByteReaderLabel(rowLabel)); // CALL nn
            return;
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(rowLabel);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }
    private static string ReadOnlyDataByteReaderLabel(string label) => $"read_data_{label}";

    private int SpriteWidth(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var assetName = GameBoyVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sprite_width argument 1");
        return SpriteWidth(assetName);
    }

    private int SpriteWidth(string assetName)
    {
        return program.SpriteAssets.TryGetValue(assetName, out var asset)
            ? asset.LogicalWidth
            : throw new InvalidOperationException($"Unknown Game Boy sprite asset '{assetName}'. Declare it with sprite_asset(...).");
    }

    private int CameraAabbWidth(SdkAabbExtent width)
    {
        return width switch
        {
            SdkAabbExtent.Constant constant => constant.Value,
            SdkAabbExtent.SpriteWidth spriteWidth => SpriteWidth(spriteWidth.SpriteId),
            _ => throw new InvalidOperationException($"Unsupported camera AABB width '{width.GetType().Name}'."),
        };
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

    private readonly record struct CameraSpanInfo(int FirstScreenColumn, int LastScreenColumn, int Row);
}
