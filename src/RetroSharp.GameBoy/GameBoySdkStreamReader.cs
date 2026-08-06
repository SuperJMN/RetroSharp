using RetroSharp.Core.Sdk;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

// Thin Game Boy-owned entry point over the shared SdkStreamReader<TItem, TOperation>
// stack machine (RetroSharp.Core.Sdk). All traversal logic (cursor tracking,
// subroutine frames, exhaustion/mismatch diagnostics) lives in Core; this
// wrapper only knows how to build a reader from a GameBoyVideoProgram and how
// to word its diagnostics.
internal sealed class Sdk2DStreamReader
{
    private static readonly SdkStreamReaderDiagnostics Diagnostics = new(
        CallPrefix: "Game Boy SDK",
        OperationNoun: "SDK operation",
        StreamNoun: "SDK stream");

    private readonly SdkStreamReader<Sdk2DStreamItem, Sdk2DOperation> reader;

    private Sdk2DStreamReader(
        IReadOnlyList<Sdk2DStreamItem> main,
        IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutines)
    {
        reader = new SdkStreamReader<Sdk2DStreamItem, Sdk2DOperation>(main, subroutines, Diagnostics);
    }

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

    public Sdk2DOperation ConsumeOperation(string callName) => reader.ConsumeOperation(callName);

    public void ConsumeSubroutineCall(string name) => reader.ConsumeSubroutineCall(name);

    public void EnterSubroutine(string name) => reader.EnterSubroutine(name);

    public void LeaveSubroutine(string name) => reader.LeaveSubroutine(name);

    public void EnsureAllConsumed(string context) => reader.EnsureAllConsumed(context);
}

// Thin Game Boy-owned entry point over the shared reader for the portable
// audio SDK stream. Identical shape to Sdk2DStreamReader above, parameterized
// over SdkAudioStreamItem/SdkAudioOperation instead.
internal sealed class SdkAudioStreamReader
{
    private static readonly SdkStreamReaderDiagnostics Diagnostics = new(
        CallPrefix: "Game Boy SDK audio",
        OperationNoun: "SDK audio operation",
        StreamNoun: "SDK audio stream");

    private readonly SdkStreamReader<SdkAudioStreamItem, SdkAudioOperation> reader;

    private SdkAudioStreamReader(
        IReadOnlyList<SdkAudioStreamItem> main,
        IReadOnlyDictionary<string, IReadOnlyList<SdkAudioStreamItem>> subroutines)
    {
        reader = new SdkStreamReader<SdkAudioStreamItem, SdkAudioOperation>(main, subroutines, Diagnostics);
    }

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

    public SdkAudioOperation ConsumeOperation(string callName) => reader.ConsumeOperation(callName);

    public void ConsumeSubroutineCall(string name) => reader.ConsumeSubroutineCall(name);

    public void EnterSubroutine(string name) => reader.EnterSubroutine(name);

    public void LeaveSubroutine(string name) => reader.LeaveSubroutine(name);

    public void EnsureAllConsumed(string context) => reader.EnsureAllConsumed(context);
}
