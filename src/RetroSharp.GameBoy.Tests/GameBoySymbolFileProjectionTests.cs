namespace RetroSharp.GameBoy.Tests;

using System.Globalization;
using System.Reflection;
using RetroSharp.Sdk;
using Xunit;

public sealed class GameBoySymbolFileProjectionTests
{
    [Fact]
    public void Projection_is_complete_deterministic_and_mcp_compatible()
    {
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        var first = GameBoySymbolFileProjection.Serialize(result);
        var second = GameBoySymbolFileProjection.Serialize(result);
        var symbols = ParseSymbols(first);
        var expected = ExpectedSymbols(result);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', first);
        Assert.Equal(expected, symbols);
        Assert.Equal(
            symbols.OrderBy(symbol => symbol.Address).ThenBy(symbol => symbol.Name, StringComparer.Ordinal),
            symbols);

        var addresses = symbols.ToDictionary(symbol => symbol.Name, symbol => symbol.Address, StringComparer.Ordinal);
        Assert.Equal(GameBoyRuntimeMemoryLayout.Camera.XLow, addresses["camera.XLow"]);
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.CommitCount, addresses["packed camera.CommitCount"]);
        Assert.Equal(GameBoyRuntimeMemoryLayout.WorldPackStaging.Start, addresses["WorldPackStaging"]);
        Assert.Equal(
            Assert.Single(result.Report.UserVariables, variable => variable.Name == "player.x").Address,
            addresses["player.x"]);
    }

    [Fact]
    public void Projection_allows_aliases_but_rejects_one_name_at_different_addresses()
    {
        Assert.Equal(
            "1234 first alias\n1234 second alias\n",
            GameBoySymbolFileProjection.SerializeSymbols(
            [
                ("first alias", 0x1234),
                ("second alias", 0x1234),
                ("first alias", 0x1234),
            ]));

        var error = Assert.Throws<InvalidOperationException>(() =>
            GameBoySymbolFileProjection.SerializeSymbols(
            [
                ("duplicate", 0x1234),
                ("duplicate", 0x5678),
            ]));

        Assert.Equal(
            "Game Boy debugger symbol 'duplicate' maps to both $1234 and $5678.",
            error.Message);
    }

    private static (string Name, ushort Address)[] ExpectedSymbols(GameBoyRomBuildResult result)
    {
        var rangeIds = typeof(GameBoyRuntimeMemoryLayout)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(GameBoyWramRange))
            .Select(field => (field.Name, Range: (GameBoyWramRange)field.GetValue(null)!))
            .Where(item => GameBoyRuntimeMemoryLayout.ReservedRanges.Contains(item.Range))
            .ToDictionary(item => item.Range, item => item.Name);

        return GameBoyRuntimeMemoryLayout.ReservedRanges
            .Select(range => (Name: rangeIds[range], Address: range.Start))
            .Concat(GameBoyRuntimeMemoryLayout.NamedAddresses.Select(
                address => (Name: $"{address.Domain}.{address.Name}", Address: address.Address)))
            .Concat(result.Report.UserVariables.Select(
                variable => (Name: variable.Name, Address: variable.Address)))
            .Distinct()
            .OrderBy(symbol => symbol.Address)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static (string Name, ushort Address)[] ParseSymbols(string text)
    {
        var lines = text.Split('\n');
        Assert.Equal(string.Empty, lines[^1]);

        return lines[..^1]
            .Select(line =>
            {
                Assert.Matches("^[0-9A-F]{4} .+$", line);
                var separator = line.IndexOf(' ');
                Assert.Equal(4, separator);
                Assert.True(ushort.TryParse(
                    line[..separator],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out var address));
                return (Name: line[(separator + 1)..], Address: address);
            })
            .ToArray();
    }
}
