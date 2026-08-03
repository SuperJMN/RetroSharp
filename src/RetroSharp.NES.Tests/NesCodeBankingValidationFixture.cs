namespace RetroSharp.NES.Tests;

internal static class NesCodeBankingValidationFixture
{
    public static string Directory => RepositoryPath("validation/fixtures/nes-code-banking-v1");

    public static string ProjectPath => Path.Combine(Directory, "nes-code-banking-v1.retrosharp.json");

    public static string Source => File.ReadAllText(Path.Combine(Directory, "src", "main.rs"));

    private static string RepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository directory '{relativePath}'.");
    }
}
