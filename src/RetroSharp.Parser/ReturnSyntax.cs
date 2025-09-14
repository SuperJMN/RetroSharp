namespace RetroSharp.Parser;

public class ReturnSyntax : StatementSyntax
{
    public Maybe<ExpressionSyntax> Expression { get; }

    public ReturnSyntax(Maybe<ExpressionSyntax> expression)
    {
        Expression = expression;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitReturn(this);
    }
}