namespace RetroSharp.SemanticAnalysis;

public class ForNode : StatementNode
{
    public ForNode(Maybe<StatementNode> initializer, Maybe<ExpressionNode> condition, Maybe<ExpressionNode> increment, BlockNode body)
    {
        Initializer = initializer;
        Condition = condition;
        Increment = increment;
        Body = body;
    }

    public Maybe<StatementNode> Initializer { get; }
    public Maybe<ExpressionNode> Condition { get; }
    public Maybe<ExpressionNode> Increment { get; }
    public BlockNode Body { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitFor(this);
    }

    public override IEnumerable<SemanticNode> Children
    {
        get
        {
            var children = new List<SemanticNode>();
            Initializer.Execute(children.Add);
            Condition.Execute(children.Add);
            Increment.Execute(children.Add);
            children.Add(Body);
            return children;
        }
    }
}
