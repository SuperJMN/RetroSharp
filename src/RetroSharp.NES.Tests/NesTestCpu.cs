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

internal readonly record struct NesOamDmaTransfer(
    byte SourcePage,
    long Cycle,
    byte[] RamSnapshot,
    byte[] SourceSnapshot,
    byte ScrollX,
    byte ScrollY,
    byte PpuControl,
    bool RenderingEnabled);

internal readonly record struct NesApuWrite(ushort Register, byte Value, long Cycle);

internal readonly record struct NesRamByteWrite(ushort Address, byte Value, long Cycle);

internal readonly record struct NesCpuStep(ushort ProgramCounter, long Cycle, byte StackPointer);

/// <summary>
/// The MMC3 state an interrupted mainline depends on: the two switchable PRG bank selections and
/// the write-only bank-select latch. The latch matters on its own because
/// <c>mmc3_select_r6</c> arms it with <c>STA $8000</c> before completing the switch with
/// <c>STA $8001</c>, so an interrupt taken between those two stores can corrupt the switch without
/// ever changing a bank number.
/// </summary>
internal readonly record struct NesMapperState(byte R6Bank, byte R7Bank, byte BankLatch)
{
    public override string ToString() => $"R6={R6Bank} R7={R7Bank} latch={BankLatch}";
}

internal readonly record struct NesCpuRegisters(byte A, byte X, byte Y)
{
    public override string ToString() => $"A=${A:X2} X=${X:X2} Y=${Y:X2}";
}

/// <summary>
/// One taken NMI, recorded so a guard can judge whether the handler was fixed-resident and left
/// the interrupted mainline undisturbed. <see cref="SwitchableWindowAccesses"/> counts every read
/// or write the handler made through <c>$8000-$BFFF</c>, which includes its own instruction fetches
/// when the handler body is not fixed-resident.
/// </summary>
internal readonly record struct NesNmiObservation(
    long Cycle,
    ushort InterruptedProgramCounter,
    ushort HandlerEntry,
    NesMapperState MapperBefore,
    NesMapperState MapperAfter,
    NesCpuRegisters RegistersBefore,
    NesCpuRegisters RegistersAfter,
    ushort LowestHandlerAddress,
    ushort HighestHandlerAddress,
    int SwitchableWindowAccesses)
{
    public override string ToString() =>
        $"cycle={Cycle} interrupted=${InterruptedProgramCounter:X4} entry=${HandlerEntry:X4} " +
        $"handler=${LowestHandlerAddress:X4}-${HighestHandlerAddress:X4} " +
        $"mapper[{MapperBefore} -> {MapperAfter}] registers[{RegistersBefore} -> {RegistersAfter}] " +
        $"switchableAccesses={SwitchableWindowAccesses}";
}

internal sealed class NesTestCpu
{
    private const long PpuCyclesPerFrame = 341 * 262;
    private const long PpuCyclesUntilVblank = (341 * 241) + 1;
    private readonly byte[] prg;
    private readonly int prgBankCount;
    private readonly byte[] ram = new byte[0x800];
    private readonly byte[] ppuMemory = new byte[0x4000];
    private readonly byte[] oam = new byte[0x100];
    private readonly List<NesRamByteWrite> ramByteWrites = [];
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
    private int nmiDepth;
    private int switchableWindowAccesses;

    public NesTestCpu(byte[] rom)
    {
        ArgumentNullException.ThrowIfNull(rom);
        mapper = (rom[6] >> 4) | (rom[7] & 0xF0);
        fourScreen = (rom[6] & 0x08) != 0;
        verticalMirroring = (rom[6] & 0x01) != 0;
        var prgLength = rom[4] * 16 * 1_024;
        prg = rom.AsSpan(16, prgLength).ToArray();
        prgBankCount = prgLength / 0x2000;
        var chrLength = rom[5] * 8 * 1_024;
        var chr = chrLength == 0 ? new byte[8 * 1_024] : rom.AsSpan(16 + prgLength, chrLength).ToArray();
        chr.CopyTo(ppuMemory, 0);
        resetVector = ReadWord(0xFFFC);
    }

    public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase);

    public byte CurrentR6Bank { get; private set; }

    public byte CurrentR7Bank { get; private set; } = 1;

    public List<int> R6BankWrites { get; } = [];

    public List<int> R7BankWrites { get; } = [];

    public List<NesRoutineResult> NestedReadResults { get; } = [];

    public List<NesPpuWrite> PpuWrites { get; } = [];

    public List<NesOamWrite> OamWrites { get; } = [];

    public List<NesOamDmaTransfer> OamDmaTransfers { get; } = [];

    public List<NesApuWrite> ApuWrites { get; } = [];

    public List<long> PpuStatusReadCycles { get; } = [];

    public int NmiCount { get; private set; }

    public List<long> NmiCompletionCycles { get; } = [];

    /// <summary>Every NMI taken during the run, in order.</summary>
    public List<NesNmiObservation> NmiObservations { get; } = [];

    public int PhysicalFrames { get; private set; }

    public int ResetCount { get; private set; }

    public long VBlankWaitCompletions { get; private set; }

    public long Cycles => cycles;

    public Action<NesCpuStep>? OnStep { get; set; }

    public byte PpuControl => ppuControl;

    public byte PpuMask => ppuMask;

    public byte ScrollX => scrollX;

    public byte ScrollY => scrollY;

    public bool RenderingEnabled => (ppuMask & 0x18) != 0;

    public void SetR6Bank(byte bank) => CurrentR6Bank = bank;

    public void SetRam(ushort address, byte value) => ram[address & 0x07FF] = value;

    public byte Ram(ushort address) => ram[address & 0x07FF];

    public HashSet<ushort> TracedRamBytes { get; } = [];

    public IReadOnlyList<NesRamByteWrite> RamByteWrites => ramByteWrites;

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

        OnStep?.Invoke(new NesCpuStep(pc, cycles, stackPointer));
        var opcode = Read(pc++);
        switch (opcode)
        {
            case 0x05: Or(Read(Read(pc++))); cycles += 3; break;
            case 0x08: Push((byte)(PackStatus() | 0x10)); cycles += 3; break;
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
            case 0x28: UnpackStatus(Pop()); cycles += 4; break;
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
                    Write(address, value, 4);
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
                    Write(address, value, 5);
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
                    Write(address, value, 4);
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
                    Write(address, value, 5);
                    SetZeroNegative(value);
                    cycles += 6;
                    break;
                }
            case 0x75: Add(Read((byte)(Read(pc++) + x))); cycles += 4; break;
            case 0x78: interruptDisable = true; cycles += 2; break;
            case 0x84: Write(Read(pc++), y, 2); cycles += 3; break;
            case 0x85: Write(Read(pc++), a, 2); cycles += 3; break;
            case 0x86: Write(Read(pc++), x, 2); cycles += 3; break;
            case 0x8C: Write(ReadWordAndAdvance(), y, 3); cycles += 4; break;
            case 0x8D: Write(ReadWordAndAdvance(), a, 3); cycles += 4; break;
            case 0x8E: Write(ReadWordAndAdvance(), x, 3); cycles += 4; break;
            case 0x90: Branch(!carry); break;
            case 0x91:
                {
                    var pointer = Read(pc++);
                    var address = (ushort)(Read(pointer) | Read((byte)(pointer + 1)) << 8);
                    Write((ushort)(address + y), a, 5);
                    cycles += 6;
                    break;
                }
            case 0x95: Write((byte)(Read(pc++) + x), a, 3); cycles += 4; break;
            case 0x99: Write((ushort)(ReadWordAndAdvance() + y), a, 4); cycles += 5; break;
            case 0x98: LoadA(y); cycles += 2; break;
            case 0x9A: stackPointer = x; cycles += 2; break;
            case 0x9D: Write((ushort)(ReadWordAndAdvance() + x), a, 4); cycles += 5; break;
            case 0xA0: LoadY(Read(pc++)); cycles += 2; break;
            case 0xA2: LoadX(Read(pc++)); cycles += 2; break;
            case 0xA4: LoadY(Read(Read(pc++))); cycles += 3; break;
            case 0xA5: LoadA(Read(Read(pc++))); cycles += 3; break;
            case 0xA6: LoadX(Read(Read(pc++))); cycles += 3; break;
            case 0xA8: LoadY(a); cycles += 2; break;
            case 0xA9: LoadA(Read(pc++)); cycles += 2; break;
            case 0xAA: LoadX(a); cycles += 2; break;
            case 0xAC: LoadY(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0xAD: LoadA(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0xAE: LoadX(Read(ReadWordAndAdvance())); cycles += 4; break;
            case 0xB0: Branch(carry); break;
            case 0xB1:
                {
                    var pointer = Read(pc++);
                    var baseAddress = (ushort)(Read(pointer) | Read((byte)(pointer + 1)) << 8);
                    var address = (ushort)(baseAddress + y);
                    LoadA(Read(address));
                    cycles += 5 + (CrossesPage(baseAddress, address) ? 1 : 0);
                    break;
                }
            case 0xB5: LoadA(Read((byte)(Read(pc++) + x))); cycles += 4; break;
            case 0xB9:
                {
                    var baseAddress = ReadWordAndAdvance();
                    var address = (ushort)(baseAddress + y);
                    LoadA(Read(address));
                    cycles += 4 + (CrossesPage(baseAddress, address) ? 1 : 0);
                    break;
                }
            case 0xBD:
                {
                    var baseAddress = ReadWordAndAdvance();
                    var address = (ushort)(baseAddress + x);
                    LoadA(Read(address));
                    cycles += 4 + (CrossesPage(baseAddress, address) ? 1 : 0);
                    break;
                }
            case 0xC5: Compare(a, Read(Read(pc++))); cycles += 3; break;
            case 0xC6:
                {
                    var address = Read(pc++);
                    var value = (byte)(Read(address) - 1);
                    Write(address, value, 4);
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
                    Write(address, value, 5);
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
                    Write(address, value, 4);
                    SetZeroNegative(value);
                    cycles += 5;
                    break;
                }
            case 0xE8: LoadX((byte)(x + 1)); cycles += 2; break;
            case 0xEE:
                {
                    var address = ReadWordAndAdvance();
                    var value = (byte)(Read(address) + 1);
                    Write(address, value, 5);
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
        CountSwitchableWindowAccess(address);
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

        // MMC3 PRG mode 0 fixes $C000-$DFFF to the second-to-last physical bank and $E000-$FFFF to
        // the last one, so both follow the board size instead of a 64 KiB assumption.
        var bank = address switch
        {
            < 0xA000 => CurrentR6Bank,
            < 0xC000 => CurrentR7Bank,
            < 0xE000 => (byte)(prgBankCount - 2),
            _ => (byte)(prgBankCount - 1),
        };
        if (bank >= prgBankCount)
        {
            // Hardware would alias here; the harness reports it so a linker bug that selects a
            // bank the image does not contain cannot pass as a silently wrapped read.
            throw new InvalidOperationException(
                $"NES test CPU read ${address:X4} through PRG bank {bank}, beyond the {prgBankCount}-bank image.");
        }

        return prg[bank * 0x2000 + (address & 0x1FFF)];
    }

    private void Write(ushort address, byte value, int busCycleOffset)
    {
        CountSwitchableWindowAccess(address);
        if (address < 0x2000)
        {
            ram[address & 0x07FF] = value;
            var normalizedAddress = checked((ushort)(address & 0x07FF));
            if (TracedRamBytes.Contains(normalizedAddress))
            {
                ramByteWrites.Add(new NesRamByteWrite(normalizedAddress, value, cycles));
            }
            return;
        }

        if (address is >= 0x4000 and <= 0x4017)
        {
            ApuWrites.Add(new(address, value, cycles));
        }

        if (address < 0x4000)
        {
            WritePpuRegister((ushort)(0x2000 | (address & 0x07)), value, cycles + busCycleOffset);
            return;
        }

        if (address == 0x4014)
        {
            var source = value << 8;
            var sourceSnapshot = Enumerable.Range(0, 256)
                .Select(index => Read((ushort)(source + index)))
                .ToArray();
            OamDmaTransfers.Add(new(value, cycles, (byte[])ram.Clone(), sourceSnapshot, scrollX, scrollY, ppuControl, RenderingEnabled));
            for (var index = 0; index < 256; index++)
            {
                var target = oamAddress++;
                oam[target] = sourceSnapshot[index];
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
            R7BankWrites.Add(value);
        }
    }

    private void WritePpuRegister(ushort register, byte value, long writeCycle)
    {
        switch (register)
        {
            case 0x2000:
                ppuControl = value;
                PpuWrites.Add(new NesPpuWrite(register, value, null, writeCycle, RenderingEnabled));
                break;
            case 0x2001:
                var renderingEnabledBeforeWrite = RenderingEnabled;
                ppuMask = value;
                PpuWrites.Add(new NesPpuWrite(register, value, null, writeCycle, renderingEnabledBeforeWrite));
                break;
            case 0x2003:
                oamAddress = value;
                PpuWrites.Add(new NesPpuWrite(register, value, null, writeCycle, RenderingEnabled));
                break;
            case 0x2004:
                oam[oamAddress] = value;
                OamWrites.Add(new NesOamWrite((ushort)(NesRuntimeMemoryLayout.Sprite.OamShadow + oamAddress), value, writeCycle, RenderingEnabled));
                oamAddress++;
                PpuWrites.Add(new NesPpuWrite(register, value, null, writeCycle, RenderingEnabled));
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
                PpuWrites.Add(new NesPpuWrite(register, value, null, writeCycle, RenderingEnabled));
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
                PpuWrites.Add(new NesPpuWrite(register, value, ppuWriteToggle ? null : ppuAddress, writeCycle, RenderingEnabled));
                break;
            case 0x2007:
                ppuMemory[NormalizePpuAddress(ppuAddress)] = value;
                PpuWrites.Add(new NesPpuWrite(register, value, ppuAddress, writeCycle, RenderingEnabled));
                ppuAddress = (ushort)((ppuAddress + ((ppuControl & 0x04) != 0 ? 32 : 1)) & 0x3FFF);
                break;
            default:
                PpuWrites.Add(new NesPpuWrite(register, value, null, writeCycle, RenderingEnabled));
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
        var mapperBefore = new NesMapperState(CurrentR6Bank, CurrentR7Bank, selectedRegister);
        var registersBefore = new NesCpuRegisters(a, x, y);
        var accessesBefore = switchableWindowAccesses;
        Push((byte)(returnPc >> 8));
        Push((byte)returnPc);
        Push(PackStatus());
        pc = ReadWord(0xFFFA);
        var entry = pc;
        var lowest = pc;
        var highest = pc;
        nmiDepth++;
        var guard = 32;
        do
        {
            lowest = Math.Min(lowest, pc);
            highest = Math.Max(highest, pc);
            Step();
        }
        while (pc != returnPc && --guard > 0);
        nmiDepth--;
        if (pc != returnPc)
        {
            throw new InvalidOperationException("NES test NMI handler did not return.");
        }
        NmiObservations.Add(new NesNmiObservation(
            cycles,
            returnPc,
            entry,
            mapperBefore,
            new NesMapperState(CurrentR6Bank, CurrentR7Bank, selectedRegister),
            registersBefore,
            new NesCpuRegisters(a, x, y),
            lowest,
            highest,
            switchableWindowAccesses - accessesBefore));
        NmiCompletionCycles.Add(cycles);
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
            var baseAddress = pc;
            pc = (ushort)(baseAddress + delta);
            cycles++;
            if (CrossesPage(baseAddress, pc))
            {
                cycles++;
            }
        }
    }

    private static bool CrossesPage(ushort baseAddress, ushort address) =>
        (baseAddress & 0xFF00) != (address & 0xFF00);

    // $8000-$BFFF is the MMC3 switchable PRG window (R6 then R7). Counting every access an
    // interrupt handler makes through it is what turns "the handler is bank-neutral" into an
    // observation instead of an assumption: a non-fixed-resident handler fetches its own
    // instructions from here. Mapper 0 has no switchable window, so nothing there is counted.
    private void CountSwitchableWindowAccess(ushort address)
    {
        if (nmiDepth > 0 && mapper != 0 && address is >= 0x8000 and < 0xC000)
        {
            switchableWindowAccesses++;
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
