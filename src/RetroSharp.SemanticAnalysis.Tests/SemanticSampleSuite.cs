using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

namespace RetroSharp.SemanticAnalysis.Tests;

public class SemanticSampleSuite
{
public static IEnumerable<object[]> Cases()
        {
            var projectSamples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Samples"));
            return Directory.EnumerateFiles(projectSamples, "*.c", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}scopes{Path.DirectorySeparatorChar}"))
                .OrderBy(p => p)
                .Select(p => new object[] { p });
        }

[Theory]
    [MemberData(nameof(Cases))]
    public async Task Golden_samples(string path)
    {
        var source = await File.ReadAllTextAsync(path);
        var analyzed = SemanticTestDriver.Analyze(source);
        Assert.True(analyzed.IsSuccess, analyzed.IsFailure ? analyzed.Error : "");
        var text = SemanticSnapshotPrinter.Print(analyzed.Value);

        var name = Path.GetFileNameWithoutExtension(path);
        var dir = Path.GetDirectoryName(path)!;
await Verifier.Verify(text)
            .UseDirectory(dir)
            .UseMethodName($"SemanticSampleSuite.{name}");
    }
}
