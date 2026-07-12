namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesLargeWorldCameraTests
{
    private const ushort FrameCounterLow = 0x036E;
    private const ushort RequestCount = 0x0370;
    private const ushort PrepareCount = 0x0371;
    private const ushort ResidentCount = 0x0372;
    private const ushort CommitCount = 0x0373;
    private const ushort ReleaseCount = 0x0374;
    private const ushort BankWorkInCommit = 0x0375;
    private const ushort DirectoryWorkInCommit = 0x0376;
    private const ushort DecodeWorkInCommit = 0x0377;
    private const ushort LastTileWrites = 0x0378;
    private const ushort LastAttributeWrites = 0x0379;
    private const ushort CriticalSection = 0x0380;
    private const ushort CommitAxis = 0x0382;
    private const ushort CommitDirection = 0x0383;
    private const ushort CommitWorldEdgeLow = 0x0384;
    private const ushort CommitWorldEdgeHigh = 0x0385;
    private const ushort CommitTarget = 0x0386;
    private const ushort CommitOrthogonalLow = 0x0387;
    private const ushort CommitOrthogonalHigh = 0x0388;
    private const ushort CommitPayloadLength = 0x0389;
    private const ushort CommitTargetStart = 0x038B;
    private const ushort Slot0State = 0x0390;
    private const ushort Slot0Axis = 0x0391;
    private const ushort Slot0Direction = 0x0392;
    private const ushort Slot0WorldEdgeLow = 0x0393;
    private const ushort Slot0WorldEdgeHigh = 0x0394;
    private const ushort Slot0Target = 0x0395;
    private const ushort Slot0OrthogonalLow = 0x0396;
    private const ushort Slot0OrthogonalHigh = 0x0397;
    private const ushort Slot0PayloadLength = 0x0398;
    private const ushort Slot0RequestFrameLow = 0x0399;
    private const ushort Slot0ResidentFrameLow = 0x039B;
    private const ushort Slot0CommitPhase = 0x039D;
    private const ushort Slot1State = 0x03A0;
    private const ushort PendingAxes = 0x03CA;
    private const ushort PendingColumn = 0x03D0;
    private const ushort PendingRow = 0x03E0;

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
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
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
            $"A valid edge must become resident within two NTSC CPU frames; measured {prepare.Cycles} cycles and {cpu.Ram(NesWorldPackRuntimeAbi.VisualDecodeCount)} visual decodes.");
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
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
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
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
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
        Assert.Equal(6, cpu.Ram(NesWorldPackRuntimeAbi.VisualDecodeCount));
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
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
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
        Assert.Equal(5, cpu.Ram(NesRomBuilder.Mmc3R6BankShadowAddress));
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
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
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
        cpu.SetRam(NesRomBuilder.Mmc3R6BankShadowAddress, 5);
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
            Assert.Equal(0, cpu.Ram(0x0379));
            Assert.Equal(0, cpu.Ram(ReleaseCount));
        }

        var attributePhase = cpu.RunRoutine(result.Report.FixedSymbols["worldpack_commit_edge"]);

        Assert.Equal((byte)NesWorldPackResult.Success, attributePhase.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(4, cpu.Ram(Slot0CommitPhase));
        Assert.Equal(0, cpu.Ram(LastTileWrites));
        Assert.Equal(9, cpu.Ram(0x0379));
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
        CopyPendingDescriptor(cpu, NesPackedCameraRuntime.Slot0, PendingColumn);
        cpu.SetRam(CommitAxis, Row);
        cpu.SetRam(CommitWorldEdgeLow, 5);
        cpu.SetRam(CommitTarget, 30);
        cpu.SetRam(CommitOrthogonalLow, 250);
        cpu.SetRam(CommitPayloadLength, 32);
        Assert.Equal(
            (byte)NesWorldPackResult.Success,
            cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackPrepareEdgeLabel], 5_000_000).A);
        CopyPendingDescriptor(cpu, NesPackedCameraRuntime.Slot1, PendingRow);
        cpu.SetRam(PendingAxes, Column | Row);
        var retainedRow = Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot1 + offset)))
            .ToArray();
        var retainedColumn = Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot0 + offset)))
            .ToArray();
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
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot0 + offset)))
            .ToArray());
        Assert.Equal(retainedRow, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot1 + offset)))
            .ToArray());

        var firstCommit = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);

        Assert.Equal((byte)NesWorldPackResult.Success, firstCommit.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(Resident, cpu.Ram(Slot1State));
        Assert.Equal(retainedRow, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot1 + offset)))
            .ToArray());
        Assert.Equal(1, cpu.Ram(CommitCount));

        var firstRowPhase = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);
        Assert.Equal((byte)NesWorldPackResult.Success, firstRowPhase.A);
        Assert.Equal(Committing, cpu.Ram(Slot1State));
        Assert.Equal(1, cpu.Ram((ushort)(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset)));

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
        CopyPendingDescriptor(cpu, NesPackedCameraRuntime.Slot0, PendingColumn);
        cpu.SetRam(PendingAxes, Column | Row);

        var interleavedColumn = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);
        Assert.Equal((byte)NesWorldPackResult.Success, interleavedColumn.A);
        Assert.Equal(Released, cpu.Ram(Slot0State));
        Assert.Equal(Committing, cpu.Ram(Slot1State));
        Assert.Equal(1, cpu.Ram((ushort)(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset)));

        for (var phase = 2; phase <= 4; phase++)
        {
            var rowPhase = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);
            Assert.Equal((byte)NesWorldPackResult.Success, rowPhase.A);
            Assert.Equal(Committing, cpu.Ram(Slot1State));
            Assert.Equal(phase, cpu.Ram((ushort)(NesPackedCameraRuntime.Slot1 + NesPackedCameraRuntime.CommitPhaseOffset)));
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
        CopyPendingDescriptor(cpu, NesPackedCameraRuntime.Slot0, PendingColumn);
        cpu.SetRam((ushort)(PendingColumn + NesPackedCameraRuntime.WorldEdgeHighOffset), 1);
        cpu.SetRam(PendingAxes, Column);
        var metadata = Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot0 + offset)))
            .ToArray();

        var commit = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackCommitEdgeLabel]);

        Assert.Equal((byte)NesWorldPackResult.Miss, commit.A);
        Assert.Equal(metadata, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot0 + offset)))
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
        cpu.SetRam(NesWorldPackRuntimeAbi.ValidationState, 2);
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
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot0 + offset)))
            .ToArray();
        _ = cpu.RunRoutine(result.Report.FixedSymbols["worldpack_release_reversed_edge"]);
        Assert.Equal(committingMetadata, Enumerable.Range(0, NesPackedCameraRuntime.SlotMetadataBytes)
            .Select(offset => cpu.Ram((ushort)(NesPackedCameraRuntime.Slot0 + offset)))
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
