namespace RetroSharp.Parser;

public class PipelineExpressionSyntax : ExpressionSyntax
{
    public PipelineExpressionSyntax(ExpressionSyntax value, IReadOnlyList<PipelineStepSyntax> steps)
    {
        Value = value;
        Steps = steps;
    }

    public ExpressionSyntax Value { get; }
    public IReadOnlyList<PipelineStepSyntax> Steps { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitPipelineExpression(this);
    }
}
