namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public static class TargetProgramSelector
{
    public static ProgramSyntax Select(ProgramSyntax program, TargetIntrinsicCatalog catalog)
    {
        return new ProgramSyntax(
            program.TypeAliases,
            program.Constants,
            program.Enums,
            program.Structs,
            program.Functions.Where(function => MatchesTarget(function, catalog.TargetId)).ToList());
    }

    private static bool MatchesTarget(FunctionSyntax function, string targetId)
    {
        var target = TargetAttributeReader.StringArgument(function, "target");
        return target is null || string.Equals(target, targetId, StringComparison.Ordinal);
    }
}
