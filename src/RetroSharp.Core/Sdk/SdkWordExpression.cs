namespace RetroSharp.Core.Sdk;

public abstract record SdkWordExpression
{
    public sealed record Constant(int Value) : SdkWordExpression;

    public sealed record Variable(SdkStorageLocation Location) : SdkWordExpression;
}
