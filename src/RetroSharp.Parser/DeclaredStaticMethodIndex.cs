namespace RetroSharp.Parser;

public static class DeclaredStaticMethodIndex
{
    public static IReadOnlyDictionary<string, string> Build(ProgramSyntax program)
    {
        var structNames = program.Structs
            .Select(structSyntax => structSyntax.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (structNames.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var candidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            foreach (var structName in structNames)
            {
                var prefix = $"{structName}_";
                if (!function.Name.StartsWith(prefix, StringComparison.Ordinal) || function.Name.Length == prefix.Length)
                {
                    continue;
                }

                var methodName = function.Name[prefix.Length..];
                foreach (var receiverName in ReceiverNames(structName))
                {
                    AddCandidate(candidates, $"{receiverName}.{methodName}", function.Name);
                }
            }
        }

        return candidates
            .Where(candidate => candidate.Value.Count == 1)
            .ToDictionary(
                candidate => candidate.Key,
                candidate => candidate.Value.Single(),
                StringComparer.Ordinal);
    }

    private static IEnumerable<string> ReceiverNames(string structName)
    {
        yield return structName;

        var parts = structName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < parts.Length; index++)
        {
            var dotted = string.Join(".", parts.Skip(index));
            if (!string.Equals(dotted, structName, StringComparison.Ordinal))
            {
                yield return dotted;
            }
        }
    }

    private static void AddCandidate(
        Dictionary<string, HashSet<string>> candidates,
        string key,
        string functionName)
    {
        if (!candidates.TryGetValue(key, out var functions))
        {
            functions = new HashSet<string>(StringComparer.Ordinal);
            candidates.Add(key, functions);
        }

        functions.Add(functionName);
    }
}
