namespace RetroSharp.Cli;

/// <summary>
/// Resolves the SDK plugin and library registries the compiler needs from the CLI/project
/// options: known SDK plugin ids become <see cref="RetroSharp.Core.Sdk.SdkPluginDescriptor"/>
/// entries, and library search directories become an <see cref="RetroSharp.Sdk.SdkLibraryRegistry"/>.
/// </summary>
internal static class SdkRegistryResolver
{
    internal static RetroSharp.Core.Sdk.SdkPluginRegistry ResolvePlugins(IReadOnlyList<string> pluginIds)
    {
        var registry = RetroSharp.Core.Sdk.SdkPluginRegistry.Empty;
        foreach (var pluginId in pluginIds)
        {
            registry = registry.Register(CreatePlugin(pluginId));
        }

        return registry;
    }

    internal static RetroSharp.Sdk.SdkLibraryRegistry? ResolveLibraries(IReadOnlyList<string> libraryPaths)
    {
        return libraryPaths.Count == 0
            ? null
            : RetroSharp.Sdk.SdkLibraryRegistry.FromDirectories(libraryPaths);
    }

    private static RetroSharp.Core.Sdk.SdkPluginDescriptor CreatePlugin(string pluginId)
    {
        return pluginId switch
        {
            RetroSharp.Sdk.Plugins.Platformer2D.Platformer2DPlugin.PluginId =>
                RetroSharp.Sdk.Plugins.Platformer2D.Platformer2DPlugin.Create(),
            _ => throw new ArgumentException(
                $"Unknown SDK plugin '{pluginId}'. Known plugins: {RetroSharp.Sdk.Plugins.Platformer2D.Platformer2DPlugin.PluginId}."),
        };
    }
}
