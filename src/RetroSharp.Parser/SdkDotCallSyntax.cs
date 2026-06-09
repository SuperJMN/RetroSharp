namespace RetroSharp.Parser;

public class SdkDotCallSyntax : ExpressionSyntax
{
    public SdkDotCallSyntax(string module, string method, IEnumerable<ExpressionSyntax> parameters)
    {
        Module = module;
        Method = method;
        Parameters = parameters;
    }

    public string Module { get; }
    public string Method { get; }
    public IEnumerable<ExpressionSyntax> Parameters { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitSdkDotCall(this);
    }
}
