namespace RetroSharp.SemanticAnalysis;

public record Symbol(string Name, SymbolType Type, bool IsImmutable = false)
{
    public override string ToString() => IsImmutable ? "let " + Name : Type + " " + Name;
}
