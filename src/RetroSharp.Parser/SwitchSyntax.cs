namespace RetroSharp.Parser;

public class SwitchSyntax : StatementSyntax
{
    public SwitchSyntax(ExpressionSyntax subject, IReadOnlyList<SwitchCaseSyntax> cases, Maybe<BlockSyntax> defaultBlock)
    {
        Subject = subject;
        Cases = cases;
        DefaultBlock = defaultBlock;
    }

    public ExpressionSyntax Subject { get; }
    public IReadOnlyList<SwitchCaseSyntax> Cases { get; }
    public Maybe<BlockSyntax> DefaultBlock { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitSwitch(this);
    }
}
