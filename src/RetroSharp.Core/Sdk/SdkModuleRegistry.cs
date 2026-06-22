namespace RetroSharp.Core.Sdk;

using System.Text;

// Owns the portable SDK dot-call naming contract: which module names are SDK
// modules and how `module.Method(...)` maps to a flat `prefix_method` call name.
// This SDK knowledge lives in the SDK layer, not in the target-neutral language
// front-end, which only consumes this contract to lower dot-call syntax.
public static class SdkModuleRegistry
{
    private static readonly Dictionary<string, string> ModulePrefixes = new(StringComparer.Ordinal)
    {
        ["video"] = "video",
        ["input"] = "input",
        ["camera"] = "camera",
        ["sprites"] = "sprite",
        ["sprite"] = "sprite",
        ["palette"] = "palette",
        ["objectPalette"] = "object_palette",
        ["tilemap"] = "tilemap",
        ["map"] = "map",
        ["world"] = "world",
        ["hud"] = "hud",
        ["scroll"] = "scroll",
        ["animation"] = "animation",
        ["audio"] = "audio",
        ["music"] = "music",
    };

    private static readonly Dictionary<string, string> MethodNames = new(StringComparer.Ordinal)
    {
        ["WaitVBlank"] = "wait_vblank",
    };

    public static bool IsKnownModule(string module)
    {
        return ModulePrefixes.ContainsKey(module);
    }

    public static bool TryResolveCallName(string module, string method, out string callName)
    {
        if (!ModulePrefixes.TryGetValue(module, out var prefix))
        {
            callName = string.Empty;
            return false;
        }

        callName = $"{prefix}_{MethodName(method)}";
        return true;
    }

    private static string MethodName(string method)
    {
        return MethodNames.TryGetValue(method, out var name)
            ? name
            : ToSnakeCase(method);
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
