namespace RetroSharp.GameBoy.Tests;

using Xunit;

public sealed class GameBoyRuntimeMemoryLayoutTests
{
    [Fact]
    public void Layout_declares_and_validates_every_reserved_runtime_range()
    {
        GameBoyRuntimeMemoryLayout.Validate();

        Assert.Equal(
            [
                ("user locals", (ushort)0xC000, 0x00E0),
                ("camera state", (ushort)0xC0E0, 0x0010),
                ("input state", (ushort)0xC0F0, 0x000A),
                ("audio state", (ushort)0xC0FA, 0x001F),
                ("camera streaming and banking state", (ushort)0xC119, 0x0016),
                ("runtime scratch state", (ushort)0xC12F, 0x0002),
                ("sound-effect state", (ushort)0xC131, 0x0009),
                ("extended camera streaming state", (ushort)0xC13A, 0x0013),
                ("packed camera state", (ushort)0xC14D, 0x009E),
                ("collision query state", (ushort)0xC1EB, 0x0005),
                ("WorldPack scalar and collision state", (ushort)0xC1F0, 0x0020),
                ("audio channel-1 shadow", (ushort)0xC210, 0x0005),
                ("collision memo state", (ushort)0xC220, 0x00C0),
                ("WorldPack staging", (ushort)0xC300, 0x022A),
                ("WRAM echo", (ushort)0xE000, 0x1E00),
                ("stack/HRAM", (ushort)0xFF80, 0x0080),
            ],
            GameBoyRuntimeMemoryLayout.ReservedRanges
                .Select(range => (range.Name, range.Start, range.Length)));

        Assert.All(
            GameBoyRuntimeMemoryLayout.NamedAddresses,
            address => Assert.True(
                address.Owner.Contains(address.Address),
                $"{address.Domain}.{address.Name} is outside {address.Owner.Name}."));
        Assert.Equal(
            GameBoyRuntimeMemoryLayout.NamedAddresses.Count,
            GameBoyRuntimeMemoryLayout.NamedAddresses.Select(address => address.Address).Distinct().Count());
        Assert.Equal(
            ["WorldPack", "audio", "banking", "camera", "collision", "input", "packed camera", "runtime"],
            GameBoyRuntimeMemoryLayout.NamedAddresses
                .Select(address => address.Domain)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Layout_declares_the_hardware_wram_echo_as_an_intentional_alias()
    {
        var alias = Assert.Single(GameBoyRuntimeMemoryLayout.IntentionalAliases);

        Assert.Equal("WRAM echo", alias.Name);
        Assert.Equal((ushort)0xC000, alias.Canonical.Start);
        Assert.Equal((ushort)0xE000, alias.Alias.Start);
        Assert.Equal(0x1E00, alias.Canonical.Length);
        Assert.Equal(alias.Canonical.Length, alias.Alias.Length);
    }

    [Fact]
    public void User_local_limit_accepts_the_exact_range_and_rejects_one_byte_more()
    {
        var maximum = GameBoyRuntimeMemoryLayout.ValidateUserLocalBytes(0x00E0);

        Assert.Equal(GameBoyRuntimeMemoryLayout.UserLocals, maximum);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameBoyRuntimeMemoryLayout.ValidateUserLocalBytes(0x00E1));
        Assert.Contains("0..224 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Worldpack_staging_accepts_current_and_maximum_shapes_and_rejects_one_byte_more()
    {
        var current = GameBoyRuntimeMemoryLayout.ValidateWorldPackStagingBytes(
            GameBoyRuntimeMemoryLayout.WorldPack.CurrentStagingBytes);
        var maximum = GameBoyRuntimeMemoryLayout.ValidateWorldPackStagingBytes(
            GameBoyRuntimeMemoryLayout.WorldPack.MaximumStagingBytes);

        Assert.Equal(362, current.Length);
        Assert.Equal((ushort)0xC529, maximum.EndInclusive);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameBoyRuntimeMemoryLayout.ValidateWorldPackStagingBytes(
                GameBoyRuntimeMemoryLayout.WorldPack.MaximumStagingBytes + 1));
        Assert.Contains("1..554 bytes", exception.Message, StringComparison.Ordinal);
    }
}
