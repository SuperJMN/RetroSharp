namespace RetroSharp.Core.Tests;

using RetroSharp.Core;
using Xunit;

public sealed class IntegerLiteralTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("1_024", 1024)]
    [InlineData("0x2A", 42)]
    [InlineData("0Xff", 255)]
    [InlineData("0b1010_0000", 160)]
    [InlineData("-0b10", -2)]
    [InlineData("255u8", 255)]
    [InlineData("-1i8", -1)]
    [InlineData("0x1234u16", 4660)]
    [InlineData("0b1010_0000u8", 160)]
    public void Parses_decimal_hex_and_binary_integer_literals(string text, int expected)
    {
        Assert.True(IntegerLiteral.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0x")]
    [InlineData("0b")]
    [InlineData("0b102")]
    [InlineData("1__2")]
    [InlineData("1u32")]
    [InlineData("1u")]
    [InlineData("1_u8")]
    public void Rejects_malformed_integer_literals(string text)
    {
        Assert.False(IntegerLiteral.TryParse(text, out _));
    }

    [Theory]
    [InlineData("300u16", "u16")]
    [InlineData("-1i8", "i8")]
    [InlineData("0x2Au8", "u8")]
    [InlineData("0b1010_0000u8", "u8")]
    [InlineData("128i16", "i16")]
    public void Reads_width_suffix_type_from_integer_literals(string text, string expected)
    {
        Assert.True(IntegerLiteral.TryGetSuffixType(text, out var suffixType));
        Assert.Equal(expected, suffixType);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("0x2A")]
    [InlineData("0b1010_0000")]
    [InlineData("1u")]
    public void Reports_no_suffix_for_unsuffixed_or_invalid_literals(string text)
    {
        Assert.False(IntegerLiteral.TryGetSuffixType(text, out _));
    }
}
