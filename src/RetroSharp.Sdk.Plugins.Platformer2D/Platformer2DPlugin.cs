namespace RetroSharp.Sdk.Plugins.Platformer2D;

using RetroSharp.Core.Sdk;

/// <summary>
/// Experimental, static in-process SDK plugin that proves the SDK plugin
/// extensibility boundary end to end: a source-package facade plus namespaced
/// descriptors (operation, capability, validator, and a per-target lowering
/// hook) that are registered through <see cref="SdkPluginRegistry"/> instead of
/// growing the compiler's central operation enums.
/// </summary>
/// <remarks>
/// The lowering hook emits a single, harmless Game Boy <c>nop</c>. It is a
/// mechanism proof, not real platformer collision code; genuine platformer
/// semantics would grow through this same descriptor path.
/// </remarks>
public static class Platformer2DPlugin
{
    public const string PluginId = "RetroSharp.Platformer2D";

    public const string GroundProbeOperationId = "RetroSharp.Platformer2D.GroundProbe";

    public const string CollisionProbeCapabilityId = "RetroSharp.Platformer2D.CollisionProbe";

    private const byte GameBoyNop = 0x00;

    public static SdkPluginDescriptor Create()
    {
        return new SdkPluginDescriptor(
            Id: PluginId,
            Version: "0.1.0",
            RequiredCompilerAbi: SdkPluginDescriptor.StaticCompilerAbi,
            SourcePackage: new SdkPluginSourcePackageDescriptor(
                ImportPath: PluginId,
                SourceFactory: _ =>
                    """
                    [intrinsic("RetroSharp.Platformer2D.GroundProbe")]
                    extern void platformer2d_ground_probe();

                    class Platformer
                    {
                        static inline void GroundProbe()
                        {
                            platformer2d_ground_probe();
                        }
                    }
                    """),
            ResourceDeclarations: [],
            Operations:
            [
                new SdkPluginOperationDescriptor(
                    OperationId: GroundProbeOperationId,
                    ReturnKind: TargetIntrinsicReturnKind.Void,
                    RuntimeArity: 0,
                    CallKind: SdkPluginOperationCallKind.Statement,
                    CompileTimeOperands: [],
                    RequiredCapabilities: [CollisionProbeCapabilityId]),
            ],
            Capabilities:
            [
                new SdkPluginCapabilityDescriptor(CollisionProbeCapabilityId),
            ],
            Validators:
            [
                new SdkPluginValidatorDescriptor(GroundProbeOperationId, ValidateGroundProbe),
            ],
            TargetLoweringHooks:
            [
                new SdkPluginTargetLoweringDescriptor(
                    TargetId: "gb",
                    OperationId: GroundProbeOperationId,
                    Lower: LowerGroundProbeToGameBoy,
                    ProvidedCapabilities: [CollisionProbeCapabilityId]),
            ],
            Compatibility: SdkPluginCompatibilityDescriptor.Unspecified);
    }

    private static void ValidateGroundProbe(SdkPluginValidationContext context)
    {
        if (context.RuntimeOperands.Count != 0 || context.CompileTimeOperands.Count != 0)
        {
            throw new InvalidOperationException(
                $"SDK plugin feature '{GroundProbeOperationId}' does not take operands.");
        }
    }

    private static void LowerGroundProbeToGameBoy(SdkPluginTargetLoweringContext context)
    {
        context.Emitter.Emit(GameBoyNop);
    }
}
