namespace RetroSharp.Core.Sdk;

public abstract record SdkByteExpression
{
    public sealed record Constant(int Value) : SdkByteExpression;

    public sealed record Variable(string Name) : SdkByteExpression;
}
