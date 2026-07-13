namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;
using Xunit;

public sealed class SimpleSampleFunctionalAcceptanceTests
{
    private const ushort UserVariableStart = 0xC000;
    private const ushort CameraXLow = 0xC0E0;
    private const ushort CameraXHigh = 0xC0E1;
    private const ushort CameraYLow = 0xC0E8;
    private const ushort CameraYHigh = 0xC0E9;

    public static TheoryData<string, string, string, string?> ProductionSamples => new()
    {
        {
            "static-drawing",
            "samples/static-drawing/drawing.rs",
            "validation/scenarios/static-drawing.gb.json",
            "samples/static-drawing/drawing.gb"
        },
        {
            "cross-target-camera",
            "samples/cross-target-camera/camera.rs",
            "validation/scenarios/cross-target-camera.gb.json",
            null
        },
        {
            "source-vscroll",
            "samples/source-vscroll/vscroll.rs",
            "validation/scenarios/source-vscroll.gb.json",
            null
        },
        {
            "source-free-scroll",
            "samples/source-free-scroll/freescroll.rs",
            "validation/scenarios/source-free-scroll.gb.json",
            "samples/source-free-scroll/freescroll.gb"
        },
        {
            "window-hud",
            "samples/window-hud/hud.rs",
            "validation/scenarios/window-hud.gb.json",
            null
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
        var firstRom = GameBoyRomCompiler.CompileSource(source, sourceDirectory);
        var regeneratedRom = GameBoyRomCompiler.CompileSource(source, sourceDirectory);
        Assert.Equal(firstRom, regeneratedRom);
        if (trackedRomRelativePath is not null)
        {
            Assert.Equal(File.ReadAllBytes(RepositoryFile(trackedRomRelativePath)), firstRom);
        }

        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        var factory = new SimpleGameBoyMachineFactory(sampleId);
        var adapter = new GameBoyFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                InputTimeline: true,
                CameraLifecycle: true,
                Background: true,
                VideoWriteTiming: true));
        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact(sourceRelativePath.Replace(".rs", ".gb", StringComparison.Ordinal), firstRom),
            adapter,
            new AuthoredGameBoyBackgroundOracle(sampleId, factory));
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

    private sealed class SimpleGameBoyMachineFactory(string sampleId) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public Dictionary<int, (int X, int Y)> VisibleCameraByFrame { get; } = [];

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new SimpleGameBoyMachine(sampleId, LoadedRom, VisibleCameraByFrame);
        }
    }

    private sealed class SimpleGameBoyMachine(
        string sampleId,
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
        private (int X, int Y)? previousRequested;
        private readonly Dictionary<(int X, int Y), long> requestSequenceByPosition = [];
        private long requestSequence;
        private long visibleSequence;

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
            var visibleCamera = (X: (int)cpu.IoRegister(0xFF43), Y: (int)cpu.IoRegister(0xFF42));
            visibleCameraByFrame[frame] = visibleCamera;
            var requestedCamera = (X: Word(CameraXLow, CameraXHigh), Y: Word(CameraYLow, CameraYHigh));
            var camera = CameraObservation(requestedCamera, visibleCamera);
            var videoWrites = cpu.VramWrites.Skip(processedVramWrites).Select(VideoWrite).ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedVramWrites = cpu.VramWrites.Count;
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
                ["displayEnabled"] = (cpu.IoRegister(0xFF40) & 0x80) != 0 ? 1 : 0,
                ["windowEnabled"] = (cpu.IoRegister(0xFF40) & 0x20) != 0 ? 1 : 0,
                ["windowX"] = cpu.IoRegister(0xFF4B),
                ["windowY"] = cpu.IoRegister(0xFF4A),
            };

            return new FunctionalFrameObservation(
                frame,
                cpu.VBlankWaitCompletions,
                cpu.AudioUpdateCalls,
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
            if (sampleId is "static-drawing" or "window-hud")
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
                visibleSequence,
                visibleSequence,
                visibleSequence);
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            if (sampleId == "window-hud")
            {
                return Enumerable.Range(0, 4)
                    .SelectMany(x => new[]
                    {
                        new FunctionalBackgroundObservation($"background:{x:D2},00", cpu.Vram((ushort)(0x9800 + x)), cpu.IoRegister(0xFF47)),
                        new FunctionalBackgroundObservation($"window:{x:D2},00", cpu.Vram((ushort)(0x9C00 + x)), cpu.IoRegister(0xFF47)),
                    })
                    .ToArray();
            }

            var width = sampleId == "static-drawing" || camera.X % 8 == 0 ? 20 : 21;
            var height = sampleId == "static-drawing" || camera.Y % 8 == 0 ? 18 : 19;
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

        private long SourceCameraX() => sampleId switch
        {
            "cross-target-camera" => cpu.Wram(UserVariableStart),
            "source-free-scroll" => cpu.Wram(UserVariableStart),
            _ => 0,
        };

        private long SourceCameraY() => sampleId switch
        {
            "source-vscroll" => cpu.Wram(UserVariableStart),
            "source-free-scroll" => cpu.Wram(UserVariableStart + 1),
            _ => 0,
        };

        private int Word(ushort low, ushort high) => cpu.Wram(low) | (cpu.Wram(high) << 8);

        private static FunctionalVideoWriteObservation VideoWrite(VramWrite write)
        {
            var timing = Timing(write.Cycles, write.Ly, write.LcdEnabled);
            return new FunctionalVideoWriteObservation("gb-vram", write.Address, write.Applied, timing);
        }

        private static FunctionalOamWriteObservation OamWrite(RetroSharp.GameBoy.Tests.OamWrite write)
        {
            var safe = !write.LcdEnabled || write.Ly >= 144;
            return new FunctionalOamWriteObservation(write.Address, safe, Timing(write.Cycles, write.Ly, write.LcdEnabled));
        }

        private static FunctionalWriteTimingObservation Timing(long cycles, byte ly, bool lcdEnabled) => new(
            cycles,
            ly,
            (int)(cycles % 456),
            !lcdEnabled ? "lcd-off" : ly >= 144 ? "vblank" : "visible",
            lcdEnabled);
    }

    private sealed class AuthoredGameBoyBackgroundOracle(
        string sampleId,
        SimpleGameBoyMachineFactory factory) : IFunctionalFrameOracle
    {
        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var camera = factory.VisibleCameraByFrame[frame];
            if (sampleId == "window-hud")
            {
                var window = new[] { 5, 1, 2, 3 };
                return new FunctionalFrameExpectation(
                    frame,
                    Enumerable.Range(0, 4)
                        .SelectMany(x => new[]
                        {
                            Expected($"background:{x:D2},00", 0),
                            Expected($"window:{x:D2},00", window[x]),
                        })
                        .ToArray());
            }

            var width = sampleId == "static-drawing" || camera.X % 8 == 0 ? 20 : 21;
            var height = sampleId == "static-drawing" || camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            var background = Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                    Expected($"screen:{x:D2},{y:D2}", AuthoredTile(startColumn + x, startRow + y))))
                .ToArray();
            return new FunctionalFrameExpectation(frame, background);
        }

        private int AuthoredTile(int x, int y) => sampleId switch
        {
            "static-drawing" => StaticTile(x, y),
            "cross-target-camera" => y is >= 10 and < 14 ? ((Mod(x, 8) + y - 10) % 5) + 1 : 0,
            "source-vscroll" => Mod(x, 2) == 0 ? (Mod(y, 24) % 5) + 1 : 5 - (Mod(y, 24) % 5),
            "source-free-scroll" => FreeScrollTile(Mod(x, 64), Mod(y, 60)),
            _ => 0,
        };

        private static int StaticTile(int x, int y)
        {
            var face = new Dictionary<(int X, int Y), int>();
            AddRow(face, 4, 7, [1, 1, 1, 1, 1, 1]);
            AddRow(face, 5, 6, [1, 1, 2, 1, 1, 2, 1, 1]);
            AddRow(face, 6, 6, [1, 1, 2, 1, 1, 2, 1, 1]);
            AddRow(face, 7, 6, [1, 1, 1, 1, 1, 1, 1, 1]);
            AddRow(face, 8, 7, [1, 3, 1, 1, 3, 1]);
            AddRow(face, 9, 8, [1, 3, 3, 1]);
            AddRow(face, 12, 7, [5, 4, 4, 4, 4, 5]);
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

        private static FunctionalBackgroundExpectation Expected(string location, int tile) => new(location, tile, 0xE4);

        private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
    }
}
