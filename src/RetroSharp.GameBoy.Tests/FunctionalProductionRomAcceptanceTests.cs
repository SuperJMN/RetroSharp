namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;
using Xunit;

public sealed class FunctionalProductionRomAcceptanceTests
{
    private const ushort SourceCameraY = 0xC000;
    private const ushort VisibleCameraYLow = 0xC14F;
    private const ushort VisibleCameraYHigh = 0xC150;
    private const ushort RequestCount = 0xC152;
    private const ushort ResidentCount = 0xC154;
    private const ushort CommitCount = 0xC155;
    private const ushort ReleaseCount = 0xC156;
    private const ushort WorldPackValidationState = 0xC1FB;

    [Fact]
    public void Checked_in_scenario_executes_the_exact_tracked_production_rom_through_the_shared_runner()
    {
        var romPath = RepositoryFile("samples/tiled-vscroll/vscroll.gb");
        var romBytes = File.ReadAllBytes(romPath);
        var scenario = FunctionalScenarioLoader.Load(
            RepositoryFile("validation/scenarios/tiled-vscroll.gb.cadence.json"));
        var factory = new GameBoyTestCpuMachineFactory();
        var adapter = new GameBoyFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                InputTimeline: true,
                CameraLifecycle: true));

        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact("samples/tiled-vscroll/vscroll.gb", romBytes),
            adapter);

        Assert.True(report.Passed, report.ToHumanReadable());
        Assert.Equal("f8a611a63f7a7c17d11816f40c149b860ce3430acbb19418ecd8200e45573df7", report.RomSha256);
        Assert.Equal(romBytes, factory.LoadedRom);
        Assert.Equal(FunctionalExecutionSource.InProcess, report.ExecutionSource);
        Assert.Equal(new FunctionalFrameWindow(20, 70, 90), report.FrameWindow);
        Assert.Equal(70, report.FrameEvidence.Count);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private sealed class GameBoyTestCpuMachineFactory : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new GameBoyTestCpuMachine(LoadedRom);
        }
    }

    private sealed class GameBoyTestCpuMachine : IFunctionalRomMachine
    {
        private readonly GameBoyTestCpu cpu;
        private int lastFrame;
        private byte previousGameplayTick;
        private long gameplayTicks;

        public GameBoyTestCpuMachine(byte[] exactRom)
        {
            cpu = new GameBoyTestCpu(exactRom)
            {
                CycleAccurateLy = true,
                EnforceVblankVramWrites = true,
            };
        }

        public FunctionalFrameObservation ObserveInitial() => Observe(0);

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            if (frame != lastFrame + 1)
            {
                throw new InvalidOperationException($"Expected frame {lastFrame + 1}, received {frame}.");
            }

            cpu.Held.Clear();
            cpu.Held.UnionWith(heldInputs);
            cpu.RunFrames(frame);
            var currentGameplayTick = cpu.Wram(SourceCameraY);
            gameplayTicks += (byte)(currentGameplayTick - previousGameplayTick);
            previousGameplayTick = currentGameplayTick;
            lastFrame = frame;
            return Observe(frame);
        }

        public void Dispose()
        {
        }

        private FunctionalFrameObservation Observe(int frame)
        {
            var camera = new FunctionalCameraLifecycleObservation(
                Sequence(cpu.Wram(RequestCount)),
                Sequence(cpu.Wram(ResidentCount)),
                Sequence(cpu.Wram(CommitCount)),
                Sequence(cpu.Wram(ReleaseCount)));
            return new FunctionalFrameObservation(
                frame,
                gameplayTicks,
                cpu.AudioUpdateCalls,
                cpu.ResetCount,
                new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    ["sourceCameraY"] = cpu.Wram(SourceCameraY),
                    ["visibleCameraY"] = cpu.Wram(VisibleCameraYLow) | (cpu.Wram(VisibleCameraYHigh) << 8),
                    ["scy"] = cpu.IoRegister(0xFF42),
                    ["worldPackValidationState"] = cpu.Wram(WorldPackValidationState),
                },
                camera);
        }

        private static long? Sequence(byte value) => value == 0 ? null : value;
    }
}
