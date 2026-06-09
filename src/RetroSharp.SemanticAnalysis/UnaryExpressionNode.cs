namespace RetroSharp.SemanticAnalysis;

public class UnaryExpressionNode : ExpressionNode
{
    public UnaryExpressionNode(string operatorSymbol, ExpressionNode operand)
    {
        OperatorSymbol = operatorSymbol;
        Operand = operand;
    }

    public string OperatorSymbol { get; }
    public ExpressionNode Operand { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitUnaryExpression(this);
    }

    public override IEnumerable<SemanticNode> Children => [Operand];
}
