namespace RetroSharp.GameBoy.Tests;

using System.Collections.Generic;

/// <summary>
/// Minimal Sharp SM83 (Game Boy CPU) interpreter used to execute the real ROMs the
/// Game Boy target emits and observe their runtime behavior. It deliberately models only
/// what the generated code needs: the opcode subset produced by <c>GbBuilder</c>, MBC1 ROM
/// bank switching, a driven <c>LY</c> register so <c>video.WaitVBlank()</c> busy-waits can
/// terminate, and capture of APU register writes. Any opcode the compiler did not emit throws,
/// so the harness can never silently mis-execute a program.
/// </summary>
internal sealed class GameBoyTestCpu
{
    private const int BankSize = 0x4000;
    private const byte FlagZ = 0x80;
    private const byte FlagN = 0x40;
    private const byte FlagH = 0x20;
    private const byte FlagC = 0x10;

    private readonly byte[] rom;
    private readonly byte[] vram = new byte[0x2000];
    private readonly byte[] extRam = new byte[0x2000];
    private readonly byte[] wram = new byte[0x2000];
    private readonly byte[] oam = new byte[0xA0];
    private readonly byte[] io = new byte[0x80];
    private readonly byte[] hram = new byte[0x7F];

    private readonly List<(ushort Register, byte Value)> apuWrites = [];
    private readonly List<(ushort Register, byte Value)> ioWrites = [];
    private readonly List<OamWrite> oamWrites = [];
    private readonly List<VramWrite> vramWrites = [];

    private byte a, b, c, d, e, h, l, f;
    private ushort sp, pc;
    private int romBank = 1;
    private long instructions;
    private long cycles;

    // Opt-in instrumentation for timing investigations. Defaults preserve existing behavior:
    // CycleAccurateLy off => LY driven by instruction count; empty Held => no buttons pressed.
    public bool CycleAccurateLy;
    public bool EnforceVblankVramWrites;
    public readonly HashSet<string> Held = [];
    public long AudioUpdateCalls;
    public long Cycles => cycles;

    public byte Vram(ushort address) => vram[address - 0x8000];

    public byte Wram(ushort address) => wram[address - 0xC000];

    public byte IoRegister(ushort address)
    {
        if (address is < 0xFF00 or >= 0xFF80)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Game Boy IO registers live in the 0xFF00-0xFF7F range.");
        }

        return io[address - 0xFF00];
    }

    public byte Oam(ushort address) => oam[address - 0xFE00];

    public IReadOnlyList<OamWrite> OamWrites => oamWrites;

    public IReadOnlyList<VramWrite> VramWrites => vramWrites;

    public GameBoyTestCpu(byte[] rom)
    {
        this.rom = rom;
        pc = 0x0100;
        sp = 0xFFFE;
    }

    public IReadOnlyList<(ushort Register, byte Value)> ApuWrites => apuWrites;

    public IReadOnlyList<byte> RunUntilIoRegisterWrites(ushort register, int count, long maxInstructions)
    {
        var values = new List<byte>(count);
        var processed = ioWrites.Count;
        var startInstructions = instructions;
        while (values.Count < count)
        {
            if (instructions - startInstructions >= maxInstructions)
            {
                throw new InvalidOperationException(
                    $"CPU executed {instructions - startInstructions} instructions but only captured {values.Count} of {count} writes to 0x{register:X4}.");
            }

            Step();
            for (; processed < ioWrites.Count; processed++)
            {
                if (ioWrites[processed].Register == register)
                {
                    values.Add(ioWrites[processed].Value);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Runs until the cycle clock reaches <paramref name="frames"/> DMG frames (70224 cycles each),
    /// or the instruction budget is exhausted. Requires <see cref="CycleAccurateLy"/> for meaningful
    /// frame timing.
    /// </summary>
    public void RunFrames(int frames, long maxInstructions = 2_000_000_000)
    {
        var target = (long)frames * 70224L;
        while (cycles < target)
        {
            if (instructions >= maxInstructions)
            {
                throw new InvalidOperationException(
                    $"CPU executed {instructions} instructions but only reached {cycles} of {target} cycles.");
            }

            Step();
        }
    }

    private byte ReadJoypad()
    {
        var selection = io[0] & 0x30;
        var low = 0x0F;
        if ((selection & 0x10) == 0) // bit4=0 selects the d-pad row
        {
            if (Held.Contains("right")) low &= ~0x01;
            if (Held.Contains("left")) low &= ~0x02;
            if (Held.Contains("up")) low &= ~0x04;
            if (Held.Contains("down")) low &= ~0x08;
        }

        if ((selection & 0x20) == 0) // bit5=0 selects the action-button row
        {
            if (Held.Contains("a")) low &= ~0x01;
            if (Held.Contains("b")) low &= ~0x02;
            if (Held.Contains("select")) low &= ~0x04;
            if (Held.Contains("start")) low &= ~0x08;
        }

        return (byte)(selection | 0xC0 | low); // 0=pressed; unused high bits read as 1
    }

    private static int CyclesFor(byte op) => op switch
    {
        0x01 or 0x11 or 0x21 or 0x31 => 12,             // LD rr,nn
        0x36 => 12,                                      // LD (HL),n
        0xE0 or 0xF0 => 12,                             // LDH (n),A / LDH A,(n)
        0xEA or 0xFA => 16,                             // LD (nn),A / LD A,(nn)
        0xC3 or 0xC2 or 0xCA or 0xD2 or 0xDA => 16,     // JP / JP cc (approx taken)
        0xCD => 24,                                      // CALL nn
        0xC9 => 16,                                      // RET
        0xF5 => 16,                                      // PUSH AF
        0xF1 => 12,                                      // POP AF
        0x18 or 0x20 or 0x28 or 0x30 or 0x38 => 12,     // JR / JR cc (approx taken)
        0x16 or 0x26 or 0x2E or 0x06 or 0x0E or 0x1E or 0x3E => 8, // LD r,n
        0xC6 or 0xD6 or 0xE6 or 0xF6 or 0xEE or 0xFE => 8,        // ALU A,n
        0x0B or 0x03 or 0x13 or 0x1B or 0x23 or 0x2B => 8,        // INC/DEC rr
        0x09 or 0x19 or 0x29 => 8,                       // ADD HL,rr
        0x1A or 0x0A or 0x22 or 0x2A or 0x32 or 0x77 or 0x7E => 8, // LD A,(rr)/(HL) loads/stores
        0x46 or 0x4E or 0x56 or 0x5E => 8,               // LD r,(HL)
        0xE2 or 0xF2 => 8,                               // LDH (C),A / LDH A,(C)
        0xCB => 8,                                       // CB-prefixed (SWAP/SRL on A)
        _ => 4,                                          // reg-reg LD, ALU r, INC/DEC r, NOP, DI, CPL
    };

    /// <summary>
    /// Runs until <paramref name="count"/> writes to the given APU <paramref name="register"/> have
    /// been captured (ignoring writes to other registers, e.g. those from <c>audio.Init()</c>) or the
    /// instruction budget is exhausted (which fails the test rather than hanging).
    /// </summary>
    public IReadOnlyList<byte> RunUntilRegisterWrites(ushort register, int count, long maxInstructions)
    {
        var values = new List<byte>(count);
        var processed = 0;
        while (values.Count < count)
        {
            if (instructions >= maxInstructions)
            {
                throw new InvalidOperationException(
                    $"CPU executed {instructions} instructions but only captured {values.Count} of {count} writes to 0x{register:X4}.");
            }

            Step();
            for (; processed < apuWrites.Count; processed++)
            {
                if (apuWrites[processed].Register == register)
                {
                    values.Add(apuWrites[processed].Value);
                }
            }
        }

        return values;
    }

    private ushort Hl => (ushort)((h << 8) | l);
    private ushort De => (ushort)((d << 8) | e);
    private ushort Bc => (ushort)((b << 8) | c);

    private void SetHl(ushort value)
    {
        h = (byte)(value >> 8);
        l = (byte)value;
    }

    private byte ReadByte(ushort addr)
    {
        switch (addr)
        {
            case < 0x4000:
                return addr < rom.Length ? rom[addr] : (byte)0;
            case < 0x8000:
            {
                var index = (romBank * BankSize) + (addr - 0x4000);
                return index < rom.Length ? rom[index] : (byte)0;
            }
            case < 0xA000:
                return vram[addr - 0x8000];
            case < 0xC000:
                return extRam[addr - 0xA000];
            case 0xC0FA:
                AudioUpdateCalls++;               // MusicActiveAddress: read once per audio.Update()
                return wram[0xC0FA - 0xC000];
            case < 0xE000:
                return wram[addr - 0xC000];
            case < 0xFE00:
                return wram[addr - 0xE000];
            case < 0xFEA0:
                return oam[addr - 0xFE00];
            case < 0xFF00:
                return 0;
            case 0xFF44:
                return CycleAccurateLy
                    ? (byte)((cycles / 456) % 154) // real DMG timing: 456 cycles/scanline, 154 lines
                    : (byte)(instructions % 154);  // instruction-driven LY so WaitVBlank loops terminate
            case 0xFF00:
                return ReadJoypad();
            case < 0xFF80:
                return io[addr - 0xFF00];
            case < 0xFFFF:
                return hram[addr - 0xFF80];
            default:
                return 0;
        }
    }

    private void WriteByte(ushort addr, byte value)
    {
        switch (addr)
        {
            case < 0x2000:
                return; // RAM enable
            case < 0x4000:
            {
                var bank = value & 0x1F;
                romBank = bank == 0 ? 1 : bank; // MBC1: bank 0 in the switchable window maps to 1
                return;
            }
            case < 0x8000:
                return; // RAM bank / banking mode (unused by the ROMs under test)
            case < 0xA000:
            {
                var ly = (byte)((cycles / 456) % 154);
                var lcdEnabled = (io[0x40] & 0x80) != 0;
                var applied = !EnforceVblankVramWrites || !lcdEnabled || ly is >= 144 and <= 153;
                vramWrites.Add(new VramWrite(
                    addr,
                    value,
                    cycles,
                    ly,
                    lcdEnabled,
                    applied));
                if (applied)
                {
                    vram[addr - 0x8000] = value;
                }

                return;
            }
            case < 0xC000:
                extRam[addr - 0xA000] = value;
                return;
            case < 0xE000:
                wram[addr - 0xC000] = value;
                return;
            case < 0xFE00:
                wram[addr - 0xE000] = value;
                return;
            case < 0xFEA0:
                oam[addr - 0xFE00] = value;
                oamWrites.Add(new OamWrite(
                    addr,
                    value,
                    cycles,
                    (byte)((cycles / 456) % 154),
                    (io[0x40] & 0x80) != 0));
                return;
            case < 0xFF00:
                return;
            case < 0xFF80:
                if (addr is >= 0xFF10 and <= 0xFF3F)
                {
                    apuWrites.Add((addr, value));
                }

                ioWrites.Add((addr, value));
                io[addr - 0xFF00] = value;
                return;
            case < 0xFFFF:
                hram[addr - 0xFF80] = value;
                return;
            default:
                return;
        }
    }

    private byte NextByte() => ReadByte(pc++);

    private ushort NextWord()
    {
        var low = NextByte();
        var high = NextByte();
        return (ushort)((high << 8) | low);
    }

    private void Step()
    {
        instructions++;
        var opcode = NextByte();
        cycles += CyclesFor(opcode);
        switch (opcode)
        {
            case 0x00: break;                                   // NOP
            case 0xF3: break;                                   // DI (no interrupts modeled)
            case 0x01: { var v = NextWord(); b = (byte)(v >> 8); c = (byte)v; break; } // LD BC,nn
            case 0x11: { var v = NextWord(); d = (byte)(v >> 8); e = (byte)v; break; } // LD DE,nn
            case 0x21: SetHl(NextWord()); break;                // LD HL,nn
            case 0x31: sp = NextWord(); break;                  // LD SP,nn
            case 0x16: d = NextByte(); break;                   // LD D,n
            case 0x26: h = NextByte(); break;                   // LD H,n
            case 0x2E: l = NextByte(); break;                   // LD L,n
            case 0x06: b = NextByte(); break;                   // LD B,n
            case 0x0E: c = NextByte(); break;                   // LD C,n
            case 0x1E: e = NextByte(); break;                   // LD E,n
            case 0x3E: a = NextByte(); break;                   // LD A,n
            case 0x36: WriteByte(Hl, NextByte()); break;        // LD (HL),n
            case 0x0B: { var v = (ushort)(Bc - 1); b = (byte)(v >> 8); c = (byte)v; break; } // DEC BC
            case 0x03: { var v = (ushort)(Bc + 1); b = (byte)(v >> 8); c = (byte)v; break; } // INC BC
            case 0x13: { var v = (ushort)(De + 1); d = (byte)(v >> 8); e = (byte)v; break; } // INC DE
            case 0x1B: { var v = (ushort)(De - 1); d = (byte)(v >> 8); e = (byte)v; break; } // DEC DE
            case 0x23: SetHl((ushort)(Hl + 1)); break;          // INC HL
            case 0x2B: SetHl((ushort)(Hl - 1)); break;          // DEC HL
            case 0x09: AddHl(Bc); break;                        // ADD HL,BC
            case 0x19: AddHl(De); break;                        // ADD HL,DE
            case 0x29: AddHl(Hl); break;                        // ADD HL,HL
            case 0x1A: a = ReadByte(De); break;                 // LD A,(DE)
            case 0x0A: a = ReadByte(Bc); break;                 // LD A,(BC)
            case 0x22: WriteByte(Hl, a); SetHl((ushort)(Hl + 1)); break; // LD (HL+),A
            case 0x2A: a = ReadByte(Hl); SetHl((ushort)(Hl + 1)); break; // LD A,(HL+)
            case 0x32: WriteByte(Hl, a); SetHl((ushort)(Hl - 1)); break; // LD (HL-),A
            case 0x77: WriteByte(Hl, a); break;                 // LD (HL),A
            case 0x7E: a = ReadByte(Hl); break;                 // LD A,(HL)
            case 0x46: b = ReadByte(Hl); break;                 // LD B,(HL)
            case 0x4E: c = ReadByte(Hl); break;                 // LD C,(HL)
            case 0x54: d = h; break;                            // LD D,H
            case 0x56: d = ReadByte(Hl); break;                 // LD D,(HL)
            case 0x5E: e = ReadByte(Hl); break;                 // LD E,(HL)
            case 0x5D: e = l; break;                            // LD E,L
            case 0x47: b = a; break;                            // LD B,A
            case 0x4F: c = a; break;                            // LD C,A
            case 0x57: d = a; break;                            // LD D,A
            case 0x5F: e = a; break;                            // LD E,A
            case 0x67: h = a; break;                            // LD H,A
            case 0x6F: l = a; break;                            // LD L,A
            case 0x60: h = b; break;                            // LD H,B
            case 0x69: l = c; break;                            // LD L,C
            case 0x6B: l = e; break;                            // LD L,E
            case 0x62: h = d; break;                            // LD H,D
            case 0x78: a = b; break;                            // LD A,B
            case 0x79: a = c; break;                            // LD A,C
            case 0x7A: a = d; break;                            // LD A,D
            case 0x7B: a = e; break;                            // LD A,E
            case 0x7C: a = h; break;                            // LD A,H
            case 0x7D: a = l; break;                            // LD A,L
            case 0x80: a = Add(a, b); break;                    // ADD A,B
            case 0x81: a = Add(a, c); break;                    // ADD A,C
            case 0x87: a = Add(a, a); break;                    // ADD A,A
            case 0xC6: a = Add(a, NextByte()); break;           // ADD A,n
            case 0x90: a = Sub(a, b); break;                    // SUB B
            case 0x91: a = Sub(a, c); break;                    // SUB C
            case 0xD6: a = Sub(a, NextByte()); break;           // SUB n
            case 0x05: b = Dec(b); break;                       // DEC B
            case 0x0D: c = Dec(c); break;                       // DEC C
            case 0x15: d = Dec(d); break;                       // DEC D
            case 0x1D: e = Dec(e); break;                       // DEC E
            case 0x3D: a = Dec(a); break;                       // DEC A
            case 0x04: b = Inc(b); break;                       // INC B
            case 0x0C: c = Inc(c); break;                       // INC C
            case 0x14: d = Inc(d); break;                       // INC D
            case 0x1C: e = Inc(e); break;                       // INC E
            case 0x24: h = Inc(h); break;                       // INC H
            case 0x3C: a = Inc(a); break;                       // INC A
            case 0xA0: a = And(b); break;                       // AND B
            case 0xA1: a = And(c); break;                       // AND C
            case 0xE6: a = And(NextByte()); break;              // AND n
            case 0xB0: a = Or(b); break;                        // OR B
            case 0xB1: a = Or(c); break;                        // OR C
            case 0xB3: a = Or(e); break;                        // OR E
            case 0xF6: a = Or(NextByte()); break;               // OR n
            case 0xA8: a = Xor(b); break;                       // XOR B
            case 0xA9: a = Xor(c); break;                       // XOR C
            case 0xAF: a = Xor(a); break;                       // XOR A
            case 0xEE: a = Xor(NextByte()); break;              // XOR n
            case 0xB8: Sub(a, b); break;                        // CP B
            case 0xB9: Sub(a, c); break;                        // CP C
            case 0xFE: Sub(a, NextByte()); break;               // CP n
            case 0x2F: a = (byte)~a; f |= FlagN | FlagH; break; // CPL
            case 0xE0: WriteByte((ushort)(0xFF00 + NextByte()), a); break; // LDH (n),A
            case 0xF0: a = ReadByte((ushort)(0xFF00 + NextByte())); break; // LDH A,(n)
            case 0xE2: WriteByte((ushort)(0xFF00 + c), a); break;          // LDH (C),A
            case 0xF2: a = ReadByte((ushort)(0xFF00 + c)); break;          // LDH A,(C)
            case 0xEA: WriteByte(NextWord(), a); break;         // LD (nn),A
            case 0xFA: a = ReadByte(NextWord()); break;         // LD A,(nn)
            case 0xC3: pc = NextWord(); break;                  // JP nn
            case 0xC2: { var t = NextWord(); if ((f & FlagZ) == 0) pc = t; break; } // JP NZ,nn
            case 0xCA: { var t = NextWord(); if ((f & FlagZ) != 0) pc = t; break; } // JP Z,nn
            case 0xD2: { var t = NextWord(); if ((f & FlagC) == 0) pc = t; break; } // JP NC,nn
            case 0xDA: { var t = NextWord(); if ((f & FlagC) != 0) pc = t; break; } // JP C,nn
            case 0xCD: { var t = NextWord(); PushWord(pc); pc = t; break; } // CALL nn
            case 0xC9: pc = PopWord(); break;                 // RET
            case 0xF5: PushWord((ushort)((a << 8) | f)); break; // PUSH AF
            case 0xF1: { var v = PopWord(); a = (byte)(v >> 8); f = (byte)(v & 0xF0); break; } // POP AF
            case 0x18: { var off = (sbyte)NextByte(); pc = (ushort)(pc + off); break; } // JR e
            case 0x20: { var off = (sbyte)NextByte(); if ((f & FlagZ) == 0) pc = (ushort)(pc + off); break; } // JR NZ,e
            case 0x28: { var off = (sbyte)NextByte(); if ((f & FlagZ) != 0) pc = (ushort)(pc + off); break; } // JR Z,e
            case 0x30: { var off = (sbyte)NextByte(); if ((f & FlagC) == 0) pc = (ushort)(pc + off); break; } // JR NC,e
            case 0x38: { var off = (sbyte)NextByte(); if ((f & FlagC) != 0) pc = (ushort)(pc + off); break; } // JR C,e
            case 0xCB: StepCb(NextByte()); break;
            default:
                throw new NotSupportedException($"Unsupported SM83 opcode 0x{opcode:X2} at 0x{(ushort)(pc - 1):X4}.");
        }
    }

    private void StepCb(byte opcode)
    {
        switch (opcode)
        {
            case 0x37: // SWAP A
                a = (byte)((a << 4) | (a >> 4));
                f = a == 0 ? FlagZ : (byte)0;
                break;
            case 0x3F: // SRL A
            {
                var carry = (byte)(a & 0x01);
                a >>= 1;
                f = 0;
                if (a == 0) f |= FlagZ;
                if (carry != 0) f |= FlagC;
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported SM83 CB opcode 0x{opcode:X2}.");
        }
    }

    private void PushWord(ushort value)
    {
        WriteByte(--sp, (byte)(value >> 8));
        WriteByte(--sp, (byte)value);
    }

    private ushort PopWord()
    {
        var low = ReadByte(sp++);
        var high = ReadByte(sp++);
        return (ushort)((high << 8) | low);
    }

    private byte Add(byte x, byte y)
    {
        var result = x + y;
        f = 0;
        if ((byte)result == 0) f |= FlagZ;
        if (((x & 0x0F) + (y & 0x0F)) > 0x0F) f |= FlagH;
        if (result > 0xFF) f |= FlagC;
        return (byte)result;
    }

    private byte Sub(byte x, byte y)
    {
        var result = x - y;
        f = FlagN;
        if ((byte)result == 0) f |= FlagZ;
        if ((x & 0x0F) < (y & 0x0F)) f |= FlagH;
        if (x < y) f |= FlagC;
        return (byte)result;
    }

    private byte Inc(byte x)
    {
        var result = (byte)(x + 1);
        f = (byte)(f & FlagC);
        if (result == 0) f |= FlagZ;
        if ((x & 0x0F) == 0x0F) f |= FlagH;
        return result;
    }

    private byte Dec(byte x)
    {
        var result = (byte)(x - 1);
        f = (byte)((f & FlagC) | FlagN);
        if (result == 0) f |= FlagZ;
        if ((x & 0x0F) == 0) f |= FlagH;
        return result;
    }

    private byte And(byte y)
    {
        a &= y;
        f = FlagH;
        if (a == 0) f |= FlagZ;
        return a;
    }

    private byte Or(byte y)
    {
        a |= y;
        f = a == 0 ? FlagZ : (byte)0;
        return a;
    }

    private byte Xor(byte y)
    {
        a ^= y;
        f = a == 0 ? FlagZ : (byte)0;
        return a;
    }

    private void AddHl(ushort value)
    {
        var result = Hl + value;
        f = (byte)(f & FlagZ);
        if (((Hl & 0x0FFF) + (value & 0x0FFF)) > 0x0FFF) f |= FlagH;
        if (result > 0xFFFF) f |= FlagC;
        SetHl((ushort)result);
    }
}

internal readonly record struct OamWrite(ushort Address, byte Value, long Cycles, byte Ly, bool LcdEnabled);

internal readonly record struct VramWrite(ushort Address, byte Value, long Cycles, byte Ly, bool LcdEnabled, bool Applied);
