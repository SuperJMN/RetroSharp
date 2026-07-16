namespace RetroSharp.Architecture.Tests;

public sealed class ArchitectureNavigationDocumentationTests
{
    private static readonly string[] RequiredNavigationAnchors =
    [
        "GameBoyRuntimeMemoryLayout",
        "GameBoyRuntimeMemoryLayoutTests",
        "NesRuntimeMemoryLayout",
        "NesRuntimeAbiProjection",
        "NesRuntimeMemoryLayoutTests",
        "TargetFrontendPreparation.Prepare",
        "TargetFrontendPreparationArchitectureTests",
        "ActorFrameworkLoweringPlan",
        "ActorFrameworkDomainArchitectureTests",
        "GameBoySdkOperationLowerer.Emit",
        "GameBoySdkLoweringArchitectureTests",
        "NesSdkOperationLowerer.Emit",
        "NesSdkLoweringArchitectureTests",
        "codegraph explore",
    ];

    [Fact]
    public void Agent_navigation_map_names_every_deep_module_and_test_seam()
    {
        var root = RepositoryRoot();
        var relativePath = "docs/AiNavigableArchitecture.md";
        var path = Path.Combine(root, relativePath);

        Assert.True(File.Exists(path), $"Agent architecture navigation map '{relativePath}' must exist.");
        var source = File.ReadAllText(path);
        Assert.All(RequiredNavigationAnchors, anchor => Assert.Contains(anchor, source, StringComparison.Ordinal));
        Assert.Contains(relativePath, File.ReadAllText(Path.Combine(root, "AGENTS.md")), StringComparison.Ordinal);
        Assert.Contains(relativePath, File.ReadAllText(Path.Combine(root, "llms.txt")), StringComparison.Ordinal);
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
