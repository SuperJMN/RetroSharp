namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;

/// <summary>
/// Test-only observation canary which retains one published camera sequence for a bounded number of frames.
/// The wrapped machine still executes the exact ROM and input timeline unchanged.
/// </summary>
internal sealed class DelayedVisibleCameraMachineFactory(
    IFunctionalRomMachineFactory inner,
    int startFrame,
    int delayedFrames) : IFunctionalRomMachineFactory
{
    public int? InjectedFrame { get; private set; }

    public int? InjectedRequestFrame { get; private set; }

    public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom) =>
        new DelayedVisibleCameraMachine(
            inner.Create(exactRom),
            startFrame,
            delayedFrames,
            (injectedFrame, requestFrame) =>
            {
                InjectedFrame = injectedFrame;
                InjectedRequestFrame = requestFrame;
            });

    private sealed class DelayedVisibleCameraMachine(
        IFunctionalRomMachine inner,
        int startFrame,
        int delayedFrames,
        Action<int, int?> onInjected) : IFunctionalRomMachine
    {
        private long? lastVisible;
        private int remainingDelay;
        private bool hasInjected;
        private readonly Dictionary<long, int> requestFrames = [];

        public FunctionalFrameObservation ObserveInitial()
        {
            var observation = inner.ObserveInitial();
            lastVisible = observation.Camera?.VisibleSequence;
            return observation;
        }

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            var observation = inner.AdvanceFrame(frame, heldInputs);
            if (observation.Camera is not { } camera)
            {
                return observation;
            }

            if (camera.RequestedSequence is { } request)
            {
                requestFrames.TryAdd(request, frame);
            }

            if (!hasInjected && remainingDelay == 0 && frame >= startFrame &&
                camera.VisibleSequence is { } visible &&
                lastVisible is { } previous && visible > previous)
            {
                remainingDelay = delayedFrames;
                hasInjected = true;
                onInjected(frame, requestFrames.TryGetValue(visible, out var requestFrame) ? requestFrame : null);
            }

            if (remainingDelay > 0 && lastVisible is { } retained)
            {
                remainingDelay--;
                return observation with { Camera = camera with { VisibleSequence = retained } };
            }

            lastVisible = camera.VisibleSequence;
            return observation;
        }

        public void Dispose() => inner.Dispose();
    }
}
