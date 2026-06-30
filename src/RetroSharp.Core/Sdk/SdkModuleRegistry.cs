namespace RetroSharp.Core.Sdk;

using System.Text;

// Owns the portable SDK dot-call naming contract: which module names are SDK
// modules and how `module.Method(...)` maps to a flat `prefix_method` call name.
// This SDK knowledge lives in the SDK layer, not in the target-neutral language
// front-end, which only consumes this contract to lower dot-call syntax.
public static class SdkModuleRegistry
{
    private static readonly SdkModuleDescriptor[] ModuleDescriptors =
    [
        LibraryModule("video", "video"),
        LibraryModule("input", "input"),
        LibraryModule("camera", "camera"),
        LibraryModule("sprites", "sprite"),
        LibraryModule("sprite", "sprite"),
        LibraryModule("palette", "palette"),
        LibraryModule("objectPalette", "object_palette"),
        LibraryModule("tilemap", "tilemap"),
        LibraryModule("map", "map"),
        LibraryModule("world", "world"),
        LibraryModule("hud", "hud"),
        LibraryModule("scroll", "scroll"),
        LibraryModule("animation", "animation"),
        LibraryModule("audio", "audio"),
        LibraryModule("music", "music"),
    ];

    private static readonly Dictionary<string, SdkModuleDescriptor> Modules = ModuleDescriptors
        .ToDictionary(module => module.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> MethodNames = new(StringComparer.Ordinal)
    {
        ["WaitVBlank"] = "wait_vblank",
    };

    public static bool IsKnownModule(string module)
    {
        return Modules.ContainsKey(module);
    }

    public static SdkModuleDescriptor? FindModule(string module)
    {
        return Modules.GetValueOrDefault(module);
    }

    public static bool TryResolveCallName(string module, string method, out string callName)
    {
        if (!Modules.TryGetValue(module, out var descriptor))
        {
            callName = string.Empty;
            return false;
        }

        callName = descriptor.ResolveCallName(method);
        return true;
    }

    internal static string MethodName(string method)
    {
        return MethodNames.TryGetValue(method, out var name)
            ? name
            : ToSnakeCase(method);
    }

    private static SdkModuleDescriptor LibraryModule(string name, string callPrefix)
    {
        return new SdkModuleDescriptor(name, callPrefix, SdkModuleKind.Library);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
