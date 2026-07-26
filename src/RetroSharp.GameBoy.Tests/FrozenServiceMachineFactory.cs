namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;

internal sealed class FrozenServiceMachineFactory(
    IFunctionalRomMachineFactory inner,
    int frozenFrame,
    int frozenFrames = 1) : IFunctionalRomMachineFactory
{
    public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom) =>
        new FrozenServiceMachine(inner.Create(exactRom), frozenFrame, frozenFrames);

    private sealed class FrozenServiceMachine(
        IFunctionalRomMachine inner,
        int frozenFrame,
        int frozenFrames) : IFunctionalRomMachine
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
                    GameplayTicks = observation.GameplayTicks - skippedFrames,
                    AudioServiceTicks = observation.AudioServiceTicks - skippedFrames,
                };
        }

        public void Dispose() => inner.Dispose();
    }
}
