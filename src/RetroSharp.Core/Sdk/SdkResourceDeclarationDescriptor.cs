namespace RetroSharp.Core.Sdk;

public enum SdkResourceDeclarationKind
{
    BackgroundPalette,
    SpritePalette,
    RawPalette,
    RawObjectPalette,
    WorldLoad,
    WorldColumn,
    WorldFlags,
    WorldMap,
    SpriteAsset,
    MusicAsset,
    SoundEffectAsset,
    AnimationClip,
    TilemapSet,
    TilemapFill,
    HudSetTile,
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
            ["palette_set"] = new SdkResourceDeclarationDescriptor("palette_set", SdkResourceDeclarationKind.RawPalette),
            ["object_palette_set"] = new SdkResourceDeclarationDescriptor("object_palette_set", SdkResourceDeclarationKind.RawObjectPalette),
            ["world_load"] = new SdkResourceDeclarationDescriptor("world_load", SdkResourceDeclarationKind.WorldLoad),
            ["world_column"] = new SdkResourceDeclarationDescriptor("world_column", SdkResourceDeclarationKind.WorldColumn),
            ["world_flags"] = new SdkResourceDeclarationDescriptor("world_flags", SdkResourceDeclarationKind.WorldFlags),
            ["world_map"] = new SdkResourceDeclarationDescriptor("world_map", SdkResourceDeclarationKind.WorldMap),
            ["sprite_asset"] = new SdkResourceDeclarationDescriptor("sprite_asset", SdkResourceDeclarationKind.SpriteAsset),
            ["music_asset"] = new SdkResourceDeclarationDescriptor("music_asset", SdkResourceDeclarationKind.MusicAsset),
            ["sfx_asset"] = new SdkResourceDeclarationDescriptor("sfx_asset", SdkResourceDeclarationKind.SoundEffectAsset),
            ["animation_clip"] = new SdkResourceDeclarationDescriptor("animation_clip", SdkResourceDeclarationKind.AnimationClip),
            ["tilemap_set"] = new SdkResourceDeclarationDescriptor("tilemap_set", SdkResourceDeclarationKind.TilemapSet),
            ["tilemap_fill"] = new SdkResourceDeclarationDescriptor("tilemap_fill", SdkResourceDeclarationKind.TilemapFill),
            ["hud_set_tile"] = new SdkResourceDeclarationDescriptor("hud_set_tile", SdkResourceDeclarationKind.HudSetTile),
        };
}
