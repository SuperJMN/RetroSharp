namespace RetroSharp.SemanticAnalysis;

public class DoWhileNode : StatementNode
{
    public DoWhileNode(BlockNode body, ExpressionNode condition)
    {
        Body = body;
        Condition = condition;
    }

    public BlockNode Body { get; }
    public ExpressionNode Condition { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitDoWhile(this);
    }

    public override IEnumerable<SemanticNode> Children => [Body, Condition];
}
