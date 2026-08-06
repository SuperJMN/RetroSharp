namespace RetroSharp.NES.Tests;

using Xunit;
using Xunit.Abstractions;

public sealed class NesTestCpuMmc3PrgModeTests(ITestOutputHelper output)
{
    [Fact]
    public void Mmc3_prg_mode_one_swaps_r6_and_fixed_windows()
    {
        var rom = NesMmc3TestRomBuilder.Create();
        NesMmc3TestRomBuilder.WritePrg(
            rom,
            7,
            0xE000,
            [
                0xA9, 0x46,             // LDA #$40 | 6
                0x8D, 0x00, 0x80,       // STA $8000
                0xA9, 0x03,             // LDA #3
                0x8D, 0x01, 0x80,       // STA $8001
                0xAD, 0x00, 0x80,       // LDA $8000
                0xAE, 0x00, 0xC0,       // LDX $C000
                0xAC, 0x00, 0xA0,       // LDY $A000
                0x60,                   // RTS
            ]);
        var cpu = new NesTestCpu(rom);

        var result = cpu.RunRoutine(0xE000);

        output.WriteLine(
            $"mode1 result: A=${result.A:X2} X=${result.X:X2} Y=${result.Y:X2}; " +
            $"R6={cpu.CurrentR6Bank}; select writes=[{string.Join(", ", cpu.BankSelectWrites.Select(write => "$" + write.ToString("X2")))}]");
        Assert.Equal(NesMmc3TestRomBuilder.BankMarker(6), result.A);
        Assert.Equal(NesMmc3TestRomBuilder.BankMarker(3), result.X);
        Assert.Equal(NesMmc3TestRomBuilder.BankMarker(1), result.Y);
        Assert.True(cpu.Mmc3PrgBankMode1);
        Assert.Equal([0x46], cpu.BankSelectWrites);
        Assert.Equal([3], cpu.R6BankWrites);
    }
}
