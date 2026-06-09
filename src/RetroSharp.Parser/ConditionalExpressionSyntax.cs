namespace RetroSharp.Parser;

public class ConditionalExpressionSyntax : ExpressionSyntax
{
    public ConditionalExpressionSyntax(ExpressionSyntax condition, ExpressionSyntax whenTrue, ExpressionSyntax whenFalse)
    {
        Condition = condition;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    public ExpressionSyntax Condition { get; }
    public ExpressionSyntax WhenTrue { get; }
    public ExpressionSyntax WhenFalse { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitConditionalExpression(this);
    }
}
