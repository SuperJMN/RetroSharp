using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
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
                    EmitRuntimeIndexToX(indexExpression.BaseIdentifier, indexExpression.Index);
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
                builder.SubtractZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
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

    private void EmitRuntimeIndexToX(string baseIdentifier, ExpressionSyntax index)
    {
        var elementSize = StorageSize(VariableStorageType(IndexedElementName(baseIdentifier, 0)));
        EmitExpressionToA(index);
        EmitMultiplyA(elementSize);
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
        sdkOperationLowerer.EmitSdkByteExpressionToA(index);
        EmitMultiplyA(layout.Stride);
        builder.TransferAToX();
    }

    private void EmitMultiplyA(int multiplier)
    {
        if (multiplier <= 1)
        {
            return;
        }

        var highestBit = 0;
        for (var remaining = multiplier; remaining > 1; remaining >>= 1)
        {
            highestBit++;
        }

        if ((multiplier & (multiplier - 1)) == 0)
        {
            for (var bit = 0; bit < highestBit; bit++)
            {
                builder.ShiftLeftA();
            }

            return;
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        for (var bit = highestBit - 1; bit >= 0; bit--)
        {
            builder.ShiftLeftA();
            if ((multiplier & (1 << bit)) != 0)
            {
                builder.ClearCarry();
                builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
            }
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
            && structArrays.ContainsKey(ScopedVariableName(candidate.BaseIdentifier))
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
        var scopedBaseIdentifier = ScopedVariableName(baseIdentifier);
        return structArrays.TryGetValue(scopedBaseIdentifier, out var layout)
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

    private Dictionary<string, int> StructFieldOffsets(StructSyntax structSyntax)
    {
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var offset = 0;
        foreach (var field in structSyntax.Fields)
        {
            if (!IsScalarLocalType(field.Type))
            {
                throw new InvalidOperationException($"NES target struct array field type '{field.Type}' is not scalar.");
            }

            offsets.Add(field.Name, offset);
            offset += StorageSize(field.Type);
        }

        return offsets;
    }

    private int StructStride(StructSyntax structSyntax)
    {
        return structSyntax.Fields.Sum(field => StorageSize(field.Type));
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
        var scopedName = ScopedVariableName(name);
        if (!variables.TryGetValue(scopedName, out var address))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return address;
    }

    private sealed class InlineVariableScope
    {
        public InlineVariableScope(string prefix)
        {
            Prefix = prefix;
        }

        public string Prefix { get; }
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct LoopTarget(string BreakLabel, string ContinueLabel);}
