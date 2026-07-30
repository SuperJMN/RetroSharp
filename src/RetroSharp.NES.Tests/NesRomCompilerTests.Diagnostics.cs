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
    public void Rejects_nes_switch_expression_that_would_re_evaluate_subject()
    {
        const string source = """
                              u8 next(u8 value) => value + 1;

                              void Main() {
                                  Video.Init();
                                  u8 speed = next(1) switch { 0 => 0, _ => 1 };
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Equal("switch expression subject must be a simple value expression so lowering cannot re-evaluate a call or side effect.", exception.Message);
    }

    [Fact]
    public void Rejects_nes_invalid_pure_helper_contracts_before_lowering()
    {
        var statementEffect = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                         pure void draw() {
                                                                                                             Video.Init();
                                                                                                         }

                                                                                                         void Main() {
                                                                                                             draw();
                                                                                                             return;
                                                                                                         }
                                                                                                         """));
        Assert.Equal("pure helper 'draw' contains side-effecting statements; pure helpers must be a single return expression.", statementEffect.Message);

        var callEffect = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                    u8 next(u8 value) => value + 1;
                                                                                                    pure u8 step(u8 value) => next(value);

                                                                                                    void Main() {
                                                                                                        Video.Init();
                                                                                                        u8 result = step(4);
                                                                                                        return;
                                                                                                    }
                                                                                                    """));
        Assert.Equal("pure helper 'step' return expression contains side-effecting operations.", callEffect.Message);
    }

    [Fact]
    public void Rejects_nes_explicit_inline_value_helper_when_not_substitutable()
    {
        const string source = """
                              inline u8 step(u8 value) {
                                  u8 next = value + 1;
                                  return next;
                              }

                              void Main() {
                                  Video.Init();
                                  u8 result = step(4);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Equal("NES target cannot inline helper 'step' as a value because inline value helpers must be exactly one return expression.", exception.Message);
    }

    [Fact]
    public void Rejects_nes_assignment_to_immutable_let_binding()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  let speed = 2;
                                  speed = 3;
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Equal("Cannot assign to immutable local 'speed'.", exception.Message);
    }

    [Fact]
    public void Rejects_nes_compound_and_postfix_mutation_of_immutable_let_binding()
    {
        var compound = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                  void Main() {
                                                                                                      Video.Init();
                                                                                                      let speed = 2;
                                                                                                      speed += 1;
                                                                                                      return;
                                                                                                  }
                                                                                                  """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", compound.Message);

        var postfix = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource("""
                                                                                                 void Main() {
                                                                                                     Video.Init();
                                                                                                     let speed = 2;
                                                                                                     speed++;
                                                                                                     return;
                                                                                                 }
                                                                                                 """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", postfix.Message);
    }

    [Fact]
    public void Rejects_window_hud_mode_through_nes_capability_check()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Hud.SetTile(window, 0, 0, 1);
                                  return;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'nes' does not support Window HUD. Use disable HUD for this target.",
            exception.Message);
    }

}
