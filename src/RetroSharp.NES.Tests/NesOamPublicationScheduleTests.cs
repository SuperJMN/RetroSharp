namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesOamPublicationScheduleTests
{
    [Theory]
    [InlineData(76, 855)]
    [InlineData(152, 1_222)]
    public void Schedule_owns_the_bounded_profile_bytes_and_cpu_cost(
        int retainedByteCount,
        long expectedCycles)
    {
        var schedule = NesOamPublicationSchedule.Create(
            NesRuntimeMemoryLayout.Sprite.OamShadow,
            retainedByteCount);
        var builder = new PrgBuilder();

        schedule.Emit(builder);

        Assert.Equal(expectedCycles, schedule.CpuCycles);
        Assert.Equal(ExpectedBytes(retainedByteCount), builder.Build());
    }

    private static byte[] ExpectedBytes(int retainedByteCount)
    {
        var bytes = new List<byte> { 0xA9, 0x00, 0x8D, 0x03, 0x20 };
        if (retainedByteCount <= 76)
        {
            for (var index = 0; index < Math.Min(4, retainedByteCount); index++)
            {
                bytes.AddRange([0xAD, (byte)index, 0x02, 0x8D, 0x04, 0x20]);
            }

            if (retainedByteCount == 76)
            {
                bytes.AddRange([0xA2, 0xB8]);
                for (var index = 0; index < 9; index++)
                {
                    bytes.AddRange([0xBD, 0x4C, 0x01, 0x8D, 0x04, 0x20, 0xE8]);
                }

                bytes.AddRange([0xD0, 0xBF]);
            }

            return bytes.ToArray();
        }

        for (var index = 0; index < retainedByteCount; index++)
        {
            bytes.AddRange([0xAD, (byte)index, 0x02, 0x8D, 0x04, 0x20]);
        }

        return bytes.ToArray();
    }
}
