namespace RetroSharp.Parser;

public static class PipelineExpressionLowerer
{
    public static ExpressionSyntax Lower(PipelineExpressionSyntax pipeline)
    {
        return pipeline.Steps.Aggregate(
            pipeline.Value,
            (current, step) => new FunctionCall(
                step.FunctionName,
                new[] { current }.Concat(step.Arguments)));
    }
}
