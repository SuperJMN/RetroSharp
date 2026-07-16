using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
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

        if (!IsScalarLocalType(declaration.Type))
        {
            throw new InvalidOperationException($"NES target only supports scalar fixed-size arrays; '{declaration.Type}' is not supported yet.");
        }

        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(NesVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var elementAddresses = new List<byte>();
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

        var length = CheckedRange(NesVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var fieldOffsets = StructFieldOffsets(structSyntax);
        var stride = StructStride(structSyntax);
        if (length * stride > 255)
        {
            throw new InvalidOperationException($"NES target struct array '{declaration.Name}' uses {length * stride} byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.");
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

                var storageName = IndexedMemberName(declaration.Name, index, fieldName);
                var address = VariableAddress(storageName);
                EmitExpressionToStorage(expression, address, VariableStorageType(storageName));
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

        var fieldAddresses = new Dictionary<string, byte>(StringComparer.Ordinal);
        var fieldNames = new List<string>();
        foreach (var field in structSyntax.Fields)
        {
            if (!IsScalarLocalType(field.Type))
            {
                throw new InvalidOperationException($"NES target does not support struct field type '{field.Type}' yet.");
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

            var storageName = $"{declaration.Name}.{fieldName}";
            EmitExpressionToStorage(expression, fieldAddresses[fieldName], VariableStorageType(storageName));
        }
    }

    private byte DeclareVariable(string name, string type)
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
        NesRuntimeMemoryLayout.ValidateUserLocalBytes(
            nextVariableAddress - NesRuntimeMemoryLayout.UserLocals.Start + size);

        var address = nextVariableAddress;
        nextVariableAddress = (byte)(nextVariableAddress + size);
        variables.Add(name, address);
        variableTypes.Add(name, type);
        return address;
    }

    private void EmitZeroToStorage(byte address, string type)
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(address);
        if (IsWordBackedType(type))
        {
            builder.StoreAZeroPage(HighAddress(address));
        }
    }

    private void EmitExpressionToStorage(ExpressionSyntax expression, byte address, string type)
    {
        if (IsWordBackedType(type))
        {
            EmitWordExpressionToStorage(expression, address, type);
            return;
        }

        ValidateWorldHitTopNarrowing(expression, type);
        EmitExpressionToA(expression);
        builder.StoreAZeroPage(address);
    }

    private void EmitWordExpressionToStorage(ExpressionSyntax expression, byte address, string targetType)
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
                builder.StoreAZeroPage(address);
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
                EmitRuntimeMemberIndexToX(indexedBase);
                EmitRuntimeStorageFromZeroPageXToWordStorage(RuntimeIndexedMemberBaseAddress(indexedBase, fieldName), VariableStorageType(IndexedMemberName(indexedBase.BaseIdentifier, 0, fieldName)), address);
                return;
            case IndexExpressionSyntax indexExpression:
                EmitRuntimeIndexToX(indexExpression.BaseIdentifier, indexExpression.Index);
                EmitRuntimeStorageFromZeroPageXToWordStorage(ArrayBaseAddress(indexExpression.BaseIdentifier), VariableStorageType(IndexedElementName(indexExpression.BaseIdentifier, 0)), address);
                return;
            case FunctionCall call:
                if (TryEmitWordValueFunctionToStorage(call, address, targetType))
                {
                    return;
                }

                EmitValueCallToA(call);
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
                return;
            case ConditionalExpressionSyntax conditional:
                EmitWordConditionalExpressionToStorage(conditional, address, targetType);
                return;
            case UnaryExpressionSyntax unary when IsBooleanValueExpression(unary):
                EmitBooleanExpressionToA(unary);
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
                return;
            case BinaryExpressionSyntax binary when IsBooleanValueExpression(binary):
                EmitBooleanExpressionToA(binary);
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
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
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
                return;
        }
    }

    private void EmitRuntimeStorageFromZeroPageXToWordStorage(byte baseAddress, string sourceType, byte targetAddress)
    {
        builder.LoadAZeroPageX(baseAddress);
        builder.StoreAZeroPage(targetAddress);
        if (IsWordBackedType(sourceType))
        {
            builder.LoadAZeroPageX(HighAddress(baseAddress));
        }
        else if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(HighAddress(targetAddress));
    }

    private void EmitWordConditionalExpressionToStorage(ConditionalExpressionSyntax conditional, byte address, string targetType)
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

    private void EmitStoreWordImmediate(byte address, int value)
    {
        builder.LoadAImmediate(value & 0xFF);
        builder.StoreAZeroPage(address);
        builder.LoadAImmediate((value >> 8) & 0xFF);
        builder.StoreAZeroPage(HighAddress(address));
    }

    private void EmitCopyToWordStorage(byte sourceAddress, string sourceType, byte targetAddress)
    {
        builder.LoadAZeroPage(sourceAddress);
        builder.StoreAZeroPage(targetAddress);

        if (IsWordBackedType(sourceType))
        {
            builder.LoadAZeroPage(HighAddress(sourceAddress));
        }
        else if (sourceType == "i8")
        {
            builder.LoadAZeroPage(sourceAddress);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(HighAddress(targetAddress));
    }

    private void EmitAddWordIntoStorage(byte address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadAZeroPage(address);
            builder.ClearCarry();
            builder.AddImmediate(constant & 0xFF);
            builder.StoreAZeroPage(address);
            builder.LoadAZeroPage(HighAddress(address));
            builder.AddImmediate((constant >> 8) & 0xFF);
            builder.StoreAZeroPage(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, NesRuntimeMemoryLayout.Runtime.IndexScratch, WordExpressionType(right));
            rightAddress = NesRuntimeMemoryLayout.Runtime.IndexScratch;
            rightType = "u16";
        }

        // Sign-extending an i8 addend clobbers the carry flag, so its high byte must be materialized
        // to scratch before the low-byte add. Wider operands load the high byte with carry-safe loads
        // after the low-byte add, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(address);
        builder.ClearCarry();
        builder.AddZeroPage(rightAddress);
        builder.StoreAZeroPage(address);

        if (!hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(HighAddress(address));
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.StoreAZeroPage(HighAddress(address));
    }

    private void EmitSubtractWordFromStorage(byte address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadAZeroPage(address);
            builder.SetCarry();
            builder.SubtractImmediate(constant & 0xFF);
            builder.StoreAZeroPage(address);
            builder.LoadAZeroPage(HighAddress(address));
            builder.SubtractImmediate((constant >> 8) & 0xFF);
            builder.StoreAZeroPage(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, NesRuntimeMemoryLayout.Runtime.IndexScratch, WordExpressionType(right));
            rightAddress = NesRuntimeMemoryLayout.Runtime.IndexScratch;
            rightType = "u16";
        }

        // Sign-extending an i8 operand clobbers the carry/borrow flag, so its high byte must be
        // materialized to scratch before the low-byte subtract. Wider operands load the high byte with
        // carry-safe loads after the low-byte subtract, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(address);
        builder.SetCarry();
        builder.SubtractZeroPage(rightAddress);
        builder.StoreAZeroPage(address);

        if (!hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(HighAddress(address));
        builder.SubtractZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.StoreAZeroPage(HighAddress(address));
    }

    private void EmitHighByteToScratch(byte address, string type)
    {
        if (IsWordBackedType(type))
        {
            builder.LoadAZeroPage(HighAddress(address));
        }
        else if (type == "i8")
        {
            builder.LoadAZeroPage(address);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
    }

    private void EmitHighByteFromLowAToStorage(byte highAddress, string sourceType)
    {
        if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(highAddress);
    }

    private void EmitSignExtensionFromA()
    {
        var negativeLabel = builder.CreateLabel("sign_extend_negative");
        var endLabel = builder.CreateLabel("sign_extend_end");

        builder.AndImmediate(0x80);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, negativeLabel); // BNE negativeLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(negativeLabel);
        builder.LoadAImmediate(0xFF);
        builder.Label(endLabel);
    }

    private static byte HighAddress(byte lowAddress) => (byte)(lowAddress + 1);

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
                signedByteLocations.Contains(ScopedVariableName(NesVideoProgram.MemberAccessName(memberAccess))),
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

    internal IReadOnlyList<NesRuntimeUserVariable> UserVariables => variables
        .Select(variable => new NesRuntimeUserVariable(
            variable.Key,
            variableTypes[variable.Key],
            variable.Value,
            StorageSize(variableTypes[variable.Key])))
        .OrderBy(variable => variable.Address)
        .ThenBy(variable => variable.Name, StringComparer.Ordinal)
        .ToArray();

    private string VariableStorageType(string name)
    {
        var scopedName = ScopedVariableName(name);
        if (!variableTypes.TryGetValue(scopedName, out var type))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return type;
    }

    private bool TryDirectStorageExpression(ExpressionSyntax expression, out byte address, out string type)
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

                var memberName = NesVideoProgram.MemberAccessName(memberAccess);
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
                type = VariableStorageType(NesVideoProgram.MemberAccessName(memberAccess));
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
        var targetType = LValueStorageType(assignment.Left);
        if (IsWordBackedType(targetType))
        {
            EmitWordAssignment(assignment, address, targetType);
            return;
        }

        ValidateWorldHitTopNarrowing(assignment.Right, targetType);
        EmitAssignmentRightToA(assignment);
        builder.StoreAZeroPage(address);
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
        var baseAddress = ArrayBaseAddress(indexLValue.BaseIdentifier);
        EmitRuntimeIndexToX(indexLValue.BaseIdentifier, indexLValue.Index);
        var elementType = VariableStorageType(IndexedElementName(indexLValue.BaseIdentifier, 0));
        if (IsWordBackedType(elementType))
        {
            EmitRuntimeIndexedWordAssignment(baseAddress, assignment, elementType, "runtime indexed assignment");
            return;
        }

        ValidateWorldHitTopNarrowing(assignment.Right, elementType);
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
        var fieldType = VariableStorageType(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
        if (IsWordBackedType(fieldType))
        {
            EmitRuntimeIndexedWordAssignment(baseAddress, assignment, fieldType, "runtime indexed struct field assignment");
            return;
        }

        ValidateWorldHitTopNarrowing(assignment.Right, fieldType);
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

    private void EmitRuntimeIndexedWordAssignment(byte baseAddress, AssignmentSyntax assignment, string targetType, string context)
    {
        if (assignment.OperatorSymbol != "=")
        {
            throw new InvalidOperationException($"NES target only supports direct '=' for 16-bit {context}.");
        }

        RequireExpressionPreservesX(assignment.Right, context);
        EmitWordExpressionToZeroPageX(baseAddress, assignment.Right, targetType);
    }

    private void EmitWordExpressionToZeroPageX(byte baseAddress, ExpressionSyntax expression, string targetType)
    {
        if (TryConst(expression, out var constant))
        {
            builder.LoadAImmediate(constant & 0xFF);
            builder.StoreAZeroPageX(baseAddress);
            builder.LoadAImmediate((constant >> 8) & 0xFF);
            builder.StoreAZeroPageX(HighAddress(baseAddress));
            return;
        }

        if (TryDirectStorageExpression(expression, out var sourceAddress, out var sourceType))
        {
            builder.LoadAZeroPage(sourceAddress);
            builder.StoreAZeroPageX(baseAddress);
            if (IsWordBackedType(sourceType))
            {
                builder.LoadAZeroPage(HighAddress(sourceAddress));
            }
            else if (sourceType == "i8")
            {
                builder.LoadAZeroPage(sourceAddress);
                EmitSignExtensionFromA();
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            builder.StoreAZeroPageX(HighAddress(baseAddress));
            return;
        }

        EmitWordExpressionToStorage(expression, NesRuntimeMemoryLayout.Runtime.IndexScratch, targetType);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.StoreAZeroPageX(baseAddress);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.StoreAZeroPageX(HighAddress(baseAddress));
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

    private void EmitWordAssignment(AssignmentSyntax assignment, byte address, string targetType)
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
                throw new InvalidOperationException($"NES target does not support 16-bit assignment operator '{assignment.OperatorSymbol}'.");
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

    private string LValueStorageType(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableStorageType(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableStorageType(NesVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableStorageType(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
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

}
