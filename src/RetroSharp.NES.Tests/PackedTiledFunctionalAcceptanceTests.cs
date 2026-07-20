namespace RetroSharp.NES.Tests;

using RetroSharp.FunctionalAcceptance;
using Xunit;
using Xunit.Abstractions;

public sealed class PackedTiledFunctionalAcceptanceTests(ITestOutputHelper output)
{
    private const ushort RequestCount = NesRuntimeMemoryLayout.PackedCamera.RequestCount;
    private const ushort ResidentCount = NesRuntimeMemoryLayout.PackedCamera.ResidentCount;
    private const ushort CommitCount = NesRuntimeMemoryLayout.PackedCamera.CommitCount;
    private const ushort ReleaseCount = NesRuntimeMemoryLayout.PackedCamera.ReleaseCount;
    private const ushort BankWorkInCommit = NesRuntimeMemoryLayout.PackedCamera.BankWorkInCommit;
    private const ushort DirectoryWorkInCommit = NesRuntimeMemoryLayout.PackedCamera.DirectoryWorkInCommit;
    private const ushort DecodeWorkInCommit = NesRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit;
    private const ushort VisibleCameraXLow = NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow;
    private const ushort VisibleCameraXHigh = NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXHigh;
    private const ushort VisibleCameraYLow = NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYLow;
    private const ushort VisibleCameraYHigh = NesRuntimeMemoryLayout.PackedCamera.VisibleCameraYHigh;
    private const byte ExpectedBottomOverscanInsetPixels = 8;

    public static TheoryData<string, string, string, string, string> ProductionSamples => new()
    {
        { "tiled-hscroll-short", "samples/tiled-hscroll/hscroll-short.rs", "samples/tiled-hscroll/stage1-short.tmj", "samples/tiled-hscroll/hscroll-short.nes", "validation/scenarios/tiled-hscroll-short.nes.json" },
        { "tiled-hscroll-full", "samples/tiled-hscroll/hscroll-full.rs", "samples/tiled-hscroll/stage1-full.tmj", "samples/tiled-hscroll/hscroll-full.nes", "validation/scenarios/tiled-hscroll-full.nes.json" },
        { "tiled-hscroll-offset", "samples/tiled-hscroll/hscroll-offset.rs", "samples/tiled-hscroll/stage1-full.tmj", "samples/tiled-hscroll/hscroll-offset.nes", "validation/scenarios/tiled-hscroll-offset.nes.json" },
        { "tiled-vscroll", "samples/tiled-vscroll/vscroll.rs", "samples/tiled-vscroll/vscroll.tmj", "samples/tiled-vscroll/vscroll.nes", "validation/scenarios/tiled-vscroll.nes.json" },
        { "tiled-free-scroll", "samples/tiled-free-scroll/free-scroll.rs", "samples/tiled-free-scroll/free-scroll.tmj", "samples/tiled-free-scroll/free-scroll.nes", "validation/scenarios/tiled-free-scroll.nes.json" },
        { "deadzone-follow", "samples/deadzone-follow/deadzone.rs", "samples/deadzone-follow/deadzone.tmj", "samples/deadzone-follow/deadzone.nes", "validation/scenarios/deadzone-follow.nes.json" },
        { "platformer-landing", "samples/platformer-landing/src/main.rs", "samples/platformer-landing/assets/platformer-landing.tmj", "samples/platformer-landing/bin/platformer-landing.nes", "validation/scenarios/platformer-landing.nes.json" },
    };

    [Fact]
    public void Exact_runner_background_survives_a_short_right_then_left_return_at_nonzero_y()
    {
        var rom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);

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
            new FunctionalRomArtifact("samples/runner/bin/runner.nes", rom),
            adapter,
            new AuthoredTiledBackgroundOracle(map, factory.VisibleCameraByFrame));

        var trajectory = factory.VisibleCameraByFrame
            .OrderBy(item => item.Key)
            .Select(item => item.Value.X)
            .ToArray();
        Assert.True(trajectory.Max() >= 100, "The regression path must move the camera at least 100 pixels right.");
        var rightmost = Array.IndexOf(trajectory, trajectory.Max());
        Assert.Contains(0, trajectory[(rightmost + 1)..]);
        Assert.True(report.Passed, Diagnostic(report));
    }

    [Fact]
    public void Exact_audio_mixed_load_rom_passes_packed_functional_acceptance()
    {
        var sourcePath = RepositoryFile("samples/audio-mixed-load/src/main.rs");
        var sourceDirectory = Path.GetDirectoryName(Path.GetDirectoryName(sourcePath))
            ?? throw new InvalidOperationException("Could not locate the audio-mixed-load sample directory.");
        var source = File.ReadAllText(sourcePath);
        var program = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            sourceDirectory,
            RetroSharp.Sdk.SdkLibraryImportMode.ExplicitOnly,
            null,
            [RetroSharp.Sdk.SdkImportResolver.Portable2D],
            null).VideoProgram;
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            sourceDirectory,
            RetroSharp.Sdk.SdkLibraryImportMode.ExplicitOnly,
            null,
            [RetroSharp.Sdk.SdkImportResolver.Portable2D],
            null);
        var romRelativePath = "samples/audio-mixed-load/bin/audio-mixed-load.nes";
        var rom = build.Rom;
        Assert.Equal("nes-mmc3-tvrom-v1", build.Report.SelectedProfile);
        var soundEffect = Assert.Single(program.SoundEffectAssetsInLoadOrder);
        var soundEffectFrames = 0;
        for (var offset = 0; soundEffect.Data[offset] != 0xFF; soundEffectFrames++)
        {
            offset += 1 + soundEffect.Data[offset] * 2;
        }
        Assert.Equal(9, soundEffectFrames);
        Assert.Equal(30, soundEffect.LingerFrames);
        Assert.Equal(
            1_282,
            Assert.Single(program.MusicAssetsInLoadOrder).DpcmBlocks.Sum(block => block.Data.Length));
        output.WriteLine(
            $"NES SFX lifecycle: registerFrames={soundEffectFrames}, lingerFrames={soundEffect.LingerFrames}");

        var map = program.PackedWorld?.LoweredWorld
            ?? throw new InvalidOperationException("The audio-mixed-load sample must retain its authored packed world.");
        var scenario = FunctionalScenarioLoader.Load(RepositoryFile("validation/scenarios/audio-mixed-load.nes.json"));
        Assert.Equal("audio-mixed-load", scenario.SampleId);
        var factory = new AudioMixedLoadNesMachineFactory(program, build.Report);
        var adapter = new NesFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                AudioService: true,
                AudioProgress: true,
                InputTimeline: true,
                CameraLifecycle: true,
                Background: true,
                SpriteOam: true,
                BankRestoration: true,
                VideoWriteTiming: true));

        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact(romRelativePath, rom),
            adapter,
            new AuthoredTiledBackgroundOracle(map, factory.VisibleCameraByFrame, program, scenario));

        output.WriteLine($"{report.ScenarioId}: {report.Summary}");
        foreach (var check in report.TimingChecks)
        {
            output.WriteLine($"{check.Metric}: observed={check.Observed:0.###} limit={check.Limit:0.###} headroom={check.Headroom:0.###}");
        }
        foreach (var failure in report.IntegrityFailures.Where(failure => failure.Code.StartsWith("audio-", StringComparison.Ordinal)))
        {
            output.WriteLine($"{failure.Code}: {failure.Detail}");
        }
        var apuEvents = report.FrameEvidence
            .SelectMany(evidence => evidence.Observed.AudioProgress?.RegisterEvents ?? [])
            .ToArray();
        Assert.Contains(apuEvents, item => item.Address == 0x4010);
        Assert.Contains(apuEvents, item => item.Address == 0x4012);
        Assert.Contains(apuEvents, item => item.Address == 0x4013);
        Assert.Contains(apuEvents, item => item.Address == 0x4015 && (item.Value & 0x10) != 0);
        Assert.True(report.Passed, Diagnostic(report));
        Assert.Equal(rom, factory.LoadedRom);
        Assert.Equal(1, factory.ResetCount);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
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
        var sourceDirectory = sampleId == "platformer-landing"
            ? Path.GetDirectoryName(Path.GetDirectoryName(sourcePath))
            : Path.GetDirectoryName(sourcePath);
        sourceDirectory = sourceDirectory
            ?? throw new InvalidOperationException($"Could not locate '{sourceRelativePath}'.");
        var source = File.ReadAllText(sourcePath);
        NesVideoProgram? platformerProgram = null;
        NesRomBuildReport? platformerBuildReport = null;
        byte[] regeneratedRom;
        if (sampleId == "platformer-landing")
        {
            platformerProgram = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
                source,
                sourceDirectory,
                RetroSharp.Sdk.SdkLibraryImportMode.ExplicitOnly,
                null,
                [RetroSharp.Sdk.SdkImportResolver.Portable2D],
                null).VideoProgram;
            var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                source,
                sourceDirectory,
                RetroSharp.Sdk.SdkLibraryImportMode.ExplicitOnly,
                null,
                [RetroSharp.Sdk.SdkImportResolver.Portable2D],
                null);
            platformerBuildReport = build.Report;
            regeneratedRom = build.Rom;
        }
        else
        {
            regeneratedRom = NesRomCompiler.CompileSource(source, sourceDirectory);
        }
        var map = platformerProgram?.PackedWorld?.LoweredWorld
            ?? NesTiledWorldImporter.Load(RepositoryFile(mapRelativePath), NesVideoProgram.FirstSpriteTile);
        Assert.True(map.Width > 0);
        Assert.InRange(map.Height, 1, 60);
        if (isHorizontalScrollCanary)
        {
            Assert.All(map.WorldFlags, flags => Assert.Equal(0, (int)flags));
        }

        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        Assert.Equal(sampleId, scenario.SampleId);
        var factory = new PackedNesMachineFactory(platformerProgram, platformerBuildReport);
        var adapter = new NesFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                InputTimeline: sampleId == "platformer-landing",
                CameraLifecycle: true,
                BankRestoration: true,
                Background: true,
                SpriteOam: sampleId == "platformer-landing",
                VideoWriteTiming: true));
        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact(romRelativePath, regeneratedRom),
            adapter,
            new AuthoredTiledBackgroundOracle(map, factory.VisibleCameraByFrame, platformerProgram, scenario));

        output.WriteLine($"{report.ScenarioId}: {report.Summary}");
        foreach (var check in report.TimingChecks)
        {
            output.WriteLine($"{check.Metric}: observed={check.Observed:0.###} limit={check.Limit:0.###} headroom={check.Headroom:0.###}");
        }

        Assert.True(report.Passed, Diagnostic(report));
        Assert.Equal(regeneratedRom, factory.LoadedRom);
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
                // The checkpoint establishes the retained logical camera. The
                // following physical frame is the first hardware publication
                // required to carry the bottom-overscan inset.
                .Where(item => item.Frame > firstCheckpointFrame)
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
                var elapsedPhysicalFrames = current.Frame - previous.Frame;
                var forwardPixels = (byte)(current.X - previous.X);
                var backwardPixels = (byte)(previous.X - current.X);
                Assert.True(
                    Math.Min(forwardPixels, backwardPixels) <= elapsedPhysicalFrames,
                    $"Applied PPUSCROLL X must move by at most one pixel per physical frame after framing; "
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

    private sealed class AudioMixedLoadNesMachineFactory(
        NesVideoProgram program,
        NesRomBuildReport buildReport) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public int ResetCount { get; private set; }

        public Dictionary<int, (int X, int Y)> VisibleCameraByFrame { get; } = [];

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new AudioMixedLoadNesMachine(
                LoadedRom,
                VisibleCameraByFrame,
                program,
                buildReport,
                resetCount => ResetCount = resetCount);
        }
    }

    private sealed class AudioMixedLoadNesMachine(
        byte[] exactRom,
        IDictionary<int, (int X, int Y)> visibleCameraByFrame,
        NesVideoProgram program,
        NesRomBuildReport buildReport,
        Action<int> publishResetCount) : IFunctionalRomMachine
    {
        private readonly NesTestCpu cpu = new(exactRom);
        private readonly IReadOnlyDictionary<string, NesRuntimeUserVariable> variables =
            buildReport.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        private readonly NesCompiledSpriteAsset playerAsset = program.SpriteAssets
            .Single(pair => pair.Key.EndsWith("player_sprite", StringComparison.Ordinal)).Value;
        private readonly NesDpcmPlaybackTracker dpcm = new();
        private int lastFrame;
        private int processedPpuWrites;
        private int processedOamWrites;
        private int processedApuWrites;
        private (int X, int Y)? previousRequestedCamera;
        private readonly Dictionary<(int X, int Y), long> requestSequenceByPosition = [];
        private long requestSequence;
        private long visibleSequence;
        private long registerEventCount;
        private bool previousMusicActive;
        private bool previousSfxActive;
        private long musicStarts;
        private long musicCompletions;
        private long musicRestarts;
        private long sfxStarts;
        private long sfxCompletions;
        private long sfxRestarts;
        private byte previousSourceSfxStarts;
        private byte previousGameplayTick;
        private byte previousAudioTick;
        private long gameplayTicks;
        private long audioTicks;

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
            var rawPpuWrites = cpu.PpuWrites.Skip(processedPpuWrites).ToArray();
            var videoWrites = rawPpuWrites
                .Where(write => write.Register is 0x2000 or 0x2005 or 0x2006 or 0x2007)
                .Select(VideoWrite)
                .ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedPpuWrites = cpu.PpuWrites.Count;
            processedOamWrites = cpu.OamWrites.Count;

            var rawApuWrites = cpu.ApuWrites.Skip(processedApuWrites).Where(IsApuRegister).ToArray();
            var registerEvents = rawApuWrites
                .Select(write => new FunctionalAudioRegisterEvent("nes-apu", write.Register, write.Value))
                .ToArray();
            processedApuWrites = cpu.ApuWrites.Count;
            registerEventCount += registerEvents.Length;
            dpcm.Observe(rawApuWrites, cpu.Cycles);
            var sourceGameplayTick = VariableByte("gameplayTick");
            var sourceAudioTick = VariableByte("audioTick");
            gameplayTicks += (byte)(sourceGameplayTick - previousGameplayTick);
            audioTicks += (byte)(sourceAudioTick - previousAudioTick);
            previousGameplayTick = sourceGameplayTick;
            previousAudioTick = sourceAudioTick;

            var state = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["playerX"] = VariableWord("playerX"),
                ["playerY"] = VariableWord("playerY"),
                ["cameraX"] = VariableWord("cameraX"),
                ["cameraY"] = VariableWord("cameraY"),
                ["grounded"] = VariableByte("grounded"),
                ["patrolX"] = VariableByte("patrolX"),
                ["gameplayTick"] = sourceGameplayTick,
                ["audioTick"] = sourceAudioTick,
                ["sfxCount"] = VariableByte("sfxCount"),
                ["collisionProbeCount"] = VariableByte("collisionProbeCount"),
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

            var musicActive = cpu.Ram(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh) != 0;
            var sfxActive = cpu.Ram(NesRuntimeMemoryLayout.Audio.SfxActive) != 0;
            UpdateLifecycle(
                musicActive,
                previousMusicActive,
                ref musicStarts,
                ref musicCompletions,
                ref musicRestarts);
            var sourceSfxStarts = VariableByte("sfxCount");
            var newSfxStarts = (byte)(sourceSfxStarts - previousSourceSfxStarts);
            sfxStarts += newSfxStarts;
            if (previousSfxActive)
            {
                sfxRestarts += newSfxStarts;
            }
            if (previousSfxActive && !sfxActive)
            {
                sfxCompletions++;
            }
            previousMusicActive = musicActive;
            previousSfxActive = sfxActive;
            previousSourceSfxStarts = sourceSfxStarts;
            state["musicActive"] = musicActive ? 1 : 0;
            state["sfxActive"] = sfxActive ? 1 : 0;
            state["dpcmActive"] = dpcm.Active ? 1 : 0;

            var camera = CameraObservation(
                (Word(NesRuntimeMemoryLayout.Camera.X, NesRuntimeMemoryLayout.Camera.XHigh),
                    Word(NesRuntimeMemoryLayout.Camera.Y, NesRuntimeMemoryLayout.Camera.YHigh)),
                visibleCamera);
            var bank = new FunctionalBankObservation(
                cpu.CurrentR6Bank,
                cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow),
                cpu.CurrentR6Bank == cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow),
                "nes-prg-r6");
            var audioProgress = new FunctionalAudioProgressObservation(
                registerEventCount,
                registerEvents,
                new(musicActive, musicStarts, musicCompletions, musicRestarts),
                new(sfxActive, sfxStarts, sfxCompletions, sfxRestarts),
                new(dpcm.Active, dpcm.Starts, dpcm.Completions, dpcm.Restarts));

            return new FunctionalFrameObservation(
                frame,
                gameplayTicks,
                audioTicks,
                cpu.ResetCount,
                state,
                camera,
                bank,
                CaptureBackground(visibleCamera),
                Sprites: CaptureSprites(),
                VideoWrites: videoWrites,
                OamWrites: oamWrites,
                AudioProgress: audioProgress);
        }

        private FunctionalCameraLifecycleObservation CameraObservation(
            (int X, int Y) requestedCamera,
            (int X, int Y) visibleCamera)
        {
            if (previousRequestedCamera != requestedCamera)
            {
                requestSequence++;
                previousRequestedCamera = requestedCamera;
                requestSequenceByPosition[requestedCamera] = requestSequence;
            }

            if (requestSequenceByPosition.TryGetValue(visibleCamera, out var matchingRequest))
            {
                visibleSequence = matchingRequest;
            }

            return new(requestSequence, requestSequence, visibleSequence, visibleSequence);
        }

        private IReadOnlyList<FunctionalSpriteObservation> CaptureSprites()
        {
            var metaspriteBytes = playerAsset.Pieces.Count * 4;
            var usedBytes = metaspriteBytes * 2;
            var player = Enumerable.Range(0, metaspriteBytes).Select(offset => (int)cpu.Oam((byte)offset)).ToArray();
            var patrol = Enumerable.Range(metaspriteBytes, metaspriteBytes).Select(offset => (int)cpu.Oam((byte)offset)).ToArray();
            var unused = Enumerable.Range(usedBytes, 256 - usedBytes).Select(offset => (int)cpu.Oam((byte)offset)).ToArray();
            return
            [
                new("player", OamVisible(player), player, 0),
                new("patrol", OamVisible(patrol), patrol, playerAsset.Pieces.Count),
                new("unused-oam", OamVisible(unused), unused, playerAsset.Pieces.Count * 2),
            ];
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

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] < 239);

        private FunctionalVideoWriteObservation VideoWrite(NesPpuWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            return new(
                $"nes-ppu-${write.Register:X4}",
                write.VramAddress ?? write.Register,
                !write.RenderingEnabled || timing.Phase == "vblank",
                timing);
        }

        private FunctionalOamWriteObservation OamWrite(NesOamWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            return new(
                write.Address,
                !write.RenderingEnabled || timing.Phase == "vblank",
                timing);
        }

        private FunctionalWriteTimingObservation Timing(long cycle, bool renderingEnabled)
        {
            var position = cpu.PpuTiming(cycle, renderingEnabled);
            return new(cycle, position.Scanline, position.Dot, position.Phase, renderingEnabled);
        }

        private int VariableWord(string name) => VariableByte(name) | (VariableByte(name, 1) << 8);

        private byte VariableByte(string name, int offset = 0) =>
            cpu.Ram(checked((ushort)(variables[name].Address + offset)));

        private int Word(ushort low, ushort high) => cpu.Ram(low) | (cpu.Ram(high) << 8);

        private static bool IsApuRegister(NesApuWrite write) =>
            write.Register is >= 0x4000 and <= 0x4013 or 0x4015 or 0x4017;

        private static void UpdateLifecycle(
            bool active,
            bool previousActive,
            ref long starts,
            ref long completions,
            ref long restarts)
        {
            if (active && !previousActive) starts++;
            if (!active && previousActive) completions++;
            _ = restarts;
        }
    }

    private sealed class NesDpcmPlaybackTracker
    {
        private static readonly int[] NtscPeriods =
            [428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 85, 72, 54];
        private byte control;
        private byte length;
        private long endCycle;

        public bool Active { get; private set; }

        public long Starts { get; private set; }

        public long Completions { get; private set; }

        public long Restarts { get; private set; }

        public void Observe(IReadOnlyList<NesApuWrite> writes, long currentCycle)
        {
            foreach (var write in writes)
            {
                AdvanceTo(write.Cycle);
                switch (write.Register)
                {
                    case 0x4010:
                        control = write.Value;
                        break;
                    case 0x4013:
                        length = write.Value;
                        break;
                    case 0x4015 when (write.Value & 0x10) == 0:
                        if (Active)
                        {
                            Active = false;
                            Completions++;
                        }
                        break;
                    case 0x4015 when !Active:
                        Active = true;
                        Starts++;
                        endCycle = checked(write.Cycle + DurationCycles());
                        break;
                }
            }

            AdvanceTo(currentCycle);
        }

        private void AdvanceTo(long cycle)
        {
            while (Active && cycle >= endCycle)
            {
                Completions++;
                if ((control & 0x40) == 0)
                {
                    Active = false;
                    return;
                }

                endCycle = checked(endCycle + DurationCycles());
            }
        }

        private long DurationCycles() => checked(((long)length * 16 + 1) * 8 * NtscPeriods[control & 0x0F]);
    }

    private sealed class PackedNesMachineFactory(
        NesVideoProgram? platformerProgram = null,
        NesRomBuildReport? platformerBuildReport = null) : IFunctionalRomMachineFactory
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
                platformerProgram,
                platformerBuildReport,
                resetCount => ResetCount = resetCount);
        }
    }

    private sealed class PackedNesMachine(
        byte[] exactRom,
        IDictionary<int, (int X, int Y)> visibleCameraByFrame,
        ICollection<(int Frame, int Y)> appliedScrollYByFrame,
        ICollection<(int Frame, int X)> appliedScrollXByFrame,
        NesVideoProgram? platformerProgram,
        NesRomBuildReport? platformerBuildReport,
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
        private readonly IReadOnlyDictionary<string, NesRuntimeUserVariable> variables =
            platformerBuildReport?.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, NesRuntimeUserVariable>(StringComparer.Ordinal);
        private readonly NesCompiledSpriteAsset? playerAsset = platformerProgram?.SpriteAssets
            .Single(pair => pair.Key.EndsWith("player_sprite", StringComparison.Ordinal)).Value;

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
            AddPlatformerState(state);
            var sprites = CapturePlatformerSprites();
            var bank = new FunctionalBankObservation(
                cpu.CurrentR6Bank,
                cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow),
                cpu.CurrentR6Bank == cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow),
                "nes-prg-r6");
            var camera = CameraObservation(
                (Word(NesRuntimeMemoryLayout.Camera.X, NesRuntimeMemoryLayout.Camera.XHigh),
                    Word(NesRuntimeMemoryLayout.Camera.Y, NesRuntimeMemoryLayout.Camera.YHigh)),
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
                Sprites: sprites,
                VideoWrites: videoWrites,
                OamWrites: oamWrites);
        }

        private void AddPlatformerState(IDictionary<string, long> state)
        {
            if (playerAsset is null)
            {
                return;
            }

            var playerY = VariableWord("player.y");
            state["playerX"] = VariableWord("player.x");
            state["playerY"] = playerY;
            state["worldFootY"] = playerY + 31;
            state["grounded"] = VariableByte("player.grounded");
            state["jumpCount"] = VariableByte("player.jumpCount");
            state["landingCount"] = VariableByte("player.landingCount");
            state["gameplayResetCount"] = VariableByte("player.gameplayResetCount");
            state["supportProbeCount"] = VariableByte("player.supportProbeCount");
            state["wallContactCount"] = VariableByte("player.wallContactCount");
            state["sourceCameraX"] = VariableWord("view.x");
            state["sourceCameraY"] = VariableWord("view.y");
            state["displayEnabled"] = cpu.RenderingEnabled ? 1 : 0;
        }

        private IReadOnlyList<FunctionalSpriteObservation>? CapturePlatformerSprites()
        {
            if (playerAsset is null)
            {
                return null;
            }

            var playerOamBytes = playerAsset.Pieces.Count * 4;
            var actualPlayer = Enumerable.Range(0, playerOamBytes)
                .Select(offset => (int)cpu.Oam((byte)offset))
                .ToArray();
            var actualUnused = Enumerable.Range(playerOamBytes, 256 - playerOamBytes)
                .Select(offset => (int)cpu.Oam((byte)offset))
                .ToArray();
            return
            [
                new FunctionalSpriteObservation("player", OamVisible(actualPlayer), actualPlayer, 0),
                new FunctionalSpriteObservation("unused-oam", OamVisible(actualUnused), actualUnused, playerAsset.Pieces.Count),
            ];
        }

        private int VariableWord(string name) => VariableByte(name) | (VariableByte(name, 1) << 8);

        private byte VariableByte(string name, int offset = 0) =>
            cpu.Ram(checked((ushort)(variables[name].Address + offset)));

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] < 239);

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
        IReadOnlyDictionary<int, (int X, int Y)> visibleCameraByFrame,
        NesVideoProgram? platformerProgram = null,
        FunctionalScenario? scenario = null) : IFunctionalFrameOracle
    {
        private readonly NesCompiledSpriteAsset? playerAsset = platformerProgram?.SpriteAssets
            .Single(pair => pair.Key.EndsWith("player_sprite", StringComparison.Ordinal)).Value;
        private readonly PlatformerLandingReferenceTimeline? platformerTimeline =
            platformerProgram is null || scenario?.SampleId != "platformer-landing"
            ? null
            : new PlatformerLandingReferenceTimeline(scenario, cameraY: 80);
        private readonly AudioMixedLoadReferenceTimeline? audioMixedTimeline =
            platformerProgram is null || scenario?.SampleId != "audio-mixed-load"
                ? null
                : new AudioMixedLoadReferenceTimeline(scenario);

        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var camera = visibleCameraByFrame[frame];
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
                    .ToArray(),
                Sprites(frame));
        }

        private IReadOnlyList<FunctionalSpriteExpectation>? Sprites(int frame)
        {
            if (playerAsset is null)
            {
                return null;
            }

            if (audioMixedTimeline is not null)
            {
                var audioState = audioMixedTimeline.ExpectedDrawState(frame);
                var player = SpriteOam(
                    audioState.PlayerScreenX,
                    audioState.PlayerScreenY,
                    audioState.Grounded ? 0 : 4,
                    false);
                var patrol = SpriteOam(audioState.PatrolX, 96, 1, audioState.PatrolLeft);
                return
                [
                    new("player", OamVisible(player), player, 0),
                    new("patrol", OamVisible(patrol), patrol, playerAsset.Pieces.Count),
                    new(
                        "unused-oam",
                        false,
                        Enumerable.Repeat(255, 256 - player.Length - patrol.Length).ToArray(),
                        playerAsset.Pieces.Count * 2),
                ];
            }

            if (platformerTimeline is null)
            {
                return null;
            }

            var state = platformerTimeline.ExpectedDrawState(frame);
            var screenX = state.PlayerX - state.CameraX;
            var screenY = state.PlayerY - state.CameraY;
            var spriteFrame = state.Grounded ? 0 : 4;
            var expectedPlayer = playerAsset.Pieces.SelectMany(piece =>
            {
                var pieceX = state.FlipX
                    ? playerAsset.LogicalWidth - 8 - piece.XOffset
                    : piece.XOffset;
                return new[]
                {
                    (screenY + piece.YOffset - 1) & 0xFF,
                    playerAsset.FirstTile + spriteFrame * playerAsset.TilesPerFrame + piece.TileOffset,
                    piece.PaletteSlotOffset | (state.FlipX ? 0x40 : 0),
                    (screenX + pieceX) & 0xFF,
                };
            }).ToArray();
            return
            [
                new FunctionalSpriteExpectation("player", OamVisible(expectedPlayer), expectedPlayer, 0),
                new FunctionalSpriteExpectation(
                    "unused-oam",
                    false,
                    Enumerable.Repeat(255, 256 - expectedPlayer.Length).ToArray(),
                    playerAsset.Pieces.Count),
            ];
        }

        private int[] SpriteOam(int screenX, int screenY, int spriteFrame, bool flipX) =>
            playerAsset!.Pieces.SelectMany(piece =>
            {
                var pieceX = flipX
                    ? playerAsset.LogicalWidth - 8 - piece.XOffset
                    : piece.XOffset;
                return new[]
                {
                    (screenY + piece.YOffset - 1) & 0xFF,
                    playerAsset.FirstTile + spriteFrame * playerAsset.TilesPerFrame + piece.TileOffset,
                    piece.PaletteSlotOffset | (flipX ? 0x40 : 0),
                    (screenX + pieceX) & 0xFF,
                };
            }).ToArray();

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] < 239);

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

    private sealed class AudioMixedLoadReferenceTimeline
    {
        // Video/palette/world/audio initialization consumes the first twenty-four
        // physical frames before the source enters its first retained loop.
        private const int FirstGameplayFrame = 25;
        private const int GroundY = 113;
        private const int TakeoffVelocity = -56;
        private const int HeldGravityThreshold = -32;
        private const int HeldGravity = 1;
        private const int ReleasedGravity = 5;
        private const int TerminalVelocity = 69;
        private const int Subpixel = 16;
        private readonly IReadOnlyDictionary<int, AudioMixedLoadDrawState> drawStateByFrame;

        public AudioMixedLoadReferenceTimeline(FunctionalScenario scenario)
        {
            if (scenario.SampleId != "audio-mixed-load")
            {
                throw new ArgumentException("The reference timeline only supports audio-mixed-load.", nameof(scenario));
            }

            var state = new MutableAudioMixedState();
            var result = new Dictionary<int, AudioMixedLoadDrawState>();
            var previousButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastFrame = checked(scenario.WarmUpFrames + scenario.ObservationFrames);
            for (var frame = 1; frame <= lastFrame; frame++)
            {
                if (frame < FirstGameplayFrame)
                {
                    continue;
                }

                // The source retains both metasprites before polling input and updating gameplay.
                result[frame] = state.DrawState();
                var heldButtons = scenario.Inputs
                    .Where(input => frame >= input.StartFrame && frame < input.StartFrame + input.DurationFrames)
                    .SelectMany(input => input.Buttons)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                Advance(state, heldButtons, previousButtons);
                previousButtons = heldButtons;
            }

            drawStateByFrame = result;
        }

        public AudioMixedLoadDrawState ExpectedDrawState(int frame) =>
            drawStateByFrame.TryGetValue(frame, out var state)
                ? state
                : throw new ArgumentOutOfRangeException(nameof(frame), frame, "Frame is outside audio-mixed-load.");

        private static void Advance(
            MutableAudioMixedState state,
            IReadOnlySet<string> heldButtons,
            IReadOnlySet<string> previousButtons)
        {
            if (heldButtons.Contains("A") && !previousButtons.Contains("A") && state.Grounded)
            {
                state.VelocityY = TakeoffVelocity;
                state.VerticalSubpixel = 0;
                state.Grounded = false;
            }

            if (!state.Grounded)
            {
                state.VelocityY = heldButtons.Contains("A") && state.VelocityY < HeldGravityThreshold
                    ? state.VelocityY + HeldGravity
                    : Math.Min(TerminalVelocity, state.VelocityY + ReleasedGravity);
                var motion = state.VerticalSubpixel + state.VelocityY;
                while (motion < 0)
                {
                    state.PlayerY--;
                    motion += Subpixel;
                }
                while (motion >= Subpixel)
                {
                    state.PlayerY++;
                    motion -= Subpixel;
                }
                state.VerticalSubpixel = motion;
                if (state.PlayerY >= GroundY)
                {
                    state.PlayerY = GroundY;
                    state.VerticalSubpixel = 0;
                    state.VelocityY = 0;
                    state.Grounded = true;
                }
            }

            var playerScreenX = state.PlayerX - state.CameraX;
            if (heldButtons.Contains("RIGHT"))
            {
                state.PlayerX++;
                if (playerScreenX >= 96)
                {
                    state.CameraX++;
                }
            }

            if (state.PatrolLeft)
            {
                state.PatrolX--;
                if (state.PatrolX <= 112)
                {
                    state.PatrolLeft = false;
                }
            }
            else
            {
                state.PatrolX++;
                if (state.PatrolX >= 184)
                {
                    state.PatrolLeft = true;
                }
            }
        }

        private sealed class MutableAudioMixedState
        {
            public int PlayerX { get; set; } = 72;

            public int PlayerY { get; set; } = GroundY;

            public int CameraX { get; set; }

            public int CameraY { get; }

            public int VelocityY { get; set; }

            public int VerticalSubpixel { get; set; }

            public bool Grounded { get; set; } = true;

            public int PatrolX { get; set; } = 144;

            public bool PatrolLeft { get; set; }

            public AudioMixedLoadDrawState DrawState() =>
                new(PlayerX - CameraX, PlayerY - CameraY, Grounded, PatrolX, PatrolLeft);
        }
    }

    private sealed record AudioMixedLoadDrawState(
        int PlayerScreenX,
        int PlayerScreenY,
        bool Grounded,
        int PatrolX,
        bool PatrolLeft);

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private static int PaletteIdentity(int paletteSlot, IReadOnlyList<int> colors) =>
        paletteSlot
        | (colors[0] << 2)
        | (colors[1] << 8)
        | (colors[2] << 14)
        | (colors[3] << 20);
}
