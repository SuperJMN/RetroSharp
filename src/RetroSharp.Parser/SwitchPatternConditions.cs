using RetroSharp.Core;

namespace RetroSharp.Parser;

public static class SwitchPatternConditions
{
    public static ExpressionSyntax CaseCondition(ExpressionSyntax subject, IEnumerable<SwitchCasePatternSyntax> patterns)
    {
        return patterns
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
