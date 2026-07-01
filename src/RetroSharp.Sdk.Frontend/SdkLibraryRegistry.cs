namespace RetroSharp.Sdk;

public sealed class SdkLibraryRegistry
{
    private readonly Dictionary<string, SdkLibrary> libraries;

    public static SdkLibraryRegistry Default { get; } = new(
    [
        new SdkLibrary(SdkImportResolver.Portable2D, SdkLibrarySource.ForTarget),
    ]);

    public SdkLibraryRegistry(IEnumerable<SdkLibrary> libraries)
    {
        this.libraries = libraries.ToDictionary(library => library.ImportPath, StringComparer.Ordinal);
    }

    public bool TryResolve(string importPath, out SdkLibrary? library)
    {
        return libraries.TryGetValue(importPath, out library);
    }
}
