namespace RetroSharp.Parser;

public class ParameterSyntax : Syntax
{
    public ParameterSyntax(string type, string name)
        : this(type, name, Maybe<ExpressionSyntax>.None, false)
    {
    }

    public ParameterSyntax(string type, string name, Maybe<ExpressionSyntax> defaultValue, bool isReceiver = false)
    {
        Type = type;
        Name = name;
        DefaultValue = defaultValue;
        IsReceiver = isReceiver;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitParameter(this);
    }

    public string Type { get; }
    public string Name { get; }
    public Maybe<ExpressionSyntax> DefaultValue { get; }
    public bool IsReceiver { get; }
}
