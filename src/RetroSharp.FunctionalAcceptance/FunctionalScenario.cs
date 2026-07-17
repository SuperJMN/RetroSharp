namespace RetroSharp.FunctionalAcceptance;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<FunctionalTarget>))]
public enum FunctionalTarget
{
    [JsonStringEnumMemberName("gb")]
    GameBoy,

    [JsonStringEnumMemberName("nes")]
    Nes,
}

public sealed record FunctionalScenario(
    string Id,
    string SampleId,
    FunctionalTarget Target,
    int WarmUpFrames,
    int ObservationFrames,
    IReadOnlyList<FunctionalInputSpan> Inputs,
    IReadOnlyList<FunctionalCheckpoint> Checkpoints,
    FunctionalExpectedFeatures ExpectedFeatures,
    FunctionalAudioExpectation Audio,
    FunctionalBudgetEvidence BudgetEvidence,
    FunctionalTimingBudgets Budgets)
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }
}

public sealed record FunctionalInputSpan(
    string Id,
    int StartFrame,
    int DurationFrames,
    IReadOnlyList<string> Buttons,
    string? ResponseSignal = null);

public sealed record FunctionalCheckpoint(
    string Id,
    int Frame,
    IReadOnlyDictionary<string, long> ExpectedSignals);

public sealed record FunctionalExpectedFeatures(
    [property: JsonRequired] bool GameplayTicks = false,
    [property: JsonRequired] bool AudioService = false,
    [property: JsonRequired] bool CameraLifecycle = false,
    [property: JsonRequired] bool Background = false,
    [property: JsonRequired] bool SpriteOam = false,
    [property: JsonRequired] bool BankRestoration = false,
    [property: JsonRequired] bool SafeVideoWrites = false,
    bool AudioProgress = false);

public sealed record FunctionalAudioExpectation(
    bool ServiceExpectedByDefault,
    IReadOnlyList<FunctionalFrameSpan> AuthoredSilence,
    int MinimumRegisterEvents = 0,
    int? MaximumRegisterEvents = null,
    int? MaximumRegisterEventGapFrames = null,
    int MinimumSoundEffectStarts = 0,
    int? MaximumSoundEffectStarts = null,
    int MinimumSoundEffectCompletions = 0,
    int? MaximumSoundEffectCompletions = null,
    int MaximumSoundEffectRestarts = 0,
    int MinimumDpcmStarts = 0,
    int? MaximumDpcmStarts = null,
    int MinimumDpcmCompletions = 0,
    int? MaximumDpcmCompletions = null,
    int MaximumDpcmRestarts = 0,
    bool? MusicActiveAtEnd = null,
    bool? SoundEffectActiveAtEnd = null,
    bool? DpcmActiveAtEnd = null,
    string? OrderedRegisterEventSha256 = null);

public sealed record FunctionalFrameSpan(int StartFrame, int DurationFrames);

public sealed record FunctionalBudgetEvidence(
    string BaselineCommit,
    string HardwareTimingRationale,
    string ProductionTraceRationale);

public sealed record FunctionalTimingBudgets(
    double MinimumGameplayTickRatio,
    int MaximumConsecutiveMissedGameplayTicks,
    int? MaximumInputToStateFrames = null,
    int? MaximumRequestToResidentFrames = null,
    int? MaximumRequestToVisibleFrames = null,
    int? MaximumUnplannedAudioGapFrames = null,
    int? MaximumAudioDriftTicks = null,
    int? MaximumSpawnToVisibleFrames = null);

public sealed record FunctionalRomArtifact(string SourcePath, byte[] Bytes);

public sealed record FunctionalFrameObservation(
    int Frame,
    long GameplayTicks,
    long AudioServiceTicks,
    int ResetCount,
    IReadOnlyDictionary<string, long>? StateSignals = null,
    FunctionalCameraLifecycleObservation? Camera = null,
    FunctionalBankObservation? Bank = null,
    IReadOnlyList<FunctionalBackgroundObservation>? Background = null,
    IReadOnlyList<FunctionalSpriteObservation>? Sprites = null,
    IReadOnlyList<FunctionalVideoWriteObservation>? VideoWrites = null,
    IReadOnlyList<FunctionalOamWriteObservation>? OamWrites = null,
    FunctionalSpawnLifecycleObservation? Spawn = null,
    FunctionalAudioProgressObservation? AudioProgress = null);

public sealed record FunctionalAudioProgressObservation(
    long RegisterEventCount,
    IReadOnlyList<FunctionalAudioRegisterEvent> RegisterEvents,
    FunctionalAudioPlaybackObservation Music,
    FunctionalAudioPlaybackObservation SoundEffect,
    FunctionalAudioPlaybackObservation Dpcm);

public sealed record FunctionalAudioRegisterEvent(string Domain, int Address, int Value);

public sealed record FunctionalAudioPlaybackObservation(
    bool Active,
    long Starts,
    long Completions,
    long Restarts);

public sealed record FunctionalSpawnLifecycleObservation(
    long? ActivatedSequence,
    long? VisibleSequence);

public sealed record FunctionalCameraLifecycleObservation(
    long? RequestedSequence,
    long? ResidentSequence,
    long? CommittedSequence,
    long? VisibleSequence);

public sealed record FunctionalBankObservation(
    int SelectedBank,
    int ShadowBank,
    bool Restored,
    string Domain = "bank/mapper");

public sealed record FunctionalBackgroundObservation(
    string Location,
    int Tile,
    int Palette);

public sealed record FunctionalSpriteObservation(
    string Id,
    bool Visible,
    IReadOnlyList<int> Oam,
    int OamSlot = -1);

public sealed record FunctionalFrameExpectation(
    int Frame,
    IReadOnlyList<FunctionalBackgroundExpectation>? Background = null,
    IReadOnlyList<FunctionalSpriteExpectation>? Sprites = null);

public sealed record FunctionalBackgroundExpectation(string Location, int Tile, int Palette);

public sealed record FunctionalSpriteExpectation(string Id, bool Visible, IReadOnlyList<int> Oam, int OamSlot = -1);

public sealed record FunctionalWriteTimingObservation(
    long Cycle,
    int Scanline,
    int Dot,
    string Phase,
    bool DisplayEnabled);

public sealed record FunctionalVideoWriteObservation(
    string Space,
    int Address,
    bool Safe,
    FunctionalWriteTimingObservation? Timing);

public sealed record FunctionalOamWriteObservation(
    int Address,
    bool Safe,
    FunctionalWriteTimingObservation? Timing);
