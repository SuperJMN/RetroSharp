namespace RetroSharp.Parser;

public class QualifiedCallSyntax : ExpressionSyntax
{
    public QualifiedCallSyntax(string qualifier, string method, IEnumerable<ExpressionSyntax> parameters)
    {
        Qualifier = qualifier;
        Method = method;
        Parameters = parameters;
    }

    public string Qualifier { get; }
    public string Method { get; }
    public IEnumerable<ExpressionSyntax> Parameters { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitQualifiedCall(this);
    }
}
