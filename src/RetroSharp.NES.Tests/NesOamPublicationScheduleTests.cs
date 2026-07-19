namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesOamPublicationScheduleTests
{
    [Theory]
    [InlineData(76, 1_071, 0xB4, 0x4C, 0x01)]
    [InlineData(152, 2_135, 0x68, 0x98, 0x01)]
    public void Schedule_owns_the_exact_loop_bytes_and_cpu_cost(
        int retainedByteCount,
        long expectedCycles,
        byte expectedStartIndex,
        byte expectedAddressLow,
        byte expectedAddressHigh)
    {
        var schedule = NesOamPublicationSchedule.Create(
            NesRuntimeMemoryLayout.Sprite.OamShadow,
            retainedByteCount);
        var builder = new PrgBuilder();

        schedule.Emit(builder);

        Assert.Equal(expectedCycles, schedule.CpuCycles);
        Assert.Equal(
            [
                0xA9, 0x00,
                0x8D, 0x03, 0x20,
                0xA2, expectedStartIndex,
                0xBD, expectedAddressLow, expectedAddressHigh,
                0x8D, 0x04, 0x20,
                0xE8,
                0xD0, 0xF7,
            ],
            builder.Build());
    }
}
