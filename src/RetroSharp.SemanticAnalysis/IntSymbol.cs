namespace RetroSharp.SemanticAnalysis;

public record IntSymbol() : SymbolType("i16")
{
    public static readonly IntSymbol Instance = new();
    public override string ToString() => base.ToString();
}
