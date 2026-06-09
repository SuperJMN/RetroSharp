namespace RetroSharp.Parser;

public class NamedArgumentSyntax : ExpressionSyntax
{
    public NamedArgumentSyntax(string name, ExpressionSyntax expression)
    {
        Name = name;
        Expression = expression;
    }

    public string Name { get; }
    public ExpressionSyntax Expression { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitNamedArgument(this);
    }
}
