namespace RetroSharp.SemanticAnalysis;

public record PrimitiveType(string TypeName, int Size) : SymbolType(TypeName)
{
    public static readonly PrimitiveType I8 = new("i8", 1);
    public static readonly PrimitiveType U8 = new("u8", 1);
    public static readonly PrimitiveType U16 = new("u16", 2);
    public static readonly PrimitiveType Bool = new("bool", 1);
}
