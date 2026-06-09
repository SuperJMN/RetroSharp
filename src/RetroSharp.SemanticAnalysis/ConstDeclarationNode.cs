namespace RetroSharp.SemanticAnalysis;

public class ConstDeclarationNode : StatementNode
{
    public ConstDeclarationNode(string name, Scope scope, ExpressionNode value)
    {
        Name = name;
        Scope = scope;
        Value = value;
    }

    public string Name { get; }
    public Scope Scope { get; }
    public ExpressionNode Value { get; }

    public override void Accept(INodeVisitor visitor)
    {
        visitor.VisitConstDeclarationNode(this);
    }

    public override IEnumerable<SemanticNode> Children => [Value];
}
