namespace RetroSharp.Parser;

public class EnumMemberSyntax : Syntax
{
    public EnumMemberSyntax(string name, Maybe<ExpressionSyntax> value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public Maybe<ExpressionSyntax> Value { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitEnumMember(this);
    }
}
