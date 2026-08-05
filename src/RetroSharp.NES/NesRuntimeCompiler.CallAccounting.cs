using RetroSharp.Parser;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
    /// <summary>
    /// Classifies one call's arguments for future ABI budgeting: compile-time shape/resource
    /// operands are counted separately from runtime bytes, and aggregates (receivers, struct and
    /// class instances) are counted without a byte claim because no NES user-function ABI exists yet
    /// to decide whether they would be copied or addressed.
    /// </summary>
    private NesUserFunctionArguments ClassifyCallArguments(FunctionSyntax function, FunctionCall call)
    {
        IReadOnlyDictionary<string, ExpressionSyntax> bound;
        try
        {
            bound = ParameterSubstitution.BindParameters(function, call, "NES");
        }
        catch (InvalidOperationException)
        {
            // Emission binds the same arguments and owns the diagnostic; accounting stays silent.
            return NesUserFunctionArguments.None;
        }

        var runtimeBytes = 0;
        var referenceArguments = 0;
        var compileTimeOperands = 0;
        foreach (var parameter in function.Parameters)
        {
            if (!bound.TryGetValue(parameter.Name, out var argument))
            {
                continue;
            }

            if (IsAggregateParameter(parameter))
            {
                referenceArguments++;
                continue;
            }

            if (IsCompileTimeArgument(argument))
            {
                compileTimeOperands++;
                continue;
            }

            runtimeBytes += ArgumentStorageSize(parameter.Type);
        }

        return new NesUserFunctionArguments(runtimeBytes, referenceArguments, compileTimeOperands);
    }

    private bool IsAggregateParameter(ParameterSyntax parameter) =>
        parameter.IsReceiver ||
        !(IsByteBackedType(parameter.Type) || IsPointerType(parameter.Type) || program.Enums.ContainsKey(parameter.Type));

    private bool IsCompileTimeArgument(ExpressionSyntax argument)
    {
        if (TryConst(argument, out _))
        {
            return true;
        }

        // A bare identifier that has no runtime storage is a shape/resource operand (an asset or
        // sprite handle), not a value the future ABI would have to marshal.
        return argument is IdentifierSyntax identifier && !HasRuntimeStorage(identifier.Identifier);
    }

    private bool HasRuntimeStorage(string name) => variableTypes.ContainsKey(ScopedVariableName(name));

    private static bool IsPointerType(string type) =>
        type.StartsWith("ptr<", StringComparison.Ordinal) && type.EndsWith('>');

    private static int ArgumentStorageSize(string type) => IsPointerType(type) || IsWordBackedType(type) ? 2 : 1;
}
