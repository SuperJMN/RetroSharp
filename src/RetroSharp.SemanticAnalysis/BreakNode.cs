namespace RetroSharp.SemanticAnalysis;

public class BreakNode : StatementNode
{
    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitBreak(this);
    }

    public override IEnumerable<SemanticNode> Children => [];
}
