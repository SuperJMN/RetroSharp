using RetroSharp.Parser;

namespace RetroSharp.NES;

internal static class NesRomBuilder
{
    private const int PrgRomSize = 16 * 1024;
    private const int ChrRomSize = 8 * 1024;

    public static byte[] Build(NesVideoProgram program)
    {
        var prg = BuildPrgRom(program);
        var chr = BuildChrRom(program);

        var rom = new byte[16 + PrgRomSize + ChrRomSize];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 1;

        prg.CopyTo(rom, 16);
        chr.CopyTo(rom, 16 + PrgRomSize);
        return rom;
    }

    private static byte[] BuildPrgRom(NesVideoProgram program)
    {
        var builder = new PrgBuilder();

        builder.Emit(0x78);                         // SEI
        builder.Emit(0xD8);                         // CLD
        builder.Emit(0xA2, 0x40);                   // LDX #$40
        builder.Emit(0x8E, 0x17, 0x40);             // STX $4017
        builder.Emit(0xA2, 0xFF);                   // LDX #$FF
        builder.Emit(0x9A);                         // TXS
        builder.Emit(0xE8);                         // INX
        builder.Emit(0x8E, 0x00, 0x20);             // STX $2000
        builder.Emit(0x8E, 0x01, 0x20);             // STX $2001
        builder.Emit(0x8E, 0x10, 0x40);             // STX $4010

        EmitWaitVBlank(builder, "vblank1");
        EmitWaitVBlank(builder, "vblank2");
        EmitPaletteUpload(builder);
        EmitNameTableUpload(builder);

        var runtimeCompiler = new NesRuntimeCompiler(builder, program);
        runtimeCompiler.EmitInitialization();

        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x05, 0x20);             // STA $2005
        builder.Emit(0x8D, 0x05, 0x20);             // STA $2005
        builder.Emit(0x8D, 0x00, 0x20);             // STA $2000
        builder.Emit(0xA9, 0x1E);                   // LDA #$1E
        builder.Emit(0x8D, 0x01, 0x20);             // STA $2001

        runtimeCompiler.Emit(program.MainBlock);

        builder.Label("forever");
        builder.JumpAbsolute("forever");

        builder.Label("palette");
        builder.Emit(program.Palette);
        builder.Label("nametable");
        builder.Emit(program.NameTable);

        var prg = new byte[PrgRomSize];
        var code = builder.Build();
        if (code.Length > PrgRomSize - 6)
        {
            throw new InvalidOperationException("NES PRG ROM overflow.");
        }

        code.CopyTo(prg, 0);
        SetVector(prg, 0x3FFA, 0xC000);
        SetVector(prg, 0x3FFC, 0xC000);
        SetVector(prg, 0x3FFE, 0xC000);
        return prg;
    }

    private static void EmitWaitVBlank(PrgBuilder builder, string label)
    {
        builder.Label(label);
        builder.Emit(0x2C, 0x02, 0x20);             // BIT $2002
        builder.BranchRelative(0x10, label);        // BPL label
    }

    private static void EmitPaletteUpload(PrgBuilder builder)
    {
        builder.Emit(0xAD, 0x02, 0x20);             // LDA $2002
        builder.Emit(0xA9, 0x3F);                   // LDA #$3F
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA2, 0x00);                   // LDX #$00
        builder.Label("palette_loop");
        builder.LdaAbsoluteX("palette");
        builder.Emit(0x8D, 0x07, 0x20);             // STA $2007
        builder.Emit(0xE8);                         // INX
        builder.Emit(0xE0, 0x20);                   // CPX #$20
        builder.BranchRelative(0xD0, "palette_loop"); // BNE palette_loop
    }

    private static void EmitNameTableUpload(PrgBuilder builder)
    {
        builder.Emit(0xAD, 0x02, 0x20);             // LDA $2002
        builder.Emit(0xA9, 0x20);                   // LDA #$20
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006

        for (var page = 0; page < 4; page++)
        {
            builder.Emit(0xA2, 0x00);               // LDX #$00
            var label = $"nametable_loop_{page}";
            builder.Label(label);
            builder.LdaAbsoluteX("nametable", page * 256);
            builder.Emit(0x8D, 0x07, 0x20);         // STA $2007
            builder.Emit(0xE8);                     // INX
            builder.BranchRelative(0xD0, label);    // BNE label
        }
    }

    private static byte[] BuildChrRom(NesVideoProgram program)
    {
        var chr = new byte[ChrRomSize];
        WriteSolidTile(chr, 1, 1);
        WriteSolidTile(chr, 2, 2);
        WriteSolidTile(chr, 3, 3);
        WriteCheckerTile(chr, 4, 1, 2);
        WriteFrameTile(chr, 5, 3);
        foreach (var asset in program.SpriteAssetsInLoadOrder)
        {
            var offset = asset.FirstTile * 16;
            if (offset + asset.TileData.Length > chr.Length)
            {
                throw new InvalidOperationException($"NES sprite asset '{asset.Name}' exceeds CHR ROM size.");
            }

            asset.TileData.CopyTo(chr, offset);
        }

        return chr;
    }

    private static void WriteSolidTile(byte[] chr, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            chr[tile * 16 + row] = (byte)((color & 1) != 0 ? 0xFF : 0x00);
            chr[tile * 16 + 8 + row] = (byte)((color & 2) != 0 ? 0xFF : 0x00);
        }
    }

    private static void WriteCheckerTile(byte[] chr, int tile, int colorA, int colorB)
    {
        for (var row = 0; row < 8; row++)
        {
            var plane0 = 0;
            var plane1 = 0;
            for (var col = 0; col < 8; col++)
            {
                var color = ((row + col) & 1) == 0 ? colorA : colorB;
                var bit = 7 - col;
                if ((color & 1) != 0) plane0 |= 1 << bit;
                if ((color & 2) != 0) plane1 |= 1 << bit;
            }

            chr[tile * 16 + row] = (byte)plane0;
            chr[tile * 16 + 8 + row] = (byte)plane1;
        }
    }

    private static void WriteFrameTile(byte[] chr, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            var bits = row is 0 or 7 ? 0xFF : 0x81;
            chr[tile * 16 + row] = (byte)((color & 1) != 0 ? bits : 0x00);
            chr[tile * 16 + 8 + row] = (byte)((color & 2) != 0 ? bits : 0x00);
        }
    }

    private static void SetVector(byte[] prg, int offset, ushort address)
    {
        prg[offset] = (byte)(address & 0xFF);
        prg[offset + 1] = (byte)(address >> 8);
    }
}

internal sealed class NesRuntimeCompiler
{
    private const byte FirstVariableAddress = 0x00;
    private const byte RuntimeReservedStateAddress = 0xE0;
    private const byte CameraXAddress = 0xE0;
    private const byte InputCurrentAddress = 0xF0;
    private const byte InputPreviousAddress = 0xF1;
    private const byte InputHoldTicksStartAddress = 0xF2;
    private const ushort OamShadowAddress = 0x0200;
    private const ushort OamDmaAddress = 0x4014;
    private const ushort ControllerPortAddress = 0x4016;

    private static readonly NesButton AButton = new("a", 0x01, InputHoldTicksStartAddress);
    private static readonly NesButton BButton = new("b", 0x02, InputHoldTicksStartAddress + 1);
    private static readonly NesButton SelectButton = new("select", 0x04, InputHoldTicksStartAddress + 2);
    private static readonly NesButton StartButton = new("start", 0x08, InputHoldTicksStartAddress + 3);
    private static readonly NesButton RightButton = new("right", 0x10, InputHoldTicksStartAddress + 4);
    private static readonly NesButton LeftButton = new("left", 0x20, InputHoldTicksStartAddress + 5);
    private static readonly NesButton UpButton = new("up", 0x40, InputHoldTicksStartAddress + 6);
    private static readonly NesButton DownButton = new("down", 0x80, InputHoldTicksStartAddress + 7);

    private static readonly NesButton[] Buttons =
    [
        AButton,
        BButton,
        SelectButton,
        StartButton,
        RightButton,
        LeftButton,
        UpButton,
        DownButton,
    ];

    private static readonly NesButton[] ControllerReadOrder =
    [
        AButton,
        BButton,
        SelectButton,
        StartButton,
        UpButton,
        DownButton,
        LeftButton,
        RightButton,
    ];

    private readonly PrgBuilder builder;
    private readonly NesVideoProgram program;
    private readonly Dictionary<string, byte> variables = [];
    private readonly HashSet<string> userFunctionCallStack = [];
    private byte nextVariableAddress = FirstVariableAddress;
    private int nextHardwareSprite;
    private bool cameraConfigured;

    public NesRuntimeCompiler(PrgBuilder builder, NesVideoProgram program)
    {
        this.builder = builder;
        this.program = program;
    }

    public void EmitInitialization()
    {
        EmitCameraStateInitialization();
        EmitInputStateInitialization();
        EmitOamShadowClear();
    }

    public void Emit(BlockSyntax block)
    {
        EmitBlock(block);
    }

    private void EmitCameraStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(CameraXAddress);
    }

    private void EmitInputStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(InputCurrentAddress);
        builder.StoreAZeroPage(InputPreviousAddress);
        foreach (var button in Buttons)
        {
            builder.StoreAZeroPage(button.HoldTicksAddress);
        }
    }

    private void EmitOamShadowClear()
    {
        var clearLabel = builder.CreateLabel("oam_clear");

        builder.LoadAImmediate(0xFF);
        builder.LoadXImmediate(0);
        builder.Label(clearLabel);
        builder.StoreAAbsoluteX(OamShadowAddress);
        builder.IncrementX();
        builder.BranchRelative(0xD0, clearLabel);   // BNE clearLabel
        EmitOamDma();
    }

    private bool EmitBlock(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            if (EmitStatement(statement))
            {
                return true;
            }
        }

        return false;
    }

    private bool EmitStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case DeclarationSyntax declaration:
                EmitDeclaration(declaration);
                return false;
            case ExpressionStatementSyntax expressionStatement:
                EmitExpressionStatement(expressionStatement);
                return false;
            case WhileSyntax whileSyntax:
                EmitWhile(whileSyntax);
                return false;
            case ReturnSyntax:
                return true;
            default:
                throw new InvalidOperationException($"Unsupported NES statement '{statement.GetType().Name}'.");
        }
    }

    private void EmitDeclaration(DeclarationSyntax declaration)
    {
        if (!IsByteBackedType(declaration.Type))
        {
            throw new InvalidOperationException($"NES target does not support local type '{declaration.Type}' yet.");
        }

        if (variables.ContainsKey(declaration.Name))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        if (nextVariableAddress >= RuntimeReservedStateAddress)
        {
            throw new InvalidOperationException("NES target local variables exceed the current prototype zero-page allocation.");
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

        builder.StoreAZeroPage(address);
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
                throw new InvalidOperationException($"Unsupported NES expression statement '{expressionStatement.Expression.GetType().Name}'.");
        }
    }

    private void EmitAssignment(AssignmentSyntax assignment)
    {
        if (assignment.Left is not IdentifierLValue identifier)
        {
            throw new InvalidOperationException("NES target only supports assignments to local variables.");
        }

        var address = VariableAddress(identifier.Identifier);
        EmitExpressionToA(assignment.Right);
        builder.StoreAZeroPage(address);
    }

    private void EmitCall(FunctionCall call)
    {
        switch (call.Name)
        {
            case "video_init":
            case "video_present":
                NesVideoProgram.RequireArity(call, 0);
                break;
            case "palette_set":
                NesVideoProgram.RequireArity(call, 2);
                break;
            case "tilemap_set":
                NesVideoProgram.RequireArity(call, 3);
                break;
            case "tilemap_fill":
                NesVideoProgram.RequireArity(call, 5);
                break;
            case "map_column":
            case "world_column":
            case "world_flags":
            case "world_map":
                break;
            case "sprite_asset":
                NesVideoProgram.RequireArity(call, 2);
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
            case "video_wait_vblank":
                NesVideoProgram.RequireArity(call, 0);
                EmitWaitVBlank();
                break;
            case "input_poll":
                EmitInputPoll(call);
                break;
            case "sprite_draw":
                EmitSpriteDraw(call);
                break;
            default:
                if (TryEmitUserFunction(call))
                {
                    break;
                }

                throw new InvalidOperationException($"Unsupported NES video API call '{call.Name}'.");
        }
    }

    private bool TryEmitUserFunction(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        NesVideoProgram.RequireParameterlessUserFunction(call, function);
        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
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

    private void EmitWhile(WhileSyntax whileSyntax)
    {
        if (!TryConst(whileSyntax.Condition, out var condition))
        {
            throw new InvalidOperationException("NES target only supports constant while conditions in the current runtime spike.");
        }

        if (condition == 0)
        {
            return;
        }

        var loopLabel = builder.CreateLabel("while");
        builder.Label(loopLabel);
        EmitBlock(whileSyntax.Body);
        builder.JumpAbsolute(loopLabel);
    }

    private void EmitWaitVBlank()
    {
        var label = builder.CreateLabel("vblank");
        builder.Label(label);
        builder.Emit(0x2C, 0x02, 0x20);             // BIT $2002
        builder.BranchRelative(0x10, label);        // BPL label
    }

    private void EmitInputPoll(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 0);

        builder.LoadAZeroPage(InputCurrentAddress);
        builder.StoreAZeroPage(InputPreviousAddress);

        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(ControllerPortAddress);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(ControllerPortAddress);
        builder.StoreAZeroPage(InputCurrentAddress);

        foreach (var button in ControllerReadOrder)
        {
            EmitReadControllerButton(button);
        }

        foreach (var button in Buttons)
        {
            EmitUpdateButtonHoldTicks(button);
        }
    }

    private void EmitReadControllerButton(NesButton button)
    {
        var skipLabel = builder.CreateLabel("input_button_skip");

        builder.LoadAAbsolute(ControllerPortAddress);
        builder.AndImmediate(0x01);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, skipLabel);    // BEQ skipLabel
        builder.LoadAZeroPage(InputCurrentAddress);
        builder.OrImmediate(button.SnapshotMask);
        builder.StoreAZeroPage(InputCurrentAddress);
        builder.Label(skipLabel);
    }

    private void EmitUpdateButtonHoldTicks(NesButton button)
    {
        var resetLabel = builder.CreateLabel("button_hold_reset");
        var endLabel = builder.CreateLabel("button_hold_end");

        builder.LoadAZeroPage(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, resetLabel);   // BEQ resetLabel

        builder.LoadAZeroPage(button.HoldTicksAddress);
        builder.CompareImmediate(0xFF);
        builder.BranchRelative(0xF0, endLabel);     // BEQ endLabel
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(button.HoldTicksAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(resetLabel);
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(button.HoldTicksAddress);
        builder.Label(endLabel);
    }

    private void EmitCameraInit(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 3);
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("camera_init requires world_map(...) data for the NES target.");
        var mapWidth = NesVideoProgram.ConstValue(call.Parameters.ElementAt(0), "camera_init argument 1");
        if (mapWidth is < 1 or > 32)
        {
            throw new InvalidOperationException("NES camera_init map width must fit the visible 32-column nametable until runtime streaming lands.");
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

        if (streamY < 0 || height < 1 || streamY + height > 30)
        {
            throw new InvalidOperationException("camera_init stream area must fit within the NES visible nametable height.");
        }

        cameraConfigured = true;
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(CameraXAddress);
    }

    private void EmitCameraSetPosition(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 2);
        EnsureCameraConfigured(call.Name);
        if (!TryConst(call.Parameters.ElementAt(1), out var y) || y != 0)
        {
            throw new InvalidOperationException("Target 'nes' supports only horizontal camera_set_position(x, 0) in the current camera spike.");
        }

        EmitExpressionToA(call.Parameters.ElementAt(0));
        builder.StoreAZeroPage(CameraXAddress);
    }

    private void EmitCameraApply(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 0);
        EnsureCameraConfigured(call.Name);

        builder.LoadAAbsolute(0x2002);              // reset PPU scroll latch
        builder.LoadAZeroPage(CameraXAddress);
        builder.StoreAAbsolute(0x2005);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0x2005);
    }

    private void EnsureCameraConfigured(string callName)
    {
        if (!cameraConfigured)
        {
            throw new InvalidOperationException($"{callName} requires camera_init(...) to be emitted first.");
        }
    }

    private void EmitSpriteDraw(FunctionCall call)
    {
        var args = call.Parameters.ToList();
        if (args.Count is not 4 and not 5 and not 6)
        {
            throw new InvalidOperationException($"sprite_draw expects 4, 5, or 6 arguments, got {args.Count}.");
        }

        var assetName = NesVideoProgram.IdentifierArg(args[0], "sprite_draw argument 1");
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        var frame = SpriteDrawFrameArgument(args, asset);
        var flipX = SpriteDrawFlipXArgument(args);
        var paletteSlot = SpriteDrawPaletteSlotArgument(args);
        var firstHardwareSprite = nextHardwareSprite;
        if (firstHardwareSprite + asset.Pieces.Count > NesTarget.Capabilities.SpriteCount)
        {
            throw new InvalidOperationException($"NES sprite_draw calls exceed the {NesTarget.Capabilities.SpriteCount} hardware sprite OAM limit.");
        }

        nextHardwareSprite += asset.Pieces.Count;
        for (var pieceIndex = 0; pieceIndex < asset.Pieces.Count; pieceIndex++)
        {
            var piece = asset.Pieces[pieceIndex];
            var oamAddress = (ushort)(OamShadowAddress + (firstHardwareSprite + pieceIndex) * 4);
            var xOffset = flipX ? asset.LogicalWidth - 8 - piece.XOffset : piece.XOffset;
            var attributes = paletteSlot | (flipX ? 0x40 : 0);

            EmitSpriteDrawY(args[2], piece.YOffset, oamAddress);

            builder.LoadAImmediate(asset.FirstTile + frame * asset.TilesPerFrame + piece.TileOffset);
            builder.StoreAAbsolute((ushort)(oamAddress + 1));

            builder.LoadAImmediate(attributes);
            builder.StoreAAbsolute((ushort)(oamAddress + 2));

            EmitSpriteDrawX(args[1], xOffset, (ushort)(oamAddress + 3));
        }

        EmitOamDma();
    }

    private int SpriteDrawFrameArgument(IReadOnlyList<ExpressionSyntax> args, NesCompiledSpriteAsset asset)
    {
        if (!TryConst(args[3], out var frame))
        {
            throw new InvalidOperationException("sprite_draw argument 4 must be a constant frame index for the current NES sprite spike.");
        }

        if (frame < 0 || frame >= asset.FrameCount)
        {
            throw new InvalidOperationException($"sprite_draw argument 4 must be between 0 and {asset.FrameCount - 1}.");
        }

        return frame;
    }

    private bool SpriteDrawFlipXArgument(IReadOnlyList<ExpressionSyntax> args)
    {
        if (args.Count < 5)
        {
            return false;
        }

        if (!TryConst(args[4], out var flipX) || flipX is not 0 and not 1)
        {
            throw new InvalidOperationException("sprite_draw argument 5 is portable flipX and must be 0, 1, true, or false for the current NES sprite spike.");
        }

        return flipX != 0;
    }

    private static int SpriteDrawPaletteSlotArgument(IReadOnlyList<ExpressionSyntax> args)
    {
        if (args.Count < 6)
        {
            return 0;
        }

        var slot = NesVideoProgram.ConstValue(args[5], "sprite_draw argument 6");
        if (slot < 0 || slot >= NesTarget.Capabilities.SpritePaletteSlots)
        {
            throw new InvalidOperationException(
                $"Target '{NesTarget.Capabilities.Name}' supports sprite palette slots 0..{NesTarget.Capabilities.SpritePaletteSlots - 1}, but slot {slot} was requested.");
        }

        return slot;
    }

    private void EmitSpriteDrawY(ExpressionSyntax yExpression, int offset, ushort oamAddress)
    {
        EmitExpressionToA(yExpression);
        EmitAddSignedImmediate(offset - 1);
        builder.StoreAAbsolute(oamAddress);
    }

    private void EmitSpriteDrawX(ExpressionSyntax xExpression, int offset, ushort oamAddress)
    {
        EmitExpressionToA(xExpression);
        EmitAddSignedImmediate(offset);
        builder.StoreAAbsolute(oamAddress);
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

    private void EmitOamDma()
    {
        builder.LoadAImmediate((OamShadowAddress >> 8) & 0xFF);
        builder.StoreAAbsolute(OamDmaAddress);
    }

    private void EmitExpressionToA(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ConstantSyntax:
                builder.LoadAImmediate(NesVideoProgram.ConstValue(expression, "constant"));
                break;
            case IdentifierSyntax { Identifier: "true" }:
                builder.LoadAImmediate(1);
                break;
            case IdentifierSyntax { Identifier: "false" }:
                builder.LoadAImmediate(0);
                break;
            case IdentifierSyntax identifier:
                builder.LoadAZeroPage(VariableAddress(identifier.Identifier));
                break;
            case FunctionCall call:
                EmitValueCallToA(call);
                break;
            default:
                throw new InvalidOperationException($"Unsupported NES expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitValueCallToA(FunctionCall call)
    {
        switch (call.Name)
        {
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
                throw new InvalidOperationException($"Unsupported NES value API call '{call.Name}'.");
        }
    }

    private void EmitButtonDown(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        EmitButtonMaskToBool(InputCurrentAddress, ButtonArg(call, "button_down argument 1"));
    }

    private void EmitButtonJustPressed(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_pressed argument 1");
        var falseLabel = builder.CreateLabel("button_just_pressed_false");
        var endLabel = builder.CreateLabel("button_just_pressed_end");

        builder.LoadAZeroPage(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);   // BEQ falseLabel

        builder.LoadAZeroPage(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, falseLabel);   // BNE falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonJustReleased(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_released argument 1");
        var falseLabel = builder.CreateLabel("button_just_released_false");
        var endLabel = builder.CreateLabel("button_just_released_end");

        builder.LoadAZeroPage(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, falseLabel);   // BNE falseLabel

        builder.LoadAZeroPage(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);   // BEQ falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonHoldTicks(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        builder.LoadAZeroPage(ButtonArg(call, "button_hold_ticks argument 1").HoldTicksAddress);
    }

    private void EmitButtonMaskToBool(byte address, NesButton button)
    {
        var pressedLabel = builder.CreateLabel("button_down");
        var endLabel = builder.CreateLabel("button_down_end");

        builder.LoadAZeroPage(address);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, pressedLabel); // BNE pressedLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private NesButton ButtonArg(FunctionCall call, string context)
    {
        var name = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), context);
        foreach (var button in Buttons)
        {
            if (button.Name == name)
            {
                return button;
            }
        }

        throw new InvalidOperationException($"Unsupported NES button '{name}'.");
    }

    private bool TryConst(ExpressionSyntax expression, out int value)
    {
        if (expression is ConstantSyntax)
        {
            value = NesVideoProgram.ConstValue(expression, "constant");
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

    private byte VariableAddress(string name)
    {
        if (!variables.TryGetValue(name, out var address))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return address;
    }

    private readonly record struct NesButton(string Name, byte SnapshotMask, byte HoldTicksAddress);
}

internal sealed class PrgBuilder
{
    private const int BaseAddress = 0xC000;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = [];
    private readonly List<(int Offset, string Label, int Addend)> absoluteFixups = [];
    private readonly List<(int Offset, string Label)> relativeFixups = [];
    private int nextLabelId;

    public void Label(string name) => labels[name] = bytes.Count;

    public string CreateLabel(string prefix) => $"{prefix}_{nextLabelId++}";

    public void Emit(params byte[] values) => bytes.AddRange(values);

    public void LoadAImmediate(int value) => Emit(0xA9, CheckedByte(value));

    public void LoadXImmediate(int value) => Emit(0xA2, CheckedByte(value));

    public void LoadAZeroPage(byte address) => Emit(0xA5, address);

    public void StoreAZeroPage(byte address) => Emit(0x85, address);

    public void LoadAAbsolute(ushort address) => Emit(0xAD, Low(address), High(address));

    public void StoreAAbsolute(ushort address) => Emit(0x8D, Low(address), High(address));

    public void StoreAAbsoluteX(ushort address) => Emit(0x9D, Low(address), High(address));

    public void AndImmediate(int value) => Emit(0x29, CheckedByte(value));

    public void OrImmediate(int value) => Emit(0x09, CheckedByte(value));

    public void CompareImmediate(int value) => Emit(0xC9, CheckedByte(value));

    public void ClearCarry() => Emit(0x18);

    public void SetCarry() => Emit(0x38);

    public void AddImmediate(int value) => Emit(0x69, CheckedByte(value));

    public void SubtractImmediate(int value) => Emit(0xE9, CheckedByte(value));

    public void IncrementX() => Emit(0xE8);

    public void LdaAbsoluteX(string label, int addend = 0)
    {
        Emit(0xBD, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, addend));
    }

    public void JumpAbsolute(string label)
    {
        Emit(0x4C, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, 0));
    }

    public void BranchRelative(byte opcode, string label)
    {
        Emit(opcode, 0x00);
        relativeFixups.Add((bytes.Count - 1, label));
    }

    public byte[] Build()
    {
        foreach (var fixup in absoluteFixups)
        {
            var address = AddressOf(fixup.Label, fixup.Addend);
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
                throw new InvalidOperationException($"Branch to '{fixup.Label}' is out of range.");
            }

            bytes[fixup.Offset] = unchecked((byte)(sbyte)delta);
        }

        return bytes.ToArray();
    }

    private static byte CheckedByte(int value)
    {
        if (value is < 0 or > 255)
        {
            throw new InvalidOperationException($"NES byte immediate must be between 0 and 255, got {value}.");
        }

        return (byte)value;
    }

    private static byte Low(ushort value) => (byte)(value & 0xFF);

    private static byte High(ushort value) => (byte)(value >> 8);

    private int AddressOf(string label, int addend = 0)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown NES PRG label '{label}'.");
        }

        return BaseAddress + offset + addend;
    }
}
