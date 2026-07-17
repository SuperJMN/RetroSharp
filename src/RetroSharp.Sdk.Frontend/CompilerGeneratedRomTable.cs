using RetroSharp.Parser;

namespace RetroSharp.Sdk;

internal sealed record CompilerGeneratedRomTable(string FunctionName, byte[] Data)
{
    internal const string ReservedAttributeName = "compiler_generated_rom_table";

    public string Label => $"generated_rom_table_{FunctionName}";

    internal static void RejectUserAuthoredAttributes(IEnumerable<FunctionSyntax> functions)
    {
        var function = functions.FirstOrDefault(candidate =>
            candidate.Attributes.Any(attribute => attribute.Name == ReservedAttributeName));
        if (function is not null)
        {
            throw new InvalidOperationException(
                $"Attribute '{ReservedAttributeName}' on function '{function.Name}' is reserved for compiler-generated functions.");
        }
    }

    internal static IReadOnlyDictionary<string, CompilerGeneratedRomTable> Validate(
        IReadOnlyDictionary<string, CompilerGeneratedRomTable>? tables,
        IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        if (tables is null || tables.Count == 0)
        {
            return new Dictionary<string, CompilerGeneratedRomTable>(StringComparer.Ordinal);
        }

        var validated = new Dictionary<string, CompilerGeneratedRomTable>(StringComparer.Ordinal);
        foreach (var (functionName, table) in tables)
        {
            if (functionName != table.FunctionName)
            {
                throw new InvalidOperationException(
                    $"Compiler-generated ROM table key '{functionName}' does not match function '{table.FunctionName}'.");
            }

            if (!functions.TryGetValue(functionName, out var function)
                || function is not { Type: "u8", Parameters: [{ Type: "u8", IsReceiver: false }] })
            {
                throw new InvalidOperationException(
                    $"Compiler-generated ROM lookup function '{functionName}' must return u8 and accept one u8 index.");
            }

            if (table.Data.Length is 0 or > byte.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Compiler-generated ROM lookup function '{functionName}' must contain 1..255 byte values, got {table.Data.Length}.");
            }

            validated.Add(functionName, table);
        }

        return validated;
    }
}
