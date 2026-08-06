namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;

// Thin NES-owned entry point over the shared SdkStreamReader<TItem, TOperation>
// stack machine (RetroSharp.Core.Sdk). All traversal logic (cursor tracking,
// subroutine frames, exhaustion/mismatch diagnostics) lives in Core; this
// wrapper only knows how to build a reader from a Sdk2DProgram/flat operation
// list and how to word its diagnostics. Unlike Game Boy's readers, NES
// requires every entered subroutine name to be declared and describes stream
// position by cursor index rather than by stream name.
internal sealed class NesSdkStreamReader
{
    private static readonly SdkStreamReaderDiagnostics Diagnostics = new(
        CallPrefix: "NES SDK",
        OperationNoun: "SDK operation",
        StreamNoun: "SDK stream",
        DescribeLocationByCursor: true,
        RequireDeclaredSubroutine: true,
        UseOperationWordingAtTopLevel: true);

    private readonly SdkStreamReader<Sdk2DStreamItem, Sdk2DOperation> reader;

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
        reader = new SdkStreamReader<Sdk2DStreamItem, Sdk2DOperation>(main, subroutines, Diagnostics);
    }

    public TOperation ConsumeOperation<TOperation>(string callName)
        where TOperation : Sdk2DOperation
        => reader.ConsumeOperation<TOperation>(callName);

    public Sdk2DOperation ConsumeOperation(string callName) => reader.ConsumeOperation(callName);

    public void ConsumeSubroutineCall(string name) => reader.ConsumeSubroutineCall(name);

    public void EnterSubroutine(string name) => reader.EnterSubroutine(name);

    public void LeaveSubroutine(string name) => reader.LeaveSubroutine(name);

    public void EnsureAllConsumed(string context) => reader.EnsureAllConsumed(context);
}
