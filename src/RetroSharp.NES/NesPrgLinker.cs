namespace RetroSharp.NES;

internal sealed record NesPrgLinkLayout(
    int FixedPhysicalOffset,
    ushort FixedTrailerStartAddress,
    ushort FixedPayloadEndAddress,
    IReadOnlyList<NesPrgSectionLayout> ProgramBanks,
    string SelectR6HelperLabel);

internal sealed record NesPrgSymbol(
    NesPrgResidence Residence,
    int? PhysicalBank,
    ushort CpuAddress,
    int PhysicalOffset);

internal sealed record NesLinkedProgramSegment(
    int PhysicalBank,
    int PhysicalOffset,
    ushort CpuAddress,
    byte[] Bytes);

internal sealed record NesPrgLinkResult(
    byte[] FixedBytes,
    IReadOnlyList<NesLinkedProgramSegment> ProgramSegments,
    IReadOnlyDictionary<string, NesPrgSymbol> Symbols,
    IReadOnlyList<NesPrgPlacementUnit> PlacementUnits,
    int FixedVeneerBytes,
    int ProgramBytes,
    int RequiredProgramBanks);

internal sealed class NesProgramBankCapacityException(
    int programBytes,
    int requiredBanks,
    int availableBanks)
    : InvalidOperationException(
        $"NES banked program requires {requiredBanks} R6 bank(s) for {programBytes} linked bytes, but only {availableBanks} whole bank(s) are available.")
{
    internal int ProgramBytes { get; } = programBytes;

    internal int RequiredBanks { get; } = requiredBanks;

    internal int AvailableBanks { get; } = availableBanks;
}

internal sealed class NesFixedVeneerCapacityException(int veneerBytes, int availableBytes)
    : InvalidOperationException(
        $"NES MMC3/TVROM fixed veneer overflow: {veneerBytes} bytes required, {availableBytes} bytes available before the reset trailer.");

internal static class NesPrgLinker
{
    private const ushort ProgramCpuBaseAddress = 0x8000;
    private const int ProgramBankSize = 8 * 1_024;
    private const int FallthroughJumpSize = 3;
    private const int VeneerSize = 12;

    private sealed class MutableAtom(
        NesPrgPlacementUnitEmission unit,
        NesPrgAtom source,
        NesPrgRelocation? branchRelocation)
    {
        internal NesPrgPlacementUnitEmission Unit { get; } = unit;

        internal NesPrgAtom Source { get; } = source;

        internal NesPrgRelocation? BranchRelocation { get; } = branchRelocation;

        internal bool Expanded { get; set; }
    }

    private sealed record MutablePlacementUnit(
        NesPrgPlacementUnitEmission Source,
        IReadOnlyList<MutableAtom> Atoms);

    private sealed record PlacedAtom(
        MutableAtom Atom,
        int BankIndex,
        int Offset,
        int Length);

    private sealed record ProgramPlacement(
        IReadOnlyList<PlacedAtom> Atoms,
        IReadOnlyList<int> UsedBytesByBank,
        int RequiredBanks);

    private sealed record ResolvedSymbol(
        NesPrgResidence? Residence,
        int? BankIndex,
        int? PhysicalBank,
        ushort CpuAddress,
        int PhysicalOffset);

    private readonly record struct VeneerTarget(int PhysicalBank, ushort CpuAddress);

    internal static NesPrgLinkResult Link(PrgBuilder builder, NesPrgLinkLayout layout)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(layout);

        var emission = builder.FreezeForLink();
        var unsupportedFixedUnit = emission.PlacementUnits.FirstOrDefault(
            unit => unit.Residence is NesPrgResidence.Fixed);
        if (unsupportedFixedUnit is not null)
        {
            throw new InvalidOperationException(
                $"NES banked linker does not support Fixed placement unit '{unsupportedFixedUnit.Name}' until fixed placement policy is implemented.");
        }

        var programUnits = emission.PlacementUnits
            .Where(unit => unit.Residence is NesPrgResidence.ProgramR6)
            .ToArray();
        foreach (var unit in programUnits)
        {
            ValidateProgramAtoms(unit);
        }

        var mutableUnits = programUnits
            .Select(unit =>
            {
                var branchRelocations = emission.Relocations
                    .Where(relocation =>
                        relocation.PlacementUnitName == unit.Name &&
                        relocation.Kind is NesPrgRelocationKind.RelativeBranch)
                    .ToDictionary(
                        relocation => FindContainingAtom(unit, relocation.Offset).Offset,
                        relocation => relocation);
                return new MutablePlacementUnit(
                    unit,
                    unit.Atoms
                        .Select(atom => new MutableAtom(
                            unit,
                            atom,
                            branchRelocations.GetValueOrDefault(atom.Offset)))
                        .ToArray());
            })
            .ToArray();

        ProgramPlacement placement;
        IReadOnlyDictionary<string, ResolvedSymbol> symbols;
        while (true)
        {
            placement = PlaceProgram(mutableUnits);
            symbols = ResolveSymbols(emission, placement, layout);
            var grew = false;
            foreach (var placed in placement.Atoms)
            {
                var atom = placed.Atom;
                if (atom.Expanded || atom.BranchRelocation is null)
                {
                    continue;
                }

                var target = ResolveTarget(symbols, atom.BranchRelocation);
                var branchFrom = ProgramCpuBaseAddress + placed.Offset + 2;
                var delta = target.CpuAddress - branchFrom;
                if (target.Residence is NesPrgResidence.ProgramR6 &&
                    target.BankIndex == placed.BankIndex &&
                    delta is >= -128 and <= 127)
                {
                    continue;
                }

                atom.Expanded = true;
                grew = true;
            }

            if (!grew)
            {
                break;
            }
        }

        var linkedProgramBytes = placement.UsedBytesByBank.Sum();
        if (placement.RequiredBanks > layout.ProgramBanks.Count)
        {
            throw new NesProgramBankCapacityException(
                linkedProgramBytes,
                placement.RequiredBanks,
                layout.ProgramBanks.Count);
        }

        var veneerTargets = CollectVeneerTargets(emission, placement, symbols, layout);
        var fixedPayloadOffset = layout.FixedPayloadEndAddress - emission.FixedBaseAddress;
        var fixedTrailerOffset = layout.FixedTrailerStartAddress - emission.FixedBaseAddress;
        var availableVeneerBytes = fixedTrailerOffset - fixedPayloadOffset;
        var requiredVeneerBytes = checked(veneerTargets.Count * VeneerSize);
        if (requiredVeneerBytes > availableVeneerBytes)
        {
            throw new NesFixedVeneerCapacityException(requiredVeneerBytes, availableVeneerBytes);
        }

        var helper = ResolveNamedSymbol(symbols, layout.SelectR6HelperLabel);
        if (helper.Residence is not NesPrgResidence.Fixed)
        {
            throw new InvalidOperationException(
                $"NES banked program selector '{layout.SelectR6HelperLabel}' must reside in fixed PRG.");
        }

        var orderedVeneers = veneerTargets
            .OrderBy(target => target.PhysicalBank)
            .ThenBy(target => target.CpuAddress)
            .ToArray();
        var veneerAddresses = orderedVeneers
            .Select((target, index) => new
            {
                Target = target,
                Address = checked((ushort)(layout.FixedPayloadEndAddress + index * VeneerSize)),
            })
            .ToDictionary(item => item.Target, item => item.Address);

        var fixedBytes = emission.FixedSection.Bytes.ToArray();
        for (var index = 0; index < orderedVeneers.Length; index++)
        {
            WriteVeneer(
                fixedBytes,
                fixedPayloadOffset + index * VeneerSize,
                orderedVeneers[index],
                helper.CpuAddress);
        }

        var programBuffers = placement.UsedBytesByBank
            .Select(length => new byte[length])
            .ToArray();
        for (var index = 0; index < placement.Atoms.Count; index++)
        {
            var placed = placement.Atoms[index];
            var destination = programBuffers[placed.BankIndex].AsSpan(placed.Offset, placed.Length);
            if (placed.Atom.Expanded)
            {
                var sourceOpcode = placed.Atom.Unit.Bytes[placed.Atom.Source.Offset];
                destination[0] = InvertBranch(sourceOpcode);
                destination[1] = 0x03;
                destination[2] = 0x4C;
            }
            else
            {
                placed.Atom.Unit.Bytes
                    .AsSpan(placed.Atom.Source.Offset, placed.Atom.Source.Length)
                    .CopyTo(destination);
            }
        }

        WriteFallthroughJumps(placement, layout, programBuffers, veneerAddresses);
        ApplyRelocations(
            emission,
            placement,
            symbols,
            veneerAddresses,
            fixedBytes,
            programBuffers);

        var programSegments = programBuffers
            .Select((bytes, index) => new NesLinkedProgramSegment(
                layout.ProgramBanks[index].PhysicalBank,
                layout.ProgramBanks[index].PhysicalOffset,
                ProgramCpuBaseAddress,
                bytes))
            .ToArray();
        var publicSymbols = emission.Labels
            .Where(pair => pair.Value.ExternalAddress is null)
            .ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var symbol = ResolveNamedSymbol(symbols, pair.Key);
                    return new NesPrgSymbol(
                        symbol.Residence ?? NesPrgResidence.Fixed,
                        symbol.PhysicalBank,
                        symbol.CpuAddress,
                        symbol.PhysicalOffset);
                },
                StringComparer.Ordinal);
        return new NesPrgLinkResult(
            fixedBytes,
            programSegments,
            publicSymbols,
            DescribePlacementUnits(emission, placement),
            requiredVeneerBytes,
            programBuffers.Sum(buffer => buffer.Length),
            placement.RequiredBanks);
    }

    private static void ValidateProgramAtoms(NesPrgPlacementUnitEmission unit)
    {
        var expectedOffset = 0;
        foreach (var atom in unit.Atoms)
        {
            if (atom.Offset != expectedOffset || atom.Length <= 0)
            {
                throw new InvalidOperationException(
                    $"NES banked program unit '{unit.Name}' has non-contiguous atoms at source offset {expectedOffset}.");
            }

            expectedOffset = checked(expectedOffset + atom.Length);
        }

        if (expectedOffset != unit.Bytes.Length)
        {
            throw new InvalidOperationException(
                $"NES banked program unit '{unit.Name}' has atoms covering {expectedOffset} bytes, but its emitted stream contains {unit.Bytes.Length} bytes.");
        }
    }

    private static ProgramPlacement PlaceProgram(IReadOnlyList<MutablePlacementUnit> units)
    {
        var atomCount = units.Sum(unit => unit.Atoms.Count);
        if (atomCount == 0)
        {
            return new ProgramPlacement([], [], RequiredBanks: 0);
        }

        var placed = new List<PlacedAtom>(atomCount);
        var used = new List<int>();
        var bankIndex = 0;
        var offset = 0;
        var atomIndex = 0;
        foreach (var unit in units)
        {
            foreach (var atom in unit.Atoms)
            {
                var length = atom.Expanded ? 5 : atom.Source.Length;
                var isFinalAtom = atomIndex == atomCount - 1;
                var bankCapacity = isFinalAtom
                    ? ProgramBankSize
                    : ProgramBankSize - FallthroughJumpSize;
                if (length > bankCapacity)
                {
                    throw new InvalidOperationException(
                        $"NES banked program atom in unit '{unit.Source.Name}' at source offset {atom.Source.Offset} is {length} bytes; an indivisible atom may use at most {bankCapacity} bytes in this position.");
                }

                if (offset + length > bankCapacity)
                {
                    used.Add(offset + FallthroughJumpSize);
                    bankIndex++;
                    offset = 0;
                }

                placed.Add(new PlacedAtom(atom, bankIndex, offset, length));
                offset += length;
                atomIndex++;
            }
        }

        used.Add(offset);
        return new ProgramPlacement(placed, used, bankIndex + 1);
    }

    private static IReadOnlyList<NesPrgPlacementUnit> DescribePlacementUnits(
        NesPrgEmission emission,
        ProgramPlacement placement)
    {
        var linkedSizes = emission.PlacementUnits.ToDictionary(
            unit => unit.Name,
            _ => 0,
            StringComparer.Ordinal);
        foreach (var placed in placement.Atoms)
        {
            linkedSizes[placed.Atom.Unit.Name] = checked(
                linkedSizes[placed.Atom.Unit.Name] + placed.Length);
        }

        for (var bankIndex = 0; bankIndex + 1 < placement.RequiredBanks; bankIndex++)
        {
            var owner = placement.Atoms.Last(atom => atom.BankIndex == bankIndex).Atom.Unit.Name;
            linkedSizes[owner] = checked(linkedSizes[owner] + FallthroughJumpSize);
        }

        return emission.PlacementUnits
            .Select(unit => new NesPrgPlacementUnit(
                unit.Name,
                unit.Residence,
                linkedSizes[unit.Name]))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, ResolvedSymbol> ResolveSymbols(
        NesPrgEmission emission,
        ProgramPlacement placement,
        NesPrgLinkLayout layout)
    {
        var programStarts = placement.Atoms.ToDictionary(
            item => (item.Atom.Unit.Name, item.Atom.Source.Offset));
        var unitsByName = emission.PlacementUnits.ToDictionary(
            unit => unit.Name,
            StringComparer.Ordinal);
        var resolved = new Dictionary<string, ResolvedSymbol>(StringComparer.Ordinal);
        foreach (var pair in emission.Labels)
        {
            var definition = pair.Value;
            if (definition.ExternalAddress is { } externalAddress)
            {
                resolved[pair.Key] = new ResolvedSymbol(
                    Residence: null,
                    BankIndex: null,
                    PhysicalBank: null,
                    externalAddress,
                    PhysicalOffset: -1);
                continue;
            }

            if (definition.PlacementUnitName is null)
            {
                resolved[pair.Key] = new ResolvedSymbol(
                    NesPrgResidence.Fixed,
                    BankIndex: null,
                    PhysicalBank: null,
                    checked((ushort)(emission.FixedBaseAddress + definition.Offset)),
                    checked(layout.FixedPhysicalOffset + definition.Offset));
                continue;
            }

            PlacedAtom location;
            var unitName = definition.PlacementUnitName;
            if (programStarts.TryGetValue((unitName, definition.Offset), out var atStart))
            {
                location = atStart;
            }
            else if (definition.Offset == unitsByName[unitName].Bytes.Length)
            {
                location = ResolveUnitEnd(emission, placement, unitName);
            }
            else
            {
                throw new InvalidOperationException(
                    $"NES banked program label '{pair.Key}' in unit '{unitName}' at source offset {definition.Offset} does not lie on an atom boundary.");
            }

            var bank = location.BankIndex < layout.ProgramBanks.Count
                ? layout.ProgramBanks[location.BankIndex]
                : null;
            resolved[pair.Key] = new ResolvedSymbol(
                NesPrgResidence.ProgramR6,
                location.BankIndex,
                bank?.PhysicalBank,
                checked((ushort)(ProgramCpuBaseAddress + location.Offset)),
                bank is null ? -1 : checked(bank.PhysicalOffset + location.Offset));
        }

        return resolved;
    }

    private static PlacedAtom ResolveUnitEnd(
        NesPrgEmission emission,
        ProgramPlacement placement,
        string unitName)
    {
        var unitOrder = emission.PlacementUnits
            .Select((unit, index) => new { unit.Name, Index = index })
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.Ordinal);
        var unitIndex = unitOrder[unitName];
        var next = placement.Atoms.FirstOrDefault(
            atom => unitOrder[atom.Atom.Unit.Name] > unitIndex);
        if (next is not null)
        {
            return next;
        }

        var previous = placement.Atoms.LastOrDefault(
            atom => unitOrder[atom.Atom.Unit.Name] <= unitIndex);
        if (previous is not null)
        {
            return previous with { Offset = previous.Offset + previous.Length };
        }

        throw new InvalidOperationException(
            $"NES banked program unit '{unitName}' has no emitted location for its end label.");
    }

    private static HashSet<VeneerTarget> CollectVeneerTargets(
        NesPrgEmission emission,
        ProgramPlacement placement,
        IReadOnlyDictionary<string, ResolvedSymbol> symbols,
        NesPrgLinkLayout layout)
    {
        var targets = new HashSet<VeneerTarget>();
        for (var bankIndex = 0; bankIndex + 1 < placement.RequiredBanks; bankIndex++)
        {
            var targetAtom = placement.Atoms.First(atom => atom.BankIndex == bankIndex + 1);
            targets.Add(new VeneerTarget(
                layout.ProgramBanks[targetAtom.BankIndex].PhysicalBank,
                checked((ushort)(ProgramCpuBaseAddress + targetAtom.Offset))));
        }

        foreach (var relocation in emission.Relocations)
        {
            var target = ResolveTarget(symbols, relocation);
            if (target.Residence is not NesPrgResidence.ProgramR6)
            {
                continue;
            }

            if (relocation.Kind is NesPrgRelocationKind.AbsoluteAddress or
                NesPrgRelocationKind.LowByte or
                NesPrgRelocationKind.HighByte)
            {
                throw new InvalidOperationException(
                    $"NES banked program label '{relocation.Label}' cannot be used as an address-only relocation in v1.");
            }

            var sourceBank = relocation.PlacementUnitName is not null
                ? FindPlacedAtom(placement, relocation).BankIndex
                : (int?)null;
            if (relocation.Kind is NesPrgRelocationKind.AbsoluteCall && sourceBank != target.BankIndex)
            {
                throw new InvalidOperationException(
                    $"NES banked program does not support cross-bank JSR to '{relocation.Label}' in v1.");
            }

            var needsVeneer = relocation.PlacementUnitName is null ||
                               sourceBank != target.BankIndex;
            if (needsVeneer &&
                (relocation.Kind is NesPrgRelocationKind.AbsoluteJump or NesPrgRelocationKind.RelativeBranch))
            {
                targets.Add(new VeneerTarget(target.PhysicalBank!.Value, target.CpuAddress));
            }
        }

        return targets;
    }

    private static void WriteFallthroughJumps(
        ProgramPlacement placement,
        NesPrgLinkLayout layout,
        IReadOnlyList<byte[]> programBuffers,
        IReadOnlyDictionary<VeneerTarget, ushort> veneerAddresses)
    {
        for (var bankIndex = 0; bankIndex + 1 < placement.RequiredBanks; bankIndex++)
        {
            var next = placement.Atoms.First(atom => atom.BankIndex == bankIndex + 1);
            var nextBank = layout.ProgramBanks[next.BankIndex];
            var target = new VeneerTarget(nextBank.PhysicalBank, checked((ushort)(ProgramCpuBaseAddress + next.Offset)));
            WriteAbsoluteJump(programBuffers[bankIndex], programBuffers[bankIndex].Length - FallthroughJumpSize, veneerAddresses[target]);
        }
    }

    private static void ApplyRelocations(
        NesPrgEmission emission,
        ProgramPlacement placement,
        IReadOnlyDictionary<string, ResolvedSymbol> symbols,
        IReadOnlyDictionary<VeneerTarget, ushort> veneerAddresses,
        byte[] fixedBytes,
        IReadOnlyList<byte[]> programBuffers)
    {
        foreach (var relocation in emission.Relocations)
        {
            var target = ResolveTarget(symbols, relocation);
            if (relocation.PlacementUnitName is null)
            {
                ApplyFixedRelocation(relocation, target, veneerAddresses, fixedBytes, emission.FixedBaseAddress);
                continue;
            }

            var placed = FindPlacedAtom(placement, relocation);
            var destination = programBuffers[placed.BankIndex];
            if (relocation.Kind is NesPrgRelocationKind.RelativeBranch)
            {
                if (placed.Atom.Expanded)
                {
                    var jumpTarget = ResolveControlTarget(placed.BankIndex, target, veneerAddresses);
                    WriteAddress(destination, placed.Offset + 3, jumpTarget);
                }
                else
                {
                    var delta = target.CpuAddress - (ProgramCpuBaseAddress + placed.Offset + 2);
                    if (target.Residence is not NesPrgResidence.ProgramR6 ||
                        target.BankIndex != placed.BankIndex ||
                        delta is < -128 or > 127)
                    {
                        throw new InvalidOperationException(
                            $"NES banked branch to '{relocation.Label}' was not relaxed before relocation.");
                    }

                    destination[placed.Offset + 1] = unchecked((byte)(sbyte)delta);
                }

                continue;
            }

            var operandOffset = placed.Offset + relocation.Offset - placed.Atom.Source.Offset;
            switch (relocation.Kind)
            {
                case NesPrgRelocationKind.AbsoluteJump:
                    WriteAddress(
                        destination,
                        operandOffset,
                        ResolveControlTarget(placed.BankIndex, target, veneerAddresses));
                    break;
                case NesPrgRelocationKind.AbsoluteCall:
                    if (target.Residence is NesPrgResidence.ProgramR6 && target.BankIndex != placed.BankIndex)
                    {
                        throw new InvalidOperationException(
                            $"NES banked program does not support cross-bank JSR to '{relocation.Label}' in v1.");
                    }

                    WriteAddress(destination, operandOffset, checked((ushort)(target.CpuAddress + relocation.Addend)));
                    break;
                case NesPrgRelocationKind.AbsoluteAddress:
                    RejectProgramAddressTarget(relocation, target);
                    WriteAddress(destination, operandOffset, checked((ushort)(target.CpuAddress + relocation.Addend)));
                    break;
                case NesPrgRelocationKind.LowByte:
                case NesPrgRelocationKind.HighByte:
                    RejectProgramAddressTarget(relocation, target);
                    WriteByteAddress(destination, operandOffset, target.CpuAddress, relocation);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported NES PRG relocation '{relocation.Kind}'.");
            }
        }
    }

    private static void ApplyFixedRelocation(
        NesPrgRelocation relocation,
        ResolvedSymbol target,
        IReadOnlyDictionary<VeneerTarget, ushort> veneerAddresses,
        byte[] fixedBytes,
        ushort fixedBaseAddress)
    {
        switch (relocation.Kind)
        {
            case NesPrgRelocationKind.AbsoluteJump:
                WriteAddress(
                    fixedBytes,
                    relocation.Offset,
                    target.Residence is NesPrgResidence.ProgramR6
                        ? veneerAddresses[new VeneerTarget(target.PhysicalBank!.Value, target.CpuAddress)]
                        : checked((ushort)(target.CpuAddress + relocation.Addend)));
                break;
            case NesPrgRelocationKind.AbsoluteCall:
                if (target.Residence is NesPrgResidence.ProgramR6)
                {
                    throw new InvalidOperationException(
                        $"NES fixed PRG cannot JSR directly into banked program label '{relocation.Label}' in v1.");
                }

                WriteAddress(fixedBytes, relocation.Offset, checked((ushort)(target.CpuAddress + relocation.Addend)));
                break;
            case NesPrgRelocationKind.AbsoluteAddress:
                RejectProgramAddressTarget(relocation, target);
                WriteAddress(fixedBytes, relocation.Offset, checked((ushort)(target.CpuAddress + relocation.Addend)));
                break;
            case NesPrgRelocationKind.LowByte:
            case NesPrgRelocationKind.HighByte:
                RejectProgramAddressTarget(relocation, target);
                WriteByteAddress(fixedBytes, relocation.Offset, target.CpuAddress, relocation);
                break;
            case NesPrgRelocationKind.RelativeBranch:
                if (target.Residence is NesPrgResidence.ProgramR6)
                {
                    throw new InvalidOperationException(
                        $"NES fixed PRG cannot branch directly into banked program label '{relocation.Label}'.");
                }

                var delta = target.CpuAddress - (fixedBaseAddress + relocation.Offset + 1);
                if (delta is < -128 or > 127)
                {
                    throw new BranchOutOfRangeException(relocation.Label, delta);
                }

                fixedBytes[relocation.Offset] = unchecked((byte)(sbyte)delta);
                break;
            default:
                throw new InvalidOperationException($"Unsupported NES PRG relocation '{relocation.Kind}'.");
        }
    }

    private static ushort ResolveControlTarget(
        int sourceBankIndex,
        ResolvedSymbol target,
        IReadOnlyDictionary<VeneerTarget, ushort> veneerAddresses)
    {
        if (target.Residence is NesPrgResidence.ProgramR6 && target.BankIndex != sourceBankIndex)
        {
            return veneerAddresses[new VeneerTarget(target.PhysicalBank!.Value, target.CpuAddress)];
        }

        return target.CpuAddress;
    }

    private static void RejectProgramAddressTarget(NesPrgRelocation relocation, ResolvedSymbol target)
    {
        if (target.Residence is NesPrgResidence.ProgramR6)
        {
            throw new InvalidOperationException(
                $"NES banked program label '{relocation.Label}' cannot be used as an address-only relocation in v1.");
        }
    }

    private static ResolvedSymbol ResolveTarget(
        IReadOnlyDictionary<string, ResolvedSymbol> symbols,
        NesPrgRelocation relocation)
    {
        var target = ResolveNamedSymbol(symbols, relocation.Label);
        if (target.Residence is NesPrgResidence.ProgramR6 && relocation.Addend != 0)
        {
            throw new InvalidOperationException(
                $"NES banked program relocation to '{relocation.Label}' cannot use addend {relocation.Addend} in v1.");
        }

        return target;
    }

    private static ResolvedSymbol ResolveNamedSymbol(
        IReadOnlyDictionary<string, ResolvedSymbol> symbols,
        string label) =>
        symbols.TryGetValue(label, out var symbol)
            ? symbol
            : throw new InvalidOperationException($"Unknown NES PRG label '{label}'.");

    private static PlacedAtom FindPlacedAtom(
        ProgramPlacement placement,
        NesPrgRelocation relocation) =>
        placement.Atoms.FirstOrDefault(atom =>
            atom.Atom.Unit.Name == relocation.PlacementUnitName &&
            relocation.Offset >= atom.Atom.Source.Offset &&
            relocation.Offset < atom.Atom.Source.Offset + atom.Atom.Source.Length)
        ?? throw new InvalidOperationException(
            $"NES banked relocation in unit '{relocation.PlacementUnitName}' at source offset {relocation.Offset} is not contained by an emitted atom.");

    private static NesPrgAtom FindContainingAtom(NesPrgPlacementUnitEmission unit, int sourceOffset) =>
        unit.Atoms.FirstOrDefault(atom => sourceOffset >= atom.Offset && sourceOffset < atom.Offset + atom.Length)
        ?? throw new InvalidOperationException(
            $"NES banked relocation in unit '{unit.Name}' at source offset {sourceOffset} is not contained by an emitted atom.");

    private static byte InvertBranch(byte opcode) => opcode switch
    {
        0x10 => 0x30,
        0x30 => 0x10,
        0x50 => 0x70,
        0x70 => 0x50,
        0x90 => 0xB0,
        0xB0 => 0x90,
        0xD0 => 0xF0,
        0xF0 => 0xD0,
        _ => throw new InvalidOperationException($"Unsupported 6502 branch opcode ${opcode:X2} in banked program."),
    };

    private static void WriteVeneer(
        byte[] fixedBytes,
        int offset,
        VeneerTarget target,
        ushort helperAddress)
    {
        fixedBytes[offset + 0] = 0x08; // PHP
        fixedBytes[offset + 1] = 0x48; // PHA
        fixedBytes[offset + 2] = 0xA9; // LDA #bank
        fixedBytes[offset + 3] = checked((byte)target.PhysicalBank);
        fixedBytes[offset + 4] = 0x20; // JSR fixed mmc3_select_r6
        WriteAddress(fixedBytes, offset + 5, helperAddress);
        fixedBytes[offset + 7] = 0x68; // PLA
        fixedBytes[offset + 8] = 0x28; // PLP
        fixedBytes[offset + 9] = 0x4C; // JMP target
        WriteAddress(fixedBytes, offset + 10, target.CpuAddress);
    }

    private static void WriteAbsoluteJump(byte[] bytes, int offset, ushort target)
    {
        bytes[offset] = 0x4C;
        WriteAddress(bytes, offset + 1, target);
    }

    private static void WriteAddress(byte[] bytes, int offset, ushort address)
    {
        bytes[offset] = (byte)(address & 0xFF);
        bytes[offset + 1] = (byte)(address >> 8);
    }

    private static void WriteByteAddress(
        byte[] bytes,
        int offset,
        ushort address,
        NesPrgRelocation relocation)
    {
        var resolved = checked(address + relocation.Addend);
        bytes[offset] = relocation.Kind is NesPrgRelocationKind.HighByte
            ? (byte)(resolved >> 8)
            : (byte)(resolved & 0xFF);
    }
}
