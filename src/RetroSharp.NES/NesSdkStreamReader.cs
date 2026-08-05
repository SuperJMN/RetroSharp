namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

internal sealed class NesSdkStreamReader
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutines;
    private readonly Stack<StreamFrame> stack = [];
    private StreamFrame current;

    public NesSdkStreamReader(Sdk2DProgram program)
        : this(program.Main, program.Subroutines)
    {
    }

    public NesSdkStreamReader(IReadOnlyList<Sdk2DOperation> operations)
        : this(
            operations.Select(operation => (Sdk2DStreamItem)new Sdk2DStreamItem.Op(operation)).ToArray(),
            new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal))
    {
    }

    private NesSdkStreamReader(
        IReadOnlyList<Sdk2DStreamItem> main,
        IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutines)
    {
        this.subroutines = subroutines;
        current = new StreamFrame("main", main);
    }

    public TOperation ConsumeOperation<TOperation>(string callName)
        where TOperation : Sdk2DOperation
    {
        if (current.Cursor >= current.Items.Count)
        {
            throw new InvalidOperationException(
                $"NES SDK call '{callName}' has no collected SDK operation at stream item {current.Cursor}.");
        }

        var item = current.Items[current.Cursor];
        if (item is not Sdk2DStreamItem.Op { Operation: TOperation typed })
        {
            var actual = item is Sdk2DStreamItem.Op op
                ? op.Operation.GetType().Name
                : item.GetType().Name;
            throw new InvalidOperationException(
                $"NES SDK call '{callName}' expected {typeof(TOperation).Name}, got {actual} at stream item {current.Cursor}.");
        }

        current.Cursor++;
        return typed;
    }

    public Sdk2DOperation ConsumeOperation(string callName)
    {
        if (current.Cursor >= current.Items.Count)
        {
            throw new InvalidOperationException(
                $"NES SDK call '{callName}' has no collected SDK operation at stream item {current.Cursor}.");
        }

        var item = current.Items[current.Cursor];
        if (item is not Sdk2DStreamItem.Op op)
        {
            throw new InvalidOperationException(
                $"NES SDK call '{callName}' expected a collected SDK operation at stream item {current.Cursor}, got {item.GetType().Name}.");
        }

        current.Cursor++;
        return op.Operation;
    }

    public void ConsumeSubroutineCall(string name)
    {
        if (current.Cursor >= current.Items.Count ||
            current.Items[current.Cursor] is not Sdk2DStreamItem.CallSubroutine marker)
        {
            return;
        }

        if (!string.Equals(marker.Name, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"NES SDK stream expected subroutine call '{marker.Name}', got '{name}'.");
        }

        current.Cursor++;
    }

    public void EnterSubroutine(string name)
    {
        if (!subroutines.TryGetValue(name, out var stream))
        {
            throw new InvalidOperationException($"NES SDK program has no subroutine stream named '{name}'.");
        }

        stack.Push(current);
        current = new StreamFrame(name, stream);
    }

    public void LeaveSubroutine(string name)
    {
        EnsureCurrentConsumed($"NES SDK subroutine '{name}'");
        current = stack.Pop();
    }

    public void EnsureAllConsumed(string context)
    {
        if (stack.Count != 0)
        {
            throw new InvalidOperationException(
                $"{context} finished while SDK stream '{current.Name}' was still active.");
        }

        EnsureCurrentConsumed(context);
    }

    private void EnsureCurrentConsumed(string context)
    {
        if (current.Cursor == current.Items.Count)
        {
            return;
        }

        var item = current.Items[current.Cursor];
        var description = item is Sdk2DStreamItem.Op op
            ? op.Operation.GetType().Name
            : item.GetType().Name;
        if (stack.Count == 0)
        {
            throw new InvalidOperationException(
                $"{context} consumed {current.Cursor} of {current.Items.Count} SDK operation(s); next operation is {description}.");
        }

        throw new InvalidOperationException(
            $"{context} consumed {current.Cursor} of {current.Items.Count} SDK stream item(s) in '{current.Name}'; next item is {description}.");
    }

    private sealed class StreamFrame(string name, IReadOnlyList<Sdk2DStreamItem> items)
    {
        public string Name { get; } = name;

        public IReadOnlyList<Sdk2DStreamItem> Items { get; } = items;

        public int Cursor { get; set; }
    }
}
