namespace RetroSharp.NES.Tests;

using RetroSharp.NES;

internal readonly record struct NesRoutineResult(byte A, byte X, byte Y, bool Carry, long Cycles);

internal sealed class NesTestCpu
{
    private readonly byte[] prg;
    private readonly byte[] ram = new byte[0x800];
    private readonly int mapper;
    private byte selectedRegister;
    private byte a;
    private byte x;
    private byte y;
    private byte stackPointer = 0xFD;
    private bool carry;
    private bool zero;
    private bool negative;
    private ushort pc;
    private long cycles;
    private (byte OuterBank, ushort Entry, uint Offset)? nestedReadInjection;
    private byte? nmiInjectionBank;
    private bool injecting;

    public NesTestCpu(byte[] rom)
    {
        ArgumentNullException.ThrowIfNull(rom);
        mapper = (rom[6] >> 4) | (rom[7] & 0xF0);
        var prgLength = rom[4] * 16 * 1_024;
        prg = rom.AsSpan(16, prgLength).ToArray();
    }

    public byte CurrentR6Bank { get; private set; }

    public byte CurrentR7Bank { get; private set; } = 1;

    public List<int> R6BankWrites { get; } = [];

    public List<NesRoutineResult> NestedReadResults { get; } = [];

    public int NmiCount { get; private set; }

    public void SetR6Bank(byte bank) => CurrentR6Bank = bank;

    public void SetRam(ushort address, byte value) => ram[address & 0x07FF] = value;

    public byte Ram(ushort address) => ram[address & 0x07FF];

    public void SetPackOffset(uint offset)
    {
        SetRam(NesWorldPackRuntimeAbi.SourceOffset0, (byte)offset);
        SetRam(NesWorldPackRuntimeAbi.SourceOffset1, (byte)(offset >> 8));
        SetRam(NesWorldPackRuntimeAbi.SourceOffset2, (byte)(offset >> 16));
        SetRam(NesWorldPackRuntimeAbi.SourceOffset3, (byte)(offset >> 24));
    }

    public void SetChunkAndSlot(ushort chunkIndex, byte slot)
    {
        SetRam(NesWorldPackRuntimeAbi.ChunkIndexLow, (byte)chunkIndex);
        SetRam(NesWorldPackRuntimeAbi.ChunkIndexHigh, (byte)(chunkIndex >> 8));
        SetRam(NesWorldPackRuntimeAbi.SlotIndex, slot);
    }

    public void SetWorldPackCoordinates(ushort x, ushort y)
    {
        SetRam(NesWorldPackRuntimeAbi.HardwareXLow, (byte)x);
        SetRam(NesWorldPackRuntimeAbi.HardwareXHigh, (byte)(x >> 8));
        SetRam(NesWorldPackRuntimeAbi.HardwareYLow, (byte)y);
        SetRam(NesWorldPackRuntimeAbi.HardwareYHigh, (byte)(y >> 8));
    }

    public void InjectNestedReadAfterSelecting(byte outerBank, ushort entry, uint nestedOffset) =>
        nestedReadInjection = (outerBank, entry, nestedOffset);

    public void InjectNmiAfterSelecting(byte outerBank) => nmiInjectionBank = outerBank;

    public NesRoutineResult RunRoutine(ushort entry, int maxInstructions = 1_000_000)
    {
        var startCycles = cycles;
        pc = entry;
        Push(0xFF);
        Push(0xFE);
        for (var instruction = 0; instruction < maxInstructions && pc != 0xFFFF; instruction++)
        {
            Step();
        }

        if (pc != 0xFFFF)
        {
            throw new InvalidOperationException($"NES test routine at ${entry:X4} did not return within {maxInstructions} instructions.");
        }

        return new NesRoutineResult(a, x, y, carry, cycles - startCycles);
    }

    private void Step()
    {
        var opcode = Read(pc++);
        switch (opcode)
        {
            case 0x09: Or(Read(pc++)); cycles += 2; break;
            case 0x0A: carry = (a & 0x80) != 0; LoadA((byte)(a << 1)); cycles += 2; break;
            case 0x0D: Or(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0x18: carry = false; cycles += 2; break;
            case 0x20: Call(); cycles += 6; break;
            case 0x29: And(Read(pc++)); cycles += 2; break;
            case 0x38: carry = true; cycles += 2; break;
            case 0x40:
                UnpackStatus(Pop());
                pc = (ushort)(Pop() | Pop() << 8);
                cycles += 6;
                break;
            case 0x48: Push(a); cycles += 3; break;
            case 0x49: LoadA((byte)(a ^ Read(pc++))); cycles += 2; break;
            case 0x4A: carry = (a & 1) != 0; LoadA((byte)(a >> 1)); cycles += 2; break;
            case 0x4C: pc = ReadWord(pc); cycles += 3; break;
            case 0x4D: LoadA((byte)(a ^ Read(ReadWordAndAdvance()))); cycles += 4; break;
            case 0x4E:
                {
                    var address = ReadWordAndAdvance();
                    var value = Read(address);
                    carry = (value & 1) != 0;
                    value >>= 1;
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 6;
                    break;
                }
            case 0x60: Return(); cycles += 6; break;
            case 0x68: LoadA(Pop()); cycles += 4; break;
            case 0x69: Add(Read(pc++)); cycles += 2; break;
            case 0x6D: Add(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0x6E:
                {
                    var address = ReadWordAndAdvance();
                    var value = Read(address);
                    var oldCarry = carry;
                    carry = (value & 1) != 0;
                    value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 6;
                    break;
                }
            case 0x85: Write(Read(pc++), a); cycles += 3; break;
            case 0x8D: Write(ReadWordAndAdvance(), a); cycles += 4; break;
            case 0x90: Branch(!carry); break;
            case 0x91:
                {
                    var pointer = Read(pc++);
                    var address = (ushort)(Read(pointer) | Read((byte)(pointer + 1)) << 8);
                    Write((ushort)(address + y), a);
                    cycles += 6;
                    break;
                }
            case 0x99: Write((ushort)(ReadWordAndAdvance() + y), a); cycles += 5; break;
            case 0x9D: Write((ushort)(ReadWordAndAdvance() + x), a); cycles += 5; break;
            case 0xA0: LoadY(Read(pc++)); cycles += 2; break;
            case 0xA2: LoadX(Read(pc++)); cycles += 2; break;
            case 0xA5: LoadA(Read(Read(pc++))); cycles += 3; break;
            case 0xA9: LoadA(Read(pc++)); cycles += 2; break;
            case 0xAA: LoadX(a); cycles += 2; break;
            case 0xAC: LoadY(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0xAD: LoadA(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0xAE: LoadX(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0xB0: Branch(carry); break;
            case 0xB1:
                {
                    var pointer = Read(pc++);
                    var address = (ushort)(Read(pointer) | Read((byte)(pointer + 1)) << 8);
                    LoadA(Read((ushort)(address + y)));
                    cycles += 5;
                    break;
                }
            case 0xBD: LoadA(Read((ushort)(ReadWordAndAdvance() + x))); cycles += 4; break;
            case 0xC9: Compare(a, Read(pc++)); cycles += 2; break;
            case 0xC8: LoadY((byte)(y + 1)); cycles += 2; break;
            case 0xCA: LoadX((byte)(x - 1)); cycles += 2; break;
            case 0xCD: Compare(a, Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0xCE:
                {
                    var address = ReadWordAndAdvance();
                    var value = (byte)(Read(address) - 1);
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 6;
                    break;
                }
            case 0xD0: Branch(!zero); break;
            case 0xE0: Compare(x, Read(pc++)); cycles += 2; break;
            case 0xE8: LoadX((byte)(x + 1)); cycles += 2; break;
            case 0xEE:
                {
                    var address = ReadWordAndAdvance();
                    var value = (byte)(Read(address) + 1);
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 6;
                    break;
                }
            case 0xE5: Subtract(Read(Read(pc++))); cycles += 3; break;
            case 0xE9: Subtract(Read(pc++)); cycles += 2; break;
            case 0xF0: Branch(zero); break;
            default: throw new InvalidOperationException($"Unsupported NES test opcode ${opcode:X2} at ${(ushort)(pc - 1):X4}.");
        }
    }

    private byte Read(ushort address)
    {
        if (address < 0x2000)
        {
            return ram[address & 0x07FF];
        }

        if (address < 0x8000)
        {
            return 0;
        }

        if (mapper == 0)
        {
            return prg[(address - 0x8000) % prg.Length];
        }

        var bank = address switch
        {
            < 0xA000 => CurrentR6Bank,
            < 0xC000 => CurrentR7Bank,
            < 0xE000 => (byte)6,
            _ => (byte)7,
        };
        return prg[bank * 0x2000 + (address & 0x1FFF)];
    }

    private void Write(ushort address, byte value)
    {
        if (address < 0x2000)
        {
            ram[address & 0x07FF] = value;
            return;
        }

        if (address == 0x8000)
        {
            selectedRegister = (byte)(value & 0x07);
            return;
        }

        if (address != 0x8001)
        {
            return;
        }

        if (selectedRegister == 6)
        {
            CurrentR6Bank = value;
            R6BankWrites.Add(value);
            HandleR6Injection(value);
        }
        else if (selectedRegister == 7)
        {
            CurrentR7Bank = value;
        }
    }

    private void HandleR6Injection(byte bank)
    {
        if (injecting)
        {
            return;
        }

        if (nmiInjectionBank == bank)
        {
            nmiInjectionBank = null;
            injecting = true;
            TriggerNmi();
            injecting = false;
        }

        if (nestedReadInjection is not { } nested || nested.OuterBank != bank)
        {
            return;
        }

        nestedReadInjection = null;
        injecting = true;
        var savedPc = pc;
        var savedA = a;
        var savedX = x;
        var savedY = y;
        var savedCarry = carry;
        var savedZero = zero;
        var savedNegative = negative;
        SetPackOffset(nested.Offset);
        NestedReadResults.Add(RunRoutine(nested.Entry));
        pc = savedPc;
        a = savedA;
        x = savedX;
        y = savedY;
        carry = savedCarry;
        zero = savedZero;
        negative = savedNegative;
        injecting = false;
    }

    private void TriggerNmi()
    {
        var returnPc = pc;
        Push((byte)(returnPc >> 8));
        Push((byte)returnPc);
        Push(PackStatus());
        pc = ReadWord(0xFFFA);
        var guard = 32;
        do
        {
            Step();
        }
        while (pc != returnPc && --guard > 0);
        if (pc != returnPc)
        {
            throw new InvalidOperationException("NES test NMI handler did not return.");
        }
        NmiCount++;
    }

    private byte PackStatus() => (byte)(0x20 | (negative ? 0x80 : 0) | (zero ? 0x02 : 0) | (carry ? 0x01 : 0));

    private void UnpackStatus(byte status)
    {
        negative = (status & 0x80) != 0;
        zero = (status & 0x02) != 0;
        carry = (status & 0x01) != 0;
    }

    private ushort ReadWordAndAdvance()
    {
        var value = ReadWord(pc);
        pc += 2;
        return value;
    }

    private ushort ReadWord(ushort address) => (ushort)(Read(address) | Read((ushort)(address + 1)) << 8);

    private void Call()
    {
        var target = ReadWord(pc);
        var returnAddress = (ushort)(pc + 1);
        Push((byte)(returnAddress >> 8));
        Push((byte)returnAddress);
        pc = target;
    }

    private void Return() => pc = (ushort)((Pop() | Pop() << 8) + 1);

    private void Branch(bool condition)
    {
        var delta = unchecked((sbyte)Read(pc++));
        cycles += 2;
        if (condition)
        {
            pc = (ushort)(pc + delta);
            cycles++;
        }
    }

    private void Push(byte value) => ram[0x0100 | stackPointer--] = value;

    private byte Pop() => ram[0x0100 | ++stackPointer];

    private void LoadA(byte value)
    {
        a = value;
        SetZeroNegative(value);
    }

    private void LoadX(byte value)
    {
        x = value;
        SetZeroNegative(value);
    }

    private void LoadY(byte value)
    {
        y = value;
        SetZeroNegative(value);
    }

    private void And(byte value) => LoadA((byte)(a & value));

    private void Or(byte value) => LoadA((byte)(a | value));

    private void Add(byte value)
    {
        var result = a + value + (carry ? 1 : 0);
        carry = result > byte.MaxValue;
        LoadA((byte)result);
    }

    private void Subtract(byte value)
    {
        var result = a - value - (carry ? 0 : 1);
        carry = result >= 0;
        LoadA((byte)result);
    }

    private void Compare(byte left, byte right)
    {
        var result = left - right;
        carry = left >= right;
        zero = left == right;
        negative = (result & 0x80) != 0;
    }

    private void SetZeroNegative(byte value)
    {
        zero = value == 0;
        negative = (value & 0x80) != 0;
    }
}
