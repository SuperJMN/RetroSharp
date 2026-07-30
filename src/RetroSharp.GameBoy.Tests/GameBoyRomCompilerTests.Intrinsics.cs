namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Word_compound_add_and_subtract_with_i8_operand_preserve_the_carry()
    {
        // Regression for the fall-through bug: adding/subtracting an i8 operand to a 16-bit local
        // must sign-extend the operand without clobbering the carry between the low and high byte
        // arithmetic. Covers a positive addend with no low-byte overflow (a += da), a positive
        // addend that overflows the low byte (b += db), a negative addend (c += dc), and a
        // subtraction that borrows into the high byte (e -= de).
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 a = 10;   i8 da = 5;    a += da;
                                  i16 b = 200;  i8 db = 100;  b += db;
                                  i16 c = 300;  i8 dc = -50;  c += dc;
                                  i16 e = 40;   i8 de = 100;  e -= de;
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        Assert.Equal(15, cpu.Wram(0xC000) | cpu.Wram(0xC001) << 8);
        Assert.Equal(300, cpu.Wram(0xC003) | cpu.Wram(0xC004) << 8);
        Assert.Equal(250, cpu.Wram(0xC006) | cpu.Wram(0xC007) << 8);
        Assert.Equal(0xFFC4, cpu.Wram(0xC009) | cpu.Wram(0xC00A) << 8);
    }

    [Fact]
    public void Word_variable_vs_variable_relational_comparisons_do_not_clobber_the_left_operand()
    {
        // Regression: 16-bit relational compares loaded the left operand into A and then materialized
        // the right operand into A as well, degrading the comparison to right-vs-right (always equal).
        // The runner's Camera.FollowPlayer used `y < maxScrollY` (both i16), so the camera could scroll
        // up but never back down. Force runtime i16 values (so they are not folded to constants) and
        // check every relational operator resolves against the real operands.
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 a = 10;   i8 da = 5;   a += da;   // a = 15
                                  i16 b = 40;   i8 db = 5;   b += db;   // b = 45
                                  u8 lt = 0;    if (a < b)  { lt = 1; }
                                  u8 le = 0;    if (a <= b) { le = 1; }
                                  u8 gt = 0;    if (a > b)  { gt = 1; }
                                  u8 ge = 0;    if (a >= b) { ge = 1; }
                                  u8 gtR = 0;   if (b > a)  { gtR = 1; }
                                  u8 ltR = 0;   if (b < a)  { ltR = 1; }
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        // a(C000/C001) da(C002) b(C003/C004) db(C005) lt(C006) le(C007) gt(C008) ge(C009) gtR(C00A) ltR(C00B)
        Assert.Equal(15, cpu.Wram(0xC000) | cpu.Wram(0xC001) << 8);
        Assert.Equal(45, cpu.Wram(0xC003) | cpu.Wram(0xC004) << 8);
        Assert.Equal(1, cpu.Wram(0xC006)); // 15 < 45
        Assert.Equal(1, cpu.Wram(0xC007)); // 15 <= 45
        Assert.Equal(0, cpu.Wram(0xC008)); // 15 > 45
        Assert.Equal(0, cpu.Wram(0xC009)); // 15 >= 45
        Assert.Equal(1, cpu.Wram(0xC00A)); // 45 > 15
        Assert.Equal(0, cpu.Wram(0xC00B)); // 45 < 15
    }

    [Fact]
    public void Compiles_wait_frame_library_helper_over_game_boy_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void Main() {
                                     Video.WaitVBlank();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("gb")]
                                       [intrinsic("wait_frame")]
                                       extern void gb_wait_frame();

                                       inline void wait_frame() {
                                           gb_wait_frame();
                                       }

                                       void Main() {
                                           wait_frame();
                                       }
                                       """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Compiles_input_poll_library_helper_over_game_boy_intrinsic_like_sdk_operation()
    {
        const string sdkSource = """
                                 void Main() {
                                     Input.Poll();
                                 }
                                 """;

        const string intrinsicSource = """
                                       [target("gb")]
                                       [intrinsic("poll_input")]
                                       extern void gb_poll_input();

                                       inline void poll_input() {
                                           gb_poll_input();
                                       }

                                       void Main() {
                                           poll_input();
                                       }
                                       """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Game_boy_selects_matching_target_intrinsic_variant_for_portable_helper()
    {
        const string sdkSource = """
                                 void Main() {
                                     Video.WaitVBlank();
                                 }
                                 """;

        const string source = """
                              [target("gb")]
                              [intrinsic("wait_frame")]
                              extern void target_wait_frame();

                              [target("nes")]
                              [intrinsic("wait_frame")]
                              extern void target_wait_frame();

                              inline void wait_frame() {
                                  target_wait_frame();
                              }

                              void Main() {
                                  wait_frame();
                              }
                              """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(sdkSource), GameBoyRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Unknown_game_boy_intrinsic_reports_target_catalog_error()
    {
        const string source = """
                              [target("gb")]
                              [intrinsic("read_magic")]
                              extern void gb_read_magic();

                              void Main() {
                                  gb_read_magic();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Target 'gb' does not support intrinsic 'read_magic' on extern function 'gb_read_magic'.", exception.Message);
    }

    [Fact]
    public void Intrinsic_extern_return_type_must_match_descriptor()
    {
        const string source = """
                              [target("gb")]
                              [intrinsic("wait_frame")]
                              extern i16 wrong_wait_frame();

                              void Main() {
                                  i16 value = wrong_wait_frame();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal("Extern intrinsic 'wrong_wait_frame' declares return type 'i16', but intrinsic 'wait_frame' returns 'void'.", exception.Message);
    }

    [Fact]
    public void Compile_time_operand_slot_rejects_runtime_value()
    {
        const string source = """
                              [target("gb")]
                              [intrinsic("world_tile_flags_for_world")]
                              extern i16 flags_for_world(i16 world, i16 x, i16 y);

                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  i16 selectedWorld = 0;
                                  i16 flags = flags_for_world(selectedWorld, 0, 8);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));

        Assert.Equal(
            "Intrinsic 'world_tile_flags_for_world' argument 1 on extern 'flags_for_world' is compile-time WorldId and cannot use runtime local 'selectedWorld'.",
            exception.Message);
    }

    [Fact]
    public void Minimal_compile_time_operand_intrinsic_is_byte_identical()
    {
        const string direct = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Flags(0, 0, 1);
                                  World.Map(1, 10, 2);
                                  i16 flags = World.TileFlagsAt(0, 8);
                              }
                              """;

        const string intrinsic = """
                                 [target("gb")]
                                 [intrinsic("world_tile_flags_for_world")]
                                 extern i16 flags_for_world(i16 world, i16 x, i16 y);

                                 void Main() {
                                     World.Column(0, 1, 2);
                                     World.Flags(0, 0, 1);
                                     World.Map(1, 10, 2);
                                     i16 flags = flags_for_world("default", 0, 8);
                                 }
                                 """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(direct), GameBoyRomCompiler.CompileSource(intrinsic));
    }

    [Fact]
    public void Single_descriptor_covers_multiple_assets_without_duplication()
    {
        var descriptor = TargetIntrinsicDescriptor.DrawLogicalSprite(
            "sprite_draw",
            runtimeArity: 4,
            compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]);
        var catalog = new TargetIntrinsicCatalog("gb", "Game Boy", [descriptor]);
        var function = ExternIntrinsic("gb", "sprite_draw", "__sprite_draw");

        var first = TargetIntrinsicResolver.ResolveCall(
            function,
            new FunctionCall("__sprite_draw", [new IdentifierSyntax("player"), new ConstantSyntax("24"), new ConstantSyntax("32"), new ConstantSyntax("0"), new ConstantSyntax("0")]),
            catalog);
        var second = TargetIntrinsicResolver.ResolveCall(
            function,
            new FunctionCall("__sprite_draw", [new IdentifierSyntax("enemy"), new ConstantSyntax("24"), new ConstantSyntax("32"), new ConstantSyntax("0"), new ConstantSyntax("0")]),
            catalog);

        Assert.Same(descriptor, first.Descriptor);
        Assert.Same(descriptor, second.Descriptor);
        Assert.Equal("player", Assert.Single(first.CompileTimeOperands).Identifier);
        Assert.Equal("enemy", Assert.Single(second.CompileTimeOperands).Identifier);
        Assert.Equal(1, catalog.Intrinsics.Count);
    }

}
