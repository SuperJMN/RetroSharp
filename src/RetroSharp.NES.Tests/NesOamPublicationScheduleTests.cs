namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesOamPublicationScheduleTests
{
    // Upper bounds, not equalities: the publication shares one VBlank with the packed background
    // commit, so what matters is that it stays under the share of the window left for it.
    [Theory]
    [InlineData(76, 640)]
    [InlineData(152, 1_240)]
    public void Schedule_publishes_oam_within_its_cpu_budget(
        int retainedByteCount,
        long cycleBudget)
    {
        var schedule = NesOamPublicationSchedule.Create(
            NesRuntimeMemoryLayout.Sprite.OamShadow,
            retainedByteCount);
        var builder = new PrgBuilder();

        schedule.Emit(builder);

        Assert.True(
            schedule.CpuCycles <= cycleBudget,
            $"OAM publication cost {schedule.CpuCycles} exceeded the {cycleBudget}-cycle budget.");
        Assert.True(
            WritesOamData(builder.Build()),
            "schedule should write the OAM shadow into OAMDATA ($2004).");
        Assert.Equal(schedule.CpuCycles, NesOamPublicationSchedule.CpuCyclesFor(retainedByteCount));
    }

    private static bool WritesOamData(byte[] bytes)
    {
        for (var index = 0; index + 2 < bytes.Length; index++)
        {
            if (bytes[index] == 0x8D && bytes[index + 1] == 0x04 && bytes[index + 2] == 0x20)
            {
                return true;
            }
        }

        return false;
    }
}
