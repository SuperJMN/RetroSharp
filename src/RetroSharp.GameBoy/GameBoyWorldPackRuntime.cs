namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;

internal enum GameBoyWorldPackResult : byte
{
    Success = 0,
    Miss = 1,
    BoundsError = 2,
    Malformed = 3,
}

internal sealed record GameBoyWorldPackPlaneRuntimeDescriptor(
    uint Offset,
    byte StoredBytes,
    byte DecodedElements,
    byte IdBytes,
    ushort IdCount,
    WorldPackCodec Codec);

internal sealed record GameBoyWorldPackRuntimePlan(
    WorldPack Pack,
    byte[] SerializedBytes,
    GameBoyWorldPackRuntimeLayout Layout,
    byte[] HeaderBytes,
    byte[] CollisionProfileBytes,
    byte[] DirectoryBytes,
    byte[] RuntimeDirectoryBytes,
    IReadOnlyList<GameBoyWorldPackPlaneRuntimeDescriptor> Planes)
{
    public static GameBoyWorldPackRuntimePlan Create(byte[] serializedBytes)
    {
        ArgumentNullException.ThrowIfNull(serializedBytes);
        var pack = WorldPackSerializer.Deserialize(serializedBytes);
        var descriptor = pack.Descriptor;
        if (descriptor.TargetCellStride != 1)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack v1 requires targetCellStride 1; received {descriptor.TargetCellStride}.");
        }

        if (descriptor.PackLength > 32u * 16u * 1024u)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack length {descriptor.PackLength} exceeds the current 32-bank MBC1 address envelope.");
        }

        var layout = GameBoyWorldPackRuntimeLayout.Create(descriptor.VisualIdBytes, descriptor.CollisionIdBytes);
        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        var profileLength = checked(descriptor.CollisionProfileCount * metatileCells);
        var directoryLength = checked(pack.Chunks.Count * WorldPackDescriptor.V1DirectoryEntryBytes);
        var planes = new List<GameBoyWorldPackPlaneRuntimeDescriptor>(checked(pack.Chunks.Count * 2));
        var runtimeDirectory = new List<byte>(checked(pack.Chunks.Count * 14));
        foreach (var chunk in pack.Chunks)
        {
            var visual = CreatePlane(
                chunk.Directory.VisualOffset,
                chunk.Directory.VisualStoredBytes,
                chunk.Directory.VisualDecodedBytes,
                descriptor.VisualIdBytes,
                descriptor.VisualMetatileCount,
                chunk.Directory.VisualCodec);
            var collision = CreatePlane(
                chunk.Directory.CollisionOffset,
                chunk.Directory.CollisionStoredBytes,
                chunk.Directory.CollisionDecodedBytes,
                descriptor.CollisionIdBytes,
                descriptor.CollisionProfileCount,
                chunk.Directory.CollisionCodec);
            planes.Add(visual);
            planes.Add(collision);
            AddRuntimePlane(runtimeDirectory, visual);
            AddRuntimePlane(runtimeDirectory, collision);
            runtimeDirectory.Add(chunk.Directory.ValidWidth);
            runtimeDirectory.Add(chunk.Directory.ValidHeight);
        }

        return new GameBoyWorldPackRuntimePlan(
            pack,
            serializedBytes.ToArray(),
            layout,
            serializedBytes[..WorldPackDescriptor.V1HeaderBytes],
            serializedBytes.AsSpan(checked((int)descriptor.CollisionProfilesOffset), profileLength).ToArray(),
            serializedBytes.AsSpan(checked((int)descriptor.DirectoryOffset), directoryLength).ToArray(),
            runtimeDirectory.ToArray(),
            planes);
    }

    private static void AddRuntimePlane(List<byte> target, GameBoyWorldPackPlaneRuntimeDescriptor plane)
    {
        target.Add((byte)plane.Offset);
        target.Add((byte)(plane.Offset >> 8));
        target.Add((byte)(plane.Offset >> 16));
        target.Add(plane.StoredBytes);
        target.Add(plane.DecodedElements);
        target.Add((byte)plane.Codec);
    }

    private static GameBoyWorldPackPlaneRuntimeDescriptor CreatePlane(
        uint offset,
        ushort storedBytes,
        ushort decodedBytes,
        int idBytes,
        int idCount,
        WorldPackCodec codec)
    {
        if (offset > 0x07FFFF)
        {
            throw new InvalidOperationException($"Game Boy WorldPack plane offset {offset} exceeds the current 19-bit MBC1 reader envelope.");
        }

        if (storedBytes > 128 || decodedBytes > 128 || decodedBytes % idBytes != 0)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack plane requires stored/decoded lengths within one 8x8 ID slot; stored={storedBytes}, decoded={decodedBytes}, idBytes={idBytes}.");
        }

        return new GameBoyWorldPackPlaneRuntimeDescriptor(
            offset,
            checked((byte)storedBytes),
            checked((byte)(decodedBytes / idBytes)),
            checked((byte)idBytes),
            checked((ushort)idCount),
            codec);
    }
}

internal static class GameBoyWorldPackRuntimeEmitter
{
    private sealed record DecoderLabels(
        string Raw,
        string Rle,
        string ReadSourceByte,
        string Success,
        string Malformed);

    private const ushort ScratchPacketCount = 0xC1F0;
    private const ushort ScratchXLow = 0xC1F1;
    private const ushort ScratchXHigh = 0xC1F2;
    private const ushort ScratchYLow = 0xC1F3;
    private const ushort ScratchYHigh = 0xC1F4;
    private const ushort ScratchCellIndex = 0xC1F5;
    private const ushort ScratchStoredRemaining = 0xC1F6;
    private const ushort ScratchSourceBank = 0xC1F7;
    private const ushort ScratchReadByte = 0xC1F8;
    private const ushort ScratchSubcellIndex = 0xC1F9;
    private const ushort ScratchSubcellIndexHigh = 0xC1FC;

    public static void Emit(GbBuilder builder, GameBoyWorldPackRuntimePlan plan, GameBoyRomLayout layout)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(layout);

        var descriptor = plan.Pack.Descriptor;
        var runtimeDirectory = builder.CreateLabel("worldpack_runtime_directory");
        var visualDecoders = CreateDecoderLabels(builder, "visual");
        var collisionDecoders = CreateDecoderLabels(builder, "collision");
        var collisionProfileBytes = EmitMetadataValidation(
            builder,
            plan,
            layout,
            runtimeDirectory,
            visualDecoders,
            collisionDecoders);
        EmitVisualRuntime(builder, plan, layout, runtimeDirectory, visualDecoders);
        EmitCollisionRuntime(builder, plan, layout, collisionProfileBytes, runtimeDirectory, collisionDecoders);
        builder.Label(runtimeDirectory);
        builder.Emit(plan.RuntimeDirectoryBytes);
    }

    private static DecoderLabels CreateDecoderLabels(GbBuilder builder, string planeName) =>
        new(
            builder.CreateLabel($"worldpack_{planeName}_decode_raw"),
            builder.CreateLabel($"worldpack_{planeName}_decode_rle"),
            builder.CreateLabel($"worldpack_{planeName}_read_source_byte"),
            builder.CreateLabel($"worldpack_{planeName}_decode_success"),
            builder.CreateLabel($"worldpack_{planeName}_decode_malformed"));

    private static void EmitVisualRuntime(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        DecoderLabels decoders)
    {
        var descriptor = plan.Pack.Descriptor;
        EmitPlaneDecodeEntry(
            builder,
            GameBoyRomBuilder.WorldPackVisualDecodeLabel,
            plan,
            layout,
            planeOffset: 0,
            plan.Layout.VisualSlots,
            runtimeDirectory,
            decoders.Raw,
            decoders.Rle);
        EmitVisualLookup(builder, plan, layout, runtimeDirectory, decoders.Raw, decoders.Rle);
        EmitRawDecoder(
            builder,
            descriptor.VisualIdBytes,
            descriptor.VisualMetatileCount,
            decoders.Raw,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack);
        EmitRleDecoder(
            builder,
            descriptor.VisualIdBytes,
            descriptor.VisualMetatileCount,
            decoders.Rle,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack);
        EmitReadSourceByte(builder, decoders.ReadSourceByte, layout.UsesBankedWorldPack);
        EmitDecodeReturns(builder, decoders.Success, decoders.Malformed, layout.UsesBankedWorldPack);
    }

    private static void EmitVisualLookup(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        string rawDecoder,
        string rleDecoder)
    {
        var bounds = builder.CreateLabel("worldpack_visual_lookup_bounds");
        var returnStatus = builder.CreateLabel("worldpack_visual_lookup_return_status");
        var descriptor = plan.Pack.Descriptor;
        builder.Label(GameBoyRomBuilder.WorldPackVisualLookupLabel);
        builder.Emit(0xD5); // PUSH DE
        builder.PushHl();
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.PopHl();
        builder.Emit(0xD1); // POP DE
        builder.LoadBFromA();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        EmitStoreLookupCoordinates(builder);
        EmitJumpIfWordOutside(builder, ScratchXLow, ScratchXHigh, 0, descriptor.HardwareWidth, bounds);
        EmitJumpIfWordOutside(builder, ScratchYLow, ScratchYHigh, 0, descriptor.HardwareHeight, bounds);
        EmitPrepareLookupState(builder, plan, runtimeDirectory);
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 0);
        builder.LoadDe(plan.Layout.VisualSlots[0].Start);
        EmitSetValidationOnly(builder, validationOnly: false);
        EmitCallSelectedDecoder(builder, rawDecoder, rleDecoder, returnStatus);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        EmitVisualExpansionLookup(builder, plan, layout);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
        builder.Label(bounds);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
        builder.Emit(0xC9);
        builder.Label(returnStatus);
        builder.LoadAImmediate(0);
        builder.Emit(0xC9);
    }

    private static void EmitVisualExpansionLookup(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(ScratchCellIndex);
        if (descriptor.VisualIdBytes == 2)
        {
            builder.AddAFromA();
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(plan.Layout.VisualSlots[0].Start);
        builder.AddHlDe();
        builder.Emit(0x4E); // LD C,(HL)
        if (descriptor.VisualIdBytes == 2)
        {
            builder.IncrementHl();
            builder.Emit(0x46); // LD B,(HL)
        }
        else
        {
            builder.Emit(0x06, 0x00); // LD B,0
        }

        builder.LoadHl(0);
        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        builder.XorA();
        builder.StoreA(ScratchYLow); // high byte of the 24-bit pack-relative expansion offset
        builder.LoadDe(checked((ushort)metatileCells));
        var multiplyLoop = builder.CreateLabel("worldpack_visual_expansion_multiply");
        var multiplyDone = builder.CreateLabel("worldpack_visual_expansion_multiply_done");
        builder.Label(multiplyLoop);
        builder.LoadAFromD();
        builder.Emit(0xB3); // OR E
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, multiplyDone);
        builder.AddHlBc();
        EmitIncrementScratchOnCarry(builder, ScratchYLow);
        builder.Emit(0x1B); // DEC DE
        builder.JumpAbsolute(multiplyLoop);
        builder.Label(multiplyDone);

        builder.LoadDe((ushort)(descriptor.TargetExpansionsOffset & 0xFFFF));
        builder.AddHlDe();
        EmitIncrementScratchOnCarry(builder, ScratchYLow);
        builder.LoadA(ScratchYLow);
        builder.AddAImmediate((int)((descriptor.TargetExpansionsOffset >> 16) & 0xFF));
        builder.StoreA(ScratchYLow);
        builder.LoadA(ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(ScratchSubcellIndexHigh);
        builder.LoadDFromA();
        builder.AddHlDe();
        EmitIncrementScratchOnCarry(builder, ScratchYLow);

        if (layout.WorldPackPlacement is { } placement)
        {
            builder.LoadDe(checked((ushort)(placement.BaseAddress - 0x4000)));
            builder.AddHlDe();
            EmitIncrementScratchOnCarry(builder, ScratchYLow);
            builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
            builder.PushAf();
            builder.LoadA(ScratchYLow);
            builder.AddAFromA();
            builder.AddAFromA();
            builder.LoadBFromA();
            builder.LoadAFromH();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.AddAFromB();
            builder.AddAImmediate(placement.BaseBank);
            builder.StoreA(ScratchSourceBank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            builder.LoadAFromH();
            builder.AndImmediate(0x3F);
            builder.OrImmediate(0x40);
            builder.LoadHFromA();
            builder.LoadAFromHl();
            builder.StoreA(ScratchReadByte);
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            builder.LoadA(ScratchReadByte);
        }
        else
        {
            builder.LoadDe(GameBoyRomBuilder.WorldPackLabel);
            builder.AddHlDe();
            builder.LoadAFromHl();
        }
    }

    private static void EmitIncrementScratchOnCarry(GbBuilder builder, ushort address)
    {
        var noCarry = builder.CreateLabel("worldpack_offset_no_carry");
        builder.JumpAbsolute(0xD2, noCarry);
        builder.LoadA(address);
        builder.Emit(0x3C); // INC A
        builder.StoreA(address);
        builder.Label(noCarry);
    }

    private static void EmitCollisionRuntime(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string profileBytes,
        string runtimeDirectory,
        DecoderLabels decoders)
    {
        var lookupBounds = builder.CreateLabel("worldpack_collision_lookup_bounds");
        var lookupReturnStatus = builder.CreateLabel("worldpack_collision_lookup_return_status");
        var descriptor = plan.Pack.Descriptor;

        EmitPlaneDecodeEntry(
            builder,
            GameBoyRomBuilder.WorldPackCollisionDecodeLabel,
            plan,
            layout,
            planeOffset: 1,
            plan.Layout.CollisionSlots,
            runtimeDirectory,
            decoders.Raw,
            decoders.Rle);

        builder.Label(GameBoyRomBuilder.WorldPackCollisionLookupLabel);
        builder.Emit(0xD5); // PUSH DE
        builder.PushHl();
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.PopHl();
        builder.Emit(0xD1); // POP DE
        builder.LoadBFromA();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, lookupReturnStatus);
        EmitStoreLookupCoordinates(builder);
        EmitJumpIfWordOutside(builder, ScratchXLow, ScratchXHigh, 0, descriptor.HardwareWidth, lookupBounds);
        EmitJumpIfWordOutside(builder, ScratchYLow, ScratchYHigh, 0, descriptor.HardwareHeight, lookupBounds);
        EmitPrepareLookupState(builder, plan, runtimeDirectory);
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 6);
        builder.LoadDe(plan.Layout.CollisionSlots[0].Start);
        EmitSetValidationOnly(builder, validationOnly: false);
        EmitCallSelectedDecoder(builder, decoders.Raw, decoders.Rle, lookupReturnStatus);
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, lookupReturnStatus);
        EmitCollisionProfileLookup(builder, plan, profileBytes);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
        builder.Label(lookupBounds);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
        builder.Emit(0xC9);
        builder.Label(lookupReturnStatus);
        builder.LoadAImmediate(0);
        builder.Emit(0xC9);

        EmitRawDecoder(
            builder,
            descriptor.CollisionIdBytes,
            descriptor.CollisionProfileCount,
            decoders.Raw,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack);
        EmitRleDecoder(
            builder,
            descriptor.CollisionIdBytes,
            descriptor.CollisionProfileCount,
            decoders.Rle,
            decoders.ReadSourceByte,
            decoders.Success,
            decoders.Malformed,
            layout.UsesBankedWorldPack);
        EmitReadSourceByte(builder, decoders.ReadSourceByte, layout.UsesBankedWorldPack);
        EmitDecodeReturns(builder, decoders.Success, decoders.Malformed, layout.UsesBankedWorldPack);

    }

    private static void EmitPlaneDecodeEntry(
        GbBuilder builder,
        string entryLabel,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        int planeOffset,
        IReadOnlyList<GameBoyWramRange> slots,
        string runtimeDirectory,
        string rawDecoder,
        string rleDecoder)
    {
        var bounds = builder.CreateLabel("worldpack_decode_bounds");
        var miss = builder.CreateLabel("worldpack_decode_miss");
        var returnStatus = builder.CreateLabel("worldpack_decode_return_status");
        builder.Label(entryLabel);
        builder.Emit(0xC5); // PUSH BC
        builder.PushHl();
        builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadAFromB();
        builder.PopHl();
        builder.Emit(0xC1); // POP BC
        builder.LoadBFromA();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, returnStatus);
        builder.LoadAFromL();
        builder.StoreA(ScratchXLow);
        builder.LoadAFromH();
        builder.StoreA(ScratchXHigh);
        builder.LoadAFromC();
        builder.StoreA(ScratchPacketCount);
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(2);
        builder.JumpAbsolute(0xD2, bounds);

        EmitJumpIfWordOutside(builder, ScratchXLow, ScratchXHigh, 0, plan.Pack.Chunks.Count, miss);
        EmitLoadSlotDestination(builder, slots);
        builder.Emit(0xD5); // PUSH DE; source preparation uses DE as arithmetic scratch
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeOffset * 6);
        builder.Emit(0xD1); // POP DE
        EmitSetValidationOnly(builder, validationOnly: false);
        EmitCallSelectedDecoder(builder, rawDecoder, rleDecoder, returnStatus);
        builder.Emit(0xC9);
        builder.Label(bounds);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.BoundsError);
        builder.Emit(0xC9);
        builder.Label(miss);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Miss);
        builder.Emit(0xC9);
        builder.Label(returnStatus);
        builder.LoadAImmediate(0);
        builder.Emit(0xC9);
    }

    private static void EmitLoadPlaneAndPrepareSource(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        int planeEntryOffset)
    {
        EmitRuntimeDirectoryPointer(builder, runtimeDirectory, planeEntryOffset);
        builder.Emit(0x2A); // LD A,(HL+): offset low
        builder.StoreA(ScratchXLow);
        builder.Emit(0x2A); // offset middle
        builder.StoreA(ScratchXHigh);
        builder.Emit(0x2A); // offset high
        builder.StoreA(ScratchYLow);
        builder.Emit(0x2A); // stored bytes
        builder.StoreA(ScratchStoredRemaining);
        builder.Emit(0x2A); // decoded elements
        builder.LoadCFromA();
        builder.LoadAFromHl(); // codec
        builder.StoreA(ScratchPacketCount);

        if (layout.WorldPackPlacement is { } placement)
        {
            builder.LoadA(ScratchXLow);
            builder.LoadLFromA();
            builder.LoadA(ScratchXHigh);
            builder.LoadHFromA();
            builder.LoadDe(checked((ushort)(placement.BaseAddress - 0x4000)));
            builder.AddHlDe();
            builder.LoadA(ScratchYLow);
            builder.AdcAImmediate(0);
            builder.StoreA(ScratchYLow);
            builder.LoadAFromH();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.ShiftRightLogicalA();
            builder.LoadBFromA();
            builder.LoadA(ScratchYLow);
            builder.AddAFromA();
            builder.AddAFromA();
            builder.AddAFromB();
            builder.AddAImmediate(placement.BaseBank);
            builder.StoreA(ScratchSourceBank);
            builder.LoadAFromH();
            builder.AndImmediate(0x3F);
            builder.OrImmediate(0x40);
            builder.LoadHFromA();
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.WorldPackLabel);
            builder.LoadA(ScratchXLow);
            builder.LoadEFromA();
            builder.LoadA(ScratchXHigh);
            builder.LoadDFromA();
            builder.AddHlDe();
            builder.LoadAImmediate(0);
            builder.StoreA(ScratchSourceBank);
        }
    }

    private static void EmitCallSelectedDecoder(
        GbBuilder builder,
        string rawDecoder,
        string rleDecoder,
        string malformedReturn)
    {
        var rle = builder.CreateLabel("worldpack_decode_select_rle");
        var done = builder.CreateLabel("worldpack_decode_select_done");
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate((byte)WorldPackCodec.Raw);
        builder.JumpAbsolute(0xC2, rle);
        builder.JumpAbsolute(0xCD, rawDecoder);
        builder.JumpAbsolute(done);
        builder.Label(rle);
        builder.CompareImmediate((byte)WorldPackCodec.ElementRle);
        builder.JumpAbsolute(0xC2, malformedReturn);
        builder.JumpAbsolute(0xCD, rleDecoder);
        builder.Label(done);
    }

    private static void EmitRuntimeDirectoryPointer(
        GbBuilder builder,
        string runtimeDirectory,
        int entryOffset)
    {
        builder.LoadA(ScratchXHigh);
        builder.LoadBFromA();
        builder.LoadA(ScratchXLow);
        builder.LoadCFromA();
        builder.LoadHl(0);
        for (var index = 0; index < 14; index++)
        {
            builder.AddHlBc();
        }

        builder.LoadDe(runtimeDirectory);
        builder.AddHlDe();
        if (entryOffset != 0)
        {
            builder.LoadDe(checked((ushort)entryOffset));
            builder.AddHlDe();
        }
    }

    private static void EmitLoadSlotDestination(GbBuilder builder, IReadOnlyList<GameBoyWramRange> slots)
    {
        var second = builder.CreateLabel("worldpack_decode_second_slot");
        var ready = builder.CreateLabel("worldpack_decode_slot_ready");
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, second);
        builder.LoadDe(slots[0].Start);
        builder.JumpAbsolute(ready);
        builder.Label(second);
        builder.LoadDe(slots[1].Start);
        builder.Label(ready);
    }

    private static (byte Bank, ushort Address, bool InlineLabel) ResolveSource(
        GameBoyRomLayout layout,
        uint relativeOffset)
    {
        if (layout.WorldPackPlacement is { } placement)
        {
            var far = placement.TranslateOffset(checked((int)relativeOffset));
            return (far.Bank, far.Address, false);
        }

        if (relativeOffset > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Inline Game Boy WorldPack relative offset {relativeOffset} exceeds the fixed 16-bit address space.");
        }

        return (0, 0, true);
    }

    private static void EmitRawDecoder(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string label,
        string readSourceByte,
        string success,
        string malformed,
        bool banked)
    {
        var loop = builder.CreateLabel("worldpack_raw_loop");
        builder.Label(label);
        EmitBeginDecode(builder, banked);
        builder.Label(loop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, success);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);
        EmitWriteDecodedId(builder, idBytes);

        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(loop);
    }

    private static void EmitRleDecoder(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string label,
        string readSourceByte,
        string success,
        string malformed,
        bool banked)
    {
        var packetLoop = builder.CreateLabel("worldpack_rle_packet");
        var runPacket = builder.CreateLabel("worldpack_rle_run");
        var literalLoop = builder.CreateLabel("worldpack_rle_literal_loop");
        var runLoop = builder.CreateLabel("worldpack_rle_run_loop");
        var packetDone = builder.CreateLabel("worldpack_rle_packet_done");

        builder.Label(label);
        EmitBeginDecode(builder, banked);
        builder.Label(packetLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, success);
        EmitRequireStoredByte(builder, malformed);
        builder.JumpAbsolute(0xCD, readSourceByte);
        builder.StoreA(ScratchReadByte);
        builder.AndImmediate(0x80);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, runPacket);

        builder.LoadA(ScratchReadByte);
        builder.AddAImmediate(1);
        builder.StoreA(ScratchPacketCount);
        EmitRequirePacketFits(builder, malformed);
        builder.Label(literalLoop);
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, packetDone);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);
        EmitWriteDecodedId(builder, idBytes);

        builder.Emit(0x0D); // DEC C
        builder.LoadA(ScratchPacketCount);
        builder.DecrementA();
        builder.StoreA(ScratchPacketCount);
        builder.JumpAbsolute(literalLoop);

        builder.Label(runPacket);
        builder.LoadA(ScratchReadByte);
        builder.AndImmediate(0x7F);
        builder.AddAImmediate(3);
        builder.StoreA(ScratchPacketCount);
        EmitRequirePacketFits(builder, malformed);
        EmitReadAndValidateId(builder, idBytes, idCount, readSourceByte, malformed);

        builder.Label(runLoop);
        builder.LoadA(ScratchPacketCount);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, packetDone);
        EmitWriteDecodedId(builder, idBytes);

        builder.Emit(0x0D); // DEC C
        builder.LoadA(ScratchPacketCount);
        builder.DecrementA();
        builder.StoreA(ScratchPacketCount);
        builder.JumpAbsolute(runLoop);

        builder.Label(packetDone);
        builder.JumpAbsolute(packetLoop);
    }

    private static void EmitBeginDecode(GbBuilder builder, bool banked)
    {
        if (!banked)
        {
            return;
        }

        builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
        builder.PushAf();
        builder.LoadA(ScratchSourceBank);
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
    }

    private static void EmitReadSourceByte(GbBuilder builder, string label, bool banked)
    {
        var noCross = builder.CreateLabel("worldpack_read_no_cross");
        builder.Label(label);
        builder.LoadAFromHl();
        builder.StoreA(ScratchReadByte);
        builder.IncrementHl();
        builder.LoadA(ScratchStoredRemaining);
        builder.DecrementA();
        builder.StoreA(ScratchStoredRemaining);
        if (banked)
        {
            builder.LoadAFromH();
            builder.CompareImmediate(0x80);
            builder.JumpAbsolute(0xC2, noCross);
            builder.LoadHImmediate(0x40);
            builder.LoadA(ScratchSourceBank);
            builder.Emit(0x3C); // INC A
            builder.StoreA(ScratchSourceBank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            builder.Label(noCross);
        }

        builder.LoadA(ScratchReadByte);
        builder.Emit(0xC9);
    }

    private static void EmitDecodeReturns(
        GbBuilder builder,
        string success,
        string malformed,
        bool banked)
    {
        var successReturn = builder.CreateLabel("worldpack_decode_success_return");
        var malformedReturn = builder.CreateLabel("worldpack_decode_malformed_return");
        builder.Label(success);
        builder.LoadA(ScratchStoredRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, malformed);
        builder.JumpAbsolute(successReturn);
        builder.Label(malformed);
        builder.JumpAbsolute(malformedReturn);

        builder.Label(successReturn);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
        }

        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);

        builder.Label(malformedReturn);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
        }

        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);
    }

    private static void EmitRequireStoredByte(GbBuilder builder, string malformed)
    {
        builder.LoadA(ScratchStoredRemaining);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, malformed);
    }

    private static void EmitRequirePacketFits(GbBuilder builder, string malformed)
    {
        builder.LoadA(ScratchPacketCount);
        builder.LoadBFromA();
        builder.LoadAFromC();
        builder.CompareB();
        builder.JumpAbsolute(0xDA, malformed); // JP C when remaining < packet count
    }

    private static void EmitStoreLookupCoordinates(GbBuilder builder)
    {
        builder.LoadAFromE();
        builder.StoreA(ScratchXLow);
        builder.LoadAFromD();
        builder.StoreA(ScratchXHigh);
        builder.LoadAFromL();
        builder.StoreA(ScratchYLow);
        builder.LoadAFromH();
        builder.StoreA(ScratchYHigh);
    }

    private static void EmitPrepareLookupState(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        string runtimeDirectory)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(ScratchXLow);
        builder.LoadEFromA();
        builder.LoadA(ScratchXHigh);
        builder.LoadDFromA();
        EmitDivideDeByConstant(builder, descriptor.MetatileWidth, ScratchSourceBank);

        builder.Emit(0xD5); // PUSH DE; preserve metatile X while dividing Y
        builder.LoadA(ScratchYLow);
        builder.LoadEFromA();
        builder.LoadA(ScratchYHigh);
        builder.LoadDFromA();
        EmitDivideDeByConstant(builder, descriptor.MetatileHeight, ScratchReadByte);
        builder.Emit(0x62); // LD H,D
        builder.Emit(0x6B); // LD L,E
        builder.Emit(0xD1); // POP DE

        builder.LoadAFromE();
        builder.AndImmediate(0x07);
        builder.StoreA(ScratchYHigh); // local metatile X
        builder.LoadAFromL();
        builder.AndImmediate(0x07);
        builder.StoreA(ScratchStoredRemaining); // local metatile Y
        for (var shift = 0; shift < 3; shift++)
        {
            EmitShiftRightDe(builder);
            EmitShiftRightHl(builder);
        }

        builder.Emit(0x44); // LD B,H (chunk Y high)
        builder.Emit(0x4D); // LD C,L (chunk Y low)
        builder.LoadHl(0);
        for (var column = 0; column < descriptor.ChunkColumns; column++)
        {
            builder.AddHlBc();
        }

        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(ScratchXLow);
        builder.LoadAFromH();
        builder.StoreA(ScratchXHigh);

        EmitRuntimeDirectoryPointer(builder, runtimeDirectory, entryOffset: 12);
        builder.LoadAFromHl(); // validWidth
        builder.LoadBFromA();
        builder.LoadA(ScratchStoredRemaining);
        builder.LoadCFromA();
        builder.XorA();
        builder.StoreA(ScratchCellIndex);
        var cellLoop = builder.CreateLabel("worldpack_cell_index_loop");
        var cellDone = builder.CreateLabel("worldpack_cell_index_done");
        builder.Label(cellLoop);
        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, cellDone);
        builder.LoadA(ScratchCellIndex);
        builder.AddAFromB();
        builder.StoreA(ScratchCellIndex);
        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(cellLoop);
        builder.Label(cellDone);
        builder.LoadA(ScratchCellIndex);
        builder.LoadBFromA();
        builder.LoadA(ScratchYHigh);
        builder.AddAFromB();
        builder.StoreA(ScratchCellIndex);

        builder.LoadA(ScratchReadByte);
        builder.LoadCFromA();
        builder.Emit(0x06, 0x00); // LD B,0
        builder.LoadHl(0);
        for (var index = 0; index < descriptor.MetatileWidth; index++)
        {
            builder.AddHlBc();
        }

        builder.LoadA(ScratchSourceBank);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromL();
        builder.StoreA(ScratchSubcellIndex);
        builder.LoadAFromH();
        builder.StoreA(ScratchSubcellIndexHigh);
    }

    private static void EmitShiftRightDe(GbBuilder builder)
    {
        builder.Emit(0xCB, 0x3A); // SRL D
        builder.Emit(0xCB, 0x1B); // RR E through D carry
    }

    private static void EmitDivideDeByConstant(GbBuilder builder, int divisor, ushort remainderAddress)
    {
        if (divisor == 1)
        {
            builder.XorA();
            builder.StoreA(remainderAddress);
            return;
        }

        var loop = builder.CreateLabel("worldpack_divide_loop");
        var subtract = builder.CreateLabel("worldpack_divide_subtract");
        var done = builder.CreateLabel("worldpack_divide_done");
        builder.LoadBc(0);
        builder.Label(loop);
        builder.LoadAFromD();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, subtract);
        builder.LoadAFromE();
        builder.CompareImmediate(divisor);
        builder.JumpAbsolute(0xDA, done);
        builder.Label(subtract);
        builder.LoadAFromE();
        builder.SubtractAImmediate(divisor);
        builder.LoadEFromA();
        builder.LoadAFromD();
        builder.SbcAImmediate(0);
        builder.LoadDFromA();
        builder.Emit(0x03); // INC BC
        builder.JumpAbsolute(loop);
        builder.Label(done);
        builder.LoadAFromE();
        builder.StoreA(remainderAddress);
        builder.Emit(0x50); // LD D,B
        builder.Emit(0x59); // LD E,C
    }

    private static void EmitShiftRightHl(GbBuilder builder)
    {
        builder.Emit(0xCB, 0x3C); // SRL H
        builder.Emit(0xCB, 0x1D); // RR L through H carry
    }

    private static void EmitReadAndValidateId(
        GbBuilder builder,
        int idBytes,
        int idCount,
        string readSourceByte,
        string malformed)
    {
        EmitRequireStoredByte(builder, malformed);
        builder.JumpAbsolute(0xCD, readSourceByte);
        builder.StoreA(ScratchXLow);
        if (idBytes == 2)
        {
            EmitRequireStoredByte(builder, malformed);
            builder.JumpAbsolute(0xCD, readSourceByte);
            builder.StoreA(ScratchXHigh);
            var highIsLower = builder.CreateLabel("worldpack_id_high_lower");
            var highMatches = builder.CreateLabel("worldpack_id_high_matches");
            builder.LoadA(ScratchXHigh);
            builder.CompareImmediate((idCount >> 8) & 0xFF);
            builder.JumpAbsolute(0xDA, highIsLower);
            builder.JumpAbsolute(0xCA, highMatches);
            builder.JumpAbsolute(malformed);
            builder.Label(highMatches);
            builder.LoadA(ScratchXLow);
            builder.CompareImmediate(idCount & 0xFF);
            builder.JumpAbsolute(0xD2, malformed);
            builder.Label(highIsLower);
        }
        else if (idCount < 256)
        {
            builder.LoadA(ScratchXLow);
            builder.CompareImmediate(idCount);
            builder.JumpAbsolute(0xD2, malformed);
        }
    }

    private static void EmitWriteDecodedId(GbBuilder builder, int idBytes)
    {
        var done = builder.CreateLabel("worldpack_decode_skip_validation_write");
        builder.LoadA(ScratchYHigh);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, done);
        builder.LoadA(ScratchXLow);
        builder.Emit(0x12); // LD (DE),A
        builder.Emit(0x13); // INC DE
        if (idBytes == 2)
        {
            builder.LoadA(ScratchXHigh);
            builder.Emit(0x12);
            builder.Emit(0x13);
        }

        builder.Label(done);
    }

    private static void EmitSetValidationOnly(GbBuilder builder, bool validationOnly)
    {
        builder.LoadAImmediate(validationOnly ? 1 : 0);
        builder.StoreA(ScratchYHigh);
    }

    private static void EmitCollisionProfileLookup(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        string profileBytes)
    {
        var descriptor = plan.Pack.Descriptor;
        builder.LoadA(ScratchCellIndex);
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.AddAFromA();
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(plan.Layout.CollisionSlots[0].Start);
        builder.AddHlDe();
        builder.Emit(0x4E); // LD C,(HL)
        if (descriptor.CollisionIdBytes == 2)
        {
            builder.IncrementHl();
            builder.Emit(0x46); // LD B,(HL)
        }
        else
        {
            builder.Emit(0x06, 0x00); // LD B,0
        }

        builder.LoadHl(0);
        var metatileCells = checked(descriptor.MetatileWidth * descriptor.MetatileHeight);
        for (var index = 0; index < metatileCells; index++)
        {
            builder.AddHlBc();
        }

        builder.LoadDe(profileBytes);
        builder.AddHlDe();
        builder.LoadA(ScratchSubcellIndex);
        builder.LoadEFromA();
        builder.LoadA(ScratchSubcellIndexHigh);
        builder.LoadDFromA();
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private static void EmitJumpIfWordOutside(
        GbBuilder builder,
        ushort lowAddress,
        ushort highAddress,
        int minimum,
        int maximumExclusive,
        string outside)
    {
        var aboveMinimum = builder.CreateLabel("worldpack_range_above_minimum");
        var belowMaximum = builder.CreateLabel("worldpack_range_below_maximum");
        builder.LoadA(highAddress);
        builder.CompareImmediate((minimum >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, outside); // high < min high
        builder.JumpAbsolute(0xC2, aboveMinimum);
        builder.LoadA(lowAddress);
        builder.CompareImmediate(minimum & 0xFF);
        builder.JumpAbsolute(0xDA, outside);
        builder.Label(aboveMinimum);

        builder.LoadA(highAddress);
        builder.CompareImmediate((maximumExclusive >> 8) & 0xFF);
        builder.JumpAbsolute(0xDA, belowMaximum);
        builder.JumpAbsolute(0xC2, outside);
        builder.LoadA(lowAddress);
        builder.CompareImmediate(maximumExclusive & 0xFF);
        builder.JumpAbsolute(0xD2, outside); // low >= max low
        builder.Label(belowMaximum);
    }

    private static string EmitMetadataValidation(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        DecoderLabels visualDecoders,
        DecoderLabels collisionDecoders)
    {
        var malformed = builder.CreateLabel("worldpack_validate_malformed");
        var success = builder.CreateLabel("worldpack_validate_success");
        var cachedMalformed = builder.CreateLabel("worldpack_validate_cached_malformed");
        var validate = builder.CreateLabel("worldpack_validate_uncached");
        var expectedHeader = builder.CreateLabel("worldpack_expected_header");
        var expectedProfiles = builder.CreateLabel("worldpack_expected_profiles");
        var expectedDirectory = builder.CreateLabel("worldpack_expected_directory");
        var banked = layout.WorldPackPlacement is not null;
        builder.Label(GameBoyRomBuilder.WorldPackValidateLabel);
        builder.LoadA(GameBoyWramLayout.WorldPackValidationStateAddress);
        builder.CompareImmediate(1);
        builder.JumpAbsolute(0xC2, cachedMalformed);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);
        builder.Label(cachedMalformed);
        builder.CompareImmediate(2);
        builder.JumpAbsolute(0xC2, validate);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);
        builder.Label(validate);
        if (banked)
        {
            builder.LoadA(GameBoyRomBuilder.ActualVisibleBankAddress);
            builder.PushAf();
        }

        EmitComparePackRange(builder, layout, offset: 0, plan.HeaderBytes.Length, expectedHeader, malformed);
        EmitComparePackRange(
            builder,
            layout,
            plan.Pack.Descriptor.CollisionProfilesOffset,
            plan.CollisionProfileBytes.Length,
            expectedProfiles,
            malformed);
        EmitComparePackRange(
            builder,
            layout,
            plan.Pack.Descriptor.DirectoryOffset,
            plan.DirectoryBytes.Length,
            expectedDirectory,
            malformed);
        EmitValidateAllPlanes(
            builder,
            plan,
            layout,
            runtimeDirectory,
            visualDecoders,
            collisionDecoders,
            malformed);

        builder.JumpAbsolute(success);
        builder.Label(malformed);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
        }

        builder.LoadAImmediate(2);
        builder.StoreA(GameBoyWramLayout.WorldPackValidationStateAddress);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Malformed);
        builder.Emit(0xC9);

        builder.Label(success);
        if (banked)
        {
            builder.PopAf();
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
        }

        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyWramLayout.WorldPackValidationStateAddress);
        builder.LoadAImmediate(0);
        builder.Emit(0x06, (byte)GameBoyWorldPackResult.Success);
        builder.Emit(0xC9);

        builder.Label(expectedHeader);
        builder.Emit(plan.HeaderBytes);
        builder.Label(expectedProfiles);
        builder.Emit(plan.CollisionProfileBytes);
        builder.Label(expectedDirectory);
        builder.Emit(plan.DirectoryBytes);
        return expectedProfiles;
    }

    private static void EmitValidateAllPlanes(
        GbBuilder builder,
        GameBoyWorldPackRuntimePlan plan,
        GameBoyRomLayout layout,
        string runtimeDirectory,
        DecoderLabels visualDecoders,
        DecoderLabels collisionDecoders,
        string malformed)
    {
        var loop = builder.CreateLabel("worldpack_validate_planes_loop");
        var validateChunk = builder.CreateLabel("worldpack_validate_planes_chunk");
        var done = builder.CreateLabel("worldpack_validate_planes_done");
        var chunkCount = checked((ushort)plan.Pack.Chunks.Count);
        builder.LoadHl(0);
        builder.Label(loop);
        builder.LoadAFromH();
        builder.CompareImmediate(chunkCount >> 8);
        builder.JumpAbsolute(0xC2, validateChunk);
        builder.LoadAFromL();
        builder.CompareImmediate(chunkCount & 0xFF);
        builder.JumpAbsolute(0xCA, done);
        builder.Label(validateChunk);

        EmitStoreChunkIndex(builder);
        builder.PushHl();
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 0);
        EmitSetValidationOnly(builder, validationOnly: true);
        EmitCallSelectedDecoder(builder, visualDecoders.Raw, visualDecoders.Rle, malformed);
        builder.PopHl();
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, malformed);

        EmitStoreChunkIndex(builder);
        builder.PushHl();
        EmitLoadPlaneAndPrepareSource(builder, plan, layout, runtimeDirectory, planeEntryOffset: 6);
        EmitSetValidationOnly(builder, validationOnly: true);
        EmitCallSelectedDecoder(builder, collisionDecoders.Raw, collisionDecoders.Rle, malformed);
        builder.PopHl();
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, malformed);
        builder.IncrementHl();
        builder.JumpAbsolute(loop);
        builder.Label(done);
    }

    private static void EmitStoreChunkIndex(GbBuilder builder)
    {
        builder.LoadAFromL();
        builder.StoreA(ScratchXLow);
        builder.LoadAFromH();
        builder.StoreA(ScratchXHigh);
    }

    private static void EmitComparePackRange(
        GbBuilder builder,
        GameBoyRomLayout layout,
        uint offset,
        int length,
        string expectedLabel,
        string malformed)
    {
        if (length == 0)
        {
            return;
        }

        var loop = builder.CreateLabel("worldpack_validate_range_loop");
        var noCross = builder.CreateLabel("worldpack_validate_range_no_cross");
        var done = builder.CreateLabel("worldpack_validate_range_done");
        var source = ResolveSource(layout, offset);
        if (source.InlineLabel)
        {
            builder.LoadHl(GameBoyRomBuilder.WorldPackLabel);
            builder.LoadBc(checked((ushort)offset));
            builder.AddHlBc();
        }
        else
        {
            builder.LoadAImmediate(source.Bank);
            builder.StoreA(ScratchSourceBank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            builder.LoadHl(source.Address);
        }

        builder.LoadDe(expectedLabel);
        builder.LoadBc(checked((ushort)length));
        builder.Label(loop);
        builder.LoadAFromB();
        builder.OrAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, done);
        builder.LoadAFromHl();
        builder.StoreA(ScratchReadByte);
        builder.IncrementHl();
        if (!source.InlineLabel)
        {
            builder.LoadAFromH();
            builder.CompareImmediate(0x80);
            builder.JumpAbsolute(0xC2, noCross);
            builder.LoadHImmediate(0x40);
            builder.LoadA(ScratchSourceBank);
            builder.Emit(0x3C); // INC A
            builder.StoreA(ScratchSourceBank);
            GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
            builder.Label(noCross);
        }

        builder.Emit(0xC5); // PUSH BC
        builder.Emit(0x1A); // LD A,(DE)
        builder.LoadBFromA();
        builder.LoadA(ScratchReadByte);
        builder.CompareB();
        builder.Emit(0xC1); // POP BC; flags from CP are preserved
        builder.JumpAbsolute(0xC2, malformed);
        builder.Emit(0x13); // INC DE
        builder.Emit(0x0B); // DEC BC
        builder.JumpAbsolute(loop);
        builder.Label(done);
    }
}

internal sealed record GameBoyWorldPackRuntimeLayout(
    int VisualIdBytes,
    int CollisionIdBytes,
    IReadOnlyList<GameBoyWramRange> VisualSlots,
    IReadOnlyList<GameBoyWramRange> CollisionSlots,
    IReadOnlyList<GameBoyWramRange> EdgeSlots,
    int TotalBytes)
{
    private const int ChunkCells = 64;
    private const int EdgeBytes = 21;

    public static GameBoyWorldPackRuntimeLayout Create(int visualIdBytes, int collisionIdBytes)
    {
        ValidateIdBytes(visualIdBytes, nameof(visualIdBytes));
        ValidateIdBytes(collisionIdBytes, nameof(collisionIdBytes));

        var cursor = GameBoyWramLayout.WorldPackStaging.Start;
        var visualSlots = CreatePair("WorldPack visual slot", visualIdBytes * ChunkCells, ref cursor);
        var collisionSlots = CreatePair("WorldPack collision slot", collisionIdBytes * ChunkCells, ref cursor);
        var edgeSlots = CreatePair("WorldPack edge slot", EdgeBytes, ref cursor);
        var totalBytes = cursor - GameBoyWramLayout.WorldPackStaging.Start;
        GameBoyWramLayout.ValidateStagingBytes(totalBytes);

        return new GameBoyWorldPackRuntimeLayout(
            visualIdBytes,
            collisionIdBytes,
            visualSlots,
            collisionSlots,
            edgeSlots,
            totalBytes);
    }

    private static IReadOnlyList<GameBoyWramRange> CreatePair(string name, int length, ref ushort cursor)
    {
        var first = new GameBoyWramRange($"{name} 0", cursor, length);
        cursor = checked((ushort)first.EndExclusive);
        var second = new GameBoyWramRange($"{name} 1", cursor, length);
        cursor = checked((ushort)second.EndExclusive);
        return [first, second];
    }

    private static void ValidateIdBytes(int value, string parameterName)
    {
        if (value is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "WorldPack v1 ID width must be one or two bytes.");
        }
    }
}
