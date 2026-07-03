namespace RetroSharp.Parser;

public static class ReceiverMethodLowerer
{
    public static bool TryLower(QualifiedCallSyntax call, IEnumerable<FunctionSyntax> functions, out FunctionCall lowered)
    {
        return TryLower(call, functions, out lowered, out _);
    }

    public static bool TryLower(QualifiedCallSyntax call, IEnumerable<FunctionSyntax> functions, out FunctionCall lowered, out FunctionSyntax function)
    {
        var matchingFunction = functions.FirstOrDefault(candidate =>
            candidate.Name == call.Method &&
            candidate.Parameters.Count > 0 &&
            candidate.Parameters[0].IsReceiver);

        if (matchingFunction is null)
        {
            function = null!;
            lowered = new FunctionCall(call.Method, []);
            return false;
        }

        function = matchingFunction;
        lowered = new FunctionCall(
            function.Name,
            new ExpressionSyntax[] { new IdentifierSyntax(call.Qualifier) }.Concat(call.Parameters));
        return true;
    }
}
