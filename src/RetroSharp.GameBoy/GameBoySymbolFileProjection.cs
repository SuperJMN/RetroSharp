namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal static class GameBoySymbolFileProjection
{
    internal static string Serialize(GameBoyRomBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        GameBoyRuntimeMemoryLayout.Validate();

        var symbols = SymbolFileProjection.ReservedRangeSymbols(
                typeof(GameBoyRuntimeMemoryLayout),
                GameBoyRuntimeMemoryLayout.ReservedRanges,
                range => range.Start)
            .Concat(GameBoyRuntimeMemoryLayout.NamedAddresses.Select(
                address => (Name: $"{address.Domain}.{address.Name}", Address: address.Address)))
            .Concat(result.Report.UserVariables.Select(
                variable => (Name: variable.Name, Address: variable.Address)));

        return SerializeSymbols(symbols);
    }

    internal static string SerializeSymbols(IEnumerable<(string Name, ushort Address)> symbols) =>
        SymbolFileProjection.SerializeSymbols("Game Boy", symbols);
}
