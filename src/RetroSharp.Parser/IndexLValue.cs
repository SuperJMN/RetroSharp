namespace RetroSharp.Parser;

public class IndexLValue : LValue
{
    public IndexLValue(string baseIdentifier, ExpressionSyntax index)
    {
        BaseIdentifier = baseIdentifier;
        Index = index;
    }

    public string BaseIdentifier { get; }
    public ExpressionSyntax Index { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitIndexLValue(this);
    }
}

