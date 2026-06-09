namespace RetroSharp.SemanticAnalysis;

public record EnumType(string EnumName, IReadOnlyList<string> Members) : SymbolType(EnumName)
{
    private readonly HashSet<string> members = Members.ToHashSet(StringComparer.Ordinal);

    public bool HasMember(string name)
    {
        return members.Contains(name);
    }
}
