using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal static class NesRomBuilder
{
    private const int PrgRomSize = 32 * 1024;
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
        rom[4] = PrgRomSize / (16 * 1024);
        rom[5] = 1;
        rom[6] = 0x01;

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
        EmitNameTableUpload(builder, program.NameTable.Length);

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
        EmitWorldMapRows(builder, program.WorldMap);
        EmitWorldMapFlagRows(builder, program.WorldMap);

        var prg = new byte[PrgRomSize];
        var code = builder.Build();
        if (code.Length > PrgRomSize - 6)
        {
            throw new InvalidOperationException("NES PRG ROM overflow.");
        }

        code.CopyTo(prg, 0);
        SetVector(prg, PrgRomSize - 6, PrgBuilder.BaseAddress);
        SetVector(prg, PrgRomSize - 4, PrgBuilder.BaseAddress);
        SetVector(prg, PrgRomSize - 2, PrgBuilder.BaseAddress);
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

    private static void EmitNameTableUpload(PrgBuilder builder, int byteCount)
    {
        builder.Emit(0xAD, 0x02, 0x20);             // LDA $2002
        builder.Emit(0xA9, 0x20);                   // LDA #$20
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006

        if (byteCount % 256 != 0)
        {
            throw new InvalidOperationException("NES nametable upload size must be page-aligned.");
        }

        for (var page = 0; page < byteCount / 256; page++)
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

    private static void EmitWorldMapRows(PrgBuilder builder, WorldMap2D? worldMap)
    {
        if (worldMap is null)
        {
            return;
        }

        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.Label(WorldMapRowLabel(row));
            for (var column = 0; column < worldMap.Width; column++)
            {
                var tileId = worldMap.TileIdAt(column, row);
                if (tileId is < 0 or > 255)
                {
                    throw new InvalidOperationException("NES world map tile ids must fit one byte.");
                }

                builder.Emit((byte)tileId);
            }
        }
    }

    internal static string WorldMapRowLabel(int row) => $"world_map_row_{row}";

    private static void EmitWorldMapFlagRows(PrgBuilder builder, WorldMap2D? worldMap)
    {
        if (worldMap is null)
        {
            return;
        }

        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.Label(WorldMapFlagRowLabel(row));
            for (var column = 0; column < worldMap.Width; column++)
            {
                builder.Emit((byte)worldMap.FlagsAt(column, row));
            }
        }
    }

    internal static string WorldMapFlagRowLabel(int row) => $"world_map_flags_row_{row}";

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

        foreach (var background in program.GeneratedBackgroundTiles)
        {
            var offset = background.FirstTile * 16;
            if (offset + background.Data.Length > chr.Length)
            {
                throw new InvalidOperationException("NES world.Load background tiles exceed CHR ROM size.");
            }

            background.Data.CopyTo(chr, offset);
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
    private sealed record StructArrayLayout(int Stride, IReadOnlyDictionary<string, int> FieldOffsets);

    private const byte FirstVariableAddress = 0x00;
    private const byte RuntimeReservedStateAddress = 0xE0;
    private const byte CameraXAddress = 0xE0;
    private const byte CameraTileColumnAddress = 0xE1;
    private const byte CameraTargetColumnAddress = 0xE2;
    private const byte CameraSourceColumnAddress = 0xE3;
    private const byte SpriteFrameScratchAddress = 0xE4;
    private const byte CollisionColumnScratchAddress = 0xE5;
    private const byte CollisionRowScratchAddress = 0xE6;
    private const byte CameraNewXAddress = 0xE7;
    private const byte RuntimeIndexScratchAddress = 0xE8;
    private const byte ExpressionScratchAddress = 0xE9;
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
    private readonly Dictionary<string, StructArrayLayout> structArrays = [];
    private readonly HashSet<string> declaredVariables = [];
    private readonly HashSet<string> immutableVariables = [];
    private readonly HashSet<string> userFunctionCallStack = [];
    private readonly Stack<LoopTarget> loopTargets = [];
    private byte nextVariableAddress = FirstVariableAddress;
    private int nextHardwareSprite;
    private NesCameraConfig? cameraConfig;

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
        builder.StoreAZeroPage(CameraTileColumnAddress);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        builder.StoreAZeroPage(CameraNewXAddress);
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
            case DoWhileSyntax doWhileSyntax:
                EmitDoWhile(doWhileSyntax);
                return false;
            case LoopSyntax loopSyntax:
                EmitWhile(LoopLowerer.Lower(loopSyntax));
                return false;
            case RangeForSyntax rangeForSyntax:
                EmitFor(RangeForLowerer.Lower(rangeForSyntax));
                return false;
            case ForSyntax forSyntax:
                EmitFor(forSyntax);
                return false;
            case IfElseSyntax ifElseSyntax:
                return EmitIf(ifElseSyntax);
            case BreakSyntax:
                EmitBreak();
                return true;
            case ContinueSyntax:
                EmitContinue();
                return true;
            case ReturnSyntax:
                return true;
            default:
                throw new InvalidOperationException($"Unsupported NES statement '{statement.GetType().Name}'.");
        }
    }

    private void EmitDeclaration(DeclarationSyntax declaration)
    {
        if (declaration.ArrayLength.HasValue)
        {
            EmitArrayDeclaration(declaration);
            return;
        }

        if (IsByteBackedLocalType(declaration.Type))
        {
            EmitByteBackedDeclaration(declaration);
            return;
        }

        if (program.Structs.TryGetValue(declaration.Type, out var structSyntax))
        {
            EmitStructDeclaration(declaration, structSyntax);
            return;
        }

        throw new InvalidOperationException($"NES target does not support local type '{declaration.Type}' yet.");
    }

    private void EmitArrayDeclaration(DeclarationSyntax declaration)
    {
        if (program.Structs.TryGetValue(declaration.Type, out var structSyntax))
        {
            EmitStructArrayDeclaration(declaration, structSyntax);
            return;
        }

        if (!IsByteBackedLocalType(declaration.Type))
        {
            throw new InvalidOperationException($"NES target only supports byte-backed fixed-size arrays; '{declaration.Type}' is not supported yet.");
        }

        if (!declaredVariables.Add(declaration.Name))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(NesVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var elementAddresses = new List<byte>();
        for (var index = 0; index < length; index++)
        {
            var address = DeclareVariable(IndexedElementName(declaration.Name, index));
            elementAddresses.Add(address);
            builder.LoadAImmediate(0);
            builder.StoreAZeroPage(address);
        }

        if (declaration.Initialization.HasValue)
        {
            EmitArrayInitializer(declaration, declaration.Initialization.Value, length, elementAddresses);
        }
    }

    private void EmitStructArrayDeclaration(DeclarationSyntax declaration, StructSyntax structSyntax)
    {
        if (!declaredVariables.Add(declaration.Name))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(NesVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var fieldOffsets = StructFieldOffsets(structSyntax, "NES");
        var stride = fieldOffsets.Count;
        if (length * stride > 255)
        {
            throw new InvalidOperationException($"NES target struct array '{declaration.Name}' uses {length * stride} byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.");
        }

        structArrays.Add(declaration.Name, new StructArrayLayout(stride, fieldOffsets));

        var fieldNames = structSyntax.Fields.Select(field => field.Name).ToList();
        for (var index = 0; index < length; index++)
        {
            foreach (var field in structSyntax.Fields)
            {
                var address = DeclareVariable(IndexedMemberName(declaration.Name, index, field.Name));
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(address);
            }
        }

        if (declaration.Initialization.HasValue)
        {
            EmitStructArrayInitializer(declaration, declaration.Initialization.Value, length, fieldNames);
        }
    }

    private void EmitStructArrayInitializer(
        DeclarationSyntax declaration,
        ExpressionSyntax initialization,
        int length,
        IReadOnlyList<string> fieldNames)
    {
        if (initialization is not ArrayInitializerSyntax arrayInitializer)
        {
            throw new InvalidOperationException($"NES target requires an array initializer for local struct array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"NES target struct array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
        }

        var knownFields = fieldNames.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < arrayInitializer.Elements.Count; index++)
        {
            if (arrayInitializer.Elements[index] is not StructInitializerSyntax structInitializer)
            {
                throw new InvalidOperationException($"NES target requires struct initializers for elements of struct array '{declaration.Name}'.");
            }

            var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
            foreach (var field in structInitializer.Fields)
            {
                if (!initializedFields.TryAdd(field.Name, field.Expression))
                {
                    throw new InvalidOperationException($"NES target struct array initializer for '{declaration.Name}' element {index} supplies field '{field.Name}' more than once.");
                }

                if (!knownFields.Contains(field.Name))
                {
                    throw new InvalidOperationException($"NES target struct array initializer for '{declaration.Name}' has no field named '{field.Name}'.");
                }
            }

            foreach (var fieldName in fieldNames)
            {
                if (!initializedFields.TryGetValue(fieldName, out var expression))
                {
                    continue;
                }

                EmitExpressionToA(expression);
                builder.StoreAZeroPage(VariableAddress(IndexedMemberName(declaration.Name, index, fieldName)));
            }
        }
    }

    private void EmitArrayInitializer(DeclarationSyntax declaration, ExpressionSyntax initialization, int length, IReadOnlyList<byte> elementAddresses)
    {
        if (initialization is not ArrayInitializerSyntax arrayInitializer)
        {
            throw new InvalidOperationException($"NES target requires an array initializer for local array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"NES target array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
        }

        for (var index = 0; index < arrayInitializer.Elements.Count; index++)
        {
            EmitExpressionToA(arrayInitializer.Elements[index]);
            builder.StoreAZeroPage(elementAddresses[index]);
        }
    }

    private void EmitByteBackedDeclaration(DeclarationSyntax declaration)
    {
        var address = DeclareVariable(declaration.Name);
        TrackImmutable(declaration);

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

    private void EmitStructDeclaration(DeclarationSyntax declaration, StructSyntax structSyntax)
    {
        if (!declaredVariables.Add(declaration.Name))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var fieldAddresses = new Dictionary<string, byte>(StringComparer.Ordinal);
        var fieldNames = new List<string>();
        foreach (var field in structSyntax.Fields)
        {
            if (!IsByteBackedLocalType(field.Type))
            {
                throw new InvalidOperationException($"NES target does not support struct field type '{field.Type}' yet.");
            }

            var address = DeclareVariable($"{declaration.Name}.{field.Name}");
            fieldAddresses.Add(field.Name, address);
            fieldNames.Add(field.Name);
            builder.LoadAImmediate(0);
            builder.StoreAZeroPage(address);
        }

        if (declaration.Initialization.HasValue)
        {
            EmitStructInitializer(declaration, declaration.Initialization.Value, fieldNames, fieldAddresses);
        }
    }

    private void EmitStructInitializer(
        DeclarationSyntax declaration,
        ExpressionSyntax initialization,
        IReadOnlyList<string> fieldNames,
        IReadOnlyDictionary<string, byte> fieldAddresses)
    {
        if (initialization is not StructInitializerSyntax structInitializer)
        {
            throw new InvalidOperationException($"NES target requires a struct initializer for local struct '{declaration.Name}'.");
        }

        var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var field in structInitializer.Fields)
        {
            if (!initializedFields.TryAdd(field.Name, field.Expression))
            {
                throw new InvalidOperationException($"NES target struct initializer for '{declaration.Name}' supplies field '{field.Name}' more than once.");
            }

            if (!fieldAddresses.ContainsKey(field.Name))
            {
                throw new InvalidOperationException($"NES target struct initializer for '{declaration.Name}' has no field named '{field.Name}'.");
            }
        }

        foreach (var fieldName in fieldNames)
        {
            if (!initializedFields.TryGetValue(fieldName, out var expression))
            {
                continue;
            }

            EmitExpressionToA(expression);
            builder.StoreAZeroPage(fieldAddresses[fieldName]);
        }
    }

    private byte DeclareVariable(string name)
    {
        if (!declaredVariables.Add(name))
        {
            throw new InvalidOperationException($"Variable '{name}' is already declared.");
        }

        if (variables.ContainsKey(name))
        {
            throw new InvalidOperationException($"Variable '{name}' is already declared.");
        }

        if (nextVariableAddress >= RuntimeReservedStateAddress)
        {
            throw new InvalidOperationException("NES target local variables exceed the current prototype zero-page allocation.");
        }

        var address = nextVariableAddress++;
        variables.Add(name, address);
        return address;
    }

    private void TrackImmutable(DeclarationSyntax declaration)
    {
        if (declaration.IsImmutable)
        {
            immutableVariables.Add(declaration.Name);
        }
    }

    private static bool IsByteBackedType(string type)
    {
        return type is "i8" or "u8" or "i16" or "u16" or "bool";
    }

    private bool IsByteBackedLocalType(string type)
    {
        return IsByteBackedType(type) || program.Enums.ContainsKey(type);
    }

    private void RequireSupportedCastTarget(CastSyntax cast)
    {
        if (!IsByteBackedLocalType(cast.Type))
        {
            throw new InvalidOperationException($"NES target only supports explicit casts to byte-backed local types; '{cast.Type}' is not supported yet.");
        }
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
        RequireMutableAssignmentTarget(assignment.Left);

        if (assignment.Left is IndexLValue indexLValue && !TryConst(indexLValue.Index, out _))
        {
            EmitRuntimeIndexedAssignment(indexLValue, assignment);
            return;
        }

        if (assignment.Left is MemberAccessLValue memberLValue && TryRuntimeIndexedMemberAccess(memberLValue.MemberAccess, out var indexedBase, out var fieldName))
        {
            EmitRuntimeIndexedMemberAssignment(indexedBase, fieldName, assignment);
            return;
        }

        var address = LValueAddress(assignment.Left);
        EmitAssignmentRightToA(assignment);
        builder.StoreAZeroPage(address);
    }

    private void RequireMutableAssignmentTarget(LValue lValue)
    {
        if (AssignedRoot(lValue) is { } name && immutableVariables.Contains(name))
        {
            throw new InvalidOperationException($"Cannot assign to immutable local '{name}'.");
        }
    }

    private static string? AssignedRoot(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => identifier.Identifier,
            IndexLValue index => index.BaseIdentifier,
            MemberAccessLValue memberAccess => MemberAccessRoot(memberAccess.MemberAccess),
            _ => null,
        };
    }

    private static string? MemberAccessRoot(MemberAccessSyntax memberAccess)
    {
        return memberAccess.Target switch
        {
            IdentifierSyntax identifier => identifier.Identifier,
            IndexExpressionSyntax indexExpression => indexExpression.BaseIdentifier,
            MemberAccessSyntax nested => MemberAccessRoot(nested),
            _ => null,
        };
    }

    private void EmitRuntimeIndexedAssignment(IndexLValue indexLValue, AssignmentSyntax assignment)
    {
        var baseAddress = ArrayBaseAddress(indexLValue.BaseIdentifier);
        EmitRuntimeIndexToX(indexLValue.Index);

        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesX(assignment.Right, "runtime indexed assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreAZeroPageX(baseAddress);
                return;
            case "+=":
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.LoadAZeroPageX(baseAddress);
                    builder.ClearCarry();
                    builder.AddImmediate(addRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var addAddress))
                {
                    builder.LoadAZeroPage(addAddress);
                    builder.ClearCarry();
                    builder.AddZeroPageX(baseAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed += assignments.");
            case "-=":
                builder.LoadAZeroPageX(baseAddress);
                builder.SetCarry();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractImmediate(subtractRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var subtractAddress))
                {
                    builder.SubtractZeroPage(subtractAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed -= assignments.");
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "&", "runtime indexed &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "|", "runtime indexed |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "^", "runtime indexed ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitRuntimeIndexedBitwiseCompoundAssignment(byte baseAddress, ExpressionSyntax right, string op, string context)
    {
        builder.LoadAZeroPageX(baseAddress);
        if (TryConst(right, out var constant))
        {
            EmitBitwiseImmediate(op, constant);
            builder.StoreAZeroPageX(baseAddress);
            return;
        }

        if (TryDirectAddress(right, out var address))
        {
            EmitBitwiseZeroPage(op, address);
            builder.StoreAZeroPageX(baseAddress);
            return;
        }

        throw new InvalidOperationException($"NES target only supports constants or direct byte-backed values on the right side of {context}.");
    }

    private void EmitRuntimeIndexedMemberAssignment(IndexExpressionSyntax indexExpression, string fieldName, AssignmentSyntax assignment)
    {
        var baseAddress = RuntimeIndexedMemberBaseAddress(indexExpression, fieldName);
        EmitRuntimeMemberIndexToX(indexExpression);

        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesX(assignment.Right, "runtime indexed struct field assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreAZeroPageX(baseAddress);
                return;
            case "+=":
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.LoadAZeroPageX(baseAddress);
                    builder.ClearCarry();
                    builder.AddImmediate(addRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var addAddress))
                {
                    builder.LoadAZeroPage(addAddress);
                    builder.ClearCarry();
                    builder.AddZeroPageX(baseAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed struct field += assignments.");
            case "-=":
                builder.LoadAZeroPageX(baseAddress);
                builder.SetCarry();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractImmediate(subtractRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var subtractAddress))
                {
                    builder.SubtractZeroPage(subtractAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed struct field -= assignments.");
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "&", "runtime indexed struct field &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "|", "runtime indexed struct field |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "^", "runtime indexed struct field ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitAssignmentRightToA(AssignmentSyntax assignment)
    {
        switch (assignment.OperatorSymbol)
        {
            case "=":
                EmitExpressionToA(assignment.Right);
                return;
            case "+=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("+")));
                return;
            case "-=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("-")));
                return;
            case "&=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("&")));
                return;
            case "|=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("|")));
                return;
            case "^=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("^")));
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private byte LValueAddress(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableAddress(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableAddress(NesVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableAddress(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
            _ => throw new InvalidOperationException("NES target only supports assignments to local variables, struct fields, or constant array indices."),
        };
    }

    private static ExpressionSyntax ExpressionFromLValue(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => new IdentifierSyntax(identifier.Identifier),
            MemberAccessLValue memberAccess => memberAccess.MemberAccess,
            IndexLValue index => new IndexExpressionSyntax(index.BaseIdentifier, index.Index),
            _ => throw new InvalidOperationException("Compound assignment target must be readable."),
        };
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
            case "palette_background":
            case "palette_sprite":
                NesVideoProgram.RequireArity(call, 5);
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
            case "map_stream_column":
                EmitSdkOperation(Sdk2DOperationCollector.ReadStreamMapColumn(call));
                break;
            case "world_load":
                NesVideoProgram.RequireArity(call, 1);
                break;
            case "sprite_asset":
                var count = call.Parameters.Count();
                if (count is not 2 and not 4)
                {
                    throw new InvalidOperationException($"sprite_asset expects 2 or 4 arguments, got {count}.");
                }

                break;
            case "animation_clip":
                if (call.Parameters.Count() < 3)
                {
                    throw new InvalidOperationException($"animation_clip expects at least 3 arguments, got {call.Parameters.Count()}.");
                }

                break;
            case "music_asset":
                NesVideoProgram.RequireArity(call, 2);
                break;
            case "audio_init":
            case "audio_update":
            case "music_stop":
                NesVideoProgram.RequireArity(call, 0);
                break;
            case "music_play":
                NesVideoProgram.RequireArity(call, 1);
                break;
            case "hud_set_tile":
                NesVideoProgram.ValidateHudSetTile(call);
                break;
            case "camera_init":
                EmitCameraInit(call);
                break;
            case "camera_set_position":
                EmitSdkOperation(Sdk2DOperationCollector.ReadSetCameraPosition(call));
                break;
            case "camera_apply":
                NesVideoProgram.RequireArity(call, 0);
                EmitSdkOperation(new Sdk2DOperation.ApplyCamera(ScrollAxes.Horizontal));
                break;
            case "video_wait_vblank":
                NesVideoProgram.RequireArity(call, 0);
                EmitSdkOperation(new Sdk2DOperation.WaitFrame());
                break;
            case "input_poll":
                NesVideoProgram.RequireArity(call, 0);
                EmitSdkOperation(new Sdk2DOperation.PollInput());
                break;
            case "sprite_draw":
                EmitSdkOperation(Sdk2DOperationCollector.ReadDrawLogicalSprite(call));
                break;
            default:
                if (TryEmitTargetIntrinsic(call))
                {
                    break;
                }

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

        if (function.IsExtern)
        {
            return false;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitBlock(ParameterSubstitution.Substitute(function, call, "NES"));
        }
        finally
        {
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private bool TryEmitUserValueFunction(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            return false;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitExpressionToA(ParameterSubstitution.SubstituteReturnExpression(function, call, "NES"));
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
        var endLabel = builder.CreateLabel("while_end");
        builder.Label(loopLabel);
        loopTargets.Push(new LoopTarget(endLabel, loopLabel));
        try
        {
            EmitBlock(whileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitDoWhile(DoWhileSyntax doWhileSyntax)
    {
        var loopLabel = builder.CreateLabel("do");
        var continueLabel = builder.CreateLabel("do_continue");
        var endLabel = builder.CreateLabel("do_end");

        builder.Label(loopLabel);
        loopTargets.Push(new LoopTarget(endLabel, continueLabel));
        try
        {
            EmitBlock(doWhileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.Label(continueLabel);
        EmitConditionFalseJump(doWhileSyntax.Condition, endLabel);
        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitFor(ForSyntax forSyntax)
    {
        if (forSyntax.Initializer.HasValue)
        {
            EmitStatement(forSyntax.Initializer.Value);
        }

        var loopLabel = builder.CreateLabel("for");
        var continueLabel = builder.CreateLabel("for_continue");
        var endLabel = builder.CreateLabel("for_end");

        builder.Label(loopLabel);
        if (forSyntax.Condition.HasValue)
        {
            EmitConditionFalseJumpToFarLabel(forSyntax.Condition.Value, endLabel, "for_condition_false");
        }

        loopTargets.Push(new LoopTarget(endLabel, continueLabel));
        try
        {
            EmitBlock(forSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.Label(continueLabel);
        if (forSyntax.Increment.HasValue)
        {
            if (forSyntax.Increment.Value is not AssignmentSyntax increment)
            {
                throw new InvalidOperationException($"Unsupported NES for increment '{forSyntax.Increment.Value.GetType().Name}'.");
            }

            EmitAssignment(increment);
        }

        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitConditionFalseJumpToFarLabel(ExpressionSyntax condition, string targetLabel, string prefix)
    {
        var trampolineLabel = builder.CreateLabel(prefix);
        var continueLabel = builder.CreateLabel($"{prefix}_continue");
        EmitConditionFalseJump(condition, trampolineLabel);
        builder.JumpAbsolute(continueLabel);
        builder.Label(trampolineLabel);
        builder.JumpAbsolute(targetLabel);
        builder.Label(continueLabel);
    }

    private bool EmitIf(IfElseSyntax ifElseSyntax)
    {
        var trueLabel = builder.CreateLabel("if_true");
        var falseTrampolineLabel = builder.CreateLabel("if_false_trampoline");
        var falseLabel = builder.CreateLabel("if_false");
        var endLabel = builder.CreateLabel("if_end");

        EmitConditionFalseJump(ifElseSyntax.Condition, falseTrampolineLabel);
        builder.JumpAbsolute(trueLabel);
        builder.Label(falseTrampolineLabel);
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
        var thenTerminates = EmitBlock(ifElseSyntax.ThenBlock);
        if (ifElseSyntax.ElseBlock.HasValue)
        {
            if (!thenTerminates)
            {
                builder.JumpAbsolute(endLabel);
            }

            builder.Label(falseLabel);
            var elseTerminates = EmitBlock(ifElseSyntax.ElseBlock.Value);
            builder.Label(endLabel);
            return thenTerminates && elseTerminates;
        }

        builder.Label(falseLabel);
        return false;
    }

    private void EmitBreak()
    {
        if (loopTargets.Count == 0)
        {
            throw new InvalidOperationException("break can only be used inside a loop.");
        }

        builder.JumpAbsolute(loopTargets.Peek().BreakLabel);
    }

    private void EmitContinue()
    {
        if (loopTargets.Count == 0)
        {
            throw new InvalidOperationException("continue can only be used inside a loop.");
        }

        builder.JumpAbsolute(loopTargets.Peek().ContinueLabel);
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
                case "&&":
                    EmitConditionFalseJump(binary.Left, falseLabel);
                    EmitConditionFalseJump(binary.Right, falseLabel);
                    return;
                case "||":
                    var trueLabel = builder.CreateLabel("or_true");
                    EmitConditionTrueJump(binary.Left, trueLabel);
                    EmitConditionFalseJump(binary.Right, falseLabel);
                    builder.Label(trueLabel);
                    return;
                case "==":
                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
                    return;
                case "!=":
                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                    return;
                case "<":
                case "<=":
                case ">":
                case ">=":
                    EmitRelationalFalseJump(binary, falseLabel);
                    return;
            }
        }

        if (condition is UnaryExpressionSyntax { OperatorSymbol: "!" } unary)
        {
            EmitConditionTrueJump(unary.Operand, falseLabel);
            return;
        }

        EmitExpressionToA(condition);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);   // BEQ falseLabel
    }

    private void EmitConditionTrueJump(ExpressionSyntax condition, string trueLabel)
    {
        if (TryConst(condition, out var constant))
        {
            if (constant != 0)
            {
                builder.JumpAbsolute(trueLabel);
            }

            return;
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            switch (binary.Operator.Symbol)
            {
                case "&&":
                    var falseLabel = builder.CreateLabel("and_false");
                    EmitConditionFalseJump(binary.Left, falseLabel);
                    EmitConditionTrueJump(binary.Right, trueLabel);
                    builder.Label(falseLabel);
                    return;
                case "||":
                    EmitConditionTrueJump(binary.Left, trueLabel);
                    EmitConditionTrueJump(binary.Right, trueLabel);
                    return;
                case "==":
                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xF0, trueLabel); // BEQ trueLabel
                    return;
                case "!=":
                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
                    return;
            }
        }

        if (condition is UnaryExpressionSyntax { OperatorSymbol: "!" } unary)
        {
            EmitConditionFalseJump(unary.Operand, trueLabel);
            return;
        }

        EmitExpressionToA(condition);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, trueLabel);     // BNE trueLabel
    }

    private void EmitCompare(ExpressionSyntax left, ExpressionSyntax right)
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

        EmitVariableOperandsToAAndScratch(left, right);
        builder.CompareZeroPage(ExpressionScratchAddress);
    }

    private void EmitVariableOperandsToAAndScratch(ExpressionSyntax left, ExpressionSyntax right)
    {
        EmitExpressionToA(left);
        builder.PushA();
        EmitExpressionToA(right);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.PullA();
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

        EmitCompare(binary.Left, binary.Right);
        EmitRelationalFalseJump(binary.Operator.Symbol, falseLabel);
    }

    private void EmitRelationalFalseJump(string op, string falseLabel)
    {
        switch (op)
        {
            case "<":
                builder.BranchRelative(0xB0, falseLabel); // BCS falseLabel
                return;
            case "<=":
                var trueLabel = builder.CreateLabel("rel_true");
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xF0, trueLabel);  // BEQ trueLabel
                builder.JumpAbsolute(falseLabel);
                builder.Label(trueLabel);
                return;
            case ">":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                return;
            case ">=":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES relational operator '{op}'.");
        }
    }

    private static string FlipRelationalOperator(string op)
    {
        return op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => throw new InvalidOperationException($"Unsupported NES relational operator '{op}'."),
        };
    }

    private void EmitSdkOperation(Sdk2DOperation operation)
    {
        NesSdkOperationLowerer.Emit(this, operation);
    }

    internal void EmitWaitFrame()
    {
        var label = builder.CreateLabel("vblank");
        builder.Label(label);
        builder.Emit(0x2C, 0x02, 0x20);             // BIT $2002
        builder.BranchRelative(0x10, label);        // BPL label
    }

    private bool TryEmitTargetIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicName(function, "nes", "NES");
        switch (intrinsic)
        {
            case "wait_frame":
                NesVideoProgram.RequireArity(call, 0);
                EmitWaitFrame();
                return true;
            default:
                throw new InvalidOperationException($"Unsupported NES intrinsic '{intrinsic}' on extern function '{function.Name}'.");
        }
    }

    private static string TargetIntrinsicName(FunctionSyntax function, string targetId, string targetName)
    {
        var intrinsic = AttributeString(function, "intrinsic")
                        ?? throw new InvalidOperationException($"Extern function '{function.Name}' must declare an intrinsic attribute.");
        var target = AttributeString(function, "target")
                     ?? throw new InvalidOperationException($"Extern intrinsic '{function.Name}' must declare a target attribute.");
        if (!string.Equals(target, targetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Extern intrinsic '{function.Name}' targets '{target}', not {targetName}.");
        }

        return intrinsic;
    }

    private static string? AttributeString(FunctionSyntax function, string name)
    {
        var attribute = function.Attributes.FirstOrDefault(attr => attr.Name == name);
        if (attribute is null)
        {
            return null;
        }

        if (attribute.Arguments is not [ConstantSyntax constant])
        {
            throw new InvalidOperationException($"Attribute '{name}' on extern function '{function.Name}' expects one string argument.");
        }

        var text = constant.Value.ToString() ?? "";
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return text[1..^1];
        }

        throw new InvalidOperationException($"Attribute '{name}' on extern function '{function.Name}' expects one string argument.");
    }

    internal void EmitPollInput()
    {
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
        if (mapWidth is < 1 or > 255)
        {
            throw new InvalidOperationException("NES camera_init map width must fit one byte in the current horizontal streaming runtime.");
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

        cameraConfig = new NesCameraConfig(mapWidth, streamY, height);
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(CameraXAddress);
        builder.StoreAZeroPage(CameraTileColumnAddress);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        builder.StoreAZeroPage(CameraNewXAddress);
    }

    internal void EmitSetCameraPosition(Sdk2DOperation.SetCameraPosition operation)
    {
        var config = EnsureCameraConfigured("camera_set_position");

        // The shared validator rejects any non-zero vertical axis for NES
        // (horizontal-only fine scroll), so only the X position is applied here.
        EmitSdkByteExpressionToA(operation.X);
        builder.StoreAZeroPage(CameraNewXAddress);
        if (config.MapWidth > 32)
        {
            EmitStreamColumnForCameraPosition(config);
            return;
        }

        builder.LoadAZeroPage(CameraNewXAddress);
        builder.StoreAZeroPage(CameraXAddress);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(CameraTileColumnAddress);
    }

    internal void EmitApplyCamera(Sdk2DOperation.ApplyCamera operation)
    {
        EnsureCameraConfigured("camera_apply");
        EmitRestoreCameraScroll();
    }

    private void EmitSdkByteExpressionToA(SdkByteExpression expression)
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

    internal void EmitStreamMapColumn(Sdk2DOperation.StreamMapColumn operation)
    {
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("map_stream_column requires world_map(...) data for the NES target.");
        var y = CheckedRange(operation.Y, 0, 29, "map_stream_column argument 3");
        var height = CheckedRange(operation.Height, 1, worldMap.Height, "map_stream_column argument 4");
        if (y + height > 30)
        {
            throw new InvalidOperationException("map_stream_column stream area must fit within the NES visible nametable height.");
        }

        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: y, Height: height));

        EmitSdkByteExpressionToA(operation.TargetColumn);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        EmitSdkByteExpressionToA(operation.SourceColumn);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        EmitWaitFrame();
        EmitStreamColumnFromAddresses(new NesCameraConfig(worldMap.Width, y, height));
    }

    private void EmitStreamColumnForCameraPosition(NesCameraConfig config)
    {
        var moveRightLabel = builder.CreateLabel("nes_camera_move_right");
        var moveLeftLabel = builder.CreateLabel("nes_camera_move_left");
        var fallbackLabel = builder.CreateLabel("nes_camera_move_fallback");
        var changedLabel = builder.CreateLabel("nes_camera_changed");
        var rightCrossedTileLabel = builder.CreateLabel("nes_camera_right_crossed_tile");
        var leftCrossedTileLabel = builder.CreateLabel("nes_camera_left_crossed_tile");
        var storeOnlyLabel = builder.CreateLabel("nes_camera_store_only");
        var storeAndStreamRightLabel = builder.CreateLabel("nes_camera_store_stream_right");
        var storeAndStreamLeftLabel = builder.CreateLabel("nes_camera_store_stream_left");
        var endLabel = builder.CreateLabel("nes_camera_stream_end");

        builder.LoadAZeroPage(CameraNewXAddress);
        builder.CompareZeroPage(CameraXAddress);
        builder.BranchRelative(0xD0, changedLabel); // BNE changedLabel
        builder.JumpAbsolute(endLabel);

        builder.Label(changedLabel);

        builder.LoadAZeroPage(CameraXAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(CameraNewXAddress);
        builder.BranchRelative(0xF0, moveRightLabel); // BEQ moveRightLabel

        builder.LoadAZeroPage(CameraNewXAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(CameraXAddress);
        builder.BranchRelative(0xF0, moveLeftLabel); // BEQ moveLeftLabel

        builder.JumpAbsolute(fallbackLabel);

        builder.Label(moveRightLabel);
        builder.LoadAZeroPage(CameraXAddress);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0x07);
        builder.BranchRelative(0xF0, rightCrossedTileLabel); // BEQ rightCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(rightCrossedTileLabel);
        EmitIncrementCameraTile(config.MapWidth);
        builder.JumpAbsolute(storeAndStreamRightLabel);

        builder.Label(moveLeftLabel);
        builder.LoadAZeroPage(CameraXAddress);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, leftCrossedTileLabel); // BEQ leftCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(leftCrossedTileLabel);
        EmitDecrementCameraTile(config.MapWidth);
        builder.JumpAbsolute(storeAndStreamLeftLabel);

        builder.Label(fallbackLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(CameraTileColumnAddress);
        builder.JumpAbsolute(storeOnlyLabel);

        builder.Label(storeAndStreamRightLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.StoreAZeroPage(CameraXAddress);
        EmitPrepareRightStreamColumn(config.MapWidth);
        EmitWaitFrame();
        EmitStreamColumnFromAddresses(config);
        EmitRestoreCameraScroll();
        builder.JumpAbsolute(endLabel);

        builder.Label(storeAndStreamLeftLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.StoreAZeroPage(CameraXAddress);
        EmitPrepareLeftStreamColumn();
        EmitWaitFrame();
        EmitStreamColumnFromAddresses(config);
        EmitRestoreCameraScroll();
        builder.JumpAbsolute(endLabel);

        builder.Label(storeOnlyLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.StoreAZeroPage(CameraXAddress);
        builder.Label(endLabel);
    }

    private void EmitIncrementCameraTile(int mapWidth)
    {
        var noWrapLabel = builder.CreateLabel("nes_camera_tile_inc_no_wrap");
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, noWrapLabel);  // BCC noWrapLabel
        builder.LoadAImmediate(0);
        builder.Label(noWrapLabel);
        builder.StoreAZeroPage(CameraTileColumnAddress);
    }

    private void EmitDecrementCameraTile(int mapWidth)
    {
        var decrementLabel = builder.CreateLabel("nes_camera_tile_dec");
        var storeLabel = builder.CreateLabel("nes_camera_tile_dec_store");

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, decrementLabel); // BNE decrementLabel
        builder.LoadAImmediate(mapWidth - 1);
        builder.JumpAbsolute(storeLabel);

        builder.Label(decrementLabel);
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);

        builder.Label(storeLabel);
        builder.StoreAZeroPage(CameraTileColumnAddress);
    }

    private void EmitPrepareRightStreamColumn(int mapWidth)
    {
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(32);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(CameraTargetColumnAddress);

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(32);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        var sourceNoWrapLabel = builder.CreateLabel("nes_camera_source_no_wrap");
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, sourceNoWrapLabel); // BCC sourceNoWrapLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapWidth);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        builder.Label(sourceNoWrapLabel);
    }

    private void EmitPrepareLeftStreamColumn()
    {
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(CameraTargetColumnAddress);

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
    }

    private void EmitRestoreCameraScroll()
    {
        var rightNameTableLabel = builder.CreateLabel("nes_camera_apply_right_nt");
        var storeControlLabel = builder.CreateLabel("nes_camera_apply_store_ctrl");

        builder.LoadAAbsolute(0x2002);              // reset PPU scroll latch
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.AndImmediate(0x20);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, rightNameTableLabel); // BNE rightNameTableLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(storeControlLabel);

        builder.Label(rightNameTableLabel);
        builder.LoadAImmediate(1);

        builder.Label(storeControlLabel);
        builder.StoreAAbsolute(0x2000);
        builder.LoadAZeroPage(CameraXAddress);
        builder.StoreAAbsolute(0x2005);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0x2005);
    }

    private void EmitStreamColumnFromAddresses(NesCameraConfig config)
    {
        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: config.StreamY, Height: config.StreamHeight));

        builder.LoadAAbsolute(0x2002);              // reset PPU address latch
        for (var row = 0; row < config.StreamHeight; row++)
        {
            EmitStreamColumnRow(row, config.StreamY + row);
        }
    }

    private void EmitStreamColumnRow(int sourceRow, int targetY)
    {
        var rightNameTableLabel = builder.CreateLabel("nes_stream_right_nt");
        var writeTileLabel = builder.CreateLabel("nes_stream_write_tile");
        var rowAddress = 0x2000 + targetY * 32;

        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.CompareImmediate(32);
        builder.BranchRelative(0xB0, rightNameTableLabel); // BCS rightNameTableLabel

        builder.LoadAImmediate(rowAddress >> 8);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);
        builder.JumpAbsolute(writeTileLabel);

        builder.Label(rightNameTableLabel);
        builder.LoadAImmediate((rowAddress >> 8) + 4);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.ClearCarry();
        builder.AddImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);

        builder.Label(writeTileLabel);
        builder.LoadAZeroPage(CameraSourceColumnAddress);
        builder.TransferAToX();
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowLabel(sourceRow));
        builder.StoreAAbsolute(0x2007);
    }

    internal void EmitCameraAabbTiles(Sdk2DOperation.CameraAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var config = EnsureCameraConfigured("camera_aabb_tiles");
        _ = WorldMapForFlagQuery("camera_aabb_tiles");
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (operation.ScreenX + width > NesTarget.Capabilities.ScreenPixels.Width)
        {
            throw new InvalidOperationException("camera_aabb_tiles screen span must fit within the visible NES width.");
        }

        var foundLabel = builder.CreateLabel("camera_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_tiles_next");
                EmitCameraTileFlagsAt(operation.ScreenX + xOffset, operation.WorldY, operation.WorldYOffset + yOffset, config, "camera_aabb_tiles");
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                builder.JumpAbsolute(foundLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitCameraAabbHitTop(Sdk2DOperation.CameraAabbHitTop operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var callName = "camera_aabb_hit_top";
        var config = EnsureCameraConfigured(callName);
        _ = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(255);
            return;
        }

        if (operation.ScreenX + width > NesTarget.Capabilities.ScreenPixels.Width)
        {
            throw new InvalidOperationException("camera_aabb_hit_top screen span must fit within the visible NES width.");
        }

        var endLabel = builder.CreateLabel("camera_aabb_hit_top_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_hit_top_next");
                var hitTopOffset = operation.WorldYOffset + yOffset;
                EmitCameraTileFlagsAt(operation.ScreenX + xOffset, operation.WorldY, hitTopOffset, config, callName);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                EmitWorldPixelTileTop(operation.WorldY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(255);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(int screenPixelX, SdkByteExpression worldY, int worldYOffset, NesCameraConfig config, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("camera_tile_flags_oob");
        var endLabel = builder.CreateLabel("camera_tile_flags_end");
        if (TrySdkConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
            EmitMapFlagsAtSourceColumnInA(row);
            return;
        }

        EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
        builder.StoreAZeroPage(CollisionColumnScratchAddress);

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        var inBoundsLabel = builder.CreateLabel("camera_tile_flags_in_bounds");
        builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
        builder.JumpAbsolute(outOfBoundsLabel);
        builder.Label(inBoundsLabel);
        builder.StoreAZeroPage(CollisionRowScratchAddress);

        for (var row = 0; row < worldMap.Height; row++)
        {
            var nextRowLabel = builder.CreateLabel("camera_tile_flags_next_row");
            builder.LoadAZeroPage(CollisionRowScratchAddress);
            builder.CompareImmediate(row);
            builder.BranchRelative(0xD0, nextRowLabel); // BNE nextRowLabel
            builder.LoadAZeroPage(CollisionColumnScratchAddress);
            EmitMapFlagsAtSourceColumnInA(row);
            builder.JumpAbsolute(endLabel);
            builder.Label(nextRowLabel);
        }

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitMapFlagsAtSourceColumnInA(int row)
    {
        builder.TransferAToX();
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapFlagRowLabel(row));
    }

    private void EmitCameraPixelToSourceColumn(int screenPixelX, int mapWidth)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");

        builder.LoadAZeroPage(CameraXAddress);
        builder.AndImmediate(0x07);
        if (screenPixelX != 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(screenPixelX);
        }

        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ClearCarry();
        builder.AddZeroPage(CameraTileColumnAddress);

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, endLabel); // BCC endLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitWorldPixelToTileCoordinate(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        EmitAddSignedImmediate(offset);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
    }

    private void EmitWorldPixelTileTop(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        EmitAddSignedImmediate(offset);
        builder.AndImmediate(0xF8);
    }

    private WorldMap2D WorldMapForFlagQuery(string callName)
    {
        return program.WorldMap
               ?? throw new InvalidOperationException($"{callName} requires world_map collision flag data.");
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

    private int SpriteWidth(string assetName)
    {
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        return asset.LogicalWidth;
    }

    private void EmitSpriteWidth(FunctionCall call)
    {
        builder.LoadAImmediate(SpriteWidth(call));
    }

    private int SpriteWidth(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var assetName = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sprite_width argument 1");
        return SpriteWidth(assetName);
    }

    private void EmitAnimationFrame(FunctionCall call)
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

            EmitSpriteDrawY(operation.Y, piece.YOffset, oamAddress);

            EmitSpriteTile(operation.Frame, asset, piece.TileOffset);
            builder.StoreAAbsolute((ushort)(oamAddress + 1));

            EmitSpriteDrawAttributes(operation.FlipX, operation.PaletteSlot, (ushort)(oamAddress + 2));

            EmitSpriteDrawX(operation.X, operation.FlipX, asset, piece, (ushort)(oamAddress + 3));
        }

        EmitOamDma();
    }

    private void EmitSpriteDrawY(SdkByteExpression yExpression, int offset, ushort oamAddress)
    {
        EmitSdkByteExpressionToA(yExpression);
        EmitAddSignedImmediate(offset - 1);
        builder.StoreAAbsolute(oamAddress);
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
        builder.StoreAAbsolute(oamAddress);
    }

    private void EmitSpriteDrawAttributes(SdkByteExpression? flipXExpression, int paletteSlot, ushort oamAddress)
    {
        if (flipXExpression is null || (TrySdkConst(flipXExpression, out var constant) && constant == 0))
        {
            builder.LoadAImmediate(paletteSlot);
            builder.StoreAAbsolute(oamAddress);
            return;
        }

        if (TrySdkConst(flipXExpression, out _))
        {
            builder.LoadAImmediate(paletteSlot | 0x40);
            builder.StoreAAbsolute(oamAddress);
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
        builder.StoreAAbsolute(oamAddress);
    }

    private void EmitMultiplyAByConstant(int factor)
    {
        if (factor == 1)
        {
            return;
        }

        builder.StoreAZeroPage(SpriteFrameScratchAddress);
        builder.LoadAImmediate(0);
        for (var i = 0; i < factor; i++)
        {
            builder.ClearCarry();
            builder.AddZeroPage(SpriteFrameScratchAddress);
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
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out var indexedBase, out var fieldName))
                {
                    EmitRuntimeMemberIndexToX(indexedBase);
                    builder.LoadAZeroPageX(RuntimeIndexedMemberBaseAddress(indexedBase, fieldName));
                }
                else
                {
                    builder.LoadAZeroPage(VariableAddress(NesVideoProgram.MemberAccessName(memberAccess)));
                }

                break;
            case IndexExpressionSyntax indexExpression:
                if (TryConst(indexExpression.Index, out _))
                {
                    builder.LoadAZeroPage(VariableAddress(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index")));
                }
                else
                {
                    EmitRuntimeIndexToX(indexExpression.Index);
                    builder.LoadAZeroPageX(ArrayBaseAddress(indexExpression.BaseIdentifier));
                }
                break;
            case FunctionCall call:
                EmitValueCallToA(call);
                break;
            case CastSyntax cast:
                RequireSupportedCastTarget(cast);
                EmitExpressionToA(cast.Expression);
                break;
            case ConditionalExpressionSyntax conditional:
                EmitConditionalExpressionToA(conditional);
                break;
            case UnaryExpressionSyntax unary when IsBooleanValueExpression(unary):
                EmitBooleanExpressionToA(unary);
                break;
            case BinaryExpressionSyntax binary when IsBooleanValueExpression(binary):
                EmitBooleanExpressionToA(binary);
                break;
            case BinaryExpressionSyntax binary:
                EmitBinaryExpressionToA(binary);
                break;
            default:
                throw new InvalidOperationException($"Unsupported NES expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitConditionalExpressionToA(ConditionalExpressionSyntax conditional)
    {
        var falseLabel = builder.CreateLabel("conditional_false");
        var endLabel = builder.CreateLabel("conditional_end");

        EmitConditionFalseJump(conditional.Condition, falseLabel);
        EmitExpressionToA(conditional.WhenTrue);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        EmitExpressionToA(conditional.WhenFalse);
        builder.Label(endLabel);
    }

    private static bool IsBooleanValueExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            UnaryExpressionSyntax { OperatorSymbol: "!" } => true,
            BinaryExpressionSyntax binary => binary.Operator.Symbol is "&&" or "||" or "==" or "!=" or "<" or "<=" or ">" or ">=",
            _ => false,
        };
    }

    private void EmitBooleanExpressionToA(ExpressionSyntax expression)
    {
        var falseLabel = builder.CreateLabel("bool_false");
        var endLabel = builder.CreateLabel("bool_end");

        EmitConditionFalseJump(expression, falseLabel);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        switch (binary.Operator.Symbol)
        {
            case "+":
                if (TryConst(binary.Right, out var addRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.ClearCarry();
                    builder.AddImmediate(addRight);
                    return;
                }

                if (TryConst(binary.Left, out var addLeft))
                {
                    EmitExpressionToA(binary.Right);
                    builder.ClearCarry();
                    builder.AddImmediate(addLeft);
                    return;
                }

                if (TryAddress(binary.Left, out var addAddress))
                {
                    EmitExpressionToA(binary.Right);
                    builder.ClearCarry();
                    builder.AddZeroPage(addAddress);
                    return;
                }

                break;
            case "-":
                if (TryConst(binary.Right, out var subtractRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.SetCarry();
                    builder.SubtractImmediate(subtractRight);
                    return;
                }

                if (TryAddress(binary.Right, out var subtractAddress))
                {
                    EmitExpressionToA(binary.Left);
                    builder.SetCarry();
                    builder.SubtractZeroPage(subtractAddress);
                    return;
                }

                EmitVariableOperandsToAAndScratch(binary.Left, binary.Right);
                builder.SetCarry();
                builder.SubtractZeroPage(ExpressionScratchAddress);
                return;
            case "&":
            case "|":
            case "^":
                if (EmitBitwiseBinaryExpressionToA(binary))
                {
                    return;
                }

                break;
        }

        throw new InvalidOperationException($"Unsupported NES binary expression '{binary.Operator.Symbol}'.");
    }

    private bool EmitBitwiseBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        if (TryConst(binary.Right, out var rightConstant))
        {
            EmitExpressionToA(binary.Left);
            EmitBitwiseImmediate(binary.Operator.Symbol, rightConstant);
            return true;
        }

        if (TryConst(binary.Left, out var leftConstant))
        {
            EmitExpressionToA(binary.Right);
            EmitBitwiseImmediate(binary.Operator.Symbol, leftConstant);
            return true;
        }

        if (TryAddress(binary.Right, out var rightAddress))
        {
            EmitExpressionToA(binary.Left);
            EmitBitwiseZeroPage(binary.Operator.Symbol, rightAddress);
            return true;
        }

        if (TryAddress(binary.Left, out var leftAddress))
        {
            EmitExpressionToA(binary.Right);
            EmitBitwiseZeroPage(binary.Operator.Symbol, leftAddress);
            return true;
        }

        return false;
    }

    private void EmitBitwiseImmediate(string op, int value)
    {
        var mask = value & 0xFF;
        switch (op)
        {
            case "&":
                builder.AndImmediate(mask);
                return;
            case "|":
                builder.OrImmediate(mask);
                return;
            case "^":
                builder.XorImmediate(mask);
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES bitwise operator '{op}'.");
        }
    }

    private void EmitBitwiseZeroPage(string op, byte address)
    {
        switch (op)
        {
            case "&":
                builder.AndZeroPage(address);
                return;
            case "|":
                builder.OrZeroPage(address);
                return;
            case "^":
                builder.XorZeroPage(address);
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES bitwise operator '{op}'.");
        }
    }

    private bool TryAddress(ExpressionSyntax expression, out byte address)
    {
        return TryDirectAddress(expression, out address);
    }

    private bool TryDirectAddress(ExpressionSyntax expression, out byte address)
    {
        switch (expression)
        {
            case CastSyntax cast:
                RequireSupportedCastTarget(cast);
                return TryDirectAddress(cast.Expression, out address);
            case IdentifierSyntax identifier:
                address = VariableAddress(identifier.Identifier);
                return true;
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out _, out _))
                {
                    address = 0;
                    return false;
                }

                address = VariableAddress(NesVideoProgram.MemberAccessName(memberAccess));
                return true;
            case IndexExpressionSyntax indexExpression when TryConst(indexExpression.Index, out _):
                address = VariableAddress(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index"));
                return true;
            default:
                address = 0;
                return false;
        }
    }

    private void EmitRuntimeIndexToX(ExpressionSyntax index)
    {
        EmitExpressionToA(index);
        builder.TransferAToX();
    }

    private void EmitRuntimeMemberIndexToX(IndexExpressionSyntax indexExpression)
    {
        var layout = StructArrayLayoutFor(indexExpression.BaseIdentifier);
        EmitExpressionToA(indexExpression.Index);
        EmitMultiplyA(layout.Stride);
        builder.TransferAToX();
    }

    private void EmitRuntimeMemberIndexToX(string baseIdentifier, SdkByteExpression index)
    {
        var layout = StructArrayLayoutFor(baseIdentifier);
        EmitSdkByteExpressionToA(index);
        EmitMultiplyA(layout.Stride);
        builder.TransferAToX();
    }

    private void EmitMultiplyA(int multiplier)
    {
        if (multiplier <= 1)
        {
            return;
        }

        builder.StoreAZeroPage(RuntimeIndexScratchAddress);
        for (var count = 1; count < multiplier; count++)
        {
            builder.ClearCarry();
            builder.AddZeroPage(RuntimeIndexScratchAddress);
        }
    }

    private byte ArrayBaseAddress(string baseIdentifier)
    {
        return VariableAddress(IndexedElementName(baseIdentifier, 0));
    }

    private byte RuntimeIndexedMemberBaseAddress(IndexExpressionSyntax indexExpression, string fieldName)
    {
        _ = StructArrayLayoutFor(indexExpression.BaseIdentifier).FieldOffsets[fieldName];
        return VariableAddress(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
    }

    private byte RuntimeIndexedMemberBaseAddress(string baseIdentifier, string fieldName)
    {
        _ = StructArrayLayoutFor(baseIdentifier).FieldOffsets[fieldName];
        return VariableAddress(IndexedMemberName(baseIdentifier, 0, fieldName));
    }

    private bool TryRuntimeIndexedMemberAccess(MemberAccessSyntax memberAccess, out IndexExpressionSyntax indexExpression, out string fieldName)
    {
        if (memberAccess.Target is IndexExpressionSyntax candidate
            && !TryConst(candidate.Index, out _)
            && structArrays.ContainsKey(candidate.BaseIdentifier)
            && StructArrayLayoutFor(candidate.BaseIdentifier).FieldOffsets.ContainsKey(memberAccess.Member))
        {
            indexExpression = candidate;
            fieldName = memberAccess.Member;
            return true;
        }

        indexExpression = null!;
        fieldName = string.Empty;
        return false;
    }

    private StructArrayLayout StructArrayLayoutFor(string baseIdentifier)
    {
        return structArrays.TryGetValue(baseIdentifier, out var layout)
            ? layout
            : throw new InvalidOperationException($"NES target has no struct array layout for '{baseIdentifier}'.");
    }

    private void RequireExpressionPreservesX(ExpressionSyntax expression, string context)
    {
        if (!PreservesX(expression))
        {
            throw new InvalidOperationException($"NES target cannot use expression '{expression.GetType().Name}' as the right side of a {context} yet because it also needs X for array indexing.");
        }
    }

    private bool PreservesX(ExpressionSyntax expression)
    {
        if (TryConst(expression, out _))
        {
            return true;
        }

        return expression switch
        {
            IdentifierSyntax => true,
            MemberAccessSyntax memberAccess => !TryRuntimeIndexedMemberAccess(memberAccess, out _, out _),
            IndexExpressionSyntax indexExpression => TryConst(indexExpression.Index, out _),
            CastSyntax cast => PreservesX(cast.Expression),
            ConditionalExpressionSyntax conditional => PreservesX(conditional.Condition) && PreservesX(conditional.WhenTrue) && PreservesX(conditional.WhenFalse),
            BinaryExpressionSyntax binary => PreservesX(binary.Left) && PreservesX(binary.Right),
            _ => false,
        };
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
            case "camera_aabb_tiles":
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraAabbTiles(call));
                break;
            case "camera_aabb_hit_top":
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraAabbHitTop(call));
                break;
            case "animation_frame":
                EmitAnimationFrame(call);
                break;
            case "sprite_width":
                EmitSpriteWidth(call);
                break;
            case "__rs_actor_camera_x_lo":
                NesVideoProgram.RequireArity(call, 0);
                builder.LoadAZeroPage(CameraXAddress);
                break;
            case "__rs_actor_camera_x_hi":
                NesVideoProgram.RequireArity(call, 0);
                builder.LoadAZeroPage(CameraTileColumnAddress);
                builder.ShiftRightA();
                builder.ShiftRightA();
                builder.ShiftRightA();
                builder.ShiftRightA();
                builder.ShiftRightA();
                break;
            default:
                if (TryEmitUserValueFunction(call))
                {
                    break;
                }

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
        if (expression is CastSyntax cast)
        {
            RequireSupportedCastTarget(cast);
            return TryConst(cast.Expression, out value);
        }

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

    private static string IndexedElementName(string baseIdentifier, int index)
    {
        return $"{baseIdentifier}[{index}]";
    }

    private static string IndexedMemberName(string baseIdentifier, int index, string fieldName)
    {
        return $"{IndexedElementName(baseIdentifier, index)}.{fieldName}";
    }

    private Dictionary<string, int> StructFieldOffsets(StructSyntax structSyntax, string targetName)
    {
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var offset = 0;
        foreach (var field in structSyntax.Fields)
        {
            if (!IsByteSizedStructArrayFieldType(field.Type))
            {
                throw new InvalidOperationException($"{targetName} target struct array field type '{field.Type}' is not byte-sized; use u8, i8, bool, or enum fields until mixed-width pool layout is implemented.");
            }

            offsets.Add(field.Name, offset);
            offset++;
        }

        return offsets;
    }

    private bool IsByteSizedStructArrayFieldType(string type)
    {
        return type is "i8" or "u8" or "bool" || program.Enums.ContainsKey(type);
    }

    private static string StorageKey(SdkStorageLocation location)
    {
        return location switch
        {
            SdkStorageLocation.Local local => local.Name,
            SdkStorageLocation.Field field => $"{StorageKey(field.Target)}.{field.FieldName}",
            SdkStorageLocation.IndexedElement indexed => IndexedElementName(indexed.BaseName, indexed.Index),
            SdkStorageLocation.RuntimeIndexedField => throw new InvalidOperationException("Runtime indexed SDK fields must be emitted directly."),
            _ => throw new InvalidOperationException($"Unsupported SDK storage location '{location.GetType().Name}'."),
        };
    }

    private static string IndexedElementName(string baseIdentifier, ExpressionSyntax index, string context)
    {
        var value = CheckedRange(NesVideoProgram.ConstValue(index, context), 0, 255, context);
        return IndexedElementName(baseIdentifier, value);
    }

    private static int CheckedRange(int value, int min, int max, string context)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{context} must be between {min} and {max}.");
        }

        return value;
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

    private readonly record struct NesCameraConfig(int MapWidth, int StreamY, int StreamHeight);

    private readonly record struct LoopTarget(string BreakLabel, string ContinueLabel);
}

internal sealed class PrgBuilder
{
    public const ushort BaseAddress = 0x8000;
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

    public void LoadAZeroPageX(byte address) => Emit(0xB5, address);

    public void StoreAZeroPage(byte address) => Emit(0x85, address);

    public void StoreAZeroPageX(byte address) => Emit(0x95, address);

    public void LoadAAbsolute(ushort address) => Emit(0xAD, Low(address), High(address));

    public void StoreAAbsolute(ushort address) => Emit(0x8D, Low(address), High(address));

    public void StoreAAbsoluteX(ushort address) => Emit(0x9D, Low(address), High(address));

    public void AndImmediate(int value) => Emit(0x29, CheckedByte(value));

    public void AndZeroPage(byte address) => Emit(0x25, address);

    public void OrImmediate(int value) => Emit(0x09, CheckedByte(value));

    public void OrZeroPage(byte address) => Emit(0x05, address);

    public void XorImmediate(int value) => Emit(0x49, CheckedByte(value));

    public void XorZeroPage(byte address) => Emit(0x45, address);

    public void CompareImmediate(int value) => Emit(0xC9, CheckedByte(value));

    public void CompareZeroPage(byte address) => Emit(0xC5, address);

    public void ClearCarry() => Emit(0x18);

    public void SetCarry() => Emit(0x38);

    public void AddImmediate(int value) => Emit(0x69, CheckedByte(value));

    public void AddZeroPage(byte address) => Emit(0x65, address);

    public void AddZeroPageX(byte address) => Emit(0x75, address);

    public void SubtractImmediate(int value) => Emit(0xE9, CheckedByte(value));

    public void SubtractZeroPage(byte address) => Emit(0xE5, address);

    public void PushA() => Emit(0x48);

    public void PullA() => Emit(0x68);

    public void IncrementX() => Emit(0xE8);

    public void TransferAToX() => Emit(0xAA);

    public void ShiftRightA() => Emit(0x4A);

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
