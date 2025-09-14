namespace RetroSharp.Parser;

public class IdentifierSyntax : ExpressionSyntax
{
    public string Identifier { get; }

    public IdentifierSyntax(string identifier)
    {
        Identifier = identifier;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitIdentifier(this);
    }
}