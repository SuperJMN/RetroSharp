namespace RetroSharp.GameBoy;

using System.Reflection;

internal static class GameBoySymbolFileProjection
{
    internal static string Serialize(GameBoyRomBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        GameBoyRuntimeMemoryLayout.Validate();

        var rangeIds = typeof(GameBoyRuntimeMemoryLayout)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(GameBoyWramRange))
            .Select(field => (field.Name, Range: (GameBoyWramRange)field.GetValue(null)!))
            .Where(item => GameBoyRuntimeMemoryLayout.ReservedRanges.Contains(item.Range))
            .ToDictionary(item => item.Range, item => item.Name);
        var symbols = GameBoyRuntimeMemoryLayout.ReservedRanges
            .Select(range => (Name: rangeIds[range], Address: range.Start))
            .Concat(GameBoyRuntimeMemoryLayout.NamedAddresses.Select(
                address => (Name: $"{address.Domain}.{address.Name}", Address: address.Address)))
            .Concat(result.Report.UserVariables.Select(
                variable => (Name: variable.Name, Address: variable.Address)));

        return SerializeSymbols(symbols);
    }

    internal static string SerializeSymbols(IEnumerable<(string Name, ushort Address)> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var addressesByName = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (addressesByName.TryGetValue(symbol.Name, out var previousAddress))
            {
                if (previousAddress != symbol.Address)
                {
                    throw new InvalidOperationException(
                        $"Game Boy debugger symbol '{symbol.Name}' maps to both ${previousAddress:X4} and ${symbol.Address:X4}.");
                }

                continue;
            }

            addressesByName.Add(symbol.Name, symbol.Address);
        }

        return string.Join(
                   "\n",
                   addressesByName
                       .Select(symbol => (Name: symbol.Key, Address: symbol.Value))
                       .OrderBy(symbol => symbol.Address)
                       .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
                       .Select(symbol => $"{symbol.Address:X4} {symbol.Name}")) +
               "\n";
    }
}
