namespace RetroSharp.Parser;

public class ConstDeclarationSyntax : StatementSyntax
{
    public ConstDeclarationSyntax(string name, ExpressionSyntax value)
        : this(Maybe<string>.None, name, value)
    {
    }

    public ConstDeclarationSyntax(string type, string name, ExpressionSyntax value)
        : this(Maybe.From(type), name, value)
    {
    }

    public ConstDeclarationSyntax(Maybe<string> typeAnnotation, string name, ExpressionSyntax value)
    {
        TypeAnnotation = typeAnnotation;
        Name = name;
        Value = value;
    }

    public Maybe<string> TypeAnnotation { get; }
    public string Type => TypeAnnotation.GetValueOrDefault("u8");
    public string Name { get; }
    public ExpressionSyntax Value { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitConstDeclaration(this);
    }
}
