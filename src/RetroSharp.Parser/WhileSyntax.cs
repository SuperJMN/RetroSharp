namespace RetroSharp.Parser;

public class WhileSyntax : StatementSyntax
{
    public WhileSyntax(ExpressionSyntax condition, BlockSyntax body)
    {
        Condition = condition;
        Body = body;
    }

    public ExpressionSyntax Condition { get; }
    public BlockSyntax Body { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitWhile(this);
    }
}
