namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class SymbolFileProjectionTests
{
    [Fact]
    public void Serialize_symbols_dedupes_identical_name_address_pairs_and_orders_by_address_then_name()
    {
        var text = SymbolFileProjection.SerializeSymbols(
            "Test",
            [
                ("zebra", 0x1234),
                ("alpha", 0x0010),
                ("zebra", 0x1234),
                ("beta", 0x0010),
            ]);

        Assert.Equal(
            "0010 alpha\n0010 beta\n1234 zebra\n",
            text);
    }

    [Fact]
    public void Serialize_symbols_rejects_the_same_name_mapped_to_two_different_addresses()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SymbolFileProjection.SerializeSymbols(
                "Test",
                [
                    ("duplicate", 0x1234),
                    ("duplicate", 0x5678),
                ]));

        Assert.Equal(
            "Test debugger symbol 'duplicate' maps to both $1234 and $5678.",
            error.Message);
    }

    [Fact]
    public void Serialize_symbols_uses_the_supplied_target_label_in_the_collision_message()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            SymbolFileProjection.SerializeSymbols(
                "Other Target",
                [
                    ("duplicate", 0x0001),
                    ("duplicate", 0x0002),
                ]));

        Assert.StartsWith("Other Target debugger symbol 'duplicate'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_symbols_produces_a_single_trailing_newline_terminated_line_per_entry()
    {
        var text = SymbolFileProjection.SerializeSymbols("Test", [("only", 0xABCD)]);

        Assert.Equal("ABCD only\n", text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    private readonly record struct FakeRange(string Name, ushort Start, int Length);

    private static class FakeLayout
    {
        internal static readonly FakeRange First = new("first", 0x0100, 0x0010);
        internal static readonly FakeRange Second = new("second", 0x0200, 0x0010);

        // Not part of ReservedRanges below: must not be discovered as a symbol.
        internal static readonly FakeRange Unreferenced = new("unreferenced", 0x0300, 0x0010);

        internal static readonly IReadOnlyList<FakeRange> ReservedRanges = [First, Second];
    }

    [Fact]
    public void Reserved_range_symbols_reflects_only_the_fields_referenced_by_reserved_ranges()
    {
        var symbols = SymbolFileProjection.ReservedRangeSymbols(
                typeof(FakeLayout),
                FakeLayout.ReservedRanges,
                range => range.Start)
            .ToArray();

        Assert.Equal(
            new (string Name, ushort Address)[] { ("First", 0x0100), ("Second", 0x0200) },
            symbols);
    }
}
