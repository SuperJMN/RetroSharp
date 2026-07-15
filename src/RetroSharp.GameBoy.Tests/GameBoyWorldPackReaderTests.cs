namespace RetroSharp.GameBoy.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;

public sealed class GameBoyWorldPackReaderTests
{
    [Fact]
    public void One_byte_runtime_uses_two_visual_slots_by_default_three_for_packed_camera_and_six_for_diagonal_camera()
    {
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            Path.Combine(RepositoryDirectory("samples/tiled-free-scroll"), "free-scroll.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);

        var direct = GameBoyWorldPackRuntimePlan.Create(canonical.SerializedBytes);
        var packed = GameBoyWorldPackRuntimePlan.Create(canonical.SerializedBytes, enablePackedCameraCache: true);
        var diagonal = GameBoyWorldPackRuntimePlan.Create(
            canonical.SerializedBytes,
            enablePackedCameraCache: true,
            enableDiagonalVisualCache: true);

        Assert.Equal(2, direct.Layout.VisualSlots.Count);
        Assert.Equal(298, direct.Layout.TotalBytes);
        Assert.Equal(3, packed.Layout.VisualSlots.Count);
        Assert.Equal(362, packed.Layout.TotalBytes);
        Assert.Equal(6, diagonal.Layout.VisualSlots.Count);
        Assert.Equal(554, diagonal.Layout.TotalBytes);
    }

    [Theory]
    [InlineData(1, 1, 298, 0xC300, 0xC340, 0xC380, 0xC3C0, 0xC400, 0xC415)]
    [InlineData(2, 2, 554, 0xC300, 0xC380, 0xC400, 0xC480, 0xC500, 0xC515)]
    public void Runtime_layout_keeps_two_visual_and_two_collision_slots_separate(
        int visualIdBytes,
        int collisionIdBytes,
        int expectedBytes,
        int visual0,
        int visual1,
        int collision0,
        int collision1,
        int edge0,
        int edge1)
    {
        var layout = GameBoyWorldPackRuntimeLayout.Create(visualIdBytes, collisionIdBytes);

        Assert.Equal(expectedBytes, layout.TotalBytes);
        Assert.Equal(visual0, layout.VisualSlots[0].Start);
        Assert.Equal(visual1, layout.VisualSlots[1].Start);
        Assert.Equal(collision0, layout.CollisionSlots[0].Start);
        Assert.Equal(collision1, layout.CollisionSlots[1].Start);
        Assert.Equal(edge0, layout.EdgeSlots[0].Start);
        Assert.Equal(edge1, layout.EdgeSlots[1].Start);
        Assert.All(layout.VisualSlots, visual =>
            Assert.All(layout.CollisionSlots, collision => Assert.False(visual.Overlaps(collision))));
    }

    [Fact]
    public void Packed_collision_reference_links_the_worldpack_without_legacy_rows()
    {
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Camera.Init(176, 0, 30);
                i16 footY = 160;
                i16 hitTop = Camera.AabbHitTop(72, footY - 4, 16, 12, 1);
            }
            """;

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Contains(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        Assert.DoesNotContain(result.Report.Segments, segment => segment.Owner == "legacy-world-data:default");
        Assert.InRange(result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackValidateLabel], 0x0150, 0x3FFF);
        Assert.InRange(result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel], 0x0150, 0x3FFF);
        Assert.InRange(result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualLookupLabel], 0x0150, 0x3FFF);
        Assert.InRange(result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionDecodeLabel], 0x0150, 0x3FFF);
        Assert.InRange(result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel], 0x0150, 0x3FFF);
    }

    [Fact]
    public void Collision_lookup_returns_packed_flags_and_never_writes_visual_slots()
    {
        var directory = RepositoryDirectory("samples/runner");
        var mapPath = Path.Combine(directory, "assets/maps/stage1.playable.tmj");
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            mapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var cell = Enumerable.Range(
                0,
                canonical.Pack.Descriptor.HardwareWidth * canonical.Pack.Descriptor.HardwareHeight)
            .First(index => canonical.Pack.CollisionAt(
                index % canonical.Pack.Descriptor.HardwareWidth,
                index / canonical.Pack.Descriptor.HardwareWidth) != WorldTileFlags.Empty);
        var hardwareX = cell % canonical.Pack.Descriptor.HardwareWidth;
        var hardwareY = cell / canonical.Pack.Descriptor.HardwareWidth;
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 4_000));
        var source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                u8 value = 0;
            """ + filler + """

                while (true) {
                    Video.WaitVBlank();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var runtimeLayout = GameBoyWorldPackRuntimeLayout.Create(
            canonical.Pack.Descriptor.VisualIdBytes,
            canonical.Pack.Descriptor.CollisionIdBytes);
        var cpu = new GameBoyTestCpu(result.Rom);
        cpu.SetCurrentRomBank(3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank, 3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank, 9);
        foreach (var slot in runtimeLayout.VisualSlots)
        {
            for (var address = slot.Start; address < slot.EndExclusive; address++)
            {
                cpu.SetWram(address, 0xA5);
            }
        }

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            checked((ushort)hardwareX),
            checked((ushort)hardwareY));
        var firstLookupBankWrites = cpu.RomBankWrites.Count;
        var cachedLookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            checked((ushort)hardwareX),
            checked((ushort)hardwareY));
        var cachedLookupBankWrites = cpu.RomBankWrites.Count - firstLookupBankWrites;

        Assert.Equal(GameBoyWorldPackResult.Success, lookup.Status);
        Assert.Equal((byte)canonical.Pack.CollisionAt(hardwareX, hardwareY), lookup.Value);
        Assert.Equal(lookup, cachedLookup);
        Assert.True(cachedLookupBankWrites < firstLookupBankWrites);
        Assert.Equal(3, cpu.CurrentRomBank);
        Assert.Equal(3, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank));
        Assert.Equal(9, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank));
        Assert.All(runtimeLayout.VisualSlots, slot =>
            Assert.All(Enumerable.Range(slot.Start, slot.Length), address => Assert.Equal(0xA5, cpu.Wram((ushort)address))));
    }

    [Theory]
    [InlineData(false, WorldPackCodec.Raw)]
    [InlineData(true, WorldPackCodec.ElementRle)]
    public void Forced_raw_and_rle_collision_chunks_decode_exactly(bool repeated, WorldPackCodec expectedCodec)
    {
        var fixture = CreateSingleChunkFixture(repeated);
        Assert.Equal(expectedCodec, fixture.Pack.Chunks[0].Directory.CollisionCodec);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new GameBoyTestCpu(result.Rom);
        var runtimeLayout = GameBoyWorldPackRuntimeLayout.Create(1, 1);
        foreach (var slot in runtimeLayout.CollisionSlots)
        {
            for (var address = slot.Start; address < slot.EndExclusive; address++)
            {
                cpu.SetWram(address, 0xA5);
            }
        }

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX: 3,
            hardwareY: 4);

        Assert.True(
            lookup.Status == GameBoyWorldPackResult.Success,
            $"Collision lookup failed with {lookup.Status}; cell={cpu.Wram(0xC1F5)}, id={cpu.Wram(0xC1F2):X2}{cpu.Wram(0xC1F1):X2}, stored={cpu.Wram(0xC1F6)}, codec={cpu.Wram(0xC1F0)}.");
        Assert.Equal((byte)fixture.Pack.CollisionAt(3, 4), lookup.Value);
        if (expectedCodec == WorldPackCodec.Raw)
        {
            Assert.All(runtimeLayout.CollisionSlots, slot =>
                Assert.All(Enumerable.Range(slot.Start, slot.Length), address => Assert.Equal(0xA5, cpu.Wram((ushort)address))));
            Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow));
        }
        else
        {
            Assert.Equal(
                fixture.Pack.Chunks[0].CollisionProfileIds.Select(id => (byte)id),
                Enumerable.Range(0, 64).Select(offset => cpu.Wram((ushort)(runtimeLayout.CollisionSlots[0].Start + offset))));
            Assert.Equal(1, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow));
        }
    }

    [Theory]
    [InlineData(false, WorldPackCodec.Raw)]
    [InlineData(true, WorldPackCodec.ElementRle)]
    public void Two_byte_collision_lookup_crosses_an_mbc1_window_for_raw_and_rle(
        bool repeated,
        WorldPackCodec expectedCodec)
    {
        var fixture = CreateTwoByteCrossBankCollisionFixture(repeated);
        Assert.Equal(expectedCodec, fixture.Pack.Chunks[0].Directory.CollisionCodec);
        var targetCellIndex = repeated ? 2 : 49;
        Assert.Equal((ushort)256, fixture.Pack.Chunks[0].CollisionProfileIds[targetCellIndex]);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegments = result.Report.Segments.Where(segment => segment.Owner == "worldpack:default").ToArray();
        Assert.NotEmpty(packSegments);
        var encodedIdOffset = fixture.Pack.Chunks[0].Directory.CollisionOffset
            + (expectedCodec == WorldPackCodec.ElementRle ? 5u : 49u * 2u);
        var firstIdPhysicalAddress = packSegments[0].PhysicalStart + checked((int)encodedIdOffset);
        Assert.Equal(0x7FFF, firstIdPhysicalAddress);
        var cpu = new GameBoyTestCpu(result.Rom);
        var validationStatus = cpu.RunWorldPackDecode(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel],
            chunkIndex: 0,
            slot: 0);
        Assert.Equal(GameBoyWorldPackResult.Success, validationStatus);
        var bankWritesBeforeLookup = cpu.RomBankWrites.Count;
        var hardwareX = repeated ? (ushort)6 : (ushort)3;
        var hardwareY = repeated ? (ushort)0 : (ushort)6;

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX,
            hardwareY);

        Assert.Equal(GameBoyWorldPackResult.Success, lookup.Status);
        Assert.Equal((byte)fixture.Pack.CollisionAt(hardwareX, hardwareY), lookup.Value);
        Assert.Equal(
            expectedCodec == WorldPackCodec.ElementRle ? 1 : 0,
            cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow));
        Assert.Contains(
            cpu.RomBankWrites.Skip(bankWritesBeforeLookup),
            write => write.SelectedBank == 2);
    }

    [Fact]
    public void Repeated_rle_collision_queries_in_one_chunk_decode_once()
    {
        const ushort collisionDecodeCountLow = 0xC204;
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new GameBoyTestCpu(result.Rom);
        var entry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel];

        var first = cpu.RunWorldPackCollisionLookup(entry, hardwareX: 0, hardwareY: 0);
        var second = cpu.RunWorldPackCollisionLookup(entry, hardwareX: 7, hardwareY: 7);

        Assert.Equal(GameBoyWorldPackResult.Success, first.Status);
        Assert.Equal(GameBoyWorldPackResult.Success, second.Status);
        Assert.Equal(1, cpu.Wram(collisionDecodeCountLow) | cpu.Wram(collisionDecodeCountLow + 1) << 8);
    }

    [Fact]
    public void Rle_collision_cache_retains_two_chunks_and_replaces_them_round_robin()
    {
        var fixture = CreateThreeChunkRleFixture();
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new GameBoyTestCpu(result.Rom);
        var entry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel];

        AssertLookup(0, 0, WorldTileFlags.Solid, expectedDecodes: 1);
        AssertLookup(8, 0, WorldTileFlags.Hazard, expectedDecodes: 2);
        AssertLookup(1, 1, WorldTileFlags.Solid, expectedDecodes: 2);
        AssertLookup(16, 0, WorldTileFlags.Platform, expectedDecodes: 3);
        AssertLookup(0, 0, WorldTileFlags.Solid, expectedDecodes: 4);
        AssertLookup(9, 1, WorldTileFlags.Hazard, expectedDecodes: 5);

        void AssertLookup(ushort x, ushort y, WorldTileFlags expected, int expectedDecodes)
        {
            var lookup = cpu.RunWorldPackCollisionLookup(entry, x, y);
            Assert.Equal(GameBoyWorldPackResult.Success, lookup.Status);
            Assert.Equal((byte)expected, lookup.Value);
            Assert.Equal(
                expectedDecodes,
                cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow)
                | cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountHigh) << 8);
        }
    }

    [Fact]
    public void Malformed_rle_replacement_never_exposes_the_replaced_slot_under_its_old_tag()
    {
        var fixture = CreateThreeChunkRleFixture();
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var rom = result.Rom.ToArray();
        var cpu = new GameBoyTestCpu(rom);
        var entry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel];

        Assert.Equal(GameBoyWorldPackResult.Success, cpu.RunWorldPackCollisionLookup(entry, 0, 0).Status);
        Assert.Equal(GameBoyWorldPackResult.Success, cpu.RunWorldPackCollisionLookup(entry, 8, 0).Status);
        rom[packSegment.PhysicalStart + checked((int)fixture.Pack.Chunks[2].Directory.CollisionOffset)] = 0xFF;
        Assert.Equal(GameBoyWorldPackResult.Malformed, cpu.RunWorldPackCollisionLookup(entry, 16, 0).Status);

        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid));
        Assert.Equal(1, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid));
        var replacedChunk = cpu.RunWorldPackCollisionLookup(entry, 0, 0);
        Assert.Equal(GameBoyWorldPackResult.Success, replacedChunk.Status);
        Assert.Equal((byte)WorldTileFlags.Solid, replacedChunk.Value);
        Assert.Equal(4, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow));
    }

    [Fact]
    public void Explicit_collision_decode_invalidates_lookup_slot_tags()
    {
        var fixture = CreateThreeChunkRleFixture();
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new GameBoyTestCpu(result.Rom);
        var lookupEntry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel];
        var decodeEntry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionDecodeLabel];

        Assert.Equal(GameBoyWorldPackResult.Success, cpu.RunWorldPackCollisionLookup(lookupEntry, 0, 0).Status);
        Assert.Equal(1, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid));

        Assert.Equal(GameBoyWorldPackResult.Success, cpu.RunWorldPackDecode(decodeEntry, chunkIndex: 2, slot: 0));

        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid));
        Assert.Equal(GameBoyWorldPackResult.Success, cpu.RunWorldPackCollisionLookup(lookupEntry, 1, 0).Status);
        Assert.Equal(2, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.DecodeCountLow));
    }

    [Fact]
    public void Complete_stage1_cached_collision_hit_fits_the_runner_frame_budget()
    {
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            Path.Combine(RunnerSample.Directory, "assets/maps/stage1.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cpu = new GameBoyTestCpu(result.Rom);
        var entry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel];

        var start = cpu.Cycles;
        var first = cpu.RunWorldPackCollisionLookup(entry, hardwareX: 9, hardwareY: 38);
        var firstCycles = cpu.Cycles - start;
        start = cpu.Cycles;
        var cached = cpu.RunWorldPackCollisionLookup(entry, hardwareX: 10, hardwareY: 38);
        var cachedCycles = cpu.Cycles - start;
        start = cpu.Cycles;
        var repeatedCell = cpu.RunWorldPackCollisionLookup(entry, hardwareX: 10, hardwareY: 38);
        var repeatedCellCycles = cpu.Cycles - start;
        start = cpu.Cycles;
        var memoTableHit = cpu.RunWorldPackCollisionLookup(entry, hardwareX: 9, hardwareY: 38);
        var memoTableHitCycles = cpu.Cycles - start;

        Assert.Equal(GameBoyWorldPackResult.Success, first.Status);
        Assert.Equal(GameBoyWorldPackResult.Success, cached.Status);
        Assert.Equal(GameBoyWorldPackResult.Success, memoTableHit.Status);
        Assert.Equal((byte)canonical.Pack.CollisionAt(9, 38), first.Value);
        Assert.Equal((byte)canonical.Pack.CollisionAt(10, 38), cached.Value);
        for (var hardwareY = 35; hardwareY < 40; hardwareY++)
        {
            for (var hardwareX = 0; hardwareX < 24; hardwareX++)
            {
                var lookup = cpu.RunWorldPackCollisionLookup(
                    entry,
                    checked((ushort)hardwareX),
                    checked((ushort)hardwareY));
                Assert.True(
                    lookup.Status == GameBoyWorldPackResult.Success
                    && lookup.Value == (byte)canonical.Pack.CollisionAt(hardwareX, hardwareY),
                    $"Packed collision mismatch at ({hardwareX},{hardwareY}): status={lookup.Status}, actual={lookup.Value}, expected={(byte)canonical.Pack.CollisionAt(hardwareX, hardwareY)}.");
            }
        }
        Assert.True(
            firstCycles <= GameBoyTestCpu.DmgCyclesPerFrame,
            $"A cold complete-stage collision chunk must decode within one DMG frame; measured {firstCycles} cycles.");
        Assert.True(
            cachedCycles <= 2_300,
            $"A resident complete-stage collision lookup must fit the runner frame budget; measured cached={cachedCycles}, repeated={repeatedCellCycles}, memo={memoTableHitCycles} cycles.");
        Assert.True(
            repeatedCellCycles <= 300,
            $"An exact repeated collision cell must leave frame slack; measured {repeatedCellCycles} cycles.");
        Assert.True(
            memoTableHitCycles <= 900,
            $"A recently observed collision cell must hit the bounded memo table; measured {memoTableHitCycles} cycles.");
    }

    [Fact]
    public void Malformed_header_fails_before_a_collision_slot_becomes_visible()
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { u8 marker = 90; }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = result.Report.Segments
            .Where(segment => segment.Owner == "worldpack:default")
            .OrderBy(segment => segment.PhysicalStart)
            .First();
        var rom = result.Rom.ToArray();
        rom[packSegment.PhysicalStart] = (byte)'X';
        var cpu = new GameBoyTestCpu(rom);
        var collisionSlot = GameBoyWorldPackRuntimeLayout.Create(1, 1).CollisionSlots[0];
        for (var address = collisionSlot.Start; address < collisionSlot.EndExclusive; address++)
        {
            cpu.SetWram(address, 0xA5);
        }

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX: 0,
            hardwareY: 0);

        Assert.Equal(GameBoyWorldPackResult.Malformed, lookup.Status);
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.CellValid));
        Assert.All(
            Enumerable.Range(collisionSlot.Start, collisionSlot.Length),
            address => Assert.Equal(0xA5, cpu.Wram((ushort)address)));
        var startupCpu = new GameBoyTestCpu(rom);
        startupCpu.RunFrames(20);
        Assert.Equal(0, startupCpu.Wram(0xC000));
        Assert.Equal(2, startupCpu.Wram(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState));
    }

    [Theory]
    [InlineData(19, 2)]
    [InlineData(12, 0xFF)]
    public void Malformed_directory_fails_before_a_collision_slot_becomes_visible(int entryOffset, byte value)
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var rom = result.Rom.ToArray();
        rom[packSegment.PhysicalStart + checked((int)fixture.Pack.Descriptor.DirectoryOffset) + entryOffset] = value;
        var cpu = new GameBoyTestCpu(rom);
        var collisionSlot = GameBoyWorldPackRuntimeLayout.Create(1, 1).CollisionSlots[0];
        for (var address = collisionSlot.Start; address < collisionSlot.EndExclusive; address++)
        {
            cpu.SetWram(address, 0xA5);
        }

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX: 0,
            hardwareY: 0);

        Assert.Equal(GameBoyWorldPackResult.Malformed, lookup.Status);
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.CellValid));
        Assert.All(
            Enumerable.Range(collisionSlot.Start, collisionSlot.Length),
            address => Assert.Equal(0xA5, cpu.Wram((ushort)address)));
    }

    [Fact]
    public void Malformed_rle_overrun_fails_before_a_collision_slot_becomes_visible()
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var rom = result.Rom.ToArray();
        rom[packSegment.PhysicalStart + checked((int)fixture.Pack.Chunks[0].Directory.CollisionOffset)] = 0xFF;
        var cpu = new GameBoyTestCpu(rom);
        var collisionSlot = GameBoyWorldPackRuntimeLayout.Create(1, 1).CollisionSlots[0];
        for (var address = collisionSlot.Start; address < collisionSlot.EndExclusive; address++)
        {
            cpu.SetWram(address, 0xA5);
        }

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX: 0,
            hardwareY: 0);

        Assert.Equal(GameBoyWorldPackResult.Malformed, lookup.Status);
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache0Valid));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.Cache1Valid));
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.Collision.CellValid));
        Assert.All(
            Enumerable.Range(collisionSlot.Start, collisionSlot.Length),
            address => Assert.Equal(0xA5, cpu.Wram((ushort)address)));
    }

    [Fact]
    public void Whole_pack_validation_rejects_compensating_mutations_that_preserve_the_legacy_word_sum()
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var rom = result.Rom.ToArray();
        var packStart = packSegment.PhysicalStart;
        var malformedOffset = checked((int)fixture.Pack.Chunks[0].Directory.CollisionOffset);
        var malformedWordOffset = malformedOffset & ~1;
        var originalMalformedWord = ReadWord(rom, packStart + malformedWordOffset);
        rom[packStart + malformedOffset] = 0xFF;
        var malformedWord = ReadWord(rom, packStart + malformedWordOffset);
        var delta = unchecked((ushort)(malformedWord - originalMalformedWord));

        const int compensationWordOffset = 4;
        var originalCompensationWord = ReadWord(rom, packStart + compensationWordOffset);
        WriteWord(
            rom,
            packStart + compensationWordOffset,
            unchecked((ushort)(originalCompensationWord - delta)));

        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(20);

        Assert.Equal(2, cpu.Wram(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState));

        static ushort ReadWord(byte[] bytes, int offset) =>
            (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

        static void WriteWord(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }
    }

    [Fact]
    public void Whole_pack_validation_rejects_same_lane_compensation_across_header_fields()
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var rom = result.Rom.ToArray();

        rom[packSegment.PhysicalStart + 5] ^= 0x80; // Invalid minor version.
        rom[packSegment.PhysicalStart + 9] ^= 0x80; // Invalid 0x8028 hardware width.

        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(20);

        Assert.Equal(2, cpu.Wram(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState));
    }

    [Fact]
    public void Staged_raw_decode_rejects_compensated_invalid_ids_before_cache_publication()
    {
        var directory = RepositoryDirectory("samples/tiled-vscroll");
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            Path.Combine(directory, "vscroll.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        Assert.Equal(1, canonical.Pack.Descriptor.VisualIdBytes);
        var rawChunkIndex = Enumerable.Range(0, canonical.Pack.Chunks.Count)
            .First(index => canonical.Pack.Chunks[index].Directory.VisualCodec == WorldPackCodec.Raw);
        var rawChunk = canonical.Pack.Chunks[rawChunkIndex];
        var originalFingerprint = Fingerprint(canonical.SerializedBytes);
        (int First, int Second)? compensation = null;
        for (var firstCell = 1; firstCell < rawChunk.VisualIds.Count && compensation is null; firstCell++)
        {
            for (var secondCell = firstCell + 1; secondCell < rawChunk.VisualIds.Count; secondCell++)
            {
                var first = checked((int)rawChunk.Directory.VisualOffset + firstCell);
                var second = checked((int)rawChunk.Directory.VisualOffset + secondCell);
                if ((canonical.SerializedBytes[first] ^ 0x80) < canonical.Pack.Descriptor.VisualMetatileCount
                    || (canonical.SerializedBytes[second] ^ 0x80) < canonical.Pack.Descriptor.VisualMetatileCount)
                {
                    continue;
                }

                var mutated = canonical.SerializedBytes.ToArray();
                mutated[first] ^= 0x80;
                mutated[second] ^= 0x80;
                if (Fingerprint(mutated).SequenceEqual(originalFingerprint))
                {
                    compensation = (first, second);
                    break;
                }
            }
        }

        Assert.True(compensation.HasValue, "Expected a same-lane payload compensation in a raw visual chunk.");
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            File.ReadAllText(Path.Combine(directory, "vscroll.rs")),
            directory);
        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var rom = result.Rom.ToArray();
        rom[packSegment.PhysicalStart + compensation.Value.First] ^= 0x80;
        rom[packSegment.PhysicalStart + compensation.Value.Second] ^= 0x80;

        var chunkX = rawChunkIndex % canonical.Pack.Descriptor.ChunkColumns;
        var chunkY = rawChunkIndex / canonical.Pack.Descriptor.ChunkColumns;
        var hardwareX = checked((ushort)(chunkX * WorldPackDescriptor.V1ChunkWidth * canonical.Pack.Descriptor.MetatileWidth));
        var hardwareY = checked((ushort)(chunkY * WorldPackDescriptor.V1ChunkHeight * canonical.Pack.Descriptor.MetatileHeight));

        var cpu = new GameBoyTestCpu(rom);
        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualLookupLabel],
            hardwareX,
            hardwareY);

        Assert.Equal(1, cpu.Wram(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState));
        Assert.Equal(GameBoyWorldPackResult.Malformed, lookup.Status);
        Assert.Equal(0, cpu.Wram(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheValid));

        static byte[] Fingerprint(ReadOnlySpan<byte> bytes)
        {
            byte[] lanes = [0x3D, 0xA7, 0x5B, 0xC1];
            for (var index = 0; index < bytes.Length; index++)
            {
                var lane = index & 3;
                var sum = unchecked((byte)(lanes[lane] + bytes[index]));
                lanes[lane] = (index >> 2 & 3) == lane
                    ? (byte)((sum << 1) | (sum >> 7))
                    : sum;
            }

            return lanes;
        }
    }

    [Fact]
    public void Validation_rejects_a_malformed_unrequested_plane_before_any_slot_becomes_visible()
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var rom = result.Rom.ToArray();
        rom[packSegment.PhysicalStart + checked((int)fixture.Pack.Chunks[0].Directory.VisualOffset)] = 0xFF;
        var cpu = new GameBoyTestCpu(rom);
        var layout = GameBoyWorldPackRuntimeLayout.Create(1, 1);
        foreach (var slot in layout.VisualSlots.Concat(layout.CollisionSlots))
        {
            for (var address = slot.Start; address < slot.EndExclusive; address++)
            {
                cpu.SetWram(address, 0xA5);
            }
        }

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX: 0,
            hardwareY: 0);

        Assert.Equal(GameBoyWorldPackResult.Malformed, lookup.Status);
        Assert.All(
            layout.VisualSlots.Concat(layout.CollisionSlots),
            slot => Assert.All(
                Enumerable.Range(slot.Start, slot.Length),
                address => Assert.Equal(0xA5, cpu.Wram((ushort)address))));
    }

    [Fact]
    public void Collision_decode_distinguishes_success_miss_bounds_and_malformed()
    {
        var fixture = CreateSingleChunkFixture(repeated: false);
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 4_000));
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { u8 value = 0;\n" + filler + "\n }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var entry = result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionDecodeLabel];

        RunAndAssertRestored(result.Rom, 0, 0, GameBoyWorldPackResult.Success);
        RunAndAssertRestored(result.Rom, 1, 0, GameBoyWorldPackResult.Miss);
        RunAndAssertRestored(result.Rom, 0, 2, GameBoyWorldPackResult.BoundsError);

        var packSegment = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var malformedRom = result.Rom.ToArray();
        malformedRom[
            packSegment.PhysicalStart
            + checked((int)fixture.Pack.Chunks[0].Directory.CollisionOffset)] = 2;
        RunAndAssertRestored(malformedRom, 0, 0, GameBoyWorldPackResult.Malformed);

        void RunAndAssertRestored(byte[] rom, ushort chunkIndex, byte slot, GameBoyWorldPackResult expected)
        {
            var cpu = new GameBoyTestCpu(rom);
            cpu.SetCurrentRomBank(3);
            cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank, 3);
            cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank, 9);

            Assert.Equal(expected, cpu.RunWorldPackDecode(entry, chunkIndex, slot));
            Assert.Equal(3, cpu.CurrentRomBank);
            Assert.Equal(3, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank));
            Assert.Equal(9, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank));
            Assert.All(cpu.RomBankWrites, write => Assert.InRange(write.ProgramCounter, 0x0150, 0x3FFF));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Visual_decode_writes_only_the_selected_visual_slot(bool repeated)
    {
        var fixture = CreateSingleChunkFixture(repeated);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var layout = GameBoyWorldPackRuntimeLayout.Create(1, 1);
        var cpu = new GameBoyTestCpu(result.Rom);
        foreach (var slot in layout.CollisionSlots.Append(layout.VisualSlots[0]))
        {
            for (var address = slot.Start; address < slot.EndExclusive; address++)
            {
                cpu.SetWram(address, 0xA5);
            }
        }

        var status = cpu.RunWorldPackDecode(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel],
            chunkIndex: 0,
            slot: 1);

        Assert.Equal(GameBoyWorldPackResult.Success, status);
        Assert.Equal(
            fixture.Pack.Chunks[0].VisualIds.Select(id => (byte)id),
            Enumerable.Range(layout.VisualSlots[1].Start, layout.VisualSlots[1].Length)
                .Select(address => cpu.Wram((ushort)address)));
        Assert.All(
            layout.CollisionSlots.Append(layout.VisualSlots[0]),
            slot => Assert.All(
                Enumerable.Range(slot.Start, slot.Length),
                address => Assert.Equal(0xA5, cpu.Wram((ushort)address))));
    }

    [Fact]
    public void Visual_decode_crosses_an_mbc1_window_and_restores_the_entry_bank_lifo()
    {
        var fixture = CreateCrossBankFixture();
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 4_000));
        var source = """
            void Main() {
                u8 value = 0;
            """ + filler + """

            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var layout = GameBoyWorldPackRuntimeLayout.Create(2, 1);
        var cpu = new GameBoyTestCpu(result.Rom);
        cpu.SetCurrentRomBank(3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank, 3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank, 9);

        var status = cpu.RunWorldPackDecode(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel],
            chunkIndex: 0,
            slot: 0);

        Assert.Equal(GameBoyWorldPackResult.Success, status);
        Assert.Equal(3, cpu.CurrentRomBank);
        Assert.Equal(3, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank));
        Assert.Equal(9, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank));
        Assert.Equal(
            fixture.Pack.Chunks[0].VisualIds.SelectMany(id => new[] { (byte)id, (byte)(id >> 8) }),
            Enumerable.Range(layout.VisualSlots[0].Start, layout.VisualSlots[0].Length)
                .Select(address => cpu.Wram((ushort)address)));
        Assert.Contains(
            cpu.RomBankWrites.Zip(cpu.RomBankWrites.Skip(1)),
            pair => pair.Second.SelectedBank == pair.First.SelectedBank + 1);
        Assert.Equal(3, cpu.RomBankWrites[^1].SelectedBank);
        Assert.All(cpu.RomBankWrites, write => Assert.InRange(write.ProgramCounter, 0x0150, 0x3FFF));
    }

    [Fact]
    public void Nested_fixed_bank_read_restores_the_outer_worldpack_bank_then_the_entry_bank()
    {
        var fixture = CreateCrossBankFixture();
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 4_000));
        var source = "void Main() { u8 value = 0;\n" + filler + "\n }";
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var packSegment = result.Report.Segments
            .Where(segment => segment.Owner == "worldpack:default")
            .OrderBy(segment => segment.PhysicalStart)
            .First();
        var outerBank = checked((byte)packSegment.Bank);
        var nestedBank = checked((byte)(outerBank == 1 ? 2 : 1));
        var cpu = new GameBoyTestCpu(result.Rom);
        cpu.SetCurrentRomBank(3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank, 3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank, 9);
        cpu.InjectFarReadAfterSelecting(
            outerBank,
            result.Report.FixedSymbols[GameBoyRomBuilder.Mbc1FarReadByteLabel],
            nestedBank,
            0x4000);

        var status = cpu.RunWorldPackDecode(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel],
            chunkIndex: 0,
            slot: 0);

        Assert.Equal(GameBoyWorldPackResult.Success, status);
        Assert.Equal(result.Rom[nestedBank * 0x4000], Assert.Single(cpu.InjectedFarReadResults).Data);
        Assert.Equal(3, cpu.CurrentRomBank);
        Assert.Equal(3, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank));
        Assert.Equal(9, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank));
        Assert.Contains(
            cpu.RomBankWrites.Zip(cpu.RomBankWrites.Skip(1)),
            pair => pair.First.SelectedBank == nestedBank && pair.Second.SelectedBank == outerBank);
        Assert.All(cpu.RomBankWrites, write => Assert.InRange(write.ProgramCounter, 0x0150, 0x3FFF));
    }

    [Fact]
    public void Two_byte_visual_and_collision_ids_use_the_declared_554_byte_maximum()
    {
        var fixture = CreateTwoByteFixture();
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var layout = GameBoyWorldPackRuntimeLayout.Create(2, 2);
        var cpu = new GameBoyTestCpu(result.Rom);

        var visualStatus = cpu.RunWorldPackDecode(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel],
            chunkIndex: 0,
            slot: 0);
        var collisionStatus = cpu.RunWorldPackDecode(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionDecodeLabel],
            chunkIndex: 0,
            slot: 1);

        Assert.Equal(554, layout.TotalBytes);
        Assert.Equal(GameBoyWorldPackResult.Success, visualStatus);
        Assert.Equal(GameBoyWorldPackResult.Success, collisionStatus);
        Assert.Equal(
            fixture.Pack.Chunks[0].VisualIds.SelectMany(id => new[] { (byte)id, (byte)(id >> 8) }),
            Enumerable.Range(layout.VisualSlots[0].Start, layout.VisualSlots[0].Length)
                .Select(address => cpu.Wram((ushort)address)));
        Assert.Equal(
            fixture.Pack.Chunks[0].CollisionProfileIds.SelectMany(id => new[] { (byte)id, (byte)(id >> 8) }),
            Enumerable.Range(layout.CollisionSlots[1].Start, layout.CollisionSlots[1].Length)
                .Select(address => cpu.Wram((ushort)address)));
    }

    [Fact]
    public void Visual_lookup_expands_the_visual_id_without_decoding_collision()
    {
        var fixture = CreateSingleChunkFixture(repeated: true);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var layout = GameBoyWorldPackRuntimeLayout.Create(1, 1);
        var cpu = new GameBoyTestCpu(result.Rom);
        foreach (var slot in layout.CollisionSlots)
        {
            for (var address = slot.Start; address < slot.EndExclusive; address++)
            {
                cpu.SetWram(address, 0xA5);
            }
        }

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualLookupLabel],
            hardwareX: 3,
            hardwareY: 4);

        Assert.Equal(GameBoyWorldPackResult.Success, lookup.Status);
        Assert.Equal(20, lookup.Value);
        Assert.All(
            layout.CollisionSlots,
            slot => Assert.All(
                Enumerable.Range(slot.Start, slot.Length),
                address => Assert.Equal(0xA5, cpu.Wram((ushort)address))));
    }

    [Theory]
    [InlineData(3, 3, 5, 5)]
    [InlineData(17, 16, 33, 31)]
    public void Lookup_preserves_complete_v1_metatile_coordinates(
        int metatileWidth,
        int metatileHeight,
        ushort hardwareX,
        ushort hardwareY)
    {
        var fixture = CreateMetatileFixture(metatileWidth, metatileHeight);
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new GameBoyTestCpu(result.Rom);

        var visual = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualLookupLabel],
            hardwareX,
            hardwareY);
        var collision = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX,
            hardwareY);

        Assert.Equal(new WorldPackLookupResult(77, GameBoyWorldPackResult.Success), visual);
        Assert.Equal(new WorldPackLookupResult((byte)WorldTileFlags.Hazard, GameBoyWorldPackResult.Success), collision);
    }

    [Fact]
    public void Visual_lookup_keeps_the_high_pack_offset_for_a_two_byte_id()
    {
        var fixture = CreateHighVisualIdFixture();
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            "void Main() { }",
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new GameBoyTestCpu(result.Rom);

        var visual = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualLookupLabel],
            hardwareX: 1,
            hardwareY: 0);

        Assert.Equal(new WorldPackLookupResult(77, GameBoyWorldPackResult.Success), visual);
        Assert.True(result.Report.Segments.Count(segment => segment.Owner == "worldpack:default") >= 4);
    }

    [Fact]
    public void Packed_collision_hit_top_returns_y_304_and_ffff_for_no_hit()
    {
        var fixture = CreateTallCollisionFixture();
        var visual = string.Join(", ", Enumerable.Repeat("0", 40));
        var flags = string.Join(", ", Enumerable.Range(0, 40).Select(row => row == 38 ? "1" : "0"));
        var source = $$"""
            void Main() {
                World.Column(0, {{visual}});
                World.Flags(0, {{flags}});
                World.Map(1, 0, 40);
                Camera.Init(1, 0, 32);
                i16 hitTop = Camera.AabbHitTop(0, 300, 8, 12, 1);
                i16 noHit = Camera.AabbHitTop(0, 300, 8, 12, 4);
                while (true) {
                    Video.WaitVBlank();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: fixture.SerializedBytes);
        var cpu = new GameBoyTestCpu(result.Rom);

        var packedLookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            0,
            38);
        Assert.Equal(GameBoyWorldPackResult.Success, packedLookup.Status);
        Assert.Equal((byte)WorldTileFlags.Solid, packedLookup.Value);
        cpu.RunFrames(20);

        Assert.Contains(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        Assert.DoesNotContain(result.Report.Segments, segment => segment.Owner == "legacy-world-data:default");
        Assert.Equal(
            new byte[] { 0x30, 0x01, 0xFF, 0xFF },
            Enumerable.Range(0xC000, 4).Select(address => cpu.Wram((ushort)address)).ToArray());
    }

    [Fact]
    public void Packed_read_restores_the_live_audio_bank_and_audio_cadence_continues()
    {
        var directory = RepositoryDirectory("samples/runner");
        var mapPath = Path.Combine(directory, "assets/maps/stage1.playable.tmj");
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            mapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var collisionCell = Enumerable.Range(
                0,
                canonical.Pack.Descriptor.HardwareWidth * canonical.Pack.Descriptor.HardwareHeight)
            .First(index => canonical.Pack.CollisionAt(
                index % canonical.Pack.Descriptor.HardwareWidth,
                index / canonical.Pack.Descriptor.HardwareWidth) != WorldTileFlags.Empty);
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 4_000));
        var source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Music.Asset(theme, "assets/music/runner.vgz");
                Sfx.Asset(jump, "assets/sfx/smb-jump.gb.vgm");
                u8 value = 0;
            """ + filler + """

                Audio.Init();
                Music.Play(theme);
                Sfx.Play(jump);
                while (true) {
                    Video.WaitVBlank();
                    Audio.Update();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        Assert.Equal(11_614, result.Report.Segments.Where(segment => segment.Owner == "bgm:theme").Sum(segment => segment.Length));
        Assert.Equal(28, result.Report.Segments.Where(segment => segment.Owner == "sfx:jump").Sum(segment => segment.Length));
        var cpu = new GameBoyTestCpu(result.Rom) { CycleAccurateLy = true };
        cpu.RunFrames(30);
        var updatesBefore = cpu.AudioUpdateCalls;
        var apuWritesBefore = cpu.ApuWrites.Count;
        var entryBank = cpu.CurrentRomBank;
        var entryShadow = cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
        var programShadow = cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
        var bankWritesBeforeLookup = cpu.RomBankWrites.Count;

        var lookup = cpu.RunWorldPackCollisionLookup(
            result.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            checked((ushort)(collisionCell % canonical.Pack.Descriptor.HardwareWidth)),
            checked((ushort)(collisionCell / canonical.Pack.Descriptor.HardwareWidth)));

        Assert.Equal(GameBoyWorldPackResult.Success, lookup.Status);
        Assert.Equal(entryBank, cpu.CurrentRomBank);
        Assert.Equal(entryShadow, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank));
        Assert.Equal(programShadow, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank));
        Assert.All(
            cpu.RomBankWrites.Skip(bankWritesBeforeLookup),
            write => Assert.InRange(write.ProgramCounter, 0x0150, 0x3FFF));
        cpu.RunFrames(90);
        Assert.InRange(updatesBefore, 10, 25);
        Assert.InRange(cpu.AudioUpdateCalls - updatesBefore, 50, 70);
        Assert.True(cpu.ApuWrites.Count > apuWritesBefore);
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
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
        const uint directoryOffset = targetExpansionsOffset + 2;
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
            TargetCellStride = 1,
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
            new byte[] { 10, 20 },
            [new WorldPackChunk(directory, ids, ids)]);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateThreeChunkRleFixture()
    {
        const int chunkCount = 3;
        const ushort storedBytes = 2;
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + 4;
        const uint directoryOffset = targetExpansionsOffset + 4;
        const uint chunkDataOffset = directoryOffset + chunkCount * WorldPackDescriptor.V1DirectoryEntryBytes;
        var chunks = new List<WorldPackChunk>(chunkCount);
        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var visualOffset = chunkDataOffset + checked((uint)(chunk * storedBytes * 2));
            var collisionOffset = visualOffset + storedBytes;
            var ids = Enumerable.Repeat(checked((ushort)(chunk + 1)), 64).ToArray();
            chunks.Add(new WorldPackChunk(
                new WorldPackChunkDirectoryEntry(
                    visualOffset,
                    storedBytes,
                    64,
                    collisionOffset,
                    storedBytes,
                    64,
                    8,
                    8,
                    WorldPackCodec.ElementRle,
                    WorldPackCodec.ElementRle),
                ids,
                ids));
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 24,
            HardwareHeight = 8,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = chunkCount,
            ChunkRows = 1,
            VisualMetatileCount = 4,
            CollisionProfileCount = 4,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 1,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = chunkDataOffset + checked((uint)(chunkCount * storedBytes * 2)),
        };
        var pack = new WorldPack(
            descriptor,
            [
                new WorldPackCollisionProfile([WorldTileFlags.Empty]),
                new WorldPackCollisionProfile([WorldTileFlags.Solid]),
                new WorldPackCollisionProfile([WorldTileFlags.Hazard]),
                new WorldPackCollisionProfile([WorldTileFlags.Platform]),
            ],
            new byte[] { 0, 1, 2, 3 },
            chunks);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateCrossBankFixture()
    {
        const int visualCount = 4_071;
        const int metatileCells = 4;
        var visualIds = Enumerable.Range(0, 64).Select(index => (ushort)index).ToArray();
        var collisionIds = Enumerable.Range(0, 64).Select(index => (ushort)(index & 1)).ToArray();
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + 2 * metatileCells;
        const uint directoryOffset = targetExpansionsOffset + visualCount * metatileCells;
        const uint chunkDataOffset = directoryOffset + WorldPackDescriptor.V1DirectoryEntryBytes;
        const ushort visualStoredBytes = 128;
        const ushort collisionStoredBytes = 64;
        var collisionOffset = chunkDataOffset + visualStoredBytes;
        var packLength = collisionOffset + collisionStoredBytes;
        var directory = new WorldPackChunkDirectoryEntry(
            chunkDataOffset,
            visualStoredBytes,
            visualStoredBytes,
            collisionOffset,
            collisionStoredBytes,
            collisionStoredBytes,
            8,
            8,
            WorldPackCodec.Raw,
            WorldPackCodec.Raw);
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 16,
            HardwareHeight = 16,
            MetatileWidth = 2,
            MetatileHeight = 2,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = visualCount,
            CollisionProfileCount = 2,
            VisualIdBytes = 2,
            CollisionIdBytes = 1,
            TargetCellStride = 1,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = packLength,
        };
        var pack = new WorldPack(
            descriptor,
            [
                new WorldPackCollisionProfile(Enumerable.Repeat(WorldTileFlags.Empty, metatileCells)),
                new WorldPackCollisionProfile(Enumerable.Repeat(WorldTileFlags.Solid, metatileCells)),
            ],
            new byte[visualCount * metatileCells],
            [new WorldPackChunk(directory, visualIds, collisionIds)]);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateTwoByteFixture()
    {
        const int idCount = 257;
        const int metatileCells = 4;
        var ids = Enumerable.Range(0, 64).Select(index => (ushort)index).ToArray();
        var profiles = Enumerable.Range(0, idCount)
            .Select(index => new WorldPackCollisionProfile(
                Enumerable.Range(0, metatileCells)
                    .Select(digit => (WorldTileFlags)((index / (int)Math.Pow(8, metatileCells - digit - 1)) % 8))))
            .ToArray();
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + idCount * metatileCells;
        const uint directoryOffset = targetExpansionsOffset + idCount * metatileCells;
        const uint chunkDataOffset = directoryOffset + WorldPackDescriptor.V1DirectoryEntryBytes;
        const ushort storedBytes = 128;
        var collisionOffset = chunkDataOffset + storedBytes;
        var packLength = collisionOffset + storedBytes;
        var directory = new WorldPackChunkDirectoryEntry(
            chunkDataOffset,
            storedBytes,
            storedBytes,
            collisionOffset,
            storedBytes,
            storedBytes,
            8,
            8,
            WorldPackCodec.Raw,
            WorldPackCodec.Raw);
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 16,
            HardwareHeight = 16,
            MetatileWidth = 2,
            MetatileHeight = 2,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = idCount,
            CollisionProfileCount = idCount,
            VisualIdBytes = 2,
            CollisionIdBytes = 2,
            TargetCellStride = 1,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = packLength,
        };
        var pack = new WorldPack(
            descriptor,
            profiles,
            new byte[idCount * metatileCells],
            [new WorldPackChunk(directory, ids, ids)]);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateTwoByteCrossBankCollisionFixture(bool repeated)
    {
        const int idCount = 257;
        const int metatileCells = 3;
        const int chunkCount = 64;
        var codec = repeated ? WorldPackCodec.ElementRle : WorldPackCodec.Raw;
        var collisionStoredBytes = repeated ? 10 : 128;
        const int visualStoredBytes = 128;
        var visualCount = repeated ? 4_717 : 4_686;
        var visualIds = Enumerable.Range(0, 64).Select(index => (ushort)(index & 1)).ToArray();
        var ids = repeated
            ? new ushort[] { 1, 2, 256 }.Concat(Enumerable.Repeat((ushort)3, 61)).ToArray()
            : Enumerable.Range(0, 64).Select(index => (ushort)(index % 2 == 1 ? 256 : 1)).ToArray();
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = collisionProfilesOffset + idCount * metatileCells;
        var directoryOffset = targetExpansionsOffset + checked((uint)(visualCount * metatileCells));
        var chunkDataOffset = directoryOffset + chunkCount * WorldPackDescriptor.V1DirectoryEntryBytes;
        var collisionOffset = chunkDataOffset + visualStoredBytes;
        var chunks = new List<WorldPackChunk>(chunkCount)
        {
            new(
                new WorldPackChunkDirectoryEntry(
                    chunkDataOffset,
                    visualStoredBytes,
                    128,
                    collisionOffset,
                    checked((ushort)collisionStoredBytes),
                    128,
                    8,
                    8,
                    WorldPackCodec.Raw,
                    codec),
                visualIds,
                ids),
        };
        var nextOffset = collisionOffset + checked((uint)collisionStoredBytes);
        var rawCollisionIds = Enumerable.Range(0, 64).Select(index => (ushort)(index & 1)).ToArray();
        for (var chunk = 1; chunk < chunkCount; chunk++)
        {
            var directory = new WorldPackChunkDirectoryEntry(
                nextOffset,
                visualStoredBytes,
                128,
                nextOffset + visualStoredBytes,
                128,
                128,
                8,
                8,
                WorldPackCodec.Raw,
                WorldPackCodec.Raw);
            chunks.Add(new WorldPackChunk(directory, visualIds, rawCollisionIds));
            nextOffset += 256;
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = chunkCount * 8 * metatileCells,
            HardwareHeight = 8,
            MetatileWidth = metatileCells,
            MetatileHeight = 1,
            ChunkColumns = chunkCount,
            ChunkRows = 1,
            VisualMetatileCount = visualCount,
            CollisionProfileCount = idCount,
            VisualIdBytes = 2,
            CollisionIdBytes = 2,
            TargetCellStride = 1,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = nextOffset,
        };
        var profiles = Enumerable.Range(0, idCount)
            .Select(index => new WorldPackCollisionProfile(
                Enumerable.Range(0, metatileCells)
                    .Select(digit => (WorldTileFlags)((index / (int)Math.Pow(8, metatileCells - digit - 1)) % 8))))
            .ToArray();
        var pack = new WorldPack(
            descriptor,
            profiles,
            new byte[visualCount * metatileCells],
            chunks);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateTallCollisionFixture()
    {
        const int chunkCount = 5;
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + 3;
        const uint directoryOffset = targetExpansionsOffset + 2;
        const uint chunkDataOffset = directoryOffset + chunkCount * WorldPackDescriptor.V1DirectoryEntryBytes;
        var chunks = new List<WorldPackChunk>(chunkCount);
        var nextOffset = chunkDataOffset;
        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var visualIds = Enumerable.Range(0, 8).Select(index => (ushort)(index & 1)).ToArray();
            var collisionIds = Enumerable.Range(0, 8).Select(index => (ushort)((index & 1) == 0 ? 0 : 2)).ToArray();
            if (chunkIndex == 4)
            {
                collisionIds[6] = 1;
            }

            var directory = new WorldPackChunkDirectoryEntry(
                nextOffset,
                8,
                8,
                nextOffset + 8,
                8,
                8,
                1,
                8,
                WorldPackCodec.Raw,
                WorldPackCodec.Raw);
            chunks.Add(new WorldPackChunk(directory, visualIds, collisionIds));
            nextOffset += 16;
        }

        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 1,
            HardwareHeight = 40,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = 1,
            ChunkRows = chunkCount,
            VisualMetatileCount = 2,
            CollisionProfileCount = 3,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 1,
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
                new WorldPackCollisionProfile([WorldTileFlags.Hazard]),
            ],
            new byte[] { 0, 0 },
            chunks);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateMetatileFixture(int metatileWidth, int metatileHeight)
    {
        var metatileCells = checked(metatileWidth * metatileHeight);
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        var targetExpansionsOffset = checked(collisionProfilesOffset + (uint)(2 * metatileCells));
        var directoryOffset = checked(targetExpansionsOffset + (uint)(2 * metatileCells));
        var chunkDataOffset = checked(directoryOffset + WorldPackDescriptor.V1DirectoryEntryBytes);
        var visualIds = new ushort[] { 0, 0, 0, 1 };
        var collisionIds = new ushort[] { 0, 0, 0, 1 };
        var directory = new WorldPackChunkDirectoryEntry(
            chunkDataOffset,
            4,
            4,
            chunkDataOffset + 4,
            4,
            4,
            2,
            2,
            WorldPackCodec.Raw,
            WorldPackCodec.Raw);
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = metatileWidth * 2,
            HardwareHeight = metatileHeight * 2,
            MetatileWidth = metatileWidth,
            MetatileHeight = metatileHeight,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = 2,
            CollisionProfileCount = 2,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 1,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = chunkDataOffset + 8,
        };
        var solidProfile = Enumerable.Repeat(WorldTileFlags.Empty, metatileCells).ToArray();
        solidProfile[^1] = WorldTileFlags.Hazard;
        var targetExpansions = new byte[2 * metatileCells];
        targetExpansions[^1] = 77;
        var pack = new WorldPack(
            descriptor,
            [
                new WorldPackCollisionProfile(Enumerable.Repeat(WorldTileFlags.Empty, metatileCells)),
                new WorldPackCollisionProfile(solidProfile),
            ],
            targetExpansions,
            [new WorldPackChunk(directory, visualIds, collisionIds)]);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }

    private static SerializedWorldPack CreateHighVisualIdFixture()
    {
        const int visualCount = 32_769;
        const int metatileCells = 2;
        const uint collisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes;
        const uint targetExpansionsOffset = collisionProfilesOffset + metatileCells;
        const uint directoryOffset = targetExpansionsOffset + visualCount * metatileCells;
        const uint chunkDataOffset = directoryOffset + WorldPackDescriptor.V1DirectoryEntryBytes;
        var directory = new WorldPackChunkDirectoryEntry(
            chunkDataOffset,
            2,
            2,
            chunkDataOffset + 2,
            1,
            1,
            1,
            1,
            WorldPackCodec.Raw,
            WorldPackCodec.Raw);
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 2,
            HardwareHeight = 1,
            MetatileWidth = 2,
            MetatileHeight = 1,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = visualCount,
            CollisionProfileCount = 1,
            VisualIdBytes = 2,
            CollisionIdBytes = 1,
            TargetCellStride = 1,
            CollisionProfilesOffset = collisionProfilesOffset,
            TargetExpansionsOffset = targetExpansionsOffset,
            DirectoryOffset = directoryOffset,
            ChunkDataOffset = chunkDataOffset,
            PackLength = chunkDataOffset + 3,
        };
        var expansions = new byte[visualCount * metatileCells];
        expansions[^1] = 77;
        var pack = new WorldPack(
            descriptor,
            [new WorldPackCollisionProfile([WorldTileFlags.Empty, WorldTileFlags.Empty])],
            expansions,
            [new WorldPackChunk(directory, new ushort[] { visualCount - 1 }, new ushort[] { 0 })]);
        return new SerializedWorldPack(pack, WorldPackSerializer.Serialize(pack));
    }
}
