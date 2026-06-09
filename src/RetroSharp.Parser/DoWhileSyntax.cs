namespace RetroSharp.Parser;

public class DoWhileSyntax : StatementSyntax
{
    public DoWhileSyntax(BlockSyntax body, ExpressionSyntax condition)
    {
        Body = body;
        Condition = condition;
    }

    public BlockSyntax Body { get; }
    public ExpressionSyntax Condition { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitDoWhile(this);
    }
}
