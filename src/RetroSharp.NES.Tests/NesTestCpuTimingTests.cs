namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesTestCpuTimingTests
{
    [Theory]
    [InlineData(false, 12)]
    [InlineData(true, 13)]
    public void Absolute_x_load_charges_page_crossing(bool crossesPage, long expectedCycles)
    {
        var baseAddress = crossesPage ? (ushort)0x80FF : (ushort)0x80FE;
        var valueAddress = (ushort)(baseAddress + 1);
        var rom = CreateRom();
        Write(rom, 0x8000, [0xA2, 0x01, 0xBD, (byte)baseAddress, (byte)(baseAddress >> 8), 0x60]);
        Write(rom, valueAddress, [0x42]);
        var cpu = new NesTestCpu(rom);

        var result = cpu.RunRoutine(0x8000);

        Assert.Equal((byte)0x42, result.A);
        Assert.Equal(expectedCycles, result.Cycles);
    }

    [Theory]
    [InlineData(false, 12)]
    [InlineData(true, 13)]
    public void Absolute_y_load_charges_page_crossing(bool crossesPage, long expectedCycles)
    {
        var baseAddress = crossesPage ? (ushort)0x80FF : (ushort)0x80FE;
        var valueAddress = (ushort)(baseAddress + 1);
        var rom = CreateRom();
        Write(rom, 0x8000, [0xA0, 0x01, 0xB9, (byte)baseAddress, (byte)(baseAddress >> 8), 0x60]);
        Write(rom, valueAddress, [0x42]);
        var cpu = new NesTestCpu(rom);

        var result = cpu.RunRoutine(0x8000);

        Assert.Equal((byte)0x42, result.A);
        Assert.Equal(expectedCycles, result.Cycles);
    }

    [Theory]
    [InlineData(false, 13)]
    [InlineData(true, 14)]
    public void Indirect_y_load_charges_page_crossing(bool crossesPage, long expectedCycles)
    {
        var baseAddress = crossesPage ? (ushort)0x00FF : (ushort)0x00FE;
        var valueAddress = (ushort)(baseAddress + 1);
        var rom = CreateRom();
        Write(rom, 0x8000, [0xA0, 0x01, 0xB1, 0x10, 0x60]);
        var cpu = new NesTestCpu(rom);
        cpu.SetRam(0x0010, (byte)baseAddress);
        cpu.SetRam(0x0011, (byte)(baseAddress >> 8));
        cpu.SetRam(valueAddress, 0x42);

        var result = cpu.RunRoutine(0x8000);

        Assert.Equal((byte)0x42, result.A);
        Assert.Equal(expectedCycles, result.Cycles);
    }

    [Theory]
    [InlineData(0x8000, 0x00, 11)]
    [InlineData(0x80FB, 0x01, 12)]
    public void Taken_branch_charges_page_crossing(ushort startAddress, byte delta, long expectedCycles)
    {
        var rom = CreateRom();
        Write(rom, startAddress, [0xA9, 0x01, 0xD0, delta, 0x60, 0x60]);
        var cpu = new NesTestCpu(rom);

        var result = cpu.RunRoutine(startAddress);

        Assert.Equal(expectedCycles, result.Cycles);
    }

    [Fact]
    public void Absolute_store_records_the_real_ppu_bus_write_cycle()
    {
        var rom = CreateRom();
        Write(rom, 0x8000, [0xA9, 0x80, 0x8D, 0x00, 0x20, 0x60]);
        var cpu = new NesTestCpu(rom);

        var result = cpu.RunRoutine(0x8000);

        var write = Assert.Single(cpu.PpuWrites);
        Assert.Equal(12, result.Cycles);
        Assert.Equal(5, write.Cycle);
    }

    [Fact]
    public void Seventy_six_byte_publisher_costs_1071_cycles_before_return()
    {
        var rom = CreateRom();
        Write(
            rom,
            0x8000,
            [
                0xA9, 0x00,
                0x8D, 0x03, 0x20,
                0xA2, 0xB4,
                0xBD, 0x4C, 0x01,
                0x8D, 0x04, 0x20,
                0xE8,
                0xD0, 0xF7,
                0x60,
            ]);
        var cpu = new NesTestCpu(rom);
        for (var index = 0; index < 76; index++)
        {
            cpu.SetRam((ushort)(0x0200 + index), (byte)index);
        }

        var result = cpu.RunRoutine(0x8000);

        Assert.Equal(1_071, result.Cycles - 6);
        Assert.Equal(76, cpu.OamWrites.Count);
        Assert.Equal(Enumerable.Range(0, 76).Select(index => (byte)index), cpu.OamWrites.Select(write => write.Value));
    }

    private static byte[] CreateRom()
    {
        var rom = new byte[16 + (32 * 1_024) + (8 * 1_024)];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 2;
        rom[5] = 1;
        rom[16 + 0x7FFC] = 0x00;
        rom[16 + 0x7FFD] = 0x80;
        return rom;
    }

    private static void Write(byte[] rom, ushort address, ReadOnlySpan<byte> bytes) =>
        bytes.CopyTo(rom.AsSpan(16 + address - 0x8000));
}
