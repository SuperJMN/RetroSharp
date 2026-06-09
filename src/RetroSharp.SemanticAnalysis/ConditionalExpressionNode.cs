namespace RetroSharp.SemanticAnalysis;

public class ConditionalExpressionNode : ExpressionNode
{
    public ConditionalExpressionNode(ExpressionNode condition, ExpressionNode whenTrue, ExpressionNode whenFalse)
    {
        Condition = condition;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    public ExpressionNode Condition { get; }
    public ExpressionNode WhenTrue { get; }
    public ExpressionNode WhenFalse { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitConditionalExpression(this);
    }

    public override IEnumerable<SemanticNode> Children => [Condition, WhenTrue, WhenFalse];
}
