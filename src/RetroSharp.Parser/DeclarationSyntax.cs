namespace RetroSharp.Parser;

public class DeclarationSyntax : StatementSyntax
{
    public DeclarationSyntax(string type, string name, Maybe<ExpressionSyntax> initialization)
        : this(type, name, Maybe<ExpressionSyntax>.None, initialization)
    {
    }

    public DeclarationSyntax(string type, string name, Maybe<ExpressionSyntax> arrayLength, Maybe<ExpressionSyntax> initialization)
        : this(type, name, arrayLength, initialization, false)
    {
    }

    public DeclarationSyntax(string type, string name, Maybe<ExpressionSyntax> arrayLength, Maybe<ExpressionSyntax> initialization, bool isImmutable)
    {
        Type = type;
        Name = name;
        ArrayLength = arrayLength;
        Initialization = initialization;
        IsImmutable = isImmutable;
    }

    public string Type { get; }
    public string Name { get; }
    public Maybe<ExpressionSyntax> ArrayLength { get; }
    public Maybe<ExpressionSyntax> Initialization { get; }
    public bool IsImmutable { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitDeclaration(this);
    }
}
