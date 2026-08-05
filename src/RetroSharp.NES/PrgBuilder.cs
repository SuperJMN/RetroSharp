namespace RetroSharp.NES;

internal enum NesPrgResidence
{
    Fixed,
    ProgramR6,
}

internal enum NesPrgRelocationKind
{
    AbsoluteAddress,
    AbsoluteJump,
    AbsoluteCall,
    RelativeBranch,
    LowByte,
    HighByte,
}

internal sealed record NesPrgAtom(int Offset, int Length);

internal sealed record NesPrgLabelDefinition(
    string? PlacementUnitName,
    int Offset,
    ushort? ExternalAddress);

internal sealed record NesPrgRelocation(
    string? PlacementUnitName,
    int Offset,
    string Label,
    int Addend,
    NesPrgRelocationKind Kind);

internal sealed record NesPrgSectionEmission(
    byte[] Bytes,
    IReadOnlyList<NesPrgAtom> Atoms);

internal sealed record NesPrgPlacementUnitEmission(
    string Name,
    NesPrgResidence Residence,
    byte[] Bytes,
    IReadOnlyList<NesPrgAtom> Atoms);

internal sealed record NesPrgPlacementUnit(
    string Name,
    NesPrgResidence Residence,
    int Size);

internal sealed record NesPrgEmission(
    ushort FixedBaseAddress,
    NesPrgSectionEmission FixedSection,
    IReadOnlyList<NesPrgPlacementUnitEmission> PlacementUnits,
    IReadOnlyDictionary<string, NesPrgLabelDefinition> Labels,
    IReadOnlyList<NesPrgRelocation> Relocations);

internal sealed class PrgBuilder
{
    private sealed class RecordedSection
    {
        public List<byte> Bytes { get; } = [];

        public List<NesPrgAtom> Atoms { get; } = [];
    }

    private sealed class RecordedPlacementUnit(
        string name,
        NesPrgResidence residence,
        int flatStartOffset)
    {
        public string Name { get; } = name;

        public NesPrgResidence Residence { get; } = residence;

        public int FlatStartOffset { get; } = flatStartOffset;

        public int? FlatSize { get; set; }

        public RecordedSection Section { get; } = new();
    }

    private sealed class PlacementUnitScope(PrgBuilder owner, RecordedPlacementUnit unit) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            owner.ExitPlacementUnit(unit);
            disposed = true;
        }
    }

    private readonly ushort baseAddress;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = [];
    private readonly List<(int Offset, string Label, int Addend)> absoluteFixups = [];
    private readonly List<(int Offset, string Label, int Addend, bool High)> byteFixups = [];
    private readonly List<(int Offset, string Label)> relativeFixups = [];
    private readonly bool sectioned;
    private readonly RecordedSection recordedFixedSection = new();
    private readonly List<RecordedPlacementUnit> recordedPlacementUnits = [];
    private readonly Dictionary<string, RecordedPlacementUnit> placementUnitsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NesPrgLabelDefinition> recordedLabels = [];
    private readonly List<NesPrgRelocation> recordedRelocations = [];
    private readonly Dictionary<string, int> subroutineCallSites = new(StringComparer.Ordinal);
    private RecordedPlacementUnit? currentPlacementUnit;
    private int nextLabelId;

    public PrgBuilder(ushort baseAddress = 0x8000) : this(baseAddress, sectioned: false)
    {
    }

    private PrgBuilder(ushort baseAddress, bool sectioned)
    {
        this.baseAddress = baseAddress;
        this.sectioned = sectioned;
    }

    internal static PrgBuilder CreateSectioned(ushort fixedBaseAddress) =>
        new(fixedBaseAddress, sectioned: true);

    public int CurrentAddress => sectioned
        ? (CurrentResidence is NesPrgResidence.ProgramR6 ? 0x8000 : baseAddress) + CurrentResidenceOffset
        : baseAddress + bytes.Count;

    internal IReadOnlyList<NesPrgPlacementUnit> PlacementUnits => recordedPlacementUnits
        .Select(unit => new NesPrgPlacementUnit(
            unit.Name,
            unit.Residence,
            sectioned
                ? unit.Section.Bytes.Count
                : unit.FlatSize ?? bytes.Count - unit.FlatStartOffset))
        .ToArray();

    public void Label(string name)
    {
        if (!sectioned)
        {
            labels[name] = bytes.Count;
            return;
        }

        recordedLabels[name] = new NesPrgLabelDefinition(
            CurrentPlacementUnitName,
            CurrentBytes.Count,
            ExternalAddress: null);
    }

    public void DefineExternalLabel(string name, ushort address)
    {
        if (!sectioned)
        {
            labels[name] = address - baseAddress;
            return;
        }

        recordedLabels[name] = new NesPrgLabelDefinition(
            PlacementUnitName: null,
            Offset: 0,
            ExternalAddress: address);
    }

    public string CreateLabel(string prefix) => $"{prefix}_{nextLabelId++}";

    public void Emit(params byte[] values)
    {
        if (!sectioned)
        {
            bytes.AddRange(values);
            return;
        }

        EmitRecordedAtom(values);
    }

    internal IDisposable EnterPlacementUnit(string name, NesPrgResidence residence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sectioned && residence is NesPrgResidence.Fixed)
        {
            throw new InvalidOperationException(
                $"NES sectioned PRG does not support Fixed placement unit '{name}' until fixed placement policy is implemented.");
        }

        if (currentPlacementUnit is not null)
        {
            throw new InvalidOperationException(
                $"NES PRG placement unit '{currentPlacementUnit.Name}' cannot contain another placement unit.");
        }

        if (placementUnitsByName.ContainsKey(name))
        {
            throw new InvalidOperationException($"NES PRG placement unit '{name}' has already been emitted.");
        }

        var unit = new RecordedPlacementUnit(name, residence, bytes.Count);
        recordedPlacementUnits.Add(unit);
        placementUnitsByName.Add(name, unit);
        currentPlacementUnit = unit;
        return new PlacementUnitScope(this, unit);
    }

    internal NesPrgEmission FreezeForLink()
    {
        if (!sectioned)
        {
            throw new InvalidOperationException("A flat NES PRG builder cannot be passed to the sectioned linker.");
        }

        return new NesPrgEmission(
            baseAddress,
            new NesPrgSectionEmission(recordedFixedSection.Bytes.ToArray(), recordedFixedSection.Atoms.ToArray()),
            recordedPlacementUnits
                .Select(unit => new NesPrgPlacementUnitEmission(
                    unit.Name,
                    unit.Residence,
                    unit.Section.Bytes.ToArray(),
                    unit.Section.Atoms.ToArray()))
                .ToArray(),
            new Dictionary<string, NesPrgLabelDefinition>(recordedLabels, StringComparer.Ordinal),
            recordedRelocations.ToArray());
    }

    public void PadToAddress(ushort address)
    {
        if (address < baseAddress)
        {
            throw new InvalidOperationException($"NES PRG address ${address:X4} is below PRG ROM base ${baseAddress:X4}.");
        }

        if (sectioned && currentPlacementUnit is not null)
        {
            throw new InvalidOperationException("NES PRG address padding is valid only in the fixed section.");
        }

        var targetOffset = address - baseAddress;
        if (targetOffset < CurrentBytes.Count)
        {
            throw new InvalidOperationException($"NES PRG address ${address:X4} has already been emitted.");
        }

        if (sectioned)
        {
            var padding = targetOffset - CurrentBytes.Count;
            if (padding > 0)
            {
                EmitRecordedAtom(new byte[padding]);
            }

            return;
        }

        while (bytes.Count < targetOffset)
        {
            bytes.Add(0);
        }
    }

    public void EmitLabelLowByte(string label, int addend = 0)
    {
        Emit(0x00);
        AddByteRelocation(label, addend, high: false);
    }

    public void EmitLabelHighByte(string label, int addend = 0)
    {
        Emit(0x00);
        AddByteRelocation(label, addend, high: true);
    }

    public void LoadAImmediate(int value) => Emit(0xA9, CheckedByte(value));

    public void LoadAImmediateLabelLowByte(string label, int addend = 0)
    {
        if (sectioned)
        {
            var offset = EmitRecordedAtom(0xA9, 0x00);
            recordedRelocations.Add(new NesPrgRelocation(
                CurrentPlacementUnitName,
                offset + 1,
                label,
                addend,
                NesPrgRelocationKind.LowByte));
            return;
        }

        Emit(0xA9);
        EmitLabelLowByte(label, addend);
    }

    public void LoadAImmediateLabelHighByte(string label, int addend = 0)
    {
        if (sectioned)
        {
            var offset = EmitRecordedAtom(0xA9, 0x00);
            recordedRelocations.Add(new NesPrgRelocation(
                CurrentPlacementUnitName,
                offset + 1,
                label,
                addend,
                NesPrgRelocationKind.HighByte));
            return;
        }

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

    public void PushA() => Emit(0x48);

    public void PullA() => Emit(0x68);

    public void DecrementZeroPage(byte address) => Emit(0xC6, address);

    public void DecrementAbsolute(ushort address) => Emit(0xCE, Low(address), High(address));

    public void IncrementZeroPage(byte address) => Emit(0xE6, address);

    public void IncrementAbsolute(ushort address) => Emit(0xEE, Low(address), High(address));

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
        AddAbsoluteRelocation(label, addend, NesPrgRelocationKind.AbsoluteAddress);
    }

    public void LoadAIndirectY(byte address) => Emit(0xB1, address);

    public void JumpAbsolute(string label)
    {
        Emit(0x4C, 0x00, 0x00);
        AddAbsoluteRelocation(label, 0, NesPrgRelocationKind.AbsoluteJump);
    }

    // Call sites are counted for every residence so that the build report can describe
    // shared SDK bodies identically on mapper 0 (single fixed section) and on MMC3, where
    // the sectioned relocation table would otherwise be the only witness.
    public IReadOnlyDictionary<string, int> SubroutineCallSites => subroutineCallSites;

    public void CallSubroutine(string label)
    {
        Emit(0x20, 0x00, 0x00);
        AddAbsoluteRelocation(label, 0, NesPrgRelocationKind.AbsoluteCall);
        subroutineCallSites[label] = subroutineCallSites.GetValueOrDefault(label) + 1;
    }

    public void BranchRelative(byte opcode, string label)
    {
        Emit(opcode, 0x00);
        if (sectioned)
        {
            recordedRelocations.Add(new NesPrgRelocation(
                CurrentPlacementUnitName,
                CurrentBytes.Count - 1,
                label,
                Addend: 0,
                NesPrgRelocationKind.RelativeBranch));
        }
        else
        {
            relativeFixups.Add((bytes.Count - 1, label));
        }
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
        if (sectioned)
        {
            var offset = EmitRecordedAtom((byte)inverse, 0x03, 0x4C, 0x00, 0x00);
            recordedRelocations.Add(new NesPrgRelocation(
                CurrentPlacementUnitName,
                offset + 3,
                label,
                Addend: 0,
                NesPrgRelocationKind.AbsoluteJump));
            return;
        }

        Emit((byte)inverse, 0x03); // Skip the following absolute JMP when the condition is false.
        JumpAbsolute(label);
    }

    public byte[] Build()
    {
        if (sectioned)
        {
            throw new InvalidOperationException("A sectioned NES PRG builder must be linked with NesPrgLinker.");
        }

        foreach (var fixup in byteFixups)
        {
            var address = AddressOf(fixup.Label, fixup.Addend);
            bytes[fixup.Offset] = (byte)(fixup.High ? address >> 8 : address & 0xFF);
        }

        foreach (var fixup in absoluteFixups)
        {
            var address = AddressOf(fixup.Label, fixup.Addend);
            bytes[fixup.Offset] = (byte)(address & 0xFF);
            bytes[fixup.Offset + 1] = (byte)(address >> 8);
        }

        foreach (var fixup in relativeFixups)
        {
            var target = AddressOf(fixup.Label);
            var branchFrom = baseAddress + fixup.Offset + 1;
            var delta = target - branchFrom;
            if (delta is < -128 or > 127)
            {
                throw new BranchOutOfRangeException(fixup.Label, delta);
            }

            bytes[fixup.Offset] = unchecked((byte)(sbyte)delta);
        }

        return bytes.ToArray();
    }

    public ushort AddressOfLabel(string label)
    {
        if (!sectioned)
        {
            return checked((ushort)AddressOf(label));
        }

        if (!recordedLabels.TryGetValue(label, out var definition))
        {
            throw new InvalidOperationException($"Unknown NES PRG label '{label}'.");
        }

        if (definition.ExternalAddress is { } externalAddress)
        {
            return externalAddress;
        }

        if (definition.PlacementUnitName is not null)
        {
            throw new InvalidOperationException(
                $"NES banked program label '{label}' requires a physical bank as well as a CPU address.");
        }

        return checked((ushort)(baseAddress + definition.Offset));
    }

    private List<byte> CurrentBytes => sectioned
        ? CurrentRecordedSection.Bytes
        : bytes;

    private RecordedSection CurrentRecordedSection => currentPlacementUnit?.Section ?? recordedFixedSection;

    private string? CurrentPlacementUnitName => currentPlacementUnit?.Name;

    private NesPrgResidence CurrentResidence => currentPlacementUnit?.Residence ?? NesPrgResidence.Fixed;

    private int CurrentResidenceOffset
    {
        get
        {
            if (currentPlacementUnit is null)
            {
                return recordedFixedSection.Bytes.Count;
            }

            // Sectioned PRG rejects Fixed placement units in EnterPlacementUnit, so a unit always
            // starts at the beginning of its residence stream. Fixed units would additionally need
            // the offset of the fixed bytes emitted before them, which the linker does not preserve.
            if (currentPlacementUnit.Residence is NesPrgResidence.Fixed)
            {
                throw new InvalidOperationException(
                    $"NES sectioned PRG cannot address Fixed placement unit '{currentPlacementUnit.Name}' until fixed placement policy is implemented.");
            }

            var offset = 0;
            foreach (var unit in recordedPlacementUnits)
            {
                if (ReferenceEquals(unit, currentPlacementUnit))
                {
                    break;
                }

                if (unit.Residence == currentPlacementUnit.Residence)
                {
                    offset = checked(offset + unit.Section.Bytes.Count);
                }
            }

            return checked(offset + currentPlacementUnit.Section.Bytes.Count);
        }
    }

    private int EmitRecordedAtom(params byte[] values)
    {
        var section = CurrentRecordedSection;
        var offset = section.Bytes.Count;
        section.Bytes.AddRange(values);
        if (values.Length > 0)
        {
            section.Atoms.Add(new NesPrgAtom(offset, values.Length));
        }

        return offset;
    }

    private void AddAbsoluteRelocation(string label, int addend, NesPrgRelocationKind kind)
    {
        if (sectioned)
        {
            recordedRelocations.Add(new NesPrgRelocation(
                CurrentPlacementUnitName,
                CurrentBytes.Count - 2,
                label,
                addend,
                kind));
        }
        else
        {
            absoluteFixups.Add((bytes.Count - 2, label, addend));
        }
    }

    private void AddByteRelocation(string label, int addend, bool high)
    {
        if (sectioned)
        {
            recordedRelocations.Add(new NesPrgRelocation(
                CurrentPlacementUnitName,
                CurrentBytes.Count - 1,
                label,
                addend,
                high ? NesPrgRelocationKind.HighByte : NesPrgRelocationKind.LowByte));
        }
        else
        {
            byteFixups.Add((bytes.Count - 1, label, addend, High: high));
        }
    }

    private void ExitPlacementUnit(RecordedPlacementUnit unit)
    {
        if (!ReferenceEquals(currentPlacementUnit, unit))
        {
            throw new InvalidOperationException($"NES PRG placement unit '{unit.Name}' is not active.");
        }

        if (!sectioned)
        {
            unit.FlatSize = checked(bytes.Count - unit.FlatStartOffset);
        }

        currentPlacementUnit = null;
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

    private int AddressOf(string label, int addend = 0)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown NES PRG label '{label}'.");
        }

        return baseAddress + offset + addend;
    }
}
