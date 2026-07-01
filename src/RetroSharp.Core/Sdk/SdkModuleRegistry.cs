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
        // Canonical C# PascalCase static facade classes (#157). These are the only
        // accepted SDK dot-call receivers; the earlier transitional lowercase aliases
        // were removed once samples, docs, tests, and the actor-framework lowerer all
        // emitted PascalCase. Each maps to a flat `prefix_method` call name.
        LibraryModule("Video", "video"),
        LibraryModule("Input", "input"),
        LibraryModule("Camera", "camera"),
        LibraryModule("Sprite", "sprite"),
        LibraryModule("Palette", "palette"),
        LibraryModule("ObjectPalette", "object_palette"),
        LibraryModule("Tilemap", "tilemap"),
        LibraryModule("Map", "map"),
        LibraryModule("World", "world"),
        LibraryModule("Hud", "hud"),
        LibraryModule("Scroll", "scroll"),
        LibraryModule("Animation", "animation"),
        LibraryModule("Audio", "audio"),
        LibraryModule("Music", "music"),
    ];

    private static readonly Dictionary<string, SdkModuleDescriptor> Modules = ModuleDescriptors
        .ToDictionary(module => module.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> MethodNames = new(StringComparer.Ordinal)
    {
        ["WaitVBlank"] = "wait_vblank",
    };

    // Per-(module, method) full call-name overrides. The Input predicate facade
    // methods lower to the existing flat button builtins, which use the `button`
    // call prefix rather than the facade's `input` prefix, so lowering stays
    // byte-identical.
    private static readonly Dictionary<(string Module, string Method), string> CallNameOverrides =
        new()
        {
            [("Input", "IsDown")] = "button_down",
            [("Input", "WasPressed")] = "button_just_pressed",
            [("Input", "WasReleased")] = "button_just_released",
            [("Input", "HoldTicks")] = "button_hold_ticks",
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

        callName = CallNameOverrides.TryGetValue((module, method), out var overrideName)
            ? overrideName
            : descriptor.ResolveCallName(method);
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
