namespace RetroSharp.Parser;

public class ContinueSyntax : StatementSyntax
{
    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitContinue(this);
    }
}
