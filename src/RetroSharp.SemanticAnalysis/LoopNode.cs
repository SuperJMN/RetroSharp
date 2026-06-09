namespace RetroSharp.SemanticAnalysis;

public class LoopNode : StatementNode
{
    public LoopNode(BlockNode body)
    {
        Body = body;
    }

    public BlockNode Body { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitLoop(this);
    }

    public override IEnumerable<SemanticNode> Children => [Body];
}
