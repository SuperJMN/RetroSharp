namespace RetroSharp.NES;

internal sealed class PrgBuilder
{
    private readonly ushort baseAddress;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, LabelDefinition> labels = [];
    private readonly List<(int Offset, string Label, int Addend)> absoluteFixups = [];
    private readonly List<(int Offset, string Label, int Addend, bool High)> byteFixups = [];
    private readonly List<(int Offset, string Label)> relativeFixups = [];
    private readonly List<JumpIfFixup> jumpIfFixups = [];
    private int nextLabelId;

    public PrgBuilder(ushort baseAddress = 0x8000)
    {
        this.baseAddress = baseAddress;
    }

    public int CurrentAddress => baseAddress + CreateBranchLayout().MapOffset(bytes.Count);

    public void Label(string name) => labels[name] = new LabelDefinition(bytes.Count, IsExternal: false);

    public void DefineExternalLabel(string name, ushort address) =>
        labels[name] = new LabelDefinition(address - baseAddress, IsExternal: true);

    public string CreateLabel(string prefix) => $"{prefix}_{nextLabelId++}";

    public void Emit(params byte[] values) => bytes.AddRange(values);

    public void PadToAddress(ushort address)
    {
        if (address < baseAddress)
        {
            throw new InvalidOperationException($"NES PRG address ${address:X4} is below PRG ROM base ${baseAddress:X4}.");
        }

        var currentAddress = CurrentAddress;
        if (address < currentAddress)
        {
            throw new InvalidOperationException($"NES PRG address ${address:X4} has already been emitted.");
        }

        bytes.AddRange(new byte[address - currentAddress]);
    }

    public void EmitLabelLowByte(string label, int addend = 0)
    {
        Emit(0x00);
        byteFixups.Add((bytes.Count - 1, label, addend, High: false));
    }

    public void EmitLabelHighByte(string label, int addend = 0)
    {
        Emit(0x00);
        byteFixups.Add((bytes.Count - 1, label, addend, High: true));
    }

    public void LoadAImmediate(int value) => Emit(0xA9, CheckedByte(value));

    public void LoadAImmediateLabelLowByte(string label, int addend = 0)
    {
        Emit(0xA9);
        EmitLabelLowByte(label, addend);
    }

    public void LoadAImmediateLabelHighByte(string label, int addend = 0)
    {
        Emit(0xA9);
        EmitLabelHighByte(label, addend);
    }

    public void LoadXImmediate(int value) => Emit(0xA2, CheckedByte(value));

    public void LoadYImmediate(int value) => Emit(0xA0, CheckedByte(value));

    public void LoadXZeroPage(byte address) => Emit(0xA6, address);

    public void LoadXAbsolute(ushort address) => Emit(0xAE, Low(address), High(address));

    public void LoadYZeroPage(byte address) => Emit(0xA4, address);

    public void LoadAZeroPage(byte address) => Emit(0xA5, address);

    public void LoadAZeroPageX(byte address) => Emit(0xB5, address);

    public void StoreAZeroPage(byte address) => Emit(0x85, address);

    public void StoreXZeroPage(byte address) => Emit(0x86, address);

    public void StoreAZeroPageX(byte address) => Emit(0x95, address);

    public void StoreYZeroPage(byte address) => Emit(0x84, address);

    public void LoadAAbsolute(ushort address) => Emit(0xAD, Low(address), High(address));

    public void StoreAAbsolute(ushort address) => Emit(0x8D, Low(address), High(address));

    public void StoreAAbsoluteX(ushort address) => Emit(0x9D, Low(address), High(address));

    public void LoadAAbsoluteX(ushort address) => Emit(0xBD, Low(address), High(address));

    public void StoreAAbsoluteY(ushort address) => Emit(0x99, Low(address), High(address));

    public void StoreAIndirectY(byte address) => Emit(0x91, address);

    public void StoreYAbsolute(ushort address) => Emit(0x8C, Low(address), High(address));

    public void StoreXAbsolute(ushort address) => Emit(0x8E, Low(address), High(address));

    public void LoadYAbsolute(ushort address) => Emit(0xAC, Low(address), High(address));

    public void AndImmediate(int value) => Emit(0x29, CheckedByte(value));

    public void AndZeroPage(byte address) => Emit(0x25, address);

    public void OrImmediate(int value) => Emit(0x09, CheckedByte(value));

    public void OrZeroPage(byte address) => Emit(0x05, address);

    public void OrAbsolute(ushort address) => Emit(0x0D, Low(address), High(address));

    public void XorImmediate(int value) => Emit(0x49, CheckedByte(value));

    public void XorAbsolute(ushort address) => Emit(0x4D, Low(address), High(address));

    public void XorZeroPage(byte address) => Emit(0x45, address);

    public void CompareImmediate(int value) => Emit(0xC9, CheckedByte(value));

    public void CompareZeroPage(byte address) => Emit(0xC5, address);

    public void CompareAbsolute(ushort address) => Emit(0xCD, Low(address), High(address));

    public void ClearCarry() => Emit(0x18);

    public void SetCarry() => Emit(0x38);

    public void AddImmediate(int value) => Emit(0x69, CheckedByte(value));

    public void AddZeroPage(byte address) => Emit(0x65, address);

    public void AddZeroPageX(byte address) => Emit(0x75, address);

    public void AddAbsolute(ushort address) => Emit(0x6D, Low(address), High(address));

    public void SubtractImmediate(int value) => Emit(0xE9, CheckedByte(value));

    public void SubtractZeroPage(byte address) => Emit(0xE5, address);

    public void SubtractAbsolute(ushort address) => Emit(0xED, Low(address), High(address));

    public void PushA() => Emit(0x48);

    public void PullA() => Emit(0x68);

    public void DecrementZeroPage(byte address) => Emit(0xC6, address);

    public void DecrementAbsolute(ushort address) => Emit(0xCE, Low(address), High(address));

    public void IncrementZeroPage(byte address) => Emit(0xE6, address);

    public void IncrementAbsolute(ushort address) => Emit(0xEE, Low(address), High(address));

    public void IncrementAbsoluteX(ushort address) => Emit(0xFE, Low(address), High(address));

    public void IncrementX() => Emit(0xE8);

    public void DecrementX() => Emit(0xCA);

    public void IncrementY() => Emit(0xC8);

    public void TransferAToX() => Emit(0xAA);

    public void TransferYToA() => Emit(0x98);

    public void CompareXImmediate(int value) => Emit(0xE0, CheckedByte(value));

    public void Return() => Emit(0x60);

    public void ShiftLeftA() => Emit(0x0A);

    public void ShiftRightA() => Emit(0x4A);

    public void ShiftRightAbsolute(ushort address) => Emit(0x4E, Low(address), High(address));

    public void RotateRightAbsolute(ushort address) => Emit(0x6E, Low(address), High(address));

    public void LdaAbsoluteX(string label, int addend = 0)
    {
        Emit(0xBD, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, addend));
    }

    public void LoadAIndirectY(byte address) => Emit(0xB1, address);

    public void JumpAbsolute(string label)
    {
        Emit(0x4C, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, 0));
    }

    public void CallSubroutine(string label)
    {
        Emit(0x20, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, 0));
    }

    public void BranchRelative(byte opcode, string label)
    {
        Emit(opcode, 0x00);
        relativeFixups.Add((bytes.Count - 1, label));
    }

    public void JumpIf(byte branchOpcode, string label)
    {
        var inverse = branchOpcode switch
        {
            0x90 => 0xB0, // BCC -> BCS
            0xB0 => 0x90, // BCS -> BCC
            0xD0 => 0xF0, // BNE -> BEQ
            0xF0 => 0xD0, // BEQ -> BNE
            _ => throw new ArgumentOutOfRangeException(nameof(branchOpcode), branchOpcode, "Unsupported 6502 condition branch."),
        };
        var offset = bytes.Count;
        Emit((byte)inverse, 0x03, 0x4C, 0x00, 0x00); // Skip the absolute JMP when the condition is false.
        jumpIfFixups.Add(new JumpIfFixup(offset, branchOpcode, (byte)inverse, label));
    }

    public byte[] Build()
    {
        var layout = CreateBranchLayout();
        var output = new List<byte>(layout.MapOffset(bytes.Count));
        for (var offset = 0; offset < bytes.Count; offset++)
        {
            if (!layout.IsRemoved(offset))
            {
                output.Add(bytes[offset]);
            }
        }

        var result = output.ToArray();
        foreach (var fixup in byteFixups)
        {
            var address = AddressOf(fixup.Label, layout, fixup.Addend);
            result[layout.MapOffset(fixup.Offset)] = (byte)(fixup.High ? address >> 8 : address & 0xFF);
        }

        foreach (var fixup in absoluteFixups)
        {
            var address = AddressOf(fixup.Label, layout, fixup.Addend);
            var offset = layout.MapOffset(fixup.Offset);
            result[offset] = (byte)(address & 0xFF);
            result[offset + 1] = (byte)(address >> 8);
        }

        foreach (var fixup in relativeFixups)
        {
            var target = AddressOf(fixup.Label, layout);
            var offset = layout.MapOffset(fixup.Offset);
            var branchFrom = baseAddress + offset + 1;
            var delta = target - branchFrom;
            if (delta is < -128 or > 127)
            {
                throw new BranchOutOfRangeException(fixup.Label, delta);
            }

            result[offset] = unchecked((byte)(sbyte)delta);
        }

        for (var index = 0; index < jumpIfFixups.Count; index++)
        {
            var fixup = jumpIfFixups[index];
            var offset = layout.MapOffset(fixup.Offset);
            var target = AddressOf(fixup.Label, layout);
            if (layout.IsRelaxed(index))
            {
                var delta = target - (baseAddress + offset + 2);
                result[offset] = fixup.BranchOpcode;
                result[offset + 1] = unchecked((byte)(sbyte)delta);
                continue;
            }

            result[offset] = fixup.InverseOpcode;
            result[offset + 1] = 0x03;
            result[offset + 2] = 0x4C;
            result[offset + 3] = (byte)(target & 0xFF);
            result[offset + 4] = (byte)(target >> 8);
        }

        return result;
    }

    public ushort AddressOfLabel(string label)
    {
        var layout = CreateBranchLayout();
        return checked((ushort)AddressOf(label, layout));
    }

    private static byte CheckedByte(int value)
    {
        if (value is < -128 or > 255)
        {
            throw new InvalidOperationException($"NES byte immediate must be between -128 and 255, got {value}.");
        }

        return (byte)value;
    }

    private static byte Low(ushort value) => (byte)(value & 0xFF);

    private static byte High(ushort value) => (byte)(value >> 8);

    private BranchLayout CreateBranchLayout()
    {
        var relaxed = new bool[jumpIfFixups.Count];
        // Start with every conditional jump in its always-safe long form. Shortening an internal
        // jump can only move its source and target closer together, so the monotone fixed point is
        // deterministic even when one relaxation enables another in a later iteration.
        while (true)
        {
            List<int>? newlyRelaxed = null;
            for (var index = 0; index < jumpIfFixups.Count; index++)
            {
                if (relaxed[index])
                {
                    continue;
                }

                var fixup = jumpIfFixups[index];
                // A fixed external address does not move with this builder's layout, so keep the
                // conservative long form when earlier relaxations could move the branch source.
                if (!labels.TryGetValue(fixup.Label, out var targetLabel) || targetLabel.IsExternal)
                {
                    continue;
                }

                var target = baseAddress + MapOffset(targetLabel.Offset, relaxed, index);
                var branchFrom = baseAddress + MapOffset(fixup.Offset, relaxed, index) + 2;
                var delta = target - branchFrom;
                if (delta is >= -128 and <= 127)
                {
                    (newlyRelaxed ??= []).Add(index);
                }
            }

            if (newlyRelaxed is null)
            {
                return new BranchLayout(jumpIfFixups, relaxed);
            }

            foreach (var index in newlyRelaxed)
            {
                relaxed[index] = true;
            }
        }
    }

    private int AddressOf(string label, BranchLayout layout, int addend = 0)
    {
        if (!labels.TryGetValue(label, out var definition))
        {
            throw new InvalidOperationException($"Unknown NES PRG label '{label}'.");
        }

        var offset = definition.IsExternal ? definition.Offset : layout.MapOffset(definition.Offset);
        return baseAddress + offset + addend;
    }

    private int MapOffset(int offset, IReadOnlyList<bool> relaxed, int candidate)
    {
        var removed = 0;
        for (var index = 0; index < jumpIfFixups.Count; index++)
        {
            if (relaxed[index] || index == candidate)
            {
                removed += Math.Clamp(offset - (jumpIfFixups[index].Offset + 2), 0, 3);
            }
        }

        return offset - removed;
    }

    private readonly record struct LabelDefinition(int Offset, bool IsExternal);

    private readonly record struct JumpIfFixup(int Offset, byte BranchOpcode, byte InverseOpcode, string Label);

    private sealed class BranchLayout(IReadOnlyList<JumpIfFixup> fixups, IReadOnlyList<bool> relaxed)
    {
        public bool IsRelaxed(int index) => relaxed[index];

        public bool IsRemoved(int offset)
        {
            for (var index = 0; index < fixups.Count; index++)
            {
                if (relaxed[index] && offset >= fixups[index].Offset + 2 && offset < fixups[index].Offset + 5)
                {
                    return true;
                }
            }

            return false;
        }

        public int MapOffset(int offset)
        {
            var removed = 0;
            for (var index = 0; index < fixups.Count; index++)
            {
                if (relaxed[index])
                {
                    removed += Math.Clamp(offset - (fixups[index].Offset + 2), 0, 3);
                }
            }

            return offset - removed;
        }
    }
}
