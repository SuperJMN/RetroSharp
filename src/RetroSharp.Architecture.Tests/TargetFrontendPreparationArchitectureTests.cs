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
        var prepare = preparation.GetMethod(
            "Prepare",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(prepare);
        var calls = ArchitectureSymbolAssertions.CalledMethods(prepare).ToList();

        Assert.False(preparation.IsPublic);
        Assert.False(options.IsPublic);
        Assert.False(preparedProgram.IsPublic);

        ArchitectureSymbolAssertions.AssertExclusiveFrontendPreparation(
            preparation,
            OrderedPreparationStageTypes
                .Select(typeName => calls
                    .Select(call => call.DeclaringType)
                    .First(type => type?.FullName == typeName)!)
                .ToArray(),
            typeof(GameBoyRomCompiler),
            typeof(NesRomCompiler));

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

    [Fact]
    public void Monolithic_compiler_regressions_do_not_reconstruct_extracted_frontend_stages()
    {
        var root = RepositoryRoot();
        var forbiddenCalls = new[]
        {
            "new SomeParser",
            "TargetProgramSelector.Select(",
            "ActorFrameworkLowerer.Lower(",
            "SdkSourcePackageFacadeLowerer.Lower(",
        };

        foreach (var relativePath in new[]
                 {
                     "src/RetroSharp.GameBoy.Tests/GameBoyRomCompilerTests.cs",
                     "src/RetroSharp.NES.Tests/NesRomCompilerTests.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.All(
                forbiddenCalls,
                call => Assert.DoesNotContain(call, source, StringComparison.Ordinal));
        }
    }

    private static void AssertTargetRoutesThroughPreparation(Type targetCompiler, Type preparation)
    {
        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(targetCompiler),
            call => call.DeclaringType == preparation);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
