namespace RetroSharp.FunctionalAcceptance;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class FunctionalScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static FunctionalScenario Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var scenario = JsonSerializer.Deserialize<FunctionalScenario>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"Functional scenario '{path}' is empty.");
            FunctionalScenarioValidator.Validate(scenario);
            return scenario;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Functional scenario '{path}' is invalid JSON: {exception.Message}", exception);
        }
    }
}
