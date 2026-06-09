namespace RetroSharp.Parser;

public class TypeAliasSyntax : Syntax
{
    public TypeAliasSyntax(string name, string type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public string Type { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitTypeAlias(this);
    }
}
