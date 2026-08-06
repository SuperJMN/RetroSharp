namespace RetroSharp.NES.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public partial class NesRomCompilerTests
{
    [Fact]
    public void Automatic_selector_preserves_the_historical_dpcm_promotion_boundary()
    {
        var dpcmOverflow = new InvalidOperationException();
        dpcmOverflow.Data[nameof(NesLinkConstraint)] = NesLinkConstraint.Mapper0Dpcm;
        var programOverflow = new InvalidOperationException();
        programOverflow.Data[nameof(NesLinkConstraint)] = NesLinkConstraint.Mapper0Prg;

        Assert.False(NesRomBuilder.ShouldPromoteMapper0ToMmc3(dpcmOverflow, hasPackedWorld: false));
        Assert.True(NesRomBuilder.ShouldPromoteMapper0ToMmc3(dpcmOverflow, hasPackedWorld: true));
        Assert.True(NesRomBuilder.ShouldPromoteMapper0ToMmc3(programOverflow, hasPackedWorld: false));
    }

    [Fact]
    public void Combined_world_and_program_r6_capacity_reports_whole_bank_ownership()
    {
        var body = string.Join(Environment.NewLine, Enumerable.Repeat("value += 1;", 3_600));
        var source = $$"""
                       void Main() {
                           u8 value = 0;
                           {{body}}
                       }
                       """;
        var packedWorld = NesWorldPackPlacementTests.CreateSyntheticWorldPack(chunkColumns: 1);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromCodeBankTestsWithReport(
                source,
                packedWorldOverride: packedWorld));

        Assert.Equal(NesLinkConstraint.Mmc3R6Capacity, exception.Data[nameof(NesLinkConstraint)]);
        Assert.Equal(
            $"NES MMC3/TVROM R6 capacity overflow: WorldPack owns 1 bank(s) ({packedWorld.Length} bytes) and program requires 4 bank(s) (25216 linked bytes), but R6 banks [0, 3, 4, 5] provide 4 whole banks.",
            exception.Message);
    }

    [Fact]
    public void Cross_bank_loop_break_continue_and_nmi_preserve_gameplay_state()
    {
        var loopBody = string.Join(Environment.NewLine, Enumerable.Repeat("score += 1;", 1_200));
        var source = $$"""
                       void Main() {
                           u8 iterations = 0;
                           u8 score = 0;
                           while (iterations < 3) {
                               iterations += 1;
                               {{loopBody}}
                               if (iterations == 2) {
                                   continue;
                               }
                               score += 7;
                               if (iterations == 3) {
                                   break;
                               }
                           }
                       }
                       """;

        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromCodeBankTestsWithReport(source);
        var program = result.Report.Segments
            .Where(segment => segment.Owner.StartsWith("program:r6:", StringComparison.Ordinal))
            .ToArray();
        var variables = result.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var visitedBanks = new HashSet<int>();
        var cpu = new NesTestCpu(result.Rom);
        cpu.InjectNmiAfterSelecting(checked((byte)program[1].PhysicalBank));
        cpu.OnStep = step =>
        {
            if (step.ProgramCounter is >= 0x8000 and <= 0x9FFF)
            {
                visitedBanks.Add(cpu.CurrentR6Bank);
            }
        };

        cpu.RunFrames(8);

        Assert.Equal(2, program.Length);
        Assert.Equal(program.Select(segment => segment.PhysicalBank).ToHashSet(), visitedBanks);
        Assert.Equal(3, cpu.Ram(variables["iterations"].Address));
        Assert.Equal(unchecked((byte)((3 * 1_200) + 14)), cpu.Ram(variables["score"].Address));
        Assert.Equal(1, cpu.NmiCount);
        Assert.Equal(1, cpu.ResetCount);
        Assert.Equal(cpu.CurrentR6Bank, cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow));
    }

    [Fact]
    public void Banked_gameplay_keeps_worldpack_and_pinned_audio_bank_ownership()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            NesCodeBankingValidationFixture.Source,
            NesCodeBankingValidationFixture.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var world = Assert.Single(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        var program = result.Report.Segments
            .Where(segment => segment.Owner.StartsWith("program:r6:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(NesRomBuilder.CodeBankedProfileName, result.Report.SelectedProfile);
        Assert.Equal(0, world.PhysicalBank);
        Assert.InRange(program.Length, 2, 3);
        Assert.DoesNotContain(program, segment => segment.PhysicalBank == world.PhysicalBank);
        Assert.All(program, segment => Assert.Contains(segment.PhysicalBank, new[] { 3, 4, 5 }));

        var visitedProgramBanks = new HashSet<int>();
        var expectedProgramBanks = program.Select(segment => segment.PhysicalBank).ToHashSet();
        var valueAddress = Assert.Single(
            result.Report.UserVariables,
            variable => variable.Name == "counter.value").Address;
        var cpu = new NesTestCpu(result.Rom);

        // Record the mapped R6 bank unfiltered, the way the sibling observers in
        // NesMmc3PrgBoardTests and Cross_bank_loop_break_continue_and_nmi_preserve_gameplay_state
        // do. Skipping non-program banks here would have made the comparison below unable to
        // observe banked execution running with a world data bank mapped, which is exactly the
        // reachability failure a missing R6 restore produces.
        cpu.OnStep = step =>
        {
            if (step.ProgramCounter is >= 0x8000 and <= 0x9FFF)
            {
                visitedProgramBanks.Add(cpu.CurrentR6Bank);
            }
        };
        for (var frame = 1;
             frame <= 900 &&
             (visitedProgramBanks.Count != expectedProgramBanks.Count ||
              cpu.Ram(valueAddress) != 171);
             frame++)
        {
            cpu.RunFrames(frame);
        }

        Assert.Equal(expectedProgramBanks, visitedProgramBanks);
        // The generated fold stream ends with counter.value == 171; see the fixture source header.
        Assert.Equal(171, cpu.Ram(valueAddress));
        Assert.Equal(1, cpu.ResetCount);
        Assert.NotEmpty(cpu.ApuWrites);
        Assert.Equal(1, cpu.CurrentR7Bank);
        Assert.Equal(1, cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R7Shadow));
        Assert.Equal(new[] { 2, 1 }, cpu.R7BankWrites);

        var callerBank = cpu.CurrentR6Bank;
        cpu.SetPackOffset(0);
        var read = cpu.RunRoutine(result.Report.FixedSymbols[NesRomBuilder.WorldPackReadByteLabel]);

        Assert.Equal(result.Rom[16 + world.PhysicalStart], read.A);
        Assert.Equal(callerBank, cpu.CurrentR6Bank);
        Assert.Equal(callerBank, cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow));
    }

    [Fact]
    public void Automatic_linker_selects_codebank_only_after_fixed_program_overflow()
    {
        var body = string.Join(Environment.NewLine, Enumerable.Repeat("value += 1;", 4_500));
        var source = $$"""
                       void Main() {
                           u8 value = 0;
                           {{body}}
                       }
                       """;

        var first = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(source);
        var second = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(source);
        var programSegments = first.Report.Segments
            .Where(segment => segment.Owner.StartsWith("program:r6:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(NesRomBuilder.CodeBankedProfileName, first.Report.SelectedProfile);
        Assert.Equal(first.Report.SelectedProfile, first.Report.CpuWork.Profile);
        Assert.InRange(programSegments.Length, 2, 4);
        Assert.Equal(programSegments.Length, programSegments.Select(segment => segment.PhysicalBank).Distinct().Count());
        Assert.All(programSegments, segment => Assert.Contains(segment.PhysicalBank, new[] { 0, 3, 4, 5 }));
        Assert.Equal(first.Report.Segments, second.Report.Segments);
        Assert.Equal(first.Report.BankedSymbols, second.Report.BankedSymbols);

        var expectedBanks = programSegments.Select(segment => segment.PhysicalBank).ToHashSet();
        var valueAddress = Assert.Single(first.Report.UserVariables, variable => variable.Name == "value").Address;
        foreach (var build in new[] { first, second })
        {
            var visitedBanks = new HashSet<int>();
            var cpu = new NesTestCpu(build.Rom);
            cpu.OnStep = step =>
            {
                if (step.ProgramCounter is >= 0x8000 and <= 0x9FFF)
                {
                    visitedBanks.Add(cpu.CurrentR6Bank);
                }
            };

            cpu.RunFrames(12);

            Assert.Equal(expectedBanks, visitedBanks);
            Assert.Equal(unchecked((byte)4_500), cpu.Ram(valueAddress));
            Assert.Equal(1, cpu.ResetCount);
            Assert.Equal(cpu.CurrentR6Bank, cpu.Ram(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow));
            Assert.DoesNotContain(
                cpu.PpuWrites,
                write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank");
            Assert.DoesNotContain(
                cpu.OamWrites,
                write => write.RenderingEnabled && cpu.PpuTiming(write.Cycle, true).Phase != "vblank");
        }
    }

    [Fact]
    public void Tracked_executable_banking_sample_selects_the_codebank_profile()
    {
        var sourcePath = RepositoryFile("samples/executable-banking/executable-banking.rs");
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            File.ReadAllText(sourcePath),
            Path.GetDirectoryName(sourcePath));
        var unit = Assert.Single(result.Report.PlacementUnits);

        Assert.Equal(NesRomBuilder.CodeBankedProfileName, result.Report.SelectedProfile);
        Assert.Equal(NesRomBuilder.MainInitPlacementUnitName, unit.Name);
        Assert.Equal(NesPrgResidence.ProgramR6, unit.Residence);
        Assert.Equal(NesPrgPlacementPhase.OneShot, unit.Phase);
        Assert.Equal(result.Report.ProgramR6Bytes, unit.Size);
        // Lower bound only: the sample must genuinely span several R6 banks for the banked path to
        // be exercised. Pinning an upper bound would couple this test to how close the sample sits
        // to the board's program-bank pool, which grows with codegen and board size.
        Assert.True(
            result.Report.Segments.Count(segment =>
                segment.Owner.StartsWith("program:r6:", StringComparison.Ordinal)) >= 2,
            "The tracked executable-banking sample must span at least two R6 program banks.");
    }

    [Fact]
    public void Forced_codebank_link_keeps_gameplay_in_r6_and_runtime_in_fixed_prg()
    {
        const string source = """
                              void Main() {
                                  u8 value = 1;
                                  value += 2;
                              }
                              """;

        var result = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromCodeBankTestsWithReport(source);
        var program = Assert.Single(result.Report.Segments, segment => segment.Owner == "program:r6:0");

        Assert.Equal(NesRomBuilder.CodeBankedProfileName, result.Report.SelectedProfile);
        Assert.Equal(result.Report.SelectedProfile, result.Report.CpuWork.Profile);
        Assert.Equal("R6 program $8000-$9FFF", program.Window);
        Assert.Equal(0, program.PhysicalBank);
        Assert.Equal(0x8000, program.CpuAddress);
        Assert.True(result.Report.ProgramR6Bytes > 0);
        Assert.True(result.Report.FixedVeneerBytes >= 12);
        Assert.Contains("banked_program_entry", result.Report.BankedSymbols.Keys);
        Assert.All(result.Report.BankedSymbols.Values, symbol =>
        {
            Assert.Equal(NesPrgResidence.ProgramR6, symbol.Residence);
            Assert.InRange(symbol.CpuAddress, 0x8000, 0x9FFF);
        });
        Assert.Equal(new byte[] { 0x04, 0x02, 0x48, 0x00 }, result.Rom[4..8]);
    }

    [Fact]
    public void Build_report_lists_main_phase_placement_units_in_both_residences()
    {
        const string source = """
                              void Main() {
                                  u8 value = 1;
                                  value += 2;
                              }
                              """;

        var fixedBuild = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(source);
        var bankedBuild = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromCodeBankTestsWithReport(source);
        var fixedUnit = Assert.Single(fixedBuild.Report.PlacementUnits);
        var bankedUnit = Assert.Single(bankedBuild.Report.PlacementUnits);

        Assert.Equal(NesRomBuilder.MainInitPlacementUnitName, fixedUnit.Name);
        Assert.Equal(NesPrgResidence.Fixed, fixedUnit.Residence);
        Assert.Equal(NesPrgPlacementPhase.OneShot, fixedUnit.Phase);
        Assert.Equal(NesRomBuilder.MainInitPlacementUnitName, bankedUnit.Name);
        Assert.Equal(NesPrgResidence.ProgramR6, bankedUnit.Residence);
        Assert.Equal(NesPrgPlacementPhase.OneShot, bankedUnit.Phase);
        Assert.Equal(fixedUnit.Size, bankedUnit.Size);
        Assert.True(fixedUnit.Size > 0);
    }

    [Fact]
    public void Runner_shaped_source_reports_cold_init_hot_frame_and_cold_tail_units()
    {
        const string source = """
                              [intrinsic("wait_frame")]
                              extern void wait_frame();

                              [intrinsic("poll_input")]
                              extern void poll_input();

                              void FrameStep() {
                                  poll_input();
                                  wait_frame();
                                  return;
                              }

                              void Main() {
                                  u8 value = 0;
                                  while (true) {
                                      value += 1;
                                      FrameStep();
                                  }
                                  return;
                              }
                              """;

        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(source);

        Assert.Collection(
            result.Report.PlacementUnits,
            unit =>
            {
                Assert.Equal(NesRomBuilder.MainInitPlacementUnitName, unit.Name);
                Assert.Equal(NesPrgResidence.Fixed, unit.Residence);
                Assert.Equal(NesPrgPlacementPhase.Cold, unit.Phase);
                Assert.True(unit.Size > 0);
            },
            unit =>
            {
                Assert.Equal(NesRomBuilder.MainFramePlacementUnitName, unit.Name);
                Assert.Equal(NesPrgResidence.Fixed, unit.Residence);
                Assert.Equal(NesPrgPlacementPhase.Hot, unit.Phase);
                Assert.True(unit.Size > 0);
            },
            unit =>
            {
                Assert.Equal(NesRomBuilder.MainTailPlacementUnitName, unit.Name);
                Assert.Equal(NesPrgResidence.Fixed, unit.Residence);
                Assert.Equal(NesPrgPlacementPhase.Cold, unit.Phase);
                Assert.True(unit.Size > 0);
            });
    }

    [Fact]
    public void Static_drawing_sample_reports_one_shot_without_hot_phase()
    {
        var sourcePath = RepositoryFile("samples/static-drawing/drawing.rs");
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            File.ReadAllText(sourcePath),
            Path.GetDirectoryName(sourcePath));
        var unit = Assert.Single(result.Report.PlacementUnits);

        Assert.Equal(NesRomBuilder.MainInitPlacementUnitName, unit.Name);
        Assert.Equal(NesPrgPlacementPhase.OneShot, unit.Phase);
        Assert.DoesNotContain(result.Report.PlacementUnits, unit => unit.Phase is NesPrgPlacementPhase.Hot);
    }

    [Fact]
    public void Phase_analyzer_reports_recursive_call_graphs_explicitly()
    {
        const string source = """
                              void Main() {
                                  A();
                                  return;
                              }

                              void A() {
                                  B();
                                  return;
                              }

                              void B() {
                                  A();
                                  return;
                              }
                              """;
        var program = NesSdkOperationBoundaryTests.ParseLoweredProgram(source);
        var main = program.Functions.Single(function => function.Name == "Main");
        var functions = program.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(
            () => NesProgramPhaseAnalyzer.Analyze(main.Block, functions, NesTarget.Intrinsics));

        Assert.Contains("NES phase analysis does not support recursive user function call cycle", exception.Message);
        Assert.Contains("A -> B -> A", exception.Message);
    }

    [Fact]
    public void Portable2D_import_does_not_affect_nes_rom_bytes()
    {
        const string implicitSdk = """
                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;
        const string explicitSdk = """
                                   import RetroSharp.Portable2D;

                                   void Main() {
                                       Video.WaitVBlank();
                                   }
                                   """;

        Assert.Equal(
            NesRomCompiler.CompileSource(implicitSdk),
            NesRomCompiler.CompileSource(explicitSdk));
    }

}
