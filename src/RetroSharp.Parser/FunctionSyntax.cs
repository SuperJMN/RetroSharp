namespace RetroSharp.Parser;

public sealed record FunctionAttributeSyntax(string Name, IReadOnlyList<ExpressionSyntax> Arguments);

public class FunctionSyntax : Syntax
{
    public string Name { get; }
    public IList<ParameterSyntax> Parameters { get; }
    public BlockSyntax Block { get; }
    public string Type { get; }
    public bool IsExpressionBodied { get; }
    public bool IsInline { get; }
    public bool IsPure { get; }
    public bool IsExtern { get; }
    public IReadOnlyList<FunctionAttributeSyntax> Attributes { get; }

    public FunctionSyntax(
        string type,
        string name,
        IList<ParameterSyntax> parameters,
        BlockSyntax block,
        bool isExpressionBodied = false,
        bool isInline = false,
        bool isPure = false,
        bool isExtern = false,
        IReadOnlyList<FunctionAttributeSyntax>? attributes = null)
    {
        Type = type;
        Name = name;
        Parameters = parameters;
        Block = block;
        IsExpressionBodied = isExpressionBodied;
        IsInline = isInline;
        IsPure = isPure;
        IsExtern = isExtern;
        Attributes = attributes ?? [];
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitFunction(this);
    }
}
