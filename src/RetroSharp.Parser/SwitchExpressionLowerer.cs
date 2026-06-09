namespace RetroSharp.Parser;

public static class SwitchExpressionLowerer
{
    public static ExpressionSyntax Lower(SwitchExpressionSyntax switchExpression)
    {
        var errors = SwitchExpressionValidator.Validate(switchExpression).ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(errors[0]);
        }

        var result = switchExpression.DefaultValue.Value;
        foreach (var arm in switchExpression.Arms.Reverse())
        {
            result = new ConditionalExpressionSyntax(
                SwitchPatternConditions.CaseCondition(switchExpression.Subject, arm.Patterns),
                arm.Value,
                result);
        }

        return result;
    }
}
