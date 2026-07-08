namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class SdkPluginDescriptorTests
{
    [Fact]
    public void Registry_registers_static_plugin_descriptors_in_process()
    {
        var plugin = PlatformerPlugin();

        var registry = SdkPluginRegistry.Empty.Register(plugin);

        Assert.True(registry.TryResolve("RetroSharp.Platformer2D", out var resolved));
        Assert.Same(plugin, resolved);
        Assert.Equal("0.1.0", resolved.Version);
        Assert.Equal("sdk-plugin-static-v1", resolved.RequiredCompilerAbi);
        Assert.Equal("RetroSharp.Platformer2D", resolved.SourcePackage.ImportPath);
        Assert.Contains(resolved.ResourceDeclarations, descriptor => descriptor.ResourceId == "RetroSharp.Platformer2D.CollisionProfile");
        Assert.Contains(resolved.Operations, descriptor => descriptor.OperationId == "RetroSharp.Platformer2D.TouchProbe");
        Assert.Contains(resolved.Capabilities, descriptor => descriptor.CapabilityId == "RetroSharp.Platformer2D.CollisionProbe");
        Assert.Empty(resolved.AssetImporters);
        Assert.Contains(resolved.Validators, descriptor => descriptor.FeatureId == "RetroSharp.Platformer2D.TouchProbe");
        Assert.Contains(resolved.TargetLoweringHooks, descriptor => descriptor.TargetId == "gb");
    }

    [Fact]
    public void Plugin_descriptor_ids_must_be_namespaced_by_plugin_id()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new SdkPluginDescriptor(
                Id: "RetroSharp.Platformer2D",
                Version: "0.1.0",
                RequiredCompilerAbi: "sdk-plugin-static-v1",
                SourcePackage: SourcePackage(),
                ResourceDeclarations:
                [
                    new SdkPluginResourceDeclarationDescriptor("collision_profile"),
                ],
                Operations:
                [
                    new SdkPluginOperationDescriptor(
                        OperationId: "TouchProbe",
                        ReturnKind: TargetIntrinsicReturnKind.Void,
                        RuntimeArity: 0,
                        CallKind: SdkPluginOperationCallKind.Statement,
                        CompileTimeOperands: [],
                        RequiredCapabilities: []),
                ],
                Capabilities:
                [
                    new SdkPluginCapabilityDescriptor("CollisionProbe"),
                ],
                Validators: [],
                TargetLoweringHooks: [],
                Compatibility: SdkPluginCompatibilityDescriptor.Unspecified));

        Assert.Equal("SDK plugin descriptor id 'collision_profile' must be namespaced under plugin id 'RetroSharp.Platformer2D'.", exception.Message);
    }

    [Fact]
    public void Resource_declaration_registry_keeps_built_ins_and_accepts_plugin_resources()
    {
        var registry = SdkResourceDeclarationRegistry.Default.Register(PlatformerPlugin());

        Assert.True(registry.TryResolve("world_load", out var builtIn));
        Assert.Equal(SdkResourceDeclarationKind.WorldLoad, builtIn.Kind);

        Assert.True(registry.TryResolve("RetroSharp.Platformer2D.CollisionProfile", out var pluginResource));
        Assert.True(pluginResource.IsPluginResource);
        Assert.Equal("RetroSharp.Platformer2D.CollisionProfile", pluginResource.PluginResource.ResourceId);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve("RetroSharp.Platformer2D.Missing"));
        Assert.Equal("Unknown SDK resource declaration 'RetroSharp.Platformer2D.Missing'.", exception.Message);
    }

    [Fact]
    public void Target_intrinsic_catalog_can_expose_plugin_operations_without_core_enum_cases()
    {
        var registry = SdkPluginRegistry.Empty.Register(PlatformerPlugin());
        var catalog = new TargetIntrinsicCatalog("gb", "Game Boy", [])
            .WithSdkPlugins(registry);

        var descriptor = catalog.Resolve("RetroSharp.Platformer2D.TouchProbe", "platformer_touch_probe");

        Assert.True(descriptor.IsPluginOperation);
        Assert.Equal("RetroSharp.Platformer2D.TouchProbe", descriptor.PluginOperation.OperationId);
        Assert.Null(descriptor.BuiltInOperation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, descriptor.ReturnKind);
        Assert.Equal(0, descriptor.RuntimeArity);
        Assert.Contains("RetroSharp.Platformer2D.CollisionProbe", descriptor.RequiredPluginCapabilities);
    }

    [Fact]
    public void Target_intrinsic_catalog_reports_plugin_operations_missing_target_opt_in()
    {
        var registry = SdkPluginRegistry.Empty.Register(PlatformerPlugin());
        var catalog = new TargetIntrinsicCatalog("nes", "NES", [])
            .WithSdkPlugins(registry);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            catalog.Resolve("RetroSharp.Platformer2D.TouchProbe", "platformer_touch_probe"));

        Assert.Equal("Target 'nes' does not support SDK plugin feature 'RetroSharp.Platformer2D.TouchProbe' on extern function 'platformer_touch_probe'.", exception.Message);
    }

    private static SdkPluginDescriptor PlatformerPlugin()
    {
        return new SdkPluginDescriptor(
            Id: "RetroSharp.Platformer2D",
            Version: "0.1.0",
            RequiredCompilerAbi: "sdk-plugin-static-v1",
            SourcePackage: SourcePackage(),
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
                new SdkPluginTargetLoweringDescriptor("gb", "RetroSharp.Platformer2D.TouchProbe", _ => { }),
            ],
            Compatibility: new SdkPluginCompatibilityDescriptor(
                MinimumCompilerVersion: "0.0.0",
                MaximumCompilerVersion: null,
                MigrationAliases: new Dictionary<string, string>()));
    }

    private static SdkPluginSourcePackageDescriptor SourcePackage()
    {
        return new SdkPluginSourcePackageDescriptor(
            ImportPath: "RetroSharp.Platformer2D",
            SourceFactory: _ => "const __retrosharp_platformer2d = 1;\n");
    }
}
