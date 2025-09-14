namespace RetroSharp.Parser;

public class BlockSyntax : ISyntax
{
    public BlockSyntax(List<StatementSyntax> statements)
    {
        Statements = statements;
    }

    public List<StatementSyntax> Statements { get; }
    public void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitBlock(this);
    }
}