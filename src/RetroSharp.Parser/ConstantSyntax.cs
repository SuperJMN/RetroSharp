namespace RetroSharp.Parser;

public class ConstantSyntax : ExpressionSyntax
{
    public ConstantSyntax(string text)
    {
        Value = text;
    }

    public object Value { get; set; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitConstant(this);
    }
}