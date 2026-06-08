using System.Globalization;
using System.Text;
using RetroSharp.Core.Sdk;
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
            throw new InvalidOperationException(
                $"Generated Game Boy program is {programBytes.Length} bytes, but only {RomSize - 0x0150} bytes fit in a 32 KiB ROM.");
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
        if (program.UsesWindowHud)
        {
            builder.LoadAImmediate(0);
            builder.StoreHighRamA(0x4A);            // WY=0
            builder.LoadAImmediate(7);
            builder.StoreHighRamA(0x4B);            // WX=7 maps to screen X=0
        }

        builder.LoadHl(0x8000);
        builder.LoadDe("tile_data");
        builder.LoadBc((ushort)tileData.Length);
        EmitCopyLoop(builder, "copy_tiles");

        builder.LoadHl(0x9800);
        builder.LoadDe("tilemap");
        builder.LoadBc(1024);
        EmitCopyLoop(builder, "copy_tilemap");

        if (program.UsesWindowHud)
        {
            builder.LoadHl(0x9C00);
            builder.LoadDe("window_tilemap");
            builder.LoadBc(1024);
            EmitCopyLoop(builder, "copy_window_tilemap");
        }

        EmitClearOam(builder, "clear_oam");

        builder.Emit(0x3E, (byte)(program.UsesWindowHud ? 0xF7 : 0x97));
        builder.Emit(0xE0, 0x40);                   // LDH ($40),A

        new GameBoyRuntimeCompiler(builder, program).Emit(program.MainBlock);

        builder.Label("forever");
        builder.JumpRelative(0x18, "forever");     // JR forever

        builder.Label("tile_data");
        builder.Emit(tileData);
        builder.Label("tilemap");
        builder.Emit(program.TileMap);
        if (program.UsesWindowHud)
        {
            builder.Label("window_tilemap");
            builder.Emit(program.WindowTileMap);
        }

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

        if (program.MapFlagColumnHeight == 0)
        {
            return;
        }

        var flagColumnCount = program.MapFlagColumns.Keys.Max() + 1;
        for (var row = 0; row < program.MapFlagColumnHeight; row++)
        {
            builder.Label(MapFlagRowLabel(row));
            for (var column = 0; column < flagColumnCount; column++)
            {
                var flags = program.MapFlagColumns.TryGetValue(column, out var flagColumn) ? flagColumn[row] : (byte)0;
                builder.Emit(flags);
            }
        }
    }

    internal static string MapRowLabel(int row) => $"map_row_{row}";

    internal static string MapFlagRowLabel(int row) => $"map_flags_row_{row}";

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
        WriteCloudTile(tiles, 1);
        WriteHillTile(tiles, 2);
        WriteSpikeTile(tiles, 3);
        WriteGroundTopTile(tiles, 4);
        WriteBrickTile(tiles, 5);

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

    private static void WriteCloudTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 0, 0, 0, 0, 1, 1, 0, 0);
        WriteTileRow(tiles, tile, 1, 0, 0, 1, 1, 1, 1, 1, 0);
        WriteTileRow(tiles, tile, 2, 0, 1, 1, 1, 1, 1, 1, 1);
        WriteTileRow(tiles, tile, 3, 1, 1, 1, 1, 1, 1, 1, 1);
        WriteTileRow(tiles, tile, 4, 0, 1, 1, 1, 1, 1, 1, 0);
        WriteTileRow(tiles, tile, 5, 0, 0, 1, 1, 1, 1, 0, 0);
        WriteTileRow(tiles, tile, 6, 0, 0, 0, 0, 0, 0, 0, 0);
        WriteTileRow(tiles, tile, 7, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static void WriteHillTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 0, 0, 0, 0, 2, 2, 0, 0);
        WriteTileRow(tiles, tile, 1, 0, 0, 0, 2, 2, 2, 2, 0);
        WriteTileRow(tiles, tile, 2, 0, 0, 2, 2, 1, 2, 2, 2);
        WriteTileRow(tiles, tile, 3, 0, 2, 2, 1, 2, 2, 1, 2);
        WriteTileRow(tiles, tile, 4, 2, 2, 2, 2, 2, 1, 2, 2);
        WriteTileRow(tiles, tile, 5, 2, 1, 2, 2, 2, 2, 2, 1);
        WriteTileRow(tiles, tile, 6, 2, 2, 2, 1, 2, 2, 2, 2);
        WriteTileRow(tiles, tile, 7, 2, 2, 2, 2, 2, 2, 2, 2);
    }

    private static void WriteSpikeTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 0, 0, 0, 3, 3, 0, 0, 0);
        WriteTileRow(tiles, tile, 1, 0, 0, 3, 3, 3, 3, 0, 0);
        WriteTileRow(tiles, tile, 2, 0, 3, 3, 3, 3, 3, 3, 0);
        WriteTileRow(tiles, tile, 3, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 4, 2, 2, 3, 2, 2, 3, 2, 2);
        WriteTileRow(tiles, tile, 5, 2, 3, 2, 2, 3, 2, 2, 3);
        WriteTileRow(tiles, tile, 6, 3, 2, 2, 3, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 7, 3, 3, 3, 3, 3, 3, 3, 3);
    }

    private static void WriteGroundTopTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 1, 1, 1, 1, 1, 1, 1, 1);
        WriteTileRow(tiles, tile, 1, 2, 1, 2, 1, 2, 1, 2, 1);
        WriteTileRow(tiles, tile, 2, 2, 2, 2, 2, 2, 2, 2, 2);
        WriteTileRow(tiles, tile, 3, 2, 3, 2, 2, 2, 3, 2, 2);
        WriteTileRow(tiles, tile, 4, 2, 2, 2, 3, 2, 2, 2, 3);
        WriteTileRow(tiles, tile, 5, 3, 2, 2, 2, 3, 2, 2, 2);
        WriteTileRow(tiles, tile, 6, 2, 2, 3, 2, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 7, 2, 2, 2, 2, 2, 2, 2, 2);
    }

    private static void WriteBrickTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 1, 3, 2, 2, 2, 3, 2, 2, 2);
        WriteTileRow(tiles, tile, 2, 3, 2, 2, 2, 3, 2, 2, 2);
        WriteTileRow(tiles, tile, 3, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 4, 2, 2, 3, 2, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 5, 2, 2, 3, 2, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 6, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 7, 2, 2, 2, 3, 2, 2, 2, 3);
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
    private const ushort RuntimeReservedStateAddress = 0xC0E0;
    private const ushort CameraXLowAddress = 0xC0E0;
    private const ushort CameraXHighAddress = 0xC0E1;
    private const ushort CameraFineXAddress = 0xC0E2;
    private const ushort CameraScreenLeftColumnAddress = 0xC0E3;
    private const ushort CameraRightBackgroundColumnAddress = 0xC0E4;
    private const ushort CameraLeftBackgroundColumnAddress = 0xC0E5;
    private const ushort CameraRightSourceColumnAddress = 0xC0E6;
    private const ushort CameraLeftSourceColumnAddress = 0xC0E7;
    private const ushort CameraYLowAddress = 0xC0E8;
    private const ushort CameraYHighAddress = 0xC0E9;
    private const ushort CameraFineYAddress = 0xC0EA;
    private const ushort CameraTopBackgroundRowAddress = 0xC0EB;
    private const ushort CameraBottomBackgroundRowAddress = 0xC0EC;
    private const ushort CameraTopSourceRowAddress = 0xC0ED;
    private const ushort CameraBottomSourceRowAddress = 0xC0EE;
    private const ushort InputCurrentAddress = 0xC0F0;
    private const ushort InputPreviousAddress = 0xC0F1;
    private const ushort InputHoldTicksStartAddress = 0xC0F2;
    private const int VisibleScreenTileWidth = 20;
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
    private int? cameraMapWidth;
    private int? cameraStreamY;
    private int? cameraStreamHeight;

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

        if (nextVariableAddress >= RuntimeReservedStateAddress)
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
            case "world_column":
            case "world_flags":
            case "world_map":
            case "sprite_asset":
                break;
            case "animation_clip":
                break;
            case "hud_set_tile":
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
            case "camera_init":
                EmitCameraInit(call);
                break;
            case "camera_set_position":
                EmitCameraSetPosition(call);
                break;
            case "camera_apply":
                EmitCameraApply(call);
                break;
            case "camera_move_right":
                EmitCameraMoveRight(call);
                break;
            case "camera_move_left":
                EmitCameraMoveLeft(call);
                break;
            case "video_wait_vblank":
                GameBoyVideoProgram.RequireArity(call, 0);
                EmitSdkOperation(new Sdk2DOperation.WaitFrame());
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

    private void EmitSdkOperation(Sdk2DOperation operation)
    {
        GameBoySdkOperationLowerer.Emit(builder, operation);
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
        if (args.Count is not 4 and not 5 and not 6)
        {
            throw new InvalidOperationException($"sprite_draw expects 4, 5, or 6 arguments, got {args.Count}.");
        }

        var assetName = GameBoyVideoProgram.IdentifierArg(args[0], "sprite_draw argument 1");
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        var options = SpriteDrawOptionsFromArguments(args);
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

            EmitSpriteDrawX(args[1], options.FlipX, asset, piece, (ushort)(oamAddress + 1));

            EmitExpressionToA(args[3]);
            EmitMultiplyAByConstant(asset.TilesPerFrame);
            builder.AddAImmediate(asset.FirstTile + piece.TileOffset);
            builder.StoreA((ushort)(oamAddress + 2));

            EmitSpriteDrawAttributes(options.FlipX, options.PaletteSlot, (ushort)(oamAddress + 3));
        }
    }

    private SpriteDrawOptions SpriteDrawOptionsFromArguments(IReadOnlyList<ExpressionSyntax> args)
    {
        return new SpriteDrawOptions(
            FlipX: SpriteDrawFlipXArgument(args),
            PaletteSlot: SpriteDrawPaletteSlotArgument(args));
    }

    private ExpressionSyntax? SpriteDrawFlipXArgument(IReadOnlyList<ExpressionSyntax> args)
    {
        if (args.Count == 4)
        {
            return null;
        }

        var flipX = args[4];
        if (TryConst(flipX, out var value) && value is not 0 and not 1)
        {
            throw new InvalidOperationException("sprite_draw argument 5 is portable flipX and must be 0, 1, true, false, or a local bool-like value. Use sprite_set for raw Game Boy OAM attributes.");
        }

        return flipX;
    }

    private static int SpriteDrawPaletteSlotArgument(IReadOnlyList<ExpressionSyntax> args)
    {
        if (args.Count < 6)
        {
            return 0;
        }

        var slot = GameBoyVideoProgram.ConstValue(args[5], "sprite_draw argument 6");
        if (slot < 0 || slot >= GameBoyTarget.Capabilities.SpritePaletteSlots)
        {
            throw new InvalidOperationException(
                $"Target '{GameBoyTarget.Capabilities.Name}' supports sprite palette slots 0..{GameBoyTarget.Capabilities.SpritePaletteSlots - 1}, but slot {slot} was requested.");
        }

        return slot;
    }

    private void EmitSpriteDrawX(
        ExpressionSyntax xExpression,
        ExpressionSyntax? flipXExpression,
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

        EmitExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, normalLabel); // JP Z,normalLabel

        EmitSpriteDrawXAtOffset(xExpression, flippedOffset, oamAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(normalLabel);
        EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
        builder.Label(endLabel);
    }

    private void EmitSpriteDrawAttributes(ExpressionSyntax? flipXExpression, int paletteSlot, ushort oamAddress)
    {
        var paletteAttribute = SpritePaletteAttribute(paletteSlot);
        if (flipXExpression is null || (TryConst(flipXExpression, out var constant) && constant == 0))
        {
            builder.LoadAImmediate(paletteAttribute);
            builder.StoreA(oamAddress);
            return;
        }

        if (TryConst(flipXExpression, out _))
        {
            builder.LoadAImmediate(paletteAttribute | 0x20);
            builder.StoreA(oamAddress);
            return;
        }

        var noFlipLabel = builder.CreateLabel("sprite_flags_no_flip");
        var storeLabel = builder.CreateLabel("sprite_flags_store");

        EmitExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, noFlipLabel); // JP Z,noFlipLabel

        builder.LoadAImmediate(paletteAttribute | 0x20);
        builder.JumpAbsolute(storeLabel);

        builder.Label(noFlipLabel);
        builder.LoadAImmediate(paletteAttribute);

        builder.Label(storeLabel);
        builder.StoreA(oamAddress);
    }

    private static int SpritePaletteAttribute(int paletteSlot)
    {
        return (paletteSlot & 0x01) << 4;
    }

    private readonly record struct SpriteDrawOptions(ExpressionSyntax? FlipX, int PaletteSlot);

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

    private void EmitCameraInit(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 3);
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("camera_init requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var mapWidth = CheckedRange(GameBoyVideoProgram.ConstValue(args[0], "camera_init argument 1"), 1, 255, "camera_init argument 1");
        var mapDataColumnCount = program.MapColumns.Keys.Max() + 1;
        if (mapWidth > mapDataColumnCount)
        {
            throw new InvalidOperationException($"camera_init argument 1 must not exceed the declared map column count ({mapDataColumnCount}).");
        }

        var y = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "camera_init argument 2"), 0, 31, "camera_init argument 2");
        var height = CheckedRange(GameBoyVideoProgram.ConstValue(args[2], "camera_init argument 3"), 1, program.MapColumnHeight, "camera_init argument 3");
        if (y + height > 32)
        {
            throw new InvalidOperationException("camera_init stream area exceeds the Game Boy background tilemap height.");
        }

        cameraMapWidth = mapWidth;
        cameraStreamY = y;
        cameraStreamHeight = height;

        builder.LoadAImmediate(0);
        builder.StoreA(CameraXLowAddress);
        builder.StoreA(CameraXHighAddress);
        builder.StoreA(CameraFineXAddress);
        builder.StoreA(CameraScreenLeftColumnAddress);

        builder.LoadAImmediate(VisibleScreenTileWidth);
        builder.StoreA(CameraRightBackgroundColumnAddress);
        builder.LoadAImmediate(31);
        builder.StoreA(CameraLeftBackgroundColumnAddress);
        builder.LoadAImmediate(VisibleScreenTileWidth % mapWidth);
        builder.StoreA(CameraRightSourceColumnAddress);
        builder.LoadAImmediate(mapWidth - 1);
        builder.StoreA(CameraLeftSourceColumnAddress);

        builder.LoadAImmediate(0);
        builder.StoreA(CameraYLowAddress);
        builder.StoreA(CameraYHighAddress);
        builder.StoreA(CameraFineYAddress);

        builder.LoadAImmediate(y);
        builder.StoreA(CameraTopBackgroundRowAddress);
        builder.LoadAImmediate((y + height) % 32);
        builder.StoreA(CameraBottomBackgroundRowAddress);
        builder.LoadAImmediate(0);
        builder.StoreA(CameraTopSourceRowAddress);
        builder.LoadAImmediate(height % program.MapColumnHeight);
        builder.StoreA(CameraBottomSourceRowAddress);
    }

    private void EmitCameraSetPosition(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var args = call.Parameters.ToList();
        var config = EnsureCameraConfigured(call.Name);

        EmitCameraSetAxisPosition(
            args[0],
            CameraXLowAddress,
            () => EmitCameraMoveLeftStep(config),
            () => EmitCameraMoveRightStep(config),
            "camera_set_position_right",
            "camera_set_position_x_end");

        EmitCameraSetAxisPosition(
            args[1],
            CameraYLowAddress,
            () => EmitCameraMoveUpStep(config),
            () => EmitCameraMoveDownStep(config),
            "camera_set_position_down",
            "camera_set_position_y_end");
    }

    private void EmitCameraApply(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);
        EnsureCameraConfigured(call.Name);

        builder.LoadA(CameraXLowAddress);
        builder.StoreHighRamA(0x43);
        builder.LoadA(CameraYLowAddress);
        builder.StoreHighRamA(0x42);
    }

    private void EmitCameraSetAxisPosition(
        ExpressionSyntax requestedPosition,
        ushort currentLowAddress,
        Action moveNegative,
        Action movePositive,
        string positiveLabelName,
        string endLabelName)
    {
        var movePositiveLabel = builder.CreateLabel(positiveLabelName);
        var endLabel = builder.CreateLabel(endLabelName);

        EmitExpressionToA(requestedPosition);
        builder.LoadBFromA();
        builder.LoadA(currentLowAddress);
        builder.LoadCFromA();
        builder.LoadAFromB();
        builder.SubtractAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel); // JP Z,endLabel
        builder.CompareImmediate(128);
        builder.JumpAbsolute(0xDA, movePositiveLabel); // JP C,movePositiveLabel

        moveNegative();
        builder.JumpAbsolute(endLabel);

        builder.Label(movePositiveLabel);
        movePositive();
        builder.Label(endLabel);
    }

    private void EmitCameraMoveRight(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraMoveRightStep(config);
    }

    private void EmitCameraMoveRightStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_right_end");

        EmitIncrement16(CameraXLowAddress, CameraXHighAddress);
        builder.LoadA(CameraFineXAddress);
        builder.AddAImmediate(1);
        builder.StoreA(CameraFineXAddress);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(0);
        builder.StoreA(CameraFineXAddress);
        EmitMapStreamColumnFromAddresses(CameraRightBackgroundColumnAddress, CameraRightSourceColumnAddress, config.StreamY, config.StreamHeight);
        EmitIncrementAddressModulo(CameraRightBackgroundColumnAddress, 32);
        EmitIncrementAddressModulo(CameraLeftBackgroundColumnAddress, 32);
        EmitIncrementAddressModulo(CameraScreenLeftColumnAddress, config.MapWidth);
        EmitIncrementAddressModulo(CameraRightSourceColumnAddress, config.MapWidth);
        EmitIncrementAddressModulo(CameraLeftSourceColumnAddress, config.MapWidth);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveLeft(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraMoveLeftStep(config);
    }

    private void EmitCameraMoveLeftStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_left_end");

        EmitDecrement16(CameraXLowAddress, CameraXHighAddress);
        builder.LoadA(CameraFineXAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(CameraFineXAddress);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(7);
        builder.StoreA(CameraFineXAddress);
        EmitMapStreamColumnFromAddresses(CameraLeftBackgroundColumnAddress, CameraLeftSourceColumnAddress, config.StreamY, config.StreamHeight);
        EmitDecrementAddressModulo(CameraRightBackgroundColumnAddress, 32);
        EmitDecrementAddressModulo(CameraLeftBackgroundColumnAddress, 32);
        EmitDecrementAddressModulo(CameraScreenLeftColumnAddress, config.MapWidth);
        EmitDecrementAddressModulo(CameraRightSourceColumnAddress, config.MapWidth);
        EmitDecrementAddressModulo(CameraLeftSourceColumnAddress, config.MapWidth);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveDownStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_down_end");

        EmitIncrement16(CameraYLowAddress, CameraYHighAddress);
        builder.LoadA(CameraFineYAddress);
        builder.AddAImmediate(1);
        builder.StoreA(CameraFineYAddress);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(0);
        builder.StoreA(CameraFineYAddress);
        EmitMapStreamRowFromSourceRowAddress(CameraBottomBackgroundRowAddress, CameraBottomSourceRowAddress, config);
        EmitIncrementAddressModulo(CameraTopBackgroundRowAddress, 32);
        EmitIncrementAddressModulo(CameraBottomBackgroundRowAddress, 32);
        EmitIncrementAddressModulo(CameraTopSourceRowAddress, config.SourceHeight);
        EmitIncrementAddressModulo(CameraBottomSourceRowAddress, config.SourceHeight);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveUpStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_up_end");

        EmitDecrement16(CameraYLowAddress, CameraYHighAddress);
        builder.LoadA(CameraFineYAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(CameraFineYAddress);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(7);
        builder.StoreA(CameraFineYAddress);
        EmitDecrementAddressModulo(CameraTopBackgroundRowAddress, 32);
        EmitDecrementAddressModulo(CameraBottomBackgroundRowAddress, 32);
        EmitDecrementAddressModulo(CameraTopSourceRowAddress, config.SourceHeight);
        EmitDecrementAddressModulo(CameraBottomSourceRowAddress, config.SourceHeight);
        EmitMapStreamRowFromSourceRowAddress(CameraTopBackgroundRowAddress, CameraTopSourceRowAddress, config);

        builder.Label(endLabel);
    }

    private void EmitMapStreamColumnFromAddresses(ushort targetColumnAddress, ushort sourceColumnAddress, int y, int height)
    {
        for (var row = 0; row < height; row++)
        {
            builder.LoadA(sourceColumnAddress);
            builder.LoadEFromA();
            builder.LoadDImmediate(0);
            builder.LoadHl(GameBoyRomBuilder.MapRowLabel(row));
            builder.AddHlDe();
            builder.LoadAFromHl();
            builder.LoadBFromA();

            var rowAddress = 0x9800 + (y + row) * 32;
            builder.LoadA(targetColumnAddress);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitMapStreamRowFromSourceRowAddress(ushort targetRowAddress, ushort sourceRowAddress, CameraConfig config)
    {
        Sdk2DOperationValidator.Validate(
            GameBoyTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(TargetRow: 0, SourceRow: 0, X: 0, Width: VisibleScreenTileWidth));

        var endLabel = builder.CreateLabel("map_stream_row_end");

        for (var sourceRow = 0; sourceRow < config.SourceHeight; sourceRow++)
        {
            var nextLabel = builder.CreateLabel("map_stream_row_next");
            builder.LoadA(sourceRowAddress);
            builder.CompareImmediate(sourceRow);
            builder.JumpAbsolute(0xC2, nextLabel); // JP NZ,nextLabel

            EmitMapStreamRow(sourceRow, targetRowAddress, config.MapWidth);
            builder.JumpAbsolute(endLabel);
            builder.Label(nextLabel);
        }

        builder.Label(endLabel);
    }

    private void EmitMapStreamRow(int sourceRow, ushort targetRowAddress, int mapWidth)
    {
        for (var screenColumn = 0; screenColumn < VisibleScreenTileWidth; screenColumn++)
        {
            EmitCameraTileColumnAt(new ConstantSyntax(screenColumn.ToString(CultureInfo.InvariantCulture)), mapWidth);
            EmitMapTileAtSourceColumnInA(sourceRow);
            builder.LoadBFromA();
            EmitVisibleBackgroundColumnToC(screenColumn);
            EmitBackgroundTileAddressToHl(targetRowAddress);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitVisibleBackgroundColumnToC(int screenColumn)
    {
        var endLabel = builder.CreateLabel("camera_row_target_column_end");

        builder.LoadA(CameraLeftBackgroundColumnAddress);
        builder.AddAImmediate(1 + screenColumn);
        builder.CompareImmediate(32);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(32);
        builder.Label(endLabel);
        builder.LoadCFromA();
    }

    private void EmitBackgroundTileAddressToHl(ushort rowAddress)
    {
        EmitBackgroundTileAddressHighToH(rowAddress);

        builder.LoadA(rowAddress);
        for (var i = 0; i < 5; i++)
        {
            builder.AddAFromA();
        }

        builder.AddAFromC();
        builder.LoadLFromA();
    }

    private void EmitBackgroundTileAddressHighToH(ushort rowAddress)
    {
        var high98Label = builder.CreateLabel("camera_row_high_98");
        var high99Label = builder.CreateLabel("camera_row_high_99");
        var high9ALabel = builder.CreateLabel("camera_row_high_9a");
        var endLabel = builder.CreateLabel("camera_row_high_end");

        builder.LoadA(rowAddress);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xDA, high98Label); // JP C,high98Label
        builder.CompareImmediate(16);
        builder.JumpAbsolute(0xDA, high99Label); // JP C,high99Label
        builder.CompareImmediate(24);
        builder.JumpAbsolute(0xDA, high9ALabel); // JP C,high9ALabel

        builder.LoadHImmediate(0x9B);
        builder.JumpAbsolute(endLabel);
        builder.Label(high98Label);
        builder.LoadHImmediate(0x98);
        builder.JumpAbsolute(endLabel);
        builder.Label(high99Label);
        builder.LoadHImmediate(0x99);
        builder.JumpAbsolute(endLabel);
        builder.Label(high9ALabel);
        builder.LoadHImmediate(0x9A);
        builder.Label(endLabel);
    }

    private void EmitIncrement16(ushort lowAddress, ushort highAddress)
    {
        var endLabel = builder.CreateLabel("increment16_end");

        builder.LoadA(lowAddress);
        builder.AddAImmediate(1);
        builder.StoreA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadA(highAddress);
        builder.AddAImmediate(1);
        builder.StoreA(highAddress);
        builder.Label(endLabel);
    }

    private void EmitDecrement16(ushort lowAddress, ushort highAddress)
    {
        var noBorrowLabel = builder.CreateLabel("decrement16_no_borrow");

        builder.LoadA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, noBorrowLabel); // JP NZ,noBorrowLabel

        builder.LoadA(highAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(highAddress);

        builder.Label(noBorrowLabel);
        builder.LoadA(lowAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(lowAddress);
    }

    private void EmitIncrementAddressModulo(ushort address, int modulo)
    {
        var endLabel = builder.CreateLabel("increment_modulo_end");

        builder.LoadA(address);
        builder.AddAImmediate(1);
        builder.StoreA(address);
        builder.CompareImmediate(modulo);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadAImmediate(0);
        builder.StoreA(address);
        builder.Label(endLabel);
    }

    private void EmitDecrementAddressModulo(ushort address, int modulo)
    {
        var endLabel = builder.CreateLabel("decrement_modulo_end");

        builder.LoadA(address);
        builder.SubtractAImmediate(1);
        builder.StoreA(address);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadAImmediate(modulo - 1);
        builder.StoreA(address);
        builder.Label(endLabel);
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
            case "map_flags_at":
                EmitMapFlagsAt(call);
                break;
            case "world_tile_flags_at":
                EmitWorldTileFlagsAt(call);
                break;
            case "collision_aabb_tiles":
                EmitCollisionAabbTiles(call);
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
            case "sprite_width":
                EmitSpriteWidth(call);
                break;
            case "animation_frame":
                EmitAnimationFrame(call);
                break;
            case "camera_tile_column_at":
                EmitCameraTileColumnAt(call);
                break;
            case "camera_span_tile_at":
                EmitCameraSpanTileAt(call);
                break;
            case "camera_span_has_tile":
                EmitCameraSpanHasTile(call);
                break;
            case "camera_span_has_flags":
                EmitCameraSpanHasFlags(call);
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

    private void EmitMapFlagsAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        if (program.MapFlagColumnHeight == 0)
        {
            throw new InvalidOperationException("map_flags_at requires world_map collision flag data.");
        }

        var args = call.Parameters.ToList();
        var row = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "map_flags_at argument 2"), 0, program.MapFlagColumnHeight - 1, "map_flags_at argument 2");

        EmitExpressionToA(args[0]);
        EmitMapFlagsAtSourceColumnInA(row);
    }

    private void EmitWorldTileFlagsAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var args = call.Parameters.ToList();
        EmitWorldTileFlagsAt(args[0], 0, args[1], 0, call.Name);
    }

    private void EmitWorldTileFlagsAt(ExpressionSyntax worldX, int worldXOffset, ExpressionSyntax worldY, int worldYOffset, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("world_tile_flags_oob");
        var endLabel = builder.CreateLabel("world_tile_flags_end");

        EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
        builder.CompareImmediate(worldMap.Width);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadBFromA();

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();

        for (var row = 0; row < worldMap.Height; row++)
        {
            var nextRowLabel = builder.CreateLabel("world_tile_flags_next_row");
            builder.LoadAFromC();
            builder.CompareImmediate(row);
            builder.JumpAbsolute(0xC2, nextRowLabel); // JP NZ,nextRowLabel
            builder.LoadAFromB();
            EmitMapFlagsAtSourceColumnInA(row);
            builder.JumpAbsolute(endLabel);
            builder.Label(nextRowLabel);
        }

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private WorldMap2D WorldMapForFlagQuery(string callName)
    {
        return program.WorldMap
               ?? throw new InvalidOperationException($"{callName} requires world_map collision flag data.");
    }

    private void EmitWorldPixelToTileCoordinate(ExpressionSyntax expression, int offset)
    {
        EmitExpressionToA(expression);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
    }

    private void EmitCollisionAabbTiles(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        _ = WorldMapForFlagQuery(call.Name);
        var args = call.Parameters.ToList();
        var width = CheckedRange(ConstRuntimeValue(args[2], "collision_aabb_tiles argument 3"), 0, 255, "collision_aabb_tiles argument 3");
        var height = CheckedRange(ConstRuntimeValue(args[3], "collision_aabb_tiles argument 4"), 0, 255, "collision_aabb_tiles argument 4");
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        var flags = CheckedRange(GameBoyVideoProgram.ConstValue(args[4], "collision_aabb_tiles argument 5"), 0, allowedFlags, "collision_aabb_tiles argument 5");
        if (width == 0 || height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        var foundLabel = builder.CreateLabel("collision_aabb_tiles_found");
        var endLabel = builder.CreateLabel("collision_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitWorldTileFlagsAt(args[0], xOffset, args[1], yOffset, call.Name);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
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

    private void EmitSpriteWidth(FunctionCall call)
    {
        builder.LoadAImmediate(SpriteWidth(call));
    }

    private void EmitAnimationFrame(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
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

    private void EmitCameraTileColumnAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraTileColumnAt(call.Parameters.ElementAt(0), config.MapWidth);
    }

    private void EmitCameraTileColumnAt(ExpressionSyntax screenColumnExpression, int mapWidth)
    {
        var wrapLabel = builder.CreateLabel("camera_tile_column_wrap");
        var endLabel = builder.CreateLabel("camera_tile_column_end");

        EmitExpressionToA(screenColumnExpression);
        builder.LoadBFromA();
        builder.LoadA(CameraScreenLeftColumnAddress);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitCameraSpanTileAt(FunctionCall call)
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

    private void EmitCameraSpanHasTile(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_has_tile");
        var tile = CheckedRange(ConstRuntimeValue(call.Parameters.ElementAt(3), "camera_span_has_tile argument 4"), 0, 255, "camera_span_has_tile argument 4");
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

    private void EmitCameraSpanHasFlags(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        if (program.MapFlagColumnHeight == 0)
        {
            throw new InvalidOperationException("camera_span_has_flags requires world_map collision flag data.");
        }

        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_has_flags");
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        var flags = CheckedRange(ConstRuntimeValue(call.Parameters.ElementAt(3), "camera_span_has_flags argument 4"), 1, allowedFlags, "camera_span_has_flags argument 4");
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

    private CameraSpanInfo BuildCameraSpan(FunctionCall call, int mapWidth, string context)
    {
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException($"{context} requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var screenX = CheckedRange(ConstRuntimeValue(args[0], $"{context} argument 1"), 0, 255, $"{context} argument 1");
        var width = CheckedRange(ConstRuntimeValue(args[1], $"{context} argument 2"), 1, 255, $"{context} argument 2");
        var row = CheckedRange(ConstRuntimeValue(args[2], $"{context} argument 3"), 0, program.MapColumnHeight - 1, $"{context} argument 3");
        var firstScreenColumn = screenX / 8;
        var lastScreenColumn = (screenX + width - 1) / 8;
        if (lastScreenColumn > 31)
        {
            throw new InvalidOperationException($"{context} span must fit within the Game Boy background tilemap width.");
        }

        if (lastScreenColumn - firstScreenColumn + 1 > mapWidth)
        {
            throw new InvalidOperationException($"{context} span must not cover more columns than the configured camera map width.");
        }

        return new CameraSpanInfo(firstScreenColumn, lastScreenColumn, row);
    }

    private void EmitMapTileAtSourceColumnInA(int row)
    {
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(GameBoyRomBuilder.MapRowLabel(row));
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitMapFlagsAtSourceColumnInA(int row)
    {
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(GameBoyRomBuilder.MapFlagRowLabel(row));
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

    private int ConstRuntimeValue(ExpressionSyntax expression, string context)
    {
        if (expression is FunctionCall { Name: "sprite_width" } spriteWidthCall)
        {
            return SpriteWidth(spriteWidthCall);
        }

        return GameBoyVideoProgram.ConstValue(expression, context);
    }

    private int SpriteWidth(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var assetName = GameBoyVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sprite_width argument 1");
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        return asset.LogicalWidth;
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

    private CameraConfig EnsureCameraConfigured(string callName)
    {
        if (cameraMapWidth is not { } mapWidth || cameraStreamY is not { } streamY || cameraStreamHeight is not { } streamHeight)
        {
            throw new InvalidOperationException($"{callName} requires camera_init(...) to be emitted first.");
        }

        return new CameraConfig(mapWidth, streamY, streamHeight, program.MapColumnHeight);
    }

    private readonly record struct CameraConfig(int MapWidth, int StreamY, int StreamHeight, int SourceHeight);

    private readonly record struct CameraSpanInfo(int FirstScreenColumn, int LastScreenColumn, int Row);
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

    public void ShiftRightLogicalA()
    {
        Emit(0xCB, 0x3F);
    }

    public void LoadBFromA()
    {
        Emit(0x47);
    }

    public void LoadCFromA()
    {
        Emit(0x4F);
    }

    public void LoadAFromB()
    {
        Emit(0x78);
    }

    public void LoadAFromC()
    {
        Emit(0x79);
    }

    public void AddAFromB()
    {
        Emit(0x80);
    }

    public void AddAFromC()
    {
        Emit(0x81);
    }

    public void AddAFromA()
    {
        Emit(0x87);
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

    public void SubtractAFromC()
    {
        Emit(0x91);
    }

    public void CompareImmediate(int value)
    {
        Emit(0xFE, (byte)value);
    }

    public void CompareB()
    {
        Emit(0xB8);
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
