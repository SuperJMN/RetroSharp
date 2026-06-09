namespace RetroSharp.Parser;

public class PipelineStepSyntax
{
    public PipelineStepSyntax(string functionName, IEnumerable<ExpressionSyntax> arguments)
    {
        FunctionName = functionName;
        Arguments = arguments;
    }

    public string FunctionName { get; }
    public IEnumerable<ExpressionSyntax> Arguments { get; }
}
