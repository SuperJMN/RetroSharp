namespace RetroSharp.FunctionalAcceptance;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<FunctionalExecutionSource>))]
public enum FunctionalExecutionSource
{
    [JsonStringEnumMemberName("in-process")]
    InProcess,

    [JsonStringEnumMemberName("gameboy-mcp")]
    GameboyMcp,

    [JsonStringEnumMemberName("nes-mcp")]
    NesMcp,

    [JsonStringEnumMemberName("external-emulator")]
    ExternalEmulator,
}

public sealed record FunctionalAdapterCapabilities(
    bool GameplayTicks = false,
    bool AudioService = false,
    bool InputTimeline = false,
    bool CameraLifecycle = false,
    bool Background = false,
    bool SpriteOam = false,
    bool BankRestoration = false,
    bool VideoWriteTiming = false);

public interface IFunctionalRomMachineFactory
{
    IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom);
}

public interface IFunctionalRomMachine : IDisposable
{
    FunctionalFrameObservation ObserveInitial();

    FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs);
}

public interface IFunctionalFrameOracle
{
    FunctionalFrameExpectation ExpectedFrame(int frame);
}

public interface IFunctionalRomAdapter
{
    FunctionalTarget Target { get; }

    FunctionalExecutionSource ExecutionSource { get; }

    FunctionalAdapterCapabilities Capabilities { get; }

    IFunctionalRomMachine CreateMachine(ReadOnlyMemory<byte> exactRom);
}

public sealed class GameBoyFunctionalRomAdapter : IFunctionalRomAdapter
{
    private readonly IFunctionalRomMachineFactory machineFactory;

    public GameBoyFunctionalRomAdapter(
        IFunctionalRomMachineFactory machineFactory,
        FunctionalAdapterCapabilities capabilities,
        FunctionalExecutionSource executionSource = FunctionalExecutionSource.InProcess)
    {
        ArgumentNullException.ThrowIfNull(machineFactory);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (executionSource == FunctionalExecutionSource.NesMcp)
        {
            throw new ArgumentException("A Game Boy adapter cannot use the NesMcp execution source.", nameof(executionSource));
        }

        this.machineFactory = machineFactory;
        Capabilities = capabilities;
        ExecutionSource = executionSource;
    }

    public FunctionalTarget Target => FunctionalTarget.GameBoy;

    public FunctionalExecutionSource ExecutionSource { get; }

    public FunctionalAdapterCapabilities Capabilities { get; }

    public IFunctionalRomMachine CreateMachine(ReadOnlyMemory<byte> exactRom) => machineFactory.Create(exactRom);
}

public sealed class NesFunctionalRomAdapter : IFunctionalRomAdapter
{
    private readonly IFunctionalRomMachineFactory machineFactory;

    public NesFunctionalRomAdapter(
        IFunctionalRomMachineFactory machineFactory,
        FunctionalAdapterCapabilities capabilities,
        FunctionalExecutionSource executionSource = FunctionalExecutionSource.InProcess)
    {
        ArgumentNullException.ThrowIfNull(machineFactory);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (executionSource == FunctionalExecutionSource.GameboyMcp)
        {
            throw new ArgumentException("A NES adapter cannot use the GameboyMcp execution source.", nameof(executionSource));
        }

        this.machineFactory = machineFactory;
        Capabilities = capabilities;
        ExecutionSource = executionSource;
    }

    public FunctionalTarget Target => FunctionalTarget.Nes;

    public FunctionalExecutionSource ExecutionSource { get; }

    public FunctionalAdapterCapabilities Capabilities { get; }

    public IFunctionalRomMachine CreateMachine(ReadOnlyMemory<byte> exactRom) => machineFactory.Create(exactRom);
}
