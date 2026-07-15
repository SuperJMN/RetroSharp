namespace RetroSharp.Architecture.Tests;

public sealed class TargetFrontendPreparationArchitectureTests
{
    private const string SharedPreparationPath = "src/RetroSharp.Sdk.Frontend/TargetFrontendPreparation.cs";

    private static readonly string[] TargetCompilerPaths =
    [
        "src/RetroSharp.GameBoy/GameBoyRomCompiler.cs",
        "src/RetroSharp.NES/NesRomCompiler.cs",
    ];

    private static readonly string[] OrderedPreparationStages =
    [
        "SdkLibrarySource.Merge(",
        "new SomeParser().Parse(",
        "TargetProgramSelector.Select(",
        "SdkImportResolver.ValidateImports(",
        "ActorFrameworkLowerer.Lower(",
        "SdkSourcePackageFacadeLowerer.Lower(",
        "LetTypeInference.ResolveOrThrow(",
        "FunctionContractValidator.ValidateProgram(",
    ];

    [Fact]
    public void Shared_frontend_module_owns_the_complete_ordered_preparation_sequence()
    {
        var root = RepositoryRoot();
        var sharedPath = Path.Combine(root, SharedPreparationPath);

        Assert.True(File.Exists(sharedPath), $"Shared frontend preparation module '{SharedPreparationPath}' must exist.");

        var sharedSource = File.ReadAllText(sharedPath);
        Assert.DoesNotContain("public sealed record TargetFrontendPreparationOptions", sharedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class PreparedTargetProgram", sharedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class TargetFrontendPreparation", sharedSource, StringComparison.Ordinal);

        var previousStageIndex = -1;
        foreach (var stage in OrderedPreparationStages)
        {
            var stageIndex = sharedSource.IndexOf(stage, StringComparison.Ordinal);
            Assert.True(stageIndex >= 0, $"Shared frontend preparation must own stage '{stage}'.");
            Assert.True(stageIndex > previousStageIndex, $"Shared frontend stage '{stage}' is out of order.");
            previousStageIndex = stageIndex;
        }

        foreach (var compilerPath in TargetCompilerPaths)
        {
            var compilerSource = File.ReadAllText(Path.Combine(root, compilerPath));
            Assert.Contains("TargetFrontendPreparation.Prepare(", compilerSource, StringComparison.Ordinal);
            Assert.All(
                OrderedPreparationStages,
                stage => Assert.DoesNotContain(stage, compilerSource, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Shared_frontend_reuses_one_actor_framework_plan_for_lowering_and_late_budget_validation()
    {
        var root = RepositoryRoot();
        var sharedSource = File.ReadAllText(Path.Combine(root, SharedPreparationPath));

        Assert.Contains("var actorFrameworkPlan = ActorFrameworkLowerer.Analyze(", sharedSource, StringComparison.Ordinal);
        Assert.Contains("ActorFrameworkLowerer.Lower(targetProgram, actorFrameworkPlan)", sharedSource, StringComparison.Ordinal);
        Assert.Contains("ActorFrameworkLowerer.ValidatePoolSpriteBudgets(\n            actorFrameworkPlan,", sharedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedPreActorProgram", sharedSource, StringComparison.Ordinal);
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
