namespace RetroSharp.FunctionalAcceptance;

public enum FunctionalScenarioRunMode
{
    FullEvidence,
    FailFast,
}

public sealed record FunctionalScenarioRunOptions(
    FunctionalScenarioRunMode Mode = FunctionalScenarioRunMode.FullEvidence,
    int EvidenceFramesBeforeFailure = 8);
