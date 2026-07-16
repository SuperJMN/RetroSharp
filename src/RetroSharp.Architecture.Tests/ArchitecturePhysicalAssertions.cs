namespace RetroSharp.Architecture.Tests;

internal readonly record struct PhysicalFileContract(string RelativePath, string Invariant);

internal static class ArchitecturePhysicalAssertions
{
    public static void AssertFilesExist(params PhysicalFileContract[] contracts)
    {
        var root = RepositoryRoot();
        Assert.All(contracts, contract =>
        {
            Assert.False(string.IsNullOrWhiteSpace(contract.Invariant));
            Assert.True(
                File.Exists(Path.Combine(root, contract.RelativePath)),
                $"Physical architecture contract failed for '{contract.RelativePath}': {contract.Invariant}");
        });
    }

    public static string ReadFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));
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
