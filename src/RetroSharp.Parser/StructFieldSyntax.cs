namespace RetroSharp.Parser;

public class StructFieldSyntax : Syntax
{
    public StructFieldSyntax(string type, string name)
    {
        Type = type;
        Name = name;
    }

    public string Type { get; }
    public string Name { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitStructField(this);
    }
}
