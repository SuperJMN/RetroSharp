namespace RetroSharp.SemanticAnalysis;

public record StructType(string StructName, IReadOnlyList<StructField> Fields) : SymbolType(StructName)
{
    private readonly Dictionary<string, StructField> fieldsByName = Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);

    public Maybe<StructField> Field(string name)
    {
        return fieldsByName.TryGetValue(name, out var field)
            ? Maybe.From(field)
            : Maybe<StructField>.None;
    }
}

public record StructField(string Name, SymbolType Type, int Offset);
