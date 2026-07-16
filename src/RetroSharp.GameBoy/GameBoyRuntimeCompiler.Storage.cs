using System.Globalization;
using System.Text;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

internal sealed partial class GameBoyRuntimeCompiler
{
    private void EmitInputStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Input.Current);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Input.Previous);
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
            case DoWhileSyntax doWhileSyntax:
                EmitDoWhile(doWhileSyntax);
                break;
            case RangeForSyntax rangeForSyntax:
                EmitFor(RangeForLowerer.Lower(rangeForSyntax));
                break;
            case ForSyntax forSyntax:
                EmitFor(forSyntax);
                break;
            case IfElseSyntax ifElseSyntax:
                EmitIf(ifElseSyntax);
                break;
            case BreakSyntax:
                EmitBreak();
                break;
            case ContinueSyntax:
                EmitContinue();
                break;
            case ReturnSyntax:
                break;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy statement '{statement.GetType().Name}'.");
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

        throw new InvalidOperationException($"Game Boy target does not support local type '{declaration.Type}' yet.");
    }

    private void EmitArrayDeclaration(DeclarationSyntax declaration)
    {
        if (program.Structs.TryGetValue(declaration.Type, out var structSyntax))
        {
            EmitStructArrayDeclaration(declaration, structSyntax);
            return;
        }

        if (!IsScalarLocalType(declaration.Type))
        {
            throw new InvalidOperationException($"Game Boy target only supports scalar fixed-size arrays; '{declaration.Type}' is not supported yet.");
        }

        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(GameBoyVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var elementAddresses = new List<ushort>();
        for (var index = 0; index < length; index++)
        {
            var sourceName = IndexedElementName(declaration.Name, index);
            var scopedName = IndexedElementName(declarationName, index);
            MapScopedVariableName(sourceName, scopedName);
            var address = DeclareVariable(scopedName, declaration.Type);
            elementAddresses.Add(address);
            EmitZeroToStorage(address, declaration.Type);
        }

        if (declaration.Initialization.HasValue)
        {
            EmitArrayInitializer(declaration, declaration.Initialization.Value, length, elementAddresses);
        }
    }

    private void EmitStructArrayDeclaration(DeclarationSyntax declaration, StructSyntax structSyntax)
    {
        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(GameBoyVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var fieldOffsets = StructFieldOffsets(structSyntax);
        var stride = StructStride(structSyntax);
        if (length * stride > 255)
        {
            throw new InvalidOperationException($"Game Boy target struct array '{declaration.Name}' uses {length * stride} byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.");
        }

        structArrays.Add(declarationName, new StructArrayLayout(stride, fieldOffsets));

        var fieldNames = structSyntax.Fields.Select(field => field.Name).ToList();
        for (var index = 0; index < length; index++)
        {
            foreach (var field in structSyntax.Fields)
            {
                var sourceName = IndexedMemberName(declaration.Name, index, field.Name);
                var scopedName = IndexedMemberName(declarationName, index, field.Name);
                MapScopedVariableName(sourceName, scopedName);
                var address = DeclareVariable(scopedName, field.Type);
                TrackSignedByteType(field.Type, scopedName);
                EmitZeroToStorage(address, field.Type);
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
            throw new InvalidOperationException($"Game Boy target requires an array initializer for local struct array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"Game Boy target struct array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
        }

        var knownFields = fieldNames.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < arrayInitializer.Elements.Count; index++)
        {
            if (arrayInitializer.Elements[index] is not StructInitializerSyntax structInitializer)
            {
                throw new InvalidOperationException($"Game Boy target requires struct initializers for elements of struct array '{declaration.Name}'.");
            }

            var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
            foreach (var field in structInitializer.Fields)
            {
                if (!initializedFields.TryAdd(field.Name, field.Expression))
                {
                    throw new InvalidOperationException($"Game Boy target struct array initializer for '{declaration.Name}' element {index} supplies field '{field.Name}' more than once.");
                }

                if (!knownFields.Contains(field.Name))
                {
                    throw new InvalidOperationException($"Game Boy target struct array initializer for '{declaration.Name}' has no field named '{field.Name}'.");
                }
            }

            foreach (var fieldName in fieldNames)
            {
                if (!initializedFields.TryGetValue(fieldName, out var expression))
                {
                    continue;
                }

                var storageName = IndexedMemberName(declaration.Name, index, fieldName);
                var address = VariableAddress(storageName);
                EmitExpressionToStorage(expression, address, VariableStorageType(storageName));
            }
        }
    }

    private void EmitArrayInitializer(DeclarationSyntax declaration, ExpressionSyntax initialization, int length, IReadOnlyList<ushort> elementAddresses)
    {
        if (initialization is not ArrayInitializerSyntax arrayInitializer)
        {
            throw new InvalidOperationException($"Game Boy target requires an array initializer for local array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"Game Boy target array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
        }

        for (var index = 0; index < arrayInitializer.Elements.Count; index++)
        {
            EmitExpressionToStorage(arrayInitializer.Elements[index], elementAddresses[index], declaration.Type);
        }
    }

    private void EmitByteBackedDeclaration(DeclarationSyntax declaration)
    {
        var scopedName = DeclareScopedVariableName(declaration.Name);
        var address = DeclareVariable(scopedName, declaration.Type);
        TrackImmutable(declaration);
        TrackSignedByteType(declaration.Type, scopedName);

        if (declaration.Initialization.HasValue)
        {
            EmitExpressionToStorage(declaration.Initialization.Value, address, declaration.Type);
            return;
        }

        EmitZeroToStorage(address, declaration.Type);
    }

    private void EmitStructDeclaration(DeclarationSyntax declaration, StructSyntax structSyntax)
    {
        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var fieldAddresses = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var fieldNames = new List<string>();
        foreach (var field in structSyntax.Fields)
        {
            if (!IsScalarLocalType(field.Type))
            {
                throw new InvalidOperationException($"Game Boy target does not support struct field type '{field.Type}' yet.");
            }

            var sourceName = $"{declaration.Name}.{field.Name}";
            var scopedName = $"{declarationName}.{field.Name}";
            MapScopedVariableName(sourceName, scopedName);
            var address = DeclareVariable(scopedName, field.Type);
            TrackSignedByteType(field.Type, scopedName);
            fieldAddresses.Add(field.Name, address);
            fieldNames.Add(field.Name);
            EmitZeroToStorage(address, field.Type);
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
        IReadOnlyDictionary<string, ushort> fieldAddresses)
    {
        if (initialization is not StructInitializerSyntax structInitializer)
        {
            throw new InvalidOperationException($"Game Boy target requires a struct initializer for local struct '{declaration.Name}'.");
        }

        var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var field in structInitializer.Fields)
        {
            if (!initializedFields.TryAdd(field.Name, field.Expression))
            {
                throw new InvalidOperationException($"Game Boy target struct initializer for '{declaration.Name}' supplies field '{field.Name}' more than once.");
            }

            if (!fieldAddresses.ContainsKey(field.Name))
            {
                throw new InvalidOperationException($"Game Boy target struct initializer for '{declaration.Name}' has no field named '{field.Name}'.");
            }
        }

        foreach (var fieldName in fieldNames)
        {
            if (!initializedFields.TryGetValue(fieldName, out var expression))
            {
                continue;
            }

            var storageName = $"{declaration.Name}.{fieldName}";
            EmitExpressionToStorage(expression, fieldAddresses[fieldName], VariableStorageType(storageName));
        }
    }

    private ushort DeclareVariable(string name, string type)
    {
        if (!declaredVariables.Add(name))
        {
            throw new InvalidOperationException($"Variable '{name}' is already declared.");
        }

        if (variables.ContainsKey(name))
        {
            throw new InvalidOperationException($"Variable '{name}' is already declared.");
        }

        var size = StorageSize(type);
        GameBoyRuntimeMemoryLayout.ValidateUserLocalBytes(
            nextVariableAddress + size - GameBoyRuntimeMemoryLayout.UserLocals.Start);

        var address = nextVariableAddress;
        nextVariableAddress += (ushort)size;
        variables.Add(name, address);
        variableTypes.Add(name, type);
        return address;
    }

    private void EmitZeroToStorage(ushort address, string type)
    {
        builder.LoadAImmediate(0);
        builder.StoreA(address);
        if (IsWordBackedType(type))
        {
            builder.StoreA(HighAddress(address));
        }
    }

    private void EmitExpressionToStorage(ExpressionSyntax expression, ushort address, string type)
    {
        if (IsWordBackedType(type))
        {
            EmitWordExpressionToStorage(expression, address, type);
            return;
        }

        ValidateWorldHitTopNarrowing(expression, type);
        EmitExpressionToA(expression);
        builder.StoreA(address);
    }

    private void EmitWordExpressionToStorage(ExpressionSyntax expression, ushort address, string targetType)
    {
        if (TryConst(expression, out var constant))
        {
            EmitStoreWordImmediate(address, constant);
            return;
        }

        switch (expression)
        {
            case CastSyntax cast:
                if (IsWordBackedType(cast.Type))
                {
                    EmitWordExpressionToStorage(cast.Expression, address, cast.Type);
                    return;
                }

                EmitExpressionToA(cast.Expression);
                builder.StoreA(address);
                EmitHighByteFromLowAToStorage(HighAddress(address), cast.Type);
                return;
            case IdentifierSyntax { Identifier: "true" }:
                EmitStoreWordImmediate(address, 1);
                return;
            case IdentifierSyntax { Identifier: "false" }:
                EmitStoreWordImmediate(address, 0);
                return;
            case IdentifierSyntax or MemberAccessSyntax or IndexExpressionSyntax when TryDirectStorageExpression(expression, out var sourceAddress, out var sourceType):
                EmitCopyToWordStorage(sourceAddress, sourceType, address);
                return;
            case MemberAccessSyntax memberAccess when TryRuntimeIndexedMemberAccess(memberAccess, out var indexedBase, out var fieldName):
                EmitRuntimeIndexedMemberAddressToHl(indexedBase, fieldName);
                EmitRuntimeStorageFromHlToWordStorage(address, VariableStorageType(IndexedMemberName(indexedBase.BaseIdentifier, 0, fieldName)));
                return;
            case IndexExpressionSyntax indexExpression:
                EmitRuntimeIndexedAddressToHl(indexExpression.BaseIdentifier, indexExpression.Index);
                EmitRuntimeStorageFromHlToWordStorage(address, VariableStorageType(IndexedElementName(indexExpression.BaseIdentifier, 0)));
                return;
            case FunctionCall call:
                if (TryEmitWordValueFunctionToStorage(call, address, targetType))
                {
                    return;
                }

                EmitValueCallToA(call);
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
                return;
            case ConditionalExpressionSyntax conditional:
                EmitWordConditionalExpressionToStorage(conditional, address, targetType);
                return;
            case UnaryExpressionSyntax unary when IsBooleanValueExpression(unary):
                EmitBooleanExpressionToA(unary);
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
                return;
            case BinaryExpressionSyntax binary when IsBooleanValueExpression(binary):
                EmitBooleanExpressionToA(binary);
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
                return;
            case BinaryExpressionSyntax { Operator.Symbol: "+" } binary:
                EmitWordExpressionToStorage(binary.Left, address, targetType);
                EmitAddWordIntoStorage(address, binary.Right);
                return;
            case BinaryExpressionSyntax { Operator.Symbol: "-" } binary:
                EmitWordExpressionToStorage(binary.Left, address, targetType);
                EmitSubtractWordFromStorage(address, binary.Right);
                return;
            default:
                EmitExpressionToA(expression);
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
                return;
        }
    }

    private void EmitRuntimeStorageFromHlToWordStorage(ushort address, string sourceType)
    {
        builder.LoadAFromHl();
        builder.StoreA(address);
        if (IsWordBackedType(sourceType))
        {
            builder.IncrementHl();
            builder.LoadAFromHl();
        }
        else if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(HighAddress(address));
    }

    private void EmitWordConditionalExpressionToStorage(ConditionalExpressionSyntax conditional, ushort address, string targetType)
    {
        var falseLabel = builder.CreateLabel("word_conditional_false");
        var endLabel = builder.CreateLabel("word_conditional_end");

        EmitConditionFalseJump(conditional.Condition, falseLabel);
        EmitWordExpressionToStorage(conditional.WhenTrue, address, targetType);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        EmitWordExpressionToStorage(conditional.WhenFalse, address, targetType);
        builder.Label(endLabel);
    }

    private void EmitStoreWordImmediate(ushort address, int value)
    {
        builder.LoadAImmediate(value & 0xFF);
        builder.StoreA(address);
        builder.LoadAImmediate((value >> 8) & 0xFF);
        builder.StoreA(HighAddress(address));
    }

    private void EmitStoreSplitWordImmediate(ushort lowAddress, ushort highAddress, int value)
    {
        builder.LoadAImmediate(value & 0xFF);
        builder.StoreA(lowAddress);
        builder.LoadAImmediate((value >> 8) & 0xFF);
        builder.StoreA(highAddress);
    }

    private void EmitCopyToWordStorage(ushort sourceAddress, string sourceType, ushort targetAddress)
    {
        builder.LoadA(sourceAddress);
        builder.StoreA(targetAddress);

        if (IsWordBackedType(sourceType))
        {
            builder.LoadA(HighAddress(sourceAddress));
        }
        else if (sourceType == "i8")
        {
            builder.LoadA(sourceAddress);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(HighAddress(targetAddress));
    }

    private void EmitAddWordIntoStorage(ushort address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadA(address);
            builder.AddAImmediate(constant & 0xFF);
            builder.StoreA(address);
            builder.LoadA(HighAddress(address));
            builder.AdcAImmediate((constant >> 8) & 0xFF);
            builder.StoreA(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow, WordExpressionType(right));
            rightAddress = GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow;
            rightType = "u16";
        }

        // Sign-extending an i8 addend clobbers the carry flag, so its high byte must be materialized
        // to scratch before the low-byte add. Wider operands load the high byte with a carry-safe LD
        // after the low-byte add, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitStoreHighByteToScratch(rightAddress, rightType, GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
        }

        builder.LoadA(rightAddress);
        builder.LoadBFromA();
        builder.LoadA(address);
        builder.AddAFromB();
        builder.StoreA(address);

        if (hoistI8HighByte)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
            builder.LoadBFromA();
        }
        else
        {
            EmitLoadHighByteToB(rightAddress, rightType);
        }

        builder.LoadA(HighAddress(address));
        builder.AdcAFromB();
        builder.StoreA(HighAddress(address));
    }

    private void EmitSubtractWordFromStorage(ushort address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadA(address);
            builder.SubtractAImmediate(constant & 0xFF);
            builder.StoreA(address);
            builder.LoadA(HighAddress(address));
            builder.SbcAImmediate((constant >> 8) & 0xFF);
            builder.StoreA(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow, WordExpressionType(right));
            rightAddress = GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow;
            rightType = "u16";
        }

        // Sign-extending an i8 operand clobbers the carry/borrow flag, so its high byte must be
        // materialized to scratch before the low-byte subtract. Wider operands load the high byte
        // with a carry-safe LD after the low-byte subtract, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitStoreHighByteToScratch(rightAddress, rightType, GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
        }

        builder.LoadA(rightAddress);
        builder.LoadBFromA();
        builder.LoadA(address);
        builder.SubtractB();
        builder.StoreA(address);

        if (hoistI8HighByte)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
            builder.LoadBFromA();
        }
        else
        {
            EmitLoadHighByteToB(rightAddress, rightType);
        }

        builder.LoadA(HighAddress(address));
        builder.SbcB();
        builder.StoreA(HighAddress(address));
    }

    private void EmitLoadHighByteToB(ushort address, string type)
    {
        if (IsWordBackedType(type))
        {
            builder.LoadA(HighAddress(address));
        }
        else if (type == "i8")
        {
            builder.LoadA(address);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.LoadBFromA();
    }

    private void EmitStoreHighByteToScratch(ushort address, string type, ushort scratchAddress)
    {
        if (IsWordBackedType(type))
        {
            builder.LoadA(HighAddress(address));
        }
        else if (type == "i8")
        {
            builder.LoadA(address);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(scratchAddress);
    }

    private void EmitHighByteFromLowAToStorage(ushort highAddress, string sourceType)
    {
        if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(highAddress);
    }

    private void EmitSignExtensionFromA()
    {
        var negativeLabel = builder.CreateLabel("sign_extend_negative");
        var endLabel = builder.CreateLabel("sign_extend_end");

        builder.AndImmediate(0x80);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, negativeLabel); // JP NZ,negativeLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(negativeLabel);
        builder.LoadAImmediate(0xFF);
        builder.Label(endLabel);
    }

    private static ushort HighAddress(ushort lowAddress) => (ushort)(lowAddress + 1);

    private void TrackImmutable(DeclarationSyntax declaration)
    {
        if (declaration.IsImmutable)
        {
            immutableVariables.Add(ScopedVariableName(declaration.Name));
        }
    }

    // Signed scalar locations use sign-bit-flipped ordering in relational compares. Unsigned
    // scalars, bools, and enums keep ordinary unsigned comparison.
    private void TrackSignedByteType(string type, string scopedName)
    {
        if (type == "i8")
        {
            signedByteLocations.Add(scopedName);
        }
    }

    private bool IsSignedRelationalOperand(ExpressionSyntax expression)
    {
        if (TryExpressionStorageType(expression, out var type))
        {
            return type is "i8" or "i16";
        }

        return expression switch
        {
            IdentifierSyntax identifier => signedByteLocations.Contains(ScopedVariableName(identifier.Identifier)),
            MemberAccessSyntax memberAccess when HasIdentifierRoot(memberAccess) =>
                signedByteLocations.Contains(ScopedVariableName(GameBoyVideoProgram.MemberAccessName(memberAccess))),
            _ => false,
        };
    }

    private static bool HasIdentifierRoot(MemberAccessSyntax memberAccess)
    {
        ExpressionSyntax current = memberAccess;
        while (current is MemberAccessSyntax member)
        {
            current = member.Target;
        }

        return current is IdentifierSyntax;
    }

    private void PushInlineVariableScope()
    {
        inlineVariableScopes.Push(new InlineVariableScope($"__retrosharp_inline_{++nextInlineVariableScopeId}"));
    }

    private void PopInlineVariableScope()
    {
        inlineVariableScopes.Pop();
    }

    private string DeclareScopedVariableName(string name)
    {
        if (!inlineVariableScopes.TryPeek(out var scope))
        {
            return name;
        }

        if (scope.Names.TryGetValue(name, out var scopedName))
        {
            return scopedName;
        }

        scopedName = $"{scope.Prefix}_{name}";
        scope.Names.Add(name, scopedName);
        return scopedName;
    }

    private void MapScopedVariableName(string name, string scopedName)
    {
        if (inlineVariableScopes.TryPeek(out var scope))
        {
            scope.Names[name] = scopedName;
        }
    }

    private string ScopedVariableName(string name)
    {
        foreach (var scope in inlineVariableScopes)
        {
            if (scope.Names.TryGetValue(name, out var scopedName))
            {
                return scopedName;
            }
        }

        return name;
    }

    private static bool IsByteBackedType(string type)
    {
        return type is "i8" or "u8" or "i16" or "u16" or "bool";
    }

    private bool IsByteBackedLocalType(string type)
    {
        return IsByteBackedType(type) || program.Enums.ContainsKey(type);
    }

    private bool IsScalarLocalType(string type)
    {
        return IsByteBackedLocalType(type);
    }

    private static bool IsWordBackedType(string type)
    {
        return type is "i16" or "u16";
    }

    private int StorageSize(string type)
    {
        return IsWordBackedType(type) ? 2 : 1;
    }

    private string VariableStorageType(string name)
    {
        var scopedName = ScopedVariableName(name);
        if (!variableTypes.TryGetValue(scopedName, out var type))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return type;
    }

    private bool TryDirectStorageExpression(ExpressionSyntax expression, out ushort address, out string type)
    {
        switch (expression)
        {
            case CastSyntax cast:
                return TryDirectStorageExpression(cast.Expression, out address, out type);
            case IdentifierSyntax identifier:
                address = VariableAddress(identifier.Identifier);
                type = VariableStorageType(identifier.Identifier);
                return true;
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out _, out _))
                {
                    address = 0;
                    type = string.Empty;
                    return false;
                }

                var memberName = GameBoyVideoProgram.MemberAccessName(memberAccess);
                address = VariableAddress(memberName);
                type = VariableStorageType(memberName);
                return true;
            case IndexExpressionSyntax indexExpression when TryConst(indexExpression.Index, out _):
                var elementName = IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index");
                address = VariableAddress(elementName);
                type = VariableStorageType(elementName);
                return true;
            default:
                address = 0;
                type = string.Empty;
                return false;
        }
    }

    private string WordExpressionType(ExpressionSyntax expression)
    {
        if (TryExpressionStorageType(expression, out var type) && IsWordBackedType(type))
        {
            return type;
        }

        return "u16";
    }

    private bool TryExpressionStorageType(ExpressionSyntax expression, out string type)
    {
        switch (expression)
        {
            case CastSyntax cast:
                type = cast.Type;
                return true;
            case IdentifierSyntax { Identifier: "true" or "false" }:
                break;
            case IdentifierSyntax identifier:
                type = VariableStorageType(identifier.Identifier);
                return true;
            case MemberAccessSyntax memberAccess when !TryRuntimeIndexedMemberAccess(memberAccess, out _, out _):
                type = VariableStorageType(GameBoyVideoProgram.MemberAccessName(memberAccess));
                return true;
            case IndexExpressionSyntax indexExpression when TryConst(indexExpression.Index, out _):
                type = VariableStorageType(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index"));
                return true;
            case BinaryExpressionSyntax binary when binary.Operator.Symbol is "+" or "-":
                if (TryExpressionStorageType(binary.Left, out var leftType) && IsWordBackedType(leftType))
                {
                    type = leftType;
                    return true;
                }

                if (TryExpressionStorageType(binary.Right, out var rightType) && IsWordBackedType(rightType))
                {
                    type = rightType;
                    return true;
                }

                break;
        }

        type = string.Empty;
        return false;
    }

    private void RequireSupportedCastTarget(CastSyntax cast)
    {
        if (!IsByteBackedLocalType(cast.Type))
        {
            throw new InvalidOperationException($"Game Boy target only supports explicit casts to byte-backed local types; '{cast.Type}' is not supported yet.");
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
                throw new InvalidOperationException($"Unsupported Game Boy expression statement '{expressionStatement.Expression.GetType().Name}'.");
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
        var targetType = LValueStorageType(assignment.Left);
        if (IsWordBackedType(targetType))
        {
            EmitWordAssignment(assignment, address, targetType);
            return;
        }

        ValidateWorldHitTopNarrowing(assignment.Right, targetType);
        EmitAssignmentRightToA(assignment);
        builder.StoreA(address);
    }

    private void RequireMutableAssignmentTarget(LValue lValue)
    {
        if (AssignedRoot(lValue) is { } name && immutableVariables.Contains(ScopedVariableName(name)))
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
        EmitRuntimeIndexedAddressToHl(indexLValue.BaseIdentifier, indexLValue.Index);
        var elementType = VariableStorageType(IndexedElementName(indexLValue.BaseIdentifier, 0));
        if (IsWordBackedType(elementType))
        {
            EmitRuntimeIndexedWordAssignment(assignment, elementType, "runtime indexed assignment");
            return;
        }

        ValidateWorldHitTopNarrowing(assignment.Right, elementType);
        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesHl(assignment.Right, "runtime indexed assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreHlA();
                return;
            case "+=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.AddAImmediate(addRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.AddAFromC();
                builder.StoreHlA();
                return;
            case "-=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractAImmediate(subtractRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.LoadBFromA();
                builder.LoadAFromC();
                builder.SubtractB();
                builder.StoreHlA();
                return;
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "&", "runtime indexed &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "|", "runtime indexed |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "^", "runtime indexed ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitRuntimeIndexedBitwiseCompoundAssignment(ExpressionSyntax right, string op, string context)
    {
        builder.LoadAFromHl();
        if (TryConst(right, out var constant))
        {
            EmitBitwiseImmediate(op, constant);
            builder.StoreHlA();
            return;
        }

        RequireExpressionPreservesHl(right, context);
        builder.LoadCFromA();
        EmitExpressionToA(right);
        EmitBitwiseAFromC(op);
        builder.StoreHlA();
    }

    private void EmitRuntimeIndexedMemberAssignment(IndexExpressionSyntax indexExpression, string fieldName, AssignmentSyntax assignment)
    {
        EmitRuntimeIndexedMemberAddressToHl(indexExpression, fieldName);
        var fieldType = VariableStorageType(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
        if (IsWordBackedType(fieldType))
        {
            EmitRuntimeIndexedWordAssignment(assignment, fieldType, "runtime indexed struct field assignment");
            return;
        }

        ValidateWorldHitTopNarrowing(assignment.Right, fieldType);
        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesHl(assignment.Right, "runtime indexed struct field assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreHlA();
                return;
            case "+=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.AddAImmediate(addRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed struct field compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.AddAFromC();
                builder.StoreHlA();
                return;
            case "-=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractAImmediate(subtractRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed struct field compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.LoadBFromA();
                builder.LoadAFromC();
                builder.SubtractB();
                builder.StoreHlA();
                return;
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "&", "runtime indexed struct field &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "|", "runtime indexed struct field |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "^", "runtime indexed struct field ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitRuntimeIndexedWordAssignment(AssignmentSyntax assignment, string targetType, string context)
    {
        if (assignment.OperatorSymbol != "=")
        {
            throw new InvalidOperationException($"Game Boy target only supports direct '=' for 16-bit {context}.");
        }

        RequireExpressionPreservesHl(assignment.Right, context);
        EmitWordExpressionToHl(assignment.Right, targetType);
    }

    private void EmitWordExpressionToHl(ExpressionSyntax expression, string targetType)
    {
        if (TryConst(expression, out var constant))
        {
            builder.LoadAImmediate(constant & 0xFF);
            builder.StoreHlA();
            builder.IncrementHl();
            builder.LoadAImmediate((constant >> 8) & 0xFF);
            builder.StoreHlA();
            return;
        }

        if (TryDirectStorageExpression(expression, out var sourceAddress, out var sourceType))
        {
            builder.LoadA(sourceAddress);
            builder.StoreHlA();
            builder.IncrementHl();
            if (IsWordBackedType(sourceType))
            {
                builder.LoadA(HighAddress(sourceAddress));
            }
            else if (sourceType == "i8")
            {
                builder.LoadA(sourceAddress);
                EmitSignExtensionFromA();
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            builder.StoreHlA();
            return;
        }

        EmitWordExpressionToStorage(expression, GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow, targetType);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow);
        builder.StoreHlA();
        builder.IncrementHl();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh);
        builder.StoreHlA();
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
                throw new InvalidOperationException($"Unsupported Game Boy assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitWordAssignment(AssignmentSyntax assignment, ushort address, string targetType)
    {
        switch (assignment.OperatorSymbol)
        {
            case "=":
                EmitWordExpressionToStorage(assignment.Right, address, targetType);
                return;
            case "+=":
                EmitAddWordIntoStorage(address, assignment.Right);
                return;
            case "-=":
                EmitSubtractWordFromStorage(address, assignment.Right);
                return;
            default:
                throw new InvalidOperationException($"Game Boy target does not support 16-bit assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private ushort LValueAddress(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableAddress(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableAddress(GameBoyVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableAddress(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
            _ => throw new InvalidOperationException("Game Boy target only supports assignments to local variables, struct fields, or constant array indices."),
        };
    }

    private string LValueStorageType(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableStorageType(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableStorageType(GameBoyVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableStorageType(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
            _ => throw new InvalidOperationException("Game Boy target only supports assignments to local variables, struct fields, or constant array indices."),
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
}
