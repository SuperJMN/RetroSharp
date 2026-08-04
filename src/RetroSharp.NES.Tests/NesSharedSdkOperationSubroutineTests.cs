namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
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

}
