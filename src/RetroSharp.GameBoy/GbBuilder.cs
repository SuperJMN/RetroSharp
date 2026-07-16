using System.Globalization;
using System.Text;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

internal sealed class GbBuilder
{
    private const int BaseAddress = 0x0150;
    private const int BankContinuationStubLength = 5;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = [];
    private readonly Dictionary<string, ushort> externalLabels = [];
    private readonly List<AbsoluteFixup> absoluteFixups = [];
    private readonly List<LabelByteFixup> labelByteFixups = [];
    private readonly List<(int Offset, string Label)> bankFixups = [];
    private readonly List<(int Offset, string Label)> relativeFixups = [];
    private int nextLabelId;
    private int? fixedPayloadLimit;
    private int? bankSize;
    private bool emitBankContinuationStubs;

    public string CreateLabel(string prefix) => $"{prefix}_{nextLabelId++}";

    public void Label(string name) => labels[name] = bytes.Count;

    public void ExternalLabel(string name, ushort address) => externalLabels[name] = address;

    public void EnableBankedAddressing(int fixedPayloadLimit, int bankSize)
    {
        this.fixedPayloadLimit = fixedPayloadLimit;
        this.bankSize = bankSize;
        emitBankContinuationStubs = true;
    }

    public void DisableBankContinuationStubs()
    {
        emitBankContinuationStubs = false;
    }

    public int LabelOffset(string label)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown Game Boy ROM label '{label}'.");
        }

        return offset;
    }

    public bool TryLabelOffset(string label, out int offset)
    {
        return labels.TryGetValue(label, out offset);
    }

    public bool TryLabelAddress(string label, out ushort address)
    {
        if (!labels.ContainsKey(label) && !externalLabels.ContainsKey(label))
        {
            address = 0;
            return false;
        }

        address = checked((ushort)AddressOf(label));
        return true;
    }

    public void Emit(params byte[] values)
    {
        if (values.Length == 0)
        {
            return;
        }

        InsertBankContinuationStubsBefore(values.Length);
        bytes.AddRange(values);
    }

    public void EmitLabelLowByte(string label)
    {
        Emit(0x00);
        labelByteFixups.Add(new LabelByteFixup(bytes.Count - 1, label, HighByte: false));
    }

    public void EmitLabelHighByte(string label)
    {
        Emit(0x00);
        labelByteFixups.Add(new LabelByteFixup(bytes.Count - 1, label, HighByte: true));
    }

    private void InsertBankContinuationStubsBefore(int byteCount)
    {
        if (!emitBankContinuationStubs || fixedPayloadLimit is null || bankSize is null)
        {
            return;
        }

        if (byteCount > bankSize.Value - BankContinuationStubLength)
        {
            return;
        }

        while (ShouldInsertBankContinuationStub(byteCount))
        {
            EmitBankContinuationStub();
        }
    }

    private bool ShouldInsertBankContinuationStub(int byteCount)
    {
        if (fixedPayloadLimit is null || bankSize is null || bytes.Count < fixedPayloadLimit.Value)
        {
            return false;
        }

        var bankOffset = (bytes.Count - fixedPayloadLimit.Value) % bankSize.Value;
        var payloadLimit = bankSize.Value - BankContinuationStubLength;
        return bankOffset >= payloadLimit || bankOffset + byteCount > payloadLimit;
    }

    private void EmitBankContinuationStub()
    {
        if (fixedPayloadLimit is null || bankSize is null)
        {
            return;
        }

        var tailOffset = bytes.Count - fixedPayloadLimit.Value;
        var bankOffset = tailOffset % bankSize.Value;
        var payloadLimit = bankSize.Value - BankContinuationStubLength;
        while (bankOffset < payloadLimit)
        {
            bytes.Add(0x00); // NOP padding up to the fixed-size continuation stub.
            bankOffset++;
        }

        var currentBank = 1 + (tailOffset / bankSize.Value);
        var nextBank = checked((byte)(currentBank + 1));
        bytes.Add(0x3E); // LD A,n
        bytes.Add(nextBank);
        bytes.Add(0xC3); // JP program_bank_continue
        bytes.Add(0x00);
        bytes.Add(0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, GameBoyRomBuilder.ProgramBankContinuationLabel, IsControlFlow: true));
    }

    public void LoadAImmediate(int value)
    {
        Emit(0x3E, (byte)value);
    }

    public void LoadAImmediateBankOf(string label)
    {
        Emit(0x3E, 0x00);
        bankFixups.Add((bytes.Count - 1, label));
    }

    public void XorA()
    {
        Emit(0xAF);
    }

    public void LoadA(ushort address)
    {
        Emit(0xFA, (byte)(address & 0xFF), (byte)(address >> 8));
    }

    public void StoreA(ushort address)
    {
        Emit(0xEA, (byte)(address & 0xFF), (byte)(address >> 8));
    }

    public void StoreHighRamA(byte offset)
    {
        Emit(0xE0, offset);
    }

    public void StoreHighRamCFromA()
    {
        Emit(0xE2);
    }

    public void LoadHighRamA(byte offset)
    {
        Emit(0xF0, offset);
    }

    public void ComplementA()
    {
        Emit(0x2F);
    }

    public void AndImmediate(int value)
    {
        Emit(0xE6, (byte)value);
    }

    public void OrImmediate(int value)
    {
        Emit(0xF6, (byte)value);
    }

    public void XorImmediate(int value)
    {
        Emit(0xEE, (byte)value);
    }

    public void SwapA()
    {
        Emit(0xCB, 0x37);
    }

    public void ShiftRightLogicalA()
    {
        Emit(0xCB, 0x3F);
    }

    public void LoadBFromA()
    {
        Emit(0x47);
    }

    public void LoadDFromA()
    {
        Emit(0x57);
    }

    public void LoadCFromA()
    {
        Emit(0x4F);
    }

    public void LoadAFromB()
    {
        Emit(0x78);
    }

    public void PushAf()
    {
        Emit(0xF5);
    }

    public void PopAf()
    {
        Emit(0xF1);
    }

    public void PushHl()
    {
        Emit(0xE5);
    }

    public void PopHl()
    {
        Emit(0xE1);
    }

    public void LoadAFromC()
    {
        Emit(0x79);
    }

    public void LoadAFromD()
    {
        Emit(0x7A);
    }

    public void LoadAFromE()
    {
        Emit(0x7B);
    }

    public void LoadAFromH()
    {
        Emit(0x7C);
    }

    public void LoadAFromL()
    {
        Emit(0x7D);
    }

    public void AddAFromB()
    {
        Emit(0x80);
    }

    public void AdcAFromB()
    {
        Emit(0x88);
    }

    public void AddAFromC()
    {
        Emit(0x81);
    }

    public void AddAFromA()
    {
        Emit(0x87);
    }

    public void OrAFromB()
    {
        Emit(0xB0);
    }

    public void AndAFromB()
    {
        Emit(0xA0);
    }

    public void AndAFromC()
    {
        Emit(0xA1);
    }

    public void OrAFromC()
    {
        Emit(0xB1);
    }

    public void XorAFromB()
    {
        Emit(0xA8);
    }

    public void XorAFromC()
    {
        Emit(0xA9);
    }

    public void LoadLFromA()
    {
        Emit(0x6F);
    }

    public void LoadHFromA()
    {
        Emit(0x67);
    }

    public void LoadLFromE()
    {
        Emit(0x6B);
    }

    public void LoadHImmediate(int value)
    {
        Emit(0x26, (byte)value);
    }

    public void StoreHlA()
    {
        Emit(0x77);
    }

    public void AddAImmediate(int value)
    {
        Emit(0xC6, (byte)value);
    }

    public void AdcAImmediate(int value)
    {
        Emit(0xCE, (byte)value);
    }

    public void DecrementA()
    {
        Emit(0x3D);
    }

    public void SubtractAImmediate(int value)
    {
        Emit(0xD6, (byte)value);
    }

    public void SbcAImmediate(int value)
    {
        Emit(0xDE, (byte)value);
    }

    public void SubtractAFromC()
    {
        Emit(0x91);
    }

    public void SubtractB()
    {
        Emit(0x90);
    }

    public void SbcB()
    {
        Emit(0x98);
    }

    public void CompareImmediate(int value)
    {
        Emit(0xFE, (byte)value);
    }

    public void CompareB()
    {
        Emit(0xB8);
    }

    public void LoadHl(ushort value)
    {
        Emit(0x21, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void IncrementHl()
    {
        Emit(0x23);
    }

    public void LoadHl(string label)
    {
        Emit(0x21, 0x00, 0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, label, IsControlFlow: false));
    }

    public void LoadDImmediate(int value)
    {
        Emit(0x16, (byte)value);
    }

    public void LoadEFromA()
    {
        Emit(0x5F);
    }

    public void AddHlDe()
    {
        Emit(0x19);
    }

    public void AddHlBc()
    {
        Emit(0x09);
    }

    public void LoadAFromHl()
    {
        Emit(0x7E);
    }

    public void LoadDe(string label)
    {
        Emit(0x11, 0x00, 0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, label, IsControlFlow: false));
    }

    public void LoadDe(ushort value)
    {
        Emit(0x11, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void LoadBc(ushort value)
    {
        Emit(0x01, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void JumpRelative(byte opcode, string label)
    {
        Emit(opcode, 0x00);
        relativeFixups.Add((bytes.Count - 1, label));
    }

    public void JumpAbsolute(string label)
    {
        JumpAbsolute(0xC3, label);
    }

    public void JumpAbsolute(byte opcode, string label)
    {
        Emit(opcode, 0x00, 0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, label, IsControlFlow: true));
    }

    public byte[] Build()
    {
        foreach (var fixup in bankFixups)
        {
            bytes[fixup.Offset] = (byte)BankOf(fixup.Label);
        }

        foreach (var fixup in absoluteFixups)
        {
            if (fixup.IsControlFlow)
            {
                ValidateDirectControlFlow(fixup.Offset - 1, fixup.Label);
            }

            var address = AddressOf(fixup.Label);
            bytes[fixup.Offset] = (byte)(address & 0xFF);
            bytes[fixup.Offset + 1] = (byte)(address >> 8);
        }

        foreach (var fixup in labelByteFixups)
        {
            var address = AddressOf(fixup.Label);
            bytes[fixup.Offset] = fixup.HighByte
                ? (byte)(address >> 8)
                : (byte)(address & 0xFF);
        }

        foreach (var fixup in relativeFixups)
        {
            ValidateDirectControlFlow(fixup.Offset - 1, fixup.Label);
            var target = AddressOf(fixup.Label);
            var branchFrom = AddressOfOffset(fixup.Offset + 1);
            var delta = target - branchFrom;
            if (delta is < -128 or > 127)
            {
                throw new InvalidOperationException($"Relative jump to '{fixup.Label}' is out of range.");
            }

            bytes[fixup.Offset] = unchecked((byte)(sbyte)delta);
        }

        return bytes.ToArray();
    }

    private void ValidateDirectControlFlow(int sourceOffset, string targetLabel)
    {
        if (!labels.TryGetValue(targetLabel, out var targetOffset))
        {
            return;
        }

        var sourceBank = BankOfOffset(sourceOffset);
        var targetBank = BankOfOffset(targetOffset);
        if (sourceBank == targetBank || sourceBank == 0 || targetBank == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Direct Game Boy control flow from switchable program bank {sourceBank} to bank {targetBank} for label '{targetLabel}' is not supported; route it through a fixed-bank trampoline or helper.");
    }

    private int AddressOfOffset(int offset)
    {
        if (fixedPayloadLimit is null || bankSize is null || offset < fixedPayloadLimit.Value)
        {
            return BaseAddress + offset;
        }

        var bankedOffset = offset - fixedPayloadLimit.Value;
        return 0x4000 + (bankedOffset % bankSize.Value);
    }

    private int AddressOf(string label)
    {
        if (externalLabels.TryGetValue(label, out var externalAddress))
        {
            return externalAddress;
        }

        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown Game Boy ROM label '{label}'.");
        }

        return AddressOfOffset(offset);
    }

    private int BankOf(string label)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown Game Boy ROM label '{label}'.");
        }

        return BankOfOffset(offset);
    }

    private int BankOfOffset(int offset)
    {
        if (fixedPayloadLimit is null || bankSize is null || offset < fixedPayloadLimit.Value)
        {
            return 0;
        }

        return 1 + ((offset - fixedPayloadLimit.Value) / bankSize.Value);
    }

    private readonly record struct AbsoluteFixup(int Offset, string Label, bool IsControlFlow);

    private readonly record struct LabelByteFixup(int Offset, string Label, bool HighByte);
}
