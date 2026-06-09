namespace RetroSharp.Parser;

public class IndexExpressionSyntax : ExpressionSyntax
{
    public IndexExpressionSyntax(string baseIdentifier, ExpressionSyntax index)
    {
        BaseIdentifier = baseIdentifier;
        Index = index;
    }

    public string BaseIdentifier { get; }
    public ExpressionSyntax Index { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitIndexExpression(this);
    }
}
