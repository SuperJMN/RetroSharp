namespace RetroSharp.NES.Tests;

using RetroSharp.FunctionalAcceptance;
using Xunit;
using Xunit.Abstractions;

public sealed class PackedTiledFunctionalAcceptanceTests(ITestOutputHelper output)
{
    private const ushort RequestCount = 0x0370;
    private const ushort ResidentCount = 0x0372;
    private const ushort CommitCount = 0x0373;
    private const ushort ReleaseCount = 0x0374;
    private const ushort BankWorkInCommit = 0x0375;
    private const ushort DirectoryWorkInCommit = 0x0376;
    private const ushort DecodeWorkInCommit = 0x0377;
    private const ushort VisibleCameraXLow = 0x03CB;
    private const ushort VisibleCameraXHigh = 0x03CC;
    private const ushort VisibleCameraYLow = 0x03CD;
    private const ushort VisibleCameraYHigh = 0x03CE;
    private const byte ExpectedBottomOverscanInsetPixels = 8;

    public static TheoryData<string, string, string, string, string> ProductionSamples => new()
    {
        { "tiled-hscroll-short", "samples/tiled-hscroll/hscroll-short.rs", "samples/tiled-hscroll/stage1-short.tmj", "samples/tiled-hscroll/hscroll-short.nes", "validation/scenarios/tiled-hscroll-short.nes.json" },
        { "tiled-hscroll-full", "samples/tiled-hscroll/hscroll-full.rs", "samples/tiled-hscroll/stage1-full.tmj", "samples/tiled-hscroll/hscroll-full.nes", "validation/scenarios/tiled-hscroll-full.nes.json" },
        { "tiled-hscroll-offset", "samples/tiled-hscroll/hscroll-offset.rs", "samples/tiled-hscroll/stage1-full.tmj", "samples/tiled-hscroll/hscroll-offset.nes", "validation/scenarios/tiled-hscroll-offset.nes.json" },
        { "tiled-vscroll", "samples/tiled-vscroll/vscroll.rs", "samples/tiled-vscroll/vscroll.tmj", "samples/tiled-vscroll/vscroll.nes", "validation/scenarios/tiled-vscroll.nes.json" },
        { "tiled-free-scroll", "samples/tiled-free-scroll/free-scroll.rs", "samples/tiled-free-scroll/free-scroll.tmj", "samples/tiled-free-scroll/free-scroll.nes", "validation/scenarios/tiled-free-scroll.nes.json" },
        { "deadzone-follow", "samples/deadzone-follow/deadzone.rs", "samples/deadzone-follow/deadzone.tmj", "samples/deadzone-follow/deadzone.nes", "validation/scenarios/deadzone-follow.nes.json" },
    };

    [Fact]
    public void Exact_runner_background_survives_a_short_right_then_left_return_at_nonzero_y()
    {
        var trackedRomPath = RepositoryFile("samples/runner/bin/runner.nes");
        var trackedRom = File.ReadAllBytes(trackedRomPath);
        var regeneratedRom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        Assert.Equal(trackedRom, regeneratedRom);

        var map = NesTiledWorldImporter.Load(
            RepositoryFile("samples/runner/assets/maps/stage1.tmj"),
            NesVideoProgram.FirstSpriteTile + 95);
        var scenario = new FunctionalScenario(
            "runner-nes-nonzero-y-return",
            "runner",
            FunctionalTarget.Nes,
            WarmUpFrames: 500,
            ObservationFrames: 240,
            Inputs:
            [
                new FunctionalInputSpan("right", 501, 110, ["RIGHT"]),
                new FunctionalInputSpan("left", 611, 120, ["LEFT"]),
            ],
            Checkpoints:
            [
                new FunctionalCheckpoint(
                    "bottom-aligned-start",
                    500,
                    new Dictionary<string, long> { ["visibleCameraY"] = 80 }),
            ],
            ExpectedFeatures: new(
                GameplayTicks: true,
                AudioService: false,
                CameraLifecycle: true,
                Background: true,
                SpriteOam: false,
                BankRestoration: true,
                SafeVideoWrites: false),
            Audio: new(ServiceExpectedByDefault: false, AuthoredSilence: []),
            BudgetEvidence: new(
                "fb992fed16d11b3f3b1fb2dadefc07cfbd0fbb72",
                "The exact mapper-4 runner advances one physical NES frame per gameplay tick outside bounded packed-column commits.",
                "The production runner map, camera Y=80, and exact RIGHT-then-LEFT input path retain every visible tile and attribute palette against the authored Tiled oracle."),
            Budgets: new(
                MinimumGameplayTickRatio: 0.95,
                MaximumConsecutiveMissedGameplayTicks: 4,
                MaximumRequestToResidentFrames: 1,
                MaximumRequestToVisibleFrames: 2));
        var factory = new PackedNesMachineFactory();
        var adapter = new NesFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                InputTimeline: true,
                CameraLifecycle: true,
                Background: true,
                BankRestoration: true));

        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact("samples/runner/bin/runner.nes", trackedRom),
            adapter,
            new AuthoredTiledBackgroundOracle(map, factory));

        var trajectory = factory.VisibleCameraByFrame
            .OrderBy(item => item.Key)
            .Select(item => item.Value.X)
            .ToArray();
        Assert.True(trajectory.Max() >= 100, "The regression path must move the camera at least 100 pixels right.");
        var rightmost = Array.IndexOf(trajectory, trajectory.Max());
        Assert.Contains(0, trajectory[(rightmost + 1)..]);
        Assert.True(report.Passed, Diagnostic(report));
    }

    [Theory]
    [MemberData(nameof(ProductionSamples))]
    public void Exact_production_rom_passes_packed_tiled_functional_acceptance(
        string sampleId,
        string sourceRelativePath,
        string mapRelativePath,
        string romRelativePath,
        string scenarioRelativePath)
    {
        var isHorizontalScrollCanary = sampleId.StartsWith("tiled-hscroll-", StringComparison.Ordinal);
        var usesBottomOverscanInset = sampleId is "tiled-hscroll-short" or "tiled-hscroll-full";
        var sourcePath = RepositoryFile(sourceRelativePath);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException($"Could not locate '{sourceRelativePath}'.");
        var trackedRom = File.ReadAllBytes(RepositoryFile(romRelativePath));
        var regeneratedRom = NesRomCompiler.CompileSource(File.ReadAllText(sourcePath), sourceDirectory);
        Assert.Equal(trackedRom, regeneratedRom);

        var map = NesTiledWorldImporter.Load(RepositoryFile(mapRelativePath), NesVideoProgram.FirstSpriteTile);
        Assert.True(map.Width > 0);
        Assert.InRange(map.Height, 1, 60);
        if (isHorizontalScrollCanary)
        {
            Assert.All(map.WorldFlags, flags => Assert.Equal(0, (int)flags));
        }

        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        Assert.Equal(sampleId, scenario.SampleId);
        var factory = new PackedNesMachineFactory();
        var adapter = new NesFunctionalRomAdapter(
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
        Assert.Equal(1, factory.ResetCount);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
        Assert.Equal(0, report.Summary.BankRestorationFailures);
        Assert.Equal(0, report.Summary.UnsafeVideoWrites);
        Assert.Equal(0, report.Summary.UnsafeOamWrites);
        Assert.Equal(0, report.Summary.BackgroundMismatches);
        if (usesBottomOverscanInset)
        {
            var firstCheckpointFrame = scenario.Checkpoints.Min(checkpoint => checkpoint.Frame);
            var appliedScrollY = factory.AppliedScrollYByFrame
                .Where(item => item.Frame >= firstCheckpointFrame)
                .ToArray();
            Assert.NotEmpty(appliedScrollY);
            Assert.All(appliedScrollY, item => Assert.Equal(ExpectedBottomOverscanInsetPixels, item.Y));
        }

        if (sampleId == "tiled-hscroll-offset")
        {
            var observedPpuSpaces = report.FrameEvidence
                .SelectMany(evidence => evidence.Observed.VideoWrites ?? [])
                .Select(write => write.Space)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("nes-ppu-$2000", observedPpuSpaces);
            Assert.Contains("nes-ppu-$2005", observedPpuSpaces);
            Assert.Contains("nes-ppu-$2006", observedPpuSpaces);
            Assert.Contains("nes-ppu-$2007", observedPpuSpaces);

            var cadenceStart = Assert.Single(scenario.Checkpoints).Frame;
            var appliedScrollCadence = factory.AppliedScrollXByFrame
                // The checkpoint frame establishes the starting scroll; the
                // following physical frames are the held-right cadence.
                .Where(item => item.Frame > cadenceStart)
                .ToArray();
            Assert.True(
                appliedScrollCadence.Length > 32,
                "Cadence coverage must cross at least one streamed tile boundary.");
            Assert.All(
                factory.VisibleCameraByFrame.Where(item => item.Key > cadenceStart),
                item => Assert.Equal(80, item.Value.Y));

            var visibleTrajectory = factory.VisibleCameraByFrame
                .Where(item => item.Key > cadenceStart)
                .OrderBy(item => item.Key)
                .Select(item => item.Value.X)
                .ToArray();
            var reversalIndex = Array.IndexOf(visibleTrajectory, 96);
            Assert.True(reversalIndex >= 0, "The non-zero-Y canary must reach its right reversal edge.");
            Assert.Contains(0, visibleTrajectory[(reversalIndex + 1)..]);

            var cadenceDeltas = appliedScrollCadence
                .Zip(appliedScrollCadence.Skip(1))
                .Select(pair => (byte)(pair.Second.X - pair.First.X))
                .ToArray();
            Assert.Contains((byte)1, cadenceDeltas);
            Assert.Contains(byte.MaxValue, cadenceDeltas);
            foreach (var (previous, current) in appliedScrollCadence.Zip(appliedScrollCadence.Skip(1)))
            {
                Assert.True(
                    current.X == previous.X ||
                    current.X == (byte)(previous.X + 1) ||
                    current.X == (byte)(previous.X - 1),
                    $"Successive applied PPUSCROLL X writes must move by at most one pixel after framing; "
                    + $"frame {previous.Frame} was {previous.X}, frame {current.Frame} was {current.X}.");
            }
        }
        Assert.All(
            report.FrameEvidence,
            evidence =>
            {
                foreach (var signal in new[] { "bankWorkInCommit", "directoryWorkInCommit", "decodeWorkInCommit" })
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

    private sealed class PackedNesMachineFactory : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public int ResetCount { get; private set; }

        public Dictionary<int, (int X, int Y)> VisibleCameraByFrame { get; } = [];

        public List<(int Frame, int Y)> AppliedScrollYByFrame { get; } = [];

        public List<(int Frame, int X)> AppliedScrollXByFrame { get; } = [];

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new PackedNesMachine(
                LoadedRom,
                VisibleCameraByFrame,
                AppliedScrollYByFrame,
                AppliedScrollXByFrame,
                resetCount => ResetCount = resetCount);
        }
    }

    private sealed class PackedNesMachine(
        byte[] exactRom,
        IDictionary<int, (int X, int Y)> visibleCameraByFrame,
        ICollection<(int Frame, int Y)> appliedScrollYByFrame,
        ICollection<(int Frame, int X)> appliedScrollXByFrame,
        Action<int> publishResetCount) : IFunctionalRomMachine
    {
        private readonly NesTestCpu cpu = new(exactRom);
        private int lastFrame;
        private int processedPpuWrites;
        private int processedOamWrites;
        private (int X, int Y)? previousRequestedCamera;
        private readonly Dictionary<(int X, int Y), long> requestSequenceByPosition = [];
        private long cameraRequestSequence;
        private long visibleSequence;
        private bool nextScrollWriteIsX = true;

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

        public void Dispose() => publishResetCount(cpu.ResetCount);

        private FunctionalFrameObservation Observe(int frame)
        {
            var visibleCamera = (
                X: Word(VisibleCameraXLow, VisibleCameraXHigh),
                Y: Word(VisibleCameraYLow, VisibleCameraYHigh));
            visibleCameraByFrame[frame] = visibleCamera;
            var rawPpuWrites = cpu.PpuWrites
                .Skip(processedPpuWrites)
                .ToArray();
            appliedScrollYByFrame.Add((frame, cpu.ScrollY));

            foreach (var write in rawPpuWrites.Where(write => write.Register == 0x2005))
            {
                if (nextScrollWriteIsX)
                {
                    appliedScrollXByFrame.Add((frame, write.Value));
                }

                nextScrollWriteIsX = !nextScrollWriteIsX;
            }

            var videoWrites = rawPpuWrites
                // Track the complete background-raster transaction: control,
                // scroll, address, and data. OAM has its own timing stream.
                .Where(write => write.Register is 0x2000 or 0x2005 or 0x2006 or 0x2007)
                .Select(VideoWrite)
                .ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedPpuWrites = cpu.PpuWrites.Count;
            processedOamWrites = cpu.OamWrites.Count;
            var state = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["user0"] = cpu.Ram(0),
                ["user1"] = cpu.Ram(1),
                ["user2"] = cpu.Ram(2),
                ["user3"] = cpu.Ram(3),
                ["user4"] = cpu.Ram(4),
                ["user5"] = cpu.Ram(5),
                ["visibleCameraX"] = visibleCamera.X,
                ["visibleCameraY"] = visibleCamera.Y,
                ["requestCount"] = cpu.Ram(RequestCount),
                ["residentCount"] = cpu.Ram(ResidentCount),
                ["commitCount"] = cpu.Ram(CommitCount),
                ["releaseCount"] = cpu.Ram(ReleaseCount),
                ["bankWorkInCommit"] = cpu.Ram(BankWorkInCommit),
                ["directoryWorkInCommit"] = cpu.Ram(DirectoryWorkInCommit),
                ["decodeWorkInCommit"] = cpu.Ram(DecodeWorkInCommit),
            };
            var bank = new FunctionalBankObservation(
                cpu.CurrentR6Bank,
                cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress),
                cpu.CurrentR6Bank == cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress),
                "nes-prg-r6");
            var camera = CameraObservation(
                (Word(0x00E0, 0x0318), Word(0x00EA, 0x0319)),
                visibleCamera);

            return new FunctionalFrameObservation(
                frame,
                cpu.VBlankWaitCompletions,
                AudioServiceTicks: 0,
                cpu.ResetCount,
                state,
                camera,
                bank,
                CaptureBackground(visibleCamera),
                VideoWrites: videoWrites,
                OamWrites: oamWrites);
        }

        private FunctionalCameraLifecycleObservation CameraObservation(
            (int X, int Y) requested,
            (int X, int Y) visible)
        {
            if (previousRequestedCamera != requested)
            {
                cameraRequestSequence++;
                previousRequestedCamera = requested;
                requestSequenceByPosition[requested] = cameraRequestSequence;
            }

            if (requestSequenceByPosition.TryGetValue(visible, out var matchingRequest))
            {
                visibleSequence = matchingRequest;
            }

            return new FunctionalCameraLifecycleObservation(
                cameraRequestSequence,
                cameraRequestSequence,
                visibleSequence,
                visibleSequence);
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 32 : 33;
            var height = camera.Y % 8 == 0 ? 30 : 31;
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

        private int Word(ushort low, ushort high) => cpu.Ram(low) | (cpu.Ram(high) << 8);

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

        private static (ushort NameTable, int Column, int Row) BackgroundCell(int x, int y)
        {
            var column = Mod(x, 64);
            var row = Mod(y, 60);
            return (
                (ushort)(0x2000 + row / 30 * 0x800 + column / 32 * 0x400),
                column % 32,
                row % 30);
        }

        private FunctionalVideoWriteObservation VideoWrite(NesPpuWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            return new FunctionalVideoWriteObservation(
                $"nes-ppu-${write.Register:X4}",
                write.VramAddress ?? write.Register,
                // Requiring physical VBlank excludes the complete pre-render
                // line, leaving its 341 PPU dots as margin before scanline 0.
                !write.RenderingEnabled || timing.Phase == "vblank",
                timing);
        }

        private FunctionalOamWriteObservation OamWrite(NesOamWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            return new FunctionalOamWriteObservation(
                write.Address,
                !write.RenderingEnabled || timing.Phase == "vblank",
                timing);
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

    private sealed class AuthoredTiledBackgroundOracle(
        NesTiledWorld map,
        PackedNesMachineFactory factory) : IFunctionalFrameOracle
    {
        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var camera = factory.VisibleCameraByFrame[frame];
            var width = camera.X % 8 == 0 ? 32 : 33;
            var height = camera.Y % 8 == 0 ? 30 : 31;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return new FunctionalFrameExpectation(
                frame,
                Enumerable.Range(0, height)
                    .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                    {
                        var authoredX = Mod(startColumn + x, map.Width);
                        var authoredY = Mod(startRow + y, map.Height);
                        var index = authoredY * map.Width + authoredX;
                        var paletteSlot = AuthoredPaletteSlot(startColumn + x, startRow + y);
                        return new FunctionalBackgroundExpectation(
                            $"screen:{x:D2},{y:D2}",
                            map.WorldTileIds[index],
                            PaletteIdentity(
                                paletteSlot,
                                map.BackgroundPalette.Skip(paletteSlot * 4).Take(4).Select(value => (int)value).ToArray()));
                    }))
                    .ToArray());
        }

        private int AuthoredPaletteSlot(int x, int y)
        {
            var baseX = x & ~1;
            var baseY = y & ~1;
            Span<int> counts = stackalloc int[4];
            Span<int> worldCounts = stackalloc int[4];
            Span<int> upperWorldCounts = stackalloc int[4];
            Span<int> lowerRowCounts = stackalloc int[4];
            for (var offsetY = 0; offsetY < 2; offsetY++)
            {
                for (var offsetX = 0; offsetX < 2; offsetX++)
                {
                    var authoredX = Mod(baseX + offsetX, map.Width);
                    var authoredY = Mod(baseY + offsetY, map.Height);
                    var index = authoredY * map.Width + authoredX;
                    var slot = map.WorldPaletteSlots[index];
                    counts[slot]++;
                    if (map.WorldSourceTiles[index] != 0)
                    {
                        worldCounts[slot]++;
                        if (offsetY == 0)
                        {
                            upperWorldCounts[slot]++;
                        }
                    }

                    if (offsetY == 1)
                    {
                        lowerRowCounts[slot]++;
                    }
                }
            }

            var bestSlot = 0;
            for (var slot = 1; slot < counts.Length; slot++)
            {
                if (IsBetterPaletteSlot(slot, bestSlot, counts, worldCounts, upperWorldCounts, lowerRowCounts))
                {
                    bestSlot = slot;
                }
            }

            return bestSlot;
        }

        private static bool IsBetterPaletteSlot(
            int candidate,
            int incumbent,
            ReadOnlySpan<int> counts,
            ReadOnlySpan<int> worldCounts,
            ReadOnlySpan<int> upperWorldCounts,
            ReadOnlySpan<int> lowerRowCounts)
        {
            if (counts[candidate] != counts[incumbent])
            {
                return counts[candidate] > counts[incumbent];
            }

            if (worldCounts[candidate] != worldCounts[incumbent])
            {
                return worldCounts[candidate] > worldCounts[incumbent];
            }

            if (upperWorldCounts[candidate] != upperWorldCounts[incumbent])
            {
                return upperWorldCounts[candidate] > upperWorldCounts[incumbent];
            }

            return lowerRowCounts[candidate] > lowerRowCounts[incumbent];
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
