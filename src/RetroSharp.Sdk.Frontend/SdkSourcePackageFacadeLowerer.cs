namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public static class SdkSourcePackageFacadeLowerer
{
    public static ProgramSyntax Lower(ProgramSyntax program)
    {
        var staticMethods = SourcePackageStaticMethods(program);
        return staticMethods.Count == 0
            ? program
            : StaticClassLowerer.LowerStaticCalls(program, staticMethods);
    }

    private static IReadOnlyDictionary<string, string> SourcePackageStaticMethods(ProgramSyntax program)
    {
        var structNames = program.Structs.Select(structSyntax => structSyntax.Name).ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (module, method) in SdkModuleRegistry.SourcePackageOnlyMethodNames)
        {
            var suffix = $"_{module}_{method}";
            var candidates = program.Functions
                .Where(function => function.Name.EndsWith($"_{method}", StringComparison.Ordinal))
                .Where(function => function.Name.EndsWith(suffix, StringComparison.Ordinal))
                .Where(function =>
                {
                    var className = function.Name[..^($"_{method}".Length)];
                    return structNames.Contains(className);
                })
                .Select(function => function.Name)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var preferred = candidates
                .Where(candidate => candidate.StartsWith("RetroSharp_Portable2D_", StringComparison.Ordinal))
                .ToArray();
            var resolved = preferred.Length == 1
                ? preferred[0]
                : candidates.Length == 1
                    ? candidates[0]
                    : null;
            if (resolved is not null)
            {
                map[$"{module}.{method}"] = resolved;
            }
        }

        return map;
    }
}
