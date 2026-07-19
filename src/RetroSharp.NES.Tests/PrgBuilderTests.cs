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
}
