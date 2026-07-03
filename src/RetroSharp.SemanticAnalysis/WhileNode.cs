namespace RetroSharp.SemanticAnalysis;

public class WhileNode : StatementNode
{
    public WhileNode(ExpressionNode condition, BlockNode body)
    {
        Condition = condition;
        Body = body;
    }

    public ExpressionNode Condition { get; }
    public BlockNode Body { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitWhile(this);
    }

    public override IEnumerable<SemanticNode> Children => [Condition, Body];
}
