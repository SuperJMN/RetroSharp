namespace RetroSharp.Parser;

public class StructInitializerSyntax : ExpressionSyntax
{
    public StructInitializerSyntax(IEnumerable<StructFieldInitializerSyntax> fields)
    {
        Fields = fields.ToList();
    }

    public IReadOnlyList<StructFieldInitializerSyntax> Fields { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitStructInitializer(this);
    }
}

public class StructFieldInitializerSyntax
{
    public StructFieldInitializerSyntax(string name, ExpressionSyntax expression)
    {
        Name = name;
        Expression = expression;
    }

    public string Name { get; }
    public ExpressionSyntax Expression { get; }
}
