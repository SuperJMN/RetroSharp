namespace RetroSharp.Parser.Tests;

public class ParserTests
{
    [Fact]
    public void Empty_main()
    {
        var source = @"i16 main() { }";
        AssertParse(source);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(1)]
    [InlineData(2304)]
    public void Return_integer_constant(int constant)
    {
        var source = $@"i16 main() {{ return {constant}; }}";
        AssertParse(source);
    }

    [Fact]
    public void Assignment()
    {
        var source = @"i16 main() { a = 12; }";
        AssertParse(source);
    }

    [Fact]
    public void Declaration()
    {
        var source = """
                     i16 main() 
                     { 
                        i16 a = 1;
                        i16 b = 2;                         
                     }
                     """;
        AssertParse(source);
    }

    [Fact]
    public void Multiple_lines()
    {
        var source = @"i16 main() { i16 b = 13; i16 a = 1; }";
        AssertParse(source);
    }

    [Fact]
    public void More_than_one_function()
    {
        var source = @"i16 main() { } void another() { }";
        AssertParse(source);
    }

    [Fact]
    public void Function_with_arguments()
    {
        var source = @"i16 main(i16 a, i16 b) { }";
        AssertParse(source);
    }

    [Fact]
    public void Arithmetic_addition()
    {
        var source = @"i16 main() { a = b + c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void Arithmetic_mult()
    {
        var source = @"i16 main() { a = b * c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void Equality()
    {
        var source = @"i16 main() { a = b == c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void Inequality()
    {
        var source = @"i16 main() { a = b != c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void Greater_than()
    {
        var source = @"i16 main() { a = b > c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void Less_than()
    {
        var source = @"i16 main() { a = b < c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void Less_than_or_equal()
    {
        var source = @"i16 main() { a = b <= c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void Greater_than_or_equal()
    {
        var source = @"i16 main() { a = b >= c; }";
        AssertParse(source);
    }
    
    [Fact]
    public void True()
    {
        var source = @"i16 main() { a = true; }";
        AssertParse(source);
    }
    
    [Fact]
    public void False()
    {
        var source = @"i16 main() { a = false; }";
        AssertParse(source);
    }

    [Fact]
    public void Empty_return()
    {
        var source = @"i16 main() { return; }";
        AssertParse(source);
    }

    [Fact]
    public void If_statement_without_else()
    {
        var source = @"i16 main() { if (a > b) { return a; }}";
        AssertParse(source);
    }
    
    [Fact]
    public void If_statement_with_else()
    {
        var source = @"i16 main() { if (a > b) { return a; } else { return b; } }";
        AssertParse(source);
    }
    
    [Fact]
    public void Parenthesis_are_OK()
    {
        var source = @"i16 main() { a = 2*(3+2); }";
        AssertParse(source);
    }
    
    [Fact]
    public void Call()
    {
        var source = @"i16 main() { Func(13); }";
        AssertParse(source);
    }
    
    private static void AssertParse(string source)
    {
        var sut = new SomeParser();
        var result = sut.Parse(source);

        var visitor = new PrintNodeVisitor();
        result.Should().Succeed()
            .And.Subject.Value.ToSyntaxString().Should().BeEquivalentToIgnoringWhitespace(source);
    }
}