using Xunit;
using SomeCompiler.Z80.Tests.Support;

namespace SomeCompiler.Z80.Tests;

public class EndToEndFactorialTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 6)]
    [InlineData(5, 120)]
    public void Factorial_returns_expected(int n, int expected)
    {
        var src = $@"int fact(int n) {{
    if (n == 0) {{
        return 1;
    }} else {{
        return n * fact(n - 1);
    }}
}}

int main() {{
    return fact({n});
}}";
        var actual = Z80E2E.RunHL(src, maxSteps: 100000);
        Assert.Equal(expected, actual);
    }
}

