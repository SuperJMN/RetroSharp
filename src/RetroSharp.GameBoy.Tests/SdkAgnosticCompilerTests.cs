namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public sealed class SdkAgnosticCompilerTests
{
    [Fact]
    public void Source_package_facade_lowerer_discovers_declared_static_methods_without_a_registry_list()
    {
        var program = new ProgramSyntax(
            [new StructSyntax("Acme", [])],
            [
                new FunctionSyntax(
                    "void",
                    "Acme_Tick",
                    [],
                    new BlockSyntax([]),
                    isInline: true),
                new FunctionSyntax(
                    "void",
                    "Main",
                    [],
                    new BlockSyntax([
                        new ExpressionStatementSyntax(new SdkDotCallSyntax("Acme", "Tick", [])),
                    ])),
            ]);

        var lowered = SdkSourcePackageFacadeLowerer.Lower(program);

        var main = Assert.Single(lowered.Functions.Where(function => function.Name == "Main"));
        var statement = Assert.IsType<ExpressionStatementSyntax>(Assert.Single(main.Block.Statements));
        var call = Assert.IsType<FunctionCall>(statement.Expression);
        Assert.Equal("Acme_Tick", call.Name);
    }

    [Fact]
    public void Portable2d_input_predicates_are_source_wrappers_over_target_intrinsics()
    {
        var source = SdkLibrarySource.ForTarget(GameBoyTarget.Intrinsics);

        Assert.Contains("[intrinsic(\"button_down\")]", source, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"button_just_pressed\")]", source, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"button_just_released\")]", source, StringComparison.Ordinal);
        Assert.Contains("[intrinsic(\"button_hold_ticks\")]", source, StringComparison.Ordinal);
        Assert.Contains("static inline bool IsDown(RetroSharp_Portable2D_Button b)", source, StringComparison.Ordinal);
        Assert.Contains("static inline bool WasPressed(RetroSharp_Portable2D_Button b)", source, StringComparison.Ordinal);
        Assert.Contains("static inline bool WasReleased(RetroSharp_Portable2D_Button b)", source, StringComparison.Ordinal);
        Assert.Contains("static inline i16 HoldTicks(RetroSharp_Portable2D_Button b)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Sdk_source_merge_does_not_autoimport_portable2d_without_a_declared_library()
    {
        var merged = SdkLibrarySource.Merge(
            GameBoyTarget.Intrinsics,
            """
            void Main() {
            }
            """);

        Assert.DoesNotContain("RetroSharp_Portable2D", merged, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("button_down", TargetIntrinsicReturnKind.Bool)]
    [InlineData("button_just_pressed", TargetIntrinsicReturnKind.Bool)]
    [InlineData("button_just_released", TargetIntrinsicReturnKind.Bool)]
    [InlineData("button_hold_ticks", TargetIntrinsicReturnKind.I16)]
    public void Game_boy_catalogs_button_predicate_intrinsics(string intrinsicId, TargetIntrinsicReturnKind returnKind)
    {
        Assert.True(GameBoyTarget.Intrinsics.TryResolve(intrinsicId, out var descriptor));
        Assert.Equal(returnKind, descriptor.ReturnKind);
        Assert.Equal(1, descriptor.RuntimeArity);
        Assert.Empty(descriptor.CompileTimeOperands);
    }
}
