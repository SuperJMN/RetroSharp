namespace RetroSharp.Parser;

public class DeclarationSyntax : StatementSyntax
{
    public DeclarationSyntax(string type, string name, Maybe<ExpressionSyntax> initialization)
    {
        Type = type;
        Name = name;
        Initialization = initialization;
    }

    public string Type { get; }
    public string Name { get; }
    public Maybe<ExpressionSyntax> Initialization { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitDeclaration(this);
    }
}