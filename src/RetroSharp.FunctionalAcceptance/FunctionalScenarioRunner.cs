namespace RetroSharp.FunctionalAcceptance;

using System.Security.Cryptography;

public static class FunctionalScenarioRunner
{
    public static FunctionalAcceptanceReport Run(
        FunctionalScenario scenario,
        FunctionalRomArtifact rom,
        IFunctionalRomAdapter adapter,
        IFunctionalFrameOracle? oracle = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(rom);
        ArgumentNullException.ThrowIfNull(adapter);
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

            if (observation.ResetCount > initialResetCount)
            {
                failures.Add(new("reset", frame, $"Reset count advanced from {initialResetCount} to {observation.ResetCount}."));
            }

            if (scenario.ExpectedFeatures.CameraLifecycle && observation.Camera is null)
            {
                failures.Add(new("missing-camera-observation", frame, "The scenario requires camera lifecycle observations."));
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

            if (scenario.ExpectedFeatures.SafeVideoWrites)
            {
                if (observation.VideoWrites is null || observation.OamWrites is null)
                {
                    failures.Add(new("missing-write-timing-observation", frame, "The scenario requires video and OAM write-timing observations."));
                }
                else
                {
                    foreach (var write in observation.VideoWrites)
                    {
                        if (!ValidTiming(write.Timing) || string.IsNullOrWhiteSpace(write.Space) || write.Address < 0)
                        {
                            failures.Add(new(
                                "invalid-video-write-timing",
                                frame,
                                $"Invalid video-write evidence for {write.Space} at 0x{write.Address:X4}."));
                        }

                        if (!write.Safe)
                        {
                            failures.Add(new(
                                "unsafe-video-write",
                                frame,
                                $"Unsafe {write.Space} write at 0x{write.Address:X4} ({write.Timing.DiagnosticText(write.Safe)})."));
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

                        if (!write.Safe)
                        {
                            failures.Add(new(
                                "unsafe-oam-write",
                                frame,
                                $"Unsafe OAM write at 0x{write.Address:X4} ({write.Timing.DiagnosticText(write.Safe)})."));
                        }
                    }
                }
            }
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

        if (scenario.ExpectedFeatures.CameraLifecycle)
        {
            ValidateCameraLifecycle(frames, start - 1, end, frames.Count - 1, failures);
        }

        foreach (var checkpoint in scenario.Checkpoints)
        {
            var signals = frames[checkpoint.Frame].StateSignals;
            foreach (var expected in checkpoint.ExpectedSignals.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (signals is null || !signals.TryGetValue(expected.Key, out var actual) || actual != expected.Value)
                {
                    failures.Add(new(
                        "checkpoint",
                        checkpoint.Frame,
                        $"{checkpoint.Id}/{expected.Key}: expected {expected.Value}, observed {(signals?.GetValueOrDefault(expected.Key).ToString() ?? "missing")}."));
                }
            }
        }

        return (failures, evidence);
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
                if (transition.Current is { } value &&
                    transition.Current != transition.Previous &&
                    !knownRequests.Contains(value))
                {
                    failures.Add(new(
                        "unexpected-camera-sequence",
                        frame.Frame,
                        $"Camera stage published sequence {value} without a matching request in the measurement window."));
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
            if (frames[frame].Camera is { } camera && stage(camera) == request.Sequence)
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
        IReadOnlyList<FunctionalIntegrityFailure> failures)
    {
        var start = scenario.WarmUpFrames;
        var end = start + scenario.ObservationFrames;
        var window = frames.Skip(start + 1).Take(scenario.ObservationFrames).ToArray();
        var camera = window.Where(frame => frame.Camera is not null).Select(frame => frame.Camera!).ToArray();
        return new FunctionalObservationSummary(
            frames[end].GameplayTicks - frames[start].GameplayTicks,
            frames[end].AudioServiceTicks - frames[start].AudioServiceTicks,
            window.Max(frame => frame.ResetCount),
            camera.Select(item => item.RequestedSequence).Where(item => item is not null).Distinct().Count(),
            camera.Select(item => item.ResidentSequence).Where(item => item is not null).Distinct().Count(),
            camera.Select(item => item.CommittedSequence).Where(item => item is not null).Distinct().Count(),
            camera.Select(item => item.VisibleSequence).Where(item => item is not null).Distinct().Count(),
            window.Count(frame => frame.Bank is { } bank && (!bank.Restored || bank.SelectedBank != bank.ShadowBank)),
            window.Sum(frame => frame.VideoWrites?.Count(write => !write.Safe) ?? 0),
            window.Sum(frame => frame.OamWrites?.Count(write => !write.Safe) ?? 0),
            failures.Count(failure => failure.Code.Contains("background", StringComparison.Ordinal)),
            failures.Count(failure => failure.Code.Contains("sprite", StringComparison.Ordinal)));
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
            if (current is { } sequence && current != previous && seen.Add(sequence))
            {
                requests.Add((sequence, frame));
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
                if (frames[frame].Camera is { } camera && completedSequence(camera) == request.Sequence)
                {
                    return frame - request.Frame;
                }
            }

            return end - request.Frame + 1;
        });
    }

    private static FunctionalTimingCheck MinimumCheck(string metric, double observed, double limit) =>
        new(metric, observed, limit, observed - limit, ">=", observed >= limit);

    private static FunctionalTimingCheck MaximumCheck(string metric, double observed, double limit) =>
        new(metric, observed, limit, limit - observed, "<=", observed <= limit);

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
    }

    private static bool AudioServiceExpected(FunctionalScenario scenario, int frame)
    {
        if (!scenario.Audio.ServiceExpectedByDefault)
        {
            return false;
        }

        return !scenario.Audio.AuthoredSilence.Any(span =>
            frame >= span.StartFrame && frame < checked(span.StartFrame + span.DurationFrames));
    }

    private static void ValidateCapabilities(FunctionalScenario scenario, IFunctionalRomAdapter adapter)
    {
        var capabilities = adapter.Capabilities;
        var missing = new List<string>();
        if (scenario.Inputs.Count > 0 && !capabilities.InputTimeline) missing.Add("input-timeline");
        if (scenario.ExpectedFeatures.GameplayTicks && !capabilities.GameplayTicks) missing.Add("gameplay-ticks");
        if (scenario.ExpectedFeatures.AudioService && !capabilities.AudioService) missing.Add("audio-service");
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
