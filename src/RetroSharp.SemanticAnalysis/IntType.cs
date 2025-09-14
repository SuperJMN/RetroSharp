namespace RetroSharp.SemanticAnalysis;

public record IntType() : SymbolType("i16")
{
    public static readonly IntType Instance = new();
    public override string ToString() => base.ToString();
}
