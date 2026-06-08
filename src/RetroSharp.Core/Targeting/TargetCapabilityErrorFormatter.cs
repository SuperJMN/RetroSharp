namespace RetroSharp.Core.Targeting;

public static class TargetCapabilityErrorFormatter
{
    public static string UnsupportedFeature(
        Target2DCapabilities capabilities,
        string requestedFeature,
        IEnumerable<string> suggestedAlternatives)
    {
        var message = $"Target '{capabilities.Name}' does not support {requestedFeature}.";
        var alternatives = suggestedAlternatives
            .Where(alternative => !string.IsNullOrWhiteSpace(alternative))
            .ToArray();

        return alternatives.Length == 0
            ? message
            : $"{message} Use {FormatAlternatives(alternatives)} for this target.";
    }

    public static string UnsupportedFeature(
        Target2DCapabilities capabilities,
        string requestedFeature,
        params string[] suggestedAlternatives)
    {
        return UnsupportedFeature(capabilities, requestedFeature, suggestedAlternatives.AsEnumerable());
    }

    private static string FormatAlternatives(IReadOnlyList<string> alternatives)
    {
        return alternatives.Count switch
        {
            1 => alternatives[0],
            2 => $"{alternatives[0]} or {alternatives[1]}",
            _ => $"{string.Join(", ", alternatives.Take(alternatives.Count - 1))}, or {alternatives[^1]}",
        };
    }
}
