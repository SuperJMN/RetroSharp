using System.Text;
using RetroSharp.Parser;

namespace RetroSharp.GameBoy;

internal static class GameBoyRomBuilder
{
    private const int RomSize = 32 * 1024;

    private static readonly byte[] NintendoLogo =
    [
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B,
        0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
        0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
        0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC,
        0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E,
    ];

    public static byte[] Build(GameBoyVideoProgram program)
    {
        var rom = new byte[RomSize];
        WriteHeaderSkeleton(rom);

        var builder = BuildProgram(program);
        var programBytes = builder.Build();
        if (programBytes.Length > RomSize - 0x0150)
        {
            throw new InvalidOperationException("Generated Game Boy program does not fit in a 32 KiB ROM.");
        }

        programBytes.CopyTo(rom, 0x0150);

        WriteHeaderChecksums(rom);
        return rom;
    }

    private static GbBuilder BuildProgram(GameBoyVideoProgram program)
    {
        var builder = new GbBuilder();
        var tileData = BuildTileData(program);

        builder.Emit(0xF3);                         // DI
        builder.Emit(0x31, 0xFE, 0xFF);             // LD SP,$FFFE
        builder.Emit(0xAF);                         // XOR A
        builder.Emit(0xE0, 0xFF);                   // LDH ($FF),A

        EmitWaitVBlank(builder, "startup_wait_vblank");

        builder.Emit(0xAF);                         // XOR A
        builder.Emit(0xE0, 0x40);                   // LDH ($40),A
        builder.Emit(0x3E, program.BackgroundPalette);
        builder.Emit(0xE0, 0x47);                   // LDH ($47),A
        builder.Emit(0x3E, program.ObjectPalette);
        builder.Emit(0xE0, 0x48);                   // LDH ($48),A
        builder.Emit(0xAF);                         // XOR A
        builder.Emit(0xE0, 0x42);                   // LDH ($42),A
        builder.Emit(0xE0, 0x43);                   // LDH ($43),A

        builder.LoadHl(0x8000);
        builder.LoadDe("tile_data");
        builder.LoadBc((ushort)tileData.Length);
        EmitCopyLoop(builder, "copy_tiles");

        builder.LoadHl(0x9800);
        builder.LoadDe("tilemap");
        builder.LoadBc(1024);
        EmitCopyLoop(builder, "copy_tilemap");

        EmitClearOam(builder, "clear_oam");

        builder.Emit(0x3E, 0x97);                   // LD A,$97
        builder.Emit(0xE0, 0x40);                   // LDH ($40),A

        new GameBoyRuntimeCompiler(builder, program).Emit(program.MainBlock);

        builder.Label("forever");
        builder.JumpRelative(0x18, "forever");     // JR forever

        builder.Label("tile_data");
        builder.Emit(tileData);
        builder.Label("tilemap");
        builder.Emit(program.TileMap);
        EmitMapData(builder, program);

        return builder;
    }

    private static void EmitMapData(GbBuilder builder, GameBoyVideoProgram program)
    {
        if (program.MapColumnHeight == 0)
        {
            return;
        }

        var columnCount = program.MapColumns.Keys.Max() + 1;
        for (var row = 0; row < program.MapColumnHeight; row++)
        {
            builder.Label(MapRowLabel(row));
            for (var column = 0; column < columnCount; column++)
            {
                var tile = program.MapColumns.TryGetValue(column, out var tiles) ? tiles[row] : (byte)0;
                builder.Emit(tile);
            }
        }
    }

    internal static string MapRowLabel(int row) => $"map_row_{row}";

    internal static void EmitWaitVBlank(GbBuilder builder, string label)
    {
        builder.Label($"{label}_wait_visible");
        builder.Emit(0xF0, 0x44);                   // LDH A,($44)
        builder.Emit(0xFE, 0x90);                   // CP $90
        builder.JumpRelative(0x30, $"{label}_wait_visible"); // JR NC,label

        builder.Label($"{label}_wait_vblank");
        builder.Emit(0xF0, 0x44);                   // LDH A,($44)
        builder.Emit(0xFE, 0x90);                   // CP $90
        builder.JumpRelative(0x38, $"{label}_wait_vblank"); // JR C,label
    }

    private static void EmitCopyLoop(GbBuilder builder, string label)
    {
        builder.Label(label);
        builder.Emit(0x1A);                         // LD A,(DE)
        builder.Emit(0x22);                         // LD (HL+),A
        builder.Emit(0x13);                         // INC DE
        builder.Emit(0x0B);                         // DEC BC
        builder.Emit(0x78);                         // LD A,B
        builder.Emit(0xB1);                         // OR C
        builder.JumpRelative(0x20, label);          // JR NZ,label
    }

    internal static void EmitClearOam(GbBuilder builder, string label)
    {
        builder.LoadHl(0xFE00);
        builder.LoadBc(160);
        builder.Label(label);
        builder.Emit(0x36, 0x00);                   // LD (HL),$00
        builder.Emit(0x23);                         // INC HL
        builder.Emit(0x0B);                         // DEC BC
        builder.Emit(0x78);                         // LD A,B
        builder.Emit(0xB1);                         // OR C
        builder.JumpRelative(0x20, label);          // JR NZ,label
    }

    private static byte[] BuildTileData(GameBoyVideoProgram program)
    {
        var tiles = new byte[(GameBoyVideoProgram.FirstSpriteTile + program.SpriteTileCount) * 16];
        WriteSolidTile(tiles, 1, 1);
        WriteSolidTile(tiles, 2, 2);
        WriteSolidTile(tiles, 3, 3);
        WriteCheckerTile(tiles, 4, 1, 2);
        WriteFrameTile(tiles, 5, 3);

        foreach (var asset in program.SpriteAssetsInLoadOrder)
        {
            asset.TileData.CopyTo(tiles, asset.FirstTile * 16);
        }

        return tiles;
    }

    private static void WriteSolidTile(byte[] tiles, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            WriteTileRow(tiles, tile, row, color, color, color, color, color, color, color, color);
        }
    }

    private static void WriteCheckerTile(byte[] tiles, int tile, int colorA, int colorB)
    {
        for (var row = 0; row < 8; row++)
        {
            var a = (row & 1) == 0 ? colorA : colorB;
            var b = (row & 1) == 0 ? colorB : colorA;
            WriteTileRow(tiles, tile, row, a, b, a, b, a, b, a, b);
        }
    }

    private static void WriteFrameTile(byte[] tiles, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            if (row is 0 or 7)
            {
                WriteTileRow(tiles, tile, row, color, color, color, color, color, color, color, color);
            }
            else
            {
                WriteTileRow(tiles, tile, row, color, 0, 0, 0, 0, 0, 0, color);
            }
        }
    }

    private static void WriteTileRow(byte[] tiles, int tile, int row, params int[] colors)
    {
        var plane0 = 0;
        var plane1 = 0;
        for (var col = 0; col < 8; col++)
        {
            var color = colors[col] & 0x03;
            var bit = 7 - col;
            if ((color & 1) != 0) plane0 |= 1 << bit;
            if ((color & 2) != 0) plane1 |= 1 << bit;
        }

        var offset = tile * 16 + row * 2;
        tiles[offset] = (byte)plane0;
        tiles[offset + 1] = (byte)plane1;
    }

    private static void WriteHeaderSkeleton(byte[] rom)
    {
        rom[0x0100] = 0x00;                         // NOP
        rom[0x0101] = 0xC3;                         // JP $0150
        rom[0x0102] = 0x50;
        rom[0x0103] = 0x01;
        NintendoLogo.CopyTo(rom, 0x0104);

        var title = Encoding.ASCII.GetBytes("RETROSHARPGB");
        title.CopyTo(rom, 0x0134);

        rom[0x0147] = 0x00;                         // ROM only
        rom[0x0148] = 0x00;                         // 32 KiB ROM
        rom[0x0149] = 0x00;                         // No cartridge RAM
        rom[0x014A] = 0x01;                         // Non-Japanese
        rom[0x014B] = 0x00;                         // No old licensee
        rom[0x014C] = 0x00;                         // Version
    }

    private static void WriteHeaderChecksums(byte[] rom)
    {
        var headerChecksum = 0;
        for (var i = 0x0134; i <= 0x014C; i++)
        {
            headerChecksum = headerChecksum - rom[i] - 1;
        }

        rom[0x014D] = (byte)headerChecksum;

        var globalChecksum = 0;
        for (var i = 0; i < rom.Length; i++)
        {
            if (i is 0x014E or 0x014F)
            {
                continue;
            }

            globalChecksum = (globalChecksum + rom[i]) & 0xFFFF;
        }

        rom[0x014E] = (byte)(globalChecksum >> 8);
        rom[0x014F] = (byte)(globalChecksum & 0xFF);
    }
}

internal sealed class GameBoyRuntimeCompiler
{
    private const ushort FirstVariableAddress = 0xC000;
    private const ushort InputCurrentAddress = 0xC0F0;
    private const ushort InputPreviousAddress = 0xC0F1;
    private const ushort InputHoldTicksStartAddress = 0xC0F2;
    private const byte JoypadDeselect = 0x30;
    private const int JoypadSettleReadCount = 4;

    private static readonly GameBoyButton[] Buttons =
    [
        new("a", 0x10, 0x01, 0x01, InputHoldTicksStartAddress),
        new("b", 0x10, 0x02, 0x02, InputHoldTicksStartAddress + 1),
        new("select", 0x10, 0x04, 0x04, InputHoldTicksStartAddress + 2),
        new("start", 0x10, 0x08, 0x08, InputHoldTicksStartAddress + 3),
        new("right", 0x20, 0x01, 0x10, InputHoldTicksStartAddress + 4),
        new("left", 0x20, 0x02, 0x20, InputHoldTicksStartAddress + 5),
        new("up", 0x20, 0x04, 0x40, InputHoldTicksStartAddress + 6),
        new("down", 0x20, 0x08, 0x80, InputHoldTicksStartAddress + 7),
    ];

    private readonly GbBuilder builder;
    private readonly GameBoyVideoProgram program;
    private readonly Dictionary<string, ushort> variables = [];
    private readonly HashSet<string> userFunctionCallStack = [];
    private int nextHardwareSprite;
    private ushort nextVariableAddress = FirstVariableAddress;

    public GameBoyRuntimeCompiler(GbBuilder builder, GameBoyVideoProgram program)
    {
        this.builder = builder;
        this.program = program;
    }

    public void Emit(BlockSyntax block)
    {
        EmitInputStateInitialization();
        EmitBlock(block);
    }

    private void EmitInputStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(InputCurrentAddress);
        builder.StoreA(InputPreviousAddress);
        foreach (var button in Buttons)
        {
            builder.StoreA(button.HoldTicksAddress);
        }
    }

    private void EmitBlock(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case DeclarationSyntax declaration:
                EmitDeclaration(declaration);
                break;
            case ExpressionStatementSyntax expressionStatement:
                EmitExpressionStatement(expressionStatement);
                break;
            case WhileSyntax whileSyntax:
                EmitWhile(whileSyntax);
                break;
            case IfElseSyntax ifElseSyntax:
                EmitIf(ifElseSyntax);
                break;
            case ReturnSyntax:
                break;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy statement '{statement.GetType().Name}'.");
        }
    }

    private void EmitDeclaration(DeclarationSyntax declaration)
    {
        if (!IsByteBackedType(declaration.Type))
        {
            throw new InvalidOperationException($"Game Boy target does not support local type '{declaration.Type}' yet.");
        }

        if (variables.ContainsKey(declaration.Name))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        if (nextVariableAddress >= InputCurrentAddress)
        {
            throw new InvalidOperationException("Game Boy target local variables exceed the current prototype WRAM allocation.");
        }

        var address = nextVariableAddress++;
        variables.Add(declaration.Name, address);

        if (declaration.Initialization.HasValue)
        {
            EmitExpressionToA(declaration.Initialization.Value);
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(address);
    }

    private static bool IsByteBackedType(string type)
    {
        return type is "i8" or "u8" or "i16" or "u16" or "bool";
    }

    private void EmitExpressionStatement(ExpressionStatementSyntax expressionStatement)
    {
        switch (expressionStatement.Expression)
        {
            case AssignmentSyntax assignment:
                EmitAssignment(assignment);
                break;
            case FunctionCall call:
                EmitCall(call);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy expression statement '{expressionStatement.Expression.GetType().Name}'.");
        }
    }

    private void EmitAssignment(AssignmentSyntax assignment)
    {
        if (assignment.Left is not IdentifierLValue identifier)
        {
            throw new InvalidOperationException("Game Boy target only supports assignments to local variables.");
        }

        var address = VariableAddress(identifier.Identifier);
        EmitExpressionToA(assignment.Right);
        builder.StoreA(address);
    }

    private void EmitCall(FunctionCall call)
    {
        switch (call.Name)
        {
            case "video_init":
            case "video_present":
            case "palette_set":
            case "object_palette_set":
            case "tilemap_set":
            case "tilemap_fill":
            case "map_column":
            case "sprite_asset":
                break;
            case "input_poll":
                EmitInputPoll(call);
                break;
            case "tilemap_fill_column":
                EmitTilemapFillColumn(call);
                break;
            case "map_stream_column":
                EmitMapStreamColumn(call);
                break;
            case "video_wait_vblank":
                GameBoyVideoProgram.RequireArity(call, 0);
                GameBoyRomBuilder.EmitWaitVBlank(builder, builder.CreateLabel("wait_vblank"));
                break;
            case "sprite_set":
                EmitSpriteSet(call);
                break;
            case "sprite_draw":
                EmitSpriteDraw(call);
                break;
            case "scroll_set":
                EmitScrollSet(call);
                break;
            default:
                if (TryEmitUserFunction(call))
                {
                    break;
                }

                throw new InvalidOperationException($"Unsupported Game Boy video API call '{call.Name}'.");
        }
    }

    private bool TryEmitUserFunction(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        GameBoyVideoProgram.RequireParameterlessUserFunction("Game Boy", call, function);
        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitBlock(function.Block);
        }
        finally
        {
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private void EmitInputPoll(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);

        builder.LoadA(InputCurrentAddress);
        builder.StoreA(InputPreviousAddress);

        EmitReadJoypadNibble(0x10);
        builder.LoadBFromA();

        EmitReadJoypadNibble(0x20);
        builder.SwapA();
        builder.OrAFromB();
        builder.StoreA(InputCurrentAddress);
        EmitDeselectJoypad();

        foreach (var button in Buttons)
        {
            EmitUpdateButtonHoldTicks(button);
        }
    }

    private void EmitReadJoypadNibble(byte selector)
    {
        builder.LoadAImmediate(selector);
        builder.StoreHighRamA(0x00);
        for (var i = 0; i < JoypadSettleReadCount; i++)
        {
            builder.LoadHighRamA(0x00);
        }

        builder.ComplementA();
        builder.AndImmediate(0x0F);
    }

    private void EmitDeselectJoypad()
    {
        builder.LoadAImmediate(JoypadDeselect);
        builder.StoreHighRamA(0x00);
    }

    private void EmitUpdateButtonHoldTicks(GameBoyButton button)
    {
        var resetLabel = builder.CreateLabel("button_hold_reset");
        var endLabel = builder.CreateLabel("button_hold_end");

        builder.LoadA(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, resetLabel); // JP Z,resetLabel

        builder.LoadA(button.HoldTicksAddress);
        builder.CompareImmediate(0xFF);
        builder.JumpAbsolute(0xCA, endLabel);   // JP Z,endLabel
        builder.AddAImmediate(1);
        builder.StoreA(button.HoldTicksAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(resetLabel);
        builder.LoadAImmediate(0);
        builder.StoreA(button.HoldTicksAddress);
        builder.Label(endLabel);
    }

    private void EmitSpriteDraw(FunctionCall call)
    {
        var args = call.Parameters.ToList();
        if (args.Count is not 4 and not 5)
        {
            throw new InvalidOperationException($"sprite_draw expects 4 or 5 arguments, got {args.Count}.");
        }

        var assetName = GameBoyVideoProgram.IdentifierArg(args[0], "sprite_draw argument 1");
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        var flags = args.Count == 5 ? args[4] : null;
        var firstHardwareSprite = nextHardwareSprite;
        if (firstHardwareSprite + asset.Pieces.Count > 40)
        {
            throw new InvalidOperationException("Game Boy sprite_draw calls exceed the 40 hardware sprite OAM limit.");
        }

        nextHardwareSprite += asset.Pieces.Count;
        for (var pieceIndex = 0; pieceIndex < asset.Pieces.Count; pieceIndex++)
        {
            var piece = asset.Pieces[pieceIndex];
            var oamAddress = (ushort)(0xFE00 + (firstHardwareSprite + pieceIndex) * 4);

            EmitExpressionToA(args[2]);
            builder.AddAImmediate(16 + piece.YOffset);
            builder.StoreA(oamAddress);

            EmitSpriteDrawX(args[1], flags, asset, piece, (ushort)(oamAddress + 1));

            EmitExpressionToA(args[3]);
            EmitMultiplyAByConstant(asset.TilesPerFrame);
            builder.AddAImmediate(asset.FirstTile + piece.TileOffset);
            builder.StoreA((ushort)(oamAddress + 2));

            if (flags is null)
            {
                builder.LoadAImmediate(0);
            }
            else
            {
                EmitExpressionToA(flags);
            }

            builder.StoreA((ushort)(oamAddress + 3));
        }
    }

    private void EmitSpriteDrawX(
        ExpressionSyntax xExpression,
        ExpressionSyntax? flagsExpression,
        GameBoyCompiledSpriteAsset asset,
        GameBoyMetaspritePiece piece,
        ushort oamAddress)
    {
        var normalOffset = piece.XOffset;
        var flippedOffset = asset.LogicalWidth - 8 - piece.XOffset;
        if (flagsExpression is null || normalOffset == flippedOffset)
        {
            EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
            return;
        }

        var normalLabel = builder.CreateLabel("sprite_x_normal");
        var endLabel = builder.CreateLabel("sprite_x_end");

        EmitExpressionToA(flagsExpression);
        builder.AndImmediate(0x20);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, normalLabel); // JP Z,normalLabel

        EmitSpriteDrawXAtOffset(xExpression, flippedOffset, oamAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(normalLabel);
        EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
        builder.Label(endLabel);
    }

    private void EmitSpriteDrawXAtOffset(ExpressionSyntax xExpression, int offset, ushort oamAddress)
    {
        EmitExpressionToA(xExpression);
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

    private void EmitMapStreamColumn(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("map_stream_column requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var y = CheckedRange(GameBoyVideoProgram.ConstValue(args[2], "map_stream_column argument 3"), 0, 31, "map_stream_column argument 3");
        var height = CheckedRange(GameBoyVideoProgram.ConstValue(args[3], "map_stream_column argument 4"), 1, program.MapColumnHeight, "map_stream_column argument 4");

        for (var row = 0; row < height; row++)
        {
            EmitExpressionToA(args[1]);
            builder.LoadEFromA();
            builder.LoadDImmediate(0);
            builder.LoadHl(GameBoyRomBuilder.MapRowLabel(row));
            builder.AddHlDe();
            builder.LoadAFromHl();
            builder.LoadBFromA();

            var rowAddress = 0x9800 + (y + row) * 32;
            EmitExpressionToA(args[0]);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitTilemapFillColumn(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        var args = call.Parameters.ToList();
        var y = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "tilemap_fill_column argument 2"), 0, 31, "tilemap_fill_column argument 2");
        var height = CheckedRange(GameBoyVideoProgram.ConstValue(args[2], "tilemap_fill_column argument 3"), 1, 32 - y, "tilemap_fill_column argument 3");

        EmitExpressionToA(args[3]);
        builder.LoadBFromA();

        for (var row = y; row < y + height; row++)
        {
            var rowAddress = 0x9800 + row * 32;
            EmitExpressionToA(args[0]);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitScrollSet(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var args = call.Parameters.ToList();

        EmitExpressionToA(args[0]);
        builder.StoreHighRamA(0x43);

        EmitExpressionToA(args[1]);
        builder.StoreHighRamA(0x42);
    }

    private void EmitSpriteSet(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var spriteId = CheckedRange(GameBoyVideoProgram.ConstValue(args[0], "sprite_set argument 1"), 0, 39, "sprite_set argument 1");
        var oamAddress = (ushort)(0xFE00 + spriteId * 4);

        EmitExpressionToA(args[2]);
        builder.AddAImmediate(16);
        builder.StoreA(oamAddress);

        EmitExpressionToA(args[1]);
        builder.AddAImmediate(8);
        builder.StoreA((ushort)(oamAddress + 1));

        EmitExpressionToA(args[3]);
        builder.StoreA((ushort)(oamAddress + 2));

        EmitExpressionToA(args[4]);
        builder.StoreA((ushort)(oamAddress + 3));
    }

    private void EmitWhile(WhileSyntax whileSyntax)
    {
        var startLabel = builder.CreateLabel("while_start");
        var endLabel = builder.CreateLabel("while_end");

        builder.Label(startLabel);
        EmitConditionFalseJump(whileSyntax.Condition, endLabel);
        EmitBlock(whileSyntax.Body);
        builder.JumpAbsolute(startLabel);
        builder.Label(endLabel);
    }

    private void EmitIf(IfElseSyntax ifElseSyntax)
    {
        var falseLabel = builder.CreateLabel("if_false");
        var endLabel = builder.CreateLabel("if_end");

        EmitConditionFalseJump(ifElseSyntax.Condition, falseLabel);
        EmitBlock(ifElseSyntax.ThenBlock);
        if (ifElseSyntax.ElseBlock.HasValue)
        {
            builder.JumpAbsolute(endLabel);
            builder.Label(falseLabel);
            EmitBlock(ifElseSyntax.ElseBlock.Value);
            builder.Label(endLabel);
        }
        else
        {
            builder.Label(falseLabel);
        }
    }

    private void EmitConditionFalseJump(ExpressionSyntax condition, string falseLabel)
    {
        if (TryConst(condition, out var constant))
        {
            if (constant == 0)
            {
                builder.JumpAbsolute(falseLabel);
            }

            return;
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            switch (binary.Operator.Symbol)
            {
                case "==":
                    EmitCompareToConstant(binary.Left, binary.Right);
                    builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
                    return;
                case "!=":
                    EmitCompareToConstant(binary.Left, binary.Right);
                    builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
                    return;
                case "<":
                case "<=":
                case ">":
                case ">=":
                    EmitRelationalFalseJump(binary, falseLabel);
                    return;
            }
        }

        EmitExpressionToA(condition);
        builder.Emit(0xFE, 0x00);                   // CP $00
        builder.JumpAbsolute(0xCA, falseLabel);     // JP Z,falseLabel
    }

    private void EmitCompareToConstant(ExpressionSyntax left, ExpressionSyntax right)
    {
        if (TryConst(right, out var rightConstant))
        {
            EmitExpressionToA(left);
            builder.CompareImmediate(rightConstant);
            return;
        }

        if (TryConst(left, out var leftConstant))
        {
            EmitExpressionToA(right);
            builder.CompareImmediate(leftConstant);
            return;
        }

        throw new InvalidOperationException("Game Boy conditions currently require one side of == or != to be constant.");
    }

    private void EmitRelationalFalseJump(BinaryExpressionSyntax binary, string falseLabel)
    {
        if (TryConst(binary.Right, out var rightConstant))
        {
            EmitExpressionToA(binary.Left);
            builder.CompareImmediate(rightConstant);
            EmitRelationalFalseJump(binary.Operator.Symbol, falseLabel);
            return;
        }

        if (TryConst(binary.Left, out var leftConstant))
        {
            EmitExpressionToA(binary.Right);
            builder.CompareImmediate(leftConstant);
            EmitRelationalFalseJump(FlipRelationalOperator(binary.Operator.Symbol), falseLabel);
            return;
        }

        throw new InvalidOperationException("Game Boy relational conditions currently require one side to be constant.");
    }

    private void EmitRelationalFalseJump(string op, string falseLabel)
    {
        switch (op)
        {
            case "<":
                builder.JumpAbsolute(0xD2, falseLabel); // JP NC,falseLabel
                return;
            case "<=":
                EmitGreaterThanFalseJump(falseLabel);
                return;
            case ">":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
                return;
            case ">=":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy relational operator '{op}'.");
        }
    }

    private void EmitGreaterThanFalseJump(string falseLabel)
    {
        var trueLabel = builder.CreateLabel("rel_true");
        builder.JumpAbsolute(0xDA, trueLabel);      // JP C,trueLabel
        builder.JumpAbsolute(0xCA, trueLabel);      // JP Z,trueLabel
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private static string FlipRelationalOperator(string op)
    {
        return op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => throw new InvalidOperationException($"Unsupported Game Boy relational operator '{op}'."),
        };
    }

    private void EmitExpressionToA(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ConstantSyntax:
                builder.LoadAImmediate(GameBoyVideoProgram.ConstValue(expression, "constant"));
                break;
            case IdentifierSyntax { Identifier: "true" }:
                builder.LoadAImmediate(1);
                break;
            case IdentifierSyntax { Identifier: "false" }:
                builder.LoadAImmediate(0);
                break;
            case IdentifierSyntax identifier:
                builder.LoadA(VariableAddress(identifier.Identifier));
                break;
            case FunctionCall call:
                EmitValueCallToA(call);
                break;
            case BinaryExpressionSyntax binary:
                EmitBinaryExpressionToA(binary);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitValueCallToA(FunctionCall call)
    {
        switch (call.Name)
        {
            case "map_tile_at":
                EmitMapTileAt(call);
                break;
            case "button_pressed":
                EmitButtonPressed(call);
                break;
            case "button_down":
                EmitButtonDown(call);
                break;
            case "button_just_pressed":
                EmitButtonJustPressed(call);
                break;
            case "button_just_released":
                EmitButtonJustReleased(call);
                break;
            case "button_hold_ticks":
                EmitButtonHoldTicks(call);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy value API call '{call.Name}'.");
        }
    }

    private void EmitMapTileAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("map_tile_at requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var row = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "map_tile_at argument 2"), 0, program.MapColumnHeight - 1, "map_tile_at argument 2");

        EmitExpressionToA(args[0]);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(GameBoyRomBuilder.MapRowLabel(row));
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitButtonPressed(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_pressed argument 1");
        var pressedLabel = builder.CreateLabel("button_pressed");
        var endLabel = builder.CreateLabel("button_end");

        EmitReadJoypadNibble(button.Selector);
        builder.AndImmediate(button.Mask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, pressedLabel); // JP NZ,pressedLabel
        EmitDeselectJoypad();
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        EmitDeselectJoypad();
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitButtonDown(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        EmitButtonMaskToBool(InputCurrentAddress, ButtonArg(call, "button_down argument 1"));
    }

    private void EmitButtonJustPressed(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_pressed argument 1");
        var falseLabel = builder.CreateLabel("button_just_pressed_false");
        var endLabel = builder.CreateLabel("button_just_pressed_end");

        builder.LoadA(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel

        builder.LoadA(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonJustReleased(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_released argument 1");
        var falseLabel = builder.CreateLabel("button_just_released_false");
        var endLabel = builder.CreateLabel("button_just_released_end");

        builder.LoadA(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel

        builder.LoadA(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonHoldTicks(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        builder.LoadA(ButtonArg(call, "button_hold_ticks argument 1").HoldTicksAddress);
    }

    private void EmitButtonMaskToBool(ushort address, GameBoyButton button)
    {
        var pressedLabel = builder.CreateLabel("button_down");
        var endLabel = builder.CreateLabel("button_down_end");

        builder.LoadA(address);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, pressedLabel); // JP NZ,pressedLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private static GameBoyButton ButtonArg(FunctionCall call, string context)
    {
        var name = GameBoyVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), context);
        var button = Buttons.FirstOrDefault(button => button.Name == name);
        if (button.Name is null)
        {
            throw new InvalidOperationException($"Unsupported Game Boy button '{name}'.");
        }

        return button;
    }

    private void EmitBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        switch (binary.Operator.Symbol)
        {
            case "+":
                if (TryConst(binary.Right, out var addRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.AddAImmediate(addRight);
                    return;
                }

                if (TryConst(binary.Left, out var addLeft))
                {
                    EmitExpressionToA(binary.Right);
                    builder.AddAImmediate(addLeft);
                    return;
                }

                EmitExpressionToA(binary.Right);
                builder.LoadBFromA();
                EmitExpressionToA(binary.Left);
                builder.AddAFromB();
                return;
            case "-":
                if (TryConst(binary.Right, out var subtractRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.SubtractAImmediate(subtractRight);
                    return;
                }

                break;
        }

        throw new InvalidOperationException($"Unsupported Game Boy binary expression '{binary.Operator.Symbol}'.");
    }

    private bool TryConst(ExpressionSyntax expression, out int value)
    {
        if (expression is ConstantSyntax)
        {
            value = GameBoyVideoProgram.ConstValue(expression, "constant");
            return true;
        }

        if (expression is IdentifierSyntax { Identifier: "true" })
        {
            value = 1;
            return true;
        }

        if (expression is IdentifierSyntax { Identifier: "false" })
        {
            value = 0;
            return true;
        }

        value = 0;
        return false;
    }

    private readonly record struct GameBoyButton(string Name, byte Selector, byte Mask, byte SnapshotMask, ushort HoldTicksAddress);

    private ushort VariableAddress(string name)
    {
        if (!variables.TryGetValue(name, out var address))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return address;
    }

    private static int CheckedRange(int value, int min, int max, string context)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{context} must be between {min} and {max}.");
        }

        return value;
    }
}

internal sealed class GbBuilder
{
    private const int BaseAddress = 0x0150;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = [];
    private readonly List<(int Offset, string Label)> absoluteFixups = [];
    private readonly List<(int Offset, string Label)> relativeFixups = [];
    private int nextLabelId;

    public string CreateLabel(string prefix) => $"{prefix}_{nextLabelId++}";

    public void Label(string name) => labels[name] = bytes.Count;

    public void Emit(params byte[] values) => bytes.AddRange(values);

    public void LoadAImmediate(int value)
    {
        Emit(0x3E, (byte)value);
    }

    public void XorA()
    {
        Emit(0xAF);
    }

    public void LoadA(ushort address)
    {
        Emit(0xFA, (byte)(address & 0xFF), (byte)(address >> 8));
    }

    public void StoreA(ushort address)
    {
        Emit(0xEA, (byte)(address & 0xFF), (byte)(address >> 8));
    }

    public void StoreHighRamA(byte offset)
    {
        Emit(0xE0, offset);
    }

    public void LoadHighRamA(byte offset)
    {
        Emit(0xF0, offset);
    }

    public void ComplementA()
    {
        Emit(0x2F);
    }

    public void AndImmediate(int value)
    {
        Emit(0xE6, (byte)value);
    }

    public void SwapA()
    {
        Emit(0xCB, 0x37);
    }

    public void LoadBFromA()
    {
        Emit(0x47);
    }

    public void LoadAFromB()
    {
        Emit(0x78);
    }

    public void AddAFromB()
    {
        Emit(0x80);
    }

    public void OrAFromB()
    {
        Emit(0xB0);
    }

    public void LoadLFromA()
    {
        Emit(0x6F);
    }

    public void LoadHImmediate(int value)
    {
        Emit(0x26, (byte)value);
    }

    public void StoreHlA()
    {
        Emit(0x77);
    }

    public void AddAImmediate(int value)
    {
        Emit(0xC6, (byte)value);
    }

    public void SubtractAImmediate(int value)
    {
        Emit(0xD6, (byte)value);
    }

    public void CompareImmediate(int value)
    {
        Emit(0xFE, (byte)value);
    }

    public void LoadHl(ushort value)
    {
        Emit(0x21, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void LoadHl(string label)
    {
        Emit(0x21, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label));
    }

    public void LoadDImmediate(int value)
    {
        Emit(0x16, (byte)value);
    }

    public void LoadEFromA()
    {
        Emit(0x5F);
    }

    public void AddHlDe()
    {
        Emit(0x19);
    }

    public void LoadAFromHl()
    {
        Emit(0x7E);
    }

    public void LoadDe(string label)
    {
        Emit(0x11, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label));
    }

    public void LoadBc(ushort value)
    {
        Emit(0x01, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void JumpRelative(byte opcode, string label)
    {
        Emit(opcode, 0x00);
        relativeFixups.Add((bytes.Count - 1, label));
    }

    public void JumpAbsolute(string label)
    {
        JumpAbsolute(0xC3, label);
    }

    public void JumpAbsolute(byte opcode, string label)
    {
        Emit(opcode, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label));
    }

    public byte[] Build()
    {
        foreach (var fixup in absoluteFixups)
        {
            var address = AddressOf(fixup.Label);
            bytes[fixup.Offset] = (byte)(address & 0xFF);
            bytes[fixup.Offset + 1] = (byte)(address >> 8);
        }

        foreach (var fixup in relativeFixups)
        {
            var target = AddressOf(fixup.Label);
            var branchFrom = BaseAddress + fixup.Offset + 1;
            var delta = target - branchFrom;
            if (delta is < -128 or > 127)
            {
                throw new InvalidOperationException($"Relative jump to '{fixup.Label}' is out of range.");
            }

            bytes[fixup.Offset] = unchecked((byte)(sbyte)delta);
        }

        return bytes.ToArray();
    }

    private int AddressOf(string label)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown Game Boy ROM label '{label}'.");
        }

        return BaseAddress + offset;
    }
}
