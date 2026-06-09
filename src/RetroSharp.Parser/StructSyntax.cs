namespace RetroSharp.Parser;

public class StructSyntax : Syntax
{
    public StructSyntax(string name, IList<StructFieldSyntax> fields)
    {
        Name = name;
        Fields = fields;
    }

    public string Name { get; }
    public IList<StructFieldSyntax> Fields { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitStruct(this);
    }
}
