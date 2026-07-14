namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;
using Xunit;
using Xunit.Abstractions;

public sealed class PackedTiledFunctionalAcceptanceTests(ITestOutputHelper output)
{
    private const ushort RequestCount = 0xC152;
    private const ushort ResidentCount = 0xC154;
    private const ushort CommitCount = 0xC155;
    private const ushort ReleaseCount = 0xC156;
    private const ushort BankWorkInCommit = 0xC157;
    private const ushort DecodeWorkInCommit = 0xC158;
    private const ushort DirectoryWorkInVBlank = 0xC19C;
    private const ushort VisibleCameraXLow = 0xC14D;
    private const ushort VisibleCameraXHigh = 0xC14E;
    private const ushort VisibleCameraYLow = 0xC14F;
    private const ushort VisibleCameraYHigh = 0xC150;
    private const ushort DirectoryWorkInCommit = 0xC1E9;
    private const ushort DecodeWorkInVBlank = 0xC1EA;

    public static TheoryData<string, string, string, string, string> ProductionSamples => new()
    {
        { "tiled-tall", "samples/tiled-tall/tall.rs", "samples/tiled-tall/tall.tmj", "samples/tiled-tall/tall.gb", "validation/scenarios/tiled-tall.gb.json" },
        { "tiled-vscroll", "samples/tiled-vscroll/vscroll.rs", "samples/tiled-vscroll/vscroll.tmj", "samples/tiled-vscroll/vscroll.gb", "validation/scenarios/tiled-vscroll.gb.json" },
        { "tiled-diagonal", "samples/tiled-diagonal/diag.rs", "samples/tiled-diagonal/diag.tmj", "samples/tiled-diagonal/diag.gb", "validation/scenarios/tiled-diagonal.gb.json" },
        { "tiled-free-scroll", "samples/tiled-free-scroll/free-scroll.rs", "samples/tiled-free-scroll/free-scroll.tmj", "samples/tiled-free-scroll/free-scroll.gb", "validation/scenarios/tiled-free-scroll.gb.json" },
        { "deadzone-follow", "samples/deadzone-follow/deadzone.rs", "samples/deadzone-follow/deadzone.tmj", "samples/deadzone-follow/deadzone.gb", "validation/scenarios/deadzone-follow.gb.json" },
    };

    [Theory]
    [MemberData(nameof(ProductionSamples))]
    public void Exact_production_rom_passes_packed_tiled_functional_acceptance(
        string sampleId,
        string sourceRelativePath,
        string mapRelativePath,
        string romRelativePath,
        string scenarioRelativePath)
    {
        var sourcePath = RepositoryFile(sourceRelativePath);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException($"Could not locate '{sourceRelativePath}'.");
        var trackedRom = File.ReadAllBytes(RepositoryFile(romRelativePath));
        var regeneratedRom = GameBoyRomCompiler.CompileSource(File.ReadAllText(sourcePath), sourceDirectory);
        Assert.Equal(trackedRom, regeneratedRom);

        var map = GameBoyTiledMapImporter.Load(RepositoryFile(mapRelativePath));
        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        Assert.Equal(sampleId, scenario.SampleId);
        var factory = new PackedGameBoyMachineFactory();
        var adapter = new GameBoyFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                CameraLifecycle: true,
                BankRestoration: true,
                Background: true,
                VideoWriteTiming: true));
        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact(romRelativePath, trackedRom),
            adapter,
            new AuthoredTiledBackgroundOracle(map, factory));

        output.WriteLine($"{report.ScenarioId}: {report.Summary}");
        foreach (var check in report.TimingChecks)
        {
            output.WriteLine($"{check.Metric}: observed={check.Observed:0.###} limit={check.Limit:0.###} headroom={check.Headroom:0.###}");
        }

        Assert.True(report.Passed, Diagnostic(report));
        Assert.Equal(trackedRom, factory.LoadedRom);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
        Assert.Equal(0, report.Summary.BankRestorationFailures);
        Assert.Equal(0, report.Summary.UnsafeVideoWrites);
        Assert.Equal(0, report.Summary.UnsafeOamWrites);
        Assert.Equal(0, report.Summary.BackgroundMismatches);
        Assert.All(
            report.FrameEvidence,
            evidence =>
            {
                foreach (var signal in new[]
                         {
                             "bankWorkInCommit",
                             "decodeWorkInCommit",
                             "directoryWorkInVBlank",
                             "directoryWorkInCommit",
                             "decodeWorkInVBlank",
                         })
                {
                    var value = evidence.Observed.StateSignals![signal];
                    Assert.True(value == 0, $"frame={evidence.Observed.Frame} signal={signal} value={value}");
                }
            });
    }

    private static string Diagnostic(FunctionalAcceptanceReport report)
    {
        var failureFrames = report.IntegrityFailures.Select(failure => failure.Frame).Distinct().Take(5).ToHashSet();
        var state = report.FrameEvidence
            .Where(evidence => failureFrames.Contains(evidence.Observed.Frame))
            .Select(evidence => $"frame={evidence.Observed.Frame} "
                + string.Join(',', evidence.Observed.StateSignals!.Select(signal => $"{signal.Key}={signal.Value}")));
        return $"{report.ScenarioId}: {report.Summary}"
            + Environment.NewLine
            + string.Join(Environment.NewLine, report.IntegrityFailures.Take(20))
            + Environment.NewLine
            + string.Join(Environment.NewLine, state);
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

    private sealed class PackedGameBoyMachineFactory : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public Dictionary<int, (int X, int Y)> VisibleCameraByFrame { get; } = [];

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new PackedGameBoyMachine(LoadedRom, VisibleCameraByFrame);
        }
    }

    private sealed class PackedGameBoyMachine(
        byte[] exactRom,
        IDictionary<int, (int X, int Y)> visibleCameraByFrame) : IFunctionalRomMachine
    {
        private readonly GameBoyTestCpu cpu = new(exactRom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        private int lastFrame;
        private int processedVramWrites;
        private int processedOamWrites;
        private byte previousRequestCount;
        private byte previousResidentCount;
        private byte previousCommitCount;
        private long requestSequence;
        private long residentSequence;
        private long commitSequence;

        public FunctionalFrameObservation ObserveInitial() => Observe(0);

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            if (frame != lastFrame + 1)
            {
                throw new InvalidOperationException($"Expected frame {lastFrame + 1}, received {frame}.");
            }

            cpu.Held.Clear();
            cpu.Held.UnionWith(heldInputs.Select(button => button.ToLowerInvariant()));
            cpu.RunFrames(frame);
            lastFrame = frame;
            return Observe(frame);
        }

        public void Dispose()
        {
        }

        private FunctionalFrameObservation Observe(int frame)
        {
            var visibleCamera = (X: Word(VisibleCameraXLow, VisibleCameraXHigh), Y: Word(VisibleCameraYLow, VisibleCameraYHigh));
            visibleCameraByFrame[frame] = visibleCamera;
            var videoWrites = cpu.VramWrites.Skip(processedVramWrites).Select(VideoWrite).ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedVramWrites = cpu.VramWrites.Count;
            processedOamWrites = cpu.OamWrites.Count;
            var state = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["user0"] = cpu.Wram(0xC000),
                ["user1"] = cpu.Wram(0xC001),
                ["user2"] = cpu.Wram(0xC002),
                ["user3"] = cpu.Wram(0xC003),
                ["user4"] = cpu.Wram(0xC004),
                ["user5"] = cpu.Wram(0xC005),
                ["visibleCameraX"] = visibleCamera.X,
                ["visibleCameraY"] = visibleCamera.Y,
                ["requestCount"] = cpu.Wram(RequestCount),
                ["residentCount"] = cpu.Wram(ResidentCount),
                ["commitCount"] = cpu.Wram(CommitCount),
                ["releaseCount"] = cpu.Wram(ReleaseCount),
                ["bankWorkInCommit"] = cpu.Wram(BankWorkInCommit),
                ["decodeWorkInCommit"] = cpu.Wram(DecodeWorkInCommit),
                ["directoryWorkInVBlank"] = cpu.Wram(DirectoryWorkInVBlank),
                ["directoryWorkInCommit"] = cpu.Wram(DirectoryWorkInCommit),
                ["decodeWorkInVBlank"] = cpu.Wram(DecodeWorkInVBlank),
            };
            var shadowBank = cpu.Wram(GameBoyRomBuilder.ActualVisibleBankAddress);
            var effectiveShadowBank = shadowBank == 0 ? 1 : shadowBank;
            var bank = new FunctionalBankObservation(
                cpu.CurrentRomBank,
                effectiveShadowBank,
                cpu.CurrentRomBank == effectiveShadowBank,
                "gb-mbc1");
            var requested = UpdateSequence(RequestCount, ref previousRequestCount, ref requestSequence);
            var resident = UpdateSequence(ResidentCount, ref previousResidentCount, ref residentSequence);
            var committed = UpdateSequence(CommitCount, ref previousCommitCount, ref commitSequence);
            var camera = new FunctionalCameraLifecycleObservation(requested, resident, committed, committed);

            return new FunctionalFrameObservation(
                frame,
                cpu.SourceWaitCompletions,
                cpu.AudioUpdateCalls,
                cpu.ResetCount,
                state,
                camera,
                bank,
                CaptureBackground(visibleCamera),
                VideoWrites: videoWrites,
                OamWrites: oamWrites);
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                {
                    var address = (ushort)(0x9800 + ((startRow + y) & 31) * 32 + ((startColumn + x) & 31));
                    return new FunctionalBackgroundObservation(
                        $"screen:{x:D2},{y:D2}",
                        cpu.Vram(address),
                        cpu.IoRegister(0xFF47));
                }))
                .ToArray();
        }

        private int Word(ushort low, ushort high) => cpu.Wram(low) | (cpu.Wram(high) << 8);

        private long? UpdateSequence(ushort address, ref byte previous, ref long sequence)
        {
            var current = cpu.Wram(address);
            sequence += (byte)(current - previous);
            previous = current;
            return sequence == 0 ? null : sequence;
        }

        private static FunctionalVideoWriteObservation VideoWrite(VramWrite write) => new(
            "gb-vram",
            write.Address,
            write.Applied,
            Timing(write.Cycles, write.Ly, write.LcdEnabled));

        private static FunctionalOamWriteObservation OamWrite(RetroSharp.GameBoy.Tests.OamWrite write) => new(
            write.Address,
            !write.LcdEnabled || write.Ly >= 144,
            Timing(write.Cycles, write.Ly, write.LcdEnabled));

        private static FunctionalWriteTimingObservation Timing(long cycles, byte ly, bool lcdEnabled) => new(
            cycles,
            ly,
            (int)(cycles % 456),
            !lcdEnabled ? "lcd-off" : ly >= 144 ? "vblank" : "visible",
            lcdEnabled);
    }

    private sealed class AuthoredTiledBackgroundOracle(
        GameBoyTiledMap map,
        PackedGameBoyMachineFactory factory) : IFunctionalFrameOracle
    {
        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var camera = factory.VisibleCameraByFrame[frame];
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return new FunctionalFrameExpectation(
                frame,
                Enumerable.Range(0, height)
                    .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                        new FunctionalBackgroundExpectation(
                            $"screen:{x:D2},{y:D2}",
                            AuthoredTile(startColumn + x, startRow + y),
                            0xE4)))
                    .ToArray());
        }

        private int AuthoredTile(int x, int y)
        {
            var wrappedX = x % map.Width;
            var wrappedY = y % map.Height;
            return map.WorldTileIds[wrappedY * map.Width + wrappedX];
        }
    }
}
