namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using static NesSdkOperationBoundaryTests;

public sealed class NesSharedSdkOperationSubroutineTests
{
    [Fact]
    public void Repeated_one_piece_shape_stays_inline_when_a_call_would_not_save_code()
    {
        var baseDirectory = WriteSpriteAsset(
            "marker.nes.json",
            """
            {"platforms":{"nes":{"frames":[[
              "11111111", "11111111", "11111111", "11111111",
              "11111111", "11111111", "11111111", "11111111"
            ]]}}}
            """);
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(marker, "marker.nes.json");
                                  u8 x = 24;
                                  u8 y = 32;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(marker, x, y, 0, false, 0);
                                      Sprite.Draw(marker, x, 96, 0, false, 0);
                                  }
                              }
                              """;
        var program = NesVideoProgram.FromProgram(ParseLoweredProgram(source), baseDirectory);

        var shared = EmitSectioned(program, shareRepeatedSdkOperations: true);
        var unrolled = EmitSectioned(program, shareRepeatedSdkOperations: false);

        Assert.DoesNotContain(
            shared.Labels,
            pair => pair.Key.StartsWith("nes_sdk_shared_draw_logical_sprite_", StringComparison.Ordinal));
        Assert.Equal(TotalBytes(unrolled), TotalBytes(shared));
    }

    [Fact]
    public void Repeated_sprite_shape_emits_one_fixed_body_and_program_calls()
    {
        var rows = string.Join(",", Enumerable.Repeat("\"1111111111111111\"", 16));
        var baseDirectory = WriteSpriteAsset(
            "hero.nes.json",
            "{\"platforms\":{\"nes\":{\"frames\":[[" + rows + "],[" + rows + "]]}}}");
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero, "hero.nes.json");
                                  u8 x = 24;
                                  u8 y = 32;
                                  u8 frame = 0;
                                  bool flip = false;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(hero, x, y, frame, flip, 0);
                                      Sprite.Draw(hero, x, 96, frame, flip, 0);
                                  }
                              }
                              """;
        var program = NesVideoProgram.FromProgram(ParseLoweredProgram(source), baseDirectory);

        var shared = EmitSectioned(program, shareRepeatedSdkOperations: true);
        var unrolled = EmitSectioned(program, shareRepeatedSdkOperations: false);

        var helper = Assert.Single(
            shared.Labels,
            pair => pair.Key.StartsWith("nes_sdk_shared_draw_logical_sprite_", StringComparison.Ordinal));
        Assert.Null(helper.Value.PlacementUnitName);
        var calls = shared.Relocations
            .Where(relocation => relocation.Kind is NesPrgRelocationKind.AbsoluteCall && relocation.Label == helper.Key)
            .ToArray();
        Assert.Equal(2, calls.Length);
        var programUnit = Assert.Single(shared.PlacementUnits);
        Assert.Equal("program:test", programUnit.Name);
        Assert.Equal(NesPrgResidence.ProgramR6, programUnit.Residence);
        Assert.All(calls, call => Assert.Equal(programUnit.Name, call.PlacementUnitName));
        Assert.DoesNotContain(
            unrolled.Labels,
            pair => pair.Key.StartsWith("nes_sdk_shared_draw_logical_sprite_", StringComparison.Ordinal));
        Assert.True(
            TotalBytes(shared) < TotalBytes(unrolled),
            $"shared={TotalBytes(shared)}, unrolled={TotalBytes(unrolled)}");
    }

    // Collision canary on the unmodified `samples/platformer-landing` production build.
    //
    // The sample already probes the same AABB shapes from more than one call site, so the
    // repetition is authored, not manufactured for this test. Both builds come from the
    // same tree and differ only in `shareRepeatedSdkOperations`, which makes the control
    // the honest comparison point.
    [Fact]
    public void Platformer_landing_shares_repeated_collision_probes_and_keeps_its_frame_budget()
    {
        var shared = BuildPlatformerLanding(shareRepeatedSdkOperations: true);
        var control = BuildPlatformerLanding(shareRepeatedSdkOperations: false);

        Assert.Empty(control.Report.SharedSdkSubroutines);
        Assert.Equal(2, shared.Report.SharedSdkSubroutines.Count);
        Assert.All(shared.Report.SharedSdkSubroutines, subroutine =>
        {
            Assert.StartsWith("nes_sdk_shared_camera_aabb_", subroutine.Label, StringComparison.Ordinal);
            Assert.True(
                subroutine.CallSites >= 2,
                $"{subroutine.Label} must be reached from more than one site, saw {subroutine.CallSites}.");
            Assert.True(
                subroutine.CpuAddress >= 0x8000,
                $"{subroutine.Label} must live in fixed PRG, saw ${subroutine.CpuAddress:X4}.");
        });

        Assert.True(
            shared.Report.FixedPayloadBytes + 64 < control.Report.FixedPayloadBytes,
            $"shared={shared.Report.FixedPayloadBytes}, control={control.Report.FixedPayloadBytes}");

        var sharedSteady = ObserveSteadyState(shared.Rom);
        var controlSteady = ObserveSteadyState(control.Rom);

        Assert.Equal(SteadyStateFrames, sharedSteady.PhysicalFrames);
        Assert.Equal(SteadyStateFrames, sharedSteady.LogicalTicks);
        Assert.Equal(1, sharedSteady.ResetCount);
        Assert.Equal(0, sharedSteady.UnsafePpuWrites);
        Assert.Equal(0, sharedSteady.UnsafeOamWrites);

        // The call overhead is a real cost and is bounded rather than assumed away. One NTSC
        // frame is 29780 CPU cycles; sharing may not spend more than ~1% of it. A rolled
        // probe grid measured +423 here and fails this budget, so the bound discriminates.
        Assert.True(
            sharedSteady.PeakTickCycles <= controlSteady.PeakTickCycles + 256,
            $"shared peak={sharedSteady.PeakTickCycles}, control peak={controlSteady.PeakTickCycles}");
        Assert.True(
            sharedSteady.PeakTickCycles < NtscCyclesPerFrame,
            $"shared peak={sharedSteady.PeakTickCycles} must complete inside one physical frame.");
    }

    private const int SteadyStateFrames = 120;
    private const int NtscCyclesPerFrame = 29_780;

    private static RetroSharp.NES.NesRomBuildResult BuildPlatformerLanding(bool shareRepeatedSdkOperations)
    {
        var sampleDirectory = RepositoryDirectory("samples/platformer-landing");
        return RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            File.ReadAllText(Path.Combine(sampleDirectory, "src", "main.rs")),
            sampleDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null,
            shareRepeatedSdkOperations: shareRepeatedSdkOperations);
    }

    private readonly record struct SteadyState(
        int PhysicalFrames,
        long LogicalTicks,
        int PeakTickCycles,
        int ResetCount,
        int UnsafePpuWrites,
        int UnsafeOamWrites);

    private static SteadyState ObserveSteadyState(byte[] rom)
    {
        var cpu = new NesTestCpu(rom);
        cpu.Held.Add("right");
        cpu.TracedRamBytes.Add(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        cpu.RunFrames(40);

        var startFrame = cpu.PhysicalFrames;
        var startTicks = cpu.VBlankWaitCompletions;
        var startNmi = cpu.NmiCompletionCycles.Count;
        var startWrite = cpu.RamByteWrites.Count;
        var startPpu = cpu.PpuWrites.Count;
        var startOam = cpu.OamWrites.Count;
        cpu.RunFrames(startFrame + SteadyStateFrames);

        var nmiCycles = cpu.NmiCompletionCycles.Skip(startNmi).ToArray();
        var publications = cpu.RamByteWrites
            .Skip(startWrite)
            .Where(write =>
                write.Address == NesRuntimeMemoryLayout.Camera.ScrollApplied &&
                write.Value == (byte)NesCameraPublicationState.Ready)
            .ToArray();
        var tickCycles = nmiCycles
            .Zip(nmiCycles.Skip(1), (start, end) => (Start: start, End: end))
            .Select(window => publications
                .Where(write => write.Cycle >= window.Start && write.Cycle < window.End)
                .Select(write => write.Cycle - window.Start)
                .DefaultIfEmpty()
                .Max())
            .Where(cycles => cycles > 0)
            .ToArray();

        return new SteadyState(
            cpu.PhysicalFrames - startFrame,
            cpu.VBlankWaitCompletions - startTicks,
            tickCycles.Length == 0 ? -1 : (int)tickCycles.Max(),
            cpu.ResetCount,
            CountUnsafe(cpu, cpu.PpuWrites.Skip(startPpu).Select(write => (write.Cycle, write.RenderingEnabled))),
            CountUnsafe(cpu, cpu.OamWrites.Skip(startOam).Select(write => (write.Cycle, write.RenderingEnabled))));
    }

    private static int CountUnsafe(NesTestCpu cpu, IEnumerable<(long Cycle, bool RenderingEnabled)> writes) =>
        writes.Count(write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank");

    private static string RepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(relativePath);
    }

    private static NesPrgEmission EmitSectioned(NesVideoProgram program, bool shareRepeatedSdkOperations)
    {
        var builder = PrgBuilder.CreateSectioned(0xC000);
        var compiler = new NesRuntimeCompiler(
            builder,
            program,
            useFourScreenNametables: false,
            usePackedCamera: false,
            useSequentialOamPublication: false,
            shareRepeatedSdkOperations: shareRepeatedSdkOperations);
        compiler.EmitInitialization();
        using (builder.EnterPlacementUnit("program:test", NesPrgResidence.ProgramR6))
        {
            compiler.Emit(program.MainBlock);
        }

        compiler.EmitReferencedSubroutines();
        return builder.FreezeForLink();
    }

    private static int TotalBytes(NesPrgEmission emission) =>
        emission.FixedSection.Bytes.Length + emission.PlacementUnits.Sum(unit => unit.Bytes.Length);

}
