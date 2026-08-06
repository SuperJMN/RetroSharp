namespace RetroSharp.Architecture.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RetroSharp.Core.Sdk;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] LanguageProjects =
    [
        "src/RetroSharp.Parser/RetroSharp.Parser.csproj",
        "src/RetroSharp.Parser.Model/RetroSharp.Parser.Model.csproj",
    ];

    private static readonly string[] LanguageSourceRoots =
    [
        "src/RetroSharp.Parser",
        "src/RetroSharp.Parser.Model",
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

    private const string PortableWorldPackDirectory = "src/RetroSharp.Core/Sdk";

    // #553: this used to be a fixed 3-file list (WorldPack.cs, WorldPackSerializer.cs,
    // TiledWorldPackPlan.cs), so every file #549-#552 added under Core/Sdk (SdkStreamReader,
    // SymbolFileProjection, TiledCellExpansion, WorldPackRuntimeResult,
    // WorldPackStagingLayoutAllocator, PackedCameraStateProtocol, ...) was silently
    // uncovered. Discovering every *.cs file under Core/Sdk recursively closes that gap for
    // both current and future files. Re-running the existing forbidden-term check (unchanged
    // below) over the full directory surfaces exactly six pre-existing files that were never
    // part of the WorldPack model lineage this test protects and were never covered before.
    //
    // This is NOT a list of blanket-permitted exceptions: every file below was read in full
    // and judged individually against one question -- does the match reflect an actual
    // literal target name (or a hardcoded target-specific memory value) appearing in Core's
    // own public API surface (a real, if narrow, leak of target vocabulary into Core), or is
    // it a coincidental hit that is either (a) an unrelated English-word meaning, (b) a
    // generic/parameterized API where the target only ever appears as caller-supplied runtime
    // data rather than as part of an identifier, or (c) vocabulary forced by an external,
    // already-standardized format Core must faithfully parse for both known targets? Five of
    // the six are judged incidental below; SdkCpuWorkReport.cs is judged a real, narrow leak
    // and is reported here rather than fixed, per #553's "no production changes" constraint --
    // a future issue should decide whether to flatten its two target-named factory methods
    // into the single generic method its own private implementation already uses internally.
    // The allowlist is asserted to match observations *after* the same line-level scan runs
    // over every file, never applied before scanning, so it cannot silently widen either.
    private static readonly string[] PortableWorldPackAllowedNonConformingFiles =
    [
        // Incidental: VgmChip.GameBoyDmg/Nes2A03 name the two sound-chip variants the VGM
        // binary format itself distinguishes by clock-rate header field; importing per-chip
        // register/bank commands for exactly the two chips RetroSharp currently targets is
        // forced by that external format, not an internal API choice -- a third target's chip
        // would add a third enum member the same way, not restructure this type. VgmImporter.cs
        // already carries a separate, explicit allowance under
        // Non_target_raw_hardware_terms_are_explicitly_allowlisted (AllowedNonTargetRawHardwareFiles)
        // for the same reason.
        "src/RetroSharp.Core/Sdk/VgmImporter.cs",

        // Incidental: the only matches are "register"/"registered", the English verb for
        // adding a plugin to a registry (e.g. "SDK plugin 'x' is already registered"). No
        // hardware register, and no target name, appears anywhere in this file.
        "src/RetroSharp.Core/Sdk/SdkPluginDescriptor.cs",

        // Incidental: same false match as SdkPluginDescriptor.cs -- "register"/"registered"
        // as the resource-registration verb, not a hardware register. No target name appears
        // anywhere in this file either.
        "src/RetroSharp.Core/Sdk/SdkResourceDeclarationDescriptor.cs",
        // (SdkResourceDeclarationRegistry.Register also stays even though its one parameter
        // is named SdkPluginDescriptor -- a type reference, not a fresh vocabulary match.)

        // Incidental: ResolvePngVariant/ResolveVariant, its only public entry points, take
        // `platform` as a runtime string parameter -- "gb"/"gameboy"/"nes" only ever appear as
        // data inside a private switch (PlatformSuffixes) and a private suffix list
        // (KnownPlatformSuffixes), never as part of a public identifier. Naming the platform
        // is the documented API surface (resolving per-platform asset file variants is this
        // type's entire purpose), not a storage-term leak into Core's own naming.
        "src/RetroSharp.Core/Sdk/PlatformAssetPathResolver.cs",

        // REAL, NARROW LEAK -- reported, not fixed (out of scope for #553's "no production
        // changes" constraint): SdkCpuWorkReportFactory exposes two public factory methods,
        // ForGameBoy and ForNes, that bake the two target names directly into Core's own
        // public API surface. This differs from PlatformAssetPathResolver above: there the
        // target only ever appears as caller-supplied data behind a generic public signature;
        // here the private CreateTargetReport(target: string, profile, unit, frameWindow, ...)
        // this file already implements is fully generic, so ForGameBoy/ForNes are not required
        // by any external format or by the varying calibration data itself -- they are a
        // RetroSharp-internal API-shaping choice that could be flattened into one generic
        // public entry point (mirroring CreateTargetReport) without losing anything. Left
        // exactly as-is; a future issue should decide whether to flatten it.
        "src/RetroSharp.Core/Sdk/SdkCpuWorkReport.cs",

        // Incidental: the only matches are "Address" (a generic ushort name-to-numeric-value
        // pairing -- the same vocabulary used by ELF/DWARF/PDB-style debug symbol formats for
        // any CPU architecture). No literal "GameBoy"/"NES" target name, and no hardcoded
        // target-specific memory value, appears anywhere in this file; targets only ever
        // supply their own label and their own address sources as caller-provided data (see
        // this file's own header comment). Reusing the generic word "address" for a generic
        // symbol-table concept is not a target-vocabulary leak.
        "src/RetroSharp.Core/Sdk/SymbolFileProjection.cs",
    ];

    private static readonly string[] RunnerContractTestFiles =
    [
        "src/RetroSharp.Architecture.Tests/ArchitectureBoundaryTests.cs",
        "src/RetroSharp.Cli.Tests/CrossTargetCliAcceptanceTests.cs",
        "src/RetroSharp.Core.Tests/SampleApiQuarantineTests.cs",
        "src/RetroSharp.GameBoy.Tests/GameBoyRunnerSmokeTests.cs",
        "src/RetroSharp.NES.Tests/NesRunnerSmokeTests.cs",
        // The runner is the acceptance scene for the NES video-safe budget: it is the only
        // shipping sample that drives a full-height streamed band and retained sprites through
        // one VBlank. These assert the same smoke contract (boots, keeps running, no unsafe
        // PPU/OAM writes), never authored gameplay.
        "src/RetroSharp.NES.Tests/NesVideoSafeBudgetTests.cs",
        "src/RetroSharp.NES.Tests/NesVideoSafeBudgetProbe.cs",
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
        var sdkRoot = Path.Combine(root, PortableWorldPackDirectory);
        Assert.True(Directory.Exists(sdkRoot), $"Portable SDK directory '{PortableWorldPackDirectory}' must exist.");
        var modelFiles = Directory.EnumerateFiles(sdkRoot, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(modelFiles);

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
        var matches = modelFiles
            .Select(file => (RelativePath: Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), File: file))
            .SelectMany(entry => File.ReadLines(entry.File)
                .Select((text, index) => (Text: text, Line: index + 1))
                .Where(line => !line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .SelectMany(line => forbiddenTerms
                    .Where(term => ContainsPortableWorldPackForbiddenTerm(line.Text, term))
                    .Select(term => (entry.RelativePath, Description: $"{entry.RelativePath}:{line.Line} exposes forbidden term '{term}'."))))
            .ToArray();

        var allowed = PortableWorldPackAllowedNonConformingFiles.ToHashSet(StringComparer.Ordinal);
        var observedAllowedFiles = matches
            .Select(match => match.RelativePath)
            .Where(allowed.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var disallowedMatches = matches
            .Where(match => !allowed.Contains(match.RelativePath))
            .Select(match => match.Description)
            .ToArray();

        Assert.Equal(PortableWorldPackAllowedNonConformingFiles.Order(StringComparer.Ordinal), observedAllowedFiles);
        Assert.Empty(disallowedMatches);
    }

    private static bool ContainsPortableWorldPackForbiddenTerm(string text, string term) =>
        term == "nes"
            ? Regex.IsMatch(
                text,
                @"(?:^|[^A-Za-z])nes(?:$|[^A-Za-z])|(?:^|[^A-Za-z]|[a-z0-9])(?:Nes|NES)(?:$|[^a-z]|[A-Z])")
            : text.Contains(term, StringComparison.OrdinalIgnoreCase);

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
    public void Tests_only_reference_the_runner_through_smoke_or_structural_contracts()
    {
        var root = RepositoryRoot();
        var allowed = RunnerContractTestFiles.ToHashSet(StringComparer.Ordinal);
        var violations = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path.Contains(".Tests/", StringComparison.Ordinal))
            .Where(path => !allowed.Contains(path))
            .SelectMany(path => File.ReadLines(Path.Combine(root, path))
                .Select((text, index) => (Path: path, Line: index + 1, Text: text)))
            .Where(line => line.Text.Contains("RunnerSample", StringComparison.Ordinal)
                || line.Text.Contains("samples/runner/", StringComparison.Ordinal))
            .Select(line => $"{line.Path}:{line.Line} references the editable runner.")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Non_runner_samples_do_not_depend_on_runner_assets()
    {
        var root = RepositoryRoot();
        var samplesRoot = Path.Combine(root, "samples");
        var runnerRoot = Path.Combine(samplesRoot, "runner") + Path.DirectorySeparatorChar;
        string[] inspectedExtensions = [".rs", ".json", ".tmj", ".tmx", ".tsj", ".tsx"];
        var violations = Directory.EnumerateFiles(samplesRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(runnerRoot, StringComparison.Ordinal))
            .Where(path => inspectedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path)
                .Select((text, index) => (Path: path, Line: index + 1, Text: text)))
            .Where(line => line.Text.Contains("runner/assets", StringComparison.Ordinal))
            .Select(line => $"{Path.GetRelativePath(root, line.Path)}:{line.Line} depends on runner/assets.")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Sdk_operation_inventory_documentation_lists_current_compiler_owned_operations()
    {
        var root = RepositoryRoot();
        var overview = File.ReadAllText(Path.Combine(root, "docs/ArchitectureOverview.md"));
        var section = MarkdownSection(overview, "## Compiler-Owned SDK Operation Inventory");
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

    private static string RepositoryRoot() => ArchitecturePhysicalAssertions.RepositoryRoot();

    private sealed record SourceMatch(string RelativePath, int LineNumber, string Term)
    {
        public override string ToString() => $"{RelativePath}:{LineNumber} contains forbidden term '{Term}'.";
    }
}
