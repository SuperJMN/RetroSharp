namespace RetroSharp.Parser;

public class SwitchCasePatternSyntax
{
    public SwitchCasePatternSyntax(ExpressionSyntax value)
        : this(value, Maybe<ExpressionSyntax>.None)
    {
    }

    public SwitchCasePatternSyntax(ExpressionSyntax start, ExpressionSyntax end)
        : this(start, Maybe.From(end))
    {
    }

    private SwitchCasePatternSyntax(ExpressionSyntax start, Maybe<ExpressionSyntax> end)
    {
        Start = start;
        End = end;
    }

    public ExpressionSyntax Start { get; }
    public Maybe<ExpressionSyntax> End { get; }
}
