namespace RetroSharp.Sdk;

using Antlr4.Runtime;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public static class SdkLibrarySource
{
    public static string ForTarget(TargetIntrinsicCatalog catalog)
    {
        if (!SdkLibraryRegistry.Default.TryResolve(SdkImportResolver.Portable2D, out var library))
        {
            throw new InvalidOperationException($"Built-in SDK library '{SdkImportResolver.Portable2D}' is not registered.");
        }

        return library!.SourceForTarget(catalog);
    }

    public static string Merge(
        TargetIntrinsicCatalog catalog,
        string source,
        SdkLibraryImportMode importMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? registry = null,
        IReadOnlyList<string>? libraryImportPaths = null)
    {
        if (source.Contains(MarkerName(catalog.TargetId), StringComparison.Ordinal))
        {
            return source;
        }

        registry ??= SdkLibraryRegistry.Default;
        var (imports, importPaths, body) = SplitLeadingImports(source);
        var libraries = importPaths
            .Concat(libraryImportPaths ?? [])
            .Select(importPath => ResolveLibrary(registry, importPath))
            .DistinctBy(library => library.ImportPath)
            .ToList();

        var librarySourceSets = libraries
            .Select(library => library.SourceSetForTarget(catalog))
            .ToList();
        var plainLibrarySource = string.Concat(librarySourceSets
            .Where(sourceSet => sourceSet.PhysicalNamespace is null)
            .Select(sourceSet => sourceSet.Source));
        var physicalLibrarySourceGroups = librarySourceSets
            .Select(sourceSet => sourceSet.PhysicalNamespace)
            .OfType<PhysicalNamespaceSourceGroup>()
            .ToArray();
        if (physicalLibrarySourceGroups.Length == 0)
        {
            return imports + plainLibrarySource + body;
        }

        var rewrittenPhysicalSource = PhysicalNamespaceSourceComposer.Compose(
            physicalLibrarySourceGroups,
            [new PhysicalNamespaceSourceFile("__retrosharp_importer.rs", body)]);
        return imports + plainLibrarySource + rewrittenPhysicalSource;
    }

    private static SdkLibrary ResolveLibrary(SdkLibraryRegistry registry, string importPath)
    {
        if (!registry.TryResolve(importPath, out var library))
        {
            throw new InvalidOperationException($"Unknown import '{importPath}'.");
        }

        return library!;
    }

    private static (string Imports, IReadOnlyList<string> ImportPaths, string Body) SplitLeadingImports(string source)
    {
        var lexer = new RetroSharpLexer(CharStreams.fromString(source));
        var tokens = lexer.GetAllTokens();
        var tokenIndex = 0;
        var importEnd = 0;
        var importPaths = new List<string>();

        while (tokenIndex < tokens.Count && tokens[tokenIndex].Text == "import")
        {
            tokenIndex++;
            var path = "";
            while (tokenIndex < tokens.Count && tokens[tokenIndex].Text != ";")
            {
                path += tokens[tokenIndex].Text;
                tokenIndex++;
            }

            if (tokenIndex >= tokens.Count)
            {
                return (source, [], string.Empty);
            }

            importPaths.Add(path);
            importEnd = tokens[tokenIndex].StopIndex + 1;
            tokenIndex++;
        }

        return (source[..importEnd], importPaths, source[importEnd..]);
    }

    private static string MarkerName(string targetId)
    {
        return $"__retrosharp_sdk_library_{targetId}";
    }
}
