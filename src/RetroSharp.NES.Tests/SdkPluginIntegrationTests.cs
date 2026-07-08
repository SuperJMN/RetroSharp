namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class SdkPluginIntegrationTests
{
    [Fact]
    public void Game_boy_compiles_platformer2d_plugin_operation_through_target_hook()
    {
        var lowered = new List<SdkPluginTargetLoweringContext>();
        var registry = SdkPluginRegistry.Empty.Register(PlatformerPlugin(context =>
        {
            context.Emitter.Emit(0x3E, 0x42);
            lowered.Add(context);
        }));

        var rom = GameBoyRomCompiler.CompileSource(Source, sdkPluginRegistry: registry);

        Assert.Equal(32 * 1024, rom.Length);
        Assert.True(
            rom.AsSpan().IndexOf(new byte[] { 0x3E, 0x42 }) >= 0,
            "Plugin hook did not emit its Game Boy instruction bytes.");
        var context = Assert.Single(lowered);
        Assert.Equal("gb", context.TargetId);
        Assert.Equal("RetroSharp.Platformer2D.TouchProbe", context.Operation.OperationId);
        Assert.Empty(context.RuntimeOperands);
        Assert.Empty(context.CompileTimeOperands);
    }

    [Fact]
    public void Target_without_plugin_opt_in_fails_before_lowering()
    {
        var lowered = new List<SdkPluginTargetLoweringContext>();
        var registry = SdkPluginRegistry.Empty.Register(PlatformerPlugin(lowered.Add));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NesRomCompiler.CompileSource(Source, sdkPluginRegistry: registry));

        Assert.Equal(
            "Target 'nes' does not support SDK plugin feature 'RetroSharp.Platformer2D.TouchProbe' on extern function 'platformer2d_touch_probe'.",
            exception.Message);
        Assert.Empty(lowered);
    }

    [Fact]
    public void Plugin_resource_without_target_importer_fails_with_clear_diagnostic()
    {
        var registry = SdkPluginRegistry.Empty.Register(PlatformerPlugin(_ => { }));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameBoyRomCompiler.CompileSource(ResourceSource, sdkPluginRegistry: registry));

        Assert.Equal(
            "Target 'gb' does not support SDK plugin resource 'RetroSharp.Platformer2D.CollisionProfile'.",
            exception.Message);
    }

    private const string Source =
        """
        import RetroSharp.Platformer2D;

        void Main()
        {
            Platformer.TouchProbe();
        }
        """;

    private const string ResourceSource =
        """
        import RetroSharp.Platformer2D;

        void Main()
        {
            Platformer.CollisionProfile(hero, 16);
        }
        """;

    [Fact]
    public void Game_boy_rejects_plugin_operation_when_target_hook_omits_required_capability()
    {
        var lowered = new List<SdkPluginTargetLoweringContext>();
        var registry = SdkPluginRegistry.Empty.Register(PlatformerPlugin(lowered.Add, provideCapability: false));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameBoyRomCompiler.CompileSource(Source, sdkPluginRegistry: registry));

        Assert.Equal(
            "Target 'gb' does not provide SDK plugin capability 'RetroSharp.Platformer2D.CollisionProbe' required by feature 'RetroSharp.Platformer2D.TouchProbe' on extern function 'platformer2d_touch_probe'.",
            exception.Message);
        Assert.Empty(lowered);
    }

    [Fact]
    public void Plugin_validator_rejects_invalid_compile_time_operand_before_lowering()
    {
        var lowered = new List<SdkPluginTargetLoweringContext>();
        var registry = SdkPluginRegistry.Empty.Register(ClampPlugin(lowered.Add));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameBoyRomCompiler.CompileSource(ClampSource(9), sdkPluginRegistry: registry));

        Assert.Equal(
            "SDK plugin feature 'RetroSharp.Platformer2D.ClampProbe' rejects collision slot 9; valid range is 0..3.",
            exception.Message);
        Assert.Empty(lowered);
    }

    [Fact]
    public void Plugin_validator_accepts_valid_compile_time_operand()
    {
        var lowered = new List<SdkPluginTargetLoweringContext>();
        var registry = SdkPluginRegistry.Empty.Register(ClampPlugin(lowered.Add));

        var rom = GameBoyRomCompiler.CompileSource(ClampSource(2), sdkPluginRegistry: registry);

        Assert.Equal(32 * 1024, rom.Length);
        var context = Assert.Single(lowered);
        var operand = Assert.Single(context.CompileTimeOperands);
        Assert.Equal(2, operand.Constant);
    }

    private static string ClampSource(int slot) =>
        $$"""
        import RetroSharp.Platformer2D;

        void Main()
        {
            Platformer.ClampProbe({{slot}});
        }
        """;

    private static SdkPluginDescriptor ClampPlugin(Action<SdkPluginTargetLoweringContext> lower)
    {
        return new SdkPluginDescriptor(
            Id: "RetroSharp.Platformer2D",
            Version: "0.1.0",
            RequiredCompilerAbi: "sdk-plugin-static-v1",
            SourcePackage: new SdkPluginSourcePackageDescriptor(
                ImportPath: "RetroSharp.Platformer2D",
                SourceFactory: _ =>
                    """
                    [intrinsic("RetroSharp.Platformer2D.ClampProbe")]
                    extern void platformer2d_clamp_probe(i16 slot);

                    class Platformer
                    {
                        static inline void ClampProbe(i16 slot)
                        {
                            platformer2d_clamp_probe(slot);
                        }
                    }
                    """),
            ResourceDeclarations: [],
            Operations:
            [
                new SdkPluginOperationDescriptor(
                    OperationId: "RetroSharp.Platformer2D.ClampProbe",
                    ReturnKind: TargetIntrinsicReturnKind.Void,
                    RuntimeArity: 0,
                    CallKind: SdkPluginOperationCallKind.Statement,
                    CompileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.ConstPaletteSlot)],
                    RequiredCapabilities: ["RetroSharp.Platformer2D.CollisionProbe"]),
            ],
            Capabilities:
            [
                new SdkPluginCapabilityDescriptor("RetroSharp.Platformer2D.CollisionProbe"),
            ],
            Validators:
            [
                new SdkPluginValidatorDescriptor("RetroSharp.Platformer2D.ClampProbe", context =>
                {
                    var slot = context.CompileTimeOperands[0].Constant;
                    if (slot is < 0 or > 3)
                    {
                        throw new InvalidOperationException(
                            $"SDK plugin feature 'RetroSharp.Platformer2D.ClampProbe' rejects collision slot {slot}; valid range is 0..3.");
                    }
                }),
            ],
            TargetLoweringHooks:
            [
                new SdkPluginTargetLoweringDescriptor(
                    "gb",
                    "RetroSharp.Platformer2D.ClampProbe",
                    lower,
                    ProvidedCapabilities: ["RetroSharp.Platformer2D.CollisionProbe"]),
            ],
            Compatibility: SdkPluginCompatibilityDescriptor.Unspecified);
    }

    private static SdkPluginDescriptor PlatformerPlugin(Action<SdkPluginTargetLoweringContext> lower, bool provideCapability = true)
    {
        return new SdkPluginDescriptor(
            Id: "RetroSharp.Platformer2D",
            Version: "0.1.0",
            RequiredCompilerAbi: "sdk-plugin-static-v1",
            SourcePackage: new SdkPluginSourcePackageDescriptor(
                ImportPath: "RetroSharp.Platformer2D",
                SourceFactory: _ =>
                    """
                    [intrinsic("RetroSharp.Platformer2D.TouchProbe")]
                    extern void platformer2d_touch_probe();

                    class Platformer
                    {
                        static inline [resource("RetroSharp.Platformer2D.CollisionProfile")] void CollisionProfile(i16 name, i16 width)
                        {
                        }

                        static inline void TouchProbe()
                        {
                            platformer2d_touch_probe();
                        }
                    }
                    """),
            ResourceDeclarations:
            [
                new SdkPluginResourceDeclarationDescriptor("RetroSharp.Platformer2D.CollisionProfile"),
            ],
            Operations:
            [
                new SdkPluginOperationDescriptor(
                    OperationId: "RetroSharp.Platformer2D.TouchProbe",
                    ReturnKind: TargetIntrinsicReturnKind.Void,
                    RuntimeArity: 0,
                    CallKind: SdkPluginOperationCallKind.Statement,
                    CompileTimeOperands: [],
                    RequiredCapabilities: ["RetroSharp.Platformer2D.CollisionProbe"]),
            ],
            Capabilities:
            [
                new SdkPluginCapabilityDescriptor("RetroSharp.Platformer2D.CollisionProbe"),
            ],
            Validators:
            [
                new SdkPluginValidatorDescriptor("RetroSharp.Platformer2D.TouchProbe", _ => { }),
            ],
            TargetLoweringHooks:
            [
                new SdkPluginTargetLoweringDescriptor(
                    "gb",
                    "RetroSharp.Platformer2D.TouchProbe",
                    lower,
                    ProvidedCapabilities: provideCapability ? ["RetroSharp.Platformer2D.CollisionProbe"] : []),
            ],
            Compatibility: SdkPluginCompatibilityDescriptor.Unspecified);
    }
}
