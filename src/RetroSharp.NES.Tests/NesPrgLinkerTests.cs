namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class NesPrgLinkerTests
{
    private const ushort FixedBaseAddress = 0xC000;
    private const ushort FixedTrailerStartAddress = 0xFF80;
    private const string SelectR6HelperLabel = "mmc3_select_r6";
    private const string TestProgramUnitName = "program:test";
    private const int ProgramBankSize = 8 * 1_024;

    [Fact]
    public void Fixed_to_program_jump_uses_a_veneer_that_preserves_a_and_flags()
    {
        var builder = CreateBuilder();
        builder.JumpAbsolute("program_entry");
        using (builder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
        {
            builder.Label("program_entry");
            builder.Emit(0xEA);
        }

        var fixedPayloadEndAddress = checked((ushort)builder.CurrentAddress);
        var result = Link(builder, fixedPayloadEndAddress, ProgramBank(3));
        var veneerAddress = fixedPayloadEndAddress;
        var veneerOffset = veneerAddress - FixedBaseAddress;

        Assert.Equal(
            [
                0x08,                   // PHP
                0x48,                   // PHA
                0xA9, 0x03,             // LDA #physical-bank
                0x20, 0x00, 0xC0,       // JSR mmc3_select_r6
                0x68,                   // PLA
                0x28,                   // PLP
                0x4C, 0x00, 0x80,       // JMP program_entry
            ],
            result.FixedBytes.AsSpan(veneerOffset, 12).ToArray());
        Assert.Equal([0x4C, Low(veneerAddress), High(veneerAddress)], result.FixedBytes[1..4]);
        Assert.Equal(12, result.FixedVeneerBytes);
    }

    [Fact]
    public void Named_placement_units_keep_stream_order_and_report_residence_and_size()
    {
        var splitBuilder = CreateBuilder();
        using (splitBuilder.EnterPlacementUnit("program:entry", NesPrgResidence.ProgramR6))
        {
            splitBuilder.JumpAbsolute("program_loop");
        }

        using (splitBuilder.EnterPlacementUnit("program:loop", NesPrgResidence.ProgramR6))
        {
            splitBuilder.Label("program_loop");
            splitBuilder.Emit(0xEA);
        }

        var controlBuilder = CreateBuilder();
        using (controlBuilder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
        {
            controlBuilder.JumpAbsolute("program_loop");
            controlBuilder.Label("program_loop");
            controlBuilder.Emit(0xEA);
        }

        var split = Link(splitBuilder, checked((ushort)splitBuilder.CurrentAddress), ProgramBank(0));
        var control = Link(controlBuilder, checked((ushort)controlBuilder.CurrentAddress), ProgramBank(0));

        Assert.Equal(control.FixedBytes, split.FixedBytes);
        Assert.Equal(control.ProgramSegments.Single().Bytes, split.ProgramSegments.Single().Bytes);
        Assert.Equal(
            [
                new NesPrgPlacementUnit("program:entry", NesPrgResidence.ProgramR6, 3),
                new NesPrgPlacementUnit("program:loop", NesPrgResidence.ProgramR6, 1),
            ],
            split.PlacementUnits);
    }

    [Fact]
    public void Cross_bank_branch_and_fallthrough_share_one_veneer_without_splitting_atoms()
    {
        var builder = CreateBuilder();
        using (builder.EnterPlacementUnit("program:first", NesPrgResidence.ProgramR6))
        {
            builder.Label("branch");
            builder.BranchRelative(0xD0, "second_bank"); // BNE
            builder.Emit(Enumerable.Repeat((byte)0xEA, ProgramBankSize - 8).ToArray());
        }

        using (builder.EnterPlacementUnit("program:second", NesPrgResidence.ProgramR6))
        {
            builder.Label("second_bank");
            builder.Emit(0xA9, 0x7E, 0x85, 0x10);
        }

        var fixedPayloadEndAddress = checked((ushort)builder.CurrentAddress);
        var result = Link(builder, fixedPayloadEndAddress, ProgramBank(0), ProgramBank(3));
        var first = result.ProgramSegments[0];
        var second = result.ProgramSegments[1];
        var veneerAddress = fixedPayloadEndAddress;

        Assert.Equal(ProgramBankSize, first.Bytes.Length);
        Assert.Equal([0xF0, 0x03, 0x4C, Low(veneerAddress), High(veneerAddress)], first.Bytes[..5]);
        Assert.Equal([0x4C, Low(veneerAddress), High(veneerAddress)], first.Bytes[^3..]);
        Assert.Equal([0xA9, 0x7E, 0x85, 0x10], second.Bytes);
        Assert.Equal(0, result.Symbols["branch"].PhysicalBank);
        Assert.Equal(3, result.Symbols["second_bank"].PhysicalBank);
        Assert.Equal((ushort)0x8000, result.Symbols["second_bank"].CpuAddress);
        Assert.Equal(12, result.FixedVeneerBytes);
        Assert.Equal(
            [
                new NesPrgPlacementUnit("program:first", NesPrgResidence.ProgramR6, ProgramBankSize),
                new NesPrgPlacementUnit("program:second", NesPrgResidence.ProgramR6, 4),
            ],
            result.PlacementUnits);
        Assert.Equal(result.ProgramBytes, result.PlacementUnits.Sum(unit => unit.Size));
    }

    [Fact]
    public void Sectioned_builder_rejects_fixed_placement_units_until_their_placement_is_defined()
    {
        var builder = CreateBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.EnterPlacementUnit("fixed:future", NesPrgResidence.Fixed));

        Assert.Equal(
            "NES sectioned PRG does not support Fixed placement unit 'fixed:future' until fixed placement policy is implemented.",
            exception.Message);
    }

    [Fact]
    public void Unit_end_label_resolves_to_the_next_unit_after_a_bank_cut()
    {
        var builder = CreateBuilder();
        builder.JumpAbsolute("next_unit");
        using (builder.EnterPlacementUnit("program:first", NesPrgResidence.ProgramR6))
        {
            builder.Emit(Enumerable.Repeat((byte)0xEA, ProgramBankSize - 3).ToArray());
            builder.Label("next_unit");
        }

        using (builder.EnterPlacementUnit("program:second", NesPrgResidence.ProgramR6))
        {
            builder.Emit(0xEA, 0xEA, 0xEA, 0xEA);
        }

        var result = Link(
            builder,
            checked((ushort)builder.CurrentAddress),
            ProgramBank(0),
            ProgramBank(3));

        Assert.Equal(3, result.Symbols["next_unit"].PhysicalBank);
        Assert.Equal((ushort)0x8000, result.Symbols["next_unit"].CpuAddress);
    }

    [Fact]
    public void Local_short_branch_keeps_the_relative_encoding()
    {
        var builder = CreateBuilder();
        using (builder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
        {
            builder.BranchRelative(0xD0, "target");
            builder.Emit(0xEA);
            builder.Label("target");
            builder.Emit(0xEA);
        }

        var result = Link(builder, checked((ushort)builder.CurrentAddress), ProgramBank(0));

        Assert.Equal([0xD0, 0x01, 0xEA, 0xEA], result.ProgramSegments.Single().Bytes);
        Assert.Equal(0, result.FixedVeneerBytes);
    }

    [Fact]
    public void Long_forward_branch_relaxes_to_inverse_branch_plus_absolute_jump()
    {
        var builder = CreateBuilder();
        using (builder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
        {
            builder.BranchRelative(0xD0, "target");
            builder.Emit(Enumerable.Repeat((byte)0xEA, 130).ToArray());
            builder.Label("target");
            builder.Emit(0xEA);
        }

        var result = Link(builder, checked((ushort)builder.CurrentAddress), ProgramBank(0));
        var targetAddress = result.Symbols["target"].CpuAddress;

        Assert.Equal([0xF0, 0x03, 0x4C, Low(targetAddress), High(targetAddress)], result.ProgramSegments.Single().Bytes[..5]);
        Assert.Equal((ushort)0x8087, targetAddress);
        Assert.Equal(0, result.FixedVeneerBytes);
    }

    [Fact]
    public void Long_back_edge_relaxes_to_inverse_branch_plus_absolute_jump()
    {
        var builder = CreateBuilder();
        using (builder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
        {
            builder.Label("loop");
            builder.Emit(Enumerable.Repeat((byte)0xEA, 130).ToArray());
            builder.BranchRelative(0xD0, "loop");
        }

        var result = Link(builder, checked((ushort)builder.CurrentAddress), ProgramBank(0));

        Assert.Equal([0xF0, 0x03, 0x4C, 0x00, 0x80], result.ProgramSegments.Single().Bytes[^5..]);
        Assert.Equal(0, result.FixedVeneerBytes);
    }

    [Fact]
    public void Cross_bank_jsr_is_rejected_in_v1()
    {
        var builder = CreateBuilder();
        using (builder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
        {
            builder.CallSubroutine("callee");
            builder.Emit(Enumerable.Repeat((byte)0xEA, ProgramBankSize - 6).ToArray());
            builder.Label("callee");
            builder.Return();
            builder.Emit(0xEA);
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => Link(
                builder,
                checked((ushort)builder.CurrentAddress),
                ProgramBank(0),
                ProgramBank(3)));

        Assert.Equal("NES banked program does not support cross-bank JSR to 'callee' in v1.", exception.Message);
    }

    [Fact]
    public void Final_program_bank_can_use_the_complete_8_kib_window()
    {
        var builder = CreateBuilder();
        using (builder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
        {
            builder.Emit(Enumerable.Repeat((byte)0xEA, ProgramBankSize).ToArray());
        }

        var result = Link(
            builder,
            checked((ushort)builder.CurrentAddress),
            ProgramBank(0));

        var segment = Assert.Single(result.ProgramSegments);
        Assert.Equal(ProgramBankSize, segment.Bytes.Length);
        Assert.Equal(ProgramBankSize, result.ProgramBytes);
    }

    [Fact]
    public void Program_capacity_failure_reports_the_complete_deterministic_deficit()
    {
        static PrgBuilder OversizedProgram()
        {
            var builder = CreateBuilder();
            using (builder.EnterPlacementUnit(TestProgramUnitName, NesPrgResidence.ProgramR6))
            {
                builder.Emit(Enumerable.Repeat((byte)0xEA, ProgramBankSize - 3).ToArray());
                builder.Emit(Enumerable.Repeat((byte)0xEA, ProgramBankSize - 3).ToArray());
                builder.Emit(0xEA, 0xEA, 0xEA, 0xEA);
            }

            return builder;
        }

        var firstBuilder = OversizedProgram();
        var secondBuilder = OversizedProgram();
        var first = Assert.Throws<NesProgramBankCapacityException>(
            () => Link(firstBuilder, checked((ushort)firstBuilder.CurrentAddress)));
        var second = Assert.Throws<NesProgramBankCapacityException>(
            () => Link(secondBuilder, checked((ushort)secondBuilder.CurrentAddress)));

        Assert.Equal(3, first.RequiredBanks);
        Assert.Equal(0, first.AvailableBanks);
        Assert.Equal((2 * ProgramBankSize) + 4, first.ProgramBytes);
        Assert.Equal(first.ProgramBytes, second.ProgramBytes);
        Assert.Equal(first.RequiredBanks, second.RequiredBanks);
        Assert.Equal(first.AvailableBanks, second.AvailableBanks);
        Assert.Equal(first.Message, second.Message);
    }

    private static PrgBuilder CreateBuilder()
    {
        var builder = PrgBuilder.CreateSectioned(FixedBaseAddress);
        builder.Label(SelectR6HelperLabel);
        builder.Return();
        return builder;
    }

    private static NesPrgLinkResult Link(
        PrgBuilder builder,
        ushort fixedPayloadEndAddress,
        params NesPrgSectionLayout[] programBanks)
    {
        builder.PadToAddress(FixedTrailerStartAddress);
        return NesPrgLinker.Link(
            builder,
            new NesPrgLinkLayout(
                FixedPhysicalOffset: 6 * ProgramBankSize,
                FixedTrailerStartAddress,
                fixedPayloadEndAddress,
                programBanks,
                SelectR6HelperLabel));
    }

    private static NesPrgSectionLayout ProgramBank(int physicalBank) =>
        new(
            physicalBank,
            physicalBank * ProgramBankSize,
            ProgramBankSize,
            NesPrgSectionKind.WorldR6);

    private static byte Low(ushort value) => (byte)(value & 0xFF);

    private static byte High(ushort value) => (byte)(value >> 8);
}
