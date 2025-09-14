namespace RetroSharp.Parser;

public class IfElseSyntax : StatementSyntax
{
    public ExpressionSyntax Condition { get; }
    public BlockSyntax ThenBlock { get; }
    public Maybe<BlockSyntax> ElseBlock { get; }

    public IfElseSyntax(ExpressionSyntax condition, BlockSyntax thenBlock, Maybe<BlockSyntax> elseBlock)
    {
        Condition = condition;
        ThenBlock = thenBlock;
        ElseBlock = elseBlock;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitIfElse(this);
    }
}