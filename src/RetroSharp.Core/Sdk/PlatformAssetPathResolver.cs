namespace RetroSharp.Core.Sdk;

public static class PlatformAssetPathResolver
{
    public static string ResolvePngVariant(string path, string platform)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return ResolveVariant(path, platform);
    }

    public static string ResolveVariant(string path, string platform)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var logicalName = StripKnownPlatformSuffix(fileName);
        foreach (var suffix in PlatformSuffixes(platform))
        {
            foreach (var extensionVariant in ExtensionVariants(extension))
            {
                var candidate = Path.Combine(directory ?? string.Empty, logicalName + suffix + extensionVariant);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return path;
    }

    private static string StripKnownPlatformSuffix(string fileName)
    {
        foreach (var suffix in KnownPlatformSuffixes())
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^suffix.Length];
            }
        }

        return fileName;
    }

    private static IReadOnlyList<string> PlatformSuffixes(string platform)
    {
        return platform.ToLowerInvariant() switch
        {
            "gb" or "gameboy" => [".gb", ".GB", ".gameboy", ".GameBoy"],
            "nes" => [".nes", ".NES"],
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown asset platform."),
        };
    }

    private static IEnumerable<string> ExtensionVariants(string extension)
    {
        yield return extension;

        var lower = extension.ToLowerInvariant();
        if (!lower.Equals(extension, StringComparison.Ordinal))
        {
            yield return lower;
        }

        var upper = extension.ToUpperInvariant();
        if (!upper.Equals(extension, StringComparison.Ordinal)
            && !upper.Equals(lower, StringComparison.Ordinal))
        {
            yield return upper;
        }
    }

    private static IReadOnlyList<string> KnownPlatformSuffixes()
    {
        return [".gb", ".gameboy", ".nes"];
    }
}
