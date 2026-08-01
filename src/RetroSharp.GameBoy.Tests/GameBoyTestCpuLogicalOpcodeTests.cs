namespace RetroSharp.GameBoy.Tests;

using Xunit;

public sealed class GameBoyTestCpuLogicalOpcodeTests
{
    private const ushort ResultAddress = 0xC000;
    private const ushort FlagsAddress = 0xC001;
    private const ushort CompletionAddress = 0xC002;
    private const ushort OperandAddress = 0xC100;

    public static TheoryData<byte> LogicalR8Opcodes => new()
    {
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF,
        0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7,
    };

    [Theory]
    [MemberData(nameof(LogicalR8Opcodes))]
    public void Logical_r8_opcodes_execute_with_correct_result_flags_and_cycles(byte opcode)
    {
        const byte left = 0x5A;
        var registerCode = opcode & 0x07;
        var operand = registerCode == 0x07 ? left : (byte)0xA5;
        var expectedResult = Apply(opcode, left, operand);
        var expectedFlags = ExpectedFlags(opcode, expectedResult);
        var rom = BuildRom(opcode, left, operand);
        var cpu = new GameBoyTestCpu(rom);

        cpu.RunUntilWramEquals(CompletionAddress, 1, maxInstructions: 20);

        Assert.Equal(expectedResult, cpu.Wram(ResultAddress));
        Assert.Equal(expectedFlags, cpu.Wram(FlagsAddress));
        Assert.Equal(ExpectedCycles(registerCode), cpu.Cycles);
    }

    private static byte[] BuildRom(byte opcode, byte left, byte operand)
    {
        var program = new List<byte>();
        EmitOperandSetup(program, opcode & 0x07, operand);
        program.AddRange(
        [
            0x3E, left,                         // LD A,left
            0x37,                               // SCF
            0x2F,                               // CPL
            0x2F,                               // CPL; restore A with C, N and H set
            opcode,
            0xEA, Low(ResultAddress), High(ResultAddress),
            0xF5,                               // PUSH AF
            0xC1,                               // POP BC
            0x79,                               // LD A,C
            0xEA, Low(FlagsAddress), High(FlagsAddress),
            0x3E, 0x01,                         // LD A,1
            0xEA, Low(CompletionAddress), High(CompletionAddress),
        ]);

        var rom = new byte[0x8000];
        program.CopyTo(rom, 0x0100);
        return rom;
    }

    private static void EmitOperandSetup(List<byte> program, int registerCode, byte operand)
    {
        switch (registerCode)
        {
            case 0: program.AddRange([0x06, operand]); break; // LD B,n
            case 1: program.AddRange([0x0E, operand]); break; // LD C,n
            case 2: program.AddRange([0x16, operand]); break; // LD D,n
            case 3: program.AddRange([0x1E, operand]); break; // LD E,n
            case 4: program.AddRange([0x26, operand]); break; // LD H,n
            case 5: program.AddRange([0x2E, operand]); break; // LD L,n
            case 6:
                program.AddRange(
                [
                    0x21, Low(OperandAddress), High(OperandAddress), // LD HL,nn
                    0x36, operand,                                  // LD (HL),n
                ]);
                break;
            case 7:
                break;
        }
    }

    private static byte Apply(byte opcode, byte left, byte operand) => (opcode & 0xF8) switch
    {
        0xA0 => (byte)(left & operand),
        0xA8 => (byte)(left ^ operand),
        0xB0 => (byte)(left | operand),
        _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
    };

    private static byte ExpectedFlags(byte opcode, byte result)
    {
        const byte flagZ = 0x80;
        const byte flagH = 0x20;
        var zero = result == 0 ? flagZ : (byte)0;
        return (byte)(zero | ((opcode & 0xF8) == 0xA0 ? flagH : 0));
    }

    private static long ExpectedCycles(int registerCode) => registerCode switch
    {
        6 => 140,
        7 => 112,
        _ => 120,
    };

    private static byte Low(ushort value) => (byte)value;

    private static byte High(ushort value) => (byte)(value >> 8);
}
