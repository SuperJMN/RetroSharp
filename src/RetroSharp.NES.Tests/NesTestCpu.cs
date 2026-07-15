namespace RetroSharp.NES.Tests;

using RetroSharp.NES;

internal readonly record struct NesRoutineResult(byte A, byte X, byte Y, bool Carry, long Cycles);

internal readonly record struct NesPpuWrite(
    ushort Register,
    byte Value,
    ushort? VramAddress,
    long Cycle,
    bool RenderingEnabled = false);

internal readonly record struct NesOamWrite(ushort Address, byte Value, long Cycle, bool RenderingEnabled);

internal sealed class NesTestCpu
{
    private const long PpuCyclesPerFrame = 341 * 262;
    private const long PpuCyclesUntilVblank = (341 * 241) + 1;
    private readonly byte[] prg;
    private readonly byte[] ram = new byte[0x800];
    private readonly byte[] ppuMemory = new byte[0x4000];
    private readonly byte[] oam = new byte[0x100];
    private readonly int mapper;
    private readonly bool fourScreen;
    private readonly bool verticalMirroring;
    private byte selectedRegister;
    private byte ppuControl;
    private byte ppuMask;
    private byte ppuStatus;
    private ushort ppuAddress;
    private bool ppuWriteToggle;
    private byte oamAddress;
    private byte controllerShift;
    private bool controllerStrobe;
    private byte scrollX;
    private byte scrollY;
    private byte a;
    private byte x;
    private byte y;
    private byte stackPointer = 0xFD;
    private bool carry;
    private bool zero;
    private bool negative;
    private bool interruptDisable;
    private bool overflow;
    private readonly ushort resetVector;
    private ushort pc;
    private long cycles;
    private readonly List<long> ppuFrameStarts = [0];
    private long nextVblankPpuCycle = PpuCyclesUntilVblank;
    private long nextFrameStartPpuCycle = PpuCyclesPerFrame;
    private int ppuFrame;
    private bool started;
    private (byte OuterBank, ushort Entry, uint Offset)? nestedReadInjection;
    private byte? nmiInjectionBank;
    private bool injecting;

    public NesTestCpu(byte[] rom)
    {
        ArgumentNullException.ThrowIfNull(rom);
        mapper = (rom[6] >> 4) | (rom[7] & 0xF0);
        fourScreen = (rom[6] & 0x08) != 0;
        verticalMirroring = (rom[6] & 0x01) != 0;
        var prgLength = rom[4] * 16 * 1_024;
        prg = rom.AsSpan(16, prgLength).ToArray();
        var chrLength = rom[5] * 8 * 1_024;
        var chr = chrLength == 0 ? new byte[8 * 1_024] : rom.AsSpan(16 + prgLength, chrLength).ToArray();
        chr.CopyTo(ppuMemory, 0);
        resetVector = ReadWord(0xFFFC);
    }

    public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase);

    public byte CurrentR6Bank { get; private set; }

    public byte CurrentR7Bank { get; private set; } = 1;

    public List<int> R6BankWrites { get; } = [];

    public List<NesRoutineResult> NestedReadResults { get; } = [];

    public List<NesPpuWrite> PpuWrites { get; } = [];

    public List<NesOamWrite> OamWrites { get; } = [];

    public List<long> PpuStatusReadCycles { get; } = [];

    public int NmiCount { get; private set; }

    public int PhysicalFrames { get; private set; }

    public int ResetCount { get; private set; }

    public long VBlankWaitCompletions { get; private set; }

    public long Cycles => cycles;

    public byte PpuControl => ppuControl;

    public byte PpuMask => ppuMask;

    public byte ScrollX => scrollX;

    public byte ScrollY => scrollY;

    public bool RenderingEnabled => (ppuMask & 0x18) != 0;

    public void SetR6Bank(byte bank) => CurrentR6Bank = bank;

    public void SetRam(ushort address, byte value) => ram[address & 0x07FF] = value;

    public byte Ram(ushort address) => ram[address & 0x07FF];

    public byte PpuVram(ushort address) => ppuMemory[NormalizePpuAddress(address)];

    public byte Oam(byte address) => oam[address];

    public (int Scanline, int Dot, string Phase) PpuTiming(long cpuCycle, bool? renderingEnabled = null)
    {
        var ppuCycle = cpuCycle * 3;
        var frameIndex = ppuFrameStarts.BinarySearch(ppuCycle);
        if (frameIndex < 0)
        {
            frameIndex = ~frameIndex - 1;
        }

        var frameCycle = ppuCycle - ppuFrameStarts[Math.Max(0, frameIndex)];
        var scanline = (int)(frameCycle / 341);
        var dot = (int)(frameCycle % 341);
        var phase = !(renderingEnabled ?? RenderingEnabled)
            ? "rendering-disabled"
            : scanline is >= 241 and <= 260
                ? "vblank"
                : scanline == 240
                    ? "post-render"
                    : scanline == 261 ? "pre-render" : "visible";
        return (scanline, dot, phase);
    }

    public void RunFrames(int targetFrame, int maxInstructionsPerFrame = 1_000_000)
    {
        if (targetFrame < PhysicalFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFrame), targetFrame, "Target frame cannot move backwards.");
        }

        EnsureStarted();
        var maximumInstructions = checked((targetFrame - PhysicalFrames + 1) * maxInstructionsPerFrame);
        for (var instruction = 0;
             PhysicalFrames < targetFrame && instruction < maximumInstructions;
             instruction++)
        {
            Step();
            ProcessPpuEvents();
        }

        if (PhysicalFrames != targetFrame)
        {
            throw new InvalidOperationException(
                $"NES test program did not reach physical frame {targetFrame} within {maximumInstructions} instructions (PC=${pc:X4}, cycles={cycles}).");
        }
    }

    public void SetPackOffset(uint offset)
    {
        SetRam(NesRuntimeMemoryLayout.WorldPack.SourceOffset0, (byte)offset);
        SetRam(NesRuntimeMemoryLayout.WorldPack.SourceOffset1, (byte)(offset >> 8));
        SetRam(NesRuntimeMemoryLayout.WorldPack.SourceOffset2, (byte)(offset >> 16));
        SetRam(NesRuntimeMemoryLayout.WorldPack.SourceOffset3, (byte)(offset >> 24));
    }

    public void SetChunkAndSlot(ushort chunkIndex, byte slot)
    {
        SetRam(NesRuntimeMemoryLayout.WorldPack.ChunkIndexLow, (byte)chunkIndex);
        SetRam(NesRuntimeMemoryLayout.WorldPack.ChunkIndexHigh, (byte)(chunkIndex >> 8));
        SetRam(NesRuntimeMemoryLayout.WorldPack.SlotIndex, slot);
    }

    public void SetWorldPackCoordinates(ushort x, ushort y)
    {
        SetRam(NesRuntimeMemoryLayout.WorldPack.HardwareXLow, (byte)x);
        SetRam(NesRuntimeMemoryLayout.WorldPack.HardwareXHigh, (byte)(x >> 8));
        SetRam(NesRuntimeMemoryLayout.WorldPack.HardwareYLow, (byte)y);
        SetRam(NesRuntimeMemoryLayout.WorldPack.HardwareYHigh, (byte)(y >> 8));
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

    private void EnsureStarted()
    {
        if (started)
        {
            return;
        }

        started = true;
        ResetCount++;
        stackPointer = 0xFD;
        interruptDisable = true;
        pc = resetVector;
    }

    private void ProcessPpuEvents()
    {
        var ppuCycles = cycles * 3;
        while (true)
        {
            if (nextVblankPpuCycle <= nextFrameStartPpuCycle && ppuCycles >= nextVblankPpuCycle)
            {
                ppuStatus |= 0x80;
                nextVblankPpuCycle = long.MaxValue;
                if ((ppuControl & 0x80) != 0)
                {
                    TriggerNmi();
                    ppuCycles = cycles * 3;
                }

                continue;
            }

            if (ppuCycles < nextFrameStartPpuCycle)
            {
                return;
            }

            ppuFrame++;
            ppuFrameStarts.Add(nextFrameStartPpuCycle);
            PhysicalFrames++;
            ppuStatus &= 0x7F;
            var frameLength = RenderingEnabled && (ppuFrame & 1) != 0
                ? PpuCyclesPerFrame - 1
                : PpuCyclesPerFrame;
            nextVblankPpuCycle = nextFrameStartPpuCycle + PpuCyclesUntilVblank;
            nextFrameStartPpuCycle += frameLength;
        }
    }

    private void Step()
    {
        if (started && cycles > 0 && pc == resetVector)
        {
            ResetCount++;
        }

        var opcode = Read(pc++);
        switch (opcode)
        {
            case 0x05: Or(Read(Read(pc++))); cycles += 3; break;
            case 0x09: Or(Read(pc++)); cycles += 2; break;
            case 0x0A: carry = (a & 0x80) != 0; LoadA((byte)(a << 1)); cycles += 2; break;
            case 0x0D: Or(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0x10: Branch(!negative); break;
            case 0x18: carry = false; cycles += 2; break;
            case 0x20: Call(); cycles += 6; break;
            case 0x25: And(Read(Read(pc++))); cycles += 3; break;
            case 0x29: And(Read(pc++)); cycles += 2; break;
            case 0x2C:
                {
                    var value = Read(ReadWordAndAdvance());
                    zero = (a & value) == 0;
                    negative = (value & 0x80) != 0;
                    overflow = (value & 0x40) != 0;
                    cycles += 4;
                    break;
                }
            case 0x30: Branch(negative); break;
            case 0x38: carry = true; cycles += 2; break;
            case 0x40:
                UnpackStatus(Pop());
                pc = (ushort)(Pop() | Pop() << 8);
                cycles += 6;
                break;
            case 0x48: Push(a); cycles += 3; break;
            case 0x45: LoadA((byte)(a ^ Read(Read(pc++)))); cycles += 3; break;
            case 0x46:
                {
                    var address = Read(pc++);
                    var value = Read(address);
                    carry = (value & 1) != 0;
                    value >>= 1;
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 5;
                    break;
                }
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
            case 0x65: Add(Read(Read(pc++))); cycles += 3; break;
            case 0x66:
                {
                    var address = Read(pc++);
                    var value = Read(address);
                    var oldCarry = carry;
                    carry = (value & 1) != 0;
                    value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 5;
                    break;
                }
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
            case 0x75: Add(Read((byte)(Read(pc++) + x))); cycles += 4; break;
            case 0x78: interruptDisable = true; cycles += 2; break;
            case 0x84: Write(Read(pc++), y); cycles += 3; break;
            case 0x85: Write(Read(pc++), a); cycles += 3; break;
            case 0x86: Write(Read(pc++), x); cycles += 3; break;
            case 0x8C: Write(ReadWordAndAdvance(), y); cycles += 4; break;
            case 0x8D: Write(ReadWordAndAdvance(), a); cycles += 4; break;
            case 0x8E: Write(ReadWordAndAdvance(), x); cycles += 4; break;
            case 0x90: Branch(!carry); break;
            case 0x91:
                {
                    var pointer = Read(pc++);
                    var address = (ushort)(Read(pointer) | Read((byte)(pointer + 1)) << 8);
                    Write((ushort)(address + y), a);
                    cycles += 6;
                    break;
                }
            case 0x95: Write((byte)(Read(pc++) + x), a); cycles += 4; break;
            case 0x99: Write((ushort)(ReadWordAndAdvance() + y), a); cycles += 5; break;
            case 0x98: LoadA(y); cycles += 2; break;
            case 0x9A: stackPointer = x; cycles += 2; break;
            case 0x9D: Write((ushort)(ReadWordAndAdvance() + x), a); cycles += 5; break;
            case 0xA0: LoadY(Read(pc++)); cycles += 2; break;
            case 0xA2: LoadX(Read(pc++)); cycles += 2; break;
            case 0xA4: LoadY(Read(Read(pc++))); cycles += 3; break;
            case 0xA5: LoadA(Read(Read(pc++))); cycles += 3; break;
            case 0xA6: LoadX(Read(Read(pc++))); cycles += 3; break;
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
            case 0xB5: LoadA(Read((byte)(Read(pc++) + x))); cycles += 4; break;
            case 0xBD: LoadA(Read((ushort)(ReadWordAndAdvance() + x))); cycles += 4; break;
            case 0xC5: Compare(a, Read(Read(pc++))); cycles += 3; break;
            case 0xC6:
                {
                    var address = Read(pc++);
                    var value = (byte)(Read(address) - 1);
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 5;
                    break;
                }
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
            case 0xD8: cycles += 2; break;
            case 0xE0: Compare(x, Read(pc++)); cycles += 2; break;
            case 0xE6:
                {
                    var address = Read(pc++);
                    var value = (byte)(Read(address) + 1);
                    Write(address, value);
                    SetZeroNegative(value);
                    cycles += 5;
                    break;
                }
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

        if (address < 0x4000)
        {
            var register = (ushort)(0x2000 | (address & 0x07));
            if (register == 0x2002)
            {
                ppuWriteToggle = false;
                PpuStatusReadCycles.Add(cycles);
                var status = ppuStatus;
                if ((status & 0x80) != 0)
                {
                    VBlankWaitCompletions++;
                }

                ppuStatus &= 0x7F;
                return status;
            }

            if (register == 0x2004)
            {
                return oam[oamAddress];
            }

            if (register == 0x2007)
            {
                var value = ppuMemory[NormalizePpuAddress(ppuAddress)];
                ppuAddress = (ushort)((ppuAddress + ((ppuControl & 0x04) != 0 ? 32 : 1)) & 0x3FFF);
                return value;
            }

            return 0;
        }

        if (address == 0x4016)
        {
            var value = (byte)(0x40 | (controllerShift & 1));
            if (!controllerStrobe)
            {
                controllerShift = (byte)((controllerShift >> 1) | 0x80);
            }

            return value;
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

        if (address < 0x4000)
        {
            WritePpuRegister((ushort)(0x2000 | (address & 0x07)), value);
            return;
        }

        if (address == 0x4014)
        {
            var source = value << 8;
            for (var index = 0; index < 256; index++)
            {
                var target = oamAddress++;
                oam[target] = Read((ushort)(source + index));
                OamWrites.Add(new NesOamWrite((ushort)(NesRuntimeMemoryLayout.Sprite.OamShadow + target), oam[target], cycles, RenderingEnabled));
            }

            cycles += 513;
            return;
        }

        if (address == 0x4016)
        {
            var nextStrobe = (value & 1) != 0;
            if (nextStrobe || controllerStrobe)
            {
                controllerShift = ControllerState();
            }

            controllerStrobe = nextStrobe;
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

    private void WritePpuRegister(ushort register, byte value)
    {
        switch (register)
        {
            case 0x2000:
                ppuControl = value;
                PpuWrites.Add(new NesPpuWrite(register, value, null, cycles, RenderingEnabled));
                break;
            case 0x2001:
                ppuMask = value;
                PpuWrites.Add(new NesPpuWrite(register, value, null, cycles, RenderingEnabled));
                break;
            case 0x2003:
                oamAddress = value;
                PpuWrites.Add(new NesPpuWrite(register, value, null, cycles, RenderingEnabled));
                break;
            case 0x2004:
                oam[oamAddress] = value;
                OamWrites.Add(new NesOamWrite((ushort)(NesRuntimeMemoryLayout.Sprite.OamShadow + oamAddress), value, cycles, RenderingEnabled));
                oamAddress++;
                PpuWrites.Add(new NesPpuWrite(register, value, null, cycles, RenderingEnabled));
                break;
            case 0x2005:
                if (!ppuWriteToggle)
                {
                    scrollX = value;
                }
                else
                {
                    scrollY = value;
                }

                ppuWriteToggle = !ppuWriteToggle;
                PpuWrites.Add(new NesPpuWrite(register, value, null, cycles, RenderingEnabled));
                break;
            case 0x2006:
                if (!ppuWriteToggle)
                {
                    ppuAddress = (ushort)((ppuAddress & 0x00FF) | (value & 0x3F) << 8);
                }
                else
                {
                    ppuAddress = (ushort)((ppuAddress & 0x3F00) | value);
                }

                ppuWriteToggle = !ppuWriteToggle;
                PpuWrites.Add(new NesPpuWrite(register, value, ppuWriteToggle ? null : ppuAddress, cycles, RenderingEnabled));
                break;
            case 0x2007:
                ppuMemory[NormalizePpuAddress(ppuAddress)] = value;
                PpuWrites.Add(new NesPpuWrite(register, value, ppuAddress, cycles, RenderingEnabled));
                ppuAddress = (ushort)((ppuAddress + ((ppuControl & 0x04) != 0 ? 32 : 1)) & 0x3FFF);
                break;
            default:
                PpuWrites.Add(new NesPpuWrite(register, value, null, cycles, RenderingEnabled));
                break;
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

    private byte PackStatus() => (byte)(
        0x20 |
        (negative ? 0x80 : 0) |
        (overflow ? 0x40 : 0) |
        (interruptDisable ? 0x04 : 0) |
        (zero ? 0x02 : 0) |
        (carry ? 0x01 : 0));

    private void UnpackStatus(byte status)
    {
        negative = (status & 0x80) != 0;
        overflow = (status & 0x40) != 0;
        interruptDisable = (status & 0x04) != 0;
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

    private void Push(byte value) => ram[NesRuntimeMemoryLayout.Stack.Start | stackPointer--] = value;

    private byte Pop() => ram[NesRuntimeMemoryLayout.Stack.Start | ++stackPointer];

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

    private byte ControllerState()
    {
        byte state = 0;
        if (Held.Contains("a")) state |= 1 << 0;
        if (Held.Contains("b")) state |= 1 << 1;
        if (Held.Contains("select")) state |= 1 << 2;
        if (Held.Contains("start")) state |= 1 << 3;
        if (Held.Contains("up")) state |= 1 << 4;
        if (Held.Contains("down")) state |= 1 << 5;
        if (Held.Contains("left")) state |= 1 << 6;
        if (Held.Contains("right")) state |= 1 << 7;
        return state;
    }

    private ushort NormalizePpuAddress(ushort address)
    {
        var normalized = (ushort)(address & 0x3FFF);
        if (normalized is >= 0x3000 and < 0x3F00)
        {
            normalized -= 0x1000;
        }

        if (normalized >= 0x3F00)
        {
            normalized = (ushort)(0x3F00 + (normalized - 0x3F00) % 0x20);
            if ((normalized & 0x13) == 0x10)
            {
                normalized -= 0x10;
            }

            return normalized;
        }

        if (normalized is < 0x2000 or >= 0x3000 || fourScreen)
        {
            return normalized;
        }

        var table = (normalized - 0x2000) / 0x400;
        var offset = (normalized - 0x2000) % 0x400;
        var physicalTable = verticalMirroring ? table % 2 : table / 2;
        return (ushort)(0x2000 + physicalTable * 0x400 + offset);
    }
}
