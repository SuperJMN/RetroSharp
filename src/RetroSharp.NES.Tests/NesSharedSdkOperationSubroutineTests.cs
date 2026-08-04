namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;
using static NesSdkOperationBoundaryTests;

public sealed class NesSharedSdkOperationSubroutineTests(ITestOutputHelper output)
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
            pair => pair.Key.StartsWith("nes_sdk_draw_logical_sprite_shared_", StringComparison.Ordinal));
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
            pair => pair.Key.StartsWith("nes_sdk_draw_logical_sprite_shared_", StringComparison.Ordinal));
        Assert.Equal(NesPrgResidence.Fixed, helper.Value.Residence);
        var calls = shared.Relocations
            .Where(relocation => relocation.Kind is NesPrgRelocationKind.AbsoluteCall && relocation.Label == helper.Key)
            .ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call => Assert.Equal(NesPrgResidence.ProgramR6, call.Residence));
        Assert.DoesNotContain(
            unrolled.Labels,
            pair => pair.Key.StartsWith("nes_sdk_draw_logical_sprite_shared_", StringComparison.Ordinal));
        Assert.True(
            TotalBytes(shared) < TotalBytes(unrolled),
            $"shared={TotalBytes(shared)}, unrolled={TotalBytes(unrolled)}");
    }

    [Fact]
    public void Audio_mixed_load_sharing_reduces_fixed_prg_and_holds_one_tick_per_frame()
    {
        var (source, directory) = AudioMixedLoadSource();
        var shared = CompileCanary(source, directory, shareRepeatedSdkOperations: true);
        var unrolled = CompileCanary(source, directory, shareRepeatedSdkOperations: false);

        Assert.Equal(unrolled.Report.SelectedProfile, shared.Report.SelectedProfile);
        Assert.True(
            shared.Report.FixedPayloadBytes < unrolled.Report.FixedPayloadBytes,
            $"shared={shared.Report.FixedPayloadBytes}, unrolled={unrolled.Report.FixedPayloadBytes}");

        var sharedObservation = ObserveSteadyState(shared.Rom);
        var unrolledObservation = ObserveSteadyState(unrolled.Rom);
        output.WriteLine($"shared: {shared.Report.FixedPayloadBytes} bytes, {sharedObservation}");
        output.WriteLine($"unrolled: {unrolled.Report.FixedPayloadBytes} bytes, {unrolledObservation}");

        AssertLive(sharedObservation);
        AssertLive(unrolledObservation);
        Assert.InRange(sharedObservation.WorstActiveTickCycles, 1, 29_780);
        Assert.InRange(unrolledObservation.WorstActiveTickCycles, 1, 29_780);
    }

    [Fact]
    public void Representative_repeated_sprite_increment_no_longer_selects_code_banking()
    {
        var (source, directory) = AudioMixedLoadSource();
        var capacityLoad = string.Join(
            Environment.NewLine,
            Enumerable.Repeat("        gameplayTick += 1;", 600));
        var incrementedSource = source.Replace(
            "        Audio.Update();",
            capacityLoad + $"{Environment.NewLine}        Audio.Update();",
            StringComparison.Ordinal);
        Assert.NotEqual(source, incrementedSource);

        var shared = CompileCanary(incrementedSource, directory, shareRepeatedSdkOperations: true);
        var unrolled = CompileCanary(incrementedSource, directory, shareRepeatedSdkOperations: false);

        output.WriteLine($"increment shared: profile={shared.Report.SelectedProfile}, fixed={shared.Report.FixedPayloadBytes}");
        output.WriteLine($"increment unrolled: profile={unrolled.Report.SelectedProfile}, fixed={unrolled.Report.FixedPayloadBytes}");
        Assert.Equal("nes-mmc3-tvrom-v1", shared.Report.SelectedProfile);
        Assert.Equal(NesRomBuilder.CodeBankedProfileName, unrolled.Report.SelectedProfile);
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
        using (builder.EnterSection(NesPrgResidence.ProgramR6))
        {
            compiler.Emit(program.MainBlock);
        }

        compiler.EmitReferencedSubroutines();
        return builder.FreezeForLink();
    }

    private static int TotalBytes(NesPrgEmission emission) => emission.Sections.Values.Sum(section => section.Bytes.Length);

    private static NesRomBuildResult CompileCanary(
        string source,
        string directory,
        bool shareRepeatedSdkOperations) =>
        RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            shareRepeatedSdkOperations: shareRepeatedSdkOperations);

    private static SteadyStateObservation ObserveSteadyState(byte[] rom)
    {
        var cpu = new NesTestCpu(rom);
        cpu.Held.Add("right");
        cpu.TracedRamBytes.Add(NesRuntimeMemoryLayout.Camera.ScrollApplied);
        cpu.RunFrames(40);
        var startFrame = cpu.PhysicalFrames;
        var startWait = cpu.VBlankWaitCompletions;
        var startNmi = cpu.NmiCompletionCycles.Count;
        var startWrite = cpu.RamByteWrites.Count;
        cpu.RunFrames(startFrame + 120);
        var nmiCycles = cpu.NmiCompletionCycles.Skip(startNmi).ToArray();
        var endOfTickWrites = cpu.RamByteWrites
            .Skip(startWrite)
            .Where(write =>
                write.Address == NesRuntimeMemoryLayout.Camera.ScrollApplied &&
                write.Value == (byte)NesCameraPublicationState.Ready)
            .ToArray();
        var activeTickCycles = nmiCycles
            .Zip(nmiCycles.Skip(1), (start, end) => (start, end))
            .Select(window => endOfTickWrites
                .Where(write => write.Cycle >= window.start && write.Cycle < window.end)
                .Select(write => write.Cycle - window.start)
                .DefaultIfEmpty()
                .Max())
            .Where(cycles => cycles > 0)
            .ToArray();
        Assert.NotEmpty(activeTickCycles);
        return new SteadyStateObservation(
            cpu.PhysicalFrames - startFrame,
            cpu.VBlankWaitCompletions - startWait,
            activeTickCycles.Max(),
            cpu.ResetCount,
            cpu.ApuWrites.Count);
    }

    private static void AssertLive(SteadyStateObservation observation)
    {
        Assert.Equal(observation.PhysicalFrames, observation.LogicalTicks);
        Assert.Equal(1, observation.ResetCount);
    }

    private static (string Source, string Directory) AudioMixedLoadSource()
    {
        var directory = RepositoryDirectory("samples/audio-mixed-load");
        return (File.ReadAllText(Path.Combine(directory, "src", "main.rs")), directory);
    }

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

        throw new InvalidOperationException($"Could not find repository directory '{relativePath}'.");
    }

    private sealed record SteadyStateObservation(
        int PhysicalFrames,
        long LogicalTicks,
        long WorstActiveTickCycles,
        int ResetCount,
        int ApuWrites)
    {
        public override string ToString() =>
            $"frames={PhysicalFrames}, ticks={LogicalTicks}, peak={WorstActiveTickCycles}, resets={ResetCount}, "
            + $"apu={ApuWrites}";
    }
}
