namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;

internal sealed class FrozenServiceMachineFactory : IFunctionalRomMachineFactory
{
    private readonly IFunctionalRomMachineFactory inner;
    private readonly int frozenFrame;
    private readonly int frozenFrames;
    private readonly bool freezeGameplay;
    private readonly bool freezeAudioService;

    public FrozenServiceMachineFactory(
        IFunctionalRomMachineFactory inner,
        int frozenFrame,
        int frozenFrames = 1,
        bool freezeGameplay = true,
        bool freezeAudioService = true)
    {
        if (!freezeGameplay && !freezeAudioService)
        {
            throw new ArgumentException("At least one observed service must be frozen.", nameof(freezeGameplay));
        }

        this.inner = inner;
        this.frozenFrame = frozenFrame;
        this.frozenFrames = frozenFrames;
        this.freezeGameplay = freezeGameplay;
        this.freezeAudioService = freezeAudioService;
    }

    public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom) =>
        new FrozenServiceMachine(inner.Create(exactRom), frozenFrame, frozenFrames, freezeGameplay, freezeAudioService);

    private sealed class FrozenServiceMachine(
        IFunctionalRomMachine inner,
        int frozenFrame,
        int frozenFrames,
        bool freezeGameplay,
        bool freezeAudioService) : IFunctionalRomMachine
    {
        public FunctionalFrameObservation ObserveInitial() => inner.ObserveInitial();

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            var observation = inner.AdvanceFrame(frame, heldInputs);
            var skippedFrames = Math.Clamp(frame - frozenFrame + 1, 0, frozenFrames);
            return skippedFrames == 0
                ? observation
                : observation with
                {
                    GameplayTicks = freezeGameplay ? observation.GameplayTicks - skippedFrames : observation.GameplayTicks,
                    AudioServiceTicks = freezeAudioService ? observation.AudioServiceTicks - skippedFrames : observation.AudioServiceTicks,
                };
        }

        public void Dispose() => inner.Dispose();
    }
}
