namespace RetroSharp.FunctionalAcceptance;

internal static class FunctionalScenarioValidator
{
    public static void Validate(FunctionalScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RequireText(scenario.Id, "id");
        RequireText(scenario.SampleId, "sampleId");
        if (!Enum.IsDefined(scenario.Target))
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' has an unknown target.");
        }

        if (scenario.WarmUpFrames < 0)
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' warmUpFrames cannot be negative.");
        }

        if (scenario.ObservationFrames <= 0)
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' observationFrames must be positive.");
        }

        if (scenario.Inputs is null ||
            scenario.Checkpoints is null ||
            scenario.ExpectedFeatures is null ||
            scenario.Audio is null ||
            scenario.BudgetEvidence is null ||
            scenario.Budgets is null)
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' must declare inputs, checkpoints, expectedFeatures, audio, budgetEvidence and budgets.");
        }

        var lastFrame = checked(scenario.WarmUpFrames + scenario.ObservationFrames);
        EnsureUnique(scenario.Inputs.Select(input => input.Id), scenario.Id, "input");
        foreach (var input in scenario.Inputs)
        {
            RequireText(input.Id, "input id");
            if (input.StartFrame <= 0 || input.DurationFrames <= 0 || input.StartFrame + input.DurationFrames - 1 > lastFrame)
            {
                throw new InvalidOperationException(
                    $"Functional scenario '{scenario.Id}' input '{input.Id}' must fit in frames 1..{lastFrame}.");
            }

            if (input.Buttons is null || input.Buttons.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException($"Functional scenario '{scenario.Id}' input '{input.Id}' has an invalid button.");
            }

            if (input.ResponseSignal is not null)
            {
                RequireText(input.ResponseSignal, $"input '{input.Id}' responseSignal");
            }
        }

        EnsureUnique(scenario.Checkpoints.Select(checkpoint => checkpoint.Id), scenario.Id, "checkpoint");
        foreach (var checkpoint in scenario.Checkpoints)
        {
            RequireText(checkpoint.Id, "checkpoint id");
            if (checkpoint.Frame < 0 || checkpoint.Frame > lastFrame || checkpoint.ExpectedSignals is null)
            {
                throw new InvalidOperationException(
                    $"Functional scenario '{scenario.Id}' checkpoint '{checkpoint.Id}' is outside frames 0..{lastFrame} or has no expectedSignals.");
            }
        }

        if (scenario.Audio.AuthoredSilence is null)
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' must declare authoredSilence, even when it is empty.");
        }

        var previousSilenceEnd = 0;
        foreach (var silence in scenario.Audio.AuthoredSilence.OrderBy(span => span.StartFrame))
        {
            var silenceEnd = checked(silence.StartFrame + silence.DurationFrames);
            if (silence.StartFrame <= 0 || silence.DurationFrames <= 0 || silenceEnd - 1 > lastFrame)
            {
                throw new InvalidOperationException(
                    $"Functional scenario '{scenario.Id}' authored silence must fit in frames 1..{lastFrame}.");
            }

            if (silence.StartFrame < previousSilenceEnd)
            {
                throw new InvalidOperationException(
                    $"Functional scenario '{scenario.Id}' declares overlapping authored-silence spans at frame {silence.StartFrame}.");
            }

            previousSilenceEnd = silenceEnd;
        }

        RequireText(scenario.BudgetEvidence.BaselineCommit, "budgetEvidence baselineCommit");
        RequireText(scenario.BudgetEvidence.HardwareTimingRationale, "budgetEvidence hardwareTimingRationale");
        RequireText(scenario.BudgetEvidence.ProductionTraceRationale, "budgetEvidence productionTraceRationale");
        if (scenario.BudgetEvidence.BaselineCommit.Length != 40 ||
            scenario.BudgetEvidence.BaselineCommit.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' budgetEvidence baselineCommit must be a full 40-character commit SHA.");
        }

        if (scenario.Budgets.MinimumGameplayTickRatio < 0 ||
            scenario.Budgets.MinimumGameplayTickRatio > 1 ||
            double.IsNaN(scenario.Budgets.MinimumGameplayTickRatio) ||
            double.IsInfinity(scenario.Budgets.MinimumGameplayTickRatio) ||
            scenario.Budgets.MaximumConsecutiveMissedGameplayTicks < 0)
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' has invalid gameplay timing budgets.");
        }

        var optionalBudgets = new int?[]
        {
            scenario.Budgets.MaximumInputToStateFrames,
            scenario.Budgets.MaximumRequestToResidentFrames,
            scenario.Budgets.MaximumRequestToVisibleFrames,
            scenario.Budgets.MaximumUnplannedAudioGapFrames,
            scenario.Budgets.MaximumAudioDriftTicks,
            scenario.Budgets.MaximumSpawnToVisibleFrames,
        };
        if (optionalBudgets.Any(value => value < 0))
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' timing budgets cannot be negative.");
        }

        if (scenario.Inputs.Any(input => input.ResponseSignal is not null) && scenario.Budgets.MaximumInputToStateFrames is null)
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' response signals require maximumInputToStateFrames.");
        }

        if (scenario.ExpectedFeatures.AudioService &&
            (scenario.Budgets.MaximumUnplannedAudioGapFrames is null || scenario.Budgets.MaximumAudioDriftTicks is null))
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' audio acceptance requires service-gap and drift budgets.");
        }
        if (scenario.ExpectedFeatures.AudioService != scenario.Audio.ServiceExpectedByDefault)
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' audio.serviceExpectedByDefault must match expectedFeatures.audioService.");
        }
        if (scenario.Audio.MinimumRegisterEvents < 0 ||
            scenario.Audio.MaximumRegisterEvents < 0 ||
            scenario.Audio.MaximumRegisterEventGapFrames < 0 ||
            scenario.Audio.MinimumSoundEffectStarts < 0 ||
            scenario.Audio.MaximumSoundEffectStarts < 0 ||
            scenario.Audio.MinimumSoundEffectCompletions < 0 ||
            scenario.Audio.MaximumSoundEffectCompletions < 0 ||
            scenario.Audio.MaximumSoundEffectRestarts < 0 ||
            scenario.Audio.MinimumDpcmStarts < 0 ||
            scenario.Audio.MaximumDpcmStarts < 0 ||
            scenario.Audio.MinimumDpcmCompletions < 0 ||
            scenario.Audio.MaximumDpcmCompletions < 0 ||
            scenario.Audio.MaximumDpcmRestarts < 0)
        {
            throw new InvalidOperationException($"Functional scenario '{scenario.Id}' audio progress budgets cannot be negative.");
        }
        if (scenario.ExpectedFeatures.AudioProgress &&
            (scenario.Audio.MaximumRegisterEvents is null ||
             scenario.Audio.MaximumRegisterEventGapFrames is null ||
             scenario.Audio.MaximumSoundEffectStarts is null ||
             scenario.Audio.MaximumSoundEffectCompletions is null ||
             scenario.Audio.MaximumDpcmStarts is null ||
             scenario.Audio.MaximumDpcmCompletions is null ||
             scenario.Audio.MusicActiveAtEnd is null ||
             scenario.Audio.SoundEffectActiveAtEnd is null ||
             scenario.Audio.DpcmActiveAtEnd is null ||
             string.IsNullOrWhiteSpace(scenario.Audio.OrderedRegisterEventSha256)))
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' audio progress acceptance requires exact upper bounds, end lifecycle states, and an ordered register-event digest.");
        }
        if (scenario.ExpectedFeatures.AudioProgress &&
            (scenario.Audio.MaximumRegisterEvents < scenario.Audio.MinimumRegisterEvents ||
             scenario.Audio.MaximumSoundEffectStarts < scenario.Audio.MinimumSoundEffectStarts ||
             scenario.Audio.MaximumSoundEffectCompletions < scenario.Audio.MinimumSoundEffectCompletions ||
             scenario.Audio.MaximumDpcmStarts < scenario.Audio.MinimumDpcmStarts ||
             scenario.Audio.MaximumDpcmCompletions < scenario.Audio.MinimumDpcmCompletions))
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' audio progress maximums cannot be lower than their minimums.");
        }
        if (scenario.ExpectedFeatures.AudioProgress &&
            (scenario.Audio.OrderedRegisterEventSha256!.Length != 64 ||
             scenario.Audio.OrderedRegisterEventSha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' orderedRegisterEventSha256 must be a 64-character SHA-256 hex digest.");
        }

        if (scenario.ExpectedFeatures.CameraLifecycle &&
            scenario.Budgets.MaximumRequestToResidentFrames is null &&
            scenario.Budgets.MaximumRequestToVisibleFrames is null)
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' camera acceptance requires a residency or visibility latency budget.");
        }

        if (scenario.Budgets.MaximumSpawnToVisibleFrames is not null && !scenario.ExpectedFeatures.SpriteOam)
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenario.Id}' spawn-to-visible acceptance requires sprite/OAM observations.");
        }
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Functional scenario {name} cannot be empty.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string scenarioId, string kind)
    {
        var duplicate = values.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Functional scenario '{scenarioId}' declares duplicate {kind} id '{duplicate.Key}'.");
        }
    }
}
