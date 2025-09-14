namespace RetroSharp.Parser;

public class ParameterSyntax : Syntax
{
    public ParameterSyntax(string type, string name)
    {
        Type = type;
        Name = name;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitParameter(this);
    }

    public string Type { get; }
    public string Name { get; }
}