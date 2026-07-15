namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesLargeWorldCameraTests
{
    private const ushort FrameCounterLow = NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow;
    private const ushort RequestCount = NesRuntimeMemoryLayout.PackedCamera.RequestCount;
    private const ushort PrepareCount = NesRuntimeMemoryLayout.PackedCamera.PrepareCount;
    private const ushort ResidentCount = NesRuntimeMemoryLayout.PackedCamera.ResidentCount;
    private const ushort CommitCount = NesRuntimeMemoryLayout.PackedCamera.CommitCount;
    private const ushort ReleaseCount = NesRuntimeMemoryLayout.PackedCamera.ReleaseCount;
    private const ushort BankWorkInCommit = NesRuntimeMemoryLayout.PackedCamera.BankWorkInCommit;
    private const ushort DirectoryWorkInCommit = NesRuntimeMemoryLayout.PackedCamera.DirectoryWorkInCommit;
    private const ushort DecodeWorkInCommit = NesRuntimeMemoryLayout.PackedCamera.DecodeWorkInCommit;
    private const ushort LastTileWrites = NesRuntimeMemoryLayout.PackedCamera.LastTileWrites;
    private const ushort LastAttributeWrites = NesRuntimeMemoryLayout.PackedCamera.LastAttributeWrites;
    private const ushort CriticalSection = NesRuntimeMemoryLayout.PackedCamera.CriticalSection;
    private const ushort CommitAxis = NesRuntimeMemoryLayout.PackedCamera.CommitAxis;
    private const ushort CommitDirection = NesRuntimeMemoryLayout.PackedCamera.CommitDirection;
    private const ushort CommitWorldEdgeLow = NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeLow;
    private const ushort CommitWorldEdgeHigh = NesRuntimeMemoryLayout.PackedCamera.CommitWorldEdgeHigh;
    private const ushort CommitTarget = NesRuntimeMemoryLayout.PackedCamera.CommitTarget;
    private const ushort CommitOrthogonalLow = NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalLow;
    private const ushort CommitOrthogonalHigh = NesRuntimeMemoryLayout.PackedCamera.CommitOrthogonalHigh;
    private const ushort CommitPayloadLength = NesRuntimeMemoryLayout.PackedCamera.CommitPayloadLength;
    private const ushort CommitTargetStart = NesRuntimeMemoryLayout.PackedCamera.CommitTargetStart;
    private const ushort Slot0State = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset;
    private const ushort Slot0Axis = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.AxisOffset;
    private const ushort Slot0Direction = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.DirectionOffset;
    private const ushort Slot0WorldEdgeLow = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.WorldEdgeLowOffset;
    private const ushort Slot0WorldEdgeHigh = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.WorldEdgeHighOffset;
    private const ushort Slot0Target = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.TargetOffset;
    private const ushort Slot0OrthogonalLow = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.OrthogonalLowOffset;
    private const ushort Slot0OrthogonalHigh = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.OrthogonalHighOffset;
    private const ushort Slot0PayloadLength = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.PayloadLengthOffset;
    private const ushort Slot0RequestFrameLow = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.RequestFrameLowOffset;
    private const ushort Slot0ResidentFrameLow = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.ResidentFrameLowOffset;
    private const ushort Slot0CommitPhase = NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.CommitPhaseOffset;
    private const ushort Slot1State = NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset;
    private const ushort PendingAxes = NesRuntimeMemoryLayout.PackedCamera.PendingAxes;
    private const ushort PendingColumn = NesRuntimeMemoryLayout.PackedCamera.PendingColumn;
    private const ushort PendingRow = NesRuntimeMemoryLayout.PackedCamera.PendingRow;

    private const byte Resident = 3;
    private const byte Column = 1;
    private const byte Row = 2;
    private const byte Positive = 2;
    private const byte Released = 5;
    private const byte Committing = 4;

    [Fact]
    public void World_load_with_camera_runtime_uses_the_packed_edge_path_without_legacy_attribute_tables()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                Camera.SetPosition(8, 8);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;

        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal("nes-mmc3-tvrom-v1", result.Report.SelectedProfile);
        Assert.Contains(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        Assert.True(
            result.Report.FixedSymbols.ContainsKey("worldpack_prepare_edge"),
            "A packed camera build must publish the fixed request/prepare entry point.");
        Assert.DoesNotContain(
            result.Report.Segments,
            segment => segment.Owner.StartsWith("pinned:world-column-attributes:", StringComparison.Ordinal));
    }

    [Fact]
    public void Packed_column_prepare_publishes_an_immutable_tagged_resident_slot_outside_vblank()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        var packed = NesTiledWorldImporter.CompileWorldPack(
            Path.Combine(directory, "tall.tmj"),
            NesVideoProgram.FirstSpriteTile);
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                Camera.SetPosition(8, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: packed.SerializedBytes);
        var runtime = NesWorldPackRuntimePlan.Create(packed.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow, 5);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel], 5_000_000).A);
        cpu.SetRam(FrameCounterLow, 7);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitWorldEdgeHigh, 0);
        cpu.SetRam(CommitTarget, 21);
        cpu.SetRam(CommitOrthogonalLow, 0);
        cpu.SetRam(CommitOrthogonalHigh, 0);
        cpu.SetRam(CommitPayloadLength, 32);
        var prepare = cpu.RunRoutine(
            result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
            maxInstructions: 5_000_000);

        Assert.Equal((byte)NesWorldPackResult.Success, prepare.A);
        Assert.True(
            prepare.Cycles <= 2 * 29_780,
            $"A valid edge must become resident within two NTSC CPU frames; measured {prepare.Cycles} cycles and {cpu.Ram(NesRuntimeMemoryLayout.WorldPack.VisualDecodeCount)} visual decodes.");
        Assert.Equal(1, cpu.Ram(RequestCount));
        Assert.Equal(1, cpu.Ram(PrepareCount));
        Assert.Equal(1, cpu.Ram(ResidentCount));
        Assert.Equal(Resident, cpu.Ram(Slot0State));
        Assert.Equal(Column, cpu.Ram(Slot0Axis));
        Assert.Equal(Positive, cpu.Ram(Slot0Direction));
        Assert.Equal(5, cpu.Ram(Slot0WorldEdgeLow));
        Assert.Equal(0, cpu.Ram(Slot0WorldEdgeHigh));
        Assert.Equal(21, cpu.Ram(Slot0Target));
        Assert.Equal(0, cpu.Ram(Slot0OrthogonalLow));
        Assert.Equal(0, cpu.Ram(Slot0OrthogonalHigh));
        Assert.Equal(32, cpu.Ram(Slot0PayloadLength));
        Assert.Equal(7, cpu.Ram(Slot0RequestFrameLow));
        Assert.Equal(7, cpu.Ram(Slot0ResidentFrameLow));

        var metatileCells = packed.Pack.Descriptor.MetatileWidth * packed.Pack.Descriptor.MetatileHeight;
        var edge = runtime.Layout.EdgeSlots[0];
        for (var y = 0; y < 32; y++)
        {
            var coordinate = packed.Pack.Locate(5, y);
            var visualId = packed.Pack.VisualIdAt(5, y);
            var expansion = (visualId * metatileCells + coordinate.SubcellIndex) * 2;
            Assert.Equal(packed.Pack.TargetExpansions.Span[expansion], cpu.Ram((ushort)(edge.Start + y)));
        }
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(5, cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow));
    }

    [Fact]
    public void Complete_stage1_resident_column_prepare_fits_one_ntsc_frame()
    {
        var directory = RepositoryDirectory("samples/runner");
        var packed = NesTiledWorldImporter.CompileWorldPack(
            Path.Combine(directory, "assets/maps/stage1.tmj"),
            NesVideoProgram.FirstSpriteTile + 95);
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            RunnerSample.CompiledSource(),
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: packed.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow, 5);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel], 5_000_000).A);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackInitializeLabel], 5_000_000).A);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 32);
        cpu.SetRam(CommitTarget, 0);
        cpu.SetRam(CommitOrthogonalLow, 0);
        cpu.SetRam(CommitOrthogonalHigh, 0);
        cpu.SetRam(CommitPayloadLength, 32);

        var prepare = cpu.RunRoutine(
            result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
            maxInstructions: 5_000_000);

        Assert.Equal((byte)NesWorldPackResult.Success, prepare.A);
        Assert.True(
            prepare.Cycles <= 13_000,
            $"A resident complete-stage column must finish within one NTSC frame; measured {prepare.Cycles} cycles.");
        Assert.Equal(1, cpu.Ram(RequestCount));
        Assert.Equal(1, cpu.Ram(PrepareCount));
        Assert.Equal(6, cpu.Ram(NesRuntimeMemoryLayout.WorldPack.VisualDecodeCount));
    }

    [Fact]
    public void Complete_stage1_horizontal_column_prepare_preserves_tiles_and_vertical_attribute_stride()
    {
        var directory = RepositoryDirectory("samples/tiled-hscroll");
        var packed = NesTiledWorldImporter.CompileWorldPack(
            Path.Combine(directory, "stage1-full.tmj"),
            NesVideoProgram.FirstSpriteTile);
        var runtime = NesWorldPackRuntimePlan.Create(packed.SerializedBytes);
        const string source = """
            void Main() {
                World.Load("stage1-full.tmj");
                Camera.Init(312, 0, 30);
                Camera.SetPosition(8, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: packed.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow, 5);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel], 5_000_000).A);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 53);
        cpu.SetRam(CommitTarget, 53);
        cpu.SetRam(CommitOrthogonalLow, 0);
        cpu.SetRam(CommitPayloadLength, 30);

        var prepare = cpu.RunRoutine(
            result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
            maxInstructions: 5_000_000);

        Assert.Equal((byte)NesWorldPackResult.Success, prepare.A);
        var edge = runtime.Layout.EdgeSlots[0];
        var metatileCells = packed.Pack.Descriptor.MetatileWidth * packed.Pack.Descriptor.MetatileHeight;
        var tileMismatches = new List<string>();
        for (var y = 0; y < 30; y++)
        {
            var coordinate = packed.Pack.Locate(53, y);
            var visualId = packed.Pack.VisualIdAt(53, y);
            var expansion = (visualId * metatileCells + coordinate.SubcellIndex) * 2;
            var expected = packed.Pack.TargetExpansions.Span[expansion];
            var actual = cpu.Ram((ushort)(edge.Start + y));
            if (expected != actual)
            {
                tileMismatches.Add($"row {y}: expected {expected}, observed {actual}");
            }
        }

        Assert.True(
            tileMismatches.Count == 0,
            $"stage1 column 53 tile mismatches: {string.Join("; ", tileMismatches)}.");

        var attributeColumn = 53 / 4;
        Assert.Equal(
            Enumerable.Range(0, 8).Select(row => runtime.Attributes.Bytes[row * runtime.Attributes.Columns + attributeColumn]),
            Enumerable.Range(0, 8).Select(index => cpu.Ram((ushort)(edge.Start + 32 + index))));
    }

    [Fact]
    public void Packed_row_prepare_uses_the_same_peer_lifecycle_with_complete_world_coordinates()
    {
        var serialized = CreatePaletteSyntheticWorldPack();
        var packed = RetroSharp.Core.Sdk.WorldPackSerializer.Deserialize(serialized);
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                Music.Asset(theme, "assets/music/runner.vgz");
                Sfx.Asset(jump, "assets/sfx/smb-jump.vgm");
                Audio.Init();
                Music.Play(theme);
                Sfx.Play(jump);
                World.Column(0, 0, 1, 0, 1, 0, 1, 0, 1);
                World.Map(312, 0, 8);
                Camera.Init(312, 0, 8);
                Camera.SetPosition(0, 8);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        Assert.Contains(result.Report.Segments, item => item.Owner == "pinned:bgm:theme");
        Assert.Contains(result.Report.Segments, item => item.Owner == "pinned:sfx:jump");
        Assert.Contains(
            result.Report.Segments,
            item => item.Owner.StartsWith("fixed:dpcm:", StringComparison.Ordinal));
        var runtime = NesWorldPackRuntimePlan.Create(serialized);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow, 5);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackValidateLabel], 5_000_000).A);
        cpu.SetRam(FrameCounterLow, 11);
        cpu.SetRam(CommitAxis, Row);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitWorldEdgeHigh, 0);
        cpu.SetRam(CommitTarget, 30);
        cpu.SetRam(CommitTargetStart, 2);
        cpu.SetRam(CommitOrthogonalLow, 240);
        cpu.SetRam(CommitOrthogonalHigh, 0);
        cpu.SetRam(CommitPayloadLength, 32);
        var bankWritesBeforePrepare = cpu.R6BankWrites.Count;

        var prepare = cpu.RunRoutine(
            result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
            maxInstructions: 5_000_000);

        Assert.Equal((byte)NesWorldPackResult.Success, prepare.A);
        Assert.True(
            prepare.Cycles <= 2 * 29_780,
            $"A valid row must become resident within two NTSC CPU frames; measured {prepare.Cycles} cycles.");
        Assert.Equal(Resident, cpu.Ram(Slot0State));
        Assert.Equal(Row, cpu.Ram(Slot0Axis));
        Assert.Equal(5, cpu.Ram(Slot0WorldEdgeLow));
        Assert.Equal(240, cpu.Ram(Slot0OrthogonalLow));
        Assert.Equal(0, cpu.Ram(Slot0OrthogonalHigh));
        Assert.Equal(11, cpu.Ram(Slot0RequestFrameLow));
        Assert.Equal(11, cpu.Ram(Slot0ResidentFrameLow));

        var metatileCells = packed.Descriptor.MetatileWidth * packed.Descriptor.MetatileHeight;
        var edge = runtime.Layout.EdgeSlots[0];
        for (var x = 240; x < 272; x++)
        {
            var coordinate = packed.Locate(x, 5);
            var visualId = packed.VisualIdAt(x, 5);
            var expansion = (visualId * metatileCells + coordinate.SubcellIndex) * 2;
            Assert.Equal(
                packed.TargetExpansions.Span[expansion],
                cpu.Ram((ushort)(edge.Start + x - 240)));
        }
        Assert.Equal(
            Enumerable.Repeat((byte)0x55, 9),
            Enumerable.Range(0, 9).Select(index => cpu.Ram((ushort)(edge.Start + 32 + index))));
        Assert.Equal(5, cpu.CurrentR6Bank);
        Assert.Equal(5, cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow));
        var prepareBankWrites = cpu.R6BankWrites.Skip(bankWritesBeforePrepare).ToArray();
        Assert.Contains(0, prepareBankWrites);
        Assert.Contains(3, prepareBankWrites);
    }

    [Fact]
    public void Packed_column_commit_consumes_only_the_resident_payload_within_the_32_tile_bound()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        var packed = NesTiledWorldImporter.CompileWorldPack(
            Path.Combine(directory, "tall.tmj"),
            NesVideoProgram.FirstSpriteTile);
        var runtime = NesWorldPackRuntimePlan.Create(packed.SerializedBytes);
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                Camera.SetPosition(8, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: packed.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow, 5);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitTarget, 21);
        cpu.SetRam(CommitPayloadLength, 32);
        var prepare = cpu.RunRoutine(
            result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
            maxInstructions: 5_000_000);
        Assert.Equal((byte)NesWorldPackResult.Success, prepare.A);
        var bankWritesBeforeCommit = cpu.R6BankWrites.Count;
        var commitStartCycles = cpu.Cycles;

        var commit = cpu.RunRoutine(result.Report.FixedSymbols["worldpack_commit_edge"]);

        Assert.Equal((byte)NesWorldPackResult.Success, commit.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(1, cpu.Ram(CommitCount));
        Assert.Equal(1, cpu.Ram(ReleaseCount));
        Assert.Equal(32, cpu.Ram(LastTileWrites));
        Assert.Equal(8, cpu.Ram(LastAttributeWrites));
        Assert.Equal(0, cpu.Ram(CriticalSection));
        Assert.Equal(0, cpu.Ram(BankWorkInCommit));
        Assert.Equal(0, cpu.Ram(DirectoryWorkInCommit));
        Assert.Equal(0, cpu.Ram(DecodeWorkInCommit));
        Assert.Equal(bankWritesBeforeCommit, cpu.R6BankWrites.Count);

        var ppuDataWrites = cpu.PpuWrites.Where(write => write.Register == 0x2007).ToArray();
        var expectedTileAddresses = Enumerable.Range(0, 30)
            .Select(row => (ushort)(0x2000 + row * 32 + 21))
            .Concat(Enumerable.Range(0, 2).Select(row => (ushort)(0x2800 + row * 32 + 21)))
            .ToArray();
        Assert.Equal(expectedTileAddresses.Select(address => (ushort?)address), ppuDataWrites.Take(32).Select(write => write.VramAddress));
        Assert.Equal(
            Enumerable.Range(0, 32).Select(index => cpu.Ram((ushort)(runtime.Layout.EdgeSlots[0].Start + index))),
            ppuDataWrites.Take(32).Select(write => write.Value));
        var expectedAttributeAddresses = Enumerable.Range(0, 8)
            .Select(row => (ushort)(0x23C5 + row * 8))
            .ToArray();
        Assert.Equal(
            expectedAttributeAddresses.Select(address => (ushort?)address),
            ppuDataWrites.Skip(32).Select(write => write.VramAddress));
        Assert.Equal(
            Enumerable.Range(32, 8).Select(index => cpu.Ram((ushort)(runtime.Layout.EdgeSlots[0].Start + index))),
            ppuDataWrites.Skip(32).Select(write => write.Value));
        Assert.Equal(new byte[] { 0x84, 0x80 }, cpu.PpuWrites.Where(write => write.Register == 0x2000).Select(write => write.Value));
        Assert.Single(cpu.PpuStatusReadCycles);
        Assert.All(ppuDataWrites, write => Assert.NotNull(write.VramAddress));
        Assert.True(
            commit.Cycles <= 2_136,
            $"The complete packed column commit must leave the runner's observed VBlank-entry/call-path margin; " +
            $"measured {commit.Cycles} CPU cycles " +
            $"with PPUDATA writes spanning relative cycles " +
            $"{ppuDataWrites[0].Cycle - commitStartCycles}..{ppuDataWrites[^1].Cycle - commitStartCycles} " +
            $"(last tile {ppuDataWrites[31].Cycle - commitStartCycles}, first attribute {ppuDataWrites[32].Cycle - commitStartCycles}).");
    }

    [Fact]
    public void Packed_wait_frame_discards_a_stale_signal_before_waiting_for_the_next_nmi()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        var packed = NesTiledWorldImporter.CompileWorldPack(
            Path.Combine(directory, "tall.tmj"),
            NesVideoProgram.FirstSpriteTile);
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: packed.SerializedBytes);

        Assert.True(
            ContainsSequence(
                result.Rom,
                [
                    0xA9, 0x00, 0x8D, 0x8F, 0x03,
                    0xAD, 0x8F, 0x03, 0xF0, 0xFB,
                    0xCE, 0x8F, 0x03,
                    0x2C, 0x02, 0x20, 0x10, 0xFB,
                ]),
            "Packed WaitFrame must discard a coalesced signal before waiting for a fresh NMI/VBlank edge.");
    }

    [Fact]
    public void Packed_row_commit_uses_four_8_tile_phases_then_one_9_attribute_phase()
    {
        var serialized = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        const string source = """
            void Main() {
                World.Column(0, 0, 1, 0, 1, 0, 1, 0, 1);
                World.Map(312, 0, 8);
                Camera.Init(312, 0, 8);
                Camera.SetPosition(0, 8);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var runtime = NesWorldPackRuntimePlan.Create(serialized);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetR6Bank(5);
        cpu.SetRam(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow, 5);
        cpu.SetRam(CommitAxis, Row);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitTarget, 30);
        cpu.SetRam(CommitTargetStart, 1);
        cpu.SetRam(CommitOrthogonalLow, 250);
        cpu.SetRam(CommitPayloadLength, 32);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(
                result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
                maxInstructions: 5_000_000).A);
        var edge = runtime.Layout.EdgeSlots[0];
        for (var attribute = 0; attribute < 9; attribute++)
        {
            cpu.SetRam((ushort)(edge.Start + 32 + attribute), (byte)(0x10 + attribute));
        }
        var bankWritesBeforeCommit = cpu.R6BankWrites.Count;

        for (var phase = 1; phase <= 4; phase++)
        {
            var tilePhase = cpu.RunRoutine(result.Report.FixedSymbols["worldpack_commit_edge"]);
            Assert.Equal((byte)NesWorldPackResult.Success, tilePhase.A);
            Assert.Equal(Committing, cpu.Ram(Slot0State));
            Assert.Equal(phase, cpu.Ram(Slot0CommitPhase));
            Assert.Equal(8, cpu.Ram(LastTileWrites));
            Assert.Equal(0, cpu.Ram(LastAttributeWrites));
            Assert.Equal(0, cpu.Ram(ReleaseCount));
        }

        var attributePhase = cpu.RunRoutine(result.Report.FixedSymbols["worldpack_commit_edge"]);

        Assert.Equal((byte)NesWorldPackResult.Success, attributePhase.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(4, cpu.Ram(Slot0CommitPhase));
        Assert.Equal(0, cpu.Ram(LastTileWrites));
        Assert.Equal(9, cpu.Ram(LastAttributeWrites));
        Assert.Equal(1, cpu.Ram(CommitCount));
        Assert.Equal(1, cpu.Ram(ReleaseCount));
        Assert.Equal(0, cpu.Ram(CriticalSection));
        Assert.Equal(bankWritesBeforeCommit, cpu.R6BankWrites.Count);
    }

    [Fact]
    public void Packed_camera_source_calls_prepare_during_gameplay_and_commit_from_camera_apply()
    {
        var directory = RepositoryDirectory("samples/runner");
        var packed = NesTiledWorldImporter.CompileWorldPack(
            Path.Combine(directory, "assets/maps/stage1.playable.tmj"),
            NesVideoProgram.FirstSpriteTile);
        const string source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Camera.Init(176, 0, 30);
                Camera.SetPosition(8, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: packed.SerializedBytes);
        var prepare = result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel];
        var commit = result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel];
        var prepareCall = new byte[] { 0x20, (byte)prepare, (byte)(prepare >> 8) };
        var commitCall = new byte[] { 0x20, (byte)commit, (byte)(commit >> 8) };

        Assert.True(result.Rom.AsSpan().IndexOf(prepareCall) >= 0, "Camera.SetPosition must call packed prepare outside VBlank.");
        Assert.True(result.Rom.AsSpan().IndexOf(commitCall) >= 0, "Camera.Apply must call the resident-only packed commit routine.");
    }

    [Fact]
    public void Diagonal_peers_select_the_column_first_and_retain_the_resident_row_immutable()
    {
        var serialized = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        var runtime = NesWorldPackRuntimePlan.Create(serialized);
        const string source = """
            void Main() {
                World.Column(0, 0, 1, 0, 1, 0, 1, 0, 1);
                World.Map(312, 0, 8);
                Camera.Init(312, 0, 8);
                Camera.SetPosition(8, 8);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitTarget, 21);
        cpu.SetRam(CommitPayloadLength, 8);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel], 5_000_000).A);
        CopyPendingDescriptor(cpu, NesRuntimeMemoryLayout.PackedCamera.Slot0, PendingColumn);
        cpu.SetRam(CommitAxis, Row);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitTarget, 30);
        cpu.SetRam(CommitOrthogonalLow, 250);
        cpu.SetRam(CommitPayloadLength, 32);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel], 5_000_000).A);
        CopyPendingDescriptor(cpu, NesRuntimeMemoryLayout.PackedCamera.Slot1, PendingRow);
        cpu.SetRam(PendingAxes, Column | Row);
        var retainedRow = Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot1 + offset)))
            .ToArray();
        var retainedColumn = Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)))
            .ToArray();
        var shortColumnAttributes = new byte[] { 0xD1, 0xD2 };
        for (var index = 0; index < shortColumnAttributes.Length; index++)
        {
            cpu.SetRam((ushort)(runtime.Layout.EdgeSlots[0].Start + 8 + index), (byte)(0xE1 + index));
            cpu.SetRam((ushort)(runtime.Layout.EdgeSlots[0].Start + 32 + index), shortColumnAttributes[index]);
        }
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 6);
        cpu.SetRam(CommitWorldEdgeHigh, 0);
        cpu.SetRam(CommitTarget, 22);
        cpu.SetRam(CommitTargetStart, 0);
        cpu.SetRam(CommitOrthogonalLow, 0);
        cpu.SetRam(CommitOrthogonalHigh, 0);
        cpu.SetRam(CommitPayloadLength, 8);
        Assert.Equal(
            NesPackedCameraRuntime.NoSlot,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel]).A);
        Assert.Equal(retainedColumn, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)))
            .ToArray());
        Assert.Equal(retainedRow, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot1 + offset)))
            .ToArray());

        var firstCommit = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);

        Assert.Equal((byte)NesWorldPackResult.Success, firstCommit.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(Resident, cpu.Ram(Slot1State));
        Assert.Equal(
            shortColumnAttributes,
            cpu.PpuWrites.Where(write => write.Register == 0x2007).Skip(8).Select(write => write.Value));
        Assert.Equal(retainedRow, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot1 + offset)))
            .ToArray());
        Assert.Equal(1, cpu.Ram(CommitCount));

        var firstRowPhase = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);
        Assert.Equal((byte)NesWorldPackResult.Success, firstRowPhase.A);
        Assert.Equal(Committing, cpu.Ram(Slot1State));
        Assert.Equal(1, cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset)));

        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 6);
        cpu.SetRam(CommitWorldEdgeHigh, 0);
        cpu.SetRam(CommitTarget, 22);
        cpu.SetRam(CommitTargetStart, 0);
        cpu.SetRam(CommitOrthogonalLow, 0);
        cpu.SetRam(CommitOrthogonalHigh, 0);
        cpu.SetRam(CommitPayloadLength, 8);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel], 5_000_000).A);
        CopyPendingDescriptor(cpu, NesRuntimeMemoryLayout.PackedCamera.Slot0, PendingColumn);
        cpu.SetRam(PendingAxes, Column | Row);

        var interleavedColumn = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);
        Assert.Equal((byte)NesWorldPackResult.Success, interleavedColumn.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(Committing, cpu.Ram(Slot1State));
        Assert.Equal(1, cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset)));

        for (var phase = 2; phase <= 4; phase++)
        {
            var rowPhase = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);
            Assert.Equal((byte)NesWorldPackResult.Success, rowPhase.A);
            Assert.Equal(Committing, cpu.Ram(Slot1State));
            Assert.Equal(phase, cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset)));
        }

        var rowAttributes = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);
        Assert.Equal((byte)NesWorldPackResult.Success, rowAttributes.A);
        Assert.Equal(Released, cpu.Ram(Slot1State));
        Assert.Equal(3, cpu.Ram(CommitCount));
        Assert.Equal(0, cpu.Ram(PendingAxes));
    }

    [Fact]
    public void Packed_commit_rejects_a_mismatched_16_bit_pending_tag_without_mutating_the_resident_slot()
    {
        var serialized = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        const string source = """
            void Main() {
                World.Column(0, 0, 1, 0, 1, 0, 1, 0, 1);
                World.Map(312, 0, 8);
                Camera.Init(312, 0, 8);
                Camera.SetPosition(8, 0);
                Camera.Apply();
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 0xFF);
        cpu.SetRam(CommitWorldEdgeHigh, 0);
        cpu.SetRam(CommitTarget, 21);
        cpu.SetRam(CommitPayloadLength, 8);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel], 5_000_000).A);
        CopyPendingDescriptor(cpu, NesRuntimeMemoryLayout.PackedCamera.Slot0, PendingColumn);
        cpu.SetRam((ushort)(PendingColumn + NesPackedCameraRuntime.WorldEdgeHighOffset), 1);
        cpu.SetRam(PendingAxes, Column);
        var metadata = Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)))
            .ToArray();

        var commit = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);

        Assert.Equal((byte)NesWorldPackResult.Miss, commit.A);
        Assert.Equal(metadata, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)))
            .ToArray());
        Assert.Equal(0, cpu.Ram(CommitCount));
        Assert.Equal(0, cpu.Ram(LastTileWrites));
        Assert.Equal(Column, cpu.Ram(PendingAxes));
    }

    [Theory]
    [InlineData(Column, 0)]
    [InlineData(Column, 33)]
    [InlineData(Row, 31)]
    public void Packed_prepare_rejects_malformed_payload_lengths_before_claiming_a_slot(byte axis, byte payloadLength)
    {
        var serialized = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        const string source = """
            void Main() {
                World.Column(0, 0, 1, 0, 1, 0, 1, 0, 1);
                World.Map(312, 0, 8);
                Camera.Init(312, 0, 8);
                Camera.SetPosition(8, 0);
                Camera.Apply();
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetRam(CommitAxis, axis);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitPayloadLength, payloadLength);

        var prepare = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel]);

        Assert.Equal((byte)NesWorldPackResult.Miss, prepare.A);
        Assert.Equal(0, cpu.Ram(Slot0State));
        Assert.Equal(0, cpu.Ram(Slot1State));
        Assert.Equal(0, cpu.Ram(RequestCount));
    }

    [Fact]
    public void Packed_prepare_releases_its_claimed_slot_when_pack_validation_is_malformed()
    {
        var serialized = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        const string source = """
            void Main() {
                World.Column(0, 0, 1, 0, 1, 0, 1, 0, 1);
                World.Map(312, 0, 8);
                Camera.Init(312, 0, 8);
                Camera.SetPosition(8, 0);
                Camera.Apply();
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetRam(NesRuntimeMemoryLayout.WorldPack.ValidationState, 2);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitTarget, 21);
        cpu.SetRam(CommitPayloadLength, 8);

        var prepare = cpu.RunRoutine(
            result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel],
            maxInstructions: 5_000_000);

        Assert.Equal((byte)NesWorldPackResult.Malformed, prepare.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(1, cpu.Ram(RequestCount));
        Assert.Equal(1, cpu.Ram(PrepareCount));
        Assert.Equal(0, cpu.Ram(ResidentCount));
        Assert.Equal(1, cpu.Ram(ReleaseCount));
        Assert.Equal(0, cpu.Ram(CommitCount));
    }

    [Fact]
    public void Reversal_releases_only_an_uncommitted_resident_peer_of_the_same_axis()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        var packed = NesTiledWorldImporter.CompileWorldPack(
            Path.Combine(directory, "tall.tmj"),
            NesVideoProgram.FirstSpriteTile);
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                Camera.SetPosition(8, 0);
                while (true) { Video.WaitVBlank(); Camera.Apply(); }
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: packed.SerializedBytes);
        var cpu = new NesTestCpu(result.Rom);
        cpu.SetRam(CommitAxis, Column);
        cpu.SetRam(CommitDirection, Positive);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitTarget, 21);
        cpu.SetRam(CommitPayloadLength, 8);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel], 5_000_000).A);
        cpu.SetRam(CommitDirection, 1);

        var release = cpu.RunRoutine(result.Report.FixedSymbols["worldpack_release_reversed_edge"]);

        Assert.Equal((byte)NesWorldPackResult.Success, release.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(1, cpu.Ram(ReleaseCount));

        cpu.SetRam(Slot0State, Committing);
        var committingMetadata = Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)))
            .ToArray();
        _ = cpu.RunRoutine(result.Report.FixedSymbols["worldpack_release_reversed_edge"]);
        Assert.Equal(committingMetadata, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesRuntimeMemoryLayout.PackedCamera.Slot0 + offset)))
            .ToArray());
        Assert.Equal(1, cpu.Ram(ReleaseCount));
    }

    [Fact]
    public void Wide_packed_camera_walk_keeps_the_requested_high_byte_outside_worldpack_scratch()
    {
        var serialized = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        const string source = """
            void Main() {
                World.Column(0, 0, 1, 0, 1, 0, 1, 0, 1);
                World.Map(312, 0, 8);
                Camera.Init(312, 0, 8);
                i16 target = 0;
                Camera.SetPosition(target, 0);
                Camera.Apply();
            }
            """;
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: serialized);

        Assert.True(
            result.Rom.AsSpan().IndexOf(new byte[] { 0x8D, 0x0C, 0x03 }) >= 0,
            "The 16-bit walk target high byte must be stored in stable absolute state, not WorldPack's $E9 pointer scratch.");
        Assert.True(
            result.Rom.AsSpan().IndexOf(new byte[] { 0xCD, 0x0C, 0x03 }) >= 0,
            "Every page-crossing comparison must reload the stable walk target high byte after packed preparation.");
        Assert.True(
            result.Rom.AsSpan().IndexOf(new byte[] { 0xA5, 0xE0, 0x8D, 0xCB, 0x03 }) >= 0,
            "A completed column commit must publish the latest safe fine-scroll position, not the older edge snapshot.");
        Assert.True(
            result.Rom.AsSpan().IndexOf(new byte[] { 0xAD, 0xD0, 0x03, 0x8D, 0xCB, 0x03 }) < 0,
            "The committed column publisher must not restore the stale position captured at the tile boundary.");
    }

    [Fact]
    public void Packed_attribute_plan_reproduces_lw_1_4_palette_and_provenance_tie_breaks()
    {
        var metadata = new byte[]
        {
            1, 1, 2, 2,
            1, 1, 2, 2,
            1, 1, 2 | 0x04, 2 | 0x04,
            3 | 0x04, 3 | 0x04, 1 | 0x04, 1 | 0x04,
        };
        var expansions = metadata.SelectMany((value, index) => new byte[] { (byte)index, value }).ToArray();
        var ids = Enumerable.Range(0, 16).Select(index => (ushort)index).ToArray();
        var descriptor = new WorldPackDescriptor
        {
            HardwareWidth = 4,
            HardwareHeight = 4,
            MetatileWidth = 1,
            MetatileHeight = 1,
            ChunkColumns = 1,
            ChunkRows = 1,
            VisualMetatileCount = 16,
            CollisionProfileCount = 1,
            VisualIdBytes = 1,
            CollisionIdBytes = 1,
            TargetCellStride = 2,
            CollisionProfilesOffset = WorldPackDescriptor.V1HeaderBytes,
            TargetExpansionsOffset = WorldPackDescriptor.V1HeaderBytes + 1,
            DirectoryOffset = WorldPackDescriptor.V1HeaderBytes + 1 + 32,
            ChunkDataOffset = WorldPackDescriptor.V1HeaderBytes + 1 + 32 + WorldPackDescriptor.V1DirectoryEntryBytes,
            PackLength = WorldPackDescriptor.V1HeaderBytes + 1 + 32 + WorldPackDescriptor.V1DirectoryEntryBytes + 32,
        };
        var directory = new WorldPackChunkDirectoryEntry(
            descriptor.ChunkDataOffset,
            16,
            16,
            descriptor.ChunkDataOffset + 16,
            16,
            16,
            4,
            4,
            WorldPackCodec.Raw,
            WorldPackCodec.Raw);
        var pack = new WorldPack(
            descriptor,
            [new WorldPackCollisionProfile([WorldTileFlags.Empty])],
            expansions,
            [new WorldPackChunk(directory, ids, Enumerable.Repeat((ushort)0, 16))]);

        var attributes = NesWorldPackAttributePlan.Create(pack);

        Assert.Equal(1, attributes.Columns);
        Assert.Equal(1, attributes.Rows);
        Assert.Equal(new byte[] { 0xB9 }, attributes.Bytes);
    }

    [Fact]
    public void Camera_accepts_logical_map_width_above_byte_range()
    {
        var rom = NesRomCompiler.CompileSource(WideCameraSource(256));

        Assert.Equal(40976, rom.Length);
    }

    [Fact]
    public void Camera_emits_word_movement_and_column_256_addressing_from_255_to_256()
    {
        var prg = Prg(NesRomCompiler.CompileSource(WideCameraSource(1888)));

        Assert.True(ContainsSequence(prg, [0xA9, 0x60, 0x85, 0x00, 0xA9, 0x07, 0x85, 0x01]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x18, 0x03, 0xCD, 0x0C, 0x03]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1A, 0x03, 0x8D, 0x1E, 0x03]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1E, 0x03, 0x8D, 0x21, 0x03]));
        Assert.True(ContainsSequence(prg, [0x6D, 0x1E, 0x03, 0x85, 0xE9, 0xA0, 0x00, 0xB1, 0xE8]));
        Assert.True(ContainsSequence(prg, [0xA5, 0xE0, 0x8D, 0x05, 0x20]));
    }

    [Fact]
    public void Camera_emits_word_movement_and_column_256_addressing_from_256_to_255()
    {
        var prg = Prg(NesRomCompiler.CompileSource(WideCameraSource(2055)));

        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x00, 0xA9, 0x08, 0x85, 0x01]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1A, 0x03, 0x38, 0xE9, 0x01, 0x8D, 0x1A, 0x03, 0xC6, 0xE1]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1A, 0x03, 0x8D, 0x1E, 0x03]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1E, 0x03, 0x8D, 0x21, 0x03]));
        Assert.True(ContainsSequence(prg, [0x6D, 0x1E, 0x03, 0x85, 0xE9, 0xA0, 0x00, 0xB1, 0xE8]));
        Assert.True(ContainsSequence(prg, [0xA5, 0xE0, 0x8D, 0x05, 0x20]));
    }

    private static string WideCameraSource(int requestedX)
    {
        return $$"""
                 void Main() {
                     Video.Init();
                     World.Column(0, 1);
                     World.Column(256, 4);
                     World.Column(311, 1);
                     World.Map(312, 0, 1);
                     Camera.Init(312, 0, 1);
                     i16 cameraX = {{requestedX}};
                     Camera.SetPosition(cameraX, 0);
                     Camera.Apply();
                 }
                 """;
    }

    private static byte[] Prg(byte[] rom) => rom.Skip(16).Take(32 * 1024).ToArray();

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            if (bytes.AsSpan(i, sequence.Length).SequenceEqual(sequence))
            {
                return true;
            }
        }

        return false;
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
    }

    private static byte[] CreatePaletteSyntheticWorldPack()
    {
        var serialized = NesWorldPackPlacementTests.CreateSyntheticWorldPack();
        var source = WorldPackSerializer.Deserialize(serialized);
        var pack = new WorldPack(
            source.Descriptor,
            source.CollisionProfiles,
            new byte[] { 0, 0, 1, 0x05 },
            source.Chunks);
        return WorldPackSerializer.Serialize(pack);
    }

    private static void CopyPendingDescriptor(NesTestCpu cpu, ushort slot, ushort pending)
    {
        for (var offset = NesPackedCameraRuntime.AxisOffset; offset < NesPackedCameraRuntime.SlotMetadataBytes; offset++)
        {
            cpu.SetRam((ushort)(pending + offset), cpu.Ram((ushort)(slot + offset)));
        }
    }
}
