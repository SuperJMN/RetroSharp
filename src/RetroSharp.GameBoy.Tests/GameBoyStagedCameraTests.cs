namespace RetroSharp.GameBoy.Tests;

using System;
using System.IO;
using System.Linq;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;
using CameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.Camera;
using PackedCameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.PackedCamera;
using WorldPackMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.WorldPack;

public sealed class GameBoyStagedCameraTests
{
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

        cpu.RunUntilWramEquals(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset, Resident);

        Assert.Equal(1, cpu.Wram(PackedCameraMemory.RequestCount));
        Assert.Equal(1, cpu.Wram(PackedCameraMemory.PrepareCount));
        Assert.Equal(1, cpu.Wram(PackedCameraMemory.ResidentCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.ReleaseCount));
        Assert.Equal(Column, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.AxisOffset));
        Assert.Equal(Positive, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.DirectionOffset));
        Assert.Equal(5, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.WorldEdgeLowOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.WorldEdgeHighOffset));
        Assert.Equal(21, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.TargetOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.OrthogonalLowOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.OrthogonalHighOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        Assert.Equal(0, cpu.IoRegister(0xFF43));

        var bankWritesBeforeCommit = cpu.RomBankWrites.Count;
        var vramWritesBeforeCommit = cpu.VramWrites.Count;
        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1);

        Assert.Equal(Released, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(1, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(19, cpu.Wram(PackedCameraMemory.LastCommitVramWrites));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CriticalSection));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank));
        Assert.Empty(cpu.RomBankWrites.Skip(bankWritesBeforeCommit));
        var commitWrites = cpu.VramWrites.Skip(vramWritesBeforeCommit).ToArray();
        Assert.Equal(19, commitWrites.Length);
        Assert.All(commitWrites, write =>
        {
            Assert.True(write.Applied);
            Assert.InRange(write.Ly, (byte)144, (byte)153);
        });
        Assert.Equal(8, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
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

        cpu.RunUntilWramEquals(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset, Resident);
        Assert.Equal(2, cpu.Wram(PackedCameraMemory.RequestCount));
        Assert.Equal(Resident, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(Resident, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(Column, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.AxisOffset));
        Assert.Equal(Positive, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.DirectionOffset));
        Assert.Equal(21, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.WorldEdgeLowOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.WorldEdgeHighOffset));
        Assert.Equal(22, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.WorldEdgeLowOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.WorldEdgeHighOffset));
        Assert.Equal(22, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.TargetOffset));
        var retainedSlot1 = Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset + offset))).ToArray();
        var writesBeforeFirstCommit = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1);

        Assert.Equal(0, cpu.Wram(PackedCameraMemory.SelectedSlot));
        Assert.Equal(21, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeLow));
        Assert.Equal(Released, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(Resident, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(retainedSlot1, Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset + offset))).ToArray());
        Assert.Equal(8, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        var firstWrites = cpu.VramWrites.Skip(writesBeforeFirstCommit).ToArray();
        Assert.Equal(19, firstWrites.Length);
        Assert.All(firstWrites, write => Assert.InRange(write.Ly, (byte)144, (byte)153));
        var writesBeforeSecondCommit = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 2);

        Assert.Equal(1, cpu.Wram(PackedCameraMemory.SelectedSlot));
        Assert.Equal(22, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeLow));
        Assert.Equal(Released, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(16, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        var secondWrites = cpu.VramWrites.Skip(writesBeforeSecondCommit).ToArray();
        Assert.Equal(19, secondWrites.Length);
        Assert.All(secondWrites, write => Assert.InRange(write.Ly, (byte)144, (byte)153));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank));
    }

    [Fact]
    public void Diagonal_staging_serializes_preparation_then_commits_column_first_and_row_second()
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

        cpu.RunUntilWramEquals(WorldPackMemory.ValidationState, 1);
        var writesBeforeColumn = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1);

        Assert.Equal(Column, cpu.Wram(PackedCameraMemory.LastCommittedAxis));
        Assert.Equal(Released, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(8, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        Assert.Equal(7, cpu.Wram(0xC14F));
        Assert.Equal(19, cpu.VramWrites.Count - writesBeforeColumn);
        var writesBeforeRow = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 2);

        Assert.Equal(Row, cpu.Wram(PackedCameraMemory.LastCommittedAxis));
        Assert.Equal(8, cpu.Wram(0xC14F));
        Assert.Equal(21, cpu.VramWrites.Count - writesBeforeRow);
        Assert.Equal(2, cpu.Wram(PackedCameraMemory.ResidentCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank));
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

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1);

        Assert.Equal(1, cpu.Wram(PackedCameraMemory.RequestCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(Released, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(Empty, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        Assert.Equal(0, cpu.IoRegister(0xFF43));
        var writesAfterCancellation = cpu.VramWrites.Count;
        cpu.RunFrames(3);
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
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
        cpu.RunUntilWramEquals(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset, Resident);
        cpu.SetWram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset, Committing);
        var committingMetadata = Enumerable.Range(0, 10)
            .Select(offset => cpu.Wram((ushort)(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset + offset)))
            .ToArray();
        cpu.Held.Remove("right");
        cpu.Held.Add("left");

        cpu.RunAdditionalFrames(4);

        Assert.Equal(committingMetadata, Enumerable.Range(0, 10)
            .Select(offset => cpu.Wram((ushort)(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset + offset)))
            .ToArray());
        Assert.Equal(Committing, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.ReleaseCount));
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

        cpu.RunUntilWramEquals(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset, Resident);
        var residentPayload = Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset + offset))).ToArray();
        cpu.SetWram((ushort)(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset + tagOffset), wrongValue);
        var vramWrites = cpu.VramWrites.Count;
        var scrollWrites = cpu.RunUntilIoRegisterWrites(0xFF43, 1, 5_000_000);

        Assert.Equal(new byte[] { 0 }, scrollWrites);
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.ReleaseCount));
        Assert.Equal(Resident, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(vramWrites, cpu.VramWrites.Count);
        var afterDeferral = Enumerable.Range(0, 10).Select(offset => cpu.Wram((ushort)(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset + offset))).ToArray();
        var expectedAfterDeferral = residentPayload.ToArray();
        expectedAfterDeferral[tagOffset] = wrongValue;
        Assert.Equal(expectedAfterDeferral, afterDeferral);

        cpu.SetWram((ushort)(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset + tagOffset), residentPayload[tagOffset]);
        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1);
        Assert.Equal(1, cpu.Wram(PackedCameraMemory.CommitCount));
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
            cpu.SetWram(WorldPackMemory.ValidationState, 2);
        }
        else
        {
            cpu.SetWram(CameraMemory.RightSourceColumn, 0xFF);
        }

        cpu.Held.Add("right");
        var writesBeforeDeferral = cpu.VramWrites.Count;
        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1);

        Assert.Equal(1, cpu.Wram(PackedCameraMemory.RequestCount));
        Assert.Equal(1, cpu.Wram(PackedCameraMemory.PrepareCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.ResidentCount));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(Released, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.StateOffset));
        Assert.Equal(writesBeforeDeferral, cpu.VramWrites.Count);
        cpu.RunUntilIoRegisterWrites(0xFF43, 1, 5_000_000);
        Assert.Equal(7, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        Assert.Equal(7, cpu.IoRegister(0xFF43));

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 2);
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(7, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
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

        cpu.RunUntilWramEquals(PackedCameraMemory.RequestCount, 1, 500_000_000);
        var requestedAtCycles = cpu.Cycles;
        cpu.RunUntilWramEquals(PackedCameraMemory.ResidentCount, 1, 500_000_000);
        Assert.InRange(cpu.Cycles - requestedAtCycles, 0, GameBoyTestCpu.DmgCyclesPerFrame);
        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1, 500_000_000);

        Assert.Equal(Column, cpu.Wram(PackedCameraMemory.LastCommittedAxis));
        Assert.Equal(Positive, cpu.Wram(PackedCameraMemory.LastCommittedDirection));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeLow));
        Assert.Equal(1, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeHigh));
        Assert.Equal(1888, cpu.Wram(PackedCameraMemory.VisibleCameraXLow) | (cpu.Wram(PackedCameraMemory.VisibleCameraXHigh) << 8));
        Assert.Equal(19, cpu.Wram(PackedCameraMemory.LastCommitVramWrites));

        cpu.Held.Remove("right");
        SetHorizontalCameraState(cpu, 2056, 257, 22, 0, 278, 256);
        cpu.SetWram(PackedCameraMemory.VisibleCameraXLow, 0x08);
        cpu.SetWram(PackedCameraMemory.VisibleCameraXHigh, 0x08);
        cpu.Held.Add("left");
        cpu.RunUntilWramEquals(PackedCameraMemory.RequestCount, 2, 500_000_000);
        requestedAtCycles = cpu.Cycles;
        cpu.RunUntilWramEquals(PackedCameraMemory.ResidentCount, 2, 500_000_000);
        Assert.InRange(cpu.Cycles - requestedAtCycles, 0, GameBoyTestCpu.DmgCyclesPerFrame);
        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 2, 500_000_000);

        Assert.Equal(Negative, cpu.Wram(PackedCameraMemory.LastCommittedDirection));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeLow));
        Assert.Equal(1, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeHigh));
        Assert.Equal(2055, cpu.Wram(PackedCameraMemory.VisibleCameraXLow) | (cpu.Wram(PackedCameraMemory.VisibleCameraXHigh) << 8));
        var crossingBankWrites = cpu.RomBankWrites.Skip(bankWritesBeforeCrossings).ToArray();
        Assert.NotEmpty(crossingBankWrites);
        Assert.All(crossingBankWrites, write => Assert.InRange(write.Ly, (byte)0, (byte)143));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank));
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
        cpu.SetWram(WorldPackMemory.ValidationState, 2);
        cpu.Held.Add("right");

        cpu.RunAdditionalFrames(8);

        Assert.Equal(8, cpu.AudioUpdateCalls - startAudio);
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(7, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        cpu.SetWram(WorldPackMemory.ValidationState, 1);
        var audioBeforePreparation = cpu.AudioUpdateCalls;

        cpu.RunAdditionalFrames(12);

        Assert.Equal(12, cpu.AudioUpdateCalls - audioBeforePreparation);
        Assert.Equal(2, cpu.Wram(PackedCameraMemory.CommitCount));
        Assert.Equal(2, cpu.Wram(PackedCameraMemory.ResidentCount));
        Assert.Equal(16, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank));
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
        cpu.RunUntilWramEquals(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.StateOffset, Resident);
        Assert.Equal(Row, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.AxisOffset));
        Assert.Equal(Row, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.AxisOffset));
        Assert.Equal(19, cpu.Wram(PackedCameraMemory.Slot0 + GameBoyPackedCameraRuntime.WorldEdgeLowOffset));
        Assert.Equal(20, cpu.Wram(PackedCameraMemory.Slot1 + GameBoyPackedCameraRuntime.WorldEdgeLowOffset));
        var beforeFirst = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 1);

        Assert.Equal(19, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeLow));
        Assert.Equal(21, cpu.VramWrites.Count - beforeFirst);
        Assert.Equal(8, cpu.Wram(0xC14F));
        var beforeSecond = cpu.VramWrites.Count;

        cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, 2);

        Assert.Equal(20, cpu.Wram(PackedCameraMemory.LastCommittedWorldEdgeLow));
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
            cpu.RunUntilWramEquals(PackedCameraMemory.ReleaseCount, release);
            observed[release - 1] = cpu.Wram(PackedCameraMemory.LastCommittedAxis);
            visibleCoordinates[release - 1] = (
                (ushort)(cpu.Wram(PackedCameraMemory.VisibleCameraXLow) | (cpu.Wram(PackedCameraMemory.VisibleCameraXHigh) << 8)),
                (ushort)(cpu.Wram(PackedCameraMemory.VisibleCameraYLow) | (cpu.Wram(PackedCameraMemory.VisibleCameraYHigh) << 8)));
        }

        Assert.Equal(new byte[] { Column, Row, Column, Row }, observed);
        Assert.Equal(new (ushort X, ushort Y)[] { (8, 7), (8, 15), (16, 15), (16, 16) }, visibleCoordinates);
        Assert.Equal(16, cpu.Wram(PackedCameraMemory.VisibleCameraXLow));
        Assert.Equal(16, cpu.Wram(0xC14F));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank));
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
        cpu.SetWram(CameraMemory.XLow, (byte)cameraX);
        cpu.SetWram(CameraMemory.XHigh, (byte)(cameraX >> 8));
        cpu.SetWram(CameraMemory.FineX, (byte)(cameraX & 7));
        cpu.SetWram(CameraMemory.ScreenLeftColumn, (byte)screenLeftColumn);
        cpu.SetWram(CameraMemory.ScreenLeftColumnHigh, (byte)(screenLeftColumn >> 8));
        cpu.SetWram(CameraMemory.RightBackgroundColumn, rightBackgroundColumn);
        cpu.SetWram(CameraMemory.LeftBackgroundColumn, leftBackgroundColumn);
        cpu.SetWram(CameraMemory.RightSourceColumn, (byte)rightSourceColumn);
        cpu.SetWram(CameraMemory.RightSourceColumnHigh, (byte)(rightSourceColumn >> 8));
        cpu.SetWram(CameraMemory.LeftSourceColumn, (byte)leftSourceColumn);
        cpu.SetWram(CameraMemory.LeftSourceColumnHigh, (byte)(leftSourceColumn >> 8));
    }
}
