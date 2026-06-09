namespace RetroSharp.SemanticAnalysis;

public class ContinueNode : StatementNode
{
    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitContinue(this);
    }

    public override IEnumerable<SemanticNode> Children => [];
}
