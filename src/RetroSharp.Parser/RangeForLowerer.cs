using RetroSharp.Core;
using Zafiro.Core.Mixins;

namespace RetroSharp.Parser;

public static class RangeForLowerer
{
    public static ForSyntax Lower(RangeForSyntax rangeForSyntax)
    {
        return new ForSyntax(
            Maybe.From<StatementSyntax>(new DeclarationSyntax(
                rangeForSyntax.Type,
                rangeForSyntax.Identifier,
                Maybe<ExpressionSyntax>.None,
                Maybe.From(rangeForSyntax.Start))),
            Maybe.From<ExpressionSyntax>(new BinaryExpressionSyntax(
                new IdentifierSyntax(rangeForSyntax.Identifier),
                rangeForSyntax.End,
                Operator.Get("<"))),
            Maybe.From<ExpressionSyntax>(new PostfixMutationSyntax(
                new IdentifierLValue(rangeForSyntax.Identifier),
                "++")),
            rangeForSyntax.Body);
    }
}
