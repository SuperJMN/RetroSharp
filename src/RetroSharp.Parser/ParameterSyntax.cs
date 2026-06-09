namespace RetroSharp.Parser;

public class ParameterSyntax : Syntax
{
    public ParameterSyntax(string type, string name)
        : this(type, name, Maybe<ExpressionSyntax>.None)
    {
    }

    public ParameterSyntax(string type, string name, Maybe<ExpressionSyntax> defaultValue)
    {
        Type = type;
        Name = name;
        DefaultValue = defaultValue;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitParameter(this);
    }

    public string Type { get; }
    public string Name { get; }
    public Maybe<ExpressionSyntax> DefaultValue { get; }
}
