namespace RetroSharp.Core.Tests;

using System.Text.RegularExpressions;
using Xunit;

public sealed class SdkApiLeakGuardTests
{
    private static readonly string[] ScanRoots =
    [
        "src/RetroSharp.Parser",
        "src/RetroSharp.SemanticAnalysis",
        "src/RetroSharp.Sdk.Frontend",
        "src/RetroSharp.GameBoy",
        "src/RetroSharp.NES",
        "src/RetroSharp.Core/Sdk",
    ];

    private static readonly string[] AllowedFacadeFiles =
    [
    ];

    private static readonly Regex DottedFacadePattern = new(
        @"\b(?<module>Video|Input|Audio|Camera|Sprite|World|Music|Palette|ObjectPalette|Tilemap|Hud)\.(?<method>WaitVBlank|Poll|IsDown|WasPressed|WasReleased|HoldTicks|Init|Update|SetPosition|Apply|AabbTiles|AabbHitTop|ScreenAabbTiles|ScreenAabbHitTop|Draw|Width|Load|Asset|Play|Stop|Background|Sprite|Set|Fill|SetTile)\b",
        RegexOptions.Compiled);

    private static readonly Regex FacadePairPattern = new(
        "\"(?<module>Video|Input|Audio|Camera|Sprite|World|Music|Palette|ObjectPalette|Tilemap|Hud)\"\\s*,\\s*\"(?<method>WaitVBlank|Poll|IsDown|WasPressed|WasReleased|HoldTicks|Init|Update|SetPosition|Apply|AabbTiles|AabbHitTop|ScreenAabbTiles|ScreenAabbHitTop|Draw|Width|Load|Asset|Play|Stop|Background|Sprite|Set|Fill|SetTile)\"",
        RegexOptions.Compiled);

    private static readonly Regex ActorFacadeRecognitionPattern = new(
        "Qualifier:\\s*\"(?<module>Actors|Enemies)\"\\s*,\\s*Method:\\s*\"(?<method>Pool|SpawnLayer|SpawnWindow|Def)\"",
        RegexOptions.Compiled);

    private static readonly Regex PluginOperationIdPattern = new(
        @"RetroSharp\.Platformer2D\.[A-Za-z0-9_]+",
        RegexOptions.Compiled);

    [Fact]
    public void Compiler_layers_do_not_reference_public_sdk_facade_names()
    {
        var leaks = ScanRepository().ToArray();

        Assert.True(leaks.Length == 0, "Forbidden public SDK facade references:" + Environment.NewLine + string.Join(Environment.NewLine, leaks));
    }

    [Fact]
    public void Compiler_layers_do_not_hardcode_sdk_plugin_operation_ids()
    {
        var leaks = ScanRepository()
            .Where(leak => leak.Contains("RetroSharp.Platformer2D.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            leaks.Length == 0,
            "Compiler layers must reach SDK plugin operations through descriptors, not hard-coded ids:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, leaks));
    }

    [Fact]
    public void Scanner_reports_synthetic_sdk_plugin_operation_id_leaks()
    {
        var leaks = ScanText(
            "src/RetroSharp.GameBoy/Fake.cs",
            """
            if (intrinsic.Name == "RetroSharp.Platformer2D.GroundProbe")
            {
            }
            """).ToArray();

        Assert.Contains(leaks, leak => leak.Contains("RetroSharp.Platformer2D.GroundProbe", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_reports_synthetic_public_sdk_facade_leaks()
    {
        var leaks = ScanText(
            "src/RetroSharp.Parser/Fake.cs",
            """
            // Direct dotted call.
            Sprite.Draw(hero, 0, 0, 0, false, 0);
            var pair = ("Camera", "AabbTiles");
            """).ToArray();

        Assert.Contains(leaks, leak => leak.Contains("Sprite.Draw", StringComparison.Ordinal));
        Assert.Contains(leaks, leak => leak.Contains("Camera.AabbTiles", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_reports_synthetic_actor_facade_recognition_leaks()
    {
        var leaks = ScanText(
            "src/RetroSharp.Sdk.Frontend/Fake.cs",
            """
            if (statement is ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Actors", Method: "Pool" } })
            {
            }

            if (statement is ExpressionStatementSyntax { Expression: QualifiedCallSyntax { Qualifier: "Enemies", Method: "Def" } })
            {
            }
            """).ToArray();

        Assert.Contains(leaks, leak => leak.Contains("Actors.Pool", StringComparison.Ordinal));
        Assert.Contains(leaks, leak => leak.Contains("Enemies.Def", StringComparison.Ordinal));
    }

    private static IEnumerable<string> ScanRepository()
    {
        var root = RepositoryRoot();
        var allowedFiles = AllowedFacadeFiles.ToHashSet(StringComparer.Ordinal);
        foreach (var file in ScanRoots
            .Select(rootPath => Path.Combine(root, rootPath))
            .SelectMany(rootPath => Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories))
            .Select(path => RelativePath(root, path))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal))
            .Where(path => !path.Contains(".Tests/", StringComparison.Ordinal))
            .Where(path => !allowedFiles.Contains(path))
            .Order(StringComparer.Ordinal))
        {
            foreach (var leak in ScanText(file, File.ReadAllText(Path.Combine(root, file))))
            {
                yield return leak;
            }
        }
    }

    private static IEnumerable<string> ScanText(string path, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (Match match in DottedFacadePattern.Matches(line))
            {
                yield return Leak(path, i + 1, match.Groups["module"].Value, match.Groups["method"].Value);
            }

            foreach (Match match in FacadePairPattern.Matches(line))
            {
                yield return Leak(path, i + 1, match.Groups["module"].Value, match.Groups["method"].Value);
            }

            foreach (Match match in ActorFacadeRecognitionPattern.Matches(line))
            {
                yield return Leak(path, i + 1, match.Groups["module"].Value, match.Groups["method"].Value);
            }

            foreach (Match match in PluginOperationIdPattern.Matches(line))
            {
                yield return $"{path}:{i + 1}: {match.Value}";
            }
        }
    }

    private static string Leak(string path, int line, string module, string method)
    {
        return $"{path}:{line}: {module}.{method}";
    }

    private static string RelativePath(string root, string absolutePath)
    {
        return Path.GetRelativePath(root, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
