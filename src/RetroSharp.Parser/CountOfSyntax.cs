namespace RetroSharp.Parser;

public class CountOfSyntax : ExpressionSyntax
{
    public CountOfSyntax(string baseIdentifier)
    {
        BaseIdentifier = baseIdentifier;
    }

    public string BaseIdentifier { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitCountOf(this);
    }
}
