namespace RetroSharp.Parser;

public class MemberAccessLValue : LValue
{
    public MemberAccessLValue(MemberAccessSyntax memberAccess)
    {
        MemberAccess = memberAccess;
    }

    public MemberAccessSyntax MemberAccess { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitMemberAccessLValue(this);
    }
}
