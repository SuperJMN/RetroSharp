namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class PrgBuilderTests
{
    [Fact]
    public void In_range_JumpIf_relaxes_to_one_relative_branch_without_changing_semantics()
    {
        var builder = new PrgBuilder();
        builder.JumpIf(0xD0, "target"); // BNE target
        builder.Emit(0xEA);              // NOP
        builder.Label("target");

        Assert.Equal([0xD0, 0x01, 0xEA], builder.Build());
    }

    [Fact]
    public void Out_of_range_JumpIf_keeps_inverse_branch_plus_absolute_jump()
    {
        var builder = new PrgBuilder();
        builder.JumpIf(0xD0, "target"); // BNE target
        builder.Emit(Enumerable.Repeat((byte)0xEA, 128).ToArray());
        builder.Label("target");

        var bytes = builder.Build();

        Assert.Equal([0xF0, 0x03, 0x4C, 0x85, 0x80], bytes[..5]);
        Assert.Equal(133, bytes.Length);
    }

    [Fact]
    public void JumpIf_relaxation_converges_when_one_short_branch_enables_another()
    {
        var builder = new PrgBuilder();
        builder.JumpIf(0xD0, "target"); // Initially one byte out of relative range.
        builder.JumpIf(0x90, "target"); // Relaxes first and moves the shared target closer.
        builder.Emit(Enumerable.Repeat((byte)0xEA, 123).ToArray());
        builder.Label("target");

        var bytes = builder.Build();

        Assert.Equal([0xD0, 0x7D, 0x90, 0x7B], bytes[..4]);
        Assert.Equal(127, bytes.Length);
    }

    [Fact]
    public void Forward_relaxed_jump_before_PadToAddress_preserves_requested_address()
    {
        var builder = new PrgBuilder(0x8000);
        builder.JumpIf(0xD0, "target");
        builder.PadToAddress(0x8010);
        builder.Label("target");

        var bytes = builder.Build();

        Assert.Equal(0x8010, builder.AddressOfLabel("target"));
        Assert.Equal(0x10, bytes.Length);
        Assert.Equal([0xD0, 0x0E], bytes[..2]);
        Assert.All(bytes[2..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void In_range_external_JumpIf_relaxes_to_one_relative_branch()
    {
        var builder = new PrgBuilder(0x8000);
        builder.DefineExternalLabel("target", 0x807F);
        builder.JumpIf(0xD0, "target");

        Assert.Equal([0xD0, 0x7D], builder.Build());
    }

    [Fact]
    public void Out_of_range_external_JumpIf_keeps_inverse_branch_plus_absolute_jump()
    {
        var builder = new PrgBuilder(0x8000);
        builder.DefineExternalLabel("target", 0x8082);
        builder.JumpIf(0xD0, "target");

        Assert.Equal([0xF0, 0x03, 0x4C, 0x82, 0x80], builder.Build());
    }

    [Fact]
    public void Relaxation_remaps_local_absolute_byte_and_relative_fixups_and_label_addresses()
    {
        var builder = new PrgBuilder(0x8000);
        builder.JumpIf(0xD0, "target");
        builder.JumpAbsolute("target");
        builder.EmitLabelLowByte("target");
        builder.EmitLabelHighByte("target");
        builder.BranchRelative(0xD0, "target");
        builder.Emit(0xEA);
        builder.Label("target");

        var bytes = builder.Build();

        Assert.Equal(0x800A, builder.AddressOfLabel("target"));
        Assert.Equal(
            [0xD0, 0x08, 0x4C, 0x0A, 0x80, 0x0A, 0x80, 0xD0, 0x01, 0xEA],
            bytes);
    }

    [Fact]
    public void Relaxation_remaps_external_absolute_byte_and_relative_fixups()
    {
        var builder = new PrgBuilder(0x8000);
        builder.DefineExternalLabel("external", 0x8010);
        builder.JumpIf(0xD0, "local");
        builder.JumpAbsolute("external");
        builder.EmitLabelLowByte("external");
        builder.EmitLabelHighByte("external");
        builder.BranchRelative(0xD0, "external");
        builder.Label("local");

        var bytes = builder.Build();

        Assert.Equal(0x8009, builder.AddressOfLabel("local"));
        Assert.Equal(
            [0xD0, 0x07, 0x4C, 0x10, 0x80, 0x10, 0x80, 0xD0, 0x07],
            bytes);
    }
}
