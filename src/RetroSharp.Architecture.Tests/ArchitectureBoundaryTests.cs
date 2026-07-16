namespace RetroSharp.Architecture.Tests;

using System.Reflection;
using System.Xml.Linq;
using RetroSharp.Core.Sdk;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] LanguageProjects =
    [
        "src/RetroSharp.Parser/RetroSharp.Parser.csproj",
        "src/RetroSharp.Parser.Model/RetroSharp.Parser.Model.csproj",
        "src/RetroSharp.SemanticAnalysis/RetroSharp.SemanticAnalysis.csproj",
        "src/RetroSharp.Generation.Intermediate/RetroSharp.Generation.Intermediate.csproj",
    ];

    private static readonly string[] LanguageSourceRoots =
    [
        "src/RetroSharp.Parser",
        "src/RetroSharp.Parser.Model",
        "src/RetroSharp.SemanticAnalysis",
        "src/RetroSharp.Generation.Intermediate",
    ];

    private static readonly string[] PortableSdkProjects =
    [
        "src/RetroSharp.Core/RetroSharp.Core.csproj",
        "src/RetroSharp.Sdk.Frontend/RetroSharp.Sdk.Frontend.csproj",
    ];

    private static readonly string[] NonTargetSourceRoots =
    [
        "src/RetroSharp.Core",
        "src/RetroSharp.Sdk.Frontend",
        "src/RetroSharp.Parser",
        "src/RetroSharp.Parser.Model",
        "src/RetroSharp.SemanticAnalysis",
        "src/RetroSharp.Generation.Intermediate",
    ];

    private static readonly string[] ForbiddenLanguageReferences =
    [
        "RetroSharp.Sdk.Frontend",
        "RetroSharp.GameBoy",
        "RetroSharp.NES",
    ];

    private static readonly string[] ForbiddenLanguageTerms =
    [
        "Camera",
        "Sprite",
        "Tilemap",
        "TileMap",
        "Controller",
        "Button",
        "GameBoy",
        "Game Boy",
        "NES",
        "Sdk2D",
        "SdkPlugin",
        "PPU",
        "APU",
        "OAM",
        "VRAM",
        "WRAM",
    ];

    private static readonly string[] RawHardwareTerms =
    [
        "PPU",
        "APU",
        "OAM",
        "VRAM",
        "WRAM",
        "2A03",
        "DMG",
        "register 0x",
        "$2000",
        "$4000",
    ];

    private static readonly string[] AllowedNonTargetRawHardwareFiles =
    [
        "src/RetroSharp.Core/Sdk/VgmImporter.cs",
        "src/RetroSharp.Sdk.Frontend/Sdk2DOperationCollector.cs",
    ];

    private static readonly string[] PortableWorldPackSourceFiles =
    [
        "src/RetroSharp.Core/Sdk/WorldPack.cs",
        "src/RetroSharp.Core/Sdk/WorldPackSerializer.cs",
        "src/RetroSharp.Core/Sdk/Tiled/TiledWorldPackPlan.cs",
    ];

    [Fact]
    public void Language_projects_do_not_reference_sdk_frontend_or_concrete_targets()
    {
        var violations = ProjectReferenceViolations(LanguageProjects, ForbiddenLanguageReferences);

        Assert.Empty(violations);
    }

    [Fact]
    public void Portable_sdk_projects_do_not_reference_concrete_target_assemblies()
    {
        var violations = ProjectReferenceViolations(PortableSdkProjects, ["RetroSharp.GameBoy", "RetroSharp.NES"]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Portable_world_pack_model_does_not_expose_target_storage_terms()
    {
        var root = RepositoryRoot();
        var modelFiles = PortableWorldPackSourceFiles
            .Select(relativePath => Path.Combine(root, relativePath))
            .ToArray();
        Assert.All(modelFiles, file => Assert.True(File.Exists(file), $"Portable WorldPack source '{Path.GetRelativePath(root, file)}' must exist."));

        string[] forbiddenTerms =
        [
            "gameboy",
            "game boy",
            "nes",
            "mbc",
            "mapper",
            "bank",
            "cartridge",
            "ppu",
            "chr",
            "prg",
            "register",
            "address",
        ];
        var violations = modelFiles
            .SelectMany(file => File.ReadLines(file)
                .Select((text, index) => (Text: text, Line: index + 1))
                .SelectMany(line => forbiddenTerms
                    .Where(term => line.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .Select(term => $"{Path.GetRelativePath(root, file)}:{line.Line} exposes forbidden term '{term}'.")))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Language_sources_do_not_contain_portable_sdk_or_target_domain_terms()
    {
        var violations = SourceTermMatches(LanguageSourceRoots, ForbiddenLanguageTerms);

        Assert.Empty(violations);
    }

    [Fact]
    public void Non_target_raw_hardware_terms_are_explicitly_allowlisted()
    {
        var matches = SourceTermMatches(NonTargetSourceRoots, RawHardwareTerms);
        var allowed = AllowedNonTargetRawHardwareFiles.ToHashSet(StringComparer.Ordinal);
        var observedAllowedFiles = matches
            .Select(match => match.RelativePath)
            .Where(allowed.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var disallowedMatches = matches
            .Where(match => !allowed.Contains(match.RelativePath))
            .Select(match => match.ToString())
            .ToArray();

        Assert.Equal(AllowedNonTargetRawHardwareFiles.Order(StringComparer.Ordinal), observedAllowedFiles);
        Assert.Empty(disallowedMatches);
    }

    [Fact]
    public void Sdk_operation_inventory_documentation_lists_current_compiler_owned_operations()
    {
        var root = RepositoryRoot();
        var roadmap = File.ReadAllText(Path.Combine(root, "docs/ArchitectureRoadmap.md"));
        var section = MarkdownSection(roadmap, "## Compiler-Owned SDK Operation Inventory");
        var expectedEntries = CompilerOwnedSdkOperationNames();

        var missing = expectedEntries
            .Where(entry => !section.Contains($"`{entry}`", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(missing);
    }

    private static IReadOnlyList<string> ProjectReferenceViolations(IEnumerable<string> projectPaths, IReadOnlyCollection<string> forbiddenProjectNames)
    {
        var root = RepositoryRoot();
        var violations = new List<string>();
        foreach (var projectPath in projectPaths)
        {
            var projectFile = Path.Combine(root, projectPath);
            var document = XDocument.Load(projectFile);
            var references = document
                .Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFileNameWithoutExtension(include!));

            violations.AddRange(
                references
                    .Where(reference => forbiddenProjectNames.Contains(reference, StringComparer.Ordinal))
                    .Select(reference => $"{projectPath} references forbidden project {reference}."));
        }

        return violations;
    }

    private static IReadOnlyList<SourceMatch> SourceTermMatches(IEnumerable<string> roots, IReadOnlyCollection<string> terms)
    {
        var repositoryRoot = RepositoryRoot();
        var matches = new List<SourceMatch>();
        foreach (var root in roots)
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root);
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                var lines = File.ReadLines(file).Select((text, index) => (Text: text, LineNumber: index + 1));
                foreach (var (text, lineNumber) in lines)
                {
                    foreach (var term in terms)
                    {
                        if (text.Contains(term, StringComparison.Ordinal))
                        {
                            matches.Add(new SourceMatch(relativePath, lineNumber, term));
                        }
                    }
                }
            }
        }

        return matches;
    }

    private static IReadOnlyList<string> CompilerOwnedSdkOperationNames()
    {
        return
        [
            .. NestedOperationNames(typeof(Sdk2DOperation)),
            .. NestedOperationNames(typeof(SdkAudioOperation)),
            .. Enum.GetNames<TargetIntrinsicOperation>().Select(name => $"{nameof(TargetIntrinsicOperation)}.{name}"),
        ];
    }

    private static IEnumerable<string> NestedOperationNames(Type owner)
    {
        return owner
            .GetNestedTypes(BindingFlags.Public)
            .Where(type => type.IsAssignableTo(owner))
            .Select(type => $"{owner.Name}.{type.Name}");
    }

    private static string MarkdownSection(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var next = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0
            ? markdown[start..]
            : markdown[start..next];
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

    private sealed record SourceMatch(string RelativePath, int LineNumber, string Term)
    {
        public override string ToString() => $"{RelativePath}:{LineNumber} contains forbidden term '{Term}'.";
    }
}
