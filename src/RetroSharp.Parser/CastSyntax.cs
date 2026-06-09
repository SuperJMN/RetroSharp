namespace RetroSharp.Parser;

public class CastSyntax : ExpressionSyntax
{
    public CastSyntax(string type, ExpressionSyntax expression)
    {
        Type = type;
        Expression = expression;
    }

    public string Type { get; }
    public ExpressionSyntax Expression { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitCast(this);
    }
}
