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
    public void Signed_i8_relational_constants_compile_in_either_operand_order()
    {
        const string source = """
                              void Main() {
                                  i8 velocityY = -33;
                                  if (velocityY < -32) {
                                      velocityY += 1;
                                  }
                                  if (-32 > velocityY) {
                                      velocityY += 1;
                                  }
                              }
                              """;

        _ = NesRomCompiler.CompileSource(source);
    }

    [Fact]
    public void Word_compound_add_and_subtract_with_i8_operand_do_not_clobber_the_carry()
    {
        // Regression for the NES fall-through bug: the sign-extension of an i8 operand clobbers the
        // carry flag, so it must be computed into scratch before the low-byte ADC/SBC. The low-byte
        // arithmetic and its STA must therefore be immediately followed by the high-byte ADC/SBC,
        // with no sign-extension code in between.
        const string source = """
                              void Main() {
                                  i16 a = 10;
                                  i8 v = 5;
                                  a += v;
                                  a -= v;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        // a occupies $00/$01, v occupies $02, and the word scratch high byte is $E9.
        Assert.True(
            ContainsSequence(prg, [0x18, 0x65, 0x02, 0x85, 0x00, 0xA5, 0x01, 0x65, 0xE9, 0x85, 0x01]),
            "word += i8 should CLC/ADC the low byte then immediately ADC the high byte with the sign-extended scratch.");
        Assert.True(
            ContainsSequence(prg, [0x38, 0xE5, 0x02, 0x85, 0x00, 0xA5, 0x01, 0xE5, 0xE9, 0x85, 0x01]),
            "word -= i8 should SEC/SBC the low byte then immediately SBC the high byte with the sign-extended scratch.");
    }

    [Fact]
    public void Nes_selects_matching_target_intrinsic_variant_for_portable_helper()
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

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(source));
    }

    [Fact]
    public void Unknown_nes_intrinsic_reports_target_catalog_error()
    {
        const string source = """
                              [target("nes")]
                              [intrinsic("read_magic")]
                              extern void nes_read_magic();

                              void Main() {
                                  nes_read_magic();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal("Target 'nes' does not support intrinsic 'read_magic' on extern function 'nes_read_magic'.", exception.Message);
    }

}
