namespace RetroSharp.Parser;

public class UnaryExpressionSyntax : ExpressionSyntax
{
    public UnaryExpressionSyntax(string operatorSymbol, ExpressionSyntax operand)
    {
        OperatorSymbol = operatorSymbol;
        Operand = operand;
    }

    public string OperatorSymbol { get; }
    public ExpressionSyntax Operand { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitUnaryOperator(this);
    }
}
