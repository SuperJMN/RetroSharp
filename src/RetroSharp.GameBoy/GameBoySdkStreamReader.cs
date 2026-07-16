using RetroSharp.Core.Sdk;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

internal sealed class Sdk2DStreamReader(
    IReadOnlyList<Sdk2DStreamItem> main,
    IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutines)
{
    private readonly Stack<StreamFrame> stack = [];
    private StreamFrame current = new("main", main);

    public static Sdk2DStreamReader ForProgram(GameBoyVideoProgram program)
    {
        if (program.SubroutineNames.Count == 0)
        {
            return new Sdk2DStreamReader(
                program.SdkOperations.Select(operation => (Sdk2DStreamItem)new Sdk2DStreamItem.Op(operation)).ToArray(),
                new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>());
        }

        return new Sdk2DStreamReader(program.SdkProgram.Main, program.SdkProgram.Subroutines);
    }

    public Sdk2DOperation ConsumeOperation(string callName)
    {
        if (current.Cursor >= current.Items.Count)
        {
            throw new InvalidOperationException($"Game Boy SDK call '{callName}' has no collected SDK operation in stream '{current.Name}'.");
        }

        var item = current.Items[current.Cursor++];
        return item is Sdk2DStreamItem.Op op
            ? op.Operation
            : throw new InvalidOperationException($"Game Boy SDK call '{callName}' expected a collected SDK operation in stream '{current.Name}', got {item.GetType().Name}.");
    }

    public void ConsumeSubroutineCall(string name)
    {
        if (current.Cursor >= current.Items.Count)
        {
            return;
        }

        if (current.Items[current.Cursor] is not Sdk2DStreamItem.CallSubroutine marker)
        {
            return;
        }

        if (!string.Equals(marker.Name, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Game Boy SDK stream expected subroutine call '{marker.Name}', got '{name}'.");
        }

        current.Cursor++;
    }

    public void EnterSubroutine(string name)
    {
        stack.Push(current);
        current = new StreamFrame(
            name,
            subroutines.TryGetValue(name, out var stream)
                ? stream
                : []);
    }

    public void LeaveSubroutine(string name)
    {
        EnsureCurrentConsumed($"Game Boy SDK subroutine '{name}'");
        current = stack.Pop();
    }

    public void EnsureAllConsumed(string context)
    {
        if (stack.Count != 0)
        {
            throw new InvalidOperationException($"{context} finished while SDK stream '{current.Name}' was still active.");
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

internal sealed class SdkAudioStreamReader(
    IReadOnlyList<SdkAudioStreamItem> main,
    IReadOnlyDictionary<string, IReadOnlyList<SdkAudioStreamItem>> subroutines)
{
    private readonly Stack<StreamFrame> stack = [];
    private StreamFrame current = new("main", main);

    public static SdkAudioStreamReader ForProgram(GameBoyVideoProgram program)
    {
        if (program.SubroutineNames.Count == 0)
        {
            return new SdkAudioStreamReader(
                program.SdkAudioOperations.Select(operation => (SdkAudioStreamItem)new SdkAudioStreamItem.Op(operation)).ToArray(),
                new Dictionary<string, IReadOnlyList<SdkAudioStreamItem>>());
        }

        return new SdkAudioStreamReader(program.SdkAudioProgram.Main, program.SdkAudioProgram.Subroutines);
    }

    public SdkAudioOperation ConsumeOperation(string callName)
    {
        if (current.Cursor >= current.Items.Count)
        {
            throw new InvalidOperationException($"Game Boy SDK audio call '{callName}' has no collected SDK audio operation in stream '{current.Name}'.");
        }

        var item = current.Items[current.Cursor++];
        return item is SdkAudioStreamItem.Op op
            ? op.Operation
            : throw new InvalidOperationException($"Game Boy SDK audio call '{callName}' expected a collected SDK audio operation in stream '{current.Name}', got {item.GetType().Name}.");
    }

    public void ConsumeSubroutineCall(string name)
    {
        if (current.Cursor >= current.Items.Count)
        {
            return;
        }

        if (current.Items[current.Cursor] is not SdkAudioStreamItem.CallSubroutine marker)
        {
            return;
        }

        if (!string.Equals(marker.Name, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Game Boy SDK audio stream expected subroutine call '{marker.Name}', got '{name}'.");
        }

        current.Cursor++;
    }

    public void EnterSubroutine(string name)
    {
        stack.Push(current);
        current = new StreamFrame(
            name,
            subroutines.TryGetValue(name, out var stream)
                ? stream
                : []);
    }

    public void LeaveSubroutine(string name)
    {
        EnsureCurrentConsumed($"Game Boy SDK audio subroutine '{name}'");
        current = stack.Pop();
    }

    public void EnsureAllConsumed(string context)
    {
        if (stack.Count != 0)
        {
            throw new InvalidOperationException($"{context} finished while SDK audio stream '{current.Name}' was still active.");
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
        var description = item is SdkAudioStreamItem.Op op
            ? op.Operation.GetType().Name
            : item.GetType().Name;
        throw new InvalidOperationException(
            $"{context} consumed {current.Cursor} of {current.Items.Count} SDK audio stream item(s) in '{current.Name}'; next item is {description}.");
    }

    private sealed class StreamFrame(string name, IReadOnlyList<SdkAudioStreamItem> items)
    {
        public string Name { get; } = name;

        public IReadOnlyList<SdkAudioStreamItem> Items { get; } = items;

        public int Cursor { get; set; }
    }
}
