namespace RetroSharp.Parser;

public class PostfixMutationSyntax : ExpressionSyntax
{
    public PostfixMutationSyntax(LValue target, string operatorSymbol)
    {
        Target = target;
        OperatorSymbol = operatorSymbol;
    }

    public LValue Target { get; }
    public string OperatorSymbol { get; }

    public AssignmentSyntax ToAssignment()
    {
        var assignmentOperator = OperatorSymbol switch
        {
            "++" => "+=",
            "--" => "-=",
            _ => throw new InvalidOperationException($"Unsupported postfix operator '{OperatorSymbol}'."),
        };

        return new AssignmentSyntax(Target, assignmentOperator, new ConstantSyntax("1"));
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitPostfixMutation(this);
    }
}
