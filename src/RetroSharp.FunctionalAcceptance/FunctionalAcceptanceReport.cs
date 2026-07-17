namespace RetroSharp.FunctionalAcceptance;

using System.Globalization;
using System.Text;
using System.Text.Json;

public sealed record FunctionalTimingCheck(
    string Metric,
    double Observed,
    double Limit,
    double Headroom,
    string Comparison,
    bool Passed);

public sealed record FunctionalIntegrityFailure(
    string Code,
    int Frame,
    string Detail);

public sealed record FunctionalFrameWindow(
    int WarmUpFrames,
    int ObservationFrames,
    int TotalPhysicalFrames);

public sealed record FunctionalObservationSummary(
    long GameplayTicks,
    long AudioServiceTicks,
    int ResetCount,
    int CameraRequests,
    int CameraResidents,
    int CameraCommits,
    int CameraVisible,
    int BankRestorationFailures,
    int UnsafeVideoWrites,
    int UnsafeOamWrites,
    int BackgroundMismatches,
    int SpriteMismatches);

public sealed record FunctionalFrameEvidence(
    FunctionalFrameObservation Observed,
    FunctionalFrameExpectation? Expected);

public sealed record FunctionalAcceptanceReport(
    string ScenarioId,
    string SampleId,
    FunctionalTarget Target,
    FunctionalExecutionSource ExecutionSource,
    string RomPath,
    string RomSha256,
    FunctionalFrameWindow FrameWindow,
    FunctionalObservationSummary Summary,
    bool Passed,
    IReadOnlyList<FunctionalTimingCheck> TimingChecks,
    IReadOnlyList<FunctionalIntegrityFailure> IntegrityFailures,
    IReadOnlyList<FunctionalFrameEvidence> FrameEvidence)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public string ToHumanReadable()
    {
        var builder = new StringBuilder();
        builder.Append("scenario=").Append(ScenarioId)
            .Append(" sample=").Append(SampleId)
            .Append(" target=").Append(Target.StableId())
            .Append(" source=").Append(ExecutionSource.StableId())
            .Append(" passed=").AppendLine(Passed ? "true" : "false");
        builder.Append("rom=").Append(RomPath)
            .Append(" sha256=").AppendLine(RomSha256);
        builder.Append("frames warmUp=").Append(FrameWindow.WarmUpFrames)
            .Append(" observation=").Append(FrameWindow.ObservationFrames)
            .Append(" totalPhysical=").Append(FrameWindow.TotalPhysicalFrames)
            .AppendLine();
        builder.Append("summary gameplayTicks=").Append(Summary.GameplayTicks)
            .Append(" audioServiceTicks=").Append(Summary.AudioServiceTicks)
            .Append(" resets=").Append(Summary.ResetCount)
            .Append(" camera=").Append(Summary.CameraRequests).Append('/')
            .Append(Summary.CameraResidents).Append('/')
            .Append(Summary.CameraCommits).Append('/')
            .Append(Summary.CameraVisible)
            .Append(" bankFailures=").Append(Summary.BankRestorationFailures)
            .Append(" unsafeVideoWrites=").Append(Summary.UnsafeVideoWrites)
            .Append(" unsafeOamWrites=").Append(Summary.UnsafeOamWrites)
            .Append(" backgroundMismatches=").Append(Summary.BackgroundMismatches)
            .Append(" spriteMismatches=").Append(Summary.SpriteMismatches)
            .AppendLine();
        foreach (var check in TimingChecks)
        {
            builder.Append("timing metric=").Append(check.Metric)
                .Append(" passed=").Append(check.Passed ? "true" : "false")
                .Append(" observed=").Append(Format(check.Observed))
                .Append(" limit=").Append(Format(check.Limit))
                .Append(" headroom=").Append(Format(check.Headroom))
                .Append(" comparison=").AppendLine(check.Comparison);
        }

        foreach (var failure in IntegrityFailures)
        {
            builder.Append("integrity code=").Append(failure.Code)
                .Append(" frame=").Append(failure.Frame)
                .Append(" detail=").AppendLine(failure.Detail);
        }

        foreach (var evidence in FrameEvidence)
        {
            AppendEvidence(builder, evidence);
        }

        return builder.ToString();
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void AppendEvidence(StringBuilder builder, FunctionalFrameEvidence evidence)
    {
        var observed = evidence.Observed;
        builder.Append("frame physical=").Append(observed.Frame)
            .Append(" gameplayTicks=").Append(observed.GameplayTicks)
            .Append(" audioServiceTicks=").Append(observed.AudioServiceTicks)
            .Append(" resets=").Append(observed.ResetCount);
        if (observed.StateSignals is { } signals)
        {
            builder.Append(" signals=[")
                .Append(string.Join(',', signals.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}")))
                .Append(']');
        }

        if (observed.Camera is { } camera)
        {
            builder.Append(" camera=")
                .Append(Sequence(camera.RequestedSequence)).Append('/')
                .Append(Sequence(camera.ResidentSequence)).Append('/')
                .Append(Sequence(camera.CommittedSequence)).Append('/')
                .Append(Sequence(camera.VisibleSequence));
        }

        if (observed.Spawn is { } spawn)
        {
            builder.Append(" spawn=")
                .Append(Sequence(spawn.ActivatedSequence)).Append('/')
                .Append(Sequence(spawn.VisibleSequence));
        }

        if (observed.AudioProgress is { } audio)
        {
            builder.Append(" audioProgress=")
                .Append(audio.RegisterEventCount)
                .Append(" music=").Append(Boolean(audio.Music.Active)).Append('/')
                .Append(audio.Music.Starts).Append('/').Append(audio.Music.Completions).Append('/').Append(audio.Music.Restarts)
                .Append(" sfx=").Append(Boolean(audio.SoundEffect.Active)).Append('/')
                .Append(audio.SoundEffect.Starts).Append('/').Append(audio.SoundEffect.Completions).Append('/').Append(audio.SoundEffect.Restarts)
                .Append(" dpcm=").Append(Boolean(audio.Dpcm.Active)).Append('/')
                .Append(audio.Dpcm.Starts).Append('/').Append(audio.Dpcm.Completions).Append('/').Append(audio.Dpcm.Restarts);
        }

        if (observed.Bank is { } bank)
        {
            builder.Append(" bank=").Append(bank.Domain).Append(':')
                .Append(bank.SelectedBank).Append('/').Append(bank.ShadowBank)
                .Append(" restored=").Append(Boolean(bank.Restored));
        }

        AppendBackground(builder, " background", observed.Background);
        AppendBackground(builder, " expectedBackground", evidence.Expected?.Background);
        AppendSprites(builder, " sprites", observed.Sprites);
        AppendSprites(builder, " expectedSprites", evidence.Expected?.Sprites);
        AppendVideoWrites(builder, observed.VideoWrites);
        AppendOamWrites(builder, observed.OamWrites);
        builder.AppendLine();
    }

    private static void AppendBackground<T>(
        StringBuilder builder,
        string name,
        IReadOnlyList<T>? cells)
        where T : notnull
    {
        if (cells is null)
        {
            return;
        }

        var values = cells.Select(cell => cell switch
        {
            FunctionalBackgroundObservation observed => $"{observed.Location}:{observed.Tile}:{observed.Palette}",
            FunctionalBackgroundExpectation expected => $"{expected.Location}:{expected.Tile}:{expected.Palette}",
            _ => throw new ArgumentOutOfRangeException(nameof(cells)),
        }).Order(StringComparer.Ordinal);
        builder.Append(name).Append("=[").Append(string.Join(',', values)).Append(']');
    }

    private static void AppendSprites<T>(
        StringBuilder builder,
        string name,
        IReadOnlyList<T>? sprites)
        where T : notnull
    {
        if (sprites is null)
        {
            return;
        }

        var values = sprites.Select(sprite => sprite switch
        {
            FunctionalSpriteObservation observed => $"{observed.Id}:slot={observed.OamSlot}:{Boolean(observed.Visible)}:{string.Join('.', observed.Oam)}",
            FunctionalSpriteExpectation expected => $"{expected.Id}:slot={expected.OamSlot}:{Boolean(expected.Visible)}:{string.Join('.', expected.Oam)}",
            _ => throw new ArgumentOutOfRangeException(nameof(sprites)),
        }).Order(StringComparer.Ordinal);
        builder.Append(name).Append("=[").Append(string.Join(',', values)).Append(']');
    }

    private static void AppendVideoWrites(
        StringBuilder builder,
        IReadOnlyList<FunctionalVideoWriteObservation>? writes)
    {
        if (writes is null)
        {
            return;
        }

        builder.Append(" videoWrites=[")
            .Append(string.Join(',', writes.Select(write =>
                $"{write.Space}@0x{write.Address:X4}:{write.Timing.DiagnosticText(write.Safe)}")))
            .Append(']');
    }

    private static void AppendOamWrites(
        StringBuilder builder,
        IReadOnlyList<FunctionalOamWriteObservation>? writes)
    {
        if (writes is null)
        {
            return;
        }

        builder.Append(" oamWrites=[")
            .Append(string.Join(',', writes.Select(write =>
                $"0x{write.Address:X4}:{write.Timing.DiagnosticText(write.Safe)}")))
            .Append(']');
    }

    private static string Sequence(long? sequence) => sequence?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Boolean(bool value) => value ? "true" : "false";
}
