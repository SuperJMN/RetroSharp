namespace RetroSharp.Core.Sdk;

// Common shape for a single portable SDK operation stream item: either a
// concrete operation to emit, or a call to a named subroutine stream
// collected elsewhere. Sdk2DStreamItem and SdkAudioStreamItem both implement
// this so a single generic reader (SdkStreamReader<TItem, TOperation>) can
// walk either stream without knowing which target or operation kind it is.
public interface ISdkStreamItem<out TOperation>
    where TOperation : class
{
    TOperation? Operation { get; }

    string? SubroutineCallName { get; }
}

// Target-owned diagnostic wording and edge-case policy for a
// SdkStreamReader<TItem, TOperation>. The stack machine that walks an SDK
// operation stream is identical for Game Boy video, Game Boy audio, and NES;
// this record is the one piece that still varies per consumer, and it exists
// so each consumer's current diagnostic message text and edge-case behavior
// survive the move to a shared reader unchanged.
public sealed record SdkStreamReaderDiagnostics(
    string CallPrefix,
    string OperationNoun,
    string StreamNoun,
    bool DescribeLocationByCursor = false,
    bool RequireDeclaredSubroutine = false,
    bool UseOperationWordingAtTopLevel = false);

// Generic pull-based reader over a portable SDK operation stream: a main
// sequence of items plus a per-subroutine-name lookup of secondary sequences.
// This is the single stack machine shared by every target's SDK stream
// reader (Game Boy video, Game Boy audio, NES): it tracks a cursor into the
// current stream, pushes/pops frames when entering and leaving a subroutine,
// and asserts the stream is fully consumed. Target-specific entry points
// (constructing this from a target's program model) stay thin wrappers in
// their own project; only this traversal is target-neutral.
public sealed class SdkStreamReader<TItem, TOperation>
    where TItem : class, ISdkStreamItem<TOperation>
    where TOperation : class
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<TItem>> subroutines;
    private readonly SdkStreamReaderDiagnostics diagnostics;
    private readonly Stack<StreamFrame> stack = [];
    private StreamFrame current;

    public SdkStreamReader(
        IReadOnlyList<TItem> main,
        IReadOnlyDictionary<string, IReadOnlyList<TItem>> subroutines,
        SdkStreamReaderDiagnostics diagnostics)
    {
        this.subroutines = subroutines;
        this.diagnostics = diagnostics;
        current = new StreamFrame("main", main);
    }

    public TOperation ConsumeOperation(string callName)
    {
        if (current.Cursor >= current.Items.Count)
        {
            throw new InvalidOperationException(StreamExhaustedMessage(callName));
        }

        var item = current.Items[current.Cursor];
        if (item.Operation is not { } operation)
        {
            throw new InvalidOperationException(UnexpectedOperationMessage(callName, item.GetType().Name));
        }

        current.Cursor++;
        return operation;
    }

    public TConcrete ConsumeOperation<TConcrete>(string callName)
        where TConcrete : TOperation
    {
        if (current.Cursor >= current.Items.Count)
        {
            throw new InvalidOperationException(StreamExhaustedMessage(callName));
        }

        var item = current.Items[current.Cursor];
        if (item.Operation is not TConcrete typed)
        {
            var actual = item.Operation is { } operation ? operation.GetType().Name : item.GetType().Name;
            throw new InvalidOperationException(
                $"{diagnostics.CallPrefix} call '{callName}' expected {typeof(TConcrete).Name}, got {actual} {DescribeLocation()}.");
        }

        current.Cursor++;
        return typed;
    }

    public void ConsumeSubroutineCall(string name)
    {
        if (current.Cursor >= current.Items.Count)
        {
            return;
        }

        if (current.Items[current.Cursor].SubroutineCallName is not { } markerName)
        {
            return;
        }

        if (!string.Equals(markerName, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{diagnostics.CallPrefix} stream expected subroutine call '{markerName}', got '{name}'.");
        }

        current.Cursor++;
    }

    public void EnterSubroutine(string name)
    {
        if (!subroutines.TryGetValue(name, out var stream))
        {
            if (diagnostics.RequireDeclaredSubroutine)
            {
                throw new InvalidOperationException($"{diagnostics.CallPrefix} program has no subroutine stream named '{name}'.");
            }

            stream = [];
        }

        stack.Push(current);
        current = new StreamFrame(name, stream);
    }

    public void LeaveSubroutine(string name)
    {
        EnsureCurrentConsumed($"{diagnostics.CallPrefix} subroutine '{name}'");
        current = stack.Pop();
    }

    public void EnsureAllConsumed(string context)
    {
        if (stack.Count != 0)
        {
            throw new InvalidOperationException($"{context} finished while {diagnostics.StreamNoun} '{current.Name}' was still active.");
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
        var description = item.Operation is { } operation ? operation.GetType().Name : item.GetType().Name;

        if (stack.Count == 0 && diagnostics.UseOperationWordingAtTopLevel)
        {
            throw new InvalidOperationException(
                $"{context} consumed {current.Cursor} of {current.Items.Count} {diagnostics.OperationNoun}(s); next operation is {description}.");
        }

        throw new InvalidOperationException(
            $"{context} consumed {current.Cursor} of {current.Items.Count} {diagnostics.StreamNoun} item(s) in '{current.Name}'; next item is {description}.");
    }

    private string DescribeLocation() =>
        diagnostics.DescribeLocationByCursor
            ? $"at stream item {current.Cursor}"
            : $"in stream '{current.Name}'";

    private string StreamExhaustedMessage(string callName) =>
        $"{diagnostics.CallPrefix} call '{callName}' has no collected {diagnostics.OperationNoun} {DescribeLocation()}.";

    private string UnexpectedOperationMessage(string callName, string actualTypeName) =>
        $"{diagnostics.CallPrefix} call '{callName}' expected a collected {diagnostics.OperationNoun} {DescribeLocation()}, got {actualTypeName}.";

    private sealed class StreamFrame(string name, IReadOnlyList<TItem> items)
    {
        public string Name { get; } = name;

        public IReadOnlyList<TItem> Items { get; } = items;

        public int Cursor { get; set; }
    }
}
