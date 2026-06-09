using RetroSharp.Core;

namespace RetroSharp.Parser;

public static class SwitchLowerer
{
    public static IfElseSyntax Lower(SwitchSyntax switchSyntax)
    {
        if (switchSyntax.Cases.Count == 0)
        {
            throw new InvalidOperationException("Switch statements require at least one case.");
        }

        var elseBlock = switchSyntax.DefaultBlock;
        foreach (var switchCase in switchSyntax.Cases.Reverse())
        {
            var condition = SwitchPatternConditions.CaseCondition(switchSyntax.Subject, switchCase.Patterns);
            elseBlock = Maybe.From(new BlockSyntax([new IfElseSyntax(condition, switchCase.Block, elseBlock)]));
        }

        return (IfElseSyntax)elseBlock.Value.Statements[0];
    }
}
