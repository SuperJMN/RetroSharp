namespace RetroSharp.Architecture.Tests;

using System.Reflection;
using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.NES.Tests;

public sealed class NesSdkLoweringArchitectureTests
{
    private static readonly PhysicalFileContract[] PhysicalModules =
    [
        new("src/RetroSharp.NES/NesCartridgeLayout.cs", "Cartridge layout and placement have a dedicated physical navigation root.", [typeof(NesCartridgeLayout)]),
        new("src/RetroSharp.NES/NesPhysicalFrameScheduler.cs", "Physical frame scheduling has one executable target-private authority.", [typeof(NesPhysicalFrameScheduler)]),
        new("src/RetroSharp.NES/NesFramePlan.cs", "Validated frame policy remains private to the executable scheduler.", [typeof(NesFramePlan)]),
        new("src/RetroSharp.NES/NesRuntimeCompiler.cs", "Runtime compilation has a dedicated physical navigation root.", [typeof(NesRuntimeCompiler)]),
        new("src/RetroSharp.NES/NesSdkStreamReader.cs", "Collected SDK stream consumption has a dedicated physical navigation root.", [typeof(NesSdkStreamReader)]),
        new("src/RetroSharp.NES/NesSdkOperationLowerer.cs", "Portable SDK emission has a dedicated physical navigation root.", [typeof(NesSdkOperationLowerer)]),
        new("src/RetroSharp.NES/PrgBuilder.cs", "NES PRG byte building has a dedicated physical navigation root.", [typeof(PrgBuilder)]),
    ];

    [Fact]
    public void Nes_sdk_lowerer_owns_emission_without_delegating_to_runtime_compiler()
    {
        ArchitectureSymbolAssertions.AssertSdkOperationOwnership(
            typeof(NesSdkOperationLowerer),
            typeof(NesRuntimeCompiler));
    }

    [Fact]
    public void Nes_cartridge_runtime_stream_and_prg_modules_are_physically_separate()
    {
        ArchitecturePhysicalAssertions.AssertModuleOwnership(
            "src/RetroSharp.NES/NesRomBuilder.cs",
            PhysicalModules);
    }

    [Fact]
    public void Nes_production_consumes_frame_policy_only_through_the_scheduler()
    {
        const BindingFlags declaredMembers =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;
        var calls = ArchitectureSymbolAssertions.CalledMethods(typeof(NesRomBuilder));
        var lowererCalls = ArchitectureSymbolAssertions.CalledMethods(typeof(NesSdkOperationLowerer));
        var nonOwners = typeof(NesFramePlan).Assembly
            .GetTypes()
            .Where(type => !IsOwnedBy(type, typeof(NesFramePlan)) &&
                           !IsOwnedBy(type, typeof(NesPhysicalFrameScheduler)))
            .ToArray();

        Assert.Contains(calls, method => method.DeclaringType == typeof(NesPhysicalFrameScheduler));
        Assert.Contains(lowererCalls, method => method.DeclaringType == typeof(NesPhysicalFrameScheduler));
        Assert.DoesNotContain(nonOwners, type =>
            ArchitectureSymbolAssertions.CalledMethods(type)
                .Any(method => method.DeclaringType == typeof(NesFramePlan)));
        Assert.DoesNotContain(nonOwners, type =>
            type.GetFields(declaredMembers).Any(field => field.FieldType == typeof(NesFramePlan)));
        Assert.DoesNotContain(nonOwners, type =>
            type.GetMethods(declaredMembers).Any(method =>
                method.ReturnType == typeof(NesFramePlan) ||
                method.GetParameters().Any(parameter => parameter.ParameterType == typeof(NesFramePlan))));
        Assert.DoesNotContain(calls, method => method.DeclaringType == typeof(SdkCpuWorkReportFactory));
    }

    [Fact]
    public void Nes_frame_scheduler_is_the_executable_frame_policy_authority()
    {
        var schedulerCalls = ArchitectureSymbolAssertions.CalledMethods(typeof(NesPhysicalFrameScheduler));

        Assert.Contains(schedulerCalls, method => method.DeclaringType == typeof(NesFramePlan));
    }

    [Fact]
    public void Nes_explicit_video_transfers_cross_the_scheduler_as_closed_commands()
    {
        var transfer = typeof(NesVideoSafeTransfer);

        foreach (var operationType in new[]
                 {
                     typeof(Sdk2DOperation.StreamMapColumn),
                     typeof(Sdk2DOperation.StreamMapRow),
                 })
        {
            var entryPoint = Assert.Single(
                typeof(NesSdkOperationLowerer)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.GetParameters() is [{ ParameterType: var parameterType }] &&
                                     parameterType == operationType));
            Assert.Contains(
                ArchitectureSymbolAssertions.CalledMethods(entryPoint),
                method => method.DeclaringType == typeof(NesPhysicalFrameScheduler) &&
                          method.GetParameters().Any(parameter => parameter.ParameterType == transfer));
        }
    }

    [Fact]
    public void Nes_camera_staging_policy_does_not_leak_phase_constants_to_the_lowerer()
    {
        var schedulerMethods = typeof(NesPhysicalFrameScheduler)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Contains(schedulerMethods, method =>
            method.ReturnType == typeof(void) &&
            method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                [typeof(NesSdkOperationLowerer), typeof(NesCameraConfig)]));
        Assert.Contains(schedulerMethods, method =>
            method.ReturnType == typeof(void) &&
            method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                [typeof(NesWorldPackRuntimePlan)]));
        Assert.DoesNotContain(schedulerMethods, method =>
            method.ReturnType.IsGenericType &&
            method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTuple<,>));
        var packedRuntimeConsumers = typeof(NesPhysicalFrameScheduler).Assembly
            .GetTypes()
            .Where(type => type != typeof(NesPhysicalFrameScheduler) &&
                           type != typeof(NesPackedCameraRuntimeEmitter))
            .Where(type => ArchitectureSymbolAssertions.CalledMethods(type)
                .Any(method => method.DeclaringType == typeof(NesPackedCameraRuntimeEmitter)))
            .ToArray();
        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(typeof(NesPhysicalFrameScheduler)),
            method => method.DeclaringType == typeof(NesPackedCameraRuntimeEmitter));
        Assert.Empty(packedRuntimeConsumers);
    }

    [Fact]
    public void Nes_scheduler_selects_automatic_camera_transfer_order_through_closed_commands()
    {
        var apply = Assert.Single(
            typeof(NesSdkOperationLowerer)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.GetParameters() is [{ ParameterType: var parameterType }] &&
                                 parameterType == typeof(Sdk2DOperation.ApplyCamera)));
        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(apply),
            method => method.DeclaringType == typeof(NesPhysicalFrameScheduler));

        var schedulerLowererCalls = ArchitectureSymbolAssertions.CalledMethods(typeof(NesPhysicalFrameScheduler))
            .Where(method => method.DeclaringType == typeof(NesSdkOperationLowerer))
            .ToArray();
        Assert.NotEmpty(schedulerLowererCalls);
        Assert.All(schedulerLowererCalls, method => Assert.Contains(
            method.GetParameters(),
            parameter => parameter.ParameterType == typeof(NesVideoSafeTransfer)));
    }

    [Fact]
    public void Lowering_regressions_are_owned_by_the_declared_focused_sdk_suites()
    {
        ArchitectureSymbolAssertions.AssertFocusedTestOwnership(
            typeof(NesRomCompilerTests),
            [
                typeof(NesSdkCameraStreamingLoweringTests),
                typeof(NesSdkCollisionLoweringTests),
                typeof(NesSdkFrameInputLoweringTests),
                typeof(NesSdkSpriteLoweringTests),
            ]);
    }

    private static bool IsOwnedBy(Type candidate, Type owner)
    {
        for (var current = candidate; current is not null; current = current.DeclaringType)
        {
            if (current == owner)
            {
                return true;
            }
        }

        return false;
    }
}
