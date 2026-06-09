using System.Text;

namespace RetroSharp.Parser;

public static class SdkDotCallLowerer
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
    };

    private static readonly Dictionary<string, string> MethodNames = new(StringComparer.Ordinal)
    {
        ["WaitVBlank"] = "wait_vblank",
    };

    public static bool IsKnownModule(string module)
    {
        return ModulePrefixes.ContainsKey(module);
    }

    public static FunctionCall Lower(SdkDotCallSyntax call)
    {
        if (!ModulePrefixes.TryGetValue(call.Module, out var prefix))
        {
            throw new InvalidOperationException($"Unknown SDK module '{call.Module}'.");
        }

        return new FunctionCall($"{prefix}_{MethodName(call.Method)}", call.Parameters);
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
