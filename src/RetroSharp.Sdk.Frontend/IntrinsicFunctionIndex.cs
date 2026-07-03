namespace RetroSharp.Sdk;

using RetroSharp.Parser;

public static class IntrinsicFunctionIndex
{
    public static IReadOnlyDictionary<string, string> Build(ProgramSyntax program)
    {
        var candidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var function in program.Functions.Where(function => function.IsExtern))
        {
            var intrinsicId = TargetAttributeReader.StringArgument(function, "intrinsic");
            if (intrinsicId is null)
            {
                continue;
            }

            if (!candidates.TryGetValue(intrinsicId, out var functions))
            {
                functions = new HashSet<string>(StringComparer.Ordinal);
                candidates.Add(intrinsicId, functions);
            }

            functions.Add(function.Name);
        }

        return candidates
            .Where(candidate => candidate.Value.Count == 1)
            .ToDictionary(
                candidate => candidate.Key,
                candidate => candidate.Value.Single(),
                StringComparer.Ordinal);
    }
}
