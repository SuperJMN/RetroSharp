using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

namespace RetroSharp.NES;

internal sealed record NesVideoSafeGeneratedCall(string Function);

internal sealed record NesSpawnActivationMetadata(
    int SpawnCount,
    int PoolCapacity,
    int WindowWidth);

internal static class NesGeneratedSpawnActivationWork
{
    internal const string AttributeName = "__rs_nes_fixed_spawn_activation";
    private const string ReservedAttributePrefix = "__rs_nes_";
    private const string Calibration = "NesGeneratedSpawnActivation.VideoSafeUpper/v1";

    // Conservative upper-bound factors for generated spawn activation when source order can delay
    // the camera-apply video-safe publication. They intentionally price the complete outlined body
    // plus the compiler-known authored scan bound; the lower bound remains zero until the full
    // actor.spawn.* descriptor is calibrated.
    private const long JsrRtsCycles = 12;
    private const long UpperCyclesPerEmittedByte = 32;
    private const long UpperCyclesPerAuthoredSpawnSlot = 256;

    internal static void ValidateReservedAttributes(IEnumerable<FunctionSyntax> functions)
    {
        foreach (var function in functions.Where(function => !function.IsCompilerGenerated))
        {
            var reserved = function.Attributes.FirstOrDefault(attribute =>
                attribute.Name.StartsWith(ReservedAttributePrefix, StringComparison.Ordinal));
            if (reserved is not null)
            {
                throw new InvalidOperationException(
                    $"NES reserved compiler attribute '[{reserved.Name}]' cannot be used on user function '{function.Name}'.");
            }
        }
    }

    internal static bool IsTrustedFixedActivation(FunctionSyntax function) =>
        function.IsCompilerGenerated
        && string.Equals(function.Type, "void", StringComparison.Ordinal)
        && function.Parameters.Count == 0
        && TryRead(function, out _);

    internal static bool TryRead(FunctionSyntax function, out NesSpawnActivationMetadata metadata)
    {
        metadata = null!;
        var attribute = function.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name, AttributeName, StringComparison.Ordinal));
        if (attribute is null || attribute.Arguments.Count != 3)
        {
            return false;
        }

        metadata = new NesSpawnActivationMetadata(
            ReadAttributeInteger(attribute.Arguments[0], $"{AttributeName} spawn count"),
            ReadAttributeInteger(attribute.Arguments[1], $"{AttributeName} pool capacity"),
            ReadAttributeInteger(attribute.Arguments[2], $"{AttributeName} window width"));
        return true;
    }

    internal static IReadOnlyList<SdkCpuWorkContributor> VideoSafeContributors(
        IReadOnlyList<NesVideoSafeGeneratedCall> calls,
        NesUserFunctionCallAccountingReport callAccounting,
        IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        if (calls.Count == 0)
        {
            return [];
        }

        var bodies = callAccounting.Collected.ToDictionary(body => body.Function, StringComparer.Ordinal);
        return calls
            .GroupBy(call => call.Function, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var function = functions[group.Key];
                var metadata = Read(function);
                var bodyBytes = bodies.TryGetValue(group.Key, out var body) ? body.EmittedBytes : 0;
                var upper = VideoSafeUpperCycles(metadata, bodyBytes);
                return SdkCpuWorkContributor.Create(
                    SdkCpuWorkContributorIds.ActorSpawnScan,
                    SdkCpuWorkContributorCategories.Generated,
                    $"outlined actor spawn activation '{group.Key}' before camera-apply publication",
                    group.Count(),
                    unitLower: 0,
                    unitUpper: upper,
                    Calibration);
            })
            .ToArray();
    }

    private static NesSpawnActivationMetadata Read(FunctionSyntax function)
    {
        if (!TryRead(function, out var metadata))
        {
            throw new InvalidOperationException(
                $"Generated NES spawn activation '{function.Name}' is missing compiler metadata.");
        }

        return metadata;
    }

    private static long VideoSafeUpperCycles(NesSpawnActivationMetadata metadata, int emittedBodyBytes) =>
        checked(
            JsrRtsCycles
            + emittedBodyBytes * UpperCyclesPerEmittedByte
            + metadata.SpawnCount * Math.Max(1, metadata.PoolCapacity) * UpperCyclesPerAuthoredSpawnSlot);

    private static int ReadAttributeInteger(ExpressionSyntax expression, string context) =>
        NesVideoProgram.ConstValue(expression, context);
}
