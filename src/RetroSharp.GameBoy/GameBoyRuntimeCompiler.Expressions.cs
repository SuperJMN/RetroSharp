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
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out var indexedBase, out var fieldName))
                {
                    EmitRuntimeIndexedMemberAddressToHl(indexedBase, fieldName);
                    builder.LoadAFromHl();
                }
                else
                {
                    builder.LoadA(VariableAddress(GameBoyVideoProgram.MemberAccessName(memberAccess)));
                }

                break;
            case IndexExpressionSyntax indexExpression:
                if (TryConst(indexExpression.Index, out _))
                {
                    builder.LoadA(VariableAddress(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index")));
                }
                else
                {
                    EmitRuntimeIndexedAddressToHl(indexExpression.BaseIdentifier, indexExpression.Index);
                    builder.LoadAFromHl();
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
                throw new InvalidOperationException($"Unsupported Game Boy expression '{expression.GetType().Name}'.");
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

    private void EmitValueCallToA(FunctionCall call)
    {
        switch (call.Name)
        {
            case "map_tile_at":
                sdkOperationLowerer.EmitMapTileAt(call);
                break;
            case "map_flags_at":
                sdkOperationLowerer.EmitMapFlagsAt(call);
                break;
            case "collision_aabb_tiles":
                sdkOperationLowerer.EmitCollisionAabbTiles(call);
                break;
            case "__rs_actor_camera_x_lo":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.XLow);
                break;
            case "__rs_actor_camera_x_hi":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.XHigh);
                break;
            case "__rs_actor_camera_y_lo":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.YLow);
                break;
            case "__rs_actor_camera_y_hi":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(GameBoyRuntimeMemoryLayout.Camera.YHigh);
                break;
            case "camera_tile_column_at":
                sdkOperationLowerer.EmitCameraTileColumnAt(call);
                break;
            case "camera_span_tile_at":
                sdkOperationLowerer.EmitCameraSpanTileAt(call);
                break;
            case "camera_span_has_tile":
                sdkOperationLowerer.EmitCameraSpanHasTile(call);
                break;
            case "camera_span_has_flags":
                sdkOperationLowerer.EmitCameraSpanHasFlags(call);
                break;
            default:
                if (TryEmitTargetValueIntrinsic(call))
                {
                    break;
                }

                if (TryEmitUserValueFunction(call))
                {
                    break;
                }

                throw new InvalidOperationException($"Unsupported Game Boy value API call '{call.Name}'.");
        }
    }

    private bool TryEmitTargetValueIntrinsic(FunctionCall call, bool preserveWordReturn = false)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics);
        if (intrinsic.IsPluginOperation)
        {
            return false;
        }

        var completeWordReturn = false;
        switch (intrinsic.Operation)
        {
            case TargetIntrinsicOperation.ReadWorldTileFlags:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(ConsumeSdkOperation<Sdk2DOperation.ReadWorldTileFlags>(call.Name));
                break;
            case TargetIntrinsicOperation.CameraAabbTiles:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(ConsumeSdkOperation<Sdk2DOperation.CameraAabbTiles>(call.Name));
                break;
            case TargetIntrinsicOperation.CameraAabbHitTop:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(ConsumeSdkOperation<Sdk2DOperation.CameraAabbHitTop>(call.Name));
                completeWordReturn = true;
                break;
            case TargetIntrinsicOperation.CameraScreenAabbTiles:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(ConsumeSdkOperation<Sdk2DOperation.CameraScreenAabbTiles>(call.Name));
                break;
            case TargetIntrinsicOperation.CameraScreenAabbHitTop:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(ConsumeSdkOperation<Sdk2DOperation.CameraScreenAabbHitTop>(call.Name));
                break;
            case TargetIntrinsicOperation.CameraVerticalScrollMax:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                sdkOperationLowerer.EmitCameraVerticalScrollMax();
                break;
            case TargetIntrinsicOperation.ButtonDown:
                sdkOperationLowerer.EmitButtonDown(call);
                break;
            case TargetIntrinsicOperation.ButtonJustPressed:
                sdkOperationLowerer.EmitButtonJustPressed(call);
                break;
            case TargetIntrinsicOperation.ButtonJustReleased:
                sdkOperationLowerer.EmitButtonJustReleased(call);
                break;
            case TargetIntrinsicOperation.ButtonHoldTicks:
                sdkOperationLowerer.EmitButtonHoldTicks(call);
                break;
            case TargetIntrinsicOperation.ReadSpriteWidth:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, program.TargetIntrinsics);
                sdkOperationLowerer.EmitSpriteWidth(call);
                break;
            case TargetIntrinsicOperation.ReadAnimationFrame:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, program.TargetIntrinsics);
                sdkOperationLowerer.EmitAnimationFrame(call);
                break;
            default:
                return false;
        }

        if (intrinsic.ReturnKind == TargetIntrinsicReturnKind.I16)
        {
            if (!completeWordReturn)
            {
                builder.LoadLFromA();
                builder.LoadHImmediate(0);
            }

            if (!preserveWordReturn)
            {
                builder.LoadAFromL();
            }
        }

        return true;
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

                EmitVariableOperandsToAAndB(binary.Left, binary.Right);
                builder.SubtractB();
                return;
            case "&":
            case "|":
            case "^":
                EmitBitwiseBinaryExpressionToA(binary);
                return;
        }

        throw new InvalidOperationException($"Unsupported Game Boy binary expression '{binary.Operator.Symbol}'.");
    }

    private void EmitBitwiseBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        if (TryConst(binary.Right, out var rightConstant))
        {
            EmitExpressionToA(binary.Left);
            EmitBitwiseImmediate(binary.Operator.Symbol, rightConstant);
            return;
        }

        if (TryConst(binary.Left, out var leftConstant))
        {
            EmitExpressionToA(binary.Right);
            EmitBitwiseImmediate(binary.Operator.Symbol, leftConstant);
            return;
        }

        EmitExpressionToA(binary.Right);
        builder.LoadBFromA();
        EmitExpressionToA(binary.Left);
        EmitBitwiseAFromB(binary.Operator.Symbol);
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
                throw new InvalidOperationException($"Unsupported Game Boy bitwise operator '{op}'.");
        }
    }

    private void EmitBitwiseAFromB(string op)
    {
        switch (op)
        {
            case "&":
                builder.AndAFromB();
                return;
            case "|":
                builder.OrAFromB();
                return;
            case "^":
                builder.XorAFromB();
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy bitwise operator '{op}'.");
        }
    }

    private void EmitBitwiseAFromC(string op)
    {
        switch (op)
        {
            case "&":
                builder.AndAFromC();
                return;
            case "|":
                builder.OrAFromC();
                return;
            case "^":
                builder.XorAFromC();
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy bitwise operator '{op}'.");
        }
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

    private void EmitRuntimeIndexedAddressToHl(string baseIdentifier, ExpressionSyntax index)
    {
        var baseAddress = VariableAddress(IndexedElementName(baseIdentifier, 0));
        var elementSize = StorageSize(VariableStorageType(IndexedElementName(baseIdentifier, 0)));
        EmitExpressionToA(index);
        EmitMultiplyA(elementSize);
        builder.LoadHl(baseAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
    }

    private void EmitRuntimeIndexedMemberAddressToHl(IndexExpressionSyntax indexExpression, string fieldName)
    {
        var layout = StructArrayLayoutFor(indexExpression.BaseIdentifier);
        _ = layout.FieldOffsets[fieldName];
        var baseAddress = VariableAddress(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
        EmitExpressionToA(indexExpression.Index);
        EmitMultiplyA(layout.Stride);
        builder.LoadHl(baseAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
    }

    private void EmitRuntimeIndexedMemberAddressToHl(string baseIdentifier, SdkByteExpression index, string fieldName)
    {
        var layout = StructArrayLayoutFor(baseIdentifier);
        _ = layout.FieldOffsets[fieldName];
        var baseAddress = VariableAddress(IndexedMemberName(baseIdentifier, 0, fieldName));
        EmitSdkByteExpressionToA(index);
        EmitMultiplyA(layout.Stride);
        builder.LoadHl(baseAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
    }

    private void EmitMultiplyA(int multiplier)
    {
        if (multiplier <= 1)
        {
            return;
        }

        builder.LoadBFromA();
        for (var count = 1; count < multiplier; count++)
        {
            builder.AddAFromB();
        }
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
            : throw new InvalidOperationException($"Game Boy target has no struct array layout for '{baseIdentifier}'.");
    }

    private void RequireExpressionPreservesHl(ExpressionSyntax expression, string context)
    {
        if (!PreservesHl(expression))
        {
            throw new InvalidOperationException($"Game Boy target cannot use expression '{expression.GetType().Name}' as the right side of a {context} yet because it also needs HL for array addressing.");
        }
    }

    private bool PreservesHl(ExpressionSyntax expression)
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
            CastSyntax cast => PreservesHl(cast.Expression),
            ConditionalExpressionSyntax conditional => PreservesHl(conditional.Condition) && PreservesHl(conditional.WhenTrue) && PreservesHl(conditional.WhenFalse),
            BinaryExpressionSyntax binary => PreservesHl(binary.Left) && PreservesHl(binary.Right),
            _ => false,
        };
    }

    private int ConstRuntimeValue(ExpressionSyntax expression, string context)
    {
        if (expression is CastSyntax cast)
        {
            RequireSupportedCastTarget(cast);
            return ConstRuntimeValue(cast.Expression, context);
        }

        if (TrySpriteWidth(expression, out var spriteWidth))
        {
            return spriteWidth;
        }

        return GameBoyVideoProgram.ConstValue(expression, context);
    }

    private bool TrySpriteWidth(ExpressionSyntax expression, out int width)
    {
        width = 0;
        if (expression is CastSyntax cast)
        {
            return TrySpriteWidth(cast.Expression, out width);
        }

        if (expression is not FunctionCall call)
        {
            return false;
        }

        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            if (TargetAttributeReader.StringArgument(function, "intrinsic") is null)
            {
                return false;
            }

            var intrinsic = TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics);
            if (intrinsic.IsPluginOperation || intrinsic.Operation != TargetIntrinsicOperation.ReadSpriteWidth)
            {
                return false;
            }

            width = SpriteWidth(call);
            return true;
        }

        if (function.Block.Statements is not [ReturnSyntax { Expression.HasValue: true }])
        {
            return false;
        }

        var returned = ParameterSubstitution.SubstituteReturnExpression(function, call, "Game Boy");
        return TrySpriteWidth(returned, out width);
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
                throw new InvalidOperationException($"Game Boy target struct array field type '{field.Type}' is not scalar.");
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
        var value = CheckedRange(GameBoyVideoProgram.ConstValue(index, context), 0, 255, context);
        return IndexedElementName(baseIdentifier, value);
    }

    private int SpriteWidth(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var assetName = GameBoyVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sprite_width argument 1");
        return SpriteWidth(assetName);
    }

    private int SpriteWidth(string assetName)
    {
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        return asset.LogicalWidth;
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

    private ushort VariableAddress(string name)
    {
        var scopedName = ScopedVariableName(name);
        if (!variables.TryGetValue(scopedName, out var address))
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

    private readonly record struct LoopTarget(string BreakLabel, string ContinueLabel);
}
