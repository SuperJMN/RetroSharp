namespace RetroSharp.Parser;

public class MemberAccessSyntax : ExpressionSyntax
{
    public MemberAccessSyntax(ExpressionSyntax target, string member)
    {
        Target = target;
        Member = member;
    }

    public ExpressionSyntax Target { get; }
    public string Member { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitMemberAccess(this);
    }
}
