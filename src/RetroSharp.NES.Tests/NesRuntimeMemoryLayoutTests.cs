namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesRuntimeMemoryLayoutTests
{
    [Fact]
    public void Layout_declares_and_validates_every_reserved_runtime_range()
    {
        NesRuntimeMemoryLayout.Validate();

        Assert.Equal(
            [
                ("user zero-page locals", (ushort)0x0000, 0x00DE),
                ("sound-effect zero-page state", (ushort)0x00DE, 0x0002),
                ("camera and runtime zero-page scratch", (ushort)0x00E0, 0x0010),
                ("input zero-page state", (ushort)0x00F0, 0x000A),
                ("audio zero-page state", (ushort)0x00FA, 0x0006),
                ("CPU stack", (ushort)0x0100, 0x0100),
                ("OAM shadow", (ushort)0x0200, 0x0100),
                ("camera control state", (ushort)0x0300, 0x000D),
                ("audio state", (ushort)0x0310, 0x0008),
                ("extended camera state", (ushort)0x0318, 0x000C),
                ("mapper bank shadows", (ushort)0x0324, 0x0002),
                ("WorldPack scalar state", (ushort)0x0326, 0x0048),
                ("packed camera and WorldPack auxiliary state", (ushort)0x036E, 0x0092),
                ("WorldPack staging", (ushort)0x0400, 0x0252),
                ("CPU RAM mirror 1", (ushort)0x0800, 0x0800),
                ("CPU RAM mirror 2", (ushort)0x1000, 0x0800),
                ("CPU RAM mirror 3", (ushort)0x1800, 0x0800),
            ],
            NesRuntimeMemoryLayout.ReservedRanges
                .Select(range => (range.Name, range.Start, range.Length)));

        Assert.All(
            NesRuntimeMemoryLayout.NamedAddresses,
            address => Assert.True(
                address.Owner.Contains(address.Address),
                $"{address.Domain}.{address.Name} is outside {address.Owner.Name}."));
        Assert.Equal(
            ["WorldPack", "audio", "banking", "camera", "input", "packed camera", "runtime", "sprite"],
            NesRuntimeMemoryLayout.NamedAddresses
                .Select(address => address.Domain)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Layout_declares_the_three_hardware_ram_mirrors_as_intentional_aliases()
    {
        Assert.Equal(3, NesRuntimeMemoryLayout.IntentionalRangeAliases.Count);

        foreach (var (alias, index) in NesRuntimeMemoryLayout.IntentionalRangeAliases.Select((alias, index) => (alias, index)))
        {
            Assert.Equal($"CPU RAM mirror {index + 1}", alias.Name);
            Assert.Equal((ushort)0x0000, alias.Canonical.Start);
            Assert.Equal((ushort)(0x0800 + index * 0x0800), alias.Alias.Start);
            Assert.Equal(0x0800, alias.Canonical.Length);
            Assert.Equal(alias.Canonical.Length, alias.Alias.Length);
        }
    }

    [Theory]
    [InlineData(0x00E4, "packed camera.PayloadIndexScratch", "runtime.SpriteFrameScratch")]
    [InlineData(0x00E8, "WorldPack.PointerLow", "packed camera.PointerLow", "runtime.IndexScratch")]
    [InlineData(0x00E9, "WorldPack.PointerHigh", "packed camera.PointerHigh", "runtime.ExpressionScratch")]
    public void Shared_zero_page_scratch_is_an_explicit_alias(ushort address, params string[] expectedRoles)
    {
        var alias = Assert.Single(NesRuntimeMemoryLayout.IntentionalAddressAliases, alias => alias.Address == address);

        Assert.Equal(expectedRoles, alias.Roles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_duplicate_named_address_is_declared_as_an_intentional_alias()
    {
        var duplicates = NesRuntimeMemoryLayout.NamedAddresses
            .GroupBy(address => address.Address)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(address => $"{address.Domain}.{address.Name}")
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        Assert.Equal(
            duplicates.Keys.Order(),
            NesRuntimeMemoryLayout.IntentionalAddressAliases.Select(alias => alias.Address).Order());
        Assert.All(
            NesRuntimeMemoryLayout.IntentionalAddressAliases,
            alias => Assert.Equal(duplicates[alias.Address], alias.Roles.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void User_local_limit_accepts_the_exact_range_and_rejects_one_byte_more()
    {
        var maximum = NesRuntimeMemoryLayout.ValidateUserLocalBytes(0x00DE);

        Assert.Equal(NesRuntimeMemoryLayout.UserLocals, maximum);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NesRuntimeMemoryLayout.ValidateUserLocalBytes(0x00DF));
        Assert.Contains("0..222 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Worldpack_staging_accepts_current_and_maximum_shapes_and_rejects_one_byte_more()
    {
        var current = NesRuntimeMemoryLayout.ValidateWorldPackStagingBytes(
            NesRuntimeMemoryLayout.WorldPack.CurrentStagingBytes);
        var maximum = NesRuntimeMemoryLayout.ValidateWorldPackStagingBytes(
            NesRuntimeMemoryLayout.WorldPack.MaximumStagingBytes);

        Assert.Equal(338, current.Length);
        Assert.Equal((ushort)0x0651, maximum.EndInclusive);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NesRuntimeMemoryLayout.ValidateWorldPackStagingBytes(
                NesRuntimeMemoryLayout.WorldPack.MaximumStagingBytes + 1));
        Assert.Contains("1..594 bytes", exception.Message, StringComparison.Ordinal);
    }
}
