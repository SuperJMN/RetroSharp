namespace RetroSharp.Parser;

public class AssignmentSyntax : ExpressionSyntax
{
    public AssignmentSyntax(LValue left, ExpressionSyntax right)
        : this(left, "=", right)
    {
    }

    public AssignmentSyntax(LValue left, string operatorSymbol, ExpressionSyntax right)
    {
        Left = left;
        OperatorSymbol = operatorSymbol;
        Right = right;
    }

    public LValue Left { get; }
    public string OperatorSymbol { get; }
    public ExpressionSyntax Right { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitAssignment(this);
    }
}
