namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal static class NesSdkProgramOperations
{
    public static IReadOnlyList<Sdk2DOperation> Collected(Sdk2DProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return program.Main
            .Concat(program.Subroutines.Values.SelectMany(stream => stream))
            .OfType<Sdk2DStreamItem.Op>()
            .Select(item => item.Operation)
            .ToArray();
    }

    public static IReadOnlyList<Sdk2DOperation> ForRuntimeWork(Sdk2DProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var operations = new List<Sdk2DOperation>();
        Expand(program.Main, program.Subroutines, [], operations);
        return operations;
    }

    private static void Expand(
        IReadOnlyList<Sdk2DStreamItem> stream,
        IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutines,
        HashSet<string> activeSubroutines,
        ICollection<Sdk2DOperation> operations)
    {
        foreach (var item in stream)
        {
            switch (item)
            {
                case Sdk2DStreamItem.Op op:
                    operations.Add(op.Operation);
                    break;
                case Sdk2DStreamItem.CallSubroutine call:
                    if (!subroutines.TryGetValue(call.Name, out var subroutine))
                    {
                        throw new InvalidOperationException(
                            $"NES SDK program references missing subroutine stream '{call.Name}'.");
                    }

                    if (!activeSubroutines.Add(call.Name))
                    {
                        throw new InvalidOperationException(
                            $"Recursive NES SDK subroutine stream '{call.Name}' is not supported.");
                    }

                    try
                    {
                        Expand(subroutine, subroutines, activeSubroutines, operations);
                    }
                    finally
                    {
                        activeSubroutines.Remove(call.Name);
                    }

                    break;
            }
        }
    }
}
