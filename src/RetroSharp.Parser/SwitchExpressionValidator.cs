namespace RetroSharp.Parser;

public static class SwitchExpressionValidator
{
    public static IEnumerable<string> Validate(SwitchExpressionSyntax switchExpression)
    {
        if (!IsSimpleSubject(switchExpression.Subject))
        {
            yield return "switch expression subject must be a simple value expression so lowering cannot re-evaluate a call or side effect.";
        }

        if (!switchExpression.DefaultValue.HasValue)
        {
            yield return "switch expression requires a default arm";
            yield break;
        }

        var branchShapes = switchExpression.Arms
            .Select(arm => BranchShape(arm.Value))
            .Append(BranchShape(switchExpression.DefaultValue.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (branchShapes.Count > 1)
        {
            yield return "switch expression branch results must be compatible.";
        }
    }

    private static bool IsSimpleSubject(ExpressionSyntax expression)
    {
        return expression switch
        {
            ConstantSyntax => true,
            IdentifierSyntax => true,
            MemberAccessSyntax memberAccess => IsSimpleSubject(memberAccess.Target),
            IndexExpressionSyntax indexExpression => IsSimpleSubject(indexExpression.Index),
            SizeOfSyntax => true,
            OffsetOfSyntax => true,
            CountOfSyntax => true,
            UnaryExpressionSyntax unary => IsSimpleSubject(unary.Operand),
            CastSyntax cast => IsSimpleSubject(cast.Expression),
            BinaryExpressionSyntax binary => IsSimpleSubject(binary.Left) && IsSimpleSubject(binary.Right),
            ConditionalExpressionSyntax conditional =>
                IsSimpleSubject(conditional.Condition) &&
                IsSimpleSubject(conditional.WhenTrue) &&
                IsSimpleSubject(conditional.WhenFalse),
            SwitchExpressionSyntax nested => Validate(nested).Any() == false,
            _ => false,
        };
    }

    private static string BranchShape(ExpressionSyntax expression)
    {
        return expression switch
        {
            ConstantSyntax { Value: "true" or "false" } => "bool",
            IdentifierSyntax { Identifier: "true" or "false" } => "bool",
            BinaryExpressionSyntax { Operator.Symbol: "==" or "!=" or "<" or "<=" or ">" or ">=" or "&&" or "||" } => "bool",
            UnaryExpressionSyntax { OperatorSymbol: "!" } => "bool",
            ConditionalExpressionSyntax conditional => CompatibleBranchShape(
                BranchShape(conditional.WhenTrue),
                BranchShape(conditional.WhenFalse)),
            SwitchExpressionSyntax nested when nested.DefaultValue.HasValue => CompatibleBranchShape(
                nested.Arms.Select(arm => BranchShape(arm.Value)).Append(BranchShape(nested.DefaultValue.Value)).Distinct(StringComparer.Ordinal).ToList()),
            _ => "scalar",
        };
    }

    private static string CompatibleBranchShape(string left, string right)
    {
        return left == right ? left : "incompatible";
    }

    private static string CompatibleBranchShape(IReadOnlyList<string> shapes)
    {
        return shapes.Count == 1 ? shapes[0] : "incompatible";
    }
}
