namespace RetroSharp.Parser;

public class LoopSyntax : StatementSyntax
{
    public LoopSyntax(BlockSyntax body)
    {
        Body = body;
    }

    public BlockSyntax Body { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitLoop(this);
    }
}
