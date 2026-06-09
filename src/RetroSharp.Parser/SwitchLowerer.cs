using RetroSharp.Core;

namespace RetroSharp.Parser;

public static class SwitchLowerer
{
    public static IfElseSyntax Lower(SwitchSyntax switchSyntax)
    {
        if (switchSyntax.Cases.Count == 0)
        {
            throw new InvalidOperationException("Switch statements require at least one case.");
        }

        var elseBlock = switchSyntax.DefaultBlock;
        foreach (var switchCase in switchSyntax.Cases.Reverse())
        {
            var condition = CaseCondition(switchSyntax.Subject, switchCase);
            elseBlock = Maybe.From(new BlockSyntax([new IfElseSyntax(condition, switchCase.Block, elseBlock)]));
        }

        return (IfElseSyntax)elseBlock.Value.Statements[0];
    }

    private static ExpressionSyntax CaseCondition(ExpressionSyntax subject, SwitchCaseSyntax switchCase)
    {
        return switchCase.Patterns
            .Select(pattern => PatternCondition(subject, pattern))
            .Aggregate((left, right) => new BinaryExpressionSyntax(left, right, Operator.Get("||")));
    }

    private static ExpressionSyntax PatternCondition(ExpressionSyntax subject, SwitchCasePatternSyntax pattern)
    {
        return pattern.End.Match(
            end =>
            {
                var lowerBound = new BinaryExpressionSyntax(subject, pattern.Start, Operator.Get(">="));
                var upperBound = new BinaryExpressionSyntax(subject, end, Operator.Get("<"));
                return (ExpressionSyntax)new BinaryExpressionSyntax(lowerBound, upperBound, Operator.Get("&&"));
            },
            () => new BinaryExpressionSyntax(subject, pattern.Start, Operator.Get("==")));
    }
}
