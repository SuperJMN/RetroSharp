namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using Xunit;

public sealed class NesWorldPackReaderTests
{
    [Theory]
    [InlineData(1, 1, 338)]
    [InlineData(2, 2, 594)]
    public void Runtime_layout_keeps_bounded_visual_collision_and_edge_slots_separate(
        int visualIdBytes,
        int collisionIdBytes,
        int expectedBytes)
    {
        var layout = NesWorldPackRuntimeLayout.Create(visualIdBytes, collisionIdBytes);
        var slots = layout.VisualSlots
            .Concat(layout.CollisionSlots)
            .Concat(layout.EdgeSlots)
            .OrderBy(slot => slot.Start)
            .ToArray();

        Assert.Equal(2, layout.VisualSlots.Count);
        Assert.Equal(2, layout.CollisionSlots.Count);
        Assert.Equal(2, layout.EdgeSlots.Count);
        Assert.All(layout.VisualSlots, slot => Assert.Equal(64 * visualIdBytes, slot.Length));
        Assert.All(layout.CollisionSlots, slot => Assert.Equal(64 * collisionIdBytes, slot.Length));
        Assert.All(layout.EdgeSlots, slot => Assert.Equal(41, slot.Length));
        Assert.Equal(expectedBytes, layout.TotalBytes);
        Assert.All(slots.Zip(slots.Skip(1)), pair => Assert.True(pair.First.EndExclusive <= pair.Second.Start));
        Assert.InRange(slots[0].Start, 0x0400, 0x07FF);
        Assert.InRange(slots[^1].EndExclusive - 1, 0x0400, 0x07FF);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Runtime_layout_rejects_id_widths_outside_worldpack_v1(int idBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NesWorldPackRuntimeLayout.Create(idBytes, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => NesWorldPackRuntimeLayout.Create(1, idBytes));
    }

    [Theory]
    [InlineData(false, WorldPackCodec.Raw, 64)]
    [InlineData(true, WorldPackCodec.ElementRle, 2)]
    public void Runtime_plan_preserves_canonical_plane_offsets_lengths_and_codecs(
        bool repeated,
        WorldPackCodec expectedCodec,
        int expectedStoredBytes)
    {
        var fixture = CreateSingleChunkFixture(repeated);

        var plan = NesWorldPackRuntimePlan.Create(fixture.SerializedBytes);

        Assert.Equal(fixture.Pack.Descriptor, plan.Pack.Descriptor);
        Assert.Equal(2, plan.Planes.Count);
        Assert.Equal(fixture.Pack.Chunks[0].Directory.VisualOffset, plan.Planes[0].Offset);
        Assert.Equal(expectedStoredBytes, plan.Planes[0].StoredBytes);
        Assert.Equal(64, plan.Planes[0].DecodedBytes);
        Assert.Equal(expectedCodec, plan.Planes[0].Codec);
        Assert.Equal(fixture.Pack.Chunks[0].Directory.CollisionOffset, plan.Planes[1].Offset);
        Assert.Equal(expectedStoredBytes, plan.Planes[1].StoredBytes);
        Assert.Equal(expectedCodec, plan.Planes[1].Codec);
    }

    [Fact]
    public void Runtime_plan_rejects_reserved_nes_expansion_metadata_bits()
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var malformed = fixture.SerializedBytes.ToArray();
        malformed[checked((int)fixture.Pack.Descriptor.TargetExpansionsOffset + 1)] = 0x08;

        var exception = Assert.Throws<InvalidOperationException>(() => NesWorldPackRuntimePlan.Create(malformed));

        Assert.Equal("NES WorldPack expansion metadata byte 0 contains reserved bits $08.", exception.Message);
    }

    [Fact]
    public void Resident_and_far_readers_publish_the_same_fixed_entry_points()
    {
        var fixture = CreateSingleChunkFixture(repeated: false);

        var resident = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var far = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);

        Assert.Equal("nes-mapper-0-current", resident.Report.SelectedProfile);
        Assert.Equal("nes-mmc3-tvrom-v1", far.Report.SelectedProfile);
        foreach (var label in new[]
                 {
                     NesRomBuilder.WorldPackValidateLabel,
                     NesRomBuilder.WorldPackVisualDecodeLabel,
                     NesRomBuilder.WorldPackVisualLookupLabel,
                     NesRomBuilder.WorldPackCollisionDecodeLabel,
                     NesRomBuilder.WorldPackCollisionLookupLabel,
                 })
        {
            Assert.InRange(resident.Report.FixedSymbols[label], 0x8000, 0xFFF9);
            Assert.InRange(far.Report.FixedSymbols[label], 0xC000, 0xFF79);
        }
    }

    [Fact]
    public void Startup_does_not_replace_the_validation_cache_with_the_return_status()
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var validateAddress = result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel];
        var invalidSequence = new byte[]
        {
            0x20,
            (byte)validateAddress,
            (byte)(validateAddress >> 8),
            0x8D,
            (byte)(NesWorldPackRuntimeAbi.ValidationState & 0xFF),
            (byte)(NesWorldPackRuntimeAbi.ValidationState >> 8),
        };

        Assert.Equal(-1, result.Rom.AsSpan().IndexOf(invalidSequence));
    }

    [Fact]
    public void Unified_byte_reader_crosses_noncontiguous_r6_segments_and_restores_entry_bank()
    {
        var residentFixture = CreateSingleChunkFixture(repeated: false);
        var resident = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            packedWorldOverride: residentFixture.SerializedBytes);
        foreach (var offset in new[] { 0, 47, residentFixture.SerializedBytes.Length - 1 })
        {
            var cpu = new NesTestCpu(resident.Rom);
            cpu.SetPackOffset((uint)offset);

            var result = cpu.RunRoutine(resident.Report.FixedSymbols[NesRomBuilder.WorldPackReadByteLabel]);

            Assert.False(result.Carry);
            Assert.Equal(residentFixture.SerializedBytes[offset], result.A);
            Assert.Empty(cpu.R6BankWrites);
        }

        var farBytes = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        var far = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: farBytes);
        foreach (var offset in new[] { 0, 8 * 1_024 - 1, 8 * 1_024, 16 * 1_024 - 1, 16 * 1_024, farBytes.Length - 1 })
        {
            var cpu = new NesTestCpu(far.Rom);
            cpu.SetR6Bank(5);
            cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
            cpu.SetPackOffset((uint)offset);

            var result = cpu.RunRoutine(far.Report.FixedSymbols[NesRomBuilder.WorldPackReadByteLabel]);

            Assert.False(result.Carry);
            Assert.Equal(farBytes[offset], result.A);
            Assert.Equal(5, cpu.CurrentR6Bank);
            Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
            var expectedReadBank = new[] { 0, 3, 4, 5 }[offset / (8 * 1_024)];
            Assert.Equal(new[] { expectedReadBank, 5 }, cpu.R6BankWrites);
        }
    }

    [Fact]
    public void Runtime_validation_rejects_mutated_metadata_before_any_slot_is_staged()
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var layout = NesWorldPackRuntimeLayout.Create(1, 1);
        var validCpu = new NesTestCpu(result.Rom);
        FillSlots(validCpu, layout, 0xA5);

        var valid = validCpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel]);

        Assert.Equal((byte)NesWorldPackResult.Success, valid.A);
        Assert.Equal(1, validCpu.Ram(NesWorldPackRuntimeAbi.ValidationState));
        AssertSlots(validCpu, layout, 0xA5);

        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var malformedRom = result.Rom.ToArray();
        malformedRom[16 + packSegment.PhysicalStart] ^= 0x20;
        var malformedCpu = new NesTestCpu(malformedRom);
        FillSlots(malformedCpu, layout, 0xA5);

        var malformed = malformedCpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel]);

        Assert.Equal((byte)NesWorldPackResult.Malformed, malformed.A);
        Assert.Equal(2, malformedCpu.Ram(NesWorldPackRuntimeAbi.ValidationState));
        AssertSlots(malformedCpu, layout, 0xA5);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Resident_and_far_raw_rle_planes_decode_into_separate_selected_slots(
        bool far,
        bool repeated)
    {
        var fixture = CreateSingleChunkFixture(repeated);
        var result = far
            ? RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes)
            : RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes);
        var layout = NesWorldPackRuntimeLayout.Create(1, 1);
        var cpu = new NesTestCpu(result.Rom);
        if (far)
        {
            cpu.SetR6Bank(5);
            cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        }
        FillSlots(cpu, layout, 0xA5);
        cpu.SetChunkAndSlot(chunkIndex: 0, slot: 0);

        var visual = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackVisualDecodeLabel]);
        cpu.SetChunkAndSlot(chunkIndex: 0, slot: 1);
        var collision = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionDecodeLabel]);

        Assert.Equal((byte)NesWorldPackResult.Success, visual.A);
        Assert.Equal((byte)NesWorldPackResult.Success, collision.A);
        var expected = repeated
            ? Enumerable.Repeat((byte)1, 64)
            : Enumerable.Range(0, 64).Select(index => (byte)(index & 1));
        Assert.Equal(expected, ReadSlot(cpu, layout.VisualSlots[0]));
        Assert.Equal(expected, ReadSlot(cpu, layout.CollisionSlots[1]));
        Assert.All(layout.VisualSlots.Skip(1), slot => Assert.All(ReadSlot(cpu, slot), value => Assert.Equal(0xA5, value)));
        Assert.All(layout.CollisionSlots.Take(1), slot => Assert.All(ReadSlot(cpu, slot), value => Assert.Equal(0xA5, value)));
        Assert.All(layout.EdgeSlots, slot => Assert.All(ReadSlot(cpu, slot), value => Assert.Equal(0xA5, value)));
        if (far)
        {
            Assert.Equal(5, cpu.CurrentR6Bank);
            Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lookup_expands_nes_visual_metadata_and_collision_from_16bit_coordinates(bool far)
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var result = far
            ? RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes)
            : RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        if (far)
        {
            cpu.SetR6Bank(5);
            cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        }
        cpu.SetWorldPackCoordinates(3, 4);

        var visual = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackVisualLookupLabel]);
        cpu.SetWorldPackCoordinates(3, 4);
        var collision = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionLookupLabel]);

        Assert.Equal((byte)NesWorldPackResult.Success, visual.A);
        Assert.Equal(20, cpu.Ram(NesWorldPackRuntimeAbi.ResultTile));
        Assert.Equal(0x04, cpu.Ram(NesWorldPackRuntimeAbi.ResultMetadata));
        Assert.Equal((byte)NesWorldPackResult.Success, collision.A);
        Assert.Equal((byte)WorldTileFlags.Solid, cpu.Ram(NesWorldPackRuntimeAbi.ResultCollision));

        cpu.SetWorldPackCoordinates(8, 0);
        var bounds = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionLookupLabel]);
        Assert.Equal((byte)NesWorldPackResult.BoundsError, bounds.A);
        if (far)
        {
            Assert.Equal(5, cpu.CurrentR6Bank);
            Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
        }
    }

    [Theory]
    [InlineData(404, 0, WorldPackCodec.Raw)]
    [InlineData(407, 406, WorldPackCodec.ElementRle)]
    public void Multi_r6_directory_and_planes_cross_boundaries_without_serialized_padding(
        int chunkColumns,
        ushort chunkIndex,
        WorldPackCodec expectedCodec)
    {
        var fixture = CreateBoundaryFixture(chunkColumns, firstVisualRaw: expectedCodec == WorldPackCodec.Raw);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        var validation = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel], maxInstructions: 5_000_000);
        cpu.SetChunkAndSlot(chunkIndex, 0);

        var decode = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackVisualDecodeLabel], maxInstructions: 5_000_000);

        Assert.True(fixture.SerializedBytes.Length > 8 * 1_024);
        Assert.Equal(expectedCodec, fixture.Pack.Chunks[chunkIndex].Directory.VisualCodec);
        Assert.Equal((byte)NesWorldPackResult.Success, validation.A);
        Assert.Equal((byte)NesWorldPackResult.Success, decode.A);
        Assert.Equal(
            fixture.Pack.Chunks[chunkIndex].VisualIds.Select(id => (byte)id),
            ReadSlot(cpu, NesWorldPackRuntimeLayout.Create(1, 1).VisualSlots[0]));
        Assert.Contains(0, cpu.R6BankWrites);
        Assert.Contains(3, cpu.R6BankWrites);
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
    }

    [Fact]
    public void Malformed_rle_overrun_keeps_the_selected_slot_unpublished_and_restores_r6()
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var malformedRom = result.Rom.ToArray();
        var collisionOffset = fixture.Pack.Chunks[0].Directory.CollisionOffset;
        MutatePackByte(malformedRom, result.Report, collisionOffset, 0xFF);
        var cpu = new NesTestCpu(malformedRom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        cpu.SetChunkAndSlot(0, 1);

        var status = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionDecodeLabel]);

        Assert.Equal((byte)NesWorldPackResult.Malformed, status.A);
        Assert.Equal(0, cpu.Ram(NesWorldPackRuntimeAbi.CollisionSlot1State));
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_byte_ids_use_the_594_byte_maximum_and_keep_high_id_bits_in_lookup(bool far)
    {
        var fixture = CreateTwoByteFixture();
        var result = far
            ? RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes)
            : RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes);
        var layout = NesWorldPackRuntimeLayout.Create(2, 2);
        var cpu = new NesTestCpu(result.Rom);
        if (far)
        {
            cpu.SetR6Bank(5);
            cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        }
        cpu.SetChunkAndSlot(0, 0);
        var visualDecode = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackVisualDecodeLabel]);
        cpu.SetChunkAndSlot(0, 1);
        var collisionDecode = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionDecodeLabel]);
        cpu.SetWorldPackCoordinates(0, 0);
        var visual = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackVisualLookupLabel]);
        cpu.SetWorldPackCoordinates(0, 0);
        var collision = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionLookupLabel]);

        Assert.Equal(594, layout.TotalBytes);
        Assert.Equal((byte)NesWorldPackResult.Success, visualDecode.A);
        Assert.Equal((byte)NesWorldPackResult.Success, collisionDecode.A);
        Assert.Equal(Enumerable.Repeat(new byte[] { 0x00, 0x01 }, 64).SelectMany(value => value), ReadSlot(cpu, layout.VisualSlots[0]));
        Assert.Equal(Enumerable.Repeat(new byte[] { 0x00, 0x01 }, 64).SelectMany(value => value), ReadSlot(cpu, layout.CollisionSlots[1]));
        Assert.Equal((byte)NesWorldPackResult.Success, visual.A);
        Assert.Equal(77, cpu.Ram(NesWorldPackRuntimeAbi.ResultTile));
        Assert.Equal(0x04, cpu.Ram(NesWorldPackRuntimeAbi.ResultMetadata));
        Assert.Equal((byte)NesWorldPackResult.Success, collision.A);
        Assert.Equal((byte)WorldTileFlags.Platform, cpu.Ram(NesWorldPackRuntimeAbi.ResultCollision));
    }

    [Fact]
    public void Nested_far_read_restores_outer_offset_bank_and_entry_bank_lifo()
    {
        var bytes = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: bytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        cpu.SetPackOffset(0);
        cpu.InjectNestedReadAfterSelecting(
            outerBank: 0,
            result.Report.FixedSymbols[NesRomBuilder.WorldPackReadByteLabel],
            nestedOffset: 8 * 1_024);

        var outer = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackReadByteLabel]);

        Assert.False(outer.Carry);
        Assert.Equal(bytes[0], outer.A);
        Assert.Equal(bytes[8 * 1_024], Assert.Single(cpu.NestedReadResults).A);
        Assert.Equal(new[] { 0, 3, 0, 5 }, cpu.R6BankWrites);
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
    }

    [Fact]
    public void Nmi_during_far_read_is_bank_neutral_and_outer_read_restores_entry_bank()
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        cpu.SetPackOffset(0);
        cpu.InjectNmiAfterSelecting(outerBank: 0);

        var read = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackReadByteLabel]);

        Assert.False(read.Carry);
        Assert.Equal(fixture.SerializedBytes[0], read.A);
        Assert.Equal(1, cpu.NmiCount);
        Assert.Equal(new[] { 0, 5 }, cpu.R6BankWrites);
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(1, cpu.CurrentR7Bank);
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(0, 2, 2)]
    public void Decode_miss_and_bounds_paths_restore_r6_and_shadow(
        ushort chunkIndex,
        byte slot,
        byte expected)
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        cpu.SetChunkAndSlot(chunkIndex, slot);

        var status = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackVisualDecodeLabel]);

        Assert.Equal(expected, status.A);
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
    }

    [Fact]
    public void Out_of_bounds_far_byte_read_restores_r6_and_shadow()
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        cpu.SetPackOffset((uint)fixture.SerializedBytes.Length);

        var read = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackReadByteLabel]);

        Assert.True(read.Carry);
        Assert.Equal(new[] { 5 }, cpu.R6BankWrites);
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
    }

    [Fact]
    public void Raw_and_rle_decode_cycles_are_deterministic_for_lw_3_4_budget()
    {
        var residentRaw = MeasureDecodeCycles(far: false, repeated: false);
        var residentRle = MeasureDecodeCycles(far: false, repeated: true);
        var farRaw = MeasureDecodeCycles(far: true, repeated: false);
        var farRle = MeasureDecodeCycles(far: true, repeated: true);

        Assert.Equal((15_883L, 7_488L, 27_198L, 9_193L), (residentRaw, residentRle, farRaw, farRle));
        Assert.True(residentRle < residentRaw);
        Assert.True(farRle < farRaw);
    }

    [Fact]
    public void Packed_collision_lookup_preserves_floor_y_304_and_existing_no_hit_word_abi()
    {
        var fixture = CreateTallCollisionFixture();
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            "void Main() { }",
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        cpu.SetWorldPackCoordinates(0, 38);

        var floor = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionLookupLabel]);
        var floorFlags = cpu.Ram(NesWorldPackRuntimeAbi.ResultCollision);
        cpu.SetWorldPackCoordinates(0, 37);
        var above = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCollisionLookupLabel]);
        var aboveFlags = cpu.Ram(NesWorldPackRuntimeAbi.ResultCollision);

        Assert.Equal(304, 38 * 8);
        Assert.Equal((byte)NesWorldPackResult.Success, floor.A);
        Assert.Equal((byte)WorldTileFlags.Solid, floorFlags);
        Assert.Equal((byte)NesWorldPackResult.Success, above.A);
        Assert.Equal((byte)WorldTileFlags.Empty, aboveFlags);

        const string hitTopSource = """
            void Main() {
                World.Column(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0);
                World.Map(1, 0, 40);
                Camera.Init(1, 0, 30);
                i16 footWorldY = 304;
                i16 hitTop = Camera.AabbHitTop(0, footWorldY - 4, 8, 12, 1);
                i16 noHit = Camera.AabbHitTop(0, footWorldY - 4, 8, 12, 4);
            }
            """;
        var abiRom = NesRomCompiler.CompileSource(
            hitTopSource,
            sdkLibraryImports: [RetroSharp.Sdk.SdkImportResolver.Portable2D]);
        var prg = abiRom.AsSpan(16, 32 * 1_024);
        Assert.True(
            prg.IndexOf(new byte[]
            {
                0xA5, 0x01, 0x85, 0xE9, 0xA5, 0xE8, 0x18, 0x69, 0x04, 0x85, 0xE8,
                0xA5, 0xE9, 0x69, 0x00, 0xAA, 0xA5, 0xE8, 0x29, 0xF8,
            }) >= 0,
            "Y=304 must keep the high byte while the hit row is aligned.");
        Assert.True(prg.IndexOf(new byte[] { 0xA9, 0xFF, 0xAA }) >= 0, "No-hit must remain $FFFF through A:X.");
    }

    [Fact]
    public void Aprnes_probe_crosses_r6_with_audio_and_leaves_r7_dpcm_handlers_and_vectors_fixed()
    {
        var fixture = CreateBoundaryFixture(407, firstVisualRaw: false);
        const ushort hardwareX = 406 * 8;
        const string source = """
            void Main() {
                Music.Asset(theme, "assets/music/runner.vgz");
                Audio.Init();
                Music.Play(theme);
                while (true) {
                    Video.WaitVBlank();
                    Audio.Update();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            RepositoryDirectory("samples/runner"),
            sdkLibraryImports: [RetroSharp.Sdk.SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes,
            worldPackProbe: new NesWorldPackProbe(hardwareX, 0));
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);

        var probe = cpu.RunRoutine(
            result.Report.FixedSymbols[NesRomBuilder.WorldPackProbeLabel],
            maxInstructions: 5_000_000);

        Assert.Equal((byte)NesWorldPackResult.Success, probe.A);
        Assert.Equal(1, cpu.Ram(NesWorldPackRuntimeAbi.ProbeCompleted));
        Assert.Equal((byte)NesWorldPackResult.Success, cpu.Ram(NesWorldPackRuntimeAbi.ProbeVisualStatus));
        Assert.Equal((byte)NesWorldPackResult.Success, cpu.Ram(NesWorldPackRuntimeAbi.ProbeCollisionStatus));
        Assert.Equal(20, cpu.Ram(NesWorldPackRuntimeAbi.ResultTile));
        Assert.Equal(0x04, cpu.Ram(NesWorldPackRuntimeAbi.ResultMetadata));
        Assert.Equal((byte)WorldTileFlags.Empty, cpu.Ram(NesWorldPackRuntimeAbi.ResultCollision));
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(1, cpu.CurrentR7Bank);
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
        Assert.Contains(result.Report.Segments, segment => segment.Owner == "pinned:bgm:theme" && segment.PhysicalBank == 1);
        Assert.All(
            result.Report.Segments.Where(segment => segment.Owner.StartsWith("fixed:dpcm:", StringComparison.Ordinal)),
            segment => Assert.InRange(segment.CpuAddress, 0xC000, 0xFFF9));
        Assert.All(
            result.Report.FixedSymbols.Values,
            address => Assert.InRange(address, 0xC000, 0xFF79));

        WriteExternalRomIfRequested("RETROSHARP_NES_LW33_PROBE_ROM", result.Rom);
    }

    private static long MeasureDecodeCycles(bool far, bool repeated)
    {
        var fixture = CreateSingleChunkFixture(repeated);
        var result = far
            ? RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes)
            : RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
                "void Main() { }",
                packedWorldOverride: fixture.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        if (far)
        {
            cpu.SetR6Bank(5);
            cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
        }
        _ = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel]);
        cpu.SetChunkAndSlot(0, 0);
        return cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackVisualDecodeLabel]).Cycles;
    }

    private static void FillSlots(NesTestCpu cpu, NesWorldPackRuntimeLayout layout, byte value)
    {
        foreach (var slot in layout.VisualSlots.Concat(layout.CollisionSlots).Concat(layout.EdgeSlots))
        {
            for (var address = slot.Start; address < slot.EndExclusive; address++)
            {
                cpu.SetRam(address, value);
            }
        }
    }

    private static void AssertSlots(NesTestCpu cpu, NesWorldPackRuntimeLayout layout, byte value)
    {
        foreach (var slot in layout.VisualSlots.Concat(layout.CollisionSlots).Concat(layout.EdgeSlots))
        {
            Assert.All(
                Enumerable.Range(slot.Start, slot.Length),
                address => Assert.Equal(value, cpu.Ram((ushort)address)));
        }
    }

    private static byte[] ReadSlot(NesTestCpu cpu, NesRamRange slot) =>
        Enumerable.Range(slot.Start, slot.Length).Select(address => cpu.Ram((ushort)address)).ToArray();

    private static string RepositoryDirectory(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
    }

    private static void WriteExternalRomIfRequested(string environmentVariable, byte[] rom)
    {
        var outputPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, rom);
    }

    private static void MutatePackByte(byte[] rom, NesRomBuildReport report, uint relativeOffset, byte value)
    {
        var segment = report.Segments.Single(item =>
            item.Owner == "worldpack:default" &&
            relativeOffset >= item.RelativeOffset &&
            relativeOffset < item.RelativeOffset + item.Length);
        rom[16 + segment.PhysicalStart + checked((int)relativeOffset - segment.RelativeOffset)] = value;
    }

    private static SerializedWorldPack CreateBoundaryFixture(int chunkColumns, bool firstVisualRaw)
    {
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + 2;
        const uint directoryOffset = targetExpansionsOffset + 4;
        var chunkDataOffset = directoryOffset + checked((uint)(chunkColumns * WorldPackDescriptor.V1DirectoryEntryBytes));
        var nextOffset = chunkDataOffset;
        var chunks = new List<WorldPackChunk>(chunkColumns);
        for (var index = 0; index < chunkColumns; index++)
        {
            var visualRaw = firstVisualRaw && index == 0;
            var visualIds = visualRaw
                ? Enumerable.Range(0, 64).Select(cell => (ushort)(cell & 1)).ToArray()
                : Enumerable.Repeat((ushort)1, 64).ToArray();
            var visualStored = visualRaw ? 64 : 2;
            var visualCodec = visualRaw ? WorldPackCodec.Raw : WorldPackCodec.ElementRle;
            var collisionIds = Enumerable.Repeat((ushort)0, 64).ToArray();
            var directory = new WorldPackChunkDirectoryEntry(
                nextOffset,
                checked((ushort)visualStored),
                64,
                nextOffset + checked((uint)visualStored),
                2,
                64,
                8,
                8,
                visualCodec,
                WorldPackCodec.ElementRle);
            chunks.Add(new WorldPackChunk(directory, visualIds, collisionIds));
            nextOffset += checked((uint)(visualStored + 2));
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = chunkColumns * 8,
            HardwareHeight = 8,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = chunkColumns,
            ChunkRows = 1,
            VisualMetatileCount = 2,
            CollisionProfileCount = 2,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 2,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = nextOffset,
        };
        var pack = new WorldPack(
            descriptor,
            [
                new WorldPackCollisionProfile([WorldTileFlags.Empty]),
                new WorldPackCollisionProfile([WorldTileFlags.Solid]),
            ],
            new byte[] { 7, 0, 20, 0x04 },
            chunks);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateTwoByteFixture()
    {
        const int count = 257;
        const int metatileCells = 3;
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + count * metatileCells;
        var directoryOffset = targetExpansionsOffset + count * metatileCells * 2;
        var chunkDataOffset = directoryOffset + WorldPackDescriptor.V1DirectoryEntryBytes;
        const ushort storedBytes = 3;
        var collisionOffset = chunkDataOffset + storedBytes;
        var packLength = collisionOffset + storedBytes;
        var ids = Enumerable.Repeat((ushort)256, 64).ToArray();
        var directory = new WorldPackChunkDirectoryEntry(
            chunkDataOffset,
            storedBytes,
            128,
            collisionOffset,
            storedBytes,
            128,
            8,
            8,
            WorldPackCodec.ElementRle,
            WorldPackCodec.ElementRle);
        var profiles = Enumerable.Range(0, count)
            .Select(index => new WorldPackCollisionProfile(
                Enumerable.Range(0, metatileCells)
                    .Select(cell => (WorldTileFlags)((index >> ((metatileCells - cell - 1) * 3)) & 0x07))))
            .ToArray();
        var expansions = new byte[count * metatileCells * 2];
        var record256 = 256 * metatileCells * 2;
        expansions[record256] = 77;
        expansions[record256 + 1] = 0x04;
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 24,
            HardwareHeight = 8,
            MetatileWidth = 3,
            MetatileHeight = 1,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = count,
            CollisionProfileCount = count,
            VisualIdBytes = 2,
            CollisionIdBytes = 2,
            TargetCellStride = 2,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = packLength,
        };
        var pack = new WorldPack(
            descriptor,
            profiles,
            expansions,
            [new WorldPackChunk(directory, ids, ids)]);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateTallCollisionFixture()
    {
        const int chunkRows = 5;
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + 2;
        const uint directoryOffset = targetExpansionsOffset + 2;
        var chunkDataOffset = directoryOffset + chunkRows * WorldPackDescriptor.V1DirectoryEntryBytes;
        var nextOffset = chunkDataOffset;
        var chunks = new List<WorldPackChunk>(chunkRows);
        for (var chunk = 0; chunk < chunkRows; chunk++)
        {
            var visual = Enumerable.Repeat((ushort)0, 8).ToArray();
            var collision = Enumerable.Range(0, 8)
                .Select(row => (ushort)(chunk == 4 && row == 6 ? 1 : 0))
                .ToArray();
            var collisionStored = chunk == 4 ? 5 : 2;
            var directory = new WorldPackChunkDirectoryEntry(
                nextOffset,
                2,
                8,
                nextOffset + 2,
                checked((ushort)collisionStored),
                8,
                1,
                8,
                WorldPackCodec.ElementRle,
                WorldPackCodec.ElementRle);
            chunks.Add(new WorldPackChunk(directory, visual, collision));
            nextOffset += checked((uint)(2 + collisionStored));
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 1,
            HardwareHeight = 40,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = 1,
            ChunkRows = chunkRows,
            VisualMetatileCount = 1,
            CollisionProfileCount = 2,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 2,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = nextOffset,
        };
        var pack = new WorldPack(
            descriptor,
            [
                new WorldPackCollisionProfile([WorldTileFlags.Empty]),
                new WorldPackCollisionProfile([WorldTileFlags.Solid]),
            ],
            new byte[] { 7, 0 },
            chunks);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateSingleChunkFixture(bool repeated)
    {
        var ids = repeated
            ? Enumerable.Repeat((ushort)1, 64).ToArray()
            : Enumerable.Range(0, 64).Select(index => (ushort)(index & 1)).ToArray();
        var codec = repeated ? WorldPackCodec.ElementRle : WorldPackCodec.Raw;
        var storedBytes = repeated ? 2 : 64;
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + 2;
        const uint directoryOffset = targetExpansionsOffset + 4;
        const uint chunkDataOffset = directoryOffset + WorldPackDescriptor.V1DirectoryEntryBytes;
        var collisionOffset = chunkDataOffset + (uint)storedBytes;
        var packLength = collisionOffset + (uint)storedBytes;
        var directory = new WorldPackChunkDirectoryEntry(
            chunkDataOffset,
            checked((ushort)storedBytes),
            64,
            collisionOffset,
            checked((ushort)storedBytes),
            64,
            8,
            8,
            codec,
            codec);
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 8,
            HardwareHeight = 8,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = 2,
            CollisionProfileCount = 2,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 2,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = packLength,
        };
        var pack = new WorldPack(
            descriptor,
            [
                new WorldPackCollisionProfile([WorldTileFlags.Empty]),
                new WorldPackCollisionProfile([WorldTileFlags.Solid]),
            ],
            new byte[] { 7, 0, 20, 0x04 },
            [new WorldPackChunk(directory, ids, ids)]);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }
}
