namespace RetroSharp.Parser;

public class RangeForSyntax : StatementSyntax
{
    public RangeForSyntax(string type, string identifier, ExpressionSyntax start, ExpressionSyntax end, BlockSyntax body)
    {
        Type = type;
        Identifier = identifier;
        Start = start;
        End = end;
        Body = body;
    }

    public string Type { get; }
    public string Identifier { get; }
    public ExpressionSyntax Start { get; }
    public ExpressionSyntax End { get; }
    public BlockSyntax Body { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitRangeFor(this);
    }
}
