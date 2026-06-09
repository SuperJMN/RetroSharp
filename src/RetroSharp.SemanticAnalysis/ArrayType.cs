namespace RetroSharp.SemanticAnalysis;

public record ArrayType(SymbolType ElementType, int Length) : SymbolType($"{ElementType}[{Length}]");
