namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;

public sealed class SdkLibrary
{
    private readonly Func<TargetIntrinsicCatalog, string> sourceFactory;

    public SdkLibrary(string importPath, Func<TargetIntrinsicCatalog, string> sourceFactory)
    {
        ImportPath = importPath;
        this.sourceFactory = sourceFactory;
    }

    public string ImportPath { get; }

    public string SourceForTarget(TargetIntrinsicCatalog catalog)
    {
        return sourceFactory(catalog);
    }
}
