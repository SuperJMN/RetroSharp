namespace RetroSharp.Cli.Tests;

using RetroSharp.Cli;
using Xunit;

public sealed class WorldBudgetProfileValidatorTests
{
    public static TheoryData<string, WorldBudgetUsage> IndependentOverflows => new()
    {
        { "addressing", ValidUsage with { AddressingPixels = 11 } },
        { "rom-prg", ValidUsage with { RomPrgBytes = 11 } },
        { "chr-tile-count", ValidUsage with { ChrTileCount = 11 } },
        { "chr-tile-count", ValidUsage with { ResidentChrBytes = 11 } },
        { "staging-ram", ValidUsage with { StagingRamBytes = 11 } },
        { "vblank", ValidUsage with { VBlankTileWrites = 11 } },
    };

    [Theory]
    [MemberData(nameof(IndependentOverflows))]
    public void Profile_validator_reports_each_overflow_independently_with_actionable_evidence(
        string category,
        WorldBudgetUsage usage)
    {
        var diagnostics = WorldBudgetProfileValidator.Validate(usage, Limits);

        var overflow = Assert.Single(diagnostics, diagnostic => diagnostic.Status == "overflow");
        Assert.Equal(category, overflow.Category);
        Assert.Equal("test-profile", overflow.Profile);
        Assert.Contains(11, overflow.Usage.Values);
        Assert.Contains(10, overflow.Limit.Values);
        Assert.False(string.IsNullOrWhiteSpace(overflow.Remedy));
        Assert.DoesNotContain("automatically", overflow.Remedy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Addressing_accepts_a_32768_pixel_extent_and_rejects_32769()
    {
        var limits = Limits with
        {
            AddressingPixels = WorldBudgetProfileValidator.MaxLogicalExtentPixels,
            AddressingProfile = "world-coordinate-i16-v1",
        };

        var atLimit = WorldBudgetProfileValidator.Validate(
            ValidUsage with { AddressingPixels = 32_768 },
            limits);
        var overflow = WorldBudgetProfileValidator.Validate(
            ValidUsage with { AddressingPixels = 32_769 },
            limits);

        Assert.Equal(32_768, WorldBudgetProfileValidator.MaxLogicalExtentPixels);
        Assert.Equal("ok", Assert.Single(atLimit, item => item.Category == "addressing").Status);
        var diagnostic = Assert.Single(overflow, item => item.Category == "addressing");
        Assert.Equal("overflow", diagnostic.Status);
        Assert.Equal("world-coordinate-i16-v1", diagnostic.Profile);
        Assert.Equal(32_769, diagnostic.Usage["extentPixels"]);
        Assert.Equal(32_768, diagnostic.Limit["extentPixels"]);
        Assert.Contains("last coordinate", diagnostic.Remedy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vblank_attribute_usage_is_validated_independently_from_tile_writes()
    {
        var diagnostics = WorldBudgetProfileValidator.Validate(
            ValidUsage with { VBlankAttributeWrites = 11 },
            Limits);

        var overflow = Assert.Single(diagnostics, diagnostic => diagnostic.Status == "overflow");
        Assert.Equal("vblank", overflow.Category);
        Assert.Equal(0, overflow.Usage["tileWrites"]);
        Assert.Equal(11, overflow.Usage["attributeWrites"]);
        Assert.Equal(10, overflow.Limit["attributeWrites"]);
    }

    private static readonly WorldBudgetUsage ValidUsage = new(
        AddressingPixels: 0,
        RomPrgBytes: 0,
        ChrTileCount: 0,
        ResidentChrBytes: 0,
        StagingRamBytes: 0,
        VBlankTileWrites: 0,
        VBlankAttributeWrites: 0);

    private static readonly WorldBudgetLimits Limits = new(
        Profile: "test-profile",
        AddressingPixels: 10,
        RomPrgBytes: 10,
        ChrTileCount: 10,
        ResidentChrBytes: 10,
        StagingRamBytes: 10,
        VBlankTileWrites: 10,
        VBlankAttributeWrites: 10);
}
