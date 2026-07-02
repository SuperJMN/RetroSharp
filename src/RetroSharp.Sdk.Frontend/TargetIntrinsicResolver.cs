namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public sealed record TargetIntrinsicRuntimeOperand(int Slot, ExpressionSyntax Expression);

public sealed record TargetIntrinsicCompileTimeValue(
    int Slot,
    TargetIntrinsicOperandRole Role,
    string? Identifier,
    int? Constant);

public sealed record ResolvedTargetIntrinsicCall(
    TargetIntrinsicDescriptor Descriptor,
    IReadOnlyList<TargetIntrinsicRuntimeOperand> RuntimeOperands,
    IReadOnlyList<TargetIntrinsicCompileTimeValue> CompileTimeOperands);

public static class TargetIntrinsicResolver
{
    public static TargetIntrinsicDescriptor Resolve(FunctionSyntax function, TargetIntrinsicCatalog catalog)
    {
        var intrinsic = TargetAttributeReader.StringArgument(function, "intrinsic")
                        ?? throw new InvalidOperationException($"Extern function '{function.Name}' must declare an intrinsic attribute.");
        var target = TargetAttributeReader.StringArgument(function, "target");
        if (target is not null && !string.Equals(target, catalog.TargetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Extern intrinsic '{function.Name}' targets '{target}', not {catalog.TargetName}.");
        }

        var descriptor = catalog.Resolve(intrinsic, function.Name);
        var expectedReturnType = SourceReturnType(descriptor.ReturnKind);
        if (!string.Equals(function.Type, expectedReturnType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Extern intrinsic '{function.Name}' declares return type '{function.Type}', but intrinsic '{descriptor.IntrinsicId}' returns '{expectedReturnType}'.");
        }

        return descriptor;
    }

    public static ResolvedTargetIntrinsicCall ResolveCall(
        FunctionSyntax function,
        FunctionCall call,
        TargetIntrinsicCatalog catalog,
        IReadOnlySet<string>? runtimeIdentifiers = null)
    {
        var descriptor = Resolve(function, catalog);
        var args = call.Parameters.ToList();
        if (args.Count != descriptor.Arity)
        {
            throw new InvalidOperationException($"{call.Name} expects {descriptor.Arity} arguments, got {args.Count}.");
        }

        var compileSlots = descriptor.CompileTimeOperands.ToDictionary(operand => operand.Slot);
        var runtimeOperands = new List<TargetIntrinsicRuntimeOperand>(descriptor.RuntimeArity);
        var compileTimeValues = new List<TargetIntrinsicCompileTimeValue>(descriptor.CompileTimeOperands.Count);
        for (var slot = 0; slot < args.Count; slot++)
        {
            if (compileSlots.TryGetValue(slot, out var compileTimeOperand))
            {
                compileTimeValues.Add(ReadCompileTimeOperand(
                    call,
                    descriptor,
                    args[slot],
                    compileTimeOperand,
                    runtimeIdentifiers ?? new HashSet<string>()));
            }
            else
            {
                runtimeOperands.Add(new TargetIntrinsicRuntimeOperand(slot, args[slot]));
            }
        }

        if (runtimeOperands.Count != descriptor.RuntimeArity)
        {
            throw new InvalidOperationException(
                $"Intrinsic '{descriptor.Name}' expects {descriptor.RuntimeArity} runtime operands, got {runtimeOperands.Count}.");
        }

        return new ResolvedTargetIntrinsicCall(descriptor, runtimeOperands, compileTimeValues);
    }

    private static TargetIntrinsicCompileTimeValue ReadCompileTimeOperand(
        FunctionCall call,
        TargetIntrinsicDescriptor descriptor,
        ExpressionSyntax expression,
        TargetIntrinsicCompileTimeOperand operand,
        IReadOnlySet<string> runtimeIdentifiers)
    {
        var context = $"Intrinsic '{descriptor.IntrinsicId}' argument {operand.Slot + 1} on extern '{call.Name}'";
        switch (operand.Role)
        {
            case TargetIntrinsicOperandRole.AssetRef:
                var identifier = SdkCallReader.IdentifierArg(expression, context);
                if (runtimeIdentifiers.Contains(identifier))
                {
                    throw new InvalidOperationException(
                        $"{context} is compile-time {operand.Role} and cannot use runtime local '{identifier}'.");
                }

                return new TargetIntrinsicCompileTimeValue(operand.Slot, operand.Role, identifier, null);
            case TargetIntrinsicOperandRole.WorldId:
                var worldId = ReadWorldId(expression, context);
                if (runtimeIdentifiers.Contains(worldId))
                {
                    throw new InvalidOperationException(
                        $"{context} is compile-time {operand.Role} and cannot use runtime local '{worldId}'.");
                }

                return new TargetIntrinsicCompileTimeValue(operand.Slot, operand.Role, worldId, null);
            case TargetIntrinsicOperandRole.ConstPaletteSlot:
            case TargetIntrinsicOperandRole.EnumFlags:
                return new TargetIntrinsicCompileTimeValue(
                    operand.Slot,
                    operand.Role,
                    null,
                    SdkCallReader.ConstValue(expression, $"{context} compile-time {operand.Role}"));
            default:
                throw new InvalidOperationException($"Unsupported compile-time operand role '{operand.Role}'.");
        }
    }

    private static string ReadWorldId(ExpressionSyntax expression, string context)
    {
        if (expression is IdentifierSyntax identifier)
        {
            return identifier.Identifier;
        }

        if (expression is ConstantSyntax constant)
        {
            var text = constant.Value.ToString() ?? "";
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            {
                return text[1..^1];
            }
        }

        throw new InvalidOperationException($"{context} must be a compile-time world id.");
    }

    private static string SourceReturnType(TargetIntrinsicReturnKind returnKind)
    {
        return returnKind switch
        {
            TargetIntrinsicReturnKind.Void => "void",
            TargetIntrinsicReturnKind.I16 => "i16",
            _ => throw new InvalidOperationException($"Unsupported target intrinsic return kind '{returnKind}'."),
        };
    }
}
