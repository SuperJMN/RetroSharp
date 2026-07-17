using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Parser;

namespace RetroSharp.Sdk;

internal sealed record CompilerGeneratedRomTable(string FunctionName, string Label, byte[] Data)
{
    internal const string AttributeName = "compiler_generated_rom_table";

    internal static IReadOnlyDictionary<string, CompilerGeneratedRomTable> Read(
        IEnumerable<FunctionSyntax> functions)
    {
        var tables = new Dictionary<string, CompilerGeneratedRomTable>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            var attributes = function.Attributes
                .Where(attribute => attribute.Name == AttributeName)
                .ToArray();
            if (attributes.Length == 0)
            {
                continue;
            }

            if (attributes.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Compiler-generated ROM lookup function '{function.Name}' must declare exactly one '{AttributeName}' attribute.");
            }

            if (function is not { Type: "u8", Parameters: [{ Type: "u8", IsReceiver: false }] })
            {
                throw new InvalidOperationException(
                    $"Compiler-generated ROM lookup function '{function.Name}' must return u8 and accept one u8 index.");
            }

            var data = attributes[0].Arguments
                .Select((argument, index) => ReadByte(argument, function.Name, index))
                .ToArray();
            if (data.Length is 0 or > byte.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Compiler-generated ROM lookup function '{function.Name}' must contain 1..255 byte values, got {data.Length}.");
            }

            var table = new CompilerGeneratedRomTable(
                function.Name,
                $"generated_rom_table_{function.Name}",
                data);
            tables.Add(function.Name, table);
        }

        return tables;
    }

    private static byte ReadByte(ExpressionSyntax expression, string functionName, int index)
    {
        if (expression is not ConstantSyntax constant
            || !IntegerLiteral.TryParse(Convert.ToString(constant.Value, CultureInfo.InvariantCulture), out var value)
            || value is < byte.MinValue or > byte.MaxValue)
        {
            throw new InvalidOperationException(
                $"Compiler-generated ROM lookup function '{functionName}' value {index} must be a byte constant.");
        }

        return (byte)value;
    }
}
