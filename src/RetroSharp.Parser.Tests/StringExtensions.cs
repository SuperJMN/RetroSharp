using System.Text.RegularExpressions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

namespace RetroSharp.Parser.Tests;

public static class StringExtensions
{
    public static AndConstraint<StringAssertions> BeEquivalentToIgnoringWhitespace(this StringAssertions assertions, string expected, string because = "", params object[] becauseArgs)
    {
        var actualWithoutWhitespace = Regex.Replace(assertions.Subject, @"\s", "");
        var expectedWithoutWhitespace = Regex.Replace(expected, @"\s", "");

        Execute.Assertion
            .ForCondition(actualWithoutWhitespace == expectedWithoutWhitespace)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:string} to be equivalent to {0}{reason}, but found {1}.", expected, assertions.Subject);
        return new AndConstraint<StringAssertions>(assertions);
    }
}