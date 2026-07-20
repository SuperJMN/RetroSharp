namespace RetroSharp.FunctionalAcceptance;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

public static class FunctionalScenarioRunner
{
    public static FunctionalAcceptanceReport Run(
        FunctionalScenario scenario,
        FunctionalRomArtifact rom,
        IFunctionalRomAdapter adapter,
        IFunctionalFrameOracle? oracle = null) =>
        Run(scenario, rom, adapter, oracle, new FunctionalScenarioRunOptions());

    public static FunctionalAcceptanceReport Run(
        FunctionalScenario scenario,
        FunctionalRomArtifact rom,
        IFunctionalRomAdapter adapter,
        IFunctionalFrameOracle? oracle,
        FunctionalScenarioRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(rom);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unknown functional scenario run mode.");
        }

        if (options.EvidenceFramesBeforeFailure <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.EvidenceFramesBeforeFailure,
                "Fail-fast evidence frames before failure must be positive.");
        }

        FunctionalScenarioValidator.Validate(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(rom.SourcePath);
        if (rom.Bytes is null || rom.Bytes.Length == 0)
        {
            throw new ArgumentException("A functional scenario requires the exact non-empty production ROM bytes.", nameof(rom));
        }

        if (adapter.Target != scenario.Target)
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.Id}' targets {scenario.Target.StableId()}, but the adapter targets {adapter.Target.StableId()}.");
        }

        ValidateCapabilities(scenario, adapter);
        if ((scenario.ExpectedFeatures.Background || scenario.ExpectedFeatures.SpriteOam) && oracle is null)
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.Id}' for target {scenario.Target.StableId()} requires an independent visual oracle.");
        }

        var lastObservationFrame = checked(scenario.WarmUpFrames + scenario.ObservationFrames);
        var frames = new List<FunctionalFrameObservation>(lastObservationFrame + 2);
        using (var machine = adapter.CreateMachine(rom.Bytes)
               ?? throw new InvalidOperationException("The functional adapter returned no ROM machine."))
        {
            frames.Add(machine.ObserveInitial());
            ValidateObservationFrame(frames[0], 0, null);
            for (var frame = 1; frame <= lastObservationFrame; frame++)
            {
                var held = scenario.Inputs
                    .Where(input => frame >= input.StartFrame && frame < input.StartFrame + input.DurationFrames)
                    .SelectMany(input => input.Buttons)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                frames.Add(machine.AdvanceFrame(frame, held));
                ValidateObservationFrame(frames[frame], frame, frames[frame - 1]);
                if (options.Mode == FunctionalScenarioRunMode.FailFast && frame > scenario.WarmUpFrames)
                {
                    var failures = new List<FunctionalIntegrityFailure>();
                    AddFramePrerequisiteFailures(scenario, frames[frame], frames[0].ResetCount, failures);
                    AddFramePublicationFailures(scenario, frames[frame], failures);
                    AddCheckpointFailures(scenario, frames[frame], failures);
                    if (failures.Count > 0)
                    {
                        return CreateFailFastReport(scenario, rom, adapter, oracle, options, frames, failures);
                    }
                }
            }

            var maximumDrainFrames = scenario.ExpectedFeatures.CameraLifecycle
                ? Math.Max(
                    scenario.Budgets.MaximumRequestToResidentFrames ?? 0,
                    scenario.Budgets.MaximumRequestToVisibleFrames ?? 0)
                : 0;
            for (var drain = 0;
                 drain < maximumDrainFrames && HasIncompleteCameraRequest(scenario, frames, lastObservationFrame);
                 drain++)
            {
                var frame = frames.Count;
                frames.Add(machine.AdvanceFrame(frame, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
                ValidateObservationFrame(frames[frame], frame, frames[frame - 1]);
            }
        }

        var timingChecks = EvaluateTiming(scenario, frames);
        var integrity = EvaluateIntegrity(scenario, frames, oracle);
        var summary = Summarize(scenario, frames, integrity.Failures);
        return new FunctionalAcceptanceReport(
            scenario.Id,
            scenario.SampleId,
            scenario.Target,
            adapter.ExecutionSource,
            rom.SourcePath,
            Convert.ToHexString(SHA256.HashData(rom.Bytes)).ToLowerInvariant(),
            new FunctionalFrameWindow(scenario.WarmUpFrames, scenario.ObservationFrames, frames.Count - 1),
            summary,
            timingChecks.All(check => check.Passed) && integrity.Failures.Count == 0,
            timingChecks,
            integrity.Failures,
            integrity.Evidence);
    }

    private static FunctionalAcceptanceReport CreateFailFastReport(
        FunctionalScenario scenario,
        FunctionalRomArtifact rom,
        IFunctionalRomAdapter adapter,
        IFunctionalFrameOracle? oracle,
        FunctionalScenarioRunOptions options,
        IReadOnlyList<FunctionalFrameObservation> frames,
        IReadOnlyList<FunctionalIntegrityFailure> failures)
    {
        var lastFrame = frames.Count - 1;
        var evidenceStart = Math.Max(
            scenario.WarmUpFrames + 1,
            lastFrame - options.EvidenceFramesBeforeFailure);
        var evidence = new List<FunctionalFrameEvidence>(lastFrame - evidenceStart + 1);
        for (var frame = evidenceStart; frame <= lastFrame; frame++)
        {
            var expectation = oracle?.ExpectedFrame(frame);
            if (expectation is not null && expectation.Frame != frame)
            {
                throw new InvalidOperationException(
                    $"The independent visual oracle returned frame {expectation.Frame} while frame {frame} was requested.");
            }

            evidence.Add(new(Normalize(frames[frame]), Normalize(expectation)));
        }

        var summary = Summarize(
            frames,
            failures,
            scenario.WarmUpFrames,
            lastFrame);
        return new FunctionalAcceptanceReport(
            scenario.Id,
            scenario.SampleId,
            scenario.Target,
            adapter.ExecutionSource,
            rom.SourcePath,
            Convert.ToHexString(SHA256.HashData(rom.Bytes)).ToLowerInvariant(),
            new FunctionalFrameWindow(
                scenario.WarmUpFrames,
                lastFrame - scenario.WarmUpFrames,
                lastFrame),
            summary,
            Passed: false,
            TimingChecks: [],
            failures,
            evidence);
    }

    private static bool HasIncompleteCameraRequest(
        FunctionalScenario scenario,
        IReadOnlyList<FunctionalFrameObservation> frames,
        int lastObservationFrame)
    {
        var requests = RequestedSequences(frames, scenario.WarmUpFrames, lastObservationFrame);
        return requests.Any(request =>
            !HasCameraStage(request, frames, frames.Count - 1, observation => observation.ResidentSequence) ||
            !HasCameraStage(request, frames, frames.Count - 1, observation => observation.CommittedSequence) ||
            !HasCameraStage(request, frames, frames.Count - 1, observation => observation.VisibleSequence));
    }

    private static IReadOnlyList<FunctionalTimingCheck> EvaluateTiming(
        FunctionalScenario scenario,
        IReadOnlyList<FunctionalFrameObservation> frames)
    {
        var checks = new List<FunctionalTimingCheck>();
        var start = scenario.WarmUpFrames;
        var end = start + scenario.ObservationFrames;
        if (scenario.ExpectedFeatures.GameplayTicks)
        {
            var gameplayTicks = frames[end].GameplayTicks - frames[start].GameplayTicks;
            var ratio = gameplayTicks / (double)scenario.ObservationFrames;
            checks.Add(MinimumCheck("gameplay-tick-ratio", ratio, scenario.Budgets.MinimumGameplayTickRatio));
            checks.Add(MaximumCheck(
                "gameplay-missed-streak",
                MaximumMissedStreak(frames, start, end, frame => frame.GameplayTicks),
                scenario.Budgets.MaximumConsecutiveMissedGameplayTicks));
        }

        if (scenario.ExpectedFeatures.AudioService)
        {
            var activeFrames = 0;
            var activeTicks = 0L;
            var maximumGap = 0;
            var currentGap = 0;
            for (var frame = start + 1; frame <= end; frame++)
            {
                var delta = frames[frame].AudioServiceTicks - frames[frame - 1].AudioServiceTicks;
                if (!AudioServiceExpected(scenario, frame))
                {
                    currentGap = 0;
                    continue;
                }

                activeFrames++;
                activeTicks += delta;
                if (delta <= 0)
                {
                    currentGap++;
                    maximumGap = Math.Max(maximumGap, currentGap);
                }
                else
                {
                    currentGap = 0;
                }
            }

            checks.Add(MaximumCheck(
                "audio-service-gap",
                maximumGap,
                scenario.Budgets.MaximumUnplannedAudioGapFrames!.Value));
            checks.Add(MaximumCheck(
                "audio-drift",
                Math.Abs(activeTicks - activeFrames),
                scenario.Budgets.MaximumAudioDriftTicks!.Value));
        }

        if (scenario.ExpectedFeatures.AudioProgress
            && frames.Skip(start).Take(end - start + 1).All(frame => frame.AudioProgress is not null))
        {
            var startProgress = frames[start].AudioProgress!;
            var endProgress = frames[end].AudioProgress!;
            var registerEvents = endProgress.RegisterEventCount - startProgress.RegisterEventCount;
            checks.Add(MinimumCheck("audio-register-events", registerEvents, scenario.Audio.MinimumRegisterEvents));
            checks.Add(MaximumCheck("audio-register-events-maximum", registerEvents, scenario.Audio.MaximumRegisterEvents!.Value));
            checks.Add(MaximumCheck(
                "audio-register-event-gap",
                MaximumAudioRegisterEventGap(scenario, frames, start, end),
                scenario.Audio.MaximumRegisterEventGapFrames!.Value));
            AddAudioLifecycleChecks(checks, "sfx", startProgress.SoundEffect, endProgress.SoundEffect,
                scenario.Audio.MinimumSoundEffectStarts, scenario.Audio.MaximumSoundEffectStarts!.Value,
                scenario.Audio.MinimumSoundEffectCompletions, scenario.Audio.MaximumSoundEffectCompletions!.Value,
                scenario.Audio.MaximumSoundEffectRestarts);
            AddAudioLifecycleChecks(checks, "dpcm", startProgress.Dpcm, endProgress.Dpcm,
                scenario.Audio.MinimumDpcmStarts, scenario.Audio.MaximumDpcmStarts!.Value,
                scenario.Audio.MinimumDpcmCompletions, scenario.Audio.MaximumDpcmCompletions!.Value,
                scenario.Audio.MaximumDpcmRestarts);
        }

        var responseInputs = scenario.Inputs.Where(input => input.ResponseSignal is not null).ToArray();
        if (responseInputs.Length > 0)
        {
            var latencies = responseInputs.Select(input => InputLatency(input, frames, end)).ToArray();
            checks.Add(MaximumCheck("input-to-state", latencies.Max(), scenario.Budgets.MaximumInputToStateFrames!.Value));
        }

        if (scenario.ExpectedFeatures.CameraLifecycle)
        {
            var requests = RequestedSequences(frames, start, end);
            var stageEnd = frames.Count - 1;
            if (scenario.Budgets.MaximumRequestToResidentFrames is { } residentLimit)
            {
                var observed = MaximumCameraLatency(requests, frames, stageEnd, observation => observation.ResidentSequence);
                checks.Add(MaximumCheck("request-to-resident", observed, residentLimit));
            }

            if (scenario.Budgets.MaximumRequestToVisibleFrames is { } visibleLimit)
            {
                var observed = MaximumCameraLatency(requests, frames, stageEnd, observation => observation.VisibleSequence);
                checks.Add(MaximumCheck("request-to-visible", observed, visibleLimit));
            }
        }

        if (scenario.Budgets.MaximumSpawnToVisibleFrames is { } spawnLimit)
        {
            var spawns = ActivatedSpawnSequences(frames, start, end);
            var observed = MaximumSpawnLatency(spawns, frames, end, spawnLimit);
            checks.Add(MaximumCheck("spawn-to-visible", observed, spawnLimit));
        }

        return checks;
    }

    private static (
        IReadOnlyList<FunctionalIntegrityFailure> Failures,
        IReadOnlyList<FunctionalFrameEvidence> Evidence) EvaluateIntegrity(
        FunctionalScenario scenario,
        IReadOnlyList<FunctionalFrameObservation> frames,
        IFunctionalFrameOracle? oracle)
    {
        var failures = new List<FunctionalIntegrityFailure>();
        var evidence = new List<FunctionalFrameEvidence>(scenario.ObservationFrames);
        var start = scenario.WarmUpFrames + 1;
        var end = scenario.WarmUpFrames + scenario.ObservationFrames;
        var initialResetCount = frames[0].ResetCount;
        var backgroundExpectationCount = 0;
        var spriteExpectationCount = 0;
        for (var frame = start; frame <= end; frame++)
        {
            var observation = frames[frame];
            var expectation = oracle?.ExpectedFrame(frame);
            if (expectation is not null && expectation.Frame != frame)
            {
                throw new InvalidOperationException(
                    $"The independent visual oracle returned frame {expectation.Frame} while frame {frame} was requested.");
            }

            evidence.Add(new(Normalize(observation), Normalize(expectation)));
            AddFramePrerequisiteFailures(scenario, observation, initialResetCount, failures);

            if (scenario.ExpectedFeatures.Background)
            {
                backgroundExpectationCount += expectation?.Background?.Count ?? 0;
                CompareBackground(frame, expectation?.Background, observation.Background, failures);
            }

            if (scenario.ExpectedFeatures.SpriteOam)
            {
                spriteExpectationCount += expectation?.Sprites?.Count ?? 0;
                CompareSprites(frame, expectation?.Sprites, observation.Sprites, failures);
            }

            AddFramePublicationFailures(scenario, observation, failures);

        }

        if (scenario.ExpectedFeatures.Background && backgroundExpectationCount == 0)
        {
            failures.Add(new(
                "empty-background-oracle",
                start,
                "The independent oracle supplied no authored background locations in the measurement window."));
        }

        if (scenario.ExpectedFeatures.SpriteOam && spriteExpectationCount == 0)
        {
            failures.Add(new(
                "empty-sprite-oracle",
                start,
                "The independent oracle supplied no logical sprites in the measurement window."));
        }

        if (scenario.ExpectedFeatures.AudioProgress
            && frames.Skip(start - 1).Take(end - start + 2).All(frame => frame.AudioProgress is not null))
        {
            var final = frames[end].AudioProgress!;
            CompareAudioActive("music", scenario.Audio.MusicActiveAtEnd, final.Music.Active, end, failures);
            CompareAudioActive("sfx", scenario.Audio.SoundEffectActiveAtEnd, final.SoundEffect.Active, end, failures);
            CompareAudioActive("dpcm", scenario.Audio.DpcmActiveAtEnd, final.Dpcm.Active, end, failures);
            var actualDigest = OrderedAudioEventSha256(frames, start, end);
            if (!string.Equals(actualDigest, scenario.Audio.OrderedRegisterEventSha256, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new(
                    "audio-register-order",
                    end,
                    $"Expected ordered per-frame register digest {scenario.Audio.OrderedRegisterEventSha256}, observed {actualDigest}."));
            }
        }

        if (scenario.ExpectedFeatures.CameraLifecycle)
        {
            ValidateCameraLifecycle(frames, start - 1, end, frames.Count - 1, failures);
        }

        if (scenario.Budgets.MaximumSpawnToVisibleFrames is { } spawnLimit)
        {
            ValidateSpawnLifecycle(frames, start - 1, end, spawnLimit, failures);
        }

        foreach (var checkpointFrame in scenario.Checkpoints.Select(checkpoint => checkpoint.Frame).Distinct())
        {
            AddCheckpointFailures(scenario, frames[checkpointFrame], failures);
        }

        return (failures, evidence);
    }

    private static void AddFramePrerequisiteFailures(
        FunctionalScenario scenario,
        FunctionalFrameObservation observation,
        int initialResetCount,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        var frame = observation.Frame;
        if (observation.ResetCount > initialResetCount)
        {
            failures.Add(new("reset", frame, $"Reset count advanced from {initialResetCount} to {observation.ResetCount}."));
        }

        if (scenario.ExpectedFeatures.CameraLifecycle && observation.Camera is null)
        {
            failures.Add(new("missing-camera-observation", frame, "The scenario requires camera lifecycle observations."));
        }

        if (scenario.ExpectedFeatures.AudioProgress && observation.AudioProgress is null)
        {
            failures.Add(new("missing-audio-progress", frame, "The scenario requires ordered APU/audio progression observations."));
        }

        if (scenario.ExpectedFeatures.BankRestoration)
        {
            if (observation.Bank is null)
            {
                failures.Add(new("missing-bank-observation", frame, "The scenario requires bank restoration observations."));
            }
            else if (!observation.Bank.Restored || observation.Bank.SelectedBank != observation.Bank.ShadowBank)
            {
                failures.Add(new(
                    "bank-restoration",
                    frame,
                    $"{observation.Bank.Domain}: selected bank {observation.Bank.SelectedBank} does not match shadow {observation.Bank.ShadowBank}."));
            }
        }

    }

    private static void AddFramePublicationFailures(
        FunctionalScenario scenario,
        FunctionalFrameObservation observation,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        var frame = observation.Frame;
        if (scenario.Budgets.MaximumSpawnToVisibleFrames is not null && observation.Spawn is null)
        {
            failures.Add(new("missing-spawn-observation", frame, "The scenario requires accepted-spawn lifecycle observations."));
        }

        if (!scenario.ExpectedFeatures.SafeVideoWrites)
        {
            return;
        }

        if (observation.VideoWrites is null || observation.OamWrites is null)
        {
            failures.Add(new("missing-write-timing-observation", frame, "The scenario requires video and OAM write-timing observations."));
            return;
        }

        // Physical-VBlank write timing is a non-blocking diagnostic. Unsafe video and OAM
        // writes are still counted in the summary and surfaced in per-frame evidence, but
        // they do not fail fluidity acceptance. Only malformed timing evidence stays blocking.
        foreach (var write in observation.VideoWrites)
        {
            if (!ValidTiming(write.Timing) || string.IsNullOrWhiteSpace(write.Space) || write.Address < 0)
            {
                failures.Add(new(
                    "invalid-video-write-timing",
                    frame,
                    $"Invalid video-write evidence for {write.Space} at 0x{write.Address:X4}."));
            }
        }

        foreach (var write in observation.OamWrites)
        {
            if (!ValidTiming(write.Timing) || write.Address < 0)
            {
                failures.Add(new(
                    "invalid-oam-write-timing",
                    frame,
                    $"Invalid OAM-write evidence at 0x{write.Address:X4}."));
            }
        }
    }

    private static void AddCheckpointFailures(
        FunctionalScenario scenario,
        FunctionalFrameObservation observation,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        foreach (var checkpoint in scenario.Checkpoints.Where(item => item.Frame == observation.Frame))
        {
            foreach (var expected in checkpoint.ExpectedSignals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (observation.StateSignals is null ||
                    !observation.StateSignals.TryGetValue(expected.Key, out var actual) ||
                    actual != expected.Value)
                {
                    failures.Add(new(
                        "checkpoint",
                        checkpoint.Frame,
                        $"{checkpoint.Id}/{expected.Key}: expected {expected.Value}, observed {(observation.StateSignals?.GetValueOrDefault(expected.Key).ToString() ?? "missing")}."));
                }
            }
        }
    }

    private static FunctionalFrameObservation Normalize(FunctionalFrameObservation observation) => observation with
    {
        StateSignals = observation.StateSignals?
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        Background = observation.Background?.OrderBy(item => item.Location, StringComparer.Ordinal).ToArray(),
        Sprites = observation.Sprites?.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
    };

    private static FunctionalFrameExpectation? Normalize(FunctionalFrameExpectation? expectation) => expectation is null
        ? null
        : expectation with
        {
            Background = expectation.Background?.OrderBy(item => item.Location, StringComparer.Ordinal).ToArray(),
            Sprites = expectation.Sprites?.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        };

    private static bool ValidTiming(FunctionalWriteTimingObservation? timing) =>
        timing is not null &&
        timing.Cycle >= 0 &&
        timing.Scanline >= 0 &&
        timing.Dot >= 0 &&
        !string.IsNullOrWhiteSpace(timing.Phase);

    private static void AddAudioLifecycleChecks(
        ICollection<FunctionalTimingCheck> checks,
        string domain,
        FunctionalAudioPlaybackObservation start,
        FunctionalAudioPlaybackObservation end,
        int minimumStarts,
        int maximumStarts,
        int minimumCompletions,
        int maximumCompletions,
        int maximumRestarts)
    {
        var starts = end.Starts - start.Starts;
        var completions = end.Completions - start.Completions;
        var restarts = end.Restarts - start.Restarts;
        checks.Add(MinimumCheck($"audio-{domain}-starts", starts, minimumStarts));
        checks.Add(MaximumCheck($"audio-{domain}-starts-maximum", starts, maximumStarts));
        checks.Add(MinimumCheck($"audio-{domain}-completions", completions, minimumCompletions));
        checks.Add(MaximumCheck($"audio-{domain}-completions-maximum", completions, maximumCompletions));
        checks.Add(MaximumCheck($"audio-{domain}-restarts", restarts, maximumRestarts));
    }

    private static void CompareAudioActive(
        string domain,
        bool? expected,
        bool actual,
        int frame,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        if (expected is { } value && value != actual)
        {
            failures.Add(new(
                $"audio-{domain}-active",
                frame,
                $"Expected {domain} active={value}, observed active={actual}."));
        }
    }

    private static string OrderedAudioEventSha256(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var frame = start; frame <= end; frame++)
        {
            foreach (var item in frames[frame].AudioProgress!.RegisterEvents)
            {
                AppendInt32LittleEndian(hash, frame - start + 1);
                hash.AppendData(Encoding.UTF8.GetBytes(item.Domain));
                hash.AppendData([0]);
                AppendInt32LittleEndian(hash, item.Address);
                AppendInt32LittleEndian(hash, item.Value);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendInt32LittleEndian(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void CompareBackground(
        int frame,
        IReadOnlyList<FunctionalBackgroundExpectation>? expected,
        IReadOnlyList<FunctionalBackgroundObservation>? actual,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        if (expected is null)
        {
            failures.Add(new("missing-background-expectation", frame, "The independent oracle supplied no retained background expectation."));
            return;
        }

        if (actual is null)
        {
            failures.Add(new("missing-background-observation", frame, "The adapter supplied no retained background observation."));
            return;
        }

        var expectedByLocation = ToUniqueDictionary(expected, item => item.Location, "background expectation", frame);
        var actualByLocation = ToUniqueDictionary(actual, item => item.Location, "background observation", frame);
        foreach (var pair in expectedByLocation.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!actualByLocation.TryGetValue(pair.Key, out var observed))
            {
                failures.Add(new("background-missing", frame, $"{pair.Key}: expected retained background cell is absent."));
                continue;
            }

            if (pair.Value.Tile != observed.Tile)
            {
                failures.Add(new("background-tile", frame, $"{pair.Key}: expected tile {pair.Value.Tile}, observed {observed.Tile}."));
            }

            if (pair.Value.Palette != observed.Palette)
            {
                failures.Add(new("background-palette", frame, $"{pair.Key}: expected palette {pair.Value.Palette}, observed {observed.Palette}."));
            }
        }

        foreach (var location in actualByLocation.Keys.Except(expectedByLocation.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            failures.Add(new("background-unexpected", frame, $"{location}: observed retained background cell has no authored expectation."));
        }
    }

    private static void CompareSprites(
        int frame,
        IReadOnlyList<FunctionalSpriteExpectation>? expected,
        IReadOnlyList<FunctionalSpriteObservation>? actual,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        if (expected is null)
        {
            failures.Add(new("missing-sprite-expectation", frame, "The independent oracle supplied no logical sprite/OAM expectation."));
            return;
        }

        if (actual is null)
        {
            failures.Add(new("missing-sprite-observation", frame, "The adapter supplied no logical sprite/OAM observation."));
            return;
        }

        var expectedById = ToUniqueDictionary(expected, item => item.Id, "sprite expectation", frame);
        var actualById = ToUniqueDictionary(actual, item => item.Id, "sprite observation", frame);
        foreach (var pair in expectedById.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!actualById.TryGetValue(pair.Key, out var observed))
            {
                failures.Add(new("sprite-missing", frame, $"{pair.Key}: expected logical sprite is absent."));
                continue;
            }

            if (pair.Value.Visible != observed.Visible)
            {
                failures.Add(new(
                    "sprite-visibility",
                    frame,
                    $"{pair.Key}: expected visible={pair.Value.Visible}, observed visible={observed.Visible}."));
            }

            if (pair.Value.OamSlot >= 0 && pair.Value.OamSlot != observed.OamSlot)
            {
                failures.Add(new(
                    "sprite-oam-slot",
                    frame,
                    $"{pair.Key}: expected OAM slot {pair.Value.OamSlot}, observed {observed.OamSlot}."));
            }

            if (!pair.Value.Oam.SequenceEqual(observed.Oam))
            {
                failures.Add(new(
                    "sprite-oam",
                    frame,
                    $"{pair.Key}: expected [{string.Join(',', pair.Value.Oam)}], observed [{string.Join(',', observed.Oam)}]."));
            }
        }

        foreach (var id in actualById.Keys.Except(expectedById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            failures.Add(new("sprite-unexpected", frame, $"{id}: observed logical sprite has no authored expectation."));
        }
    }

    private static void ValidateCameraLifecycle(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end,
        int stageEnd,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        var requests = RequestedSequences(frames, start, end);
        if (requests.Count == 0)
        {
            failures.Add(new("missing-camera-request", start + 1, "No camera request sequence was observed in the measurement window."));
            return;
        }

        foreach (var request in requests)
        {
            RequireCameraStage(request, frames, stageEnd, observation => observation.ResidentSequence, "camera-not-resident", failures);
            RequireCameraStage(request, frames, stageEnd, observation => observation.CommittedSequence, "camera-not-committed", failures);
            RequireCameraStage(request, frames, stageEnd, observation => observation.VisibleSequence, "camera-not-visible", failures);
        }

        var knownRequests = requests.Select(request => request.Sequence).ToHashSet();
        if (frames[start].Camera?.RequestedSequence is { } baselineRequest)
        {
            knownRequests.Add(baselineRequest);
        }

        var previous = frames[start].Camera;
        foreach (var frame in frames.Skip(start + 1).Take(end - start))
        {
            var camera = frame.Camera;
            if (camera is null)
            {
                previous = null;
                continue;
            }

            var transitions = new[]
            {
                (Current: camera.ResidentSequence, Previous: previous?.ResidentSequence),
                (Current: camera.CommittedSequence, Previous: previous?.CommittedSequence),
                (Current: camera.VisibleSequence, Previous: previous?.VisibleSequence),
            };
            foreach (var transition in transitions)
            {
                foreach (var value in SequenceTransitions(transition.Previous, transition.Current))
                {
                    if (!knownRequests.Contains(value))
                    {
                        failures.Add(new(
                            "unexpected-camera-sequence",
                            frame.Frame,
                            $"Camera stage published sequence {value} without a matching request in the measurement window."));
                    }
                }
            }

            previous = camera;
        }
    }

    private static void RequireCameraStage(
        (long Sequence, int Frame) request,
        IReadOnlyList<FunctionalFrameObservation> frames,
        int end,
        Func<FunctionalCameraLifecycleObservation, long?> stage,
        string failureCode,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        if (HasCameraStage(request, frames, end, stage))
        {
            return;
        }

        failures.Add(new(failureCode, request.Frame, $"Camera request sequence {request.Sequence} did not complete this stage."));
    }

    private static bool HasCameraStage(
        (long Sequence, int Frame) request,
        IReadOnlyList<FunctionalFrameObservation> frames,
        int end,
        Func<FunctionalCameraLifecycleObservation, long?> stage)
    {
        for (var frame = request.Frame; frame <= end; frame++)
        {
            if (frames[frame].Camera is { } camera && stage(camera) is { } sequence && sequence >= request.Sequence)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, T> ToUniqueDictionary<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string kind,
        int frame)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var key = keySelector(value);
            if (string.IsNullOrWhiteSpace(key) || !result.TryAdd(key, value))
            {
                throw new InvalidOperationException($"Frame {frame} has an empty or duplicate {kind} key '{key}'.");
            }
        }

        return result;
    }

    private static FunctionalObservationSummary Summarize(
        FunctionalScenario scenario,
        IReadOnlyList<FunctionalFrameObservation> frames,
        IReadOnlyList<FunctionalIntegrityFailure> failures) =>
        Summarize(
            frames,
            failures,
            scenario.WarmUpFrames,
            scenario.WarmUpFrames + scenario.ObservationFrames);

    private static FunctionalObservationSummary Summarize(
        IReadOnlyList<FunctionalFrameObservation> frames,
        IReadOnlyList<FunctionalIntegrityFailure> failures,
        int start,
        int end)
    {
        var window = frames.Skip(start + 1).Take(end - start).ToArray();
        return new FunctionalObservationSummary(
            frames[end].GameplayTicks - frames[start].GameplayTicks,
            frames[end].AudioServiceTicks - frames[start].AudioServiceTicks,
            window.Max(frame => frame.ResetCount),
            CountCameraTransitions(frames, start, end, camera => camera.RequestedSequence),
            CountCameraTransitions(frames, start, end, camera => camera.ResidentSequence),
            CountCameraTransitions(frames, start, end, camera => camera.CommittedSequence),
            CountCameraTransitions(frames, start, end, camera => camera.VisibleSequence),
            window.Count(frame => frame.Bank is { } bank && (!bank.Restored || bank.SelectedBank != bank.ShadowBank)),
            window.Sum(frame => frame.VideoWrites?.Count(write => !write.Safe) ?? 0),
            window.Sum(frame => frame.OamWrites?.Count(write => !write.Safe) ?? 0),
            failures.Count(failure => failure.Code.Contains("background", StringComparison.Ordinal)),
            failures.Count(failure => failure.Code.Contains("sprite", StringComparison.Ordinal)));
    }

    private static int CountCameraTransitions(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end,
        Func<FunctionalCameraLifecycleObservation, long?> sequence)
    {
        var seen = new HashSet<long>();
        var previous = frames[start].Camera is { } baseline ? sequence(baseline) : null;
        for (var frame = start + 1; frame <= end; frame++)
        {
            var current = frames[frame].Camera is { } camera ? sequence(camera) : null;
            foreach (var value in SequenceTransitions(previous, current))
            {
                seen.Add(value);
            }

            previous = current;
        }

        return seen.Count;
    }
    private static int MaximumMissedStreak(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end,
        Func<FunctionalFrameObservation, long> counter)
    {
        var maximum = 0;
        var current = 0;
        for (var frame = start + 1; frame <= end; frame++)
        {
            if (counter(frames[frame]) <= counter(frames[frame - 1]))
            {
                current++;
                maximum = Math.Max(maximum, current);
            }
            else
            {
                current = 0;
            }
        }

        return maximum;
    }

    private static int InputLatency(
        FunctionalInputSpan input,
        IReadOnlyList<FunctionalFrameObservation> frames,
        int lastFrame)
    {
        var signal = input.ResponseSignal!;
        var baselineFrame = Math.Max(0, input.StartFrame - 1);
        if (frames[baselineFrame].StateSignals is null ||
            !frames[baselineFrame].StateSignals!.TryGetValue(signal, out var baseline))
        {
            return lastFrame - input.StartFrame + 1;
        }

        for (var frame = input.StartFrame; frame <= lastFrame; frame++)
        {
            if (frames[frame].StateSignals is { } signals &&
                signals.TryGetValue(signal, out var current) &&
                current != baseline)
            {
                return frame - input.StartFrame;
            }
        }

        return lastFrame - input.StartFrame + 1;
    }

    private static IReadOnlyList<(long Sequence, int Frame)> RequestedSequences(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end)
    {
        var requests = new List<(long Sequence, int Frame)>();
        var seen = new HashSet<long>();
        var previous = frames[start].Camera?.RequestedSequence;
        for (var frame = start + 1; frame <= end; frame++)
        {
            var current = frames[frame].Camera?.RequestedSequence;
            foreach (var sequence in SequenceTransitions(previous, current))
            {
                if (seen.Add(sequence))
                {
                    requests.Add((sequence, frame));
                }
            }

            previous = current;
        }

        return requests;
    }

    private static int MaximumCameraLatency(
        IReadOnlyList<(long Sequence, int Frame)> requests,
        IReadOnlyList<FunctionalFrameObservation> frames,
        int end,
        Func<FunctionalCameraLifecycleObservation, long?> completedSequence)
    {
        if (requests.Count == 0)
        {
            return 0;
        }

        return requests.Max(request =>
        {
            for (var frame = request.Frame; frame <= end; frame++)
            {
                if (frames[frame].Camera is { } camera &&
                    completedSequence(camera) is { } sequence &&
                    sequence >= request.Sequence)
                {
                    return frame - request.Frame;
                }
            }

            return end - request.Frame + 1;
        });
    }

    private static IReadOnlyList<(long Sequence, int Frame)> ActivatedSpawnSequences(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end)
    {
        var spawns = new List<(long Sequence, int Frame)>();
        var seen = new HashSet<long>();
        var previous = frames[start].Spawn?.ActivatedSequence;
        for (var frame = start + 1; frame <= end; frame++)
        {
            var current = frames[frame].Spawn?.ActivatedSequence;
            foreach (var sequence in SequenceTransitions(previous, current))
            {
                if (seen.Add(sequence))
                {
                    spawns.Add((sequence, frame));
                }
            }

            previous = current;
        }

        return spawns;
    }

    private static int MaximumSpawnLatency(
        IReadOnlyList<(long Sequence, int Frame)> spawns,
        IReadOnlyList<FunctionalFrameObservation> frames,
        int end,
        int limit)
    {
        if (spawns.Count == 0)
        {
            return limit + 1;
        }

        return spawns.Max(spawn =>
        {
            for (var frame = spawn.Frame; frame <= end; frame++)
            {
                if (frames[frame].Spawn?.VisibleSequence == spawn.Sequence)
                {
                    return frame - spawn.Frame;
                }
            }

            return limit + 1;
        });
    }

    private static void ValidateSpawnLifecycle(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end,
        int limit,
        ICollection<FunctionalIntegrityFailure> failures)
    {
        var spawns = ActivatedSpawnSequences(frames, start, end);
        if (spawns.Count == 0)
        {
            failures.Add(new(
                "missing-spawn-transition",
                start + 1,
                "A spawn-to-visible budget requires at least one accepted spawn transition in the measurement window."));
            return;
        }

        long? previousVisible = frames[start].Spawn?.VisibleSequence;
        for (var frame = start + 1; frame <= end; frame++)
        {
            var visible = frames[frame].Spawn?.VisibleSequence;
            if (visible is { } current && previousVisible is { } previous && current > previous + 1)
            {
                failures.Add(new(
                    "spawn-visible-sequence-gap",
                    frame,
                    $"Visible spawn watermark jumped from {previous} to {current}; later visibility cannot complete a missing earlier spawn."));
            }

            previousVisible = visible;
        }

        foreach (var spawn in spawns)
        {
            var completionEnd = Math.Min(end, spawn.Frame + limit);
            var completed = Enumerable.Range(spawn.Frame, completionEnd - spawn.Frame + 1)
                .Any(frame => frames[frame].Spawn?.VisibleSequence == spawn.Sequence);
            if (!completed)
            {
                failures.Add(new(
                    "spawn-not-visible-within-budget",
                    spawn.Frame,
                    $"Accepted spawn sequence {spawn.Sequence} did not become the contiguous visible watermark within {limit} frame(s)."));
            }
        }
    }

    private static FunctionalTimingCheck MinimumCheck(string metric, double observed, double limit) =>
        new(metric, observed, limit, observed - limit, ">=", observed >= limit);

    private static FunctionalTimingCheck MaximumCheck(string metric, double observed, double limit) =>
        new(metric, observed, limit, limit - observed, "<=", observed <= limit);

    private static IEnumerable<long> SequenceTransitions(long? previous, long? current)
    {
        if (current is not { } value || current == previous)
        {
            yield break;
        }

        if (previous is { } prior && value > prior)
        {
            for (var sequence = prior + 1; sequence <= value; sequence++)
            {
                yield return sequence;
            }

            yield break;
        }

        yield return value;
    }

    private static void ValidateObservationFrame(
        FunctionalFrameObservation observation,
        int expectedFrame,
        FunctionalFrameObservation? previous)
    {
        if (observation is null)
        {
            throw new InvalidOperationException($"The functional adapter returned no observation for frame {expectedFrame}.");
        }

        if (observation.Frame != expectedFrame)
        {
            throw new InvalidOperationException(
                $"The functional adapter returned frame {observation.Frame} while frame {expectedFrame} was requested.");
        }

        if (observation.GameplayTicks < 0 || observation.AudioServiceTicks < 0 || observation.ResetCount < 0)
        {
            throw new InvalidOperationException($"Frame {expectedFrame} contains a negative cumulative counter.");
        }

        if (previous is not null &&
            (observation.GameplayTicks < previous.GameplayTicks ||
             observation.AudioServiceTicks < previous.AudioServiceTicks ||
             observation.ResetCount < previous.ResetCount))
        {
            throw new InvalidOperationException(
                $"Frame {expectedFrame} regressed a cumulative gameplay, audio-service, or reset counter.");
        }
        if (observation.AudioProgress is { } currentAudio)
        {
            if (currentAudio.RegisterEvents.Any(item =>
                    string.IsNullOrWhiteSpace(item.Domain) || item.Address < 0 || item.Value is < 0 or > 255))
            {
                throw new InvalidOperationException($"Frame {expectedFrame} contains an invalid ordered audio register event.");
            }

            if (previous?.AudioProgress is { } previousAudio)
            {
                var currentCounters = AudioCounters(currentAudio);
                var previousCounters = AudioCounters(previousAudio);
                if (currentCounters.Where((value, index) => value < previousCounters[index]).Any())
                {
                    throw new InvalidOperationException($"Frame {expectedFrame} regressed a cumulative audio-progress counter.");
                }

                if (currentAudio.RegisterEventCount - previousAudio.RegisterEventCount != currentAudio.RegisterEvents.Count)
                {
                    throw new InvalidOperationException(
                        $"Frame {expectedFrame} register event count does not match its ordered per-frame event list.");
                }
            }
        }
    }

    private static long[] AudioCounters(FunctionalAudioProgressObservation audio) =>
    [
        audio.RegisterEventCount,
        audio.Music.Starts, audio.Music.Completions, audio.Music.Restarts,
        audio.SoundEffect.Starts, audio.SoundEffect.Completions, audio.SoundEffect.Restarts,
        audio.Dpcm.Starts, audio.Dpcm.Completions, audio.Dpcm.Restarts,
    ];

    private static bool AudioServiceExpected(FunctionalScenario scenario, int frame)
        => scenario.Audio.ServiceExpectedByDefault;

    private static int MaximumAudioRegisterEventGap(
        FunctionalScenario scenario,
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end)
    {
        var maximum = 0;
        var current = 0;
        for (var frame = start + 1; frame <= end; frame++)
        {
            if (scenario.Audio.AuthoredSilence.Any(span =>
                    frame >= span.StartFrame && frame < checked(span.StartFrame + span.DurationFrames)))
            {
                current = 0;
                continue;
            }

            if (frames[frame].AudioProgress!.RegisterEventCount == frames[frame - 1].AudioProgress!.RegisterEventCount)
            {
                current++;
                maximum = Math.Max(maximum, current);
            }
            else
            {
                current = 0;
            }
        }

        return maximum;
    }

    private static void ValidateCapabilities(FunctionalScenario scenario, IFunctionalRomAdapter adapter)
    {
        var capabilities = adapter.Capabilities;
        var missing = new List<string>();
        if (scenario.Inputs.Count > 0 && !capabilities.InputTimeline) missing.Add("input-timeline");
        if (scenario.ExpectedFeatures.GameplayTicks && !capabilities.GameplayTicks) missing.Add("gameplay-ticks");
        if (scenario.ExpectedFeatures.AudioService && !capabilities.AudioService) missing.Add("audio-service");
        if (scenario.ExpectedFeatures.AudioProgress && !capabilities.AudioProgress) missing.Add("audio-progress");
        if (scenario.ExpectedFeatures.CameraLifecycle && !capabilities.CameraLifecycle) missing.Add("camera-lifecycle");
        if (scenario.ExpectedFeatures.Background && !capabilities.Background) missing.Add("background");
        if (scenario.ExpectedFeatures.SpriteOam && !capabilities.SpriteOam) missing.Add("sprite-oam");
        if (scenario.ExpectedFeatures.BankRestoration && !capabilities.BankRestoration) missing.Add("bank-restoration");
        if (scenario.ExpectedFeatures.SafeVideoWrites && !capabilities.VideoWriteTiming) missing.Add("video-write-timing");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Adapter for target {adapter.Target.StableId()} via {adapter.ExecutionSource.StableId()} " +
                $"in scenario '{scenario.Id}' lacks required observations: {string.Join(", ", missing)}.");
        }
    }
}
