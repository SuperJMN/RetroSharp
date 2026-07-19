namespace RetroSharp.NES.Tests;

using System.Security.Cryptography;
using System.Text.Json;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesRuntimeAbiProjectionTests
{
    [Fact]
    public void Projection_is_complete_versioned_and_deterministic()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            "void Main() { i16 playerX = 0; i16 playerY = 0; }");

        var first = SerializeProjection(result);
        var second = SerializeProjection(result);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal("retrosharp.nes.runtime-abi", root.GetProperty("contract").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("nes", root.GetProperty("target").GetString());
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(result.Rom)),
            root.GetProperty("romSha256").GetString());

        Assert.Equal(
            NesRuntimeMemoryLayout.ReservedRanges.Select(range => (range.Name, range.Start, range.Length)),
            root.GetProperty("ranges").EnumerateArray().Select(range => (
                range.GetProperty("name").GetString()!,
                checked((ushort)range.GetProperty("start").GetInt32()),
                range.GetProperty("length").GetInt32())));
        Assert.Equal(
            NesRuntimeMemoryLayout.NamedAddresses.Select(address => (address.Domain, address.Name, address.Address)),
            root.GetProperty("addresses").EnumerateArray().Select(address => (
                address.GetProperty("domain").GetString()!,
                address.GetProperty("name").GetString()!,
                checked((ushort)address.GetProperty("address").GetInt32()))));

        var constants = root.GetProperty("constants").EnumerateArray().ToDictionary(
            item => item.GetProperty("name").GetString()!,
            item => item.GetProperty("value").GetInt32(),
            StringComparer.Ordinal);
        Assert.Equal(NesPackedCameraRuntime.Empty, constants["packed camera.Empty"]);
        Assert.Equal(NesPackedCameraRuntime.Released, constants["packed camera.Released"]);
        Assert.Equal(NesPackedCameraRuntime.NoSlot, constants["packed camera.NoSlot"]);
        Assert.Equal(NesPackedCameraRuntime.SlotMetadataBytes, constants["packed camera.SlotMetadataBytes"]);
        Assert.Equal(NesRuntimeMemoryLayout.WorldPack.MaximumStagingBytes, constants["WorldPack.MaximumStagingBytes"]);

        var addresses = root.GetProperty("addresses").EnumerateArray().ToDictionary(
            item => $"{item.GetProperty("domain").GetString()}.{item.GetProperty("name").GetString()}",
            item => item.GetProperty("address").GetInt32(),
            StringComparer.Ordinal);
        Assert.Equal(0x039D, addresses["packed camera.Slot0CommitPhase"]);
        Assert.Equal(0x039F, addresses["packed camera.Slot0PayloadCursor"]);
        Assert.Equal(0x03AD, addresses["packed camera.Slot1CommitPhase"]);
        Assert.Equal(0x03AF, addresses["packed camera.Slot1PayloadCursor"]);
    }

    [Fact]
    public void Projection_binds_compiled_user_variables_to_the_exact_rom()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            "void Main() { i16 playerX = 0; i16 playerY = 0; bool grounded = true; }");

        using var document = JsonDocument.Parse(SerializeProjection(result));
        var root = document.RootElement;
        var variables = root.GetProperty("userVariables").EnumerateArray().ToDictionary(
            variable => variable.GetProperty("name").GetString()!,
            variable => (
                variable.GetProperty("type").GetString(),
                variable.GetProperty("address").GetInt32(),
                variable.GetProperty("size").GetInt32()),
            StringComparer.Ordinal);

        Assert.Equal(("i16", 0x0000, 2), variables["playerX"]);
        Assert.Equal(("i16", 0x0002, 2), variables["playerY"]);
        Assert.Equal(("bool", 0x0004, 1), variables["grounded"]);
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("abiFingerprint").GetString());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("romSha256").GetString());
    }

    [Fact]
    public void Projection_exposes_the_runner_world_pack_regions_observed_by_external_tools()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        using var document = JsonDocument.Parse(SerializeProjection(result));
        var visualSlots = document.RootElement
            .GetProperty("runtimeRegions")
            .EnumerateArray()
            .Where(region => region.GetProperty("name").GetString()!.StartsWith(
                "WorldPack.VisualSlot",
                StringComparison.Ordinal))
            .Select(region => (
                region.GetProperty("name").GetString()!,
                region.GetProperty("start").GetInt32(),
                region.GetProperty("length").GetInt32()))
            .ToArray();

        Assert.Equal(6, visualSlots.Length);
        Assert.Equal(
            Enumerable.Range(0, 6).Select(index => ($"WorldPack.VisualSlot{index}", 0x0400 + index * 64, 64)),
            visualSlots);
        Assert.Equal(
            File.ReadAllText(Path.Combine(RunnerSample.Directory, "bin", "runner.nes.runtime-abi.json")),
            SerializeProjection(result));
    }

    private static string SerializeProjection(NesRomBuildResult result)
        => NesRuntimeAbiProjection.Serialize(result);
}
