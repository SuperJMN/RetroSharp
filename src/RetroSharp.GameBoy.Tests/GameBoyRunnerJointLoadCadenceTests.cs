namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;
using PackedCameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.PackedCamera;

public sealed class GameBoyRunnerJointLoadCadenceTests(ITestOutputHelper output)
{
    private const int FrozenObservationOffset = 10;
    private const int RetainedPoseRegressionOffset = 40;
    private const int FreshnessSampleLimit = 8;
    private const int MaximumSourceToVisibleCameraDelta = 2;
    // Broad experience floors for a 360-frame RIGHT+B run and one authored jump.
    // They reject token movement while retaining generous headroom from the measured baseline.
    private const int MinimumPlayerHorizontalProgress = 256;
    private const int MinimumCameraHorizontalProgress = 128;
    private const int MinimumJumpVerticalExcursion = 8;
    private static readonly string[] ForbiddenVideoSignals =
    [
        "bankWorkInCommit",
        "decodeWorkInCommit",
        "directoryWorkInVBlank",
        "directoryWorkInCommit",
        "decodeWorkInVBlank",
    ];

    [Fact]
    public void Shared_runner_joint_load_cadence_gate()
    {
        var scenario = FunctionalScenarioLoader.Load(RepositoryFile("validation/scenarios/runner-joint-load.gb.json"));
        var source = RunnerSample.CompiledSource();
        var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        // PrepareVideoProgram supplies independent authored map/sprite metadata to the oracle.
        // Every machine below still executes the one exact ROM compiled above.
        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            RunnerSample.Directory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        var map = GameBoyTiledMapImporter.Load(
            Path.Combine(RunnerSample.Directory, "assets", "maps", "stage1.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var artifact = new FunctionalRomArtifact("samples/runner/bin/runner.gb (fresh manifest build)", build.Rom);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var addresses = new RunnerVariableAddresses(
            variables["player.x"].Address,
            variables["player.y"].Address,
            variables["player.grounded"].Address,
            variables["player.displayFrame"].Address,
            variables["player.displayFlipX"].Address,
            variables["view.x"].Address,
            variables["view.y"].Address);

        var frozenFrame = scenario.WarmUpFrames + FrozenObservationOffset;
        var retainedPoseRegressionFrame = scenario.WarmUpFrames + RetainedPoseRegressionOffset;
        var failFast = new FunctionalScenarioRunOptions(
            FunctionalScenarioRunMode.FailFast,
            EvidenceFramesBeforeFailure: 8);
        var collisionLookup = build.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel];
        var stalledFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var stalledReport = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(new FrozenServiceMachineFactory(stalledFactory, frozenFrame, frozenFrames: 2)),
            new RunnerJointLoadOracle(map, program, stalledFactory),
            failFast);

        Assert.False(stalledReport.Passed);
        Assert.Equal(build.Rom, stalledFactory.LoadedRom);
        Assert.Equal(frozenFrame + 1, stalledReport.FrameWindow.TotalPhysicalFrames);
        Assert.Contains(
            stalledReport.IntegrityFailures,
            failure => failure.Code == "gameplay-cadence-gap" && failure.Frame == frozenFrame + 1);
        Assert.Contains(
            stalledReport.IntegrityFailures,
            failure => failure.Code == "audio-service-gap" && failure.Frame == frozenFrame + 1);
        Assert.Equal(9, stalledReport.FrameEvidence.Count);
        Assert.Equal(frozenFrame + 1, stalledReport.FrameEvidence[^1].Observed.Frame);
        AssertVisualsIntact(stalledReport.FrameEvidence[^1]);

        var gameplayOnlyFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var gameplayOnly = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(new FrozenServiceMachineFactory(
                gameplayOnlyFactory,
                frozenFrame,
                frozenFrames: 2,
                freezeAudioService: false)),
            new RunnerJointLoadOracle(map, program, gameplayOnlyFactory),
            failFast);
        Assert.Contains(gameplayOnly.IntegrityFailures, failure =>
            failure.Code == "gameplay-cadence-gap" && failure.Frame == frozenFrame + 1);
        Assert.DoesNotContain(gameplayOnly.IntegrityFailures, failure => failure.Code == "audio-service-gap");

        var audioOnlyFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var audioOnly = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(new FrozenServiceMachineFactory(
                audioOnlyFactory,
                frozenFrame,
                frozenFrames: 2,
                freezeGameplay: false)),
            new RunnerJointLoadOracle(map, program, audioOnlyFactory),
            failFast);
        Assert.Contains(audioOnly.IntegrityFailures, failure =>
            failure.Code == "audio-service-gap" && failure.Frame == frozenFrame + 1);
        Assert.DoesNotContain(audioOnly.IntegrityFailures, failure => failure.Code == "gameplay-cadence-gap");

        var delayedVisibleFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var delayedVisibleCanary = new DelayedVisibleCameraMachineFactory(
            delayedVisibleFactory,
            scenario.WarmUpFrames + 1,
            delayedFrames: 4);
        var delayedVisible = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(delayedVisibleCanary),
            new RunnerJointLoadOracle(map, program, delayedVisibleFactory),
            failFast);
        var delayedFailure = Assert.Single(delayedVisible.IntegrityFailures, failure => failure.Code == "camera-visible-gap");
        var injectedRequestFrame = Assert.IsType<int>(delayedVisibleCanary.InjectedRequestFrame);
        Assert.NotNull(delayedVisibleCanary.InjectedFrame);
        Assert.Equal(
            injectedRequestFrame + scenario.Budgets.MaximumRequestToVisibleFrames!.Value + 1,
            delayedFailure.Frame);

        var retainedPoseScenario = scenario with
        {
            ObservationFrames = RetainedPoseRegressionOffset,
            Inputs = scenario.Inputs
                .Where(input => input.StartFrame <= retainedPoseRegressionFrame)
                .Select(input => input with
                {
                    DurationFrames = Math.Min(
                        input.DurationFrames,
                        retainedPoseRegressionFrame - input.StartFrame + 1),
                })
                .ToArray(),
        };
        var retainedPoseFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var retainedPose = FunctionalScenarioRunner.Run(
            retainedPoseScenario,
            artifact,
            Adapter(retainedPoseFactory),
            new RunnerJointLoadOracle(map, program, retainedPoseFactory),
            failFast);
        Assert.Equal(build.Rom, retainedPoseFactory.LoadedRom);
        Assert.True(
            retainedPose.Passed,
            $"sourceVisibleMaxDelta={MaxSourceToVisibleDelta(retainedPose, "sourceCameraX", "visibleCameraX")}/"
            + $"{MaxSourceToVisibleDelta(retainedPose, "sourceCameraY", "visibleCameraY")} failures="
            + string.Join(" | ", retainedPose.IntegrityFailures.Take(8).Select(failure => $"{failure.Code}@{failure.Frame}:{failure.Detail}")));
        AssertVisualsIntact(Assert.Single(
            retainedPose.FrameEvidence,
            evidence => evidence.Observed.Frame == retainedPoseRegressionFrame));

        var corruptedFactory = new RunnerJointLoadMachineFactory(
            program,
            collisionLookup,
            addresses,
            corruptedOamFrame: retainedPoseRegressionFrame);
        var corrupted = FunctionalScenarioRunner.Run(
            retainedPoseScenario,
            artifact,
            Adapter(corruptedFactory),
            new RunnerJointLoadOracle(map, program, corruptedFactory),
            failFast);
        Assert.Equal(build.Rom, corruptedFactory.LoadedRom);
        Assert.False(corrupted.Passed, corrupted.ToHumanReadable());
        Assert.Contains(
            corrupted.IntegrityFailures,
            failure => failure.Code == "sprite-oam" && failure.Frame == retainedPoseRegressionFrame);

        var firstFactory = new RunnerJointLoadMachineFactory(
            program,
            collisionLookup,
            addresses,
            warmUpFrame: scenario.WarmUpFrames);
        var first = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(firstFactory),
            new RunnerJointLoadOracle(map, program, firstFactory),
            failFast);
        var secondFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var second = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(secondFactory),
            new RunnerJointLoadOracle(map, program, secondFactory),
            failFast);
        var thirdFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var third = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(thirdFactory),
            new RunnerJointLoadOracle(map, program, thirdFactory),
            failFast);

        var delayedRecoveryFactory = new RunnerJointLoadMachineFactory(program, collisionLookup, addresses);
        var delayedRecovery = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(new DelayedVisibleCameraMachineFactory(
                delayedRecoveryFactory,
                scenario.WarmUpFrames + 1,
                delayedFrames: 4)),
            new RunnerJointLoadOracle(map, program, delayedRecoveryFactory));

        Assert.Equal(build.Rom, firstFactory.LoadedRom);
        Assert.Equal(build.Rom, secondFactory.LoadedRom);
        Assert.Equal(build.Rom, thirdFactory.LoadedRom);
        Assert.Equal(first.Passed, second.Passed);
        Assert.Equal(first.Passed, third.Passed);
        Assert.Equal(first.ToJson(), second.ToJson());
        Assert.Equal(first.ToJson(), third.ToJson());
        Assert.Equal(first.Summary.CameraVisible, delayedRecovery.Summary.CameraVisible);
        WriteDiagnostic("injected-stall", stalledReport);
        WriteDiagnostic("corrupted-oam", corrupted);
        var warmUpObservation = Assert.IsType<FunctionalFrameObservation>(firstFactory.WarmUpObservation);
        var baselineDiagnostic = WriteDiagnostic("baseline", first, warmUpObservation);
        WriteReplayArtifacts(scenario, artifact, first, warmUpObservation, addresses);
        Assert.Contains(
            baselineDiagnostic,
            line => line.Contains(
                "verdict=NOT_REPRODUCED reviewedGapBound=1 freshness=\"no gap >1\"",
                StringComparison.Ordinal));
        var freshnessSummary = Assert.Single(
            baselineDiagnostic,
            line => line.Contains("kind=freshness-summary", StringComparison.Ordinal));
        Assert.Contains("total=", freshnessSummary, StringComparison.Ordinal);
        Assert.Contains("histogram=", freshnessSummary, StringComparison.Ordinal);
        Assert.Contains("first=", freshnessSummary, StringComparison.Ordinal);
        Assert.Contains("last=", freshnessSummary, StringComparison.Ordinal);
        Assert.InRange(
            baselineDiagnostic.Count(line => line.Contains("kind=freshness-anomaly", StringComparison.Ordinal)),
            0,
            FreshnessSampleLimit);
        Assert.All(
            baselineDiagnostic.Where(line => line.Contains("kind=freshness-anomaly", StringComparison.Ordinal)),
            line => Assert.Contains("gameplayDelta=", line, StringComparison.Ordinal));
        AssertFrameEvidence(first);
    }

    [Fact]
    public void Freshness_anomaly_selection_requires_a_non_unit_gameplay_or_audio_delta()
    {
        var previous = new FunctionalFrameObservation(10, 20, 30, 0);

        Assert.False(IsFreshnessAnomaly(new(11, 21, 31, 0), previous));
        Assert.True(IsFreshnessAnomaly(new(11, 20, 31, 0), previous));
        Assert.True(IsFreshnessAnomaly(new(11, 21, 32, 0), previous));
    }

    [Fact]
    public void Forbidden_video_work_is_always_structural_red()
    {
        Assert.Equal("STRUCTURAL_RED", ClassifyVerdict(reportPassed: true, cadenceFailure: false, forbiddenVideoWork: true));
    }

    [Fact]
    public void Replay_descriptor_path_cannot_overwrite_the_rom()
    {
        Assert.Equal(
            Path.Combine("tmp", "runner.gb"),
            ReplayRomPath(Path.Combine("tmp", "runner.timeline.json")));
        Assert.Throws<InvalidOperationException>(() => ReplayRomPath(Path.Combine("tmp", "runner.gb")));
    }

    private static GameBoyFunctionalRomAdapter Adapter(IFunctionalRomMachineFactory factory) => new(
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

    private static void AssertFrameEvidence(FunctionalAcceptanceReport report)
    {
        Assert.NotEmpty(report.FrameEvidence);
        Assert.All(
            report.FrameEvidence,
            evidence =>
            {
                var signals = Assert.IsAssignableFrom<IReadOnlyDictionary<string, long>>(evidence.Observed.StateSignals);
                Assert.All(
                    new[]
                    {
                        "cpuCycle", "lcdPhase", "sourceWait", "romGameplayTick", "packedAudioTick", "inputMask",
                        "playerX", "playerY", "playerGrounded", "cameraRequest", "cameraPrepare",
                        "cameraResident", "cameraCommit", "cameraRelease", "visibleCameraX",
                        "visibleCameraY", "sourceCameraX", "sourceCameraY", "visibleRomBank", "forbiddenVideoWork",
                        "bankWorkInCommit", "decodeWorkInCommit", "directoryWorkInVBlank",
                        "directoryWorkInCommit", "decodeWorkInVBlank",
                    },
                    signal => Assert.True(signals.ContainsKey(signal), $"frame={evidence.Observed.Frame} missing={signal}"));
                Assert.NotNull(evidence.Observed.AudioProgress);
                Assert.NotNull(evidence.Observed.Camera);
                Assert.NotNull(evidence.Observed.Bank);
                Assert.NotNull(evidence.Observed.Background);
                Assert.NotNull(evidence.Observed.Sprites);
                Assert.NotNull(evidence.Observed.VideoWrites);
                Assert.NotNull(evidence.Observed.OamWrites);
                Assert.NotNull(evidence.Expected);
            });
        if (!report.Passed)
        {
            return;
        }

        Assert.Contains(report.FrameEvidence, evidence => (evidence.Observed.StateSignals!["inputMask"] & 4) != 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.StateSignals!["collisionQueries"] > 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.StateSignals!["cameraRequest"] > 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.AudioProgress!.Music.Active);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.AudioProgress!.SoundEffect.Starts > 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.AudioProgress!.SoundEffect.Completions > 0);
        Assert.All(
            report.FrameEvidence,
            evidence => Assert.All(
                ForbiddenVideoSignals,
                signal => Assert.Equal(0, evidence.Observed.StateSignals![signal])));

        var first = report.FrameEvidence[0];
        var last = report.FrameEvidence[^1];
        var playerTravel = MaxSignal(report, "playerX") - MinSignal(report, "playerX");
        var jumpExcursion = MaxSignal(report, "playerY") - MinSignal(report, "playerY");
        var sourceCameraTravel = MaxSignal(report, "sourceCameraX") - MinSignal(report, "sourceCameraX");
        var visibleCameraTravel = MaxSignal(report, "visibleCameraX") - MinSignal(report, "visibleCameraX");
        Assert.True(
            playerTravel >= MinimumPlayerHorizontalProgress,
            $"playerX travel={playerTravel} minimum={MinimumPlayerHorizontalProgress}");
        Assert.True(
            jumpExcursion >= MinimumJumpVerticalExcursion,
            $"playerY excursion={jumpExcursion} minimum={MinimumJumpVerticalExcursion}");
        Assert.True(
            sourceCameraTravel >= MinimumCameraHorizontalProgress,
            $"sourceCameraX travel={sourceCameraTravel} minimum={MinimumCameraHorizontalProgress}");
        Assert.True(
            visibleCameraTravel >= MinimumCameraHorizontalProgress,
            $"visibleCameraX travel={visibleCameraTravel} minimum={MinimumCameraHorizontalProgress}");
        Assert.True(Signal(last, "collisionQueries") > Signal(first, "collisionQueries"));
        Assert.True(
            (last.Observed.Camera!.RequestedSequence ?? 0)
            > (first.Observed.Camera!.RequestedSequence ?? 0));

        var firstAirborne = report.FrameEvidence
            .Select((evidence, index) => (evidence, index))
            .First(item => Signal(item.evidence, "playerGrounded") == 0)
            .index;
        Assert.Contains(
            report.FrameEvidence.Take(firstAirborne),
            evidence => Signal(evidence, "playerGrounded") != 0);
        Assert.Contains(
            report.FrameEvidence.Skip(firstAirborne + 1),
            evidence => Signal(evidence, "playerGrounded") != 0);

        Assert.InRange(MaxSourceToVisibleDelta(report, "sourceCameraX", "visibleCameraX"), 0, MaximumSourceToVisibleCameraDelta);
        Assert.InRange(MaxSourceToVisibleDelta(report, "sourceCameraY", "visibleCameraY"), 0, MaximumSourceToVisibleCameraDelta);
    }

    private static long Signal(FunctionalFrameEvidence evidence, string name) =>
        evidence.Observed.StateSignals![name];

    private static long MinSignal(FunctionalAcceptanceReport report, string name) =>
        report.FrameEvidence.Min(evidence => Signal(evidence, name));

    private static long MaxSignal(FunctionalAcceptanceReport report, string name) =>
        report.FrameEvidence.Max(evidence => Signal(evidence, name));

    private static long MaxSourceToVisibleDelta(
        FunctionalAcceptanceReport report,
        string source,
        string visible) =>
        report.FrameEvidence.Max(evidence => Math.Abs(Signal(evidence, source) - Signal(evidence, visible)));

    private static void AssertVisualsIntact(FunctionalFrameEvidence evidence)
    {
        var expected = Assert.IsType<FunctionalFrameExpectation>(evidence.Expected);
        Assert.Equal(
            expected.Background!.Select(cell => $"{cell.Location}:{cell.Tile}:{cell.Palette}"),
            evidence.Observed.Background!.Select(cell => $"{cell.Location}:{cell.Tile}:{cell.Palette}"));
        Assert.Equal(
            expected.Sprites!.Select(sprite => $"{sprite.Id}:{sprite.Visible}:{sprite.OamSlot}:{string.Join(',', sprite.Oam)}"),
            evidence.Observed.Sprites!.Select(sprite => $"{sprite.Id}:{sprite.Visible}:{sprite.OamSlot}:{string.Join(',', sprite.Oam)}"));
    }

    private IReadOnlyList<string> WriteDiagnostic(
        string run,
        FunctionalAcceptanceReport report,
        FunctionalFrameObservation? observationPredecessor = null)
    {
        var lines = DiagnosticLines(run, report, observationPredecessor);
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        return lines;
    }

    private static void WriteReplayArtifacts(
        FunctionalScenario scenario,
        FunctionalRomArtifact artifact,
        FunctionalAcceptanceReport report,
        FunctionalFrameObservation warmUpObservation,
        RunnerVariableAddresses addresses)
    {
        var reportPath = Environment.GetEnvironmentVariable("RETROSHARP_RPH62_REPORT");
        var replayPath = Environment.GetEnvironmentVariable("RETROSHARP_RPH62_REPLAY");
        if (string.IsNullOrWhiteSpace(reportPath) && string.IsNullOrWhiteSpace(replayPath))
        {
            return;
        }

        string? absoluteReportPath = null;
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            absoluteReportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteReportPath)!);
            // The ordinary report includes every captured write and is useful for local
            // diagnosis, but it is far too large to be a replay contract. Keep only the
            // normalized baseline and frame rows consumed by the comparison tool.
            var compactReport = new
            {
                schema = "retrosharp-rph62-in-process-v2",
                romSha256 = report.RomSha256,
                baseline = ReplayRow(warmUpObservation),
                frames = report.FrameEvidence.Select(evidence => ReplayRow(evidence.Observed)),
            };
            File.WriteAllText(
                absoluteReportPath,
                System.Text.Json.JsonSerializer.Serialize(compactReport, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        var absoluteReplayPath = !string.IsNullOrWhiteSpace(replayPath)
            ? Path.GetFullPath(replayPath)
            : Path.ChangeExtension(absoluteReportPath!, ".timeline.json");
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteReplayPath)!);
        var romPath = ReplayRomPath(absoluteReplayPath);
        File.WriteAllBytes(romPath, artifact.Bytes);

        var frames = Enumerable.Range(1, scenario.WarmUpFrames + scenario.ObservationFrames)
            .Select(frame => new
            {
                frame,
                inputMask = ReplayInputMask(scenario.Inputs
                    .Where(input => frame >= input.StartFrame && frame < input.StartFrame + input.DurationFrames)
                    .SelectMany(input => input.Buttons)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)),
                audioServiceExpected = ReplayAudioServiceExpected(scenario, frame),
            });
        var replay = new
        {
            schema = "retrosharp-rph62-replay-v1",
            romSha256 = report.RomSha256,
            id = scenario.Id,
            warmUpFrames = scenario.WarmUpFrames,
            observationFrames = scenario.ObservationFrames,
            layout = new
            {
                playerX = addresses.PlayerXLow,
                playerY = addresses.PlayerYLow,
                gameplayTick = GameBoyRuntimeMemoryLayout.Collision.GameplayTickCount,
                audioTick = PackedCameraMemory.AudioTickCount,
                cameraRequest = PackedCameraMemory.RequestCount,
                cameraResident = PackedCameraMemory.ResidentCount,
                cameraCommit = PackedCameraMemory.CommitCount,
                cameraVisible = PackedCameraMemory.ReleaseCount,
                visibleCameraX = PackedCameraMemory.VisibleCameraXLow,
                visibleCameraY = PackedCameraMemory.VisibleCameraYLow,
                shadowBank = GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank,
                musicActive = GameBoyRuntimeMemoryLayout.Audio.MusicActive,
                sfxActive = GameBoyRuntimeMemoryLayout.Audio.SfxActive,
                forbiddenCounters = new[]
                {
                    PackedCameraMemory.BankWorkInCommit,
                    PackedCameraMemory.DecodeWorkInCommit,
                    PackedCameraMemory.DirectoryWorkInVBlank,
                    PackedCameraMemory.DirectoryWorkInCommit,
                    PackedCameraMemory.DecodeWorkInVBlank,
                },
            },
            cadence = new
            {
                minimumGameplayTickRatio = scenario.Budgets.MinimumGameplayTickRatio,
                maximumConsecutiveMissedGameplayTicks = scenario.Budgets.MaximumConsecutiveMissedGameplayTicks,
                maximumUnplannedAudioGapFrames = scenario.Budgets.MaximumUnplannedAudioGapFrames,
                maximumRequestToVisibleFrames = scenario.Budgets.MaximumRequestToVisibleFrames,
            },
            frames,
        };
        File.WriteAllText(
            absoluteReplayPath,
            System.Text.Json.JsonSerializer.Serialize(replay, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ReplayRomPath(string replayPath)
    {
        const string TimelineSuffix = ".timeline.json";
        if (!replayPath.EndsWith(TimelineSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RETROSHARP_RPH62_REPLAY must end with '.timeline.json' so the descriptor cannot overwrite the ROM.");
        }

        return replayPath[..^TimelineSuffix.Length] + ".gb";
    }

    private static object ReplayRow(FunctionalFrameObservation observation)
    {
        var signals = observation.StateSignals!;
        return new
        {
            frame = observation.Frame,
            gameplayTicks = observation.GameplayTicks,
            audioServiceTicks = observation.AudioServiceTicks,
            state = new
            {
                romGameplayTick = signals["romGameplayTick"],
                packedAudioTick = signals["packedAudioTick"],
                playerX = signals["playerX"],
                playerY = signals["playerY"],
                visibleCameraX = signals["visibleCameraX"],
                visibleCameraY = signals["visibleCameraY"],
                shadowRomBank = signals["shadowRomBank"],
                forbiddenVideoWork = signals["forbiddenVideoWork"],
                musicActive = signals["musicActive"],
                sfxActive = signals["sfxActive"],
                camera = new
                {
                    request = observation.Camera!.RequestedSequence ?? 0,
                    resident = observation.Camera.ResidentSequence ?? 0,
                    commit = observation.Camera.CommittedSequence ?? 0,
                    visible = observation.Camera.VisibleSequence ?? 0,
                },
                backgroundDigest = ReplayDigest(observation.Background!
                    .OrderBy(cell => cell.Location, StringComparer.Ordinal)
                    .Select(cell => $"{cell.Location}:{cell.Tile}:{cell.Palette}")),
                oamDigest = ReplayDigest(observation.Sprites!
                    .OrderBy(sprite => sprite.OamSlot)
                    .SelectMany(sprite => sprite.Oam)
                    .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            },
        };
    }

    private static string ReplayDigest(IEnumerable<string> values)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join('|', values));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static int ReplayInputMask(IReadOnlySet<string> heldInputs) =>
        (heldInputs.Contains("right", StringComparer.OrdinalIgnoreCase) ? 1 : 0)
        | (heldInputs.Contains("b", StringComparer.OrdinalIgnoreCase) ? 2 : 0)
        | (heldInputs.Contains("a", StringComparer.OrdinalIgnoreCase) ? 4 : 0);

    private static bool ReplayAudioServiceExpected(FunctionalScenario scenario, int frame) =>
        scenario.Audio.ServiceExpectedByDefault
        && !scenario.Audio.AuthoredSilence.Any(span =>
            frame >= span.StartFrame && frame < span.StartFrame + span.DurationFrames);

    private static IReadOnlyList<string> DiagnosticLines(
        string run,
        FunctionalAcceptanceReport report,
        FunctionalFrameObservation? observationPredecessor)
    {
        var lines = new List<string>();
        var timingFailures = report.TimingChecks.Where(check => !check.Passed).ToArray();
        var cadenceFailure = timingFailures.Any(check => check.Metric is
                "gameplay-tick-ratio"
                or "gameplay-missed-streak"
                or "audio-service-gap"
                or "audio-drift")
            || report.IntegrityFailures.Any(failure => failure.Code is "gameplay-cadence-gap" or "audio-service-gap");
        var forbiddenVideoWork = report.FrameEvidence.Any(evidence =>
            evidence.Observed.StateSignals is { } signals
            && signals.TryGetValue("forbiddenVideoWork", out var count)
            && count > 0);
        var verdict = ClassifyVerdict(report.Passed, cadenceFailure, forbiddenVideoWork);
        var freshness = cadenceFailure ? "gap >1" : "no gap >1";
        var inputToState = report.TimingChecks
            .FirstOrDefault(check => check.Metric == "input-to-state")?.Observed;
        lines.Add(
            $"runner-joint-load run={run} verdict={verdict} reviewedGapBound=1 freshness=\"{freshness}\" "
            + $"inputToState={(inputToState is null ? "n/a" : inputToState.Value.ToString("0.###"))} romSha256={report.RomSha256}");
        lines.AddRange(timingFailures.Select(timing =>
            $"run={run} timing code={timing.Metric} observed={timing.Observed:0.###} limit={timing.Limit:0.###} comparison={timing.Comparison}"));

        if (report.IntegrityFailures.Count == 0)
        {
            lines.Add($"run={run} first-frame failures=[]");
            lines.Add(ProgressLine(run, report));
            AddFreshnessAnomalies(lines, run, report.FrameEvidence, observationPredecessor);
            return lines;
        }

        var firstFrame = report.IntegrityFailures.Min(failure => failure.Frame);
        lines.AddRange(report.IntegrityFailures
            .Where(failure => failure.Frame == firstFrame)
            .Select(failure => $"run={run} first-frame failure={failure.Code}@{failure.Frame} detail={failure.Detail}"));

        var evidence = report.FrameEvidence
            .Where(item => item.Observed.Frame >= firstFrame - 8 && item.Observed.Frame <= firstFrame)
            .ToArray();
        var previous = evidence[0].Observed;
        foreach (var current in evidence.Skip(1))
        {
            lines.Add(ObservationLine(run, "failure-evidence", current, previous));
            previous = current.Observed;
        }

        return lines;
    }

    private static string ClassifyVerdict(
        bool reportPassed,
        bool cadenceFailure,
        bool forbiddenVideoWork) =>
        forbiddenVideoWork
            ? "STRUCTURAL_RED"
            : reportPassed
                ? "NOT_REPRODUCED"
                : cadenceFailure
                    ? "CADENCE_RED"
                    : "STRUCTURAL_RED";

    private static string ProgressLine(string run, FunctionalAcceptanceReport report)
    {
        var first = report.FrameEvidence[0];
        var last = report.FrameEvidence[^1];
        return $"run={run} kind=progress "
            + $"playerXRange={MinSignal(report, "playerX")}..{MaxSignal(report, "playerX")} "
            + $"playerYRange={MinSignal(report, "playerY")}..{MaxSignal(report, "playerY")} "
            + $"sourceCameraXRange={MinSignal(report, "sourceCameraX")}..{MaxSignal(report, "sourceCameraX")} "
            + $"visibleCameraXRange={MinSignal(report, "visibleCameraX")}..{MaxSignal(report, "visibleCameraX")} "
            + $"minimumProgress={MinimumPlayerHorizontalProgress}/{MinimumCameraHorizontalProgress}/{MinimumJumpVerticalExcursion} "
            + $"sourceVisibleMaxDelta={MaxSourceToVisibleDelta(report, "sourceCameraX", "visibleCameraX")}/"
            + $"{MaxSourceToVisibleDelta(report, "sourceCameraY", "visibleCameraY")} "
            + $"collisionDelta={Signal(last, "collisionQueries") - Signal(first, "collisionQueries")} "
            + $"cameraRequestDelta={(last.Observed.Camera!.RequestedSequence ?? 0) - (first.Observed.Camera!.RequestedSequence ?? 0)}";
    }

    private static void AddFreshnessAnomalies(
        ICollection<string> lines,
        string run,
        IReadOnlyList<FunctionalFrameEvidence> evidence,
        FunctionalFrameObservation? observationPredecessor)
    {
        var firstIndex = observationPredecessor is null ? 1 : 0;
        var anomalyIndexes = new List<int>();
        for (var index = firstIndex; index < evidence.Count; index++)
        {
            var previous = index == 0 ? observationPredecessor! : evidence[index - 1].Observed;
            if (IsFreshnessAnomaly(evidence[index].Observed, previous))
            {
                anomalyIndexes.Add(index);
            }
        }

        string Describe(int index)
        {
            var current = evidence[index].Observed;
            var previous = index == 0 ? observationPredecessor! : evidence[index - 1].Observed;
            return $"frame:{current.Frame}/gameplayDelta:{current.GameplayTicks - previous.GameplayTicks}"
                + $"/audioDelta:{current.AudioServiceTicks - previous.AudioServiceTicks}";
        }

        var histogram = anomalyIndexes
            .GroupBy(index =>
            {
                var current = evidence[index].Observed;
                var previous = index == 0 ? observationPredecessor! : evidence[index - 1].Observed;
                return (
                    Gameplay: current.GameplayTicks - previous.GameplayTicks,
                    Audio: current.AudioServiceTicks - previous.AudioServiceTicks);
            })
            .OrderBy(group => group.Key.Gameplay)
            .ThenBy(group => group.Key.Audio)
            .Select(group => $"{group.Key.Gameplay}/{group.Key.Audio}:{group.Count()}");
        lines.Add(
            $"run={run} kind=freshness-summary total={anomalyIndexes.Count} "
            + $"transitions={evidence.Count - firstIndex} "
            + $"predecessorFrame={(observationPredecessor is null ? "none" : observationPredecessor.Frame.ToString())} "
            + $"histogram=[{string.Join(',', histogram)}] "
            + $"first={(anomalyIndexes.Count == 0 ? "none" : Describe(anomalyIndexes[0]))} "
            + $"last={(anomalyIndexes.Count == 0 ? "none" : Describe(anomalyIndexes[^1]))}");

        var sampleIndexes = anomalyIndexes.Count <= FreshnessSampleLimit
            ? anomalyIndexes
            : anomalyIndexes.Take(FreshnessSampleLimit / 2)
                .Concat(anomalyIndexes.TakeLast(FreshnessSampleLimit / 2));
        foreach (var index in sampleIndexes)
        {
            var previous = index == 0 ? observationPredecessor! : evidence[index - 1].Observed;
            lines.Add(ObservationLine(run, "freshness-anomaly", evidence[index], previous));
        }
    }

    private static bool IsFreshnessAnomaly(
        FunctionalFrameObservation current,
        FunctionalFrameObservation previous) =>
        current.GameplayTicks - previous.GameplayTicks != 1
        || current.AudioServiceTicks - previous.AudioServiceTicks != 1;

    private static string ObservationLine(
        string run,
        string kind,
        FunctionalFrameEvidence current,
        FunctionalFrameObservation previous)
    {
        var observed = current.Observed;
        var signals = observed.StateSignals!;
        var previousSignals = previous.StateSignals!;
        var unsafeVideo = observed.VideoWrites!.Count(write => !write.Safe);
        var unsafeOam = observed.OamWrites!.Count(write => !write.Safe);
        var forbiddenDelta = ForbiddenVideoSignals.Sum(signal => ByteDelta(signals, previousSignals, signal));
        var forbiddenCounters = string.Join(
            ',',
            ForbiddenVideoSignals.Select(signal =>
                $"{signal}:{signals[signal]}/{ByteDelta(signals, previousSignals, signal)}"));
        var oamMatches = OamMatches(current);
        var oamDetail = oamMatches
            ? string.Empty
            : $" playerOam={PlayerOam(observed.Sprites)}/{ExpectedPlayerOam(current.Expected?.Sprites)}";
        return $"run={run} kind={kind} frame={observed.Frame} frameDelta={observed.Frame - previous.Frame} "
            + $"cycle={signals["cpuCycle"]} cycleDelta={signals["cpuCycle"] - previousSignals["cpuCycle"]} "
            + $"lcd={signals["lcdPhase"]} lcdDelta={signals["lcdPhase"] - previousSignals["lcdPhase"]} "
            + $"reset={observed.ResetCount} resetDelta={observed.ResetCount - previous.ResetCount} "
            + $"sourceWait={signals["sourceWait"]} sourceWaitDelta={signals["sourceWait"] - previousSignals["sourceWait"]} "
            + $"gameplay={observed.GameplayTicks} gameplayDelta={observed.GameplayTicks - previous.GameplayTicks} "
            + $"audio={observed.AudioServiceTicks} audioDelta={observed.AudioServiceTicks - previous.AudioServiceTicks} "
            + $"packedAudio={signals["packedAudioTick"]} packedAudioDelta={ByteDelta(signals, previousSignals, "packedAudioTick")} "
            + $"apu={signals["apuEventCount"]} apuDelta={signals["apuEventCount"] - previousSignals["apuEventCount"]} "
            + $"sfx={signals["sfxStarts"]}/{signals["sfxActive"]}/{signals["sfxCompletions"]} "
            + $"sfxDelta={signals["sfxStarts"] - previousSignals["sfxStarts"]}/{signals["sfxActive"] - previousSignals["sfxActive"]}/{signals["sfxCompletions"] - previousSignals["sfxCompletions"]} "
            + $"input={signals["inputMask"]} inputDelta={signals["inputMask"] - previousSignals["inputMask"]} "
            + $"player={signals["playerX"]},{signals["playerY"]},{signals["playerGrounded"]} "
            + $"playerDelta={signals["playerX"] - previousSignals["playerX"]},{signals["playerY"] - previousSignals["playerY"]},{signals["playerGrounded"] - previousSignals["playerGrounded"]} "
            + $"camera={signals["cameraRequest"]}/{signals["cameraPrepare"]}/{signals["cameraResident"]}/{signals["cameraCommit"]}/{signals["cameraRelease"]} "
            + $"cameraDelta={ByteDelta(signals, previousSignals, "cameraRequest")}/{ByteDelta(signals, previousSignals, "cameraPrepare")}/{ByteDelta(signals, previousSignals, "cameraResident")}/{ByteDelta(signals, previousSignals, "cameraCommit")}/{ByteDelta(signals, previousSignals, "cameraRelease")} "
            + $"visible={signals["visibleCameraX"]},{signals["visibleCameraY"]} "
            + $"visibleDelta={signals["visibleCameraX"] - previousSignals["visibleCameraX"]},{signals["visibleCameraY"] - previousSignals["visibleCameraY"]} "
            + $"sourceCamera={signals["sourceCameraX"]},{signals["sourceCameraY"]} "
            + $"sourceCameraDelta={signals["sourceCameraX"] - previousSignals["sourceCameraX"]},{signals["sourceCameraY"] - previousSignals["sourceCameraY"]} "
            + $"visibleRomBank={signals["visibleRomBank"]} visibleRomBankDelta={signals["visibleRomBank"] - previousSignals["visibleRomBank"]} "
            + $"bank={observed.Bank?.SelectedBank}/{observed.Bank?.ShadowBank} "
            + $"bankDelta={observed.Bank!.SelectedBank - previous.Bank!.SelectedBank}/{observed.Bank.ShadowBank - previous.Bank.ShadowBank} "
            + $"forbidden={signals["forbiddenVideoWork"]} forbiddenDelta={forbiddenDelta} forbiddenCounters={forbiddenCounters} "
            + $"unsafe={unsafeVideo}/{unsafeOam} "
            + $"backgroundMatch={Flag(BackgroundMatches(current))} oamMatch={Flag(oamMatches)}"
            + oamDetail;
    }

    private static long ByteDelta(
        IReadOnlyDictionary<string, long> current,
        IReadOnlyDictionary<string, long> previous,
        string signal) =>
        (byte)(current[signal] - previous[signal]);

    private static string Flag(bool value) => value ? "true" : "false";

    private static bool BackgroundMatches(FunctionalFrameEvidence evidence)
    {
        var observed = evidence.Observed.Background;
        var expected = evidence.Expected?.Background;
        return observed is not null
            && expected is not null
            && observed.Count == expected.Count
            && observed.Zip(expected).All(pair =>
                pair.First.Location == pair.Second.Location
                && pair.First.Tile == pair.Second.Tile
                && pair.First.Palette == pair.Second.Palette);
    }

    private static bool OamMatches(FunctionalFrameEvidence evidence)
    {
        var observed = evidence.Observed.Sprites;
        var expected = evidence.Expected?.Sprites;
        return observed is not null
            && expected is not null
            && observed.Count == expected.Count
            && observed.Zip(expected).All(pair =>
                pair.First.Id == pair.Second.Id
                && pair.First.Visible == pair.Second.Visible
                && pair.First.OamSlot == pair.Second.OamSlot
                && pair.First.Oam.SequenceEqual(pair.Second.Oam));
    }

    private static string PlayerOam(IReadOnlyList<FunctionalSpriteObservation>? sprites) =>
        string.Join('.', sprites!.Single(sprite => sprite.Id == "player").Oam);

    private static string ExpectedPlayerOam(IReadOnlyList<FunctionalSpriteExpectation>? sprites) =>
        string.Join('.', sprites!.Single(sprite => sprite.Id == "player").Oam);

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

    private sealed class RunnerJointLoadMachineFactory(
        GameBoyVideoProgram program,
        ushort collisionLookup,
        RunnerVariableAddresses addresses,
        int? warmUpFrame = null,
        int? corruptedOamFrame = null) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public Dictionary<int, RunnerDrawState> DrawStates { get; } = [];

        public FunctionalFrameObservation? WarmUpObservation { get; private set; }

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new RunnerJointLoadMachine(
                LoadedRom,
                program,
                collisionLookup,
                addresses,
                DrawStates,
                warmUpFrame,
                observation => WarmUpObservation = observation,
                corruptedOamFrame);
        }
    }

    private sealed class RunnerJointLoadMachine(
        byte[] exactRom,
        GameBoyVideoProgram program,
        ushort collisionLookup,
        RunnerVariableAddresses addresses,
        IDictionary<int, RunnerDrawState> drawStates,
        int? warmUpFrame,
        Action<FunctionalFrameObservation> retainWarmUpObservation,
        int? corruptedOamFrame) : IFunctionalRomMachine
    {
        private readonly GameBoyTestCpu cpu = CreateCpu(exactRom, collisionLookup, addresses);
        private readonly GameBoyCompiledSpriteAsset playerAsset = program.SpriteAssets["mario_player"];
        private int lastFrame;
        private int processedVramWrites;
        private int processedOamWrites;
        private int processedApuWrites;
        private byte previousRequest;
        private byte previousResident;
        private byte previousCommit;
        private byte previousRelease;
        private long requestSequence;
        private long residentSequence;
        private long commitSequence;
        private long releaseSequence;
        private bool previousMusicActive;
        private bool previousSfxActive;
        private long musicStarts;
        private long musicCompletions;
        private long sfxStarts;
        private long sfxCompletions;

        public FunctionalFrameObservation ObserveInitial() => Observe(0, EmptyDrawState, 0);

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            if (frame != lastFrame + 1)
            {
                throw new InvalidOperationException($"Expected frame {lastFrame + 1}, received {frame}.");
            }

            cpu.Held.Clear();
            cpu.Held.UnionWith(heldInputs);
            // Keep samples on absolute DMG frame boundaries so instruction-boundary
            // overshoot cannot accumulate into a false mid-operation bank observation.
            cpu.RunFrames(frame);
            lastFrame = frame;
            return Observe(frame, CapturePublishedDrawState(), InputMask(heldInputs));
        }

        public void Dispose()
        {
        }

        private FunctionalFrameObservation Observe(int frame, RunnerDrawState drawState, int inputMask)
        {
            var visibleCamera = (
                X: ReadWord(PackedCameraMemory.VisibleCameraXLow),
                Y: ReadWord(PackedCameraMemory.VisibleCameraYLow));
            drawStates[frame] = drawState;
            var audio = CaptureAudioProgress();
            var bankWorkInCommit = cpu.Wram(PackedCameraMemory.BankWorkInCommit);
            var decodeWorkInCommit = cpu.Wram(PackedCameraMemory.DecodeWorkInCommit);
            var directoryWorkInVBlank = cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank);
            var directoryWorkInCommit = cpu.Wram(PackedCameraMemory.DirectoryWorkInCommit);
            var decodeWorkInVBlank = cpu.Wram(PackedCameraMemory.DecodeWorkInVBlank);
            var signals = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["cpuCycle"] = cpu.Cycles,
                ["lcdPhase"] = cpu.IoRegister(0xFF44) >= 144 ? 1 : 0,
                ["sourceWait"] = cpu.SourceWaitCompletions,
                // This counter is written by the ROM at the packed Input.Poll tick boundary.
                // Keep it distinct from the host's opcode-marker projection above.
                ["romGameplayTick"] = cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.GameplayTickCount),
                ["packedAudioTick"] = cpu.Wram(PackedCameraMemory.AudioTickCount),
                ["apuEventCount"] = audio.RegisterEventCount,
                ["sfxStarts"] = audio.SoundEffect.Starts,
                ["sfxActive"] = cpu.Wram(GameBoyRuntimeMemoryLayout.Audio.SfxActive),
                ["sfxCompletions"] = audio.SoundEffect.Completions,
                ["musicActive"] = cpu.Wram(GameBoyRuntimeMemoryLayout.Audio.MusicActive),
                ["inputMask"] = inputMask,
                ["playerX"] = ReadWord(addresses.PlayerXLow),
                ["playerY"] = ReadWord(addresses.PlayerYLow),
                ["playerGrounded"] = cpu.Wram(addresses.PlayerGrounded),
                ["playerDisplayFrame"] = ReadWord(addresses.PlayerDisplayFrameLow),
                ["cameraRequest"] = cpu.Wram(PackedCameraMemory.RequestCount),
                ["cameraPrepare"] = cpu.Wram(PackedCameraMemory.PrepareCount),
                ["cameraResident"] = cpu.Wram(PackedCameraMemory.ResidentCount),
                ["cameraCommit"] = cpu.Wram(PackedCameraMemory.CommitCount),
                ["cameraRelease"] = cpu.Wram(PackedCameraMemory.ReleaseCount),
                ["visibleCameraX"] = visibleCamera.X,
                ["visibleCameraY"] = visibleCamera.Y,
                ["sourceCameraX"] = drawState.SourceCameraX,
                ["sourceCameraY"] = drawState.SourceCameraY,
                ["visibleRomBank"] = cpu.CurrentRomBank,
                ["collisionQueries"] = cpu.WorldPackCollisionQueries.Count,
                ["bankWorkInCommit"] = bankWorkInCommit,
                ["decodeWorkInCommit"] = decodeWorkInCommit,
                ["directoryWorkInVBlank"] = directoryWorkInVBlank,
                ["directoryWorkInCommit"] = directoryWorkInCommit,
                ["decodeWorkInVBlank"] = decodeWorkInVBlank,
                ["forbiddenVideoWork"] = bankWorkInCommit
                    + decodeWorkInCommit
                    + directoryWorkInVBlank
                    + directoryWorkInCommit
                    + decodeWorkInVBlank,
            };
            var shadowBank = cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            var effectiveShadowBank = shadowBank == 0 ? 1 : shadowBank;
            signals["shadowRomBank"] = effectiveShadowBank;
            var observation = new FunctionalFrameObservation(
                frame,
                cpu.SourceWaitCompletions,
                cpu.AudioUpdateCalls,
                cpu.ResetCount,
                signals,
                CaptureCamera(visibleCamera),
                new FunctionalBankObservation(cpu.CurrentRomBank, effectiveShadowBank, cpu.CurrentRomBank == effectiveShadowBank, "gb-mbc1"),
                CaptureBackground(visibleCamera),
                CaptureSprites(frame),
                CaptureVideoWrites(),
                CaptureOamWrites(),
                AudioProgress: audio);
            if (frame == warmUpFrame)
            {
                retainWarmUpObservation(observation);
            }

            return observation;
        }

        private RunnerDrawState CapturePublishedDrawState()
        {
            if (cpu.OamDmaTransfers.Count == 0 || cpu.SourceWaitCompletions == 0)
            {
                return EmptyDrawState;
            }

            var publication = cpu.OamDmaTransfers[^1];
            var drawCycle = cpu.WramByteWrites.Last(write =>
                write.Address == GameBoyRuntimeMemoryLayout.Sprites.OamShadowStart
                && write.Cycles < publication.StartCycle).Cycles;
            if (!cpu.WramWordWrites.Any(write =>
                    write.LowAddress == addresses.PlayerXLow
                    && write.Cycles <= drawCycle))
            {
                // Video initialization clears and publishes shadow OAM before source
                // player state exists; that publication intentionally represents empty OAM.
                return EmptyDrawState;
            }

            // OAM publishes the prior source-authored shadow, while Camera.Apply
            // consumes the current runtime camera request immediately after DMA.
            return new(
                ReadWordAt(addresses.PlayerXLow, drawCycle),
                ReadWordAt(addresses.PlayerYLow, drawCycle),
                ReadWordAt(addresses.PlayerDisplayFrameLow, drawCycle),
                ReadByteAt(addresses.PlayerDisplayFlipX, drawCycle) != 0,
                ReadWordAt(addresses.CameraXLow, drawCycle),
                ReadWordAt(addresses.CameraYLow, drawCycle),
                ReadWordAt(GameBoyRuntimeMemoryLayout.Camera.XLow, publication.StartCycle),
                ReadWordAt(GameBoyRuntimeMemoryLayout.Camera.YLow, publication.StartCycle));
        }

        private FunctionalCameraLifecycleObservation CaptureCamera((int X, int Y) visibleCamera) => new(
            UpdateSequence(PackedCameraMemory.RequestCount, ref previousRequest, ref requestSequence),
            UpdateSequence(PackedCameraMemory.ResidentCount, ref previousResident, ref residentSequence),
            UpdateSequence(PackedCameraMemory.CommitCount, ref previousCommit, ref commitSequence),
            UpdateSequence(PackedCameraMemory.ReleaseCount, ref previousRelease, ref releaseSequence));

        private long? UpdateSequence(ushort address, ref byte previous, ref long sequence)
        {
            var current = cpu.Wram(address);
            sequence += (byte)(current - previous);
            previous = current;
            return sequence == 0 ? null : sequence;
        }

        private FunctionalAudioProgressObservation CaptureAudioProgress()
        {
            var events = cpu.ApuWrites.Skip(processedApuWrites)
                .Select(write => new FunctionalAudioRegisterEvent("gb-apu", write.Register, write.Value))
                .ToArray();
            processedApuWrites = cpu.ApuWrites.Count;
            var musicActive = cpu.Wram(GameBoyRuntimeMemoryLayout.Audio.MusicActive) != 0;
            var sfxActive = cpu.Wram(GameBoyRuntimeMemoryLayout.Audio.SfxActive) != 0;
            UpdateLifecycle(musicActive, previousMusicActive, ref musicStarts, ref musicCompletions);
            UpdateLifecycle(sfxActive, previousSfxActive, ref sfxStarts, ref sfxCompletions);
            previousMusicActive = musicActive;
            previousSfxActive = sfxActive;
            return new(
                cpu.ApuWrites.Count,
                events,
                new FunctionalAudioPlaybackObservation(musicActive, musicStarts, musicCompletions, 0),
                new FunctionalAudioPlaybackObservation(sfxActive, sfxStarts, sfxCompletions, 0),
                new FunctionalAudioPlaybackObservation(false, 0, 0, 0));
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x => new FunctionalBackgroundObservation(
                    $"screen:{x:D2},{y:D2}",
                    cpu.Vram((ushort)(0x9800 + ((startRow + y) & 31) * 32 + ((startColumn + x) & 31))),
                    cpu.IoRegister(0xFF47))))
                .ToArray();
        }

        private IReadOnlyList<FunctionalSpriteObservation> CaptureSprites(int frame)
        {
            var playerLength = playerAsset.Pieces.Count * 4;
            var player = Enumerable.Range(0, playerLength)
                .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + offset)))
                .ToArray();
            if (frame == corruptedOamFrame)
            {
                player[0] ^= 1;
            }

            var unused = Enumerable.Range(playerLength, 160 - playerLength)
                .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + offset)))
                .ToArray();
            return
            [
                new FunctionalSpriteObservation("player", OamVisible(player), player, 0),
                new FunctionalSpriteObservation("unused-oam", OamVisible(unused), unused, playerAsset.Pieces.Count),
            ];
        }

        private IReadOnlyList<FunctionalVideoWriteObservation> CaptureVideoWrites()
        {
            var writes = cpu.VramWrites.Skip(processedVramWrites)
                .Select(write => new FunctionalVideoWriteObservation(
                    "gb-vram",
                    write.Address,
                    write.Applied,
                    Timing(write.Cycles, write.Ly, write.LcdEnabled)))
                .ToArray();
            processedVramWrites = cpu.VramWrites.Count;
            return writes;
        }

        private IReadOnlyList<FunctionalOamWriteObservation> CaptureOamWrites()
        {
            var writes = cpu.OamWrites.Skip(processedOamWrites)
                .Select(write => new FunctionalOamWriteObservation(
                    write.Address,
                    !write.LcdEnabled || write.Ly >= 144,
                    Timing(write.Cycles, write.Ly, write.LcdEnabled)))
                .ToArray();
            processedOamWrites = cpu.OamWrites.Count;
            return writes;
        }

        private static FunctionalWriteTimingObservation Timing(long cycles, byte ly, bool lcdEnabled) => new(
            cycles,
            ly,
            (int)(cycles % 456),
            !lcdEnabled ? "lcd-off" : ly >= 144 ? "vblank" : "visible",
            lcdEnabled);

        private static void UpdateLifecycle(bool active, bool previous, ref long starts, ref long completions)
        {
            if (active && !previous)
            {
                starts++;
            }
            else if (!active && previous)
            {
                completions++;
            }
        }

        private static int InputMask(IReadOnlySet<string> heldInputs) =>
            (heldInputs.Contains("right", StringComparer.OrdinalIgnoreCase) ? 1 : 0)
            | (heldInputs.Contains("b", StringComparer.OrdinalIgnoreCase) ? 2 : 0)
            | (heldInputs.Contains("a", StringComparer.OrdinalIgnoreCase) ? 4 : 0);

        private int ReadWord(ushort low) => cpu.Wram(low) | (cpu.Wram((ushort)(low + 1)) << 8);

        private int ReadWordAt(ushort low, long cycle)
        {
            for (var index = cpu.WramWordWrites.Count - 1; index >= 0; index--)
            {
                var write = cpu.WramWordWrites[index];
                if (write.LowAddress == low && write.Cycles <= cycle)
                {
                    return write.Value;
                }
            }

            throw new InvalidOperationException(
                $"No WRAM word trace exists for address 0x{low:X4} at or before cycle {cycle}.");
        }

        private byte ReadByteAt(ushort address, long cycle)
        {
            for (var index = cpu.WramByteWrites.Count - 1; index >= 0; index--)
            {
                var write = cpu.WramByteWrites[index];
                if (write.Address == address && write.Cycles <= cycle)
                {
                    return write.Value;
                }
            }

            throw new InvalidOperationException(
                $"No WRAM byte trace exists for address 0x{address:X4} at or before cycle {cycle}.");
        }

        private static GameBoyTestCpu CreateCpu(
            byte[] rom,
            ushort collisionLookup,
            RunnerVariableAddresses variables)
        {
            var cpu = new GameBoyTestCpu(rom)
            {
                CycleAccurateLy = true,
                EnforceVblankVramWrites = true,
                TracedWorldPackCollisionLookupEntry = collisionLookup,
            };
            cpu.TracedWramWords.UnionWith(
            [
                variables.PlayerXLow,
                variables.PlayerYLow,
                variables.PlayerDisplayFrameLow,
                variables.CameraXLow,
                variables.CameraYLow,
                GameBoyRuntimeMemoryLayout.Camera.XLow,
                GameBoyRuntimeMemoryLayout.Camera.YLow,
            ]);
            cpu.TracedWramBytes.UnionWith(
            [
                variables.PlayerDisplayFlipX,
                GameBoyRuntimeMemoryLayout.Sprites.OamShadowStart,
            ]);
            return cpu;
        }

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] is > 0 and < 160);

        private static readonly RunnerDrawState EmptyDrawState = new(0, 0, 0, false, 0, 0, 0, 0);
    }

    private sealed class RunnerJointLoadOracle(
        GameBoyTiledMap map,
        GameBoyVideoProgram program,
        RunnerJointLoadMachineFactory factory) : IFunctionalFrameOracle
    {
        private readonly GameBoyCompiledSpriteAsset playerAsset = program.SpriteAssets["mario_player"];

        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var draw = factory.DrawStates[frame];
            var width = draw.SourceCameraX % 8 == 0 ? 20 : 21;
            var height = draw.SourceCameraY % 8 == 0 ? 18 : 19;
            var background = Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x => new FunctionalBackgroundExpectation(
                    $"screen:{x:D2},{y:D2}",
                    AuthoredTile(draw.SourceCameraX / 8 + x, draw.SourceCameraY / 8 + y),
                    0xE4)))
                .ToArray();
            var player = playerAsset.Pieces.SelectMany(piece =>
            {
                var pieceX = draw.FlipX
                    ? playerAsset.LogicalWidth - 8 - piece.XOffset
                    : piece.XOffset;
                return new[]
                {
                    (draw.PlayerY - draw.PublishedCameraY + 16 + piece.YOffset) & 0xFF,
                    (draw.PlayerX - draw.PublishedCameraX + 8 + pieceX) & 0xFF,
                    playerAsset.FirstTile + draw.AnimationFrame * playerAsset.TilesPerFrame + piece.TileOffset,
                    draw.FlipX ? 0x20 : 0,
                };
            }).ToArray();
            return new(
                frame,
                background,
                [
                    new FunctionalSpriteExpectation("player", OamVisible(player), player, 0),
                    new FunctionalSpriteExpectation(
                        "unused-oam",
                        false,
                        Enumerable.Repeat(0, 160 - player.Length).ToArray(),
                        playerAsset.Pieces.Count),
                ]);
        }

        private int AuthoredTile(int x, int y)
        {
            var wrappedX = x % map.Width;
            var wrappedY = y % map.Height;
            return map.WorldTileIds[wrappedY * map.Width + wrappedX];
        }

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] is > 0 and < 160);
    }

    private sealed record RunnerDrawState(
        int PlayerX,
        int PlayerY,
        int AnimationFrame,
        bool FlipX,
        int PublishedCameraX,
        int PublishedCameraY,
        int SourceCameraX,
        int SourceCameraY);

    private sealed record RunnerVariableAddresses(
        ushort PlayerXLow,
        ushort PlayerYLow,
        ushort PlayerGrounded,
        ushort PlayerDisplayFrameLow,
        ushort PlayerDisplayFlipX,
        ushort CameraXLow,
        ushort CameraYLow);
}
