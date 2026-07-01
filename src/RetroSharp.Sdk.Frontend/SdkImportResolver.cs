namespace RetroSharp.Sdk;

using RetroSharp.Parser;

public static class SdkImportResolver
{
    public const string Portable2D = "RetroSharp.Portable2D";

    public static void ValidateImports(ProgramSyntax program)
    {
        foreach (var import in program.Imports)
        {
            if (import.Path != Portable2D)
            {
                throw new InvalidOperationException($"Unknown import '{import.Path}'.");
            }
        }
    }

    public static bool ImportsPortable2D(ProgramSyntax program)
    {
        return program.Imports.Any(import => import.Path == Portable2D);
    }
}
