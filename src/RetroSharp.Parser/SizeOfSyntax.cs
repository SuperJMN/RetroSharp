namespace RetroSharp.Parser;

public class SizeOfSyntax : ExpressionSyntax
{
    public SizeOfSyntax(string type)
    {
        Type = type;
    }

    public string Type { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitSizeOf(this);
    }
}
