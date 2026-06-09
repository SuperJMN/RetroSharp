namespace RetroSharp.Parser;

public class DeclarationSyntax : StatementSyntax
{
    public DeclarationSyntax(string type, string name, Maybe<ExpressionSyntax> initialization)
        : this(type, name, Maybe<ExpressionSyntax>.None, initialization)
    {
    }

    public DeclarationSyntax(string type, string name, Maybe<ExpressionSyntax> arrayLength, Maybe<ExpressionSyntax> initialization)
    {
        Type = type;
        Name = name;
        ArrayLength = arrayLength;
        Initialization = initialization;
    }

    public string Type { get; }
    public string Name { get; }
    public Maybe<ExpressionSyntax> ArrayLength { get; }
    public Maybe<ExpressionSyntax> Initialization { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitDeclaration(this);
    }
}
