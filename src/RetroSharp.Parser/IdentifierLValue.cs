namespace RetroSharp.Parser;

public class IdentifierLValue : LValue
{
    public IdentifierLValue(string identifier)
    {
        Identifier = identifier;
    }

    public string Identifier { get; }
    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitIdentifierLValue(this);
    }
}