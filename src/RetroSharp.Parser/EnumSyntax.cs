namespace RetroSharp.Parser;

public class EnumSyntax : Syntax
{
    public EnumSyntax(string name, IList<EnumMemberSyntax> members)
    {
        Name = name;
        Members = members;
    }

    public string Name { get; }
    public IList<EnumMemberSyntax> Members { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitEnum(this);
    }
}
