namespace RetroSharp.Parser;

public class PointerDerefLValue : LValue
{
    public PointerDerefLValue(ExpressionSyntax expression)
    {
        Expression = expression;
    }

    public ExpressionSyntax Expression { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitPointerDerefLValue(this);
    }
}

