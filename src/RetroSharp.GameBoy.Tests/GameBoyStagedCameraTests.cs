namespace RetroSharp.GameBoy.Tests;

using System;
using System.IO;
using System.Linq;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;

public sealed class GameBoyStagedCameraTests
{
    private const ushort VisibleCameraXLow = 0xC14D;
    private const ushort CriticalSection = 0xC151;
    private const ushort RequestCount = 0xC152;
    private const ushort PrepareCount = 0xC153;
    private const ushort ResidentCount = 0xC154;
    private const ushort CommitCount = 0xC155;
    private const ushort ReleaseCount = 0xC156;
    private const ushort BankWorkInCommit = 0xC157;
    private const ushort DecodeWorkInCommit = 0xC158;
    private const ushort LastCommitVramWrites = 0xC159;
    private const ushort Slot0State = 0xC170;
    private const ushort Slot0Axis = 0xC171;
    private const ushort Slot0Direction = 0xC172;
    private const ushort Slot0WorldEdgeLow = 0xC173;
    private const ushort Slot0WorldEdgeHigh = 0xC174;
    private const ushort Slot0Target = 0xC175;
    private const ushort Slot0OrthogonalLow = 0xC177;
    private const ushort Slot0OrthogonalHigh = 0xC178;
    private const ushort SelectedSlot = 0xC16F;
    private const ushort LastCommittedWorldEdgeLow = 0xC188;
    private const ushort LastCommittedAxis = 0xC18A;
    private const ushort Slot1State = 0xC17A;
    private const ushort Slot1Axis = 0xC17B;
    private const ushort Slot1Direction = 0xC17C;
    private const ushort Slot1WorldEdgeLow = 0xC17D;
    private const ushort Slot1WorldEdgeHigh = 0xC17E;
    private const ushort Slot1Target = 0xC17F;
    private const ushort WorldPackValidationState = 0xC1FB;
    private const ushort CameraRightSourceColumn = 0xC0E6;
    private const ushort CameraXLow = 0xC0E0;
    private const ushort CameraXHigh = 0xC0E1;
    private const ushort CameraFineX = 0xC0E2;
    private const ushort CameraScreenLeftColumn = 0xC0E3;
    private const ushort CameraRightBackgroundColumn = 0xC0E4;
    private const ushort CameraLeftBackgroundColumn = 0xC0E5;
    private const ushort CameraLeftSourceColumn = 0xC0E7;
    private const ushort CameraScreenLeftColumnHigh = 0xC143;
    private const ushort CameraRightSourceColumnHigh = 0xC144;
    private const ushort CameraLeftSourceColumnHigh = 0xC145;
    private const ushort VisibleCameraXHigh = 0xC14E;
    private const ushort VisibleCameraYLow = 0xC14F;
    private const ushort VisibleCameraYHigh = 0xC150;
    private const ushort LastCommittedWorldEdgeHigh = 0xC189;
    private const ushort LastCommittedDirection = 0xC18B;
    private const ushort DirectoryWorkInVBlank = 0xC19C;

    private const byte Released = 5;
    private const byte Empty = 0;
    private const byte Resident = 3;
    private const byte Committing = 4;
    private const byte Column = 1;
    private const byte Row = 2;
    private const byte Positive = 2;
    private const byte Negative = 1;

    [Fact]
    public void Packed_column_follows_request_prepare_resident_commit_release_before_camera_publication()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 32);
                Camera.SetPosition(8, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "tall.tmj"));
        Assert.Contains(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        Assert.DoesNotContain(result.Report.Segments, segment => segment.Owner == "legacy-world-data:default");

        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        cpu.RunUntilWramEquals(Slot0State, Resident);

        Assert.Equal(1, cpu.Wram(RequestCount));
        Assert.Equal(1, cpu.Wram(PrepareCount));
        Assert.Equal(1, cpu.Wram(ResidentCount));
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(0, cpu.Wram(ReleaseCount));
        Assert.Equal(Column, cpu.Wram(Slot0Axis));
        Assert.Equal(Positive, cpu.Wram(Slot0Direction));
        Assert.Equal(5, cpu.Wram(Slot0WorldEdgeLow));
        Assert.Equal(0, cpu.Wram(Slot0WorldEdgeHigh));
        Assert.Equal(21, cpu.Wram(Slot0Target));
        Assert.Equal(0, cpu.Wram(Slot0OrthogonalLow));
        Assert.Equal(0, cpu.Wram(Slot0OrthogonalHigh));
        Assert.Equal(0, cpu.Wram(VisibleCameraXLow));
        Assert.Equal(0, cpu.IoRegister(0xFF43));

        var bankWritesBeforeCommit = cpu.RomBankWrites.Count;
        var vramWritesBeforeCommit = cpu.VramWrites.Count;
        cpu.RunUntilWramEquals(ReleaseCount, 1);

        Assert.Equal(Released, cpu.Wram(Slot0State));
        Assert.Equal(1, cpu.Wram(CommitCount));
        Assert.Equal(19, cpu.Wram(LastCommitVramWrites));
        Assert.Equal(0, cpu.Wram(CriticalSection));
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
        Assert.Empty(cpu.RomBankWrites.Skip(bankWritesBeforeCommit));
        var commitWrites = cpu.VramWrites.Skip(vramWritesBeforeCommit).ToArray();
        Assert.Equal(19, commitWrites.Length);
        Assert.All(commitWrites, write =>
        {
            Assert.True(write.Applied);
            Assert.InRange(write.Ly, (byte)144, (byte)153);
        });
        Assert.Equal(8, cpu.Wram(VisibleCameraXLow));
        var scrollWrites = cpu.RunUntilIoRegisterWrites(0xFF43, 1, 1_000_000);
        Assert.Equal(new byte[] { 8 }, scrollWrites);
        Assert.Equal(8, cpu.IoRegister(0xFF43));
    }

    [Fact]
    public void Two_same_axis_crossings_commit_in_world_edge_order_one_bounded_vblank_at_a_time()
    {
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Camera.Init(176, 0, 30);
                Camera.SetPosition(16, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "assets/maps/stage1.playable.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        cpu.RunUntilWramEquals(Slot1State, Resident);
        Assert.Equal(2, cpu.Wram(RequestCount));
        Assert.Equal(Resident, cpu.Wram(Slot0State));
        Assert.Equal(Resident, cpu.Wram(Slot1State));
        Assert.Equal(Column, cpu.Wram(Slot1Axis));
        Assert.Equal(Positive, cpu.Wram(Slot1Direction));
        Assert.Equal(21, cpu.Wram(Slot0WorldEdgeLow));
        Assert.Equal(0, cpu.Wram(Slot0WorldEdgeHigh));
        Assert.Equal(22, cpu.Wram(Slot1WorldEdgeLow));
        Assert.Equal(0, cpu.Wram(Slot1WorldEdgeHigh));
        Assert.Equal(22, cpu.Wram(Slot1Target));
        var retainedSlot1 = Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(Slot1State + offset))).ToArray();
        var writesBeforeFirstCommit = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(ReleaseCount, 1);

        Assert.Equal(0, cpu.Wram(SelectedSlot));
        Assert.Equal(21, cpu.Wram(LastCommittedWorldEdgeLow));
        Assert.Equal(Released, cpu.Wram(Slot0State));
        Assert.Equal(Resident, cpu.Wram(Slot1State));
        Assert.Equal(retainedSlot1, Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(Slot1State + offset))).ToArray());
        Assert.Equal(8, cpu.Wram(VisibleCameraXLow));
        var firstWrites = cpu.VramWrites.Skip(writesBeforeFirstCommit).ToArray();
        Assert.Equal(19, firstWrites.Length);
        Assert.All(firstWrites, write => Assert.InRange(write.Ly, (byte)144, (byte)153));
        var writesBeforeSecondCommit = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(ReleaseCount, 2);

        Assert.Equal(1, cpu.Wram(SelectedSlot));
        Assert.Equal(22, cpu.Wram(LastCommittedWorldEdgeLow));
        Assert.Equal(Released, cpu.Wram(Slot1State));
        Assert.Equal(16, cpu.Wram(VisibleCameraXLow));
        var secondWrites = cpu.VramWrites.Skip(writesBeforeSecondCommit).ToArray();
        Assert.Equal(19, secondWrites.Length);
        Assert.All(secondWrites, write => Assert.InRange(write.Ly, (byte)144, (byte)153));
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
    }

    [Fact]
    public void Diagonal_staging_keeps_column_and_row_peers_then_commits_column_first_and_row_second()
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
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "tall.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        cpu.RunUntilWramEquals(Slot1State, Resident);
        Assert.Equal(Column, cpu.Wram(Slot0Axis));
        Assert.Equal(Row, cpu.Wram(Slot1Axis));
        var writesBeforeColumn = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(ReleaseCount, 1);

        Assert.Equal(Column, cpu.Wram(LastCommittedAxis));
        Assert.Equal(Released, cpu.Wram(Slot0State));
        Assert.Equal(Resident, cpu.Wram(Slot1State));
        Assert.Equal(8, cpu.Wram(VisibleCameraXLow));
        Assert.Equal(0, cpu.Wram(0xC14F));
        Assert.Equal(19, cpu.VramWrites.Count - writesBeforeColumn);
        var writesBeforeRow = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(ReleaseCount, 2);

        Assert.Equal(Row, cpu.Wram(LastCommittedAxis));
        Assert.Equal(Released, cpu.Wram(Slot1State));
        Assert.Equal(8, cpu.Wram(0xC14F));
        Assert.Equal(21, cpu.VramWrites.Count - writesBeforeRow);
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
    }

    [Fact]
    public void Reversal_before_commit_releases_the_resident_edge_without_visible_advance()
    {
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Camera.Init(176, 0, 30);
                i16 target = 8;
                Camera.SetPosition(target, 0);
                target = 0;
                Camera.SetPosition(target, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "assets/maps/stage1.playable.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        cpu.RunUntilWramEquals(ReleaseCount, 1);

        Assert.Equal(1, cpu.Wram(RequestCount));
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(Released, cpu.Wram(Slot0State));
        Assert.Equal(Empty, cpu.Wram(Slot1State));
        Assert.Equal(0, cpu.Wram(VisibleCameraXLow));
        Assert.Equal(0, cpu.IoRegister(0xFF43));
        var writesAfterCancellation = cpu.VramWrites.Count;
        cpu.RunFrames(3);
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(writesAfterCancellation, cpu.VramWrites.Count);
    }

    [Fact]
    public void Reversal_never_mutates_a_slot_that_has_entered_committing()
    {
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Camera.Init(176, 0, 30);
                i16 target = 0;
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                    Input.Poll();
                    if (Input.IsDown(Button.Right)) {
                        target = 8;
                    }
                    if (Input.IsDown(Button.Left)) {
                        target = 0;
                    }
                    Camera.SetPosition(target, 0);
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "assets/maps/stage1.playable.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom) { CycleAccurateLy = true, EnforceVblankVramWrites = true };
        cpu.Held.Add("right");
        cpu.RunUntilWramEquals(Slot0State, Resident);
        cpu.SetWram(Slot0State, Committing);
        var committingMetadata = Enumerable.Range(0, 10)
            .Select(offset => cpu.Wram((ushort)(Slot0State + offset)))
            .ToArray();
        cpu.Held.Remove("right");
        cpu.Held.Add("left");

        cpu.RunAdditionalFrames(4);

        Assert.Equal(committingMetadata, Enumerable.Range(0, 10)
            .Select(offset => cpu.Wram((ushort)(Slot0State + offset)))
            .ToArray());
        Assert.Equal(Committing, cpu.Wram(Slot0State));
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(0, cpu.Wram(ReleaseCount));
    }

    [Theory]
    [InlineData(1, Row)]
    [InlineData(2, Negative)]
    [InlineData(3, 6)]
    [InlineData(5, 22)]
    public void Wrong_slot_tag_defers_without_mutating_or_releasing_the_resident_slot(int tagOffset, byte wrongValue)
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 32);
                Camera.SetPosition(8, 0);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "tall.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        cpu.RunUntilWramEquals(Slot0State, Resident);
        var residentPayload = Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(Slot0State + offset))).ToArray();
        cpu.SetWram((ushort)(Slot0State + tagOffset), wrongValue);
        var vramWrites = cpu.VramWrites.Count;
        var scrollWrites = cpu.RunUntilIoRegisterWrites(0xFF43, 1, 5_000_000);

        Assert.Equal(new byte[] { 0 }, scrollWrites);
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(0, cpu.Wram(ReleaseCount));
        Assert.Equal(Resident, cpu.Wram(Slot0State));
        Assert.Equal(vramWrites, cpu.VramWrites.Count);
        var afterDeferral = Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(Slot0State + offset))).ToArray();
        var expectedAfterDeferral = residentPayload.ToArray();
        expectedAfterDeferral[tagOffset] = wrongValue;
        Assert.Equal(expectedAfterDeferral, afterDeferral);

        cpu.SetWram((ushort)(Slot0State + tagOffset), residentPayload[tagOffset]);
        cpu.RunUntilWramEquals(ReleaseCount, 1);
        Assert.Equal(1, cpu.Wram(CommitCount));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Malformed_or_unavailable_edge_defers_the_crossing_and_holds_stable_frames(bool malformed)
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 32);
                i16 target = 0;
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                    Input.Poll();
                    if (Input.IsDown(Button.Right)) {
                        target = 8;
                    }
                    Camera.SetPosition(target, 0);
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "tall.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        cpu.RunUntilIoRegisterWrites(0xFF43, 1, 50_000_000); // startup SCX initialization
        cpu.RunUntilIoRegisterWrites(0xFF43, 1, 50_000_000); // first Camera.Apply; Main loop is live
        if (malformed)
        {
            cpu.SetWram(WorldPackValidationState, 2);
        }
        else
        {
            cpu.SetWram(CameraRightSourceColumn, 0xFF);
        }

        cpu.Held.Add("right");
        var writesBeforeDeferral = cpu.VramWrites.Count;
        cpu.RunUntilWramEquals(ReleaseCount, 1);

        Assert.Equal(1, cpu.Wram(RequestCount));
        Assert.Equal(1, cpu.Wram(PrepareCount));
        Assert.Equal(0, cpu.Wram(ResidentCount));
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(Released, cpu.Wram(Slot0State));
        Assert.Equal(writesBeforeDeferral, cpu.VramWrites.Count);
        cpu.RunUntilIoRegisterWrites(0xFF43, 1, 5_000_000);
        Assert.Equal(7, cpu.Wram(VisibleCameraXLow));
        Assert.Equal(7, cpu.IoRegister(0xFF43));

        cpu.RunUntilWramEquals(ReleaseCount, 2);
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(7, cpu.Wram(VisibleCameraXLow));
        Assert.Equal(writesBeforeDeferral, cpu.VramWrites.Count);
    }

    [Fact]
    public void Packed_camera_crosses_255_256_both_directions_across_chunk_and_physical_bank_boundaries()
    {
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                Video.Init();
                World.Column(0, 1);
                World.Column(256, 4);
                World.Column(311, 1);
                World.Map(312, 0, 1);
                Camera.Init(312, 0, 1);
                i16 target = 0;
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                    Input.Poll();
                    if (Input.IsDown(Button.Right)) {
                        target = 1888;
                    }
                    if (Input.IsDown(Button.Left)) {
                        target = 2055;
                    }
                    Camera.SetPosition(target, 0);
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: GameBoyWorldPackPlacementTests.CreateSyntheticWorldPack(chunkColumns: 200));
        Assert.Equal("gb-simple-mbc1-current", result.Report.SelectedProfile);
        Assert.True(result.Report.Segments.Count(segment => segment.Owner == "worldpack:default") >= 2);
        var cpu = new GameBoyTestCpu(result.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        cpu.RunUntilIoRegisterWrites(0xFF43, 1, 50_000_000);
        cpu.RunUntilIoRegisterWrites(0xFF43, 1, 500_000_000);
        SetHorizontalCameraState(cpu, 1887, 235, 0, 10, 256, 234);
        var bankWritesBeforeCrossings = cpu.RomBankWrites.Count;
        cpu.Held.Add("right");

        cpu.RunUntilWramEquals(RequestCount, 1, 500_000_000);
        var requestedAtCycles = cpu.Cycles;
        cpu.RunUntilWramEquals(ResidentCount, 1, 500_000_000);
        Assert.InRange(cpu.Cycles - requestedAtCycles, 0, GameBoyTestCpu.DmgCyclesPerFrame);
        cpu.RunUntilWramEquals(ReleaseCount, 1, 500_000_000);

        Assert.Equal(Column, cpu.Wram(LastCommittedAxis));
        Assert.Equal(Positive, cpu.Wram(LastCommittedDirection));
        Assert.Equal(0, cpu.Wram(LastCommittedWorldEdgeLow));
        Assert.Equal(1, cpu.Wram(LastCommittedWorldEdgeHigh));
        Assert.Equal(1888, cpu.Wram(VisibleCameraXLow) | (cpu.Wram(VisibleCameraXHigh) << 8));
        Assert.Equal(19, cpu.Wram(LastCommitVramWrites));

        cpu.Held.Remove("right");
        SetHorizontalCameraState(cpu, 2056, 257, 22, 0, 278, 256);
        cpu.SetWram(VisibleCameraXLow, 0x08);
        cpu.SetWram(VisibleCameraXHigh, 0x08);
        cpu.Held.Add("left");
        cpu.RunUntilWramEquals(RequestCount, 2, 500_000_000);
        requestedAtCycles = cpu.Cycles;
        cpu.RunUntilWramEquals(ResidentCount, 2, 500_000_000);
        Assert.InRange(cpu.Cycles - requestedAtCycles, 0, GameBoyTestCpu.DmgCyclesPerFrame);
        cpu.RunUntilWramEquals(ReleaseCount, 2, 500_000_000);

        Assert.Equal(Negative, cpu.Wram(LastCommittedDirection));
        Assert.Equal(0, cpu.Wram(LastCommittedWorldEdgeLow));
        Assert.Equal(1, cpu.Wram(LastCommittedWorldEdgeHigh));
        Assert.Equal(2055, cpu.Wram(VisibleCameraXLow) | (cpu.Wram(VisibleCameraXHigh) << 8));
        var crossingBankWrites = cpu.RomBankWrites.Skip(bankWritesBeforeCrossings).ToArray();
        Assert.NotEmpty(crossingBankWrites);
        Assert.All(crossingBankWrites, write => Assert.InRange(write.Ly, (byte)0, (byte)143));
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
    }

    [Fact]
    public void Packed_stalls_and_consecutive_crossings_keep_bgm_at_one_update_per_real_frame()
    {
        var directory = RepositoryDirectory("samples/runner");
        const string source = """
            void Main() {
                World.Load("assets/maps/stage1.playable.tmj");
                Music.Asset(theme, "assets/music/runner.vgz");
                Camera.Init(176, 0, 30);
                Audio.Init();
                Music.Play(theme);
                u8 frames = 0;
                i16 target = 0;
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                    Audio.Update();
                    frames += 1;
                    Input.Poll();
                    if (Input.IsDown(Button.Right)) {
                        target = 16;
                    }
                    Camera.SetPosition(target, 0);
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "assets/maps/stage1.playable.tmj"));
        Assert.Equal(11_614, result.Report.Segments.Where(segment => segment.Owner == "bgm:theme").Sum(segment => segment.Length));
        var cpu = new GameBoyTestCpu(result.Rom) { CycleAccurateLy = true, EnforceVblankVramWrites = true };
        cpu.RunUntilWramEquals(0xC000, 2, 100_000_000);
        var startAudio = cpu.AudioUpdateCalls;
        cpu.SetWram(WorldPackValidationState, 2);
        cpu.Held.Add("right");

        cpu.RunAdditionalFrames(8);

        Assert.Equal(8, cpu.AudioUpdateCalls - startAudio);
        Assert.Equal(0, cpu.Wram(CommitCount));
        Assert.Equal(7, cpu.Wram(VisibleCameraXLow));
        cpu.SetWram(WorldPackValidationState, 1);
        var audioBeforePreparation = cpu.AudioUpdateCalls;

        cpu.RunAdditionalFrames(12);

        Assert.Equal(12, cpu.AudioUpdateCalls - audioBeforePreparation);
        Assert.Equal(2, cpu.Wram(CommitCount));
        Assert.Equal(2, cpu.Wram(ResidentCount));
        Assert.Equal(16, cpu.Wram(VisibleCameraXLow));
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
    }

    [Fact]
    public void Two_vertical_crossings_keep_row_order_and_never_exceed_twenty_one_writes_per_vblank()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                Camera.SetPosition(0, 16);
                while (true) {
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "tall.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom) { CycleAccurateLy = true, EnforceVblankVramWrites = true };
        cpu.RunUntilWramEquals(Slot1State, Resident);
        Assert.Equal(Row, cpu.Wram(Slot0Axis));
        Assert.Equal(Row, cpu.Wram(Slot1Axis));
        Assert.Equal(19, cpu.Wram(Slot0WorldEdgeLow));
        Assert.Equal(20, cpu.Wram(Slot1WorldEdgeLow));
        var beforeFirst = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(ReleaseCount, 1);

        Assert.Equal(19, cpu.Wram(LastCommittedWorldEdgeLow));
        Assert.Equal(21, cpu.VramWrites.Count - beforeFirst);
        Assert.Equal(8, cpu.Wram(0xC14F));
        var beforeSecond = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(ReleaseCount, 2);

        Assert.Equal(20, cpu.Wram(LastCommittedWorldEdgeLow));
        Assert.Equal(21, cpu.VramWrites.Count - beforeSecond);
        Assert.Equal(16, cpu.Wram(0xC14F));
    }

    [Fact]
    public void Repeated_diagonal_crossings_alternate_column_row_column_row()
    {
        var directory = RepositoryDirectory("samples/tiled-tall");
        const string source = """
            void Main() {
                World.Load("tall.tmj");
                Camera.Init(16, 0, 40);
                while (true) {
                    Camera.SetPosition(16, 16);
                    Video.WaitVBlank();
                    Camera.Apply();
                }
            }
            """;
        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            packedWorldOverride: CompileWorldPack(directory, "tall.tmj"));
        var cpu = new GameBoyTestCpu(result.Rom) { CycleAccurateLy = true, EnforceVblankVramWrites = true };
        var observed = new byte[4];
        var visibleCoordinates = new (ushort X, ushort Y)[4];
        for (byte release = 1; release <= observed.Length; release++)
        {
            cpu.RunUntilWramEquals(ReleaseCount, release);
            observed[release - 1] = cpu.Wram(LastCommittedAxis);
            visibleCoordinates[release - 1] = (
                (ushort)(cpu.Wram(VisibleCameraXLow) | (cpu.Wram(VisibleCameraXHigh) << 8)),
                (ushort)(cpu.Wram(VisibleCameraYLow) | (cpu.Wram(VisibleCameraYHigh) << 8)));
        }

        Assert.Equal(new byte[] { Column, Row, Column, Row }, observed);
        Assert.Equal(new (ushort X, ushort Y)[] { (8, 7), (8, 15), (16, 15), (16, 16) }, visibleCoordinates);
        Assert.Equal(16, cpu.Wram(VisibleCameraXLow));
        Assert.Equal(16, cpu.Wram(0xC14F));
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
    }

    private static byte[] CompileWorldPack(string directory, string relativeMapPath) =>
        GameBoyTiledMapImporter.CompileWorldPack(
            Path.Combine(directory, relativeMapPath),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile).SerializedBytes;

    private static void SetHorizontalCameraState(
        GameBoyTestCpu cpu,
        ushort cameraX,
        ushort screenLeftColumn,
        byte rightBackgroundColumn,
        byte leftBackgroundColumn,
        ushort rightSourceColumn,
        ushort leftSourceColumn)
    {
        cpu.SetWram(CameraXLow, (byte)cameraX);
        cpu.SetWram(CameraXHigh, (byte)(cameraX >> 8));
        cpu.SetWram(CameraFineX, (byte)(cameraX & 7));
        cpu.SetWram(CameraScreenLeftColumn, (byte)screenLeftColumn);
        cpu.SetWram(CameraScreenLeftColumnHigh, (byte)(screenLeftColumn >> 8));
        cpu.SetWram(CameraRightBackgroundColumn, rightBackgroundColumn);
        cpu.SetWram(CameraLeftBackgroundColumn, leftBackgroundColumn);
        cpu.SetWram(CameraRightSourceColumn, (byte)rightSourceColumn);
        cpu.SetWram(CameraRightSourceColumnHigh, (byte)(rightSourceColumn >> 8));
        cpu.SetWram(CameraLeftSourceColumn, (byte)leftSourceColumn);
        cpu.SetWram(CameraLeftSourceColumnHigh, (byte)(leftSourceColumn >> 8));
    }
}
