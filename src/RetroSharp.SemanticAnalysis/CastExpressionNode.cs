namespace RetroSharp.SemanticAnalysis;

public class CastExpressionNode : ExpressionNode
{
    public CastExpressionNode(string type, ExpressionNode expression)
    {
        Type = type;
        Expression = expression;
    }

    public string Type { get; }
    public ExpressionNode Expression { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitCastExpression(this);
    }

    public override IEnumerable<SemanticNode> Children => [Expression];
}
