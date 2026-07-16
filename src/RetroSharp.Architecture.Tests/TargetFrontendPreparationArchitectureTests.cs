using System.Reflection;
using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Sdk;

namespace RetroSharp.Architecture.Tests;

public sealed class TargetFrontendPreparationArchitectureTests
{
    private static readonly string[] OrderedPreparationStageTypes =
    [
        "RetroSharp.Sdk.SdkLibrarySource",
        "RetroSharp.Parser.SomeParser",
        "RetroSharp.Sdk.TargetProgramSelector",
        "RetroSharp.Sdk.SdkImportResolver",
        "RetroSharp.Sdk.ActorFrameworkLowerer",
        "RetroSharp.Sdk.SdkSourcePackageFacadeLowerer",
        "RetroSharp.Parser.LetTypeInference",
        "RetroSharp.Parser.FunctionContractValidator",
    ];

    [Fact]
    public void Shared_frontend_symbol_owns_the_complete_ordered_preparation_sequence()
    {
        var assembly = typeof(ActorFrameworkLowerer).Assembly;
        var preparation = ArchitectureSymbolAssertions.RequiredType(assembly, "RetroSharp.Sdk.TargetFrontendPreparation");
        var options = ArchitectureSymbolAssertions.RequiredType(assembly, "RetroSharp.Sdk.TargetFrontendPreparationOptions");
        var preparedProgram = ArchitectureSymbolAssertions.RequiredType(assembly, "RetroSharp.Sdk.PreparedTargetProgram");
        var calls = ArchitectureSymbolAssertions.CalledMethods(preparation).ToList();

        Assert.False(preparation.IsPublic);
        Assert.False(options.IsPublic);
        Assert.False(preparedProgram.IsPublic);

        var previousStageIndex = -1;
        foreach (var stageTypeName in OrderedPreparationStageTypes)
        {
            var stageIndex = calls.FindIndex(call => call.DeclaringType?.FullName == stageTypeName);
            Assert.True(stageIndex >= 0, $"Shared frontend preparation must call stage type '{stageTypeName}'.");
            Assert.True(stageIndex > previousStageIndex, $"Shared frontend stage type '{stageTypeName}' is out of order.");
            previousStageIndex = stageIndex;
        }

        AssertTargetRoutesThroughPreparation(typeof(GameBoyRomCompiler), preparation);
        AssertTargetRoutesThroughPreparation(typeof(NesRomCompiler), preparation);
    }

    [Fact]
    public void Shared_frontend_reuses_one_actor_framework_plan_for_lowering_and_late_budget_validation()
    {
        var lowerer = typeof(ActorFrameworkLowerer);
        var assembly = lowerer.Assembly;
        var preparation = ArchitectureSymbolAssertions.RequiredType(assembly, "RetroSharp.Sdk.TargetFrontendPreparation");
        var preparedProgram = ArchitectureSymbolAssertions.RequiredType(assembly, "RetroSharp.Sdk.PreparedTargetProgram");
        var plan = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "ActorFrameworkLoweringPlan");
        var preparationActorCalls = ArchitectureSymbolAssertions.CalledMethods(preparation)
            .Where(call => call.DeclaringType == lowerer)
            .ToList();

        Assert.Contains(preparationActorCalls, call => call is MethodInfo method && method.ReturnType == plan);
        Assert.Contains(preparationActorCalls, call => call.GetParameters().Any(parameter => parameter.ParameterType == plan));
        Assert.Single(preparedProgram
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == plan));
        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(preparedProgram),
            call => call.DeclaringType == lowerer && call.GetParameters().Any(parameter => parameter.ParameterType == plan));
    }

    private static void AssertTargetRoutesThroughPreparation(Type targetCompiler, Type preparation)
    {
        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(targetCompiler),
            call => call.DeclaringType == preparation);
    }
}
