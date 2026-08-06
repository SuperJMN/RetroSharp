namespace RetroSharp.Core.Sdk;

using System.Reflection;

// Target-neutral serialization for the debugger symbol files emitted by
// `--symbols-out`. This is an external-facing debugging artifact (consumed by
// the Game Boy/NES debug MCP), so its exact format — dedup-by-name, address
// collision validation, ordering by address then name, and the
// "{Address:X4} {Name}" line shape — must stay byte-for-byte stable across
// targets. Each target supplies only its label (used in the collision error
// message) and its own reserved-range/named-address/report symbol sources.
public static class SymbolFileProjection
{
    public static string SerializeSymbols(string targetLabel, IEnumerable<(string Name, ushort Address)> symbols)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLabel);
        ArgumentNullException.ThrowIfNull(symbols);

        var addressesByName = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (addressesByName.TryGetValue(symbol.Name, out var previousAddress))
            {
                if (previousAddress != symbol.Address)
                {
                    throw new InvalidOperationException(
                        $"{targetLabel} debugger symbol '{symbol.Name}' maps to both ${previousAddress:X4} and ${symbol.Address:X4}.");
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

    // Discovers the debugger-visible name for each reserved memory range by
    // reflecting over `layoutType`'s static fields of type `TRange` — the same
    // pattern both targets used to use independently. Reflection, not a stored
    // name map, is deliberate: it guarantees the symbol file can never drift
    // from the field a target actually declared for that range.
    public static IEnumerable<(string Name, ushort Address)> ReservedRangeSymbols<TRange>(
        Type layoutType,
        IReadOnlyCollection<TRange> reservedRanges,
        Func<TRange, ushort> start)
        where TRange : notnull
    {
        ArgumentNullException.ThrowIfNull(layoutType);
        ArgumentNullException.ThrowIfNull(reservedRanges);
        ArgumentNullException.ThrowIfNull(start);

        var rangeIds = layoutType
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(TRange))
            .Select(field => (field.Name, Range: (TRange)field.GetValue(null)!))
            .Where(item => reservedRanges.Contains(item.Range))
            .ToDictionary(item => item.Range, item => item.Name);

        return reservedRanges.Select(range => (Name: rangeIds[range], Address: start(range)));
    }
}
