namespace RetroSharp.NES;

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class NesRuntimeAbiProjection
{
    internal const string Contract = "retrosharp.nes.runtime-abi";
    internal const int Version = 1;

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static string Serialize(NesRomBuildResult result)
    {
        var layout = CreateLayout();
        var fingerprintSource = new NesRuntimeAbiFingerprint(
            layout.Ranges,
            layout.Addresses,
            layout.RangeAliases,
            layout.AddressAliases,
            layout.Constants,
            result.Report.RuntimeRegions,
            result.Report.UserVariables);
        var layoutBytes = JsonSerializer.SerializeToUtf8Bytes(fingerprintSource, CompactJson);
        var contract = new NesRuntimeAbiContract(
            Contract,
            Version,
            "nes",
            Convert.ToHexStringLower(SHA256.HashData(layoutBytes)),
            Convert.ToHexStringLower(SHA256.HashData(result.Rom)),
            layout.Ranges,
            layout.Addresses,
            layout.RangeAliases,
            layout.AddressAliases,
            layout.Constants,
            result.Report.RuntimeRegions,
            result.Report.UserVariables);
        return JsonSerializer.Serialize(contract, IndentedJson) + "\n";
    }

    private static NesRuntimeAbiLayout CreateLayout()
    {
        NesRuntimeMemoryLayout.Validate();
        var rangeIds = typeof(NesRuntimeMemoryLayout)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(NesRamRange))
            .Select(field => (field.Name, Range: (NesRamRange)field.GetValue(null)!))
            .Where(item => NesRuntimeMemoryLayout.ReservedRanges.Contains(item.Range))
            .ToDictionary(item => item.Range, item => item.Name);
        var ranges = NesRuntimeMemoryLayout.ReservedRanges
            .Select(range => new NesRuntimeAbiRange(rangeIds[range], range.Name, range.Start, range.Length))
            .ToArray();
        var addresses = NesRuntimeMemoryLayout.NamedAddresses
            .Select(address => new NesRuntimeAbiAddress(
                address.Domain,
                address.Name,
                address.Address,
                rangeIds[address.Owner]))
            .ToArray();
        var rangeAliases = NesRuntimeMemoryLayout.IntentionalRangeAliases
            .Select(alias => new NesRuntimeAbiRangeAlias(
                alias.Name,
                alias.Canonical.Start,
                alias.Canonical.Length,
                alias.Alias.Start,
                alias.Alias.Length))
            .ToArray();
        var addressAliases = NesRuntimeMemoryLayout.IntentionalAddressAliases
            .Select(alias => new NesRuntimeAbiAddressAlias(alias.Name, alias.Address, alias.Roles))
            .ToArray();
        var constants = DescribeConstants();
        return new NesRuntimeAbiLayout(ranges, addresses, rangeAliases, addressAliases, constants);
    }

    private static IReadOnlyList<NesRuntimeAbiConstant> DescribeConstants()
    {
        var packedCamera = typeof(NesPackedCameraRuntime)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(IsIntegerConstant)
            .Select(field => new NesRuntimeAbiConstant(
                $"packed camera.{field.Name}",
                Convert.ToInt32(field.GetRawConstantValue())));
        var worldPack = typeof(NesRuntimeMemoryLayout.WorldPack)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(int) && field.IsLiteral)
            .Select(field => new NesRuntimeAbiConstant(
                $"WorldPack.{field.Name}",
                Convert.ToInt32(field.GetRawConstantValue())));
        return packedCamera
            .Concat(worldPack)
            .OrderBy(constant => constant.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsIntegerConstant(FieldInfo field) =>
        field.IsLiteral && field.FieldType is not null &&
        (field.FieldType == typeof(byte) ||
         field.FieldType == typeof(ushort) ||
         field.FieldType == typeof(int));
}

internal sealed record NesRuntimeAbiLayout(
    IReadOnlyList<NesRuntimeAbiRange> Ranges,
    IReadOnlyList<NesRuntimeAbiAddress> Addresses,
    IReadOnlyList<NesRuntimeAbiRangeAlias> RangeAliases,
    IReadOnlyList<NesRuntimeAbiAddressAlias> AddressAliases,
    IReadOnlyList<NesRuntimeAbiConstant> Constants);

internal sealed record NesRuntimeAbiFingerprint(
    IReadOnlyList<NesRuntimeAbiRange> Ranges,
    IReadOnlyList<NesRuntimeAbiAddress> Addresses,
    IReadOnlyList<NesRuntimeAbiRangeAlias> RangeAliases,
    IReadOnlyList<NesRuntimeAbiAddressAlias> AddressAliases,
    IReadOnlyList<NesRuntimeAbiConstant> Constants,
    IReadOnlyList<NesRuntimeRegion> RuntimeRegions,
    IReadOnlyList<NesRuntimeUserVariable> UserVariables);

internal sealed record NesRuntimeAbiContract(
    string Contract,
    int Version,
    string Target,
    string AbiFingerprint,
    string RomSha256,
    IReadOnlyList<NesRuntimeAbiRange> Ranges,
    IReadOnlyList<NesRuntimeAbiAddress> Addresses,
    IReadOnlyList<NesRuntimeAbiRangeAlias> RangeAliases,
    IReadOnlyList<NesRuntimeAbiAddressAlias> AddressAliases,
    IReadOnlyList<NesRuntimeAbiConstant> Constants,
    IReadOnlyList<NesRuntimeRegion> RuntimeRegions,
    IReadOnlyList<NesRuntimeUserVariable> UserVariables);

internal sealed record NesRuntimeAbiRange(string Id, string Name, ushort Start, int Length);

internal sealed record NesRuntimeAbiAddress(string Domain, string Name, ushort Address, string Owner);

internal sealed record NesRuntimeAbiRangeAlias(
    string Name,
    ushort CanonicalStart,
    int CanonicalLength,
    ushort AliasStart,
    int AliasLength);

internal sealed record NesRuntimeAbiAddressAlias(
    string Name,
    ushort Address,
    IReadOnlyList<string> Roles);

internal sealed record NesRuntimeAbiConstant(string Name, int Value);
