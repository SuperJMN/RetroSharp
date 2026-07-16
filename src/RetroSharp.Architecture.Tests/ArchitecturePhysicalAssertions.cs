namespace RetroSharp.Architecture.Tests;

using System.Text.RegularExpressions;

internal readonly record struct PhysicalFileContract(
    string RelativePath,
    string Invariant,
    IReadOnlyCollection<Type> DeclaredOwners);

internal static class ArchitecturePhysicalAssertions
{
    public static void AssertModuleOwnership(
        string nonOwnerRelativePath,
        params PhysicalFileContract[] contracts)
    {
        var root = RepositoryRoot();
        var nonOwnerSource = RequiredSource(root, nonOwnerRelativePath, "ROM builder non-owner module");
        Assert.All(contracts, contract =>
        {
            Assert.False(string.IsNullOrWhiteSpace(contract.Invariant));
            Assert.NotEmpty(contract.DeclaredOwners);
            var ownerSource = RequiredSource(root, contract.RelativePath, contract.Invariant);
            Assert.All(contract.DeclaredOwners, owner =>
            {
                var declaration = DeclarationPattern(owner);
                Assert.Matches(declaration, ownerSource);
                Assert.DoesNotMatch(declaration, nonOwnerSource);
            });
        });
    }

    private static string RequiredSource(string root, string relativePath, string invariant)
    {
        var path = Path.Combine(root, relativePath);
        Assert.True(File.Exists(path), $"Physical architecture contract failed for '{relativePath}': {invariant}");
        return File.ReadAllText(path);
    }

    private static Regex DeclarationPattern(Type owner) => new(
        $@"\b(?:class|record(?:\s+class|\s+struct)?|struct|interface|enum)\s+{Regex.Escape(owner.Name)}\b",
        RegexOptions.CultureInvariant);

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
