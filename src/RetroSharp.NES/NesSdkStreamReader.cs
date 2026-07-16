namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal sealed class NesSdkStreamReader(IReadOnlyList<Sdk2DOperation> operations)
{
    private int cursor;

    public TOperation ConsumeOperation<TOperation>(string callName)
        where TOperation : Sdk2DOperation
    {
        if (cursor >= operations.Count)
        {
            throw new InvalidOperationException(
                $"NES SDK call '{callName}' has no collected SDK operation at stream item {cursor}.");
        }

        var operation = operations[cursor];
        if (operation is not TOperation typed)
        {
            throw new InvalidOperationException(
                $"NES SDK call '{callName}' expected {typeof(TOperation).Name}, got {operation.GetType().Name} at stream item {cursor}.");
        }

        cursor++;
        return typed;
    }

    public Sdk2DOperation ConsumeOperation(string callName)
    {
        if (cursor >= operations.Count)
        {
            throw new InvalidOperationException(
                $"NES SDK call '{callName}' has no collected SDK operation at stream item {cursor}.");
        }

        return operations[cursor++];
    }

    public void EnsureAllConsumed(string context)
    {
        if (cursor == operations.Count)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{context} consumed {cursor} of {operations.Count} SDK operation(s); next operation is {operations[cursor].GetType().Name}.");
    }
}
