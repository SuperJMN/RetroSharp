namespace RetroSharp.Parser;

public class ForSyntax : StatementSyntax
{
    public ForSyntax(Maybe<StatementSyntax> initializer, Maybe<ExpressionSyntax> condition, Maybe<ExpressionSyntax> increment, BlockSyntax body)
    {
        Initializer = initializer;
        Condition = condition;
        Increment = increment;
        Body = body;
    }

    public Maybe<StatementSyntax> Initializer { get; }
    public Maybe<ExpressionSyntax> Condition { get; }
    public Maybe<ExpressionSyntax> Increment { get; }
    public BlockSyntax Body { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitFor(this);
    }
}
