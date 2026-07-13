namespace RetroSharp.NES.Tests;

using RetroSharp.FunctionalAcceptance;
using Xunit;

public sealed class SimpleSampleFunctionalAcceptanceTests
{
    private const ushort CameraX = 0x00E0;
    private const ushort CameraY = 0x00EA;
    private static readonly int ExpectedBackgroundPaletteIdentity = PaletteIdentity(0, [0x0F, 0x27, 0x16, 0x30]);

    [Fact]
    public void Reset_vector_reentry_is_observed_as_an_additional_reset()
    {
        var rom = new byte[16 + (32 * 1_024) + (8 * 1_024)];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 2;
        rom[5] = 1;
        rom[16] = 0x4C; // JMP $8000
        rom[17] = 0x00;
        rom[18] = 0x80;
        rom[16 + 0x7FFC] = 0x00;
        rom[16 + 0x7FFD] = 0x80;
        var cpu = new NesTestCpu(rom);

        cpu.RunFrames(1);

        Assert.True(cpu.ResetCount > 1);
    }

    [Fact]
    public void Six_hundred_rendered_frames_do_not_accumulate_a_rounded_ntsc_frame_drift()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;
        var cpu = new NesTestCpu(NesRomCompiler.CompileSource(source));

        cpu.RunFrames(600);

        Assert.InRange(cpu.Cycles, 17_868_250, 17_868_400);
    }

    public static TheoryData<string, string, string, string?> ProductionSamples => new()
    {
        {
            "static-drawing",
            "samples/static-drawing/drawing.rs",
            "validation/scenarios/static-drawing.nes.json",
            "samples/static-drawing/drawing.nes"
        },
        {
            "cross-target-camera",
            "samples/cross-target-camera/camera.rs",
            "validation/scenarios/cross-target-camera.nes.json",
            null
        },
        {
            "source-free-scroll",
            "samples/source-free-scroll/freescroll.rs",
            "validation/scenarios/source-free-scroll.nes.json",
            "samples/source-free-scroll/freescroll.nes"
        },
    };

    [Theory]
    [MemberData(nameof(ProductionSamples))]
    public void Production_rom_passes_the_shared_functional_scenario(
        string sampleId,
        string sourceRelativePath,
        string scenarioRelativePath,
        string? trackedRomRelativePath)
    {
        var sourcePath = RepositoryFile(sourceRelativePath);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException($"Could not locate the directory for '{sourceRelativePath}'.");
        var source = File.ReadAllText(sourcePath);
        var firstRom = NesRomCompiler.CompileSource(source, sourceDirectory);
        var regeneratedRom = NesRomCompiler.CompileSource(source, sourceDirectory);
        Assert.Equal(firstRom, regeneratedRom);
        if (trackedRomRelativePath is not null)
        {
            Assert.Equal(File.ReadAllBytes(RepositoryFile(trackedRomRelativePath)), firstRom);
        }

        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        var factory = new SimpleNesMachineFactory(sampleId);
        var adapter = new NesFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                InputTimeline: true,
                CameraLifecycle: true,
                Background: true,
                VideoWriteTiming: true));
        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact(sourceRelativePath.Replace(".rs", ".nes", StringComparison.Ordinal), firstRom),
            adapter,
            new AuthoredNesBackgroundOracle(sampleId, factory));
        var failureFrames = report.IntegrityFailures
            .Select(failure => failure.Frame)
            .Distinct()
            .Take(5)
            .ToHashSet();
        var failureState = report.FrameEvidence
            .Where(evidence => failureFrames.Contains(evidence.Observed.Frame))
            .Select(evidence =>
                $"frame={evidence.Observed.Frame} "
                + string.Join(",", evidence.Observed.StateSignals?.Select(signal => $"{signal.Key}={signal.Value}") ?? []));

        Assert.True(
            report.Passed,
            $"{report.ScenarioId}: {report.Summary}{Environment.NewLine}"
            + string.Join(Environment.NewLine, report.IntegrityFailures.Take(20))
            + Environment.NewLine
            + string.Join(Environment.NewLine, failureState));
        Assert.Equal(firstRom, factory.LoadedRom);
        Assert.Equal(1, factory.ResetCount);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
        Assert.Equal(scenario.ObservationFrames, report.FrameEvidence.Count);
        if (scenario.ExpectedFeatures.GameplayTicks)
        {
            Assert.Equal(scenario.ObservationFrames, report.Summary.GameplayTicks);
            Assert.Equal(1, report.TimingChecks.Single(check => check.Metric == "gameplay-tick-ratio").Observed);
            Assert.Equal(0, report.TimingChecks.Single(check => check.Metric == "gameplay-missed-streak").Observed);
        }
        Assert.Equal(0, report.Summary.UnsafeVideoWrites);
        Assert.Equal(0, report.Summary.BackgroundMismatches);
        Assert.All(
            report.FrameEvidence.SelectMany(evidence => evidence.Observed.Background ?? []),
            background => Assert.Equal(ExpectedBackgroundPaletteIdentity, background.Palette));
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

    private sealed class SimpleNesMachineFactory(string sampleId) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public int ResetCount { get; private set; }

        public Dictionary<int, (int X, int Y)> VisibleCameraByFrame { get; } = [];

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new SimpleNesMachine(
                sampleId,
                LoadedRom,
                VisibleCameraByFrame,
                resetCount => ResetCount = resetCount);
        }
    }

    private sealed class SimpleNesMachine(
        string sampleId,
        byte[] exactRom,
        IDictionary<int, (int X, int Y)> visibleCameraByFrame,
        Action<int> publishResetCount) : IFunctionalRomMachine
    {
        private readonly NesTestCpu cpu = new(exactRom);
        private int lastFrame;
        private int processedPpuWrites;
        private int processedOamWrites;
        private (int X, int Y)? previousRequested;
        private readonly Dictionary<(int X, int Y), long> requestSequenceByPosition = [];
        private long requestSequence;
        private long visibleSequence;

        public FunctionalFrameObservation ObserveInitial()
        {
            cpu.RunFrames(0);
            return Observe(0);
        }

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            if (frame != lastFrame + 1)
            {
                throw new InvalidOperationException($"Expected frame {lastFrame + 1}, received {frame}.");
            }

            cpu.Held.Clear();
            cpu.Held.UnionWith(heldInputs);
            cpu.RunFrames(frame);
            lastFrame = frame;
            return Observe(frame);
        }

        public void Dispose()
        {
            publishResetCount(cpu.ResetCount);
        }

        private FunctionalFrameObservation Observe(int frame)
        {
            var visibleCamera = VisibleCamera();
            visibleCameraByFrame[frame] = visibleCamera;
            var requestedCamera = (X: (int)cpu.Ram(CameraX), Y: (int)cpu.Ram(CameraY));
            var camera = CameraObservation(requestedCamera, visibleCamera);
            var videoWrites = cpu.PpuWrites
                .Skip(processedPpuWrites)
                .Where(write => write.Register == 0x2007)
                .Select(VideoWrite)
                .ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedPpuWrites = cpu.PpuWrites.Count;
            processedOamWrites = cpu.OamWrites.Count;
            var state = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["sourceTick"] = cpu.VBlankWaitCompletions,
                ["sourceCameraX"] = SourceCameraX(),
                ["sourceCameraY"] = SourceCameraY(),
                ["requestedCameraX"] = requestedCamera.X,
                ["requestedCameraY"] = requestedCamera.Y,
                ["visibleCameraX"] = visibleCamera.X,
                ["visibleCameraY"] = visibleCamera.Y,
                ["displayEnabled"] = cpu.RenderingEnabled ? 1 : 0,
            };

            return new FunctionalFrameObservation(
                frame,
                cpu.VBlankWaitCompletions,
                AudioServiceTicks: 0,
                cpu.ResetCount,
                state,
                camera,
                Background: CaptureBackground(visibleCamera),
                VideoWrites: videoWrites,
                OamWrites: oamWrites);
        }

        private FunctionalCameraLifecycleObservation? CameraObservation(
            (int X, int Y) requested,
            (int X, int Y) visible)
        {
            if (sampleId == "static-drawing")
            {
                return null;
            }

            if (previousRequested != requested)
            {
                requestSequence++;
                previousRequested = requested;
                requestSequenceByPosition[requested] = requestSequence;
            }

            if (requestSequenceByPosition.TryGetValue(visible, out var matchingRequest))
            {
                visibleSequence = matchingRequest;
            }

            return new FunctionalCameraLifecycleObservation(
                requestSequence,
                requestSequence,
                requestSequence,
                visibleSequence);
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            var width = sampleId == "static-drawing" || camera.X % 8 == 0 ? 32 : 33;
            var height = sampleId == "static-drawing" || camera.Y % 8 == 0 ? 30 : 31;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                    new FunctionalBackgroundObservation(
                        $"screen:{x:D2},{y:D2}",
                        cpu.PpuVram(BackgroundAddress(startColumn + x, startRow + y)),
                        ObservedPaletteIdentity(startColumn + x, startRow + y))))
                .ToArray();
        }

        private (int X, int Y) VisibleCamera()
        {
            if (sampleId == "static-drawing")
            {
                return (0, 0);
            }

            var x = cpu.ScrollX + ((cpu.PpuControl & 0x01) != 0 ? 256 : 0);
            var y = cpu.ScrollY + ((cpu.PpuControl & 0x02) != 0 ? 240 : 0);
            return (x, y);
        }

        private long SourceCameraX() => sampleId switch
        {
            "cross-target-camera" or "source-free-scroll" => cpu.Ram(0),
            _ => 0,
        };

        private long SourceCameraY() => sampleId == "source-free-scroll" ? cpu.Ram(1) : 0;

        private ushort BackgroundAddress(int x, int y)
        {
            var cell = BackgroundCell(x, y);
            return (ushort)(cell.NameTable + cell.Row * 32 + cell.Column);
        }

        private int ObservedPaletteIdentity(int x, int y)
        {
            var cell = BackgroundCell(x, y);
            var attributeAddress = (ushort)(cell.NameTable + 0x3C0 + (cell.Row / 4 * 8) + (cell.Column / 4));
            var shift = ((cell.Row & 2) != 0 ? 4 : 0) + ((cell.Column & 2) != 0 ? 2 : 0);
            var paletteSlot = (cpu.PpuVram(attributeAddress) >> shift) & 0x03;
            var colors = Enumerable.Range(0, 4)
                .Select(offset => (int)cpu.PpuVram((ushort)(0x3F00 + paletteSlot * 4 + offset)))
                .ToArray();
            return PaletteIdentity(paletteSlot, colors);
        }

        private (ushort NameTable, int Column, int Row) BackgroundCell(int x, int y)
        {
            var mapWidth = sampleId == "static-drawing" ? 32 : 64;
            var mapHeight = sampleId == "source-free-scroll" ? 60 : 30;
            var column = Mod(x, mapWidth);
            var row = Mod(y, mapHeight);
            return (
                (ushort)(0x2000 + row / 30 * 0x800 + column / 32 * 0x400),
                column % 32,
                row % 30);
        }

        private FunctionalVideoWriteObservation VideoWrite(NesPpuWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            var safe = !write.RenderingEnabled || timing.Phase == "vblank";
            return new FunctionalVideoWriteObservation("nes-ppudata", write.VramAddress ?? write.Register, safe, timing);
        }

        private FunctionalOamWriteObservation OamWrite(NesOamWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            var safe = !write.RenderingEnabled || timing.Phase == "vblank";
            return new FunctionalOamWriteObservation(write.Address, safe, timing);
        }

        private FunctionalWriteTimingObservation Timing(long cycle, bool renderingEnabled)
        {
            var position = cpu.PpuTiming(cycle, renderingEnabled);
            return new FunctionalWriteTimingObservation(
                cycle,
                position.Scanline,
                position.Dot,
                position.Phase,
                renderingEnabled);
        }
    }

    private sealed class AuthoredNesBackgroundOracle(
        string sampleId,
        SimpleNesMachineFactory factory) : IFunctionalFrameOracle
    {
        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var camera = factory.VisibleCameraByFrame[frame];
            var width = sampleId == "static-drawing" || camera.X % 8 == 0 ? 32 : 33;
            var height = sampleId == "static-drawing" || camera.Y % 8 == 0 ? 30 : 31;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            var background = Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                    new FunctionalBackgroundExpectation(
                        $"screen:{x:D2},{y:D2}",
                        AuthoredTile(startColumn + x, startRow + y),
                        ExpectedBackgroundPaletteIdentity)))
                .ToArray();
            return new FunctionalFrameExpectation(frame, background);
        }

        private int AuthoredTile(int x, int y) => sampleId switch
        {
            "static-drawing" => StaticTile(x, y),
            "cross-target-camera" => y is >= 10 and < 14 ? ((Mod(x, 8) + y - 10) % 5) + 1 : 0,
            "source-free-scroll" => FreeScrollTile(Mod(x, 64), Mod(y, 60)),
            _ => 0,
        };

        private static int StaticTile(int x, int y)
        {
            var face = new Dictionary<(int X, int Y), int>();
            AddRow(face, 10, 13, [1, 1, 1, 1, 1, 1]);
            AddRow(face, 11, 12, [1, 1, 2, 1, 1, 2, 1, 1]);
            AddRow(face, 12, 12, [1, 1, 2, 1, 1, 2, 1, 1]);
            AddRow(face, 13, 12, [1, 1, 1, 1, 1, 1, 1, 1]);
            AddRow(face, 14, 13, [1, 3, 1, 1, 3, 1]);
            AddRow(face, 15, 14, [1, 3, 3, 1]);
            AddRow(face, 18, 13, [5, 4, 4, 4, 4, 5]);
            return face.GetValueOrDefault((x, y));
        }

        private static int FreeScrollTile(int x, int y)
        {
            var offset = x switch
            {
                0 or 63 => 0,
                15 => 1,
                31 => 2,
                32 => 3,
                47 => 4,
                _ => -1,
            };
            return offset < 0 ? 0 : ((y / 3 + offset) % 5) + 1;
        }

        private static void AddRow(IDictionary<(int X, int Y), int> map, int y, int startX, IReadOnlyList<int> tiles)
        {
            for (var index = 0; index < tiles.Count; index++)
            {
                map[(startX + index, y)] = tiles[index];
            }
        }
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private static int PaletteIdentity(int paletteSlot, IReadOnlyList<int> colors) =>
        paletteSlot
        | (colors[0] << 2)
        | (colors[1] << 8)
        | (colors[2] << 14)
        | (colors[3] << 20);
}
