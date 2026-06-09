namespace RetroSharp.Parser;

public class SwitchExpressionArmSyntax
{
    public SwitchExpressionArmSyntax(IReadOnlyList<SwitchCasePatternSyntax> patterns, ExpressionSyntax value)
    {
        Patterns = patterns;
        Value = value;
    }

    public IReadOnlyList<SwitchCasePatternSyntax> Patterns { get; }
    public ExpressionSyntax Value { get; }
}
