namespace RetroSharp.Parser;

public class ExpressionStatementSyntax : StatementSyntax
{
    public ExpressionStatementSyntax(ExpressionSyntax expression)
    {
        Expression = expression;
    }

    public ExpressionSyntax Expression { get; }
    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitExpressionStatement(this);
    }
}