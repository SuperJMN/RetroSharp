namespace RetroSharp.Sdk;

using RetroSharp.Parser;

public static class SdkImportResolver
{
    public const string Portable2D = "RetroSharp.Portable2D";

    public static void ValidateImports(ProgramSyntax program, SdkLibraryRegistry? registry = null)
    {
        registry ??= SdkLibraryRegistry.Default;
        foreach (var import in program.Imports)
        {
            if (!registry.TryResolve(import.Path, out _))
            {
                throw new InvalidOperationException($"Unknown import '{import.Path}'.");
            }
        }
    }
}
