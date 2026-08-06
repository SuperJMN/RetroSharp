namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal static class NesSymbolFileProjection
{
    internal static string Serialize(NesRomBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        NesRuntimeMemoryLayout.Validate();

        var symbols = SymbolFileProjection.ReservedRangeSymbols(
                typeof(NesRuntimeMemoryLayout),
                NesRuntimeMemoryLayout.ReservedRanges,
                range => range.Start)
            .Concat(NesRuntimeMemoryLayout.NamedAddresses.Select(
                address => (Name: $"{address.Domain}.{address.Name}", Address: address.Address)))
            .Concat(result.Report.RuntimeRegions.Select(
                region => (Name: region.Name, Address: region.Start)))
            .Concat(result.Report.UserVariables.Select(
                variable => (Name: variable.Name, Address: variable.Address)));

        return SerializeSymbols(symbols);
    }

    internal static string SerializeSymbols(IEnumerable<(string Name, ushort Address)> symbols) =>
        SymbolFileProjection.SerializeSymbols("NES", symbols);
}
