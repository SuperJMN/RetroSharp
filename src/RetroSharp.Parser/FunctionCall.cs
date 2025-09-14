namespace RetroSharp.Parser;

public class FunctionCall : ExpressionSyntax
{
    public FunctionCall(string name, IEnumerable<ExpressionSyntax> parameters)
    {
        Name = name;
        Parameters = parameters;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitFunctionCall(this);
    }

    public string Name { get; }
    public IEnumerable<ExpressionSyntax> Parameters { get; }
}