namespace RetroSharp.Core.Sdk;

public abstract record SdkAabbExtent
{
    public sealed record Constant(int Value) : SdkAabbExtent;

    public sealed record SpriteWidth(string SpriteId) : SdkAabbExtent;
}
