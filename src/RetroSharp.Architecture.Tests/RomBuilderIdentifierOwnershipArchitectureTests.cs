namespace RetroSharp.Architecture.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using RetroSharp.GameBoy;
using RetroSharp.NES;

/// <summary>
/// Guards the boundary `docs/AgentContext.md` already states in prose: "The ROM builders link and
/// orchestrate output; they are not the owner of runtime memory, frontend stages, SDK emission, or
/// scheduling policy." Before #545/#546 that rule eroded silently because it only ever lived in
/// documentation: `GameBoyWorldPackRuntime.cs` ended up reading `GameBoyRomBuilder.WorldPack*` 31
/// times, i.e. a runtime subsystem depending on its orchestrator for its own identifiers.
/// <see cref="ArchitectureBoundaryTests"/> only watches dependencies between assemblies; this erosion
/// happened inside one assembly (`internal` is visible repo-wide), so it needs its own physical,
/// source-level check.
/// </summary>
public sealed class RomBuilderIdentifierOwnershipArchitectureTests
{
    private const BindingFlags DeclaredStaticMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void Game_boy_runtime_and_lowering_types_own_their_identifiers_instead_of_reading_the_rom_builder()
    {
        AssertNoUnownedIdentifierEscapes(
            typeof(GameBoyRomBuilder),
            "src/RetroSharp.GameBoy/GameBoyRomBuilder.cs",
            "src/RetroSharp.GameBoy",
            knownExceptions: Array.Empty<string>());

        // The exception is exercised by real code, not just "nothing references anything": the WorldPack
        // runtime is the verified owner of the `worldpack_default` data block; GameBoyRomBuilder.cs itself
        // places it (`builder.Label(WorldPackLabel)` right next to TileDataLabel/TileMapLabel), so reading
        // that one label back for pointer arithmetic is legitimate, per #545 - and it stays policed by the
        // general rule below only because the builder's own source uses `WorldPackLabel` again itself.
        Assert.Contains(
            SourceLines("src/RetroSharp.GameBoy/GameBoyWorldPackRuntime.cs"),
            line => line.Contains("GameBoyRomBuilder.WorldPackLabel", StringComparison.Ordinal));

        // And the allowed direction still holds: the builder orchestrates the WorldPack runtime emitter,
        // not the other way around.
        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(typeof(GameBoyRomBuilder)),
            method => method.DeclaringType == typeof(GameBoyWorldPackRuntimeEmitter));
    }

    /// <summary>
    /// NesRomBuilder.MainInitPlacementUnitName/MainFramePlacementUnitName/MainTailPlacementUnitName are the
    /// one known, named exception on either target: NesProgramPhaseAnalyzer.cs reads them back as banking
    /// vocabulary, then hands the resulting plan into the very builder loop that calls
    /// `EnterPlacementUnit(...)` - a plan-indirection shape #546 already reviewed and approved.
    /// This is a short, explicit list of exact names, not a reusable pattern: nobody can join it by naming a
    /// new constant `...PlacementUnitName`, because <see cref="UnownedReferences"/> only ever checks
    /// membership in this fixed list - it does not derive membership from any part of the name. And unlike
    /// a silent whitelist, its failure mode is loud: <see cref="AssertKnownExceptionsStillExist"/> fails
    /// with an actionable message the moment any of these three literal names stops being a member of
    /// <see cref="NesRomBuilder"/> (a rename or removal), forcing this list to be revisited rather than
    /// leaving a stale, unenforced entry or a silent new gap.
    /// </summary>
    private static readonly string[] NesKnownPlanIndirectionExceptions =
    {
        "MainInitPlacementUnitName",
        "MainFramePlacementUnitName",
        "MainTailPlacementUnitName",
    };

    [Fact]
    public void Nes_runtime_and_lowering_types_own_their_identifiers_instead_of_reading_the_rom_builder()
    {
        AssertKnownExceptionsStillExist(typeof(NesRomBuilder), NesKnownPlanIndirectionExceptions);

        AssertNoUnownedIdentifierEscapes(
            typeof(NesRomBuilder),
            "src/RetroSharp.NES/NesRomBuilder.cs",
            "src/RetroSharp.NES",
            NesKnownPlanIndirectionExceptions);

        // Same non-vacuous check as Game Boy: the WorldPack runtime is a genuine consumer of the
        // self-placed WorldPackLabel, and that stays policed-but-tolerated purely through the general
        // self-use rule (the builder itself uses WorldPackLabel more than once).
        Assert.Contains(
            SourceLines("src/RetroSharp.NES/NesWorldPackRuntime.cs"),
            line => line.Contains("NesRomBuilder.WorldPackLabel", StringComparison.Ordinal));

        // And the known exception is exercised by real code, not vacuous: NesProgramPhaseAnalyzer.cs really
        // does read the exact name the exception lists.
        Assert.Contains(
            SourceLines("src/RetroSharp.NES/NesProgramPhaseAnalyzer.cs"),
            line => line.Contains("NesRomBuilder.MainInitPlacementUnitName", StringComparison.Ordinal));

        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(typeof(NesRomBuilder)),
            method => method.DeclaringType == typeof(NesWorldPackRuntimeEmitter));
        Assert.Contains(
            ArchitectureSymbolAssertions.CalledMethods(typeof(NesRomBuilder)),
            method => method.DeclaringType == typeof(NesProgramPhaseAnalyzer));
    }

    /// <summary>
    /// Fails if any production file under <paramref name="sourceRootRelativePath"/> reads a string
    /// identifier (a label constant, or a label-factory method) that <paramref name="builder"/> declares
    /// but never places itself, unless the identifier's exact name is in <paramref name="knownExceptions"/>.
    /// Compile-time `const string` labels are inlined by the compiler at every call site, so IL call-graph
    /// inspection (as used elsewhere in this project) cannot see who really references them; this needs a
    /// physical, source-text check instead, in the same style as <see cref="ArchitecturePhysicalAssertions"/>.
    /// Scans <see cref="SearchOption.AllDirectories"/> (excluding `bin`/`obj`), not just the top directory,
    /// so a leak hidden in a subdirectory cannot go unexamined.
    /// </summary>
    private static void AssertNoUnownedIdentifierEscapes(
        Type builder,
        string builderRelativePath,
        string sourceRootRelativePath,
        IReadOnlyCollection<string> knownExceptions)
    {
        var root = ArchitecturePhysicalAssertions.RepositoryRoot();
        var builderPath = Path.Combine(root, builderRelativePath);
        Assert.True(File.Exists(builderPath), $"ROM builder source '{builderRelativePath}' must exist.");
        var builderSource = File.ReadAllText(builderPath);

        var sourceRoot = Path.Combine(root, sourceRootRelativePath);
        var otherFiles = ProductionSourceFiles(sourceRoot)
            .Where(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/') != builderRelativePath)
            .ToList();

        var violations = StringIdentifierMemberNames(builder)
            .Where(member => !IsPlacedByTheBuilderItself(member, builderSource) && !knownExceptions.Contains(member))
            .SelectMany(member => UnownedReferences(builder, member, otherFiles, root))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Runtime/lowering types depend on '{builder.Name}' for identifiers it never places itself:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Every `*.cs` file under <paramref name="sourceRoot"/>, at any depth, excluding build output
    /// (`bin`/`obj`) - a plain <see cref="SearchOption.TopDirectoryOnly"/> scan would silently miss a leak
    /// placed in a subdirectory.
    /// </summary>
    private static IReadOnlyList<string> ProductionSourceFiles(string sourceRoot)
    {
        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(sourceRoot, file).Split(Path.DirectorySeparatorChar);
                return !relative.Contains("bin", StringComparer.Ordinal) && !relative.Contains("obj", StringComparer.Ordinal);
            })
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Fails with an actionable message if any name in <paramref name="knownExceptions"/> is no longer a
    /// static string member of <paramref name="builder"/>. This is what turns the exception list from
    /// "silently stops applying" into "loudly demands review": if one of these names is ever renamed or
    /// removed on the production side, this is what catches it, instead of the list quietly becoming a set
    /// of dead strings nobody notices - or worse, one of those dead strings coincidentally matching a
    /// brand-new, unrelated member later.
    /// </summary>
    private static void AssertKnownExceptionsStillExist(Type builder, IReadOnlyCollection<string> knownExceptions)
    {
        var currentMembers = new HashSet<string>(StringIdentifierMemberNames(builder), StringComparer.Ordinal);
        var stale = knownExceptions.Where(name => !currentMembers.Contains(name)).ToList();
        Assert.True(
            stale.Count == 0,
            $"The known-exception list for '{builder.Name}' names member(s) that no longer exist: " +
            $"{string.Join(", ", stale)}. This exception was justified by a specific, reviewed indirection " +
            "(see #546) - it does not carry over automatically to a rename. Update the exception list in " +
            $"{nameof(RomBuilderIdentifierOwnershipArchitectureTests)} to match the current member name, or " +
            "remove the entry if the indirection no longer exists.");
    }

    /// <summary>
    /// The candidate set is every non-private static member of the builder whose value is a name/label
    /// string: `const string` fields (<c>IsLiteral</c>), `static readonly string` fields (<c>IsInitOnly</c>
    /// - the only other way a label expression that is not itself a compile-time constant, e.g. one built
    /// with `nameof` or string formatting, can be declared in C#), and `string`-returning factory methods
    /// such as `MapRowLabel(int row)`. Private members are excluded because the compiler already forbids
    /// reading them from another file; only members reachable from elsewhere in the assembly can leak.
    /// </summary>
    private static IReadOnlyList<string> StringIdentifierMemberNames(Type builder)
    {
        var fieldNames = builder
            .GetFields(DeclaredStaticMembers)
            .Where(field => (field.IsLiteral || field.IsInitOnly) && !field.IsPrivate && field.FieldType == typeof(string))
            .Select(field => field.Name);
        var methodNames = builder
            .GetMethods(DeclaredStaticMembers)
            .Where(method => !method.IsPrivate && !method.IsSpecialName && method.ReturnType == typeof(string))
            .Select(method => method.Name);

        return fieldNames.Concat(methodNames).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A member is legitimately builder-owned only when the builder's own source actually places the byte
    /// range the identifier names - not merely when the name happens to appear twice anywhere in the file
    /// (a raw occurrence count is satisfied just as well by an explanatory comment or a log message that
    /// repeats the name, which would silently exempt that member from every other file's scrutiny). This
    /// requires the member to appear as the argument to one of the builder's own emission calls -
    /// `Label(...)`, `DefineExternalLabel(...)`, or `Emit(...)` - which is how every current label constant
    /// and label-factory method (`WorldPackLabel`, `WorldMapRowLabel(row)`, `MusicDataLabel(name)`, ...) is
    /// actually placed in both `NesRomBuilder.cs` and `GameBoyRomBuilder.cs`.
    /// </summary>
    private static bool IsPlacedByTheBuilderItself(string member, string builderSource)
    {
        var placementCall = new Regex($@"\.(?:Label|DefineExternalLabel|Emit)\s*\(\s*{Regex.Escape(member)}\b");
        return placementCall.IsMatch(builderSource);
    }

    /// <summary>
    /// Scans for a fully-qualified `{Builder}.{Member}` reference, one physical line at a time. This is a
    /// textual check, so it has three known, accepted blind spots - each verified to have zero precedent in
    /// the current GameBoy/NES source trees, so none of them is a live gap today:
    /// <list type="bullet">
    /// <item>A file that opens `using static RetroSharp.NES.NesRomBuilder;` (or the Game Boy equivalent) and
    /// then reads the bare, unqualified member name would not match this fully-qualified pattern.</item>
    /// <item>A file that aliases the builder type, e.g. `using Builder = RetroSharp.NES.NesRomBuilder;`, and
    /// then reads `Builder.Member`, would not match either - the alias name is not `{Builder}`.</item>
    /// <item>A `.cs` file linked into a target project via an explicit `&lt;Compile Include&gt;` from
    /// outside its `src/RetroSharp.NES`/`src/RetroSharp.GameBoy` directory would never be enumerated by
    /// <see cref="ProductionSourceFiles"/>, which walks the physical directory tree, not the project file.
    /// Every project in this repository is SDK-style with implicit globbing (no explicit
    /// `&lt;Compile Include&gt;`), so this cannot happen without someone deliberately editing a `.csproj`.
    /// </item>
    /// </list>
    /// Closing these would require actual symbol resolution (a Roslyn compilation), not a textual scan, and
    /// this test intentionally stays textual, in the same style as
    /// <see cref="ArchitecturePhysicalAssertions"/> - so if either of the first two conventions is ever
    /// introduced against a ROM builder identifier, this check needs revisiting.
    /// </summary>
    private static IEnumerable<string> UnownedReferences(
        Type builder,
        string member,
        IReadOnlyCollection<string> otherFiles,
        string root)
    {
        var reference = new Regex($@"\b{Regex.Escape(builder.Name)}\.{Regex.Escape(member)}\b");
        foreach (var file in otherFiles)
        {
            var relativeFile = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!reference.IsMatch(lines[index]))
                {
                    continue;
                }

                yield return
                    $"{relativeFile}:{index + 1} references '{builder.Name}.{member}', an identifier " +
                    $"'{builder.Name}' declares but never places itself. Move '{member}' (and the emission " +
                    $"that gives it meaning) into the runtime/lowering type that owns it - the way WorldPack " +
                    "label constants moved to GameBoyWorldPackRuntimeEmitter/NesWorldPackRuntimeEmitter in " +
                    $"#545/#546 - instead of reading it off '{builder.Name}'.";
            }
        }
    }

    private static IReadOnlyList<string> SourceLines(string relativePath)
    {
        return File.ReadAllLines(Path.Combine(ArchitecturePhysicalAssertions.RepositoryRoot(), relativePath));
    }
}
