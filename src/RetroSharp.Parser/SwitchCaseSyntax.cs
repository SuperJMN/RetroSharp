namespace RetroSharp.Parser;

public class SwitchCaseSyntax : Syntax
{
    public SwitchCaseSyntax(ExpressionSyntax value, BlockSyntax block)
        : this([new SwitchCasePatternSyntax(value)], block)
    {
    }

    public SwitchCaseSyntax(IEnumerable<ExpressionSyntax> values, BlockSyntax block)
        : this(values.Select(value => new SwitchCasePatternSyntax(value)).ToList(), block)
    {
    }

    public SwitchCaseSyntax(IReadOnlyList<SwitchCasePatternSyntax> patterns, BlockSyntax block)
    {
        if (patterns.Count == 0)
        {
            throw new ArgumentException("Switch cases require at least one value.", nameof(patterns));
        }

        Patterns = patterns;
        Block = block;
    }

    public ExpressionSyntax Value => Patterns[0].Start;
    public IReadOnlyList<ExpressionSyntax> Values => Patterns.Select(pattern => pattern.Start).ToList();
    public IReadOnlyList<SwitchCasePatternSyntax> Patterns { get; }
    public BlockSyntax Block { get; }

    public override void Accept(ISyntaxVisitor visitor)
    {
        visitor.VisitSwitchCase(this);
    }
}
