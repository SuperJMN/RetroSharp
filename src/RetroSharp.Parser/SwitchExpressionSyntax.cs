using RetroSharp.Core;

namespace RetroSharp.Parser;

public class SwitchExpressionSyntax : ExpressionSyntax
{
    public SwitchExpressionSyntax(ExpressionSyntax subject, IReadOnlyList<SwitchExpressionArmSyntax> arms, Maybe<ExpressionSyntax> defaultValue)
    {
        Subject = subject;
        Arms = arms;
        DefaultValue = defaultValue;
    }

    public ExpressionSyntax Subject { get; }
    public IReadOnlyList<SwitchExpressionArmSyntax> Arms { get; }
    public Maybe<ExpressionSyntax> DefaultValue { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitSwitchExpression(this);
    }
}
