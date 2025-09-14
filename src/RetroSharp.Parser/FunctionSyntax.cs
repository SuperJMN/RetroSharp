namespace RetroSharp.Parser;

public class FunctionSyntax : Syntax
{
    public string Name { get; }
    public IList<ParameterSyntax> Parameters { get; }
    public BlockSyntax Block { get; }
    public string Type { get; }

    public FunctionSyntax(string type, string name, IList<ParameterSyntax> parameters, BlockSyntax block)
    {
        Type = type;
        Name = name;
        Parameters = parameters;
        Block = block;
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitFunction(this);
    }
}