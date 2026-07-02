namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;

public sealed class SdkLibrary
{
    private readonly Func<TargetIntrinsicCatalog, SdkLibrarySourceSet> sourceFactory;

    public SdkLibrary(string importPath, Func<TargetIntrinsicCatalog, string> sourceFactory)
        : this(importPath, catalog => SdkLibrarySourceSet.FromSource(sourceFactory(catalog)))
    {
    }

    public SdkLibrary(string importPath, Func<TargetIntrinsicCatalog, SdkLibrarySourceSet> sourceFactory)
    {
        ImportPath = importPath;
        this.sourceFactory = sourceFactory;
    }

    public string ImportPath { get; }

    public string SourceForTarget(TargetIntrinsicCatalog catalog)
    {
        return SourceSetForTarget(catalog).Source;
    }

    public SdkLibrarySourceSet SourceSetForTarget(TargetIntrinsicCatalog catalog)
    {
        return sourceFactory(catalog);
    }
}

public sealed record SdkLibrarySourceSet(string Source, PhysicalNamespaceSourceGroup? PhysicalNamespace)
{
    public static SdkLibrarySourceSet FromSource(string source) => new(source, null);

    public static SdkLibrarySourceSet FromPhysicalNamespace(PhysicalNamespaceSourceGroup sourceGroup) =>
        new(PhysicalNamespaceSourceComposer.Compose(sourceGroup), sourceGroup);
}
