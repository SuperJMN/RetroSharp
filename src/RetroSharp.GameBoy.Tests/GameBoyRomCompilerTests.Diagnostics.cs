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
    public void Rejects_game_boy_switch_expression_that_would_re_evaluate_subject()
    {
        const string source = """
                              u8 next(u8 value) => value + 1;

                              void Main() {
                                  Video.Init();
                                  u8 speed = next(1) switch { 0 => 0, _ => 1 };
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Equal("switch expression subject must be a simple value expression so lowering cannot re-evaluate a call or side effect.", exception.Message);
    }

    [Fact]
    public void Rejects_game_boy_invalid_pure_helper_contracts_before_lowering()
    {
        var statementEffect = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                             pure void draw() {
                                                                                                                 Video.Init();
                                                                                                             }

                                                                                                             void Main() {
                                                                                                                 draw();
                                                                                                             }
                                                                                                             """));
        Assert.Equal("pure helper 'draw' contains side-effecting statements; pure helpers must be a single return expression.", statementEffect.Message);

        var callEffect = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                        u8 next(u8 value) => value + 1;
                                                                                                        pure u8 step(u8 value) => next(value);

                                                                                                        void Main() {
                                                                                                            Video.Init();
                                                                                                            u8 result = step(4);
                                                                                                        }
                                                                                                        """));
        Assert.Equal("pure helper 'step' return expression contains side-effecting operations.", callEffect.Message);
    }

    [Fact]
    public void Rejects_game_boy_explicit_inline_value_helper_when_not_substitutable()
    {
        const string source = """
                              inline u8 step(u8 value) {
                                  u8 next = value + 1;
                                  return next;
                              }

                              void Main() {
                                  Video.Init();
                                  u8 result = step(4);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Equal("Game Boy target cannot inline helper 'step' as a value because inline value helpers must be exactly one return expression.", exception.Message);
    }

    [Fact]
    public void Rejects_game_boy_assignment_to_immutable_let_binding()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  let speed = 2;
                                  speed = 3;
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Equal("Cannot assign to immutable local 'speed'.", exception.Message);
    }

    [Fact]
    public void Rejects_game_boy_compound_and_postfix_mutation_of_immutable_let_binding()
    {
        var compound = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                      void Main() {
                                                                                                          Video.Init();
                                                                                                          let speed = 2;
                                                                                                          speed += 1;
                                                                                                      }
                                                                                                      """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", compound.Message);

        var postfix = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource("""
                                                                                                     void Main() {
                                                                                                         Video.Init();
                                                                                                         let speed = 2;
                                                                                                         speed++;
                                                                                                     }
                                                                                                     """));
        Assert.Equal("Cannot assign to immutable local 'speed'.", postfix.Message);
    }

    [Fact]
    public void Static_class_rejects_instance_members()
    {
        const string source = """
                              static class Level { const i16 Width = 16; i16 broken; }

                              void Main() {
                                  Video.Init();
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("Static class 'Level'", exception.Message);
    }

}
