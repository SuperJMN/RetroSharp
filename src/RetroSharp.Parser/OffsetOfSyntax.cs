namespace RetroSharp.Parser;

public class OffsetOfSyntax : ExpressionSyntax
{
    public OffsetOfSyntax(string type, string field)
    {
        Type = type;
        Field = field;
    }

    public string Type { get; }
    public string Field { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitOffsetOf(this);
    }
}
