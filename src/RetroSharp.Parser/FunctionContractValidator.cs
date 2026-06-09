namespace RetroSharp.Parser;

public static class FunctionContractValidator
{
    public static IEnumerable<string> ValidateProgram(ProgramSyntax program)
    {
        return program.Functions.SelectMany(Validate);
    }

    public static IEnumerable<string> Validate(FunctionSyntax function)
    {
        if (!function.IsPure)
        {
            return [];
        }

        if (function.Block.Statements is not [ReturnSyntax { Expression.HasValue: true } returnSyntax])
        {
            return [$"pure helper '{function.Name}' contains side-effecting statements; pure helpers must be a single return expression."];
        }

        return IsPureExpression(returnSyntax.Expression.Value)
            ? []
            : [$"pure helper '{function.Name}' return expression contains side-effecting operations."];
    }

    public static void RequireValueInlineSubstitution(FunctionSyntax function, string targetName)
    {
        if (!function.IsInline)
        {
            return;
        }

        if (function.Block.Statements is [ReturnSyntax { Expression.HasValue: true }])
        {
            return;
        }

        throw new InvalidOperationException(
            $"{targetName} target cannot inline helper '{function.Name}' as a value because inline value helpers must be exactly one return expression.");
    }

    private static bool IsPureExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            ConstantSyntax => true,
            IdentifierSyntax => true,
            SizeOfSyntax => true,
            OffsetOfSyntax => true,
            CountOfSyntax => true,
            BinaryExpressionSyntax binary => IsPureExpression(binary.Left) && IsPureExpression(binary.Right),
            ConditionalExpressionSyntax conditional =>
                IsPureExpression(conditional.Condition) &&
                IsPureExpression(conditional.WhenTrue) &&
                IsPureExpression(conditional.WhenFalse),
            SwitchExpressionSyntax switchExpression =>
                IsPureExpression(switchExpression.Subject) &&
                switchExpression.Arms.All(IsPureSwitchArm) &&
                switchExpression.DefaultValue.Match(IsPureExpression, () => false),
            PipelineExpressionSyntax pipeline => IsPureExpression(PipelineExpressionLowerer.Lower(pipeline)),
            UnaryExpressionSyntax unary => IsPureExpression(unary.Operand),
            CastSyntax cast => IsPureExpression(cast.Expression),
            MemberAccessSyntax memberAccess => IsPureExpression(memberAccess.Target),
            IndexExpressionSyntax indexExpression => IsPureExpression(indexExpression.Index),
            NamedArgumentSyntax namedArgument => IsPureExpression(namedArgument.Expression),
            _ => false,
        };
    }

    private static bool IsPureSwitchArm(SwitchExpressionArmSyntax arm)
    {
        return arm.Patterns.All(IsPureSwitchPattern) && IsPureExpression(arm.Value);
    }

    private static bool IsPureSwitchPattern(SwitchCasePatternSyntax pattern)
    {
        return IsPureExpression(pattern.Start) && pattern.End.Match(IsPureExpression, () => true);
    }
}
