namespace RetroSharp.Core.Sdk;

public abstract record SdkStorageLocation
{
    public sealed record Local(string Name) : SdkStorageLocation;

    public sealed record Field(SdkStorageLocation Target, string FieldName) : SdkStorageLocation;

    public sealed record IndexedElement(string BaseName, int Index) : SdkStorageLocation;
}

public abstract record SdkByteExpression
{
    public sealed record Constant(int Value) : SdkByteExpression;

    public sealed record Variable(SdkStorageLocation Location) : SdkByteExpression;
}
