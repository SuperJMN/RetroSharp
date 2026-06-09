namespace RetroSharp.Parser;

public class BreakSyntax : StatementSyntax
{
    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitBreak(this);
    }
}
