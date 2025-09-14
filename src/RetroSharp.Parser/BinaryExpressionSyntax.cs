using RetroSharp.Core;

namespace RetroSharp.Parser;

public class BinaryExpressionSyntax : ExpressionSyntax
{
    public ExpressionSyntax Left { get; }
    public ExpressionSyntax Right { get; }
    public Operator Operator { get; }

    public BinaryExpressionSyntax(ExpressionSyntax left, ExpressionSyntax right, Operator @operator)
    {
        Left = left;
        Right = right;
        Operator = @operator;
    }
    
    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitBinaryOperator(this);
    }
}