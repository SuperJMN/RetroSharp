namespace RetroSharp.SemanticAnalysis;

public class FunctionNode : SemanticNode
{
    public string Name { get; }
    public BlockNode Block { get; }
    public IReadOnlyList<string> Parameters { get; }
    public string ReturnType { get; }

    public FunctionNode(string returnType, string name, BlockNode block, IReadOnlyList<string> parameters)
    {
        ReturnType = returnType;
        Name = name;
        Block = block;
        Parameters = parameters;
    }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitFunctionNode(this);
    }

    public override IEnumerable<SemanticNode> Children => [Block];

    public override string ToString()
    {
        var ps = string.Join(", ", Parameters);
        return $"{ReturnType} {Name}({ps}) {Block}";
    }
}
