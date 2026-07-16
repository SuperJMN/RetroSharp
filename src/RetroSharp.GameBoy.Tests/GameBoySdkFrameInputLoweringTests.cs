namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;

public sealed class GameBoySdkFrameInputLoweringTests
{
    [Fact]
    public void Lowers_wait_frame_operation_to_existing_game_boy_vblank_routine()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(builder);

        compiler.SdkOperationLowerer.Emit(new Sdk2DOperation.WaitFrame());

        Assert.Equal(
            [0xF0, 0x44, 0xFE, 0x90, 0x30, 0xFA, 0xF0, 0x44, 0xFE, 0x90, 0x38, 0xFA],
            builder.Build());
    }

    [Fact]
    public void Lowers_poll_input_operation_to_deterministic_game_boy_bytes()
    {
        var builder = new GbBuilder();
        CreateRuntimeCompiler(builder).SdkOperationLowerer.Emit(new Sdk2DOperation.PollInput());

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0xFA, 0xF0, 0xC0, 0xEA, 0xF1, 0xC0]), "poll input should preserve the previous joypad snapshot.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F]), "poll input should select and settle the Game Boy button row.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37, 0xB0, 0xEA, 0xF0, 0xC0]), "poll input should select, settle, combine, and publish the Game Boy direction row.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x30, 0xE0, 0x00]), "poll input should deselect both joypad rows after sampling.");
    }
}
