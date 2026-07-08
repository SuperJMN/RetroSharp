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

public sealed record SdkResourceDeclarationDescriptor
{
    private readonly SdkResourceDeclarationKind? kind;

    public SdkResourceDeclarationDescriptor(string ResourceId, SdkResourceDeclarationKind Kind)
    {
        this.ResourceId = ResourceId;
        kind = Kind;
    }

    private SdkResourceDeclarationDescriptor(SdkPluginResourceDeclarationDescriptor pluginResource)
    {
        ResourceId = pluginResource.ResourceId;
        PluginResource = pluginResource;
    }

    public string ResourceId { get; }

    public SdkResourceDeclarationKind Kind =>
        kind ?? throw new InvalidOperationException($"SDK resource declaration '{ResourceId}' is plugin-owned and has no built-in resource kind.");

    public bool IsPluginResource => PluginResource is not null;

    public SdkPluginResourceDeclarationDescriptor PluginResource { get; } = null!;

    public static bool TryCreate(string resourceId, out SdkResourceDeclarationDescriptor descriptor)
    {
        return SdkResourceDeclarationRegistry.Default.TryResolve(resourceId, out descriptor);
    }

    public static bool TryCreate(
        string resourceId,
        SdkResourceDeclarationRegistry registry,
        out SdkResourceDeclarationDescriptor descriptor)
    {
        return registry.TryResolve(resourceId, out descriptor);
    }

    public static SdkResourceDeclarationDescriptor Create(string resourceId)
    {
        return SdkResourceDeclarationRegistry.Default.Resolve(resourceId);
    }

    public static SdkResourceDeclarationDescriptor Create(
        string resourceId,
        SdkResourceDeclarationRegistry registry)
    {
        return registry.Resolve(resourceId);
    }

    public static SdkResourceDeclarationDescriptor FromPlugin(SdkPluginResourceDeclarationDescriptor pluginResource)
    {
        return new SdkResourceDeclarationDescriptor(pluginResource);
    }
}

public sealed class SdkResourceDeclarationRegistry
{
    private readonly Dictionary<string, SdkResourceDeclarationDescriptor> descriptors;

    public static SdkResourceDeclarationRegistry Default { get; } = new(BuiltInDescriptors());

    public SdkResourceDeclarationRegistry(IEnumerable<SdkResourceDeclarationDescriptor> descriptors)
    {
        this.descriptors = new Dictionary<string, SdkResourceDeclarationDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (!this.descriptors.TryAdd(descriptor.ResourceId, descriptor))
            {
                throw new InvalidOperationException($"SDK resource declaration '{descriptor.ResourceId}' is registered more than once.");
            }
        }
    }

    public SdkResourceDeclarationRegistry Register(SdkPluginDescriptor plugin)
    {
        return new SdkResourceDeclarationRegistry(
            descriptors.Values.Concat(plugin.ResourceDeclarations.Select(SdkResourceDeclarationDescriptor.FromPlugin)));
    }

    public bool TryResolve(string resourceId, out SdkResourceDeclarationDescriptor descriptor)
    {
        return descriptors.TryGetValue(resourceId, out descriptor!);
    }

    public SdkResourceDeclarationDescriptor Resolve(string resourceId)
    {
        return TryResolve(resourceId, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Unknown SDK resource declaration '{resourceId}'.");
    }

    private static IReadOnlyList<SdkResourceDeclarationDescriptor> BuiltInDescriptors()
    {
        return
        [
            new SdkResourceDeclarationDescriptor("palette_background", SdkResourceDeclarationKind.BackgroundPalette),
            new SdkResourceDeclarationDescriptor("palette_sprite", SdkResourceDeclarationKind.SpritePalette),
            new SdkResourceDeclarationDescriptor("palette_set", SdkResourceDeclarationKind.RawPalette),
            new SdkResourceDeclarationDescriptor("object_palette_set", SdkResourceDeclarationKind.RawObjectPalette),
            new SdkResourceDeclarationDescriptor("world_load", SdkResourceDeclarationKind.WorldLoad),
            new SdkResourceDeclarationDescriptor("world_column", SdkResourceDeclarationKind.WorldColumn),
            new SdkResourceDeclarationDescriptor("world_flags", SdkResourceDeclarationKind.WorldFlags),
            new SdkResourceDeclarationDescriptor("world_map", SdkResourceDeclarationKind.WorldMap),
            new SdkResourceDeclarationDescriptor("sprite_asset", SdkResourceDeclarationKind.SpriteAsset),
            new SdkResourceDeclarationDescriptor("music_asset", SdkResourceDeclarationKind.MusicAsset),
            new SdkResourceDeclarationDescriptor("sfx_asset", SdkResourceDeclarationKind.SoundEffectAsset),
            new SdkResourceDeclarationDescriptor("animation_clip", SdkResourceDeclarationKind.AnimationClip),
            new SdkResourceDeclarationDescriptor("tilemap_set", SdkResourceDeclarationKind.TilemapSet),
            new SdkResourceDeclarationDescriptor("tilemap_fill", SdkResourceDeclarationKind.TilemapFill),
            new SdkResourceDeclarationDescriptor("hud_set_tile", SdkResourceDeclarationKind.HudSetTile),
        ];
    }
}
