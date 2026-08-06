namespace RetroSharp.NES.Tests;

internal static class NesMmc3TestRomBuilder
{
    internal const int DefaultPrgBankCount = 8;

    internal static byte[] Create(int prgBankCount = DefaultPrgBankCount)
    {
        if (prgBankCount <= 0 || prgBankCount % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prgBankCount), prgBankCount, "MMC3 PRG banks must form whole 16 KiB iNES units.");
        }

        var prgLength = prgBankCount * 8 * 1_024;
        var rom = new byte[16 + prgLength + (8 * 1_024)];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = checked((byte)(prgBankCount / 2));
        rom[5] = 1;
        rom[6] = 0x40;

        for (var bank = 0; bank < prgBankCount; bank++)
        {
            rom.AsSpan(16 + (bank * 0x2000), 0x2000).Fill(BankMarker(bank));
        }

        WriteVector(rom, 0xFFFA, 0xE000);
        WriteVector(rom, 0xFFFC, 0xE000);
        WriteVector(rom, 0xFFFE, 0xE000);
        return rom;
    }

    internal static byte BankMarker(int bank) => checked((byte)(0x30 + bank));

    internal static void WritePrg(byte[] rom, int bank, ushort address, ReadOnlySpan<byte> bytes) =>
        bytes.CopyTo(rom.AsSpan(16 + (bank * 0x2000) + (address & 0x1FFF)));

    internal static void WriteVector(byte[] rom, ushort vectorAddress, ushort value) =>
        WritePrg(rom, PrgBankCount(rom) - 1, vectorAddress, [(byte)value, (byte)(value >> 8)]);

    private static int PrgBankCount(byte[] rom) => rom[4] * 2;
}
