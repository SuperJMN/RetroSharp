namespace RetroSharp.Parser;

public class ArrayInitializerSyntax : ExpressionSyntax
{
    public ArrayInitializerSyntax(IEnumerable<ExpressionSyntax> elements)
    {
        Elements = elements.ToList();
    }

    public IReadOnlyList<ExpressionSyntax> Elements { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitArrayInitializer(this);
    }
}
