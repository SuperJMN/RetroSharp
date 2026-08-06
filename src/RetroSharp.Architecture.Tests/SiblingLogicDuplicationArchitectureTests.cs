namespace RetroSharp.Architecture.Tests;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.NES;

// Part of #553 (epic #542): every existing architecture test in this project watches
// *dependency direction* (Core/Sdk.Frontend must not reference GameBoy/NES) or *vocabulary
// leaks* (Core must not mention target storage terms). None of them watch for the defect
// this epic actually found ~700-800 lines of: two sibling target assemblies independently
// reimplementing the *same target-neutral logic* with no shared owner. That is exactly how
// the SDK stream reader got tripled (#549) and how WorldPack/PackedCamera result enums and
// staging helpers got duplicated (#550-#552) without a single test failing.
//
// The hard part is telling *emission* (SM83 vs 6502 opcode emission is legitimately similar
// in shape and must NOT be flagged) apart from *pure logic* (identical stack machines,
// identical result codes, identical parsing helpers, which SHOULD have one Core.Sdk owner).
// This file follows the issue's suggested heuristic, validated against the real repository:
//
//   1. A type/method that references its target's assembler builder (GbBuilder/PrgBuilder)
//      or a memory-layout type is emission or hardware placement, never a duplication
//      candidate (checked by exact type identity for the two known builders, and by a
//      "Layout" name fragment for every current layout type: GameBoyRomLayout,
//      GameBoyRuntimeMemoryLayout, NesRuntimeMemoryLayout, NesCartridgeLayout, ...).
//   2. A method that forwards its real work to code already reachable from BOTH target
//      assemblies (RetroSharp.Core, RetroSharp.Parser, RetroSharp.Sdk.Frontend -- computed
//      dynamically from each assembly's own referenced-assembly list, not hardcoded) has
//      already been given a shared owner; it is not a fresh duplicate. Plain data-shape
//      access (a property or indexer getter, e.g. reading `call.Arguments.Count`) does not
//      count as "forwarding work", only calling a real shared method does, and forwarding
//      through the type's own private helpers is followed transitively so a thin wrapper
//      around a wrapper still counts as delegating.
//
// Coverage measured against the current tree (also reported in the task summary): out of 106
// Game Boy and 210 NES types (top-level and nested; Assembly.GetTypes() already flattens
// nested types, see TopLevelTypes below), 29 Game Boy and 31 NES types pass the "does not
// touch a builder or memory-layout type" filter -- now resolved per source-file fragment via
// PdbFragmentResolver, not per whole type -- and have at least one own, non-delegating
// method; those are the only types this rule inspects for shared shapes. Every
// GameBoySdkOperationLowerer/NesSdkOperationLowerer fragment file today still touches
// emission on its own merits (each either references the owning GbBuilder/PrgBuilder field,
// calls a builder/layout method, or has a builder/layout-typed signature somewhere in that
// same file), so neither lowerer currently contributes any candidate shape -- but the filter
// is no longer type-wide: a brand-new partial file added to either lowerer with genuinely
// pure logic *would* surface as a candidate (verified empirically, see the comment on
// LocalNonDelegatingMethodShape).
public sealed class SiblingLogicDuplicationArchitectureTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // A method-name/arity signature must be shared by at least this many members before two
    // types are reported as likely duplicates. The triplicated stream reader (#549) shared
    // five signatures (ConsumeOperation, ConsumeSubroutineCall, EnterSubroutine,
    // LeaveSubroutine, EnsureAllConsumed); the duplicated WorldPack staging helpers (#550-
    // #552) shared several more. Below this bar, the only match in the current tree is
    // GameBoyWramRange/NesRamRange sharing two generic range-check members (Contains,
    // Overlaps) -- exactly the kind of small-utility-shape coincidence flagging would make
    // this rule the noisy, unreliable kind the issue warns against.
    private const int MinimumSharedMemberCount = 3;

    private static readonly HashSet<string> CompilerSynthesizedMemberNames = new(StringComparer.Ordinal)
    {
        "ToString", "Equals", "GetHashCode", "PrintMembers", "Deconstruct", "get_EqualityContract",
    };

    // This is a registry of KNOWN, UNRESOLVED DUPLICATION DEBT, not a list of permitted
    // exceptions -- every pair below is real target-neutral logic this rule's shape
    // heuristic genuinely detects today, sitting outside a Core.Sdk owner. #553 only added
    // the detector; unifying these four pairs is out of scope for it ("no modifiques código
    // de producción") and is tracked as follow-up work in issue #566, which records the same
    // four pairs with their shared-signature counts. The expected way this list shrinks is
    // one entry disappearing every time a pair gets a Core.Sdk owner (mirroring exactly what
    // #549-#552 already did for the stream reader, symbol projection, Tiled cell expansion,
    // and WorldPack/PackedCamera types) -- an entry staying here is not "accepted", it is
    // "not yet done". Each pair is bound by typeof(...), not by name or file, so renaming a
    // member cannot shrink this list, and adding a pair requires visibly editing this test
    // file; the test asserts this list matches what is *observed* today (see
    // Assert.Equal(expectedKnownPairs, observedKnownPairs) below), so it cannot silently grow
    // either -- a fifth pair crossing the threshold makes the test fail and forces a visible
    // decision (unify it, or add it here with its own justification and its own #566-style
    // follow-up). Every pair below was individually verified by reading both bodies side by
    // side (see issue #566 for the full comparison). This is also the documented limit of
    // this rule: it does not chase every possible duplicate inside large multi-concern
    // compiler-frontend types on its own (a name+arity shape match is not a guarantee that
    // every one of the N shared signatures is a true duplicate -- see the
    // GameBoyMusicAssetCompiler/NesMusicAssetCompiler note below).
    private static readonly (Type GameBoy, Type Nes)[] KnownUnresolvedLogicDuplicatePairs =
    [
        // #566: 2 of the 4 shared signatures are true duplication debt: GameBoyRomCompiler/
        // NesRomCompiler's HardwareSpriteScanlineCounts and WorstCaseDynamicScanlineCounts are
        // byte-for-byte identical hardware-sprite-per-scanline budget math (both take
        // spriteHeight/screenHeight as parameters -- there is nothing Game-Boy- or
        // NES-specific left in the method bodies). The other 2, ActorMetaspriteGeometry and
        // DrawSpriteBudget, are NOT pure duplication and must not be naively unified: NES
        // filters `asset.Pieces.Where(piece => !piece.Optional)` before computing sprite
        // count/geometry/scanline budget, because the NES sprite pipeline has an "optional
        // piece" concept that Game Boy's does not, so the two bodies compute over a
        // genuinely different piece set. Pending unification of the first 2 into a Core.Sdk
        // owner; remove this entry once that lands (the pair would then need re-adding under
        // a name covering only the 2 real duplicates, if ActorMetaspriteGeometry/
        // DrawSpriteBudget still coincidentally share a name/arity shape at that point).
        (typeof(GameBoyRomCompiler), typeof(NesRomCompiler)),

        // #566: GameBoyVideoProgram/NesVideoProgram collect FunctionCall arguments for the
        // whole per-target program. The real, reflection-verified shared shape (12
        // signatures, the largest pair this rule finds) is: ApplyBackgroundTiles,
        // BuildEnumIndex, BuildFunctionIndex, BuildStructIndex, CheckedRange, FillTiles,
        // HudModeArg, IdentifierArg, RequireArity, ResolveAssetPath, SetTile, StringArg.
        // (ConstArg is NOT in this shape -- it delegates to RetroSharp.Parser's
        // IntegerLiteral.TryParse, so it is correctly excluded as shared-owner code, not
        // duplication.) Most of these -- BuildEnumIndex/BuildFunctionIndex/BuildStructIndex/
        // RequireArity/HudModeArg/IdentifierArg/StringArg/CheckedRange/ResolveAssetPath --
        // are identical parsing/validation helpers over RetroSharp.Parser syntax nodes, which
        // is target-neutral by definition; there is no architectural reason this logic needs
        // two owners. SetTile and ApplyBackgroundTiles are the exception and are NOT
        // naively unifiable: Game Boy indexes a flat 32x32 tilemap
        // (`TileMap[y * 32 + x]`), NES indexes a 64x30 nametable through
        // NameTableTileOffset's own bounds/mirroring rules (`NameTable[NameTableTileOffset(x,
        // y)]`) -- the same "legitimately similar in shape, genuinely different in body" trap
        // as WriteSolidTile/WriteCheckerTile/WriteFrameTile (see PureTypeShapes), just hiding
        // inside an otherwise-real debt pair instead of causing a raw test failure. Pending
        // unification of the other 10 signatures into a Core.Sdk owner; remove this entry
        // once that lands (SetTile/ApplyBackgroundTiles would need to stay target-owned, or
        // be re-examined separately if a shared addressing abstraction is ever designed).
        (typeof(GameBoyVideoProgram), typeof(NesVideoProgram)),

        // #566: GameBoySpriteAssetCompiler/NesSpriteAssetCompiler duplicate sprite atlas
        // packing and validation (PadFramesToHardwareCells, ReadJsonFrames, RoundUp,
        // ValidateFrames, WriteTile). Pending unification into a Core.Sdk owner; remove this
        // entry once that lands.
        (typeof(GameBoySpriteAssetCompiler), typeof(NesSpriteAssetCompiler)),

        // #566: GameBoyMusicAssetCompiler/NesMusicAssetCompiler duplicate RequiredString (a
        // generic JSON-property extraction helper, byte-for-byte identical) and
        // CheckedFrameDelta (an inter-frame-delay bounds check identical except for the
        // exception message text) -- both pending unification into a Core.Sdk owner. The
        // third shared signature, BuildApuTraceGroupBody, is a name/arity-only coincidence
        // with genuinely different bodies (ApuTraceFrameGroup vs VgmFrame parameters) and is
        // NOT itself duplication debt; it only rides along because this rule compares whole
        // type pairs, not individual signatures. Remove this entry once RequiredString and
        // CheckedFrameDelta are unified (BuildApuTraceGroupBody staying different is fine and
        // expected).
        (typeof(GameBoyMusicAssetCompiler), typeof(NesMusicAssetCompiler)),
    ];

    [Fact]
    public void Game_boy_and_nes_do_not_declare_sibling_enums_with_the_same_member_shape()
    {
        var gameBoyAssembly = typeof(GameBoyRomBuilder).Assembly;
        var nesAssembly = typeof(NesRomBuilder).Assembly;

        var gameBoyEnums = TopLevelTypes(gameBoyAssembly).Where(type => type.IsEnum).ToArray();
        var nesEnums = TopLevelTypes(nesAssembly).Where(type => type.IsEnum).ToArray();

        var violations =
            (from gameBoyEnum in gameBoyEnums
             from nesEnum in nesEnums
             where EnumShapesMatch(gameBoyEnum, nesEnum)
             select $"'{gameBoyEnum.FullName}' and '{nesEnum.FullName}' declare the exact same enum member " +
                    $"shape ({EnumShapeDescription(gameBoyEnum)}). A duplicated result/state vocabulary like " +
                    "this belongs in one RetroSharp.Core.Sdk enum that both targets reference directly " +
                    "(see WorldPackRuntimeResult and PackedCameraStateProtocol for the established pattern).")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Game_boy_and_nes_do_not_declare_sibling_types_with_duplicated_non_delegating_logic()
    {
        var gameBoyAssembly = typeof(GameBoyRomBuilder).Assembly;
        var nesAssembly = typeof(NesRomBuilder).Assembly;
        var sharedAssemblyNames = SharedPortableAssemblyNames(gameBoyAssembly, nesAssembly);

        using var fragments = new PdbFragmentResolver();
        var gameBoyCandidates = PureTypeShapes(gameBoyAssembly, sharedAssemblyNames, fragments);
        var nesCandidates = PureTypeShapes(nesAssembly, sharedAssemblyNames, fragments);
        var knownPairs = KnownUnresolvedLogicDuplicatePairs.ToHashSet();

        var duplicatePairs =
            (from gameBoy in gameBoyCandidates
             from nes in nesCandidates
             let shared = gameBoy.Shape.Intersect(nes.Shape, StringComparer.Ordinal).ToArray()
             where shared.Length >= MinimumSharedMemberCount
             select (GameBoy: gameBoy.Type, Nes: nes.Type, Shared: shared))
            .ToArray();

        var unexpectedDuplicates = duplicatePairs
            .Where(pair => !knownPairs.Contains((pair.GameBoy, pair.Nes)))
            .Select(pair => $"'{pair.GameBoy.FullName}' and '{pair.Nes.FullName}' share {pair.Shared.Length} " +
                            $"non-delegating, non-emission method signatures ({string.Join(", ", pair.Shared)}) " +
                            "that neither type forwards to shared RetroSharp.Core/RetroSharp.Sdk.Frontend code. " +
                            "Move the shared logic to a new RetroSharp.Core.Sdk type (see SdkStreamReader<,> " +
                            "for the established pattern) and have both targets call it.")
            .ToArray();

        Assert.Empty(unexpectedDuplicates);

        // The known pairs above must still be exactly what this rule detects today: if one
        // gets fixed, this list must shrink (proving the fix), and nothing else may join it
        // silently.
        var observedKnownPairs = duplicatePairs
            .Where(pair => knownPairs.Contains((pair.GameBoy, pair.Nes)))
            .Select(pair => (pair.GameBoy, pair.Nes))
            .OrderBy(pair => pair.GameBoy.FullName, StringComparer.Ordinal)
            .ToArray();
        var expectedKnownPairs = KnownUnresolvedLogicDuplicatePairs
            .Select(pair => (pair.GameBoy, pair.Nes))
            .OrderBy(pair => pair.GameBoy.FullName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedKnownPairs, observedKnownPairs);
    }

    // Explicit, named regression pin for the "no false positives on legitimately similar
    // emission code" requirement: the Game Boy and NES collision lowerers
    // (GameBoySdkOperationLowerer.Collision.cs / NesSdkOperationLowerer.Collision.cs) and the
    // streaming-runtime/camera-streaming lowerers
    // (GameBoySdkOperationLowerer.StreamingRuntime.cs, .CameraStreaming.cs /
    // NesSdkOperationLowerer.StreamingRuntime.cs, .CameraStreaming.cs) are partial pieces of
    // GameBoySdkOperationLowerer/NesSdkOperationLowerer. Both owning types hold a
    // GbBuilder/PrgBuilder field, so this test asserts they are excluded from candidacy at
    // the type level -- this rule cannot fire on them no matter how similarly the two ISAs'
    // emission code is shaped.
    [Fact]
    public void Collision_and_streaming_lowerers_are_excluded_from_duplication_candidacy()
    {
        Assert.True(TouchesBuilderOrLayoutType(typeof(GameBoySdkOperationLowerer)));
        Assert.True(TouchesBuilderOrLayoutType(typeof(NesSdkOperationLowerer)));
    }

    private static bool EnumShapesMatch(Type gameBoyEnum, Type nesEnum)
    {
        if (Enum.GetUnderlyingType(gameBoyEnum) != Enum.GetUnderlyingType(nesEnum))
        {
            return false;
        }

        var gameBoyMembers = EnumMembers(gameBoyEnum);
        var nesMembers = EnumMembers(nesEnum);
        return gameBoyMembers.Count >= MinimumSharedMemberCount && gameBoyMembers.SequenceEqual(nesMembers, StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> EnumMembers(Type enumType) =>
        Enum.GetNames(enumType)
            .Zip(Enum.GetValues(enumType).Cast<object>(), (name, value) => $"{name}={Convert.ToInt64(value)}")
            .ToArray();

    private static string EnumShapeDescription(Type enumType) => string.Join(", ", EnumMembers(enumType));

    // The set of RetroSharp assemblies reachable from *both* target assemblies today
    // (RetroSharp.Core, RetroSharp.Parser, RetroSharp.Sdk.Frontend). Computed from each
    // assembly's own reference list rather than hardcoded, so a future shared project is
    // picked up automatically without editing this file.
    private static HashSet<string> SharedPortableAssemblyNames(Assembly gameBoyAssembly, Assembly nesAssembly)
    {
        var gameBoyReferences = gameBoyAssembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("RetroSharp.", StringComparison.Ordinal))
            .ToHashSet();
        var nesReferences = nesAssembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("RetroSharp.", StringComparison.Ordinal))
            .ToHashSet();
        gameBoyReferences.IntersectWith(nesReferences);
        return gameBoyReferences;
    }

    private static IReadOnlyList<(Type Type, IReadOnlyList<string> Shape)> PureTypeShapes(
        Assembly assembly, HashSet<string> sharedAssemblyNames, PdbFragmentResolver fragments)
    {
        // There is deliberately no whole-type "touches a builder/layout type -> exclude
        // everything" gate here any more. GameBoySdkOperationLowerer/NesSdkOperationLowerer
        // are split into per-feature partial files (.Collision.cs, .CameraStreaming.cs, ...)
        // -- this repo's established convention for extending them -- and a type-wide gate
        // made every single one of those files permanently invisible just because the *base*
        // file's constructor holds a GbBuilder/PrgBuilder field, even for a brand-new partial
        // fragment added later that never touches emission at all. LocalNonDelegatingMethodShape
        // now resolves the emission/layout touch per source-file fragment (via fragments,
        // backed by each assembly's own portable PDB), so a fragment that never touches
        // emission is a candidate even when a sibling fragment of the very same type does.
        // See LocalNonDelegatingMethodShape for why this is still safe against the
        // WriteSolidTile false positive (GameBoyRomBuilder/NesRomBuilder are single-file, so
        // per-fragment and per-type collapse to the same answer for them).
        return TopLevelTypes(assembly)
            .Where(type => !type.IsEnum)
            .Select(type => (Type: type, Shape: LocalNonDelegatingMethodShape(type, sharedAssemblyNames, fragments)))
            .Where(candidate => candidate.Shape.Count > 0)
            .ToArray();
    }

    // Deliberately includes nested types (a class/record/enum declared inside another type),
    // not just top-level ones -- `Assembly.GetTypes()` already returns every nested type
    // flatly (including a doubly-nested one such as
    // NesProgramPhaseAnalyzer+AnalysisContext+VisitState), so no separate recursive walk is
    // needed; only `DeclaringType is null` needs to stop being a filter. Declaring pure
    // shared logic inside a small nested helper/DTO type (see GameBoySpriteAssetCompiler+
    // SpriteAssetDocument/NesSpriteAssetCompiler+SpriteAssetDocument, an existing sibling pair
    // that used to be invisible here) is exactly as idiomatic in this codebase as declaring
    // it at the top level, so a filter that only ever looked at DeclaringType is null was an
    // accidental blind spot, not an intentional one.
    private static Type[] TopLevelTypes(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => type.Namespace is not null &&
                           type.Namespace.StartsWith("RetroSharp.", StringComparison.Ordinal))
            .Where(type => type.GetCustomAttributesData().All(attribute => attribute.AttributeType.Name != "CompilerGeneratedAttribute"))
            .ToArray();
    }

    // The issue's own suggested heuristic: a type/method that references its target's
    // assembler builder (GbBuilder/PrgBuilder) or a memory-layout type is emission or
    // hardware placement, never a duplication candidate. Matched by exact type identity for
    // the two known builders, and by a "Layout" name fragment for every current memory/ROM
    // layout type (GameBoyRomLayout, GameBoyRuntimeMemoryLayout, NesRuntimeMemoryLayout,
    // NesCartridgeLayout, and their nested layout records). Loosening this check can only
    // ever *shrink* the analyzed surface (more false exclusions, never a bypass), since it is
    // the trigger for excluding a type from candidacy, not for including one.
    private static bool IsAssemblerOrLayoutType(Type type) =>
        type == typeof(GbBuilder) ||
        type == typeof(PrgBuilder) ||
        type.Name.Contains("Layout", StringComparison.Ordinal);

    private static bool TouchesBuilderOrLayoutType(Type type)
    {
        if (type.GetFields(DeclaredMembers).Any(field => IsAssemblerOrLayoutType(Unwrap(field.FieldType))))
        {
            return true;
        }

        if (type.GetMethods(DeclaredMembers).Any(method =>
                IsAssemblerOrLayoutType(Unwrap(method.ReturnType)) ||
                method.GetParameters().Any(parameter => IsAssemblerOrLayoutType(Unwrap(parameter.ParameterType)))))
        {
            return true;
        }

        if (ArchitectureSymbolAssertions.CalledMethods(type).Any(method => method.DeclaringType is not null && IsAssemblerOrLayoutType(method.DeclaringType)))
        {
            return true;
        }

        if (ArchitectureSymbolAssertions.ReferencedFields(type).Any(field => IsAssemblerOrLayoutType(Unwrap(field.FieldType))))
        {
            return true;
        }

        return false;
    }

    private static Type Unwrap(Type type) => type.IsByRef || type.IsPointer || type.IsArray ? type.GetElementType()! : type;

    // Maps a method/constructor to the physical source file that declares it, using each
    // assembly's own portable PDB (already produced by a normal `dotnet build`; reading it
    // needs only System.Reflection.Metadata, which ships in the shared framework, so no new
    // package reference is required). This is what makes a per-source-file-fragment
    // emission/layout-touch check possible instead of a per-whole-type one: a PDB sequence
    // point is the same ground truth the debugger itself uses, so -- unlike a name- or
    // suffix-based guess at which file "owns" a partial method -- it cannot be gamed by
    // renaming or reshaping a member. A member whose file cannot be resolved (no sequence
    // points, e.g. a fully compiler-optimized trivial member) is treated by every caller as
    // unresolved, never as "clean", so an unresolvable fragment can only narrow candidacy.
    private sealed class PdbFragmentResolver : IDisposable
    {
        private readonly Dictionary<Assembly, MetadataReaderProvider?> providers = new();
        private readonly Dictionary<Assembly, MetadataReader?> readers = new();

        public string? TryGetDeclaringFile(MethodBase member)
        {
            var reader = ReaderFor(member.Module.Assembly);
            if (reader is null)
            {
                return null;
            }

            try
            {
                var handle = MetadataTokens.MethodDefinitionHandle(member.MetadataToken & 0xFFFFFF);
                var debugInfo = reader.GetMethodDebugInformation(handle);
                var sequencePoint = debugInfo.GetSequencePoints().FirstOrDefault(point => !point.IsHidden);
                return sequencePoint.Document.IsNil ? null : reader.GetString(reader.GetDocument(sequencePoint.Document).Name);
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        private MetadataReader? ReaderFor(Assembly assembly)
        {
            if (readers.TryGetValue(assembly, out var cached))
            {
                return cached;
            }

            MetadataReader? reader = null;
            var pdbPath = Path.ChangeExtension(assembly.Location, ".pdb");
            if (File.Exists(pdbPath))
            {
                var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
                providers[assembly] = provider;
                reader = provider.GetMetadataReader();
            }

            readers[assembly] = reader;
            return reader;
        }

        public void Dispose()
        {
            foreach (var provider in providers.Values)
            {
                provider?.Dispose();
            }
        }
    }

    // Groups every one of the type's own executable members (methods, constructors, and the
    // static constructor -- the same member set ArchitectureSymbolAssertions.CalledMethods/
    // ReferencedFields(Type) inspect at the type level) by the source file each is physically
    // declared in, then returns the subset of `candidates` whose *file* contains at least one
    // member that touches a builder/layout type by signature, call, or field reference. A
    // private GbBuilder/PrgBuilder field is normally only ever touched directly through the
    // constructor (`this.builder = builder;`), so it is the constructor's own file -- found
    // structurally via the PDB, not guessed from a name -- that marks its file as touching;
    // every *other* fragment file of the same partial type is judged independently and stays
    // a candidate if nothing in it touches emission. A member whose file cannot be resolved
    // is excluded outright rather than assumed clean.
    private static HashSet<MethodBase> MethodsInTouchingFragments(Type type, IReadOnlyList<MethodInfo> candidates, PdbFragmentResolver fragments)
    {
        var executableMembers = type.GetMethods(DeclaredMembers)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(DeclaredMembers))
            .Concat(type.TypeInitializer is { } typeInitializer ? [typeInitializer] : [])
            .Where(member => member.GetMethodBody() is not null)
            .DistinctBy(member => member.MetadataToken)
            .ToArray();

        var candidateSet = new HashSet<MethodBase>(candidates);
        var membersByFile = new Dictionary<string, List<MethodBase>>(StringComparer.Ordinal);
        var touchingFiles = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new HashSet<MethodBase>();

        foreach (var member in executableMembers)
        {
            var file = fragments.TryGetDeclaringFile(member);
            if (file is null)
            {
                if (candidateSet.Contains(member))
                {
                    excluded.Add(member);
                }

                continue;
            }

            if (!membersByFile.TryGetValue(file, out var members))
            {
                members = [];
                membersByFile[file] = members;
            }

            members.Add(member);

            var touchesDirectly =
                (member is MethodInfo method &&
                 (IsAssemblerOrLayoutType(Unwrap(method.ReturnType)) ||
                  method.GetParameters().Any(parameter => IsAssemblerOrLayoutType(Unwrap(parameter.ParameterType))))) ||
                ArchitectureSymbolAssertions.CalledMethods(member).Any(called => called.DeclaringType is not null && IsAssemblerOrLayoutType(called.DeclaringType)) ||
                ArchitectureSymbolAssertions.ReferencedFields(member).Any(field => IsAssemblerOrLayoutType(Unwrap(field.FieldType)));

            if (touchesDirectly)
            {
                touchingFiles.Add(file);
            }
        }

        foreach (var file in touchingFiles)
        {
            foreach (var member in membersByFile[file])
            {
                if (candidateSet.Contains(member))
                {
                    excluded.Add(member);
                }
            }
        }

        return excluded;
    }

    // A method counts toward a type's duplication shape only if it has its own IL body, is
    // not a special-name/compiler-synthesized member (property accessors, record
    // Equals/GetHashCode/ToString/PrintMembers/Deconstruct, which would otherwise coincide
    // for any two unrelated records), never touches a builder/layout type by signature or by
    // call (emission), and -- the key distinction from an already-fixed thin wrapper -- does
    // not forward its real work to a method declared in a portable assembly reachable from
    // both targets (RetroSharp.Core/RetroSharp.Parser/RetroSharp.Sdk.Frontend). Forwarding is
    // resolved to a fixed point over the type's own private helpers (so a method that only
    // calls a same-type helper that itself forwards to shared code is *also* treated as
    // delegating), and a call only counts as "forwarding work" if it invokes a real method,
    // not a property/indexer getter (reading `call.Arguments.Count` is data access, not
    // logic). This is exactly what the already-fixed thin wrappers look like today: after
    // #549, Sdk2DStreamReader/SdkAudioStreamReader/NesSdkStreamReader each only call methods
    // on a Core-owned SdkStreamReader<TItem, TOperation> field, so every one of their methods
    // resolves to delegating and none of them contribute to this type's shape.
    //
    // Emission/layout-touch exclusion starts from MethodsInTouchingFragments (per-file, see
    // above) and is then extended to a fixed point *within* the type: a private helper that
    // itself sits in a touching fragment makes every same-type caller of that helper
    // emission-adjacent too, even if the caller's own fragment, signature, and direct calls
    // look innocent. This only ever narrows a fragment that was otherwise clean -- it cannot
    // un-exclude anything MethodsInTouchingFragments already flagged.
    //
    // Why per-fragment instead of per-type: GameBoySdkOperationLowerer/NesSdkOperationLowerer
    // are always split into per-feature partial files, which is this repo's established
    // convention for extending them -- meaning the natural way to add new logic to either
    // lowerer is a brand-new partial file, and a whole-type gate made every single one of
    // those files permanently invisible to this rule just because the *base* file's
    // constructor holds a GbBuilder/PrgBuilder field. Per-fragment analysis closes that blind
    // spot: it was measured against every fragment of both lowerer types (11 Game Boy files,
    // 8 NES files) and every one of them still touches emission on its own merits (each
    // fragment either has a signature/call/field touch directly, or -- for the handful that
    // don't, such as GameBoySdkOperationLowerer.AnimationCameraQueries.cs, which only reads a
    // GameBoyRuntimeMemoryLayout constant on one line -- still trips a real touch), so no new
    // candidates were let through by this change for either lowerer.
    //
    // Known, accepted gap: this rule still cannot see a *pure* method sitting in the SAME
    // FRAGMENT FILE as an emission-touching one -- fragment granularity is coarser than
    // per-method, deliberately: dropping file grouping entirely (in favour of a purely
    // per-method check with no grouping at all) was tried and reverted, because it let
    // GameBoyRomBuilder/NesRomBuilder's private WriteSolidTile/WriteCheckerTile/WriteFrameTile
    // helpers through as a false positive. Those methods call neither GbBuilder/PrgBuilder
    // nor a layout-typed field directly -- they just write raw 2bpp tile bytes into a `byte[]`
    // parameter -- so a purely per-method signal cannot tell them apart from real duplication;
    // only grouping by fragment (GameBoyRomBuilder/NesRomBuilder are each a single file, so
    // per-fragment and per-type collapse to the same, correct answer for them) keeps them
    // excluded. So the accepted trade moved down one level, from "a pure method next to an
    // emission-touching one anywhere in the type" (the old, coarser gap; e.g. pre-#550
    // GameBoySymbolFileProjection.Serialize calling GameBoyRuntimeMemoryLayout.Validate() used
    // to hide its fully pure sibling SerializeSymbols even though they were declared right
    // next to each other in the same file) to "a pure method next to an emission-touching one
    // in the very same *file*" -- still a real, named limit, just a narrower one.
    //
    // Two smaller gaps in this same family were also identified and are deliberately left
    // unfixed because closing them is out of proportion to the risk they pose:
    //   - Renaming one target's method (e.g. ComputeChecksum -> CalculateChecksum) defeats
    //     name/arity matching even though the duplicated logic is untouched -- two
    //     implementers independently naming the same concept differently is a real,
    //     unremarkable occurrence, and reliably closing it would need body-similarity
    //     heuristics (diffing IL or syntax trees), which is out of scope for a name/arity
    //     shape rule.
    //   - An optional parameter added to only one target's copy changes that method's arity
    //     and drops it out of the shared shape, and a cosmetic, unused GbBuilder/PrgBuilder?
    //     field added to a type only to make TouchesBuilderOrLayoutType/MethodsInTouchingFragments
    //     trip would also evade this rule. Both are self-limiting rather than silent: an
    //     unused field is flagged by the compiler's own unused-field warning, and a
    //     gratuitous optional parameter is a visible, reviewable diff on the method itself --
    //     unlike a member-name-suffix bypass, neither can be done invisibly, so this rule
    //     leaves them as documented, accepted limits rather than adding heuristics to chase
    //     them.
    private static IReadOnlyList<string> LocalNonDelegatingMethodShape(Type type, HashSet<string> sharedAssemblyNames, PdbFragmentResolver fragments)
    {
        var candidates = type.GetMethods(DeclaredMembers)
            .Where(method => !method.IsSpecialName &&
                              !method.Name.StartsWith('<') &&
                              !CompilerSynthesizedMemberNames.Contains(method.Name) &&
                              method.GetMethodBody() is not null &&
                              !IsAssemblerOrLayoutType(Unwrap(method.ReturnType)) &&
                              !method.GetParameters().Any(parameter => IsAssemblerOrLayoutType(Unwrap(parameter.ParameterType))))
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        var calledMethodsByMethod = candidates.ToDictionary(
            method => (MethodBase)method,
            method => ArchitectureSymbolAssertions.CalledMethods(method).ToArray());

        var excludedByEmission = new HashSet<MethodBase>(MethodsInTouchingFragments(type, candidates, fragments));
        bool emissionChanged;
        do
        {
            emissionChanged = false;
            foreach (var method in candidates)
            {
                if (excludedByEmission.Contains(method))
                {
                    continue;
                }

                var touchesEmissionThroughOwnHelper = calledMethodsByMethod[method].Any(called =>
                    called.DeclaringType == type &&
                    candidates.Any(candidate => candidate == called && excludedByEmission.Contains(candidate)));

                if (touchesEmissionThroughOwnHelper)
                {
                    excludedByEmission.Add(method);
                    emissionChanged = true;
                }
            }
        }
        while (emissionChanged);

        var delegating = new HashSet<MethodBase>();
        bool changed;
        do
        {
            changed = false;
            foreach (var method in candidates)
            {
                if (excludedByEmission.Contains(method) || delegating.Contains(method))
                {
                    continue;
                }

                var calledMethods = calledMethodsByMethod[method];
                var delegatesDirectly = calledMethods.Any(called =>
                    called.DeclaringType is not null &&
                    !called.IsSpecialName &&
                    sharedAssemblyNames.Contains(called.DeclaringType.Assembly.GetName().Name!));
                var delegatesThroughOwnHelper = calledMethods.Any(called =>
                    called.DeclaringType == type &&
                    !called.IsSpecialName &&
                    candidates.Any(candidate => candidate == called && delegating.Contains(candidate)));

                if (delegatesDirectly || delegatesThroughOwnHelper)
                {
                    delegating.Add(method);
                    changed = true;
                }
            }
        }
        while (changed);

        return candidates
            .Where(method => !excludedByEmission.Contains(method) && !delegating.Contains(method))
            .Select(method => $"{method.Name}/{method.GetParameters().Length}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();
    }
}
