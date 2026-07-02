namespace RetroSharp.Core.Sdk;

public enum SdkResourceDeclarationKind
{
    BackgroundPalette,
    SpritePalette,
    WorldLoad,
    SpriteAsset,
    MusicAsset,
    AnimationClip,
}

public sealed record SdkResourceDeclarationDescriptor(string ResourceId, SdkResourceDeclarationKind Kind)
{
    public static bool TryCreate(string resourceId, out SdkResourceDeclarationDescriptor descriptor)
    {
        if (Descriptors.TryGetValue(resourceId, out descriptor!))
        {
            return true;
        }

        descriptor = null!;
        return false;
    }

    public static SdkResourceDeclarationDescriptor Create(string resourceId)
    {
        return TryCreate(resourceId, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Unknown SDK resource declaration '{resourceId}'.");
    }

    private static readonly Dictionary<string, SdkResourceDeclarationDescriptor> Descriptors =
        new(StringComparer.Ordinal)
        {
            ["palette_background"] = new SdkResourceDeclarationDescriptor("palette_background", SdkResourceDeclarationKind.BackgroundPalette),
            ["palette_sprite"] = new SdkResourceDeclarationDescriptor("palette_sprite", SdkResourceDeclarationKind.SpritePalette),
            ["world_load"] = new SdkResourceDeclarationDescriptor("world_load", SdkResourceDeclarationKind.WorldLoad),
            ["sprite_asset"] = new SdkResourceDeclarationDescriptor("sprite_asset", SdkResourceDeclarationKind.SpriteAsset),
            ["music_asset"] = new SdkResourceDeclarationDescriptor("music_asset", SdkResourceDeclarationKind.MusicAsset),
            ["animation_clip"] = new SdkResourceDeclarationDescriptor("animation_clip", SdkResourceDeclarationKind.AnimationClip),
        };
}
